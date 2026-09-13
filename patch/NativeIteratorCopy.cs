using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SuperchargedPatch
{
    // Copies only an explicit generated-iterator schema. No iterator constructor,
    // MoveNext, Dispose or Unity scheduler API is invoked by capture/restore.
    public sealed class NativeIteratorCopy
    {
        private readonly Dictionary<Type, FieldInfo[]> schemas;
        private readonly Func<object, bool> pinned;
        private static readonly MethodInfo clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        public NativeIteratorCopy(Dictionary<Type, string[]> fields, Func<object, bool> pinned)
        {
            this.pinned = pinned;
            schemas = new Dictionary<Type, FieldInfo[]>();
            foreach (var pair in fields)
            {
                if (!typeof(IEnumerator).IsAssignableFrom(pair.Key)) throw new InvalidOperationException("Whitelisted native iterator is not an IEnumerator.");
                var actual = pair.Key.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).OrderBy(f => f.Name).ToArray();
                if (!actual.Select(f => f.Name).SequenceEqual(pair.Value.OrderBy(n => n)))
                    throw new InvalidOperationException("Native iterator field layout changed: " + pair.Key.FullName);
                schemas.Add(pair.Key, actual);
            }
        }
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
            public int GetHashCode(object value) { return RuntimeHelpers.GetHashCode(value); }
        }
        public sealed class Snapshot
        {
            private readonly NativeIteratorCopy owner;
            private readonly IEnumerator saved;
            internal Snapshot(NativeIteratorCopy owner, IEnumerator saved) { this.owner = owner; this.saved = saved; }
            public IEnumerator RestoreCopy() { return owner.Copy(saved); }
            public bool Matches(IEnumerator actual) { return owner.Same(saved, actual, 0); }
        }
        public Snapshot Capture(IEnumerator iterator) { return new Snapshot(this, Copy(iterator)); }
        private IEnumerator Copy(IEnumerator value)
        {
            return (IEnumerator)CopyValue(value, new Dictionary<object, object>(new ReferenceComparer()), 0);
        }
        private object CopyValue(object value, Dictionary<object, object> copies, int depth)
        {
            if (depth > 8 || copies.Count > 8) throw new InvalidOperationException("Excessive native iterator graph.");
            if (value == null || value is string) return value;
            if (value.GetType().IsValueType)
            {
                if (!SafeValueType(value.GetType(), 0)) throw new InvalidOperationException("Native iterator value contains a managed reference.");
                return value;
            }
            FieldInfo[] fields;
            if (!schemas.TryGetValue(value.GetType(), out fields))
            {
                if (pinned(value)) return value;
                throw new InvalidOperationException("Unknown mutable native iterator reference: " + value.GetType().FullName);
            }
            object existing;
            if (copies.TryGetValue(value, out existing)) return existing;
            object result = clone.Invoke(value, null);
            copies.Add(value, result);
            foreach (FieldInfo field in fields) field.SetValue(result, CopyValue(field.GetValue(value), copies, depth + 1));
            return result;
        }
        private static bool SafeValueType(Type type, int depth)
        {
            if (type.IsPrimitive || type.IsEnum) return true;
            return depth < 8 && type.IsValueType && type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .All(field => SafeValueType(field.FieldType, depth + 1));
        }
        private bool Same(object a, object b, int depth)
        {
            if (depth > 8) return false;
            if (a == null || b == null) return a == null && b == null;
            if (a.GetType() != b.GetType()) return false;
            if (a.GetType().IsValueType || a is string) return a.Equals(b);
            FieldInfo[] fields;
            if (!schemas.TryGetValue(a.GetType(), out fields)) return pinned(a) && ReferenceEquals(a, b);
            foreach (FieldInfo field in fields) if (!Same(field.GetValue(a), field.GetValue(b), depth + 1)) return false;
            return true;
        }
    }
}
