using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OrderController;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace Oc2Tas
{
    [Serializable]
    public sealed class GameScoreState
    {
        public int score, baseScore, tips, multiplier, combo, deductions, delivered;
        public bool comboMaintained;
        internal GameScoreState Copy() { return (GameScoreState)MemberwiseClone(); }
    }

    [Serializable]
    public sealed class GameEventState
    {
        public long index, frame, fixedFrame, gameplayFrame, gameplayFixedFrame, finalizedFrame = -1;
        public int unityFrame, team, stationEntityId, orderId, recipeId, baseValue;
        public string kind, recipe, foodSignature, reason;
        public int[] deliveredIngredientIds = new int[0];
        public float remainingFraction = -1f, orderRemaining = -1f, orderLifetime = -1f, roundElapsed = -1f;
        public bool nativeMatch, wasCombo, scoreApplied, inputsNeutral;
        public GameScoreState beforeScore, afterScore;
        public int scoreDelta, baseScoreDelta, tipDelta, deductionDelta, deliveryDelta;

        internal GameEventState Copy()
        {
            GameEventState copy = (GameEventState)MemberwiseClone();
            copy.deliveredIngredientIds = (int[])deliveredIngredientIds.Clone();
            if (beforeScore != null) copy.beforeScore = beforeScore.Copy();
            if (afterScore != null) copy.afterScore = afterScore.Copy();
            return copy;
        }
    }

    // All hooks observe native arguments/results and score objects. They never
    // change an argument, return value, game state, timer, order, or input event.
    // Call Install/Reset/Capture and RecordInputRelease on Unity's main thread.
    public static class GameEvents
    {
        private sealed class PendingDelivery
        {
            public ServerTeamMonitor monitor;
            public GameEventState record;
        }

        private const int Capacity = 4096;
        private static readonly List<GameEventState> Events = new List<GameEventState>();
        private static readonly List<PendingDelivery> Pending = new List<PendingDelivery>();
        private static long nextIndex;
        public static bool Installed { get; private set; }
        public static int DroppedEventCount { get; private set; }
        public static string LastError { get; private set; }

        public static void Install(Harmony harmony)
        {
            if (harmony == null) throw new ArgumentNullException("harmony");
            if (Installed) return;
            Patch(harmony, typeof(ServerTeamMonitor), "OnFoodDelivered",
                new Type[] { typeof(AssembledDefinitionNode), typeof(PlatingStepData), typeof(OrderID).MakeByRefType(),
                    typeof(RecipeList.Entry).MakeByRefType(), typeof(float).MakeByRefType(), typeof(bool).MakeByRefType() },
                "BeforeMatch", "AfterMatch");
            Patch(harmony, typeof(ServerKitchenFlowControllerBase), "OnSuccessfulDelivery",
                new Type[] { typeof(OrderID), typeof(RecipeList.Entry), typeof(float), typeof(bool), typeof(ServerPlateStation) },
                null, "AfterSuccessfulDelivery");
            Patch(harmony, typeof(ServerKitchenFlowControllerBase), "OnFailedDelivery",
                new Type[] { typeof(ServerPlateStation) }, null, "AfterFailedDelivery");
            Patch(harmony, typeof(ServerKitchenFlowControllerBase), "OnOrderExpired",
                new Type[] { typeof(TeamID), typeof(OrderID) }, "BeforeTimeout", "AfterTimeout");
            Installed = true;
            Reset();
        }

        private static void Patch(Harmony harmony, Type type, string name, Type[] parameters, string prefix, string postfix)
        {
            MethodInfo original = AccessTools.Method(type, name, parameters);
            if (original == null) throw new MissingMethodException(type.FullName, name);
            harmony.Patch(original, Hook(prefix), Hook(postfix));
        }

        private static HarmonyMethod Hook(string name)
        {
            return name == null ? null : new HarmonyMethod(typeof(GameEvents).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));
        }

        public static void Reset()
        {
            Events.Clear();
            Pending.Clear();
            nextIndex = 0;
            DroppedEventCount = 0;
            LastError = "";
        }

        // Non-destructive snapshots allow successive telemetry frames to retain
        // delivery evidence. index is unique until the caller resets for a run.
        public static GameEventState[] Capture()
        {
            List<GameEventState> result = new List<GameEventState>();
            foreach (GameEventState record in Events) result.Add(record.Copy());
            // Normally no pending entry survives the native flow call. If it
            // does, expose scoreApplied=false instead of inventing a score delta.
            foreach (PendingDelivery pending in Pending) result.Add(pending.record.Copy());
            result.Sort(delegate(GameEventState a, GameEventState b) { return a.index.CompareTo(b.index); });
            return result.ToArray();
        }

        private static GameEventState Begin(string kind)
        {
            return new GameEventState { index = nextIndex++, kind = kind, frame = NativeTime.Frame,
                fixedFrame = NativeTime.FixedFrame, gameplayFrame = NativeTime.GameplayFrame,
                gameplayFixedFrame = NativeTime.GameplayFixedFrame, unityFrame = Time.frameCount };
        }

        private static GameScoreState Score(ServerTeamMonitor monitor)
        {
            if (monitor == null || monitor.Score == null) return null;
            TeamMonitor.TeamScoreStats score = monitor.Score;
            return new GameScoreState { score = score.GetTotalScore(), baseScore = score.TotalBaseScore,
                tips = score.TotalTipsScore, multiplier = score.TotalMultiplier, combo = score.TotalCombo,
                deductions = score.TotalTimeExpireDeductions, delivered = score.TotalSuccessfulDeliveries,
                comboMaintained = score.ComboMaintained };
        }

        private static void BeforeMatch(ServerTeamMonitor __instance, AssembledDefinitionNode __0, out GameEventState __state)
        {
            __state = null;
            try
            {
                __state = Begin("delivery");
                __state.beforeScore = Score(__instance);
                List<int> ingredients = new List<int>();
                __state.foodSignature = FoodSignature(__0, ingredients, 0);
                __state.deliveredIngredientIds = ingredients.ToArray();
            }
            catch (Exception error) { ObserverError("before match", error); }
        }

        private static void AfterMatch(ServerTeamMonitor __instance, OrderID __2, RecipeList.Entry __3,
            float __4, bool __5, bool __result, GameEventState __state)
        {
            try
            {
                if (__state == null) return;
                __state.nativeMatch = __result;
                __state.orderId = __result ? (int)__2.m_id : -1;
                __state.remainingFraction = __4;
                __state.wasCombo = __5;
                SetRecipe(__state, __3);
                Pending.Add(new PendingDelivery { monitor = __instance, record = __state });
            }
            catch (Exception error) { ObserverError("after match", error); }
        }

        private static void AfterSuccessfulDelivery(ServerKitchenFlowControllerBase __instance, OrderID __0,
            RecipeList.Entry __1, float __2, bool __3, ServerPlateStation __4)
        {
            try
            {
                ServerTeamMonitor monitor = __instance.GetMonitorForTeam(__4.GetTeamID());
                GameEventState record = TakePending(monitor, true, (int)__0.m_id);
                if (record == null)
                {
                    record = Begin("delivery");
                    record.nativeMatch = true;
                    record.orderId = (int)__0.m_id;
                    record.remainingFraction = __2;
                    record.wasCombo = __3;
                    record.reason = "Native success observed without a matching monitor callback; beforeScore is unavailable.";
                    SetRecipe(record, __1);
                }
                SetStation(record, __instance, __4);
                FinalizeScore(record, monitor);
            }
            catch (Exception error) { ObserverError("successful delivery", error); }
        }

        private static void AfterFailedDelivery(ServerKitchenFlowControllerBase __instance, ServerPlateStation __0)
        {
            try
            {
                ServerTeamMonitor monitor = __instance.GetMonitorForTeam(__0.GetTeamID());
                GameEventState record = TakePending(monitor, false, -1);
                if (record == null)
                {
                    record = Begin("delivery");
                    record.orderId = -1;
                    record.reason = "Native failure observed without a matching monitor callback; beforeScore is unavailable.";
                }
                SetStation(record, __instance, __0);
                FinalizeScore(record, monitor);
            }
            catch (Exception error) { ObserverError("failed delivery", error); }
        }

        private static GameEventState TakePending(ServerTeamMonitor monitor, bool success, int orderId)
        {
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                PendingDelivery pending = Pending[i];
                if (System.Object.ReferenceEquals(pending.monitor, monitor) && pending.record.nativeMatch == success
                    && (!success || pending.record.orderId == orderId))
                {
                    Pending.RemoveAt(i);
                    return pending.record;
                }
            }
            return null;
        }

        private static void BeforeTimeout(ServerKitchenFlowControllerBase __instance, TeamID __0, OrderID __1, out GameEventState __state)
        {
            __state = null;
            try
            {
                __state = Begin("timeout");
                __state.team = (int)__0;
                __state.orderId = (int)__1.m_id;
                ServerTeamMonitor monitor = __instance.GetMonitorForTeam(__0);
                __state.beforeScore = Score(monitor);
                if (__instance.RoundTimer != null) __state.roundElapsed = __instance.RoundTimer.TimeElapsed;
                ServerOrderData order = monitor.OrdersController.GetSerialisedOrderData(__1) as ServerOrderData;
                if (order != null)
                {
                    SetRecipe(__state, order.RecipeListEntry);
                    __state.orderRemaining = order.Remaining;
                    __state.orderLifetime = order.Lifetime;
                }
            }
            catch (Exception error) { ObserverError("before timeout", error); }
        }

        private static void AfterTimeout(ServerKitchenFlowControllerBase __instance, TeamID __0, GameEventState __state)
        {
            try { if (__state != null) FinalizeScore(__state, __instance.GetMonitorForTeam(__0)); }
            catch (Exception error) { ObserverError("after timeout", error); }
        }

        private static void SetRecipe(GameEventState record, RecipeList.Entry entry)
        {
            if (entry == null) return;
            record.baseValue = entry.m_scoreForMeal;
            if (entry.m_order != null) { record.recipeId = entry.m_order.m_uID; record.recipe = entry.m_order.name; }
        }

        private static void SetStation(GameEventState record, ServerKitchenFlowControllerBase flow, ServerPlateStation station)
        {
            record.team = (int)station.GetTeamID();
            record.stationEntityId = (int)EntitySerialisationRegistry.GetId(station.gameObject);
            if (flow.RoundTimer != null) record.roundElapsed = flow.RoundTimer.TimeElapsed;
        }

        private static void FinalizeScore(GameEventState record, ServerTeamMonitor monitor)
        {
            // These hooks run after the native flow increments score or applies
            // timeout/combo deductions, before another delivery can enter the flow.
            record.afterScore = Score(monitor);
            record.finalizedFrame = NativeTime.Frame;
            record.scoreApplied = record.afterScore != null;
            if (record.beforeScore != null && record.afterScore != null)
            {
                record.scoreDelta = record.afterScore.score - record.beforeScore.score;
                record.baseScoreDelta = record.afterScore.baseScore - record.beforeScore.baseScore;
                record.tipDelta = record.afterScore.tips - record.beforeScore.tips;
                record.deductionDelta = record.afterScore.deductions - record.beforeScore.deductions;
                record.deliveryDelta = record.afterScore.delivered - record.beforeScore.delivered;
            }
            Add(record);
        }

        // Optional protocol integration: call after Inputs.Release() on a lost
        // connection or cancelled run. This records whether release actually left
        // the observed four-pad input state neutral; it does not release anything.
        public static void RecordInputRelease(string reason)
        {
            try
            {
                GameEventState record = Begin("input_release");
                record.reason = reason ?? "";
                record.inputsNeutral = true;
                foreach (ChefInput input in Inputs.Current())
                    if (input.x != 0 || input.y != 0 || input.pickup || input.use || input.dash) record.inputsNeutral = false;
                record.finalizedFrame = NativeTime.Frame;
                Add(record);
            }
            catch (Exception error) { ObserverError("input release", error); }
        }

        private static string FoodSignature(AssembledDefinitionNode node, List<int> ingredients, int depth)
        {
            if (node == null) return "null";
            if (depth > 24) return "depth-limit";
            IngredientAssembledNode ingredient = node as IngredientAssembledNode;
            if (ingredient != null && ingredient.m_ingriedientOrderNode != null)
            {
                int id = ingredient.m_ingriedientOrderNode.m_uID;
                ingredients.Add(id);
                return id.ToString();
            }
            string label = node.GetType().Name;
            CookedCompositeAssembledNode cooked = node as CookedCompositeAssembledNode;
            MixedCompositeAssembledNode mixed = node as MixedCompositeAssembledNode;
            if (cooked != null) label = "cook:" + (cooked.m_cookingStep == null ? "?" : cooked.m_cookingStep.m_uID.ToString()) + ":" + cooked.m_progress;
            else if (mixed != null) label = "mix:" + mixed.m_progress;
            CompositeAssembledNode composite = node as CompositeAssembledNode;
            if (composite == null || composite.m_composition == null) return label;
            List<string> children = new List<string>();
            foreach (AssembledDefinitionNode child in composite.m_composition) children.Add(FoodSignature(child, ingredients, depth + 1));
            return label + "(" + String.Join(",", children.ToArray()) + ")";
        }

        private static void Add(GameEventState record)
        {
            if (Events.Count >= Capacity) { Events.RemoveAt(0); DroppedEventCount++; }
            Events.Add(record);
        }

        private static void ObserverError(string source, Exception error)
        {
            string message = source + ": " + error.GetType().Name + ": " + error.Message;
            if (LastError != message) Debug.LogWarning("[OC2 TAS events] " + message);
            LastError = message;
        }
    }
}
