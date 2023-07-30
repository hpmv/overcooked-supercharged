using System;
using System.Collections.Generic;

namespace Hpmv {
    public class Versioned<T> {
        public T initialValue;

        public Versioned(T initialValue) {
            this.initialValue = initialValue;
        }

        public List<(int time, T value)> changes = new List<(int, T)>();

        public void ChangeTo(T value, int time) {
            if (changes.Count > 0 && changes[changes.Count - 1].time > time) {
                throw new ArgumentException($"Cannot go backwards in time: {time}", "time");
            } else if (changes.Count > 0 && changes[changes.Count - 1].time == time) {
                changes[changes.Count - 1] = (time, value);
                return;
            } else if (time == 0) {
                this.initialValue = value;
                return;
            }
            var last = Last();
            if ((value == null && last == null) || value.Equals(last)) {
                return;
            }
            changes.Add((time, value));
        }

        public T this[int index] {
            get {
                int lb = -1, ub = changes.Count - 1;
                while (lb < ub) {
                    int mb = (lb + ub + 1) / 2;
                    if (changes[mb].time <= index) {
                        lb = mb;
                    } else {
                        ub = mb - 1;
                    }
                }
                if (lb == -1) {
                    return initialValue;
                }
                return changes[lb].value;
            }
        }

        public void RemoveAllAfter(int time) {
            int lb = 0, ub = changes.Count;
            while (lb < ub) {
                int mb = (lb + ub) / 2;
                if (changes[mb].time > time) {
                    ub = mb;
                } else {
                    lb = mb + 1;
                }
            }
            changes.RemoveRange(lb, changes.Count - lb);
        }

        public T Last() {
            return changes.Count == 0 ? initialValue : changes[changes.Count - 1].value;
        }

        public void AppendWith(int frame, Func<T, T> func) {
            ChangeTo(func(Last()), frame);
        }
    }
}