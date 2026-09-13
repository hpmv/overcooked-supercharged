using System;
using System.Collections.Generic;
using System.Linq;

namespace SuperchargedPatch.Authoring.Modules
{
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
