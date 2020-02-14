using System;
using UnityEngineInternal;

namespace UnityEngine {
    /// <summary>
    ///   <para>A collection of common math functions.</para>
    /// </summary>
    // Token: 0x02000075 RID: 117
    public struct Mathf {
        /// <summary>
        ///   <para>Returns the sine of angle f.</para>
        /// </summary>
        /// <param name="f">The input angle, in radians.</param>
        /// <returns>
        ///   <para>The return value between -1 and +1.</para>
        /// </returns>
        // Token: 0x060009B2 RID: 2482 RVA: 0x0000E7A0 File Offset: 0x0000C9A0
        public static float Sin(float f) {
            return (float) Math.Sin((double) f);
        }

        /// <summary>
        ///   <para>Returns the cosine of angle f.</para>
        /// </summary>
        /// <param name="f">The input angle, in radians.</param>
        /// <returns>
        ///   <para>The return value between -1 and 1.</para>
        /// </returns>
        // Token: 0x060009B3 RID: 2483 RVA: 0x0000E7C0 File Offset: 0x0000C9C0
        public static float Cos(float f) {
            return (float) Math.Cos((double) f);
        }

        /// <summary>
        ///   <para>Returns the tangent of angle f in radians.</para>
        /// </summary>
        /// <param name="f"></param>
        // Token: 0x060009B4 RID: 2484 RVA: 0x0000E7E0 File Offset: 0x0000C9E0
        public static float Tan(float f) {
            return (float) Math.Tan((double) f);
        }

        /// <summary>
        ///   <para>Returns the arc-sine of f - the angle in radians whose sine is f.</para>
        /// </summary>
        /// <param name="f"></param>
        // Token: 0x060009B5 RID: 2485 RVA: 0x0000E800 File Offset: 0x0000CA00
        public static float Asin(float f) {
            return (float) Math.Asin((double) f);
        }

        /// <summary>
        ///   <para>Returns the arc-cosine of f - the angle in radians whose cosine is f.</para>
        /// </summary>
        /// <param name="f"></param>
        // Token: 0x060009B6 RID: 2486 RVA: 0x0000E820 File Offset: 0x0000CA20
        public static float Acos(float f) {
            return (float) Math.Acos((double) f);
        }

        /// <summary>
        ///   <para>Returns the arc-tangent of f - the angle in radians whose tangent is f.</para>
        /// </summary>
        /// <param name="f"></param>
        // Token: 0x060009B7 RID: 2487 RVA: 0x0000E840 File Offset: 0x0000CA40
        public static float Atan(float f) {
            return (float) Math.Atan((double) f);
        }

        /// <summary>
        ///   <para>Returns the angle in radians whose Tan is y/x.</para>
        /// </summary>
        /// <param name="y"></param>
        /// <param name="x"></param>
        // Token: 0x060009B8 RID: 2488 RVA: 0x0000E860 File Offset: 0x0000CA60
        public static float Atan2(float y, float x) {
            return (float) Math.Atan2((double) y, (double) x);
        }

        /// <summary>
        ///   <para>Returns square root of f.</para>
        /// </summary>
        /// <param name="f"></param>
        // Token: 0x060009B9 RID: 2489 RVA: 0x0000E880 File Offset: 0x0000CA80
        public static float Sqrt(float f) {
            return (float) Math.Sqrt((double) f);
        }

        /// <summary>
        ///   <para>Returns the absolute value of f.</para>
        /// </summary>
        /// <param name="f"></param>
        // Token: 0x060009BA RID: 2490 RVA: 0x0000E8A0 File Offset: 0x0000CAA0
        public static float Abs(float f) {
            return Math.Abs(f);
        }

        /// <summary>
        ///   <para>Returns the absolute value of value.</para>
        /// </summary>
        /// <param name="value"></param>
        // Token: 0x060009BB RID: 2491 RVA: 0x0000E8BC File Offset: 0x0000CABC
        public static int Abs(int value) {
            return Math.Abs(value);
        }

        /// <summary>
        ///   <para>Returns the smallest of two or more values.</para>
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="values"></param>
        // Token: 0x060009BC RID: 2492 RVA: 0x0000E8D8 File Offset: 0x0000CAD8
        public static float Min(float a, float b) {
            return (a >= b) ? b : a;
        }

        /// <summary>
        ///   <para>Returns the smallest of two or more values.</para>
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="values"></param>
        // Token: 0x060009BD RID: 2493 RVA: 0x0000E8FC File Offset: 0x0000CAFC
        public static float Min(params float[] values) {
            int num = values.Length;
            float result;
            if (num == 0) {
                result = 0f;
            } else {
                float num2 = values[0];
                for (int i = 1; i < num; i++) {
                    if (values[i] < num2) {
                        num2 = values[i];
                    }
                }
                result = num2;
            }
            return result;
        }

        /// <summary>
        ///   <para>Returns the smallest of two or more values.</para>
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="values"></param>
        // Token: 0x060009BE RID: 2494 RVA: 0x0000E94C File Offset: 0x0000CB4C
        public static int Min(int a, int b) {
            return (a >= b) ? b : a;
        }

        /// <summary>
        ///   <para>Returns the smallest of two or more values.</para>
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="values"></param>
        // Token: 0x060009BF RID: 2495 RVA: 0x0000E970 File Offset: 0x0000CB70
        public static int Min(params int[] values) {
            int num = values.Length;
            int result;
            if (num == 0) {
                result = 0;
            } else {
                int num2 = values[0];
                for (int i = 1; i < num; i++) {
                    if (values[i] < num2) {
                        num2 = values[i];
                    }
                }
                result = num2;
            }
            return result;
        }

        /// <summary>
        ///   <para>Returns largest of two or more values.</para>
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="values"></param>
        // Token: 0x060009C0 RID: 2496 RVA: 0x0000E9BC File Offset: 0x0000CBBC
        public static float Max(float a, float b) {
            return (a <= b) ? b : a;
        }

        /// <summary>
        ///   <para>Returns largest of two or more values.</para>
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="values"></param>
        // Token: 0x060009C1 RID: 2497 RVA: 0x0000E9E0 File Offset: 0x0000CBE0
        public static float Max(params float[] values) {
            int num = values.Length;
            float result;
            if (num == 0) {
                result = 0f;
            } else {
                float num2 = values[0];
                for (int i = 1; i < num; i++) {
                    if (values[i] > num2) {
                        num2 = values[i];
                    }
                }
                result = num2;
            }
            return result;
        }

        /// <summary>
        ///   <para>Returns the largest of two or more values.</para>
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="values"></param>
        // Token: 0x060009C2 RID: 2498 RVA: 0x0000EA30 File Offset: 0x0000CC30
        public static int Max(int a, int b) {
            return (a <= b) ? b : a;
        }

        /// <summary>
        ///   <para>Returns the largest of two or more values.</para>
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="values"></param>
        // Token: 0x060009C3 RID: 2499 RVA: 0x0000EA54 File Offset: 0x0000CC54
        public static int Max(params int[] values) {
            int num = values.Length;
            int result;
            if (num == 0) {
                result = 0;
            } else {
                int num2 = values[0];
                for (int i = 1; i < num; i++) {
                    if (values[i] > num2) {
                        num2 = values[i];
                    }
                }
                result = num2;
            }
            return result;
        }

        /// <summary>
        ///   <para>Returns f raised to power p.</para>
        /// </summary>
        /// <param name="f"></param>
        /// <param name="p"></param>
        // Token: 0x060009C4 RID: 2500 RVA: 0x0000EAA0 File Offset: 0x0000CCA0
        public static float Pow(float f, float p) {
            return (float) Math.Pow((double) f, (double) p);
        }

        /// <summary>
        ///   <para>Returns e raised to the specified power.</para>
        /// </summary>
        /// <param name="power"></param>
        // Token: 0x060009C5 RID: 2501 RVA: 0x0000EAC0 File Offset: 0x0000CCC0
        public static float Exp(float power) {
            return (float) Math.Exp((double) power);
        }

