using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace SuperchargedPatch.Authoring.Modules
{
    internal sealed class DeliveryFadeSpawnShape
    {
        internal IList<int> SpawningPath;
        internal IList<int> LogicalPath;
        internal bool HasIngredientContainer;
    }

    // StartCoroutine advances its IEnumerator synchronously.  This one-yield
    // relay lets authoring resume schedule an already-advanced native iterator
    // without consuming its restored frame before normal Unity scheduling.
    internal sealed class DeliveryFadeResumeRelay : IEnumerator
    {
        internal readonly IEnumerator Inner;
        internal bool Primed;
        private object current;

        internal DeliveryFadeResumeRelay(IEnumerator inner)
        {
            if(inner==null)throw new ArgumentNullException("inner");
            Inner=inner;
        }

        public object Current { get { return current; } }

        public bool MoveNext()
        {
            if(!Primed){Primed=true;current=null;return true;}
            bool result=Inner.MoveNext();current=result?Inner.Current:null;return result;
        }

        public void Reset() { throw new NotSupportedException(); }
    }

    // Pure admission helpers shared with the offline contract check.  Keeping
    // these rules free of Unity state makes the fail-closed edge cases
    // independently executable without loading the game.
    internal static class DeliveryFadeReincarnationContract
    {
        internal const string PathMarkerType = "SuperchargedPatch.EntityPathReferenceMarker";

        internal static bool IsExactStory11PlateFactory(IList<int> spawningPath,IList<int> logicalPath,int entityId)
        {
            return spawningPath!=null&&spawningPath.Count==3
                &&spawningPath[0]==34&&spawningPath[1]==0&&spawningPath[2]==0
                &&logicalPath!=null&&logicalPath.Count==1&&logicalPath[0]==entityId;
        }

        internal static int ExactStory11Entity2PlateSpawnIndex(IList<DeliveryFadeSpawnShape> spawns)
        {
            if(spawns==null)return -1;
            int exact=-1;
            for(int i=0;i<spawns.Count;i++)
            {
                var spawn=spawns[i];
                if(spawn==null)continue;
                bool plateFactory=spawn.SpawningPath!=null&&spawn.SpawningPath.Count==3
                    &&spawn.SpawningPath[0]==34&&spawn.SpawningPath[1]==0&&spawn.SpawningPath[2]==0;
                bool entity2=spawn.LogicalPath!=null&&spawn.LogicalPath.Count!=0&&spawn.LogicalPath[0]==2;
                if(!plateFactory&&!entity2)continue;
                bool candidate=IsExactStory11PlateFactory(spawn.SpawningPath,spawn.LogicalPath,2)
                    &&spawn.HasIngredientContainer;
                if(!candidate||exact>=0)return -1;
                exact=i;
            }
            return exact;
        }

        internal static bool IsDisjointFutureDeletionSet(IList<int> deletionIds,IList<int> targetPlateIds)
        {
            if(deletionIds==null||targetPlateIds==null)return false;
            var target=new HashSet<int>();
            foreach(int id in targetPlateIds)if(id<=0||!target.Add(id))return false;
            var deleted=new HashSet<int>();
            foreach(int id in deletionIds)
                if(id<=0||target.Contains(id)||!deleted.Add(id))return false;
            return true;
        }

        internal static bool IsExactRecreatedComponentTopology(IList<string> historical,IList<string> recreated)
        {
            if(historical==null||recreated==null||historical.Count(value=>value==PathMarkerType)>1
                ||recreated.Count(value=>value==PathMarkerType)!=1)return false;
            return historical.Where(value=>value!=PathMarkerType)
                .SequenceEqual(recreated.Where(value=>value!=PathMarkerType));
        }

        internal static bool IsExactAttachmentParentIncarnation(bool historicalReferencePresent,
            bool historicalAlive,int historicalId,bool recreatedAlive,int recreatedId,bool sameReference)
        {
            if(!historicalReferencePresent||historicalId==0||!recreatedAlive||recreatedId==0)return false;
            return historicalAlive
                ?sameReference&&recreatedId==historicalId
                :!sameReference&&recreatedId!=historicalId;
        }

        // Returns the current index for each target key.  Enumeration order is
        // deliberately irrelevant; missing, extra or duplicate keys reject.
        internal static int[] ExactUniqueKeyMap(IList<string> target,IList<string> current)
        {
            if(target==null||current==null||target.Count!=current.Count)return null;
            var indices=new Dictionary<string,int>(StringComparer.Ordinal);
            for(int i=0;i<current.Count;i++)
            {
                string key=current[i];
                if(key==null||indices.ContainsKey(key))return null;
                indices.Add(key,i);
            }
            var result=new int[target.Count];var seen=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<target.Count;i++)
            {
                string key=target[i];int index;
                if(key==null||!seen.Add(key)||!indices.TryGetValue(key,out index))return null;
                result[i]=index;
            }
            return result;
        }

        internal static bool IsTerminalFadeProgress(float value)
        {
            return !Single.IsNaN(value)&&!Single.IsInfinity(value)&&value>=1f&&value<=1.00001f;
        }

        internal static bool IsExactStory11ActiveFadeState(int entityId,int pc,bool disposing,bool errored,
            bool currentIsNull,bool currentIsStationWait,float progress,bool pfxAlive,bool pfxDetached,
            float pfxDelay,float fadeTime,IList<bool> colliderEnabled,IList<float> materialAlpha)
        {
            return entityId==2&&pc==2&&!disposing&&!errored&&currentIsNull&&!currentIsStationWait
                &&progress==.3f&&pfxAlive&&pfxDetached&&pfxDelay==0f&&fadeTime==.5f
                &&colliderEnabled!=null&&colliderEnabled.Count==2&&colliderEnabled.All(value=>!value)
                &&materialAlpha!=null&&materialAlpha.Count==2
                &&materialAlpha.All(value=>value==.733333349f);
        }

        internal static bool IsExactObserverEntry(int count,bool containsExpected,int observedInstanceId,int expectedInstanceId)
        {
            return count==1&&containsExpected&&observedInstanceId>0&&observedInstanceId==expectedInstanceId;
        }

        internal static bool IsNonEmptyDistinctSubset(IList<int> values,IList<int> ownerValues)
        {
            if(values==null||ownerValues==null||values.Count==0)return false;
            var valuesSet=new HashSet<int>();
            foreach(int value in values)if(value<=0||!valuesSet.Add(value))return false;
            var ownerSet=new HashSet<int>();
            foreach(int value in ownerValues)if(value<=0||!ownerSet.Add(value))return false;
            return valuesSet.IsSubsetOf(ownerSet);
        }

        internal static bool IsRebindableDeliveryPhase(int pc,bool currentIsNull,bool currentIsStationWait,
            float progress,bool hasColliders,bool hasRenderers,bool pfxAlive,bool pfxDetached,bool pfxReferenced)
        {
            if(!Finite(progress))return false;
            return pc==0&&currentIsNull&&!currentIsStationWait&&progress==0f
                    &&!hasColliders&&!hasRenderers&&!pfxAlive&&!pfxDetached
                ||pc==1&&!currentIsNull&&currentIsStationWait&&progress==0f
                    &&hasColliders&&!hasRenderers&&!pfxAlive&&!pfxDetached
                ||pc==2&&currentIsNull&&!currentIsStationWait&&progress>=0f&&progress<1f
                    &&hasColliders&&hasRenderers&&pfxAlive&&pfxDetached&&pfxReferenced;
        }

        internal static bool IsBoundedPresentationScale(float cx,float cy,float cz,float tx,float ty,float tz)
        {
            const float maximumResidual=0.000001f;
            return Finite(cx)&&Finite(cy)&&Finite(cz)&&Finite(tx)&&Finite(ty)&&Finite(tz)
                &&Math.Abs(cx-tx)<=maximumResidual&&Math.Abs(cy-ty)<=maximumResidual
                &&Math.Abs(cz-tz)<=maximumResidual;
        }

        internal static bool IsExactVirginStory11SushiPresentation(
            IList<string> targetKeys,IList<bool> targetPresentation,
            IList<string> currentKeys,IList<bool> currentPresentation,
            string presentationContainerKey,int sushiComponents,int assignableComponents,
            bool mealContainerNull,bool mealOrderDefinitionNull,bool mealRendererInfoNull,
            string targetComposition,string assignedComposition)
        {
            const string plate="Plate#1@0";
            const string sushi="AttachPoint#0/CompositeSushi(Clone)#0/IngredientContainer#0/m_recipe_sushi_02#0@0";
            const string container="AttachPoint#0/CompositeSushi(Clone)#0";
            if(targetKeys==null||targetPresentation==null||currentKeys==null||currentPresentation==null
                ||targetKeys.Count!=2||targetPresentation.Count!=2
                ||currentKeys.Count!=1||currentPresentation.Count!=1
                ||presentationContainerKey!=container||sushiComponents!=1||assignableComponents!=1
                ||!mealContainerNull||!mealOrderDefinitionNull||!mealRendererInfoNull
                ||targetComposition!="Composite(C=[Ingredient(23600)],O=[])"
                ||assignedComposition!=targetComposition||currentKeys[0]!=plate||currentPresentation[0])return false;
            bool sawPlate=false,sawSushi=false;
            for(int i=0;i<targetKeys.Count;i++)
            {
                if(targetKeys[i]==plate&&!targetPresentation[i]&&!sawPlate)sawPlate=true;
                else if(targetKeys[i]==sushi&&targetPresentation[i]&&!sawSushi)sawSushi=true;
                else return false;
            }
            return sawPlate&&sawSushi;
        }

        internal static bool HasManagedReference(object value)
        {
            // UnityEngine.Object overloads == so a retained wrapper whose native
            // object was destroyed compares equal to null.  Terminal rewind
            // proof needs the managed wrapper identity independently of liveness.
            return !ReferenceEquals(value,null);
        }

        private static bool Finite(float value)
        {
            return !Single.IsNaN(value)&&!Single.IsInfinity(value);
        }
    }
}
