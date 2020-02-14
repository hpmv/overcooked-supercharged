using System;
using UnityEngine;

namespace Hpmv {
    public class MathUtils {
        public static float SinusoidalSCurve(float _prop) {
            float num = Mathf.Clamp(_prop, 0f, 1f);
            return 0.5f * (1f - Mathf.Cos(3.14159274f * num));
        }
    }
}