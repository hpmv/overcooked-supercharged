using System;
using System.Collections.Generic;
using System.Numerics;

namespace Hpmv {
    public static class CollectionsUtils {
        public static V ComputeIfAbsent<K, V>(this Dictionary<K, V> dict, K key, Func<V> valueFunc) {
            V value;
            if (dict.TryGetValue(key, out value)) {
                return value;
            }
            value = valueFunc();
            dict[key] = value;
            return value;
        }

        public static Vector3 ToNumericsVector(this UnityEngine.Vector3 vector) {
            return new Vector3(vector.x, vector.y, vector.z);
        }

        public static UnityEngine.Vector3 ToUnityVector(this Point vector) {
            return new UnityEngine.Vector3((float) vector.X, (float) vector.Y, (float) vector.Z);
        }

        public static Vector3 ToNumericsVector(this Point vector) {
            return new Vector3((float) vector.X, (float) vector.Y, (float) vector.Z);
        }

        public static UnityEngine.Vector3 ToUnityVector(this Vector3 vector) {
            return new UnityEngine.Vector3(vector.X, vector.Y, vector.Z);
        }

        public static Vector3 ToXZVector3(this(double x, double y) d) {
            return new Vector3((float) d.x, 0, (float) d.y);
        }
    }
}