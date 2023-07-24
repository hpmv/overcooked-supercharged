using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SuperchargedPatch.AlteredComponents
{
    public class WarpableRoundData : RoundData
    {
        public WarpableRoundData(RoundData original)
        {
            m_recipes = original.m_recipes;
            m_roundTimer = original.m_roundTimer;
            random = new System.Random(12345);
        }

        public override RoundInstanceDataBase InitialiseRound()
        {
            return new WarpableRoundInstanceData
            {
                CumulativeFrequencies = new int[m_recipes.m_recipes.Length]
            };
        }

        public override RecipeList.Entry[] GetNextRecipe(RoundInstanceDataBase _data)
        {
            var data = _data as WarpableRoundInstanceData;
            if (data.nextIndex < data.history.Count)
            {
                var result = data.history[data.nextIndex];
                data.nextIndex++;
                return new RecipeList.Entry[]
                {
                    m_recipes.m_recipes[result]
                };
            }
            KeyValuePair<int, RecipeList.Entry> weightedRandomElement =  m_recipes.m_recipes.GetWeightedRandomElement(
                random, (int i, RecipeList.Entry e) => GetWeight(data, i));
            data.CumulativeFrequencies[weightedRandomElement.Key]++;
            data.history.Add(weightedRandomElement.Key);
            data.nextIndex++;
            return new RecipeList.Entry[]
            {
                weightedRandomElement.Value
            };
        }

        private float GetWeight(WarpableRoundInstanceData _instance, int _recipeIndex)
        {
            int num = _instance.CumulativeFrequencies.Collapse((int f, int total) => total + f);
            float num2 = (float)(num + 2) / m_recipes.m_recipes.Length;
            return Mathf.Max(num2 - _instance.CumulativeFrequencies[_recipeIndex], 0f);
        }

        public void Warp(WarpableRoundInstanceData instance, int toIndex)
        {
            while (toIndex > instance.history.Count)
            {
                GetNextRecipe(instance);
            }
            instance.nextIndex = toIndex;
        }

        public System.Random random;
    }

    public class WarpableRoundInstanceData : RoundInstanceDataBase
    {
        public int[] CumulativeFrequencies;
        public List<int> history = new List<int>();
        public int nextIndex = 0;
    }

    public static class OrderRandom
    {
        public static KeyValuePair<int, T> GetWeightedRandomElement<T>(this T[] _items, System.Random random, Func<int, T, float> _weight)
        {
            float num = 0f;
            for (int i = 0; i < _items.Length; i++)
            {
                float num2 = _weight(i, _items[i]);
                num += num2;
            }
            float num3 = (float)(random.NextDouble() * num);
            float num4 = 0f;
            for (int j = 0; j < _items.Length; j++)
            {
                num4 += _weight(j, _items[j]);
                if (num3 <= num4)
                {
                    return new KeyValuePair<int, T>(j, _items[j]);
                }
            }
            return new KeyValuePair<int, T>(-1, default);
        }
    }

    [HarmonyPatch(typeof(CampaignLevelConfig), "GetRoundData")]
    public static class PatchCampaignLevelConfigGetRoundData
    {
        [HarmonyPostfix]
        public static void Postfix(ref RoundData __result)
        {
            __result = new WarpableRoundData(__result);
        }
    }
}
