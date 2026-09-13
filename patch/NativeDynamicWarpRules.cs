using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace SuperchargedPatch
{
    // Shared with the offline contract tests. These rules run before native mutation.
    public static class NativeDynamicWarpRules
    {
        public sealed class Target
        {
            public int? ExistingId;
            public int[] Path;
            public int[] SpawnPath;
            public bool InitialRootRecreation;
        }

        public static string PathKey(IEnumerable<int> path)
        {
            if (path == null) throw new InvalidOperationException("Missing authoring entity path.");
            int[] values = path.ToArray();
            if (values.Length < 1 || values.Length > 32 || values[0] <= 0 || values.Skip(1).Any(v => v < 0))
                throw new InvalidOperationException("Invalid or excessive authoring entity path.");
            return string.Join(".", values.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray());
        }

        public static void Validate(Target[] targets, int[] deletions, HashSet<int> currentIds)
        {
            if (targets == null || deletions == null || targets.Length > 1022 || deletions.Length > 1022)
                throw new InvalidOperationException("Missing or excessive authoring entity plan.");
            var removed = new HashSet<int>();
            foreach (int id in deletions)
                if (!currentIds.Contains(id) || !removed.Add(id))
                    throw new InvalidOperationException("Unknown or duplicate authoring deletion: " + id);
            var kept = new HashSet<int>();
            var paths = new HashSet<string>();
            int initialRootRecreations = 0;
            foreach (Target target in targets)
            {
                bool spawning = target.SpawnPath != null && target.SpawnPath.Length != 0;
                if(target.InitialRootRecreation)
                {
                    if(++initialRootRecreations>1||!spawning||target.ExistingId.HasValue
                        ||target.Path==null||target.Path.Length!=1)
                        throw new InvalidOperationException("Invalid or duplicate initial-root recreation authority.");
                }
                if (target.ExistingId.HasValue == spawning)
                    throw new InvalidOperationException("Each authoring entity needs exactly one native ID or spawn path.");
                if (target.ExistingId.HasValue)
                {
                    int id = target.ExistingId.Value;
                    if (!currentIds.Contains(id) || removed.Contains(id) || !kept.Add(id))
                        throw new InvalidOperationException("Unknown, deleted or duplicate retained authoring entity: " + id);
                }
                else
                {
                    PathKey(target.SpawnPath);
                    if (target.SpawnPath.Length < 2 || !currentIds.Contains(target.SpawnPath[0]))
                        throw new InvalidOperationException("Authoring spawn chain has no live native root.");
                    if (target.Path == null || (target.Path.Length < 2&&!target.InitialRootRecreation))
                        throw new InvalidOperationException("Recreated authoring entity has no dynamic logical path.");
                }
                if (target.Path != null && !paths.Add(PathKey(target.Path)))
                    throw new InvalidOperationException("Duplicate authoring logical path.");
            }
        }

        // Remove one transaction-owned row from an engine canonical list.
        // Every admission check precedes mutation and all comparisons use CLR
        // reference identity, including Unity wrappers that may already be
        // native-destroyed by the time this synchronous cleanup runs.
        public static void RetireExactCanonicalListValue(IList currentList,IList capturedList,
            object[] capturedPreimage,int capturedIndex,object capturedValue)
        {
            if(currentList==null||capturedList==null||capturedPreimage==null||capturedValue==null
                ||!ReferenceEquals(currentList,capturedList))
                throw new InvalidOperationException("Native canonical list retirement identity differs.");
            if(capturedIndex<0||capturedIndex>=capturedPreimage.Length
                ||!ReferenceEquals(capturedPreimage[capturedIndex],capturedValue)
                ||currentList.Count!=capturedPreimage.Length)
                throw new InvalidOperationException("Native canonical list retirement preimage differs.");
            int matches=0;
            for(int i=0;i<capturedPreimage.Length;i++)
            {
                if(!ReferenceEquals(currentList[i],capturedPreimage[i]))
                    throw new InvalidOperationException("Native canonical list retirement preimage differs at index "+i+".");
                if(ReferenceEquals(currentList[i],capturedValue))matches++;
            }
            if(matches!=1)
                throw new InvalidOperationException("Native canonical list retirement value is not unique.");
            currentList.RemoveAt(capturedIndex);
            if(currentList.Count!=capturedPreimage.Length-1)
                throw new InvalidOperationException("Native canonical list retirement cardinality differs after removal.");
            for(int source=0,destination=0;source<capturedPreimage.Length;source++)
            {
                if(source==capturedIndex)continue;
                if(!ReferenceEquals(currentList[destination],capturedPreimage[source]))
                    throw new InvalidOperationException("Native canonical list retirement order differs after removal.");
                destination++;
            }
        }

        // Remove a proved set of transaction-owned rows as one fail-closed
        // operation.  All identity, order, presence, and uniqueness checks run
        // before the first mutation; removal then proceeds from the highest
        // index so the relative order of every survivor is unchanged.
        public static void RetireExactCanonicalListValues(IList currentList,IList capturedList,
            object[] capturedPreimage,object[] capturedValues)
        {
            if(currentList==null||capturedList==null||capturedPreimage==null||capturedValues==null
                ||!ReferenceEquals(currentList,capturedList))
                throw new InvalidOperationException("Native canonical-list retirement-set identity differs.");
            if(capturedValues.Length==0||currentList.Count!=capturedPreimage.Length)
                throw new InvalidOperationException("Native canonical-list retirement-set preimage differs.");
            for(int i=0;i<capturedPreimage.Length;i++)
                if(!ReferenceEquals(currentList[i],capturedPreimage[i]))
                    throw new InvalidOperationException("Native canonical-list retirement-set preimage differs at index "+i+".");
            var indices=new int[capturedValues.Length];
            for(int valueIndex=0;valueIndex<capturedValues.Length;valueIndex++)
            {
                object value=capturedValues[valueIndex];
                if(value==null)throw new InvalidOperationException("Native canonical-list retirement-set contains a null value.");
                for(int prior=0;prior<valueIndex;prior++)
                    if(ReferenceEquals(capturedValues[prior],value))
                        throw new InvalidOperationException("Native canonical-list retirement-set contains a duplicate value.");
                int match=-1;
                for(int i=0;i<capturedPreimage.Length;i++)
                    if(ReferenceEquals(capturedPreimage[i],value))
                    {
                        if(match>=0)throw new InvalidOperationException("Native canonical-list retirement-set value is not unique.");
                        match=i;
                    }
                if(match<0)throw new InvalidOperationException("Native canonical-list retirement-set value is absent.");
                indices[valueIndex]=match;
            }
            Array.Sort(indices);
            for(int i=indices.Length-1;i>=0;i--)currentList.RemoveAt(indices[i]);
            var expected=capturedPreimage.Where(value=>!capturedValues.Any(retired=>ReferenceEquals(retired,value))).ToArray();
            if(currentList.Count!=expected.Length)
                throw new InvalidOperationException("Native canonical-list retirement-set cardinality differs after removal.");
            for(int i=0;i<expected.Length;i++)
                if(!ReferenceEquals(currentList[i],expected[i]))
                    throw new InvalidOperationException("Native canonical-list retirement-set order differs after removal.");
        }
    }

    // Native callbacks are supplied by NativeDynamicWarpPlan. Failure cleanup owns
    // only objects returned by this transaction, never pre-existing world objects.
    public sealed class NativeSpawnTransaction<T> where T : class
    {
        private readonly List<T> created = new List<T>();
        private readonly Action<T> destroy;
        public NativeSpawnTransaction(Action<T> destroy) { this.destroy = destroy; }
        public T Create(Func<T> spawn)
        {
            T value = spawn();
            if (value == null) throw new InvalidOperationException("Native authoring spawn returned no object.");
            created.Add(value);
            return value;
        }
        public void RemoveIntermediate(T value)
        {
            RemoveIntermediate(value,destroy);
        }
        public void RemoveIntermediate(T value,Action<T> exactDestroy)
        {
            if (!created.Contains(value)) throw new InvalidOperationException("Unowned authoring intermediate.");
            if(exactDestroy==null)throw new InvalidOperationException("Missing authoring intermediate cleanup.");
            exactDestroy(value);
            created.Remove(value);
        }
        public void Commit() { created.Clear(); }
        public void Cleanup()
        {
            var failures = new List<string>();
            for (int i = created.Count - 1; i >= 0; i--)
                try { destroy(created[i]); } catch (Exception error) { failures.Add(error.Message); }
            created.Clear();
            if (failures.Count != 0)
                throw new InvalidOperationException("Native authoring spawn cleanup failed: " + string.Join("; ", failures.ToArray()));
        }
    }
}
