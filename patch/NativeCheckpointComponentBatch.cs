using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SuperchargedPatch
{
    // Membership only, scoped to one synchronous CaptureFrame invocation. Every
    // sidecar still reads the native fields and serialises its payload afresh.
    public static class NativeCheckpointComponentBatch
    {
        private static MonoBehaviour[] current;
        private static int generation = -1;
        private static readonly Dictionary<Type, int> verified = new Dictionary<Type, int>();
        private static readonly HashSet<Type> refused = new HashSet<Type>();
        private static readonly Dictionary<Type, object> proofs = new Dictionary<Type, object>();
        private static long batches, discoveryTicks, hits, nativeFallbacks, comparisons;
        private static int lastCount;

        public static void Begin()
        {
            if (current != null) throw new InvalidOperationException("Nested native checkpoint component discovery.");
            if (generation != NativeSceneMetadata.Refreshes)
            {
                generation = NativeSceneMetadata.Refreshes;
                verified.Clear(); refused.Clear(); proofs.Clear();
            }
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            current = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
            discoveryTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
            lastCount = current.Length;
            batches++;
        }

        public static void End() { current = null; }

        public static T[] Find<T>() where T : MonoBehaviour
        {
            Type type = typeof(T);
            if (current == null || refused.Contains(type))
            {
                nativeFallbacks++;
                return UnityEngine.Object.FindObjectsOfType<T>();
            }
            // OfType preserves the native broad-query order. The first three
            // requests per type/load compare that exact sequence to the old
            // typed native query; any mismatch uses the old query for this load.
            T[] result = current.OfType<T>().ToArray();
            int count;
            verified.TryGetValue(type, out count);
            if (count < 3)
            {
                T[] native = UnityEngine.Object.FindObjectsOfType<T>();
                int[] broadIds = result.Select(c => c.GetInstanceID()).ToArray();
                int[] nativeIds = native.Select(c => c.GetInstanceID()).ToArray();
                bool same = broadIds.SequenceEqual(nativeIds);
                comparisons++;
                verified[type] = count + 1;
                proofs[type] = new Dictionary<string, object> {
                    { "checks", count + 1 }, { "sameMembershipAndOrder", same },
                    { "broadInstanceIds", broadIds }, { "typedInstanceIds", nativeIds }
                };
                if (!same)
                {
                    refused.Add(type); nativeFallbacks++;
                    return native;
                }
            }
            hits++;
            return result;
        }

        public static ServerKitchenFlowControllerBase FindFlow()
        {
            var flows = Find<ServerKitchenFlowControllerBase>();
            if (flows.Length <= 1) return flows.Length == 0 ? null : flows[0];
            // Preserve the original singular query if more than one flow is
            // present, even if current broad ordering already compares equal.
            nativeFallbacks++;
            return UnityEngine.Object.FindObjectOfType<ServerKitchenFlowControllerBase>();
        }

        public static object Diagnostics()
        {
            return new Dictionary<string, object> {
                { "scope", "One synchronous CaptureFrame membership snapshot; no component references retained between callbacks. Native fields are always reread. First three typed uses per scene-metadata generation compare exact IDs/order; mismatch falls back for that type until next load." },
                { "generation", generation }, { "active", current != null }, { "batches", batches },
                { "discoveryTicks", discoveryTicks }, { "tickFrequency", System.Diagnostics.Stopwatch.Frequency },
                { "lastMonoBehaviourCount", lastCount }, { "batchQueries", hits },
                { "typedFallbackQueries", nativeFallbacks }, { "typedComparisons", comparisons },
                { "membershipProofs", proofs.ToDictionary(p => p.Key.FullName, p => p.Value) },
                { "refusedTypes", refused.Select(t => t.FullName).OrderBy(x => x).ToArray() }
            };
        }
    }
}
