using System;
using System.Collections.Generic;

namespace Hpmv {
    public class Versioned<T> {
        private T initialValue;

        public Versioned(T initialValue) {
            this.initialValue = initialValue;
        }

        private List < (int time, T value) > changes = new List < (int, T) > ();

        public void ChangeTo(T value, int time) {
            if (changes.Count > 0 && changes[changes.Count - 1].time >= time) {
                throw new ArgumentException($"Cannot go backwards in time: {time}", "time");
            }
            changes.Add((time, value));
        }

        public T this [int index] {
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
    }
}