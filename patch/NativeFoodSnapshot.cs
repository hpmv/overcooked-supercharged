using System;
using System.Collections.Generic;
using HarmonyLib;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch
{
    // Explicit, read-only decision-boundary telemetry. This is not collected on
    // every frame and retains native preparation hierarchy lost by flat lists.
    public static class NativeFoodSnapshot
    {
        public static object Capture()
        {
            var records = new List<object>();
            var entries = EntitySerialisationRegistry.m_EntitiesList;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries._items[i]; var obj = entry.m_GameObject;
                if (obj == null) continue;
                IOrderDefinition definition = obj.GetComponent<ServerCookableContainer>();
                if (definition == null) definition = obj.GetComponent<ServerMixableContainer>();
                if (definition == null) definition = obj.GetComponent<ServerPreparationContainer>();
                if (definition == null) definition = obj.GetComponent<ServerPlate>();
                if (definition == null) definition = obj.GetComponent<IngredientPropertiesComponent>();
                var cooking = obj.GetComponent<ServerCookingHandler>();
                var mixing = obj.GetComponent<ServerMixingHandler>();
                var work = obj.GetComponent<ServerWorkableItem>();
                if (definition == null && cooking == null && mixing == null && work == null) continue;
                var record = Map("id", (int)entry.m_Header.m_uEntityID, "name", obj.name,
                    "composition", definition == null ? null : Food(definition.GetOrderComposition(), 0));
                if (cooking != null)
                {
                    record["cookingProgress"] = cooking.GetCookingProgress();
                    record["cookingState"] = cooking.GetCookedOrderState().ToString();
                    var config = obj.GetComponent<CookingHandler>();
                    if (config != null) record["cookingTime"] = config.m_cookingtime;
                }
                if (mixing != null)
                {
                    record["mixingProgress"] = mixing.GetMixingProgress();
                    record["mixingState"] = mixing.GetMixedOrderState().ToString();
                    var config = obj.GetComponent<MixingHandler>();
                    if (config != null) record["mixingTime"] = config.m_mixingTime;
                }
                if (work != null)
                {
                    record["workStage"] = AccessTools.Field(typeof(ServerWorkableItem), "m_progress").GetValue(work);
                    record["workSubStage"] = AccessTools.Field(typeof(ServerWorkableItem), "m_subProgress").GetValue(work);
                    var config = obj.GetComponent<WorkableItem>();
                    if (config != null) record["workStages"] = config.m_stages;
                }
                records.Add(record);
            }
            return Map("source", "native-server-preparation-composition", "unityFrame", Time.frameCount, "entities", records);
        }

        private static object Food(AssembledDefinitionNode node, int depth)
        {
            if (node == null) return null;
            if (depth > 24) throw new InvalidOperationException("Native preparation hierarchy exceeds telemetry depth limit.");
            var result = Map("type", node.GetType().Name);
            var ingredient = node as IngredientAssembledNode;
            if (ingredient != null && ingredient.m_ingriedientOrderNode != null)
            {
                result["id"] = ingredient.m_ingriedientOrderNode.m_uID;
                result["name"] = ingredient.m_ingriedientOrderNode.name;
            }
            var item = node as ItemAssembledNode;
            if (item != null && item.m_itemOrderNode != null)
            {
                result["id"] = item.m_itemOrderNode.m_uID;
                result["name"] = item.m_itemOrderNode.name;
            }
            var cooked = node as CookedCompositeAssembledNode;
            if (cooked != null)
            {
                result["state"] = cooked.m_progress.ToString(); result["recordedProgress"] = cooked.m_recordedProgress;
                result["cookingStepId"] = cooked.m_cookingStep == null ? (object)null : cooked.m_cookingStep.m_uID;
            }
            var mixed = node as MixedCompositeAssembledNode;
            if (mixed != null)
            {
                result["state"] = mixed.m_progress.ToString(); result["recordedProgress"] = mixed.m_recordedProgress;
            }
            var composite = node as CompositeAssembledNode;
            if (composite != null)
            {
                var children = new List<object>(); var optional = new List<object>();
                if (composite.m_composition != null) foreach (var child in composite.m_composition) children.Add(Food(child, depth + 1));
                if (composite.m_optional != null) foreach (var child in composite.m_optional) optional.Add(Food(child, depth + 1));
                result["children"] = children; result["optional"] = optional;
            }
            return result;
        }

        private static Dictionary<string, object> Map(params object[] pairs)
        {
            var result = new Dictionary<string, object>();
            for (int i = 0; i < pairs.Length; i += 2) result.Add((string)pairs[i], pairs[i + 1]);
            return result;
        }
    }
}