        /// <summary>
        ///   <para>Returns the logarithm of a specified number in a specified base.</para>
        /// </summary>
        /// <param name="f"></param>
        /// <param name="p"></param>
        // Token: 0x060009C6 RID: 2502 RVA: 0x0000EAE0 File Offset: 0x0000CCE0
        public static float Log(float f, float p) {
            return (float) Math.Log((double) f, (double) p);
        }

        /// <summary>
        ///   <para>Returns the natural (base e) logarithm of a specified number.</para>
        /// </summary>
        /// <param name="f"></param>
        // Token: 0x060009C7 RID: 2503 RVA: 0x0000EB00 File Offset: 0x0000CD00
        public static float Log(float f) {
            return (float) Math.Log((double) f);
        }

        /// <summary>
        ///   <para>Returns the base 10 logarithm of a specified number.</para>
        /// </summary>
        /// <param name="f"></param>
        // Token: 0x060009C8 RID: 2504 RVA: 0x0000EB20 File Offset: 0x0000CD20
        public static float Log10(float f) {
            return (float) Math.Log10((double) f);
        }

        /// <summary>
        ///   <para>Returns the smallest integer greater to or equal to f.</para>
        /// </summary>
        /// <param name="f"></param>
        // Token: 0x060009C9 RID: 2505 RVA: 0x0000EB40 File Offset: 0x0000CD40
        public static float Ceil(float f) {
            return (float) Math.Ceiling((double) f);
        }

        /// <summary>
        ///   <para>Returns the largest integer smaller to or equal to f.</para>
        /// </summary>
        /// <param name="f"></param>
        // Token: 0x060009CA RID: 2506 RVA: 0x0000EB60 File Offset: 0x0000CD60
        public static float Floor(float f) {
            return (float) Math.Floor((double) f);
        }

        /// <summary>
        ///   <para>Returns f rounded to the nearest integer.</para>
        /// </summary>
        /// <param name="f"></param>
        // Token: 0x060009CB RID: 2507 RVA: 0x0000EB80 File Offset: 0x0000CD80
        public static float Round(float f) {
            return (float) Math.Round((double) f);
        }

        /// <summary>
        ///   <para>Returns the smallest integer greater to or equal to f.</para>
        /// </summary>
        /// <param name="f"></param>
        // Token: 0x060009CC RID: 2508 RVA: 0x0000EBA0 File Offset: 0x0000CDA0
        public static int CeilToInt(float f) {
            return (int) Math.Ceiling((double) f);
        }

        /// <summary>
        ///   <para>Returns the largest integer smaller to or equal to f.</para>
        /// </summary>
        /// <param name="f"></param>
        // Token: 0x060009CD RID: 2509 RVA: 0x0000EBC0 File Offset: 0x0000CDC0
        public static int FloorToInt(float f) {
            return (int) Math.Floor((double) f);
        }

        /// <summary>
        ///   <para>Returns f rounded to the nearest integer.</para>
        /// </summary>
        /// <param name="f"></param>
        // Token: 0x060009CE RID: 2510 RVA: 0x0000EBE0 File Offset: 0x0000CDE0
        public static int RoundToInt(float f) {
            return (int) Math.Round((double) f);
        }

        /// <summary>
        ///   <para>Returns the sign of f.</para>
        /// </summary>
        /// <param name="f"></param>
        // Token: 0x060009CF RID: 2511 RVA: 0x0000EC00 File Offset: 0x0000CE00
        public static float Sign(float f) {
            return (f < 0f) ? -1f : 1f;
        }

        /// <summary>
        ///   <para>Clamps a value between a minimum float and maximum float value.</para>
        /// </summary>
        /// <param name="value"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        // Token: 0x060009D0 RID: 2512 RVA: 0x0000EC30 File Offset: 0x0000CE30
        public static float Clamp(float value, float min, float max) {
            if (value < min) {
                value = min;
            } else if (value > max) {
                value = max;
            }
            return value;
        }

        /// <summary>
        ///   <para>Clamps value between min and max and returns value.</para>
        /// </summary>
        /// <param name="value"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        // Token: 0x060009D1 RID: 2513 RVA: 0x0000EC60 File Offset: 0x0000CE60
        public static int Clamp(int value, int min, int max) {
            if (value < min) {
                value = min;
            } else if (value > max) {
                value = max;
            }
            return value;
        }

        /// <summary>
        ///   <para>Clamps value between 0 and 1 and returns value.</para>
        /// </summary>
        /// <param name="value"></param>
        // Token: 0x060009D2 RID: 2514 RVA: 0x0000EC90 File Offset: 0x0000CE90
        public static float Clamp01(float value) {
            float result;
            if (value < 0f) {
                result = 0f;
            } else if (value > 1f) {
                result = 1f;
            } else {
                result = value;
            }
            return result;
        }

        /// <summary>
        ///   <para>Linearly interpolates between a and b by t.</para>
        /// </summary>
        /// <param name="a">The start value.</param>
        /// <param name="b">The end value.</param>
        /// <param name="t">The interpolation value between the two floats.</param>
        /// <returns>
        ///   <para>The interpolated float result between the two float values.</para>
        /// </returns>
        // Token: 0x060009D3 RID: 2515 RVA: 0x0000ECD4 File Offset: 0x0000CED4
        public static float Lerp(float a, float b, float t) {
            return a + (b - a) * Mathf.Clamp01(t);
        }

        /// <summary>
        ///   <para>Linearly interpolates between a and b by t with no limit to t.</para>
        /// </summary>
        /// <param name="a">The start value.</param>
        /// <param name="b">The end value.</param>
        /// <param name="t">The interpolation between the two floats.</param>
        /// <returns>
        ///   <para>The float value as a result from the linear interpolation.</para>
        /// </returns>
        // Token: 0x060009D4 RID: 2516 RVA: 0x0000ECF8 File Offset: 0x0000CEF8
        public static float LerpUnclamped(float a, float b, float t) {
            return a + (b - a) * t;
        }

        /// <summary>
        ///   <para>Same as Lerp but makes sure the values interpolate correctly when they wrap around 360 degrees.</para>
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="t"></param>
        // Token: 0x060009D5 RID: 2517 RVA: 0x0000ED14 File Offset: 0x0000CF14
        public static float LerpAngle(float a, float b, float t) {
            float num = Mathf.Repeat(b - a, 360f);
            if (num > 180f) {
                num -= 360f;
            }
            return a + num * Mathf.Clamp01(t);
        }

        /// <summary>
        ///   <para>Moves a value current towards target.</para>
        /// </summary>
        /// <param name="current">The current value.</param>
        /// <param name="target">The value to move towards.</param>
        /// <param name="maxDelta">The maximum change that should be applied to the value.</param>
        // Token: 0x060009D6 RID: 2518 RVA: 0x0000ED54 File Offset: 0x0000CF54
        public static float MoveTowards(float current, float target, float maxDelta) {
            float result;
            if (Mathf.Abs(target - current) <= maxDelta) {
                result = target;
            } else {
                result = current + Mathf.Sign(target - current) * maxDelta;
            }
            return result;
        }

        /// <summary>
        ///   <para>Same as MoveTowards but makes sure the values interpolate correctly when they wrap around 360 degrees.</para>
        /// </summary>
        /// <param name="current"></param>
        /// <param name="target"></param>
        /// <param name="maxDelta"></param>
        // Token: 0x060009D7 RID: 2519 RVA: 0x0000ED8C File Offset: 0x0000CF8C
        public static float MoveTowardsAngle(float current, float target, float maxDelta) {
            float num = Mathf.DeltaAngle(current, target);
            float result;
            if (-maxDelta < num && num < maxDelta) {
                result = target;
            } else {
                target = current + num;
                result = Mathf.MoveTowards(current, target, maxDelta);
            }
            return result;
        }

        /// <summary>
        ///   <para>Interpolates between min and max with smoothing at the limits.</para>
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <param name="t"></param>
        // Token: 0x060009D8 RID: 2520 RVA: 0x0000EDCC File Offset: 0x0000CFCC
        public static float SmoothStep(float from, float to, float t) {
            t = Mathf.Clamp01(t);
            t = -2f * t * t * t + 3f * t * t;
            return to * t + from * (1f - t);
        }

        // Token: 0x060009D9 RID: 2521 RVA: 0x0000EE10 File Offset: 0x0000D010
        public static float Gamma(float value, float absmax, float gamma) {
            bool flag = false;
            if (value < 0f) {
                flag = true;
            }
            float num = Mathf.Abs(value);
            float result;
            if (num > absmax) {
                result = ((!flag) ? num : (-num));
            } else {
                float num2 = Mathf.Pow(num / absmax, gamma) * absmax;
                result = ((!flag) ? num2 : (-num2));
            }
            return result;
        }

        /// <summary>
        ///   <para>Compares two floating point values and returns true if they are similar.</para>
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        // Token: 0x060009DA RID: 2522 RVA: 0x0000EE70 File Offset: 0x0000D070
        public static bool Approximately(float a, float b) {
            return Mathf.Abs(b - a) < Mathf.Max(1E-06f * Mathf.Max(Mathf.Abs(a), Mathf.Abs(b)), Mathf.Epsilon * 8f);
        }

        // Token: 0x060009DD RID: 2525 RVA: 0x0000EF0C File Offset: 0x0000D10C
        public static float SmoothDamp(float current, float target, ref float currentVelocity, float smoothTime, float maxSpeed, float deltaTime) {
            smoothTime = Mathf.Max(0.0001f, smoothTime);
            float num = 2f / smoothTime;
            float num2 = num * deltaTime;
            float num3 = 1f / (1f + num2 + 0.48f * num2 * num2 + 0.235f * num2 * num2 * num2);
            float num4 = current - target;
            float num5 = target;
            float num6 = maxSpeed * smoothTime;
            num4 = Mathf.Clamp(num4, -num6, num6);
            target = current - num4;
            float num7 = (currentVelocity + num * num4) * deltaTime;
            currentVelocity = (currentVelocity - num * num7) * num3;
            float num8 = target + (num4 + num7) * num3;
            if (num5 - current > 0f == num8 > num5) {
                num8 = num5;
                currentVelocity = (num8 - num5) / deltaTime;
            }
            return num8;
        }

        // Token: 0x060009E0 RID: 2528 RVA: 0x0000F01C File Offset: 0x0000D21C
        public static float SmoothDampAngle(float current, float target, ref float currentVelocity, float smoothTime, float maxSpeed, float deltaTime) {
            target = current + Mathf.DeltaAngle(current, target);
            return Mathf.SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        }

        /// <summary>
        ///   <para>Loops the value t, so that it is never larger than length and never smaller than 0.</para>
        /// </summary>
        /// <param name="t"></param>
        /// <param name="length"></param>
        // Token: 0x060009E1 RID: 2529 RVA: 0x0000F04C File Offset: 0x0000D24C
        public static float Repeat(float t, float length) {
            return Mathf.Clamp(t - Mathf.Floor(t / length) * length, 0f, length);
        }

        /// <summary>
        ///   <para>PingPongs the value t, so that it is never larger than length and never smaller than 0.</para>
        /// </summary>
        /// <param name="t"></param>
        /// <param name="length"></param>
        // Token: 0x060009E2 RID: 2530 RVA: 0x0000F078 File Offset: 0x0000D278
        public static float PingPong(float t, float length) {
            t = Mathf.Repeat(t, length * 2f);
            return length - Mathf.Abs(t - length);
        }

        /// <summary>
        ///   <para>Calculates the linear parameter t that produces the interpolant value within the range [a, b].</para>
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="value"></param>
        // Token: 0x060009E3 RID: 2531 RVA: 0x0000F0A8 File Offset: 0x0000D2A8
        public static float InverseLerp(float a, float b, float value) {
            float result;
            if (a != b) {
                result = Mathf.Clamp01((value - a) / (b - a));
            } else {
                result = 0f;
            }
            return result;
        }

        /// <summary>
        ///   <para>Calculates the shortest difference between two given angles given in degrees.</para>
        /// </summary>
        /// <param name="current"></param>
        /// <param name="target"></param>
        // Token: 0x060009E4 RID: 2532 RVA: 0x0000F0DC File Offset: 0x0000D2DC
        public static float DeltaAngle(float current, float target) {
            float num = Mathf.Repeat(target - current, 360f);
            if (num > 180f) {
                num -= 360f;
            }
            return num;
        }

        // Token: 0x060009E5 RID: 2533 RVA: 0x0000F114 File Offset: 0x0000D314
        internal static bool LineIntersection(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, ref Vector2 result) {
            float num = p2.x - p1.x;
            float num2 = p2.y - p1.y;
            float num3 = p4.x - p3.x;
            float num4 = p4.y - p3.y;
            float num5 = num * num4 - num2 * num3;
            bool result2;
            if (num5 == 0f) {
                result2 = false;
            } else {
                float num6 = p3.x - p1.x;
                float num7 = p3.y - p1.y;
                float num8 = (num6 * num4 - num7 * num3) / num5;
                result = new Vector2(p1.x + num8 * num, p1.y + num8 * num2);
                result2 = true;
            }
            return result2;
        }

        // Token: 0x060009E6 RID: 2534 RVA: 0x0000F1DC File Offset: 0x0000D3DC
        internal static bool LineSegmentIntersection(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, ref Vector2 result) {
            float num = p2.x - p1.x;
            float num2 = p2.y - p1.y;
            float num3 = p4.x - p3.x;
            float num4 = p4.y - p3.y;
            float num5 = num * num4 - num2 * num3;
            bool result2;
            if (num5 == 0f) {
                result2 = false;
            } else {
                float num6 = p3.x - p1.x;
                float num7 = p3.y - p1.y;
                float num8 = (num6 * num4 - num7 * num3) / num5;
                if (num8 < 0f || num8 > 1f) {
                    result2 = false;
                } else {
                    float num9 = (num6 * num2 - num7 * num) / num5;
                    if (num9 < 0f || num9 > 1f) {
                        result2 = false;
                    } else {
                        result = new Vector2(p1.x + num8 * num, p1.y + num8 * num2);
                        result2 = true;
                    }
                }
            }
            return result2;
        }

        // Token: 0x060009E7 RID: 2535 RVA: 0x0000F2F4 File Offset: 0x0000D4F4
        internal static long RandomToLong(Random r) {
            byte[] array = new byte[8];
            r.NextBytes(array);
            return (long) (BitConverter.ToUInt64(array, 0) & 9223372036854775807UL);
        }

        /// <summary>
        ///   <para>The infamous 3.14159265358979... value (Read Only).</para>
        /// </summary>
        // Token: 0x040000EB RID: 235
        public const float PI = 3.14159274f;

        /// <summary>
        ///   <para>A representation of positive infinity (Read Only).</para>
        /// </summary>
        // Token: 0x040000EC RID: 236
        public const float Infinity = float.PositiveInfinity;

        /// <summary>
        ///   <para>A representation of negative infinity (Read Only).</para>
        /// </summary>
        // Token: 0x040000ED RID: 237
        public const float NegativeInfinity = float.NegativeInfinity;

        /// <summary>
        ///   <para>Degrees-to-radians conversion constant (Read Only).</para>
        /// </summary>
        // Token: 0x040000EE RID: 238
        public const float Deg2Rad = 0.0174532924f;

        /// <summary>
        ///   <para>Radians-to-degrees conversion constant (Read Only).</para>
        /// </summary>
        // Token: 0x040000EF RID: 239
        public const float Rad2Deg = 57.29578f;

        /// <summary>
        ///   <para>A tiny floating point value (Read Only).</para>
        /// </summary>
        // Token: 0x040000F0 RID: 240
        public static readonly float Epsilon = (!MathfInternal.IsFlushToZeroEnabled) ? MathfInternal.FloatMinDenormal : MathfInternal.FloatMinNormal;
    }
}

namespace UnityEngineInternal {
    // Token: 0x020001B3 RID: 435
    public struct MathfInternal {
        // Token: 0x04000751 RID: 1873
        public static volatile float FloatMinNormal = 1.17549435E-38f;

        // Token: 0x04000752 RID: 1874
        public static volatile float FloatMinDenormal = float.Epsilon;

        // Token: 0x04000753 RID: 1875
        public static bool IsFlushToZeroEnabled = MathfInternal.FloatMinDenormal == 0f;
    }
}