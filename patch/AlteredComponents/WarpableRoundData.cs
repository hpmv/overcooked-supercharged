using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SuperchargedPatch.AlteredComponents
{
    // Native recipe generation with an isolated per-round Unity RNG stream.
    // Rewind is explicit authoring state restoration; normal draws always execute
    // the installed RoundData.GetNextRecipe on its own native instance.
    public sealed class WarpableRoundData : RoundData
    {
        private readonly RoundData original;
        private static int configuredSeed;

        public WarpableRoundData(RoundData source)
        {
            if (source == null || source.GetType() != typeof(RoundData))
                throw new ArgumentException("Native weighted wrapper supports exactly RoundData.");
            original = source;
            m_recipes = source.m_recipes;
            m_roundTimer = source.m_roundTimer;
        }

        // Call before native InitialiseRound. Already-running round streams keep
        // their own seed/history; outgoing rounds cannot consume the new stream.
        public static void ConfigureSeedForNextRound(int seed) { configuredSeed = seed; }

        public override RoundInstanceDataBase InitialiseRound()
        {
            var ambient = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(configuredSeed);
                return CreateInstance(configuredSeed, UnityEngine.Random.state);
            }
            finally { UnityEngine.Random.state = ambient; }
        }

        private WarpableRoundInstanceData CreateInstance(int seed, UnityEngine.Random.State initialRandom)
        {
            var ambient = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.state = initialRandom;
                var native = original.InitialiseRound();
                if (!(native is RoundInstanceData))
                    throw new InvalidOperationException("Native InitialiseRound returned an unsupported instance.");
                var result = new WarpableRoundInstanceData {
                    Owner = this, NativeData = native, Seed = seed, RandomState = UnityEngine.Random.state
                };
                result.Checkpoints.Add(CaptureNative(result));
                return result;
            }
            finally { UnityEngine.Random.state = ambient; }
        }

        public override RecipeList.Entry[] GetNextRecipe(RoundInstanceDataBase data)
        {
            var instance = RequireInstance(data);
            ValidateCurrent(instance);
            var before = CaptureNative(instance);
            var ambient = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.state = instance.RandomState;
                // Do not reproduce the weighting formula, choose an index, or
                // substitute System.Random. The installed native method owns it.
                var entries = original.GetNextRecipe(instance.NativeData);
                var afterRandom = UnityEngine.Random.state;
                if (entries == null || entries.Length != 1 || entries[0] == null || entries[0].m_order == null)
                    throw new InvalidOperationException("Native RoundData did not produce one recipe.");
                int selected = Array.IndexOf(m_recipes.m_recipes, entries[0]);
                if (selected < 0) throw new InvalidOperationException("Native recipe is not in the original asset.");
                instance.RandomState = afterRandom;
                var after = CaptureNative(instance);
                if (after.RecipeCount != before.RecipeCount + 1 || !IsNativeFrequencyDelta(before.Frequencies, after.Frequencies, selected))
                    throw new InvalidOperationException("Native weighted round state changed outside one draw.");
                int index = instance.Index;
                if (index < instance.History.Count)
                {
                    if (instance.History[index] != selected || !SameCheckpoint(after, instance.Checkpoints[index + 1]))
                        throw new InvalidOperationException("Replayed native draw disagrees with its recorded round checkpoint.");
                }
                else
                {
                    instance.History.Add(selected);
                    instance.Checkpoints.Add(after);
                }
                instance.Index++;
                return entries;
            }
            catch
            {
                RestoreNative(instance, before);
                throw;
            }
            finally { UnityEngine.Random.state = ambient; }
        }

        // Authoring seek restores frequency counts, RecipeCount and RNG together.
        // Future checkpoints are retained only as assertions of repeated native
        // execution. A fresh-process qualification must not invoke this method.
        public void Warp(WarpableRoundInstanceData instance, int toIndex)
        {
            RequireInstance(instance);
            if (toIndex < 0) throw new ArgumentOutOfRangeException("toIndex");
            ValidateCurrent(instance);
            int originalIndex = instance.Index;
            var before = CaptureNative(instance);
            int historyCount = instance.History.Count;
            try
            {
                if (toIndex >= instance.Checkpoints.Count)
                {
                    instance.Index = instance.History.Count;
                    RestoreNative(instance, instance.Checkpoints[instance.Index]);
                    while (instance.Index < toIndex) GetNextRecipe(instance);
                }
                else
                {
                    RestoreNative(instance, instance.Checkpoints[toIndex]);
                    instance.Index = toIndex;
                }
                ValidateCurrent(instance);
                instance.AuthoringWarpCount++;
            }
            catch
            {
                instance.Index = originalIndex;
                RestoreNative(instance, before);
                if (instance.History.Count > historyCount) instance.History.RemoveRange(historyCount, instance.History.Count - historyCount);
                if (instance.Checkpoints.Count > historyCount + 1) instance.Checkpoints.RemoveRange(historyCount + 1, instance.Checkpoints.Count - historyCount - 1);
                throw;
            }
        }

        // Preview uses an independent native instance and copied checkpoint arrays.
        // It neither appends to live history nor changes the live frequency frontier.
        public RoundDataAuxMessage GetAuxMessage(RoundInstanceDataBase data, int numToRewind)
        {
            var live = RequireInstance(data);
            if (numToRewind < 0) throw new ArgumentOutOfRangeException("numToRewind");
            ValidateCurrent(live);
            var before = CaptureNative(live);
            int beforeIndex = live.Index, beforeHistory = live.History.Count;
            var ambient = UnityEngine.Random.state;
            try
            {
                int start = Math.Max(0, live.Index - numToRewind);
                var scratch = CreateInstance(live.Seed, live.Checkpoints[0].RandomState);
                scratch.History.Clear();
                scratch.Checkpoints.Clear();
                for (int i = 0; i <= start; i++) scratch.Checkpoints.Add(live.Checkpoints[i].Clone());
                for (int i = 0; i < start; i++) scratch.History.Add(live.History[i]);
                RestoreNative(scratch, scratch.Checkpoints[start]);
                scratch.Index = start;
                var recipes = new AssembledDefinitionNode[10];
                for (int i = 0; i < recipes.Length; i++) recipes[i] = GetNextRecipe(scratch)[0].m_order.Convert();
                return new RoundDataAuxMessage { recipes = recipes, currentIndex = start };
            }
            finally
            {
                UnityEngine.Random.state = ambient;
                if (live.Index != beforeIndex || live.History.Count != beforeHistory || !SameCheckpoint(before, CaptureNative(live)))
                    throw new InvalidOperationException("Read-only native recipe preview changed its live round.");
            }
        }

        public NativeRoundDiagnostics GetDiagnostics(RoundInstanceDataBase data)
        {
            var instance = RequireInstance(data);
            ValidateCurrent(instance);
            var state = CaptureNative(instance);
            var recipes = new NativeRecipeDraw[instance.History.Count];
            for (int i = 0; i < recipes.Length; i++)
            {
                var entry = m_recipes.m_recipes[instance.History[i]];
                recipes[i] = new NativeRecipeDraw {
                    drawIndex = i, recipeListIndex = instance.History[i], recipeId = entry.m_order.m_uID,
                    recipe = entry.m_order.name, baseValue = entry.m_scoreForMeal
                };
            }
            return new NativeRoundDiagnostics {
                seed = instance.Seed, nextIndex = instance.Index, recipeCount = state.RecipeCount,
                cumulativeFrequencies = (int[])state.Frequencies.Clone(), history = instance.History.ToArray(),
                generator = original.GetType().FullName + ".GetNextRecipe",
                nativeModuleVersionId = typeof(RoundData).Module.ModuleVersionId.ToString(),
                roundDuration = original.m_roundTimer, isolatedPerRound = true,
                authoringWarpCount = instance.AuthoringWarpCount, nativeRecipes = recipes
            };
        }

        private WarpableRoundInstanceData RequireInstance(RoundInstanceDataBase data)
        {
            var instance = data as WarpableRoundInstanceData;
            if (instance == null || !ReferenceEquals(instance.Owner, this) || !(instance.NativeData is RoundInstanceData))
                throw new ArgumentException("Round instance does not belong to this native weighted wrapper.");
            return instance;
        }
        private NativeRoundCheckpoint CaptureNative(WarpableRoundInstanceData instance)
        {
            var native = (RoundInstanceData)instance.NativeData;
            return new NativeRoundCheckpoint {
                RecipeCount = native.RecipeCount, Frequencies = (int[])native.CumulativeFrequencies.Clone(),
                RandomState = instance.RandomState
            };
        }
        private void RestoreNative(WarpableRoundInstanceData instance, NativeRoundCheckpoint checkpoint)
        {
            var native = (RoundInstanceData)instance.NativeData;
            native.RecipeCount = checkpoint.RecipeCount;
            native.CumulativeFrequencies = (int[])checkpoint.Frequencies.Clone();
            instance.RandomState = checkpoint.RandomState;
        }
        private void ValidateCurrent(WarpableRoundInstanceData instance)
        {
            if (instance.Index < 0 || instance.Index >= instance.Checkpoints.Count || instance.Checkpoints.Count != instance.History.Count + 1
                || !SameCheckpoint(CaptureNative(instance), instance.Checkpoints[instance.Index]))
                throw new InvalidOperationException("Native round history, frequency state and cursor disagree.");
        }
        private static bool SameCheckpoint(NativeRoundCheckpoint a, NativeRoundCheckpoint b)
        {
            if (a.RecipeCount != b.RecipeCount || !a.RandomState.Equals(b.RandomState) || a.Frequencies.Length != b.Frequencies.Length) return false;
            for (int i = 0; i < a.Frequencies.Length; i++) if (a.Frequencies[i] != b.Frequencies[i]) return false;
            return true;
        }
        private static bool IsNativeFrequencyDelta(int[] before, int[] after, int selected)
        {
            if (before.Length != after.Length) return false;
            for (int i = 0; i < before.Length; i++) if (after[i] != before[i] + (i == selected ? 1 : 0)) return false;
            return true;
        }
    }

    public sealed class WarpableRoundInstanceData : RoundInstanceDataBase
    {
        internal WarpableRoundData Owner;
        internal RoundInstanceDataBase NativeData;
        internal UnityEngine.Random.State RandomState;
        internal readonly List<NativeRoundCheckpoint> Checkpoints = new List<NativeRoundCheckpoint>();
        internal readonly List<int> History = new List<int>();
        internal int Seed, Index, AuthoringWarpCount;
        public int nextIndex { get { return Index; } }
        public IList<int> history { get { return History.AsReadOnly(); } }
        public int RecipeCount { get { return Checkpoints[Index].RecipeCount; } }
        public int[] CumulativeFrequencies { get { return (int[])Checkpoints[Index].Frequencies.Clone(); } }
    }

    internal sealed class NativeRoundCheckpoint
    {
        internal int RecipeCount;
        internal int[] Frequencies;
        internal UnityEngine.Random.State RandomState;
        internal NativeRoundCheckpoint Clone() { return new NativeRoundCheckpoint { RecipeCount = RecipeCount, Frequencies = (int[])Frequencies.Clone(), RandomState = RandomState }; }
    }

    [Serializable]
    public sealed class NativeRoundDiagnostics
    {
        public int seed, nextIndex, recipeCount, authoringWarpCount;
        public int[] cumulativeFrequencies, history;
        public string generator, nativeModuleVersionId;
        public float roundDuration;
        public bool isolatedPerRound;
        public NativeRecipeDraw[] nativeRecipes;
    }

    [Serializable]
    public sealed class NativeRecipeDraw
    {
        public int drawIndex, recipeListIndex, recipeId, baseValue;
        public string recipe;
    }

    [HarmonyPatch(typeof(CampaignLevelConfig), "GetRoundData")]
    public static class PatchCampaignLevelConfigGetRoundData
    {
        [HarmonyPostfix]
        public static void Postfix(ref RoundData __result)
        {
            // Other native RoundData subclasses may depend on live game state.
            // Leave those native implementations intact; do not falsely wrap them.
            if (__result != null && __result.GetType() == typeof(RoundData)) __result = new WarpableRoundData(__result);
        }
    }
}

