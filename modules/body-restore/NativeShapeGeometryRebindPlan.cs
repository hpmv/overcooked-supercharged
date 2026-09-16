using System;

namespace SuperchargedPatch.Authoring.Modules
{
    // Pure reference/pointer proof used before carrying historical native
    // geometry across a Collider/PxShape reincarnation.  This class performs
    // no Unity or PhysX mutation and is kept separate for exhaustive offline
    // testing.
    internal static class NativeShapeGeometryRebindPlan
    {
        internal sealed class RecreatedPair
        {
            internal object Historical;
            internal object Current;
        }

        internal sealed class Result
        {
            internal bool[] RecreatedByActorIndex;
            internal int[] HistoricalManagedIndexByActorIndex;
            internal int[] CurrentManagedIndexByActorIndex;
        }

        internal static Result Build(object[] historicalColliders,UIntPtr[] historicalColliderShapes,
            UIntPtr[] historicalActorShapes,object[] currentColliders,UIntPtr[] currentColliderShapes,
            UIntPtr[] currentActorShapes,RecreatedPair[] recreatedPairs,int requiredRecreatedCount)
        {
            if(historicalColliders==null||historicalColliderShapes==null||historicalActorShapes==null
                ||currentColliders==null||currentColliderShapes==null||currentActorShapes==null
                ||recreatedPairs==null)
                throw new InvalidOperationException("Native shape geometry rebind arrays are null.");
            int count=historicalColliders.Length;
            if(count==0||count!=historicalColliderShapes.Length||count!=historicalActorShapes.Length
                ||count!=currentColliders.Length||count!=currentColliderShapes.Length
                ||count!=currentActorShapes.Length)
                throw new InvalidOperationException("Native shape geometry rebind arrays differ.");
            if(requiredRecreatedCount<1||recreatedPairs.Length!=requiredRecreatedCount)
                throw new InvalidOperationException("Native shape geometry rebind pair count differs.");
            RequireUniqueReferences(historicalColliders,"historical Collider");
            RequireUniqueReferences(currentColliders,"current Collider");
            RequireUniquePointers(historicalColliderShapes,"historical Collider shape");
            RequireUniquePointers(currentColliderShapes,"current Collider shape");
            RequireUniquePointers(historicalActorShapes,"historical actor shape");
            RequireUniquePointers(currentActorShapes,"current actor shape");
            RequireSamePointerSet(historicalColliderShapes,historicalActorShapes,"historical");
            RequireSamePointerSet(currentColliderShapes,currentActorShapes,"current");

            for(int i=0;i<recreatedPairs.Length;i++)
            {
                var pair=recreatedPairs[i];
                if(pair==null||pair.Historical==null||pair.Current==null
                    ||ReferenceEquals(pair.Historical,pair.Current)
                    ||ReferenceIndexOf(historicalColliders,pair.Historical)<0
                    ||ReferenceIndexOf(currentColliders,pair.Current)<0)
                    throw new InvalidOperationException("Native shape geometry recreated pair is invalid.");
                for(int j=0;j<i;j++)
                    if(ReferenceEquals(recreatedPairs[j].Historical,pair.Historical)
                        ||ReferenceEquals(recreatedPairs[j].Current,pair.Current))
                        throw new InvalidOperationException("Native shape geometry recreated pairs are not bijective.");
            }

            var mappedCurrent=new int[count];
            var recreatedByHistoricalManaged=new bool[count];
            var usedCurrent=new bool[count];
            for(int historicalManaged=0;historicalManaged<count;historicalManaged++)
            {
                object historical=historicalColliders[historicalManaged];
                object mapped=historical;
                int pairIndex=PairIndexOfHistorical(recreatedPairs,historical);
                if(pairIndex>=0){mapped=recreatedPairs[pairIndex].Current;recreatedByHistoricalManaged[historicalManaged]=true;}
                int currentManaged=ReferenceIndexOf(currentColliders,mapped);
                if(currentManaged<0||usedCurrent[currentManaged])
                    throw new InvalidOperationException("Native shape geometry role map is incomplete or ambiguous.");
                usedCurrent[currentManaged]=true;mappedCurrent[historicalManaged]=currentManaged;
            }
            for(int i=0;i<count;i++)if(!usedCurrent[i])
                throw new InvalidOperationException("Native shape geometry role map does not cover every current Collider.");

            var result=new Result {RecreatedByActorIndex=new bool[count],
                HistoricalManagedIndexByActorIndex=new int[count],CurrentManagedIndexByActorIndex=new int[count]};
            for(int actorIndex=0;actorIndex<count;actorIndex++)
            {
                int historicalManaged=PointerIndexOf(historicalColliderShapes,historicalActorShapes[actorIndex]);
                int currentManaged=PointerIndexOf(currentColliderShapes,currentActorShapes[actorIndex]);
                if(historicalManaged<0||currentManaged<0||mappedCurrent[historicalManaged]!=currentManaged)
                    throw new InvalidOperationException("Native shape geometry semantic actor order differs.");
                result.RecreatedByActorIndex[actorIndex]=recreatedByHistoricalManaged[historicalManaged];
                result.HistoricalManagedIndexByActorIndex[actorIndex]=historicalManaged;
                result.CurrentManagedIndexByActorIndex[actorIndex]=currentManaged;
            }
            return result;
        }

        private static int PairIndexOfHistorical(RecreatedPair[] pairs,object value)
        {
            int result=-1;
            for(int i=0;i<pairs.Length;i++)if(ReferenceEquals(pairs[i].Historical,value))
            {if(result>=0)throw new InvalidOperationException("Native shape geometry historical role is duplicated.");result=i;}
            return result;
        }

        private static int ReferenceIndexOf(object[] values,object wanted)
        {
            int result=-1;
            for(int i=0;i<values.Length;i++)if(ReferenceEquals(values[i],wanted))
            {if(result>=0)return -2;result=i;}
            return result;
        }

        private static int PointerIndexOf(UIntPtr[] values,UIntPtr wanted)
        {
            int result=-1;
            for(int i=0;i<values.Length;i++)if(values[i].Equals(wanted))
            {if(result>=0)return -2;result=i;}
            return result;
        }

        private static void RequireUniqueReferences(object[] values,string kind)
        {
            for(int i=0;i<values.Length;i++)
            {
                if(values[i]==null)throw new InvalidOperationException("Native shape geometry "+kind+" is null.");
                for(int j=0;j<i;j++)if(ReferenceEquals(values[i],values[j]))
                    throw new InvalidOperationException("Native shape geometry "+kind+" identity is duplicated.");
            }
        }

        private static void RequireUniquePointers(UIntPtr[] values,string kind)
        {
            for(int i=0;i<values.Length;i++)
            {
                if(values[i].Equals(UIntPtr.Zero))throw new InvalidOperationException("Native shape geometry "+kind+" is null.");
                for(int j=0;j<i;j++)if(values[i].Equals(values[j]))
                    throw new InvalidOperationException("Native shape geometry "+kind+" is duplicated.");
            }
        }

        private static void RequireSamePointerSet(UIntPtr[] bindings,UIntPtr[] actor,string phase)
        {
            for(int i=0;i<bindings.Length;i++)if(PointerIndexOf(actor,bindings[i])<0)
                throw new InvalidOperationException("Native shape geometry "+phase+" Collider/PxShape binding is not bijective.");
        }
    }
}
