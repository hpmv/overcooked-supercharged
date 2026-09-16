using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SuperchargedPatch.AlteredComponents;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // Frozen-X compatibility: the shell satisfies its concrete type contract;
    // the source bound before InitialiseRound is always the actual native object.
    public sealed class ScriptedRoundBinding
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private static readonly Type WrapperType=typeof(WarpableRoundData), InstanceType=typeof(WarpableRoundInstanceData);
        private static readonly FieldInfo OriginalField=Field(WrapperType,"original"), OwnerField=Field(InstanceType,"Owner"),
            NativeField=Field(InstanceType,"NativeData"), RandomField=Field(InstanceType,"RandomState"), IndexField=Field(InstanceType,"Index"),
            HistoryField=Field(InstanceType,"History"), CheckpointsField=Field(InstanceType,"Checkpoints");
        private static readonly MethodInfo Validate=Method("ValidateCurrent"), Capture=Method("CaptureNative"), Restore=Method("RestoreNative"), Same=Method("SameCheckpoint");
        public readonly ScriptedRoundData Source;
        public readonly WarpableRoundData Wrapper;
        private readonly RecipeList recipeList;
        private readonly RecipeList.Entry[] weightedArray,manualArray;
        private readonly Entry[] weighted,manual;
        private readonly int[] diagnosticIndices;
        private readonly float duration;
        public long ManualInvocations { get; private set; }
        private sealed class Entry
        {
            internal RecipeList.Entry Native;
            internal OrderDefinitionNode Order;
            internal int Id,Score;
            internal string Name;
            internal float Weight;
        }
        public ScriptedRoundBinding(ScriptedRoundData source)
        {
            if(source==null||source.GetType()!=typeof(ScriptedRoundData))throw new ArgumentException("Exactly native ScriptedRoundData is supported.");
            Source=source; recipeList=source.m_recipes; duration=source.m_roundTimer;
            if(recipeList==null||recipeList.m_recipes==null||recipeList.m_recipes.Length==0||source.m_manualOrder==null||source.m_manualOrder.Length!=6||duration!=150f)
                throw new InvalidOperationException("Expected Story11 six-entry scripted prefix and150-second native round.");
            weightedArray=recipeList.m_recipes; manualArray=source.m_manualOrder;
            weighted=CaptureEntries(weightedArray);manual=CaptureEntries(manualArray);diagnosticIndices=new int[manual.Length];
            for(int i=0;i<manual.Length;i++)
            {
                int found=-1;
                for(int j=0;j<weighted.Length;j++)if(ReferenceEquals(manual[i].Order,weighted[j].Order)&&manual[i].Score==weighted[j].Score)
                {if(found!=-1)throw new InvalidOperationException("Manual entry has ambiguous weighted diagnostic identity.");found=j;}
                if(found<0)throw new InvalidOperationException("Manual entry cannot be represented faithfully in frozen-X diagnostics.");
                diagnosticIndices[i]=found;
            }
            // The ordinary constructor only copies metadata and stores this
            // temporary source. It never initialises or draws from it.
            Wrapper=new WarpableRoundData(new RoundData{m_recipes=recipeList,m_roundTimer=duration});
            OriginalField.SetValue(Wrapper,source);
            RequireMetadata();
        }
        public void RequireMetadata()
        {
            if(!ReferenceEquals(OriginalField.GetValue(Wrapper),Source)||!ReferenceEquals(Wrapper.m_recipes,recipeList)||Wrapper.m_roundTimer!=duration||
                !ReferenceEquals(Source.m_recipes,recipeList)||!ReferenceEquals(recipeList.m_recipes,weightedArray)||!ReferenceEquals(Source.m_manualOrder,manualArray)||Source.m_roundTimer!=duration)
                throw new InvalidOperationException("Bound native scripted source or asset-array identity changed.");
            RequireEntries(weightedArray,weighted);RequireEntries(manualArray,manual);
        }
        // True means handled by this manual-prefix adapter; false delegates to
        // the unmodified frozen weighted method, which calls Source virtually.
        public bool TryManual(RoundInstanceDataBase data,out RecipeList.Entry[] entries)
        {
            entries=null;RequireMetadata();
            var instance=data as WarpableRoundInstanceData;
            if(instance==null||!ReferenceEquals(OwnerField.GetValue(instance),Wrapper))throw new ArgumentException("Wrong scripted wrapper instance owner.");
            object native=NativeField.GetValue(instance);
            var countField=Field(native.GetType(),"RecipeCount");var frequencyField=Field(native.GetType(),"CumulativeFrequencies");
            int beforeCount=(int)countField.GetValue(native);
            if(beforeCount>=manual.Length)return false;
            if(beforeCount<0)throw new InvalidOperationException("Negative native scripted cursor.");
            Call(Validate,Wrapper,new object[]{instance});
            object before=Call(Capture,Wrapper,new object[]{instance});
            int beforeIndex=(int)IndexField.GetValue(instance);
            if(beforeIndex!=beforeCount)throw new InvalidOperationException("Native script cursor differs from frozen wrapper draw index.");
            int[] frequencies=(int[])((int[])frequencyField.GetValue(native)).Clone();
            var stream=(UnityEngine.Random.State)RandomField.GetValue(instance);
            var ambient=UnityEngine.Random.state;
            var history=(IList)HistoryField.GetValue(instance);var checkpoints=(IList)CheckpointsField.GetValue(instance);
            int historyCount=history.Count,checkpointCount=checkpoints.Count;
            try
            {
                UnityEngine.Random.state=stream;
                entries=Source.GetNextRecipe((RoundInstanceDataBase)native);
                ManualInvocations++;
                if(entries==null||entries.Length!=1||!ReferenceEquals(entries[0],manual[beforeCount].Native)||
                    (int)countField.GetValue(native)!=beforeCount+1||!SameVector(frequencies,(int[])frequencyField.GetValue(native))||!UnityEngine.Random.state.Equals(stream))
                    throw new InvalidOperationException("Native scripted draw changed more than its exact manual cursor/entry.");
                RandomField.SetValue(instance,UnityEngine.Random.state);
                object after=Call(Capture,Wrapper,new object[]{instance});
                int diagnosticIndex=diagnosticIndices[beforeCount];
                if(beforeIndex<history.Count)
                {
                    if((int)history[beforeIndex]!=diagnosticIndex||!(bool)Call(Same,null,new[]{after,checkpoints[beforeIndex+1]}))
                        throw new InvalidOperationException("Repeated native scripted draw differs from the recorded checkpoint.");
                }
                else {history.Add(diagnosticIndex);checkpoints.Add(after);}
                IndexField.SetValue(instance,beforeIndex+1);
                return true;
            }
            catch
            {
                IndexField.SetValue(instance,beforeIndex);
                while(history.Count>historyCount)history.RemoveAt(history.Count-1);
                while(checkpoints.Count>checkpointCount)checkpoints.RemoveAt(checkpoints.Count-1);
                Call(Restore,Wrapper,new[]{(object)instance,before});
                throw;
            }
            finally {UnityEngine.Random.state=ambient;}
        }
        public object Diagnostics()
        {
            RequireMetadata();var rows=new List<object>();
            for(int i=0;i<manual.Length;i++)rows.Add(new Dictionary<string,object>{{"manualIndex",i},{"recipeId",manual[i].Id},{"recipe",manual[i].Name},
                {"baseValue",manual[i].Score},{"manualWeight",manual[i].Weight},{"weightedDiagnosticIndex",diagnosticIndices[i]},
                {"nativeManualEntryIsWeightedEntry",ReferenceEquals(manual[i].Native,weighted[diagnosticIndices[i]].Native)}});
            return new Dictionary<string,object>{{"originalType",Source.GetType().FullName},{"originalNativeModuleVersionId",typeof(ScriptedRoundData).Module.ModuleVersionId.ToString()},
                {"wrapperType",Wrapper.GetType().FullName},{"manual",rows.ToArray()},{"duration",duration},{"sameRecipeListAndArray",ReferenceEquals(Wrapper.m_recipes,Source.m_recipes)&&ReferenceEquals(weightedArray,Source.m_recipes.m_recipes)},
                {"manualInvocationsIncludingPreviews",ManualInvocations},{"manualSemantics","Native manual entry; RecipeCount+1; no RNG/frequency change"},
                {"weightedSemantics","Unmodified frozen wrapper calls original ScriptedRoundData.GetNextRecipe; native base method owns weighting"}};
        }
        private static Entry[] CaptureEntries(RecipeList.Entry[] entries)
        {
            var result=new Entry[entries.Length];
            for(int i=0;i<entries.Length;i++)
            {var e=entries[i];if(e==null||e.m_order==null||float.IsNaN(e.m_weight)||float.IsInfinity(e.m_weight))throw new InvalidOperationException("Native recipe entry is incomplete.");
             result[i]=new Entry{Native=e,Order=e.m_order,Id=e.m_order.m_uID,Name=e.m_order.name,Score=e.m_scoreForMeal,Weight=e.m_weight};}
            return result;
        }
        private static void RequireEntries(RecipeList.Entry[] entries,Entry[] expected)
        {
            if(entries.Length!=expected.Length)throw new InvalidOperationException("Native recipe array length changed.");
            for(int i=0;i<expected.Length;i++)
            {var a=expected[i];var b=entries[i];if(!ReferenceEquals(a.Native,b)||!ReferenceEquals(a.Order,b.m_order)||a.Id!=b.m_order.m_uID||a.Name!=b.m_order.name||a.Score!=b.m_scoreForMeal||a.Weight!=b.m_weight)
                throw new InvalidOperationException("Native recipe entry identity/content changed.");}
        }
        private static bool SameVector(int[] a,int[] b){if(a==null||b==null||a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
        private static FieldInfo Field(Type t,string n){var f=t.GetField(n,Flags);if(f==null)throw new MissingFieldException(t.FullName,n);return f;}
        private static MethodInfo Method(string n){var m=WrapperType.GetMethod(n,Flags);if(m==null)throw new MissingMethodException(WrapperType.FullName,n);return m;}
        private static object Call(MethodInfo m,object owner,object[] args){try{return m.Invoke(owner,args);}catch(TargetInvocationException e){throw e.InnerException??e;}}
    }
}
