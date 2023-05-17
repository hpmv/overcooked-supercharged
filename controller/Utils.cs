using System;
using System.Collections.Generic;
using System.Numerics;
using Newtonsoft.Json;
using Team17.Online.Multiplayer.Messaging;

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

        public static Vector3 ToNumerics(this UnityEngine.Vector3 vector) {
            return new Vector3(vector.x, vector.y, vector.z);
        }

        public static System.Numerics.Quaternion ToNumerics(this UnityEngine.Quaternion q) {
            return new System.Numerics.Quaternion(q.x, q.y, q.z, q.w);
        }

        public static Vector3 FromThrift(this Point vector) {
            return new Vector3((float)vector.X, (float)vector.Y, (float)vector.Z);
        }

        public static Point ToThrift(this Vector3 vector) {
            return new Point { X = vector.X, Y = vector.Y, Z = vector.Z };
        }

        public static System.Numerics.Quaternion FromThrift(this Quaternion quaternion) {
            return new System.Numerics.Quaternion((float)quaternion.X, (float)quaternion.Y, (float)quaternion.Z, (float)quaternion.W);
        }

        public static Quaternion ToThrift(this System.Numerics.Quaternion quaternion) {
            return new Quaternion { X = quaternion.X, Y = quaternion.Y, Z = quaternion.Z, W = quaternion.W };
        }

        public static Vector3 ToXZVector3(this (double x, double y) d) {
            return new Vector3((float)d.x, 0, (float)d.y);
        }

        public static Vector3 ToXZVector3(this Vector2 d) {
            return new Vector3(d.X, 0, d.Y);
        }

        public static Vector2 XZ(this Vector3 vector) {
            return new Vector2(vector.X, vector.Z);
        }

        public static Vector2 ToForwardVector(this System.Numerics.Quaternion rotation) {
            return Vector3.Transform(Vector3.UnitZ, rotation).XZ();
        }
    }

    public static class JsonUtils {
        public static string toJson(this Serialisable serialisable) {
            if (serialisable == null) return "(null)";
            string serialized;
            try {
                serialized = JsonConvert.SerializeObject(serialisable, new JsonSerializerSettings() {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    Formatting = Formatting.Indented
                });
            } catch (Exception e) {
                serialized = e.Message;
            }
            return serialized;
        }
    }

    public static class IngredientsUtils {
        public static IEnumerable<int> Flatten(this AssembledDefinitionNode[] nodes) {
            foreach (var node in nodes) {
                foreach (var item in node.Flatten()) {
                    yield return item;
                }
            }
        }

        public static IEnumerable<int> Flatten(this AssembledDefinitionNode node) {
            if (node is CompositeAssembledNode composite) {
                foreach (var item in composite.m_composition.Flatten()) {
                    yield return item;
                }
            } else if (node is IngredientAssembledNode ingredient) {
                yield return ingredient.id;
            }
        }
    }
}