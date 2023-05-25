using System;
using System.Collections.Generic;

namespace Hpmv {
    public static class OrderRandom {
        public static Random myRandom = new Random();

        public static KeyValuePair<int, T> GetWeightedRandomElement<T>(this T[] _items, Func<int, T, float> _weight) {
            float num = 0f;
            for (int i = 0; i < _items.Length; i++) {
                float num2 = _weight(i, _items[i]);
                num += num2;
            }
            float num3 = (float) (myRandom.NextDouble() * num);
            float num4 = 0f;
            for (int j = 0; j < _items.Length; j++) {
                num4 += _weight(j, _items[j]);
                if (num3 <= num4) {
                    return new KeyValuePair<int, T>(j, _items[j]);
                }
            }
            return new KeyValuePair<int, T>(-1, default(T));
        }
    }
}