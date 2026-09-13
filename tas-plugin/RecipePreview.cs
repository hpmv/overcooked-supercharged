using System;
using System.Collections.Generic;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oc2Tas
{
    [Serializable]
    public sealed class PreviewRecipeState
    {
        public int index, drawIndex, recipeId, baseValue;
        public string recipe;
    }

    [Serializable]
    public sealed class RecipePreviewState
    {
        public int seed, count, liveRecipeCountBefore, liveRecipeCountAfter;
        public int liveRoundInstanceOrdinalBefore, liveRoundInstanceOrdinalAfter;
        public long frame, fixedFrame;
        public string scene, generator, method, initialiseMethod, nativeModuleVersionId, interpretation;
        public string ambientBefore, ambientAfter, isolatedBefore, isolatedAfter, previewInitialRandom, previewFinalRandom;
        public string isolatedScope, isolatedRegistryBefore, isolatedRegistryAfter;
        public bool freshInstance, observersBypassed, ambientRngRestored, isolatedRngRestored;
        public bool observationsRestored, liveInstanceUnchanged, frameUnchanged;
        public bool isolatedRegistryRestored;
        public int observedDrawsBefore, observedDrawsAfter, lifecycleBefore, lifecycleAfter;
        public int[] liveFrequenciesBefore = new int[0], liveFrequenciesAfter = new int[0], previewFinalFrequencies = new int[0];
        public PreviewRecipeState[] recipes = new PreviewRecipeState[0];
    }

    public static class RecipePreview
    {
        // Call only from the existing main-thread command boundary. The native
        // RoundData object supplies immutable recipe configuration; all mutable
        // frequency/count state belongs to the newly initialized temporary data.
        public static RecipePreviewState Preview(int seed, int count)
        {
            if (count < 1 || count > 1024) throw new ArgumentOutOfRangeException("count", "Recipe preview count must be 1..1024.");
            RecipePreviewState result = new RecipePreviewState { seed = seed, count = count,
                method = "RoundData.GetNextRecipe(RoundInstanceDataBase)", initialiseMethod = "RoundData.InitialiseRound()",
                nativeModuleVersionId = typeof(RoundData).Module.ModuleVersionId.ToString(),
                isolatedScope = "native-round-instance",
                interpretation = "Fresh native weighted sequence for the requested seed. Each native RoundInstanceData has an independent seeded recipe RNG stream, so outgoing rounds cannot consume this round's draws. This predicts the isolated stream from round start; ambient draws can change a non-isolated live sequence." };
            NativeTime.RecipePreviewGuard guard = new NativeTime.RecipePreviewGuard();
            object liveInstance = null;
            ServerOrderControllerBase controller = null;
            RoundData round = null;
            try
            {
                result.frame = guard.FrameBefore;
                result.fixedFrame = guard.FixedFrameBefore;
                result.scene = SceneManager.GetActiveScene().name;
                result.ambientBefore = guard.AmbientBefore;
                result.isolatedBefore = guard.IsolatedBefore;
                result.isolatedRegistryBefore = guard.RegistryBefore;
                result.liveRoundInstanceOrdinalBefore = guard.LiveRoundOrdinalBefore;
                result.observedDrawsBefore = guard.DrawCountBefore;
                result.lifecycleBefore = guard.LifecycleCountBefore;
                ServerKitchenFlowControllerBase flow = UnityEngine.Object.FindObjectOfType<ServerKitchenFlowControllerBase>();
                if (flow == null) throw new InvalidOperationException("Load a native kitchen before requesting a recipe preview.");
                ServerTeamMonitor monitor = flow.GetMonitorForTeam(TeamID.One);
                controller = monitor == null ? null : monitor.OrdersController;
                object config = NativeFields.Get(controller, "m_roundData");
                // Subclasses can depend on phase, player state or live callbacks.
                // The audited RoundData implementation mutates only fresh data.
                if (config == null || config.GetType() != typeof(RoundData))
                    throw new InvalidOperationException("Recipe preview supports exactly native RoundData; observed " + (config == null ? "null" : config.GetType().FullName));
                round = (RoundData)config;
                result.generator = round.GetType().FullName;
                liveInstance = NativeFields.Get(controller, "m_roundInstanceData");
                if (liveInstance == null) throw new InvalidOperationException("The live native round instance is unavailable.");
                result.liveRecipeCountBefore = NativeFields.Int(liveInstance, "RecipeCount");
                result.liveFrequenciesBefore = Frequencies(liveInstance);
                UnityEngine.Random.InitState(seed);
                result.previewInitialRandom = NativeTime.RandomStateFingerprint(UnityEngine.Random.state);
                RoundInstanceDataBase fresh = round.InitialiseRound();
                if (fresh == null || object.ReferenceEquals(fresh, liveInstance))
                    throw new InvalidOperationException("Native InitialiseRound did not return an independent instance.");
                result.freshInstance = true;
                result.observersBypassed = true;
                List<PreviewRecipeState> recipes = new List<PreviewRecipeState>();
                for (int draw = 0; draw < count; draw++)
                {
                    RecipeList.Entry[] entries = round.GetNextRecipe(fresh);
                    if (entries == null || entries.Length != 1 || entries[0] == null || entries[0].m_order == null)
                        throw new InvalidOperationException("Audited RoundData must produce exactly one valid native recipe per draw.");
                    RecipeList.Entry entry = entries[0];
                    recipes.Add(new PreviewRecipeState { index = recipes.Count, drawIndex = draw,
                        recipeId = entry.m_order.m_uID, recipe = entry.m_order.name, baseValue = entry.m_scoreForMeal });
                }
                result.recipes = recipes.ToArray();
                result.previewFinalFrequencies = Frequencies(fresh);
                result.previewFinalRandom = NativeTime.RandomStateFingerprint(UnityEngine.Random.state);
            }
            finally { guard.Dispose(); }
            result.ambientAfter = NativeTime.RandomStateFingerprint(UnityEngine.Random.state);
            result.isolatedAfter = NativeTime.IsolatedRecipeStateFingerprint();
            result.isolatedRegistryAfter = NativeTime.RecipeStreamRegistryFingerprint();
            result.liveRoundInstanceOrdinalAfter = NativeTime.CurrentRecipeRoundInstanceOrdinal;
            result.observedDrawsAfter = NativeTime.GetRecipeDraws().Length;
            result.lifecycleAfter = NativeTime.GetLifecycleMarkers().Length;
            result.liveRecipeCountAfter = NativeFields.Int(liveInstance, "RecipeCount");
            result.liveFrequenciesAfter = Frequencies(liveInstance);
            result.ambientRngRestored = result.ambientBefore == result.ambientAfter;
            result.isolatedRngRestored = result.isolatedBefore == result.isolatedAfter;
            result.isolatedRegistryRestored = result.isolatedRegistryBefore == result.isolatedRegistryAfter
                && result.liveRoundInstanceOrdinalBefore == result.liveRoundInstanceOrdinalAfter;
            result.observationsRestored = result.observedDrawsBefore == result.observedDrawsAfter && result.lifecycleBefore == result.lifecycleAfter;
            result.frameUnchanged = NativeTime.Frame == result.frame && NativeTime.FixedFrame == result.fixedFrame;
            result.liveInstanceUnchanged = object.ReferenceEquals(NativeFields.Get(controller, "m_roundInstanceData"), liveInstance)
                && object.ReferenceEquals(NativeFields.Get(controller, "m_roundData"), round)
                && result.liveRecipeCountBefore == result.liveRecipeCountAfter && Equal(result.liveFrequenciesBefore, result.liveFrequenciesAfter);
            if (!result.ambientRngRestored || !result.isolatedRngRestored || !result.isolatedRegistryRestored || !result.observationsRestored || !result.frameUnchanged || !result.liveInstanceUnchanged)
                throw new InvalidOperationException("Read-only recipe preview state-restoration invariant failed.");
            return result;
        }

        private static int[] Frequencies(object instance)
        {
            int[] frequencies = NativeFields.Get(instance, "CumulativeFrequencies") as int[];
            if (frequencies == null) throw new InvalidOperationException("Native RoundData frequency state is unavailable.");
            return (int[])frequencies.Clone();
        }

        private static bool Equal(int[] a, int[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
