using System;

namespace UnityEngine {
    /// <summary>
    ///   <para>Representation of 3D vectors and points.</para>
    /// </summary>
    // Token: 0x02000070 RID: 112
    public struct Vector3 {
        /// <summary>
        ///   <para>Creates a new vector with given x, y, z components.</para>
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        // Token: 0x060008C4 RID: 2244 RVA: 0x0000ADC3 File Offset: 0x00008FC3
        public Vector3(float x, float y, float z) {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        /// <summary>
        ///   <para>Creates a new vector with given x, y components and sets z to zero.</para>
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        // Token: 0x060008C5 RID: 2245 RVA: 0x0000ADDB File Offset: 0x00008FDB
        public Vector3(float x, float y) {
            this.x = x;
            this.y = y;
            this.z = 0f;
        }

        // Token: 0x17000218 RID: 536
        public float this [int index] {
            get {
                float result;
                switch (index) {
                    case 0:
                        result = this.x;
                        break;
                    case 1:
                        result = this.y;
                        break;
                    case 2:
                        result = this.z;
                        break;
                    default:
                        throw new IndexOutOfRangeException("Invalid Vector3 index!");
                }
                return result;
            }
            set {
                switch (index) {
                    case 0:
                        this.x = value;
                        break;
                    case 1:
                        this.y = value;
                        break;
                    case 2:
                        this.z = value;
                        break;
                    default:
                        throw new IndexOutOfRangeException("Invalid Vector3 index!");
                }
            }
        }

        /// <summary>
        ///   <para>Set x, y and z components of an existing Vector3.</para>
        /// </summary>
        /// <param name="newX"></param>
        /// <param name="newY"></param>
        /// <param name="newZ"></param>
        // Token: 0x060008DB RID: 2267 RVA: 0x0000ADC3 File Offset: 0x00008FC3
        public void Set(float newX, float newY, float newZ) {
            this.x = newX;
            this.y = newY;
            this.z = newZ;
        }

        /// <summary>
        ///   <para>Multiplies two vectors component-wise.</para>
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        // Token: 0x060008DC RID: 2268 RVA: 0x0000B1E4 File Offset: 0x000093E4
        public static Vector3 Scale(Vector3 a, Vector3 b) {
            return new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        }

        /// <summary>
        ///   <para>Multiplies every component of this vector by the same component of scale.</para>
        /// </summary>
        /// <param name="scale"></param>
        // Token: 0x060008DD RID: 2269 RVA: 0x0000B22B File Offset: 0x0000942B
        public void Scale(Vector3 scale) {
            this.x *= scale.x;
            this.y *= scale.y;
            this.z *= scale.z;
        }

        /// <summary>
        ///   <para>Cross Product of two vectors.</para>
        /// </summary>
        /// <param name="lhs"></param>
        /// <param name="rhs"></param>
        // Token: 0x060008DE RID: 2270 RVA: 0x0000B26C File Offset: 0x0000946C
        public static Vector3 Cross(Vector3 lhs, Vector3 rhs) {
            return new Vector3(lhs.y * rhs.z - lhs.z * rhs.y, lhs.z * rhs.x - lhs.x * rhs.z, lhs.x * rhs.y - lhs.y * rhs.x);
        }

        // Token: 0x060008DF RID: 2271 RVA: 0x0000B2E4 File Offset: 0x000094E4
        public override int GetHashCode() {
            return this.x.GetHashCode() ^ this.y.GetHashCode() << 2 ^ this.z.GetHashCode() >> 2;
        }

        /// <summary>
        ///   <para>Returns true if the given vector is exactly equal to this vector.</para>
        /// </summary>
        /// <param name="other"></param>
        // Token: 0x060008E0 RID: 2272 RVA: 0x0000B334 File Offset: 0x00009534
        public override bool Equals(object other) {
            bool result;
            if (!(other is Vector3)) {
                result = false;
            } else {
                Vector3 vector = (Vector3) other;
                result = (this.x.Equals(vector.x) && this.y.Equals(vector.y) && this.z.Equals(vector.z));
            }
            return result;
        }

        /// <summary>
        ///   <para>Reflects a vector off the plane defined by a normal.</para>
        /// </summary>
        /// <param name="inDirection"></param>
        /// <param name="inNormal"></param>
        // Token: 0x060008E1 RID: 2273 RVA: 0x0000B3A8 File Offset: 0x000095A8
        public static Vector3 Reflect(Vector3 inDirection, Vector3 inNormal) {
            return -2f * Vector3.Dot(inNormal, inDirection) * inNormal + inDirection;
        }

        /// <summary>
        ///   <para>Dot Product of two vectors.</para>
        /// </summary>
        /// <param name="lhs"></param>
        /// <param name="rhs"></param>
        // Token: 0x060008E5 RID: 2277 RVA: 0x0000B47C File Offset: 0x0000967C
        public static float Dot(Vector3 lhs, Vector3 rhs) {
            return lhs.x * rhs.x + lhs.y * rhs.y + lhs.z * rhs.z;
        }

        // Token: 0x060008EE RID: 2286 RVA: 0x0000B718 File Offset: 0x00009918
        public static float SqrMagnitude(Vector3 vector) {
            return vector.x * vector.x + vector.y * vector.y + vector.z * vector.z;
        }

        /// <summary>
        ///   <para>Returns the squared length of this vector (Read Only).</para>
        /// </summary>
        // Token: 0x1700021B RID: 539
        // (get) Token: 0x060008EF RID: 2287 RVA: 0x0000B75C File Offset: 0x0000995C
        public float sqrMagnitude {
            get {
                return this.x * this.x + this.y * this.y + this.z * this.z;
            }
        }

        /// <summary>
        ///   <para>Shorthand for writing Vector3(0, 0, 0).</para>
        /// </summary>
        // Token: 0x1700021C RID: 540
        // (get) Token: 0x060008F2 RID: 2290 RVA: 0x0000B844 File Offset: 0x00009A44
        public static Vector3 zero {
            get {
                return Vector3.zeroVector;
            }
        }

        /// <summary>
        ///   <para>Shorthand for writing Vector3(1, 1, 1).</para>
        /// </summary>
        // Token: 0x1700021D RID: 541
        // (get) Token: 0x060008F3 RID: 2291 RVA: 0x0000B860 File Offset: 0x00009A60
        public static Vector3 one {
            get {
                return Vector3.oneVector;
            }
        }

        /// <summary>
        ///   <para>Shorthand for writing Vector3(0, 0, 1).</para>
        /// </summary>
        // Token: 0x1700021E RID: 542
        // (get) Token: 0x060008F4 RID: 2292 RVA: 0x0000B87C File Offset: 0x00009A7C
        public static Vector3 forward {
            get {
                return Vector3.forwardVector;
            }
        }

        /// <summary>
        ///   <para>Shorthand for writing Vector3(0, 0, -1).</para>
        /// </summary>
        // Token: 0x1700021F RID: 543
        // (get) Token: 0x060008F5 RID: 2293 RVA: 0x0000B898 File Offset: 0x00009A98
        public static Vector3 back {
            get {
                return Vector3.backVector;
            }
        }

        /// <summary>
        ///   <para>Shorthand for writing Vector3(0, 1, 0).</para>
        /// </summary>
        // Token: 0x17000220 RID: 544
        // (get) Token: 0x060008F6 RID: 2294 RVA: 0x0000B8B4 File Offset: 0x00009AB4
        public static Vector3 up {
            get {
                return Vector3.upVector;
            }
        }

        /// <summary>
        ///   <para>Shorthand for writing Vector3(0, -1, 0).</para>
        /// </summary>
        // Token: 0x17000221 RID: 545
        // (get) Token: 0x060008F7 RID: 2295 RVA: 0x0000B8D0 File Offset: 0x00009AD0
        public static Vector3 down {
            get {
                return Vector3.downVector;
            }
        }

        /// <summary>
        ///   <para>Shorthand for writing Vector3(-1, 0, 0).</para>
        /// </summary>
        // Token: 0x17000222 RID: 546
        // (get) Token: 0x060008F8 RID: 2296 RVA: 0x0000B8EC File Offset: 0x00009AEC
        public static Vector3 left {
            get {
                return Vector3.leftVector;
            }
        }

        /// <summary>
        ///   <para>Shorthand for writing Vector3(1, 0, 0).</para>
        /// </summary>
        // Token: 0x17000223 RID: 547
        // (get) Token: 0x060008F9 RID: 2297 RVA: 0x0000B908 File Offset: 0x00009B08
        public static Vector3 right {
            get {
                return Vector3.rightVector;
            }
        }

        /// <summary>
        ///   <para>Shorthand for writing Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity).</para>
        /// </summary>
        // Token: 0x17000224 RID: 548
        // (get) Token: 0x060008FA RID: 2298 RVA: 0x0000B924 File Offset: 0x00009B24
        public static Vector3 positiveInfinity {
            get {
                return Vector3.positiveInfinityVector;
            }
        }

        /// <summary>
        ///   <para>Shorthand for writing Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity).</para>
        /// </summary>
        // Token: 0x17000225 RID: 549
        // (get) Token: 0x060008FB RID: 2299 RVA: 0x0000B940 File Offset: 0x00009B40
        public static Vector3 negativeInfinity {
            get {
                return Vector3.negativeInfinityVector;
            }
        }

        // Token: 0x060008FC RID: 2300 RVA: 0x0000B95C File Offset: 0x00009B5C
        public static Vector3 operator +(Vector3 a, Vector3 b) {
            return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        }

        // Token: 0x060008FD RID: 2301 RVA: 0x0000B9A4 File Offset: 0x00009BA4
        public static Vector3 operator -(Vector3 a, Vector3 b) {
            return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        }

        // Token: 0x060008FE RID: 2302 RVA: 0x0000B9EC File Offset: 0x00009BEC
        public static Vector3 operator -(Vector3 a) {
            return new Vector3(-a.x, -a.y, -a.z);
        }

        // Token: 0x060008FF RID: 2303 RVA: 0x0000BA20 File Offset: 0x00009C20
        public static Vector3 operator *(Vector3 a, float d) {
            return new Vector3(a.x * d, a.y * d, a.z * d);
        }

        // Token: 0x06000900 RID: 2304 RVA: 0x0000BA58 File Offset: 0x00009C58
        public static Vector3 operator *(float d, Vector3 a) {
            return new Vector3(a.x * d, a.y * d, a.z * d);
        }

        // Token: 0x06000901 RID: 2305 RVA: 0x0000BA90 File Offset: 0x00009C90
        public static Vector3 operator /(Vector3 a, float d) {
            return new Vector3(a.x / d, a.y / d, a.z / d);
        }

        // Token: 0x06000902 RID: 2306 RVA: 0x0000BAC8 File Offset: 0x00009CC8
        public static bool operator ==(Vector3 lhs, Vector3 rhs) {
            return Vector3.SqrMagnitude(lhs - rhs) < 9.99999944E-11f;
        }

        // Token: 0x06000903 RID: 2307 RVA: 0x0000BAF0 File Offset: 0x00009CF0
        public static bool operator !=(Vector3 lhs, Vector3 rhs) {
            return !(lhs == rhs);
        }

        // Token: 0x040000BD RID: 189
        public const float kEpsilon = 1E-05f;

        /// <summary>
        ///   <para>X component of the vector.</para>
        /// </summary>
        // Token: 0x040000BE RID: 190
        public float x;

        /// <summary>
        ///   <para>Y component of the vector.</para>
        /// </summary>
        // Token: 0x040000BF RID: 191
        public float y;

        /// <summary>
        ///   <para>Z component of the vector.</para>
        /// </summary>
        // Token: 0x040000C0 RID: 192
        public float z;

        // Token: 0x040000C1 RID: 193
        private static readonly Vector3 zeroVector = new Vector3(0f, 0f, 0f);

        // Token: 0x040000C2 RID: 194
        private static readonly Vector3 oneVector = new Vector3(1f, 1f, 1f);

        // Token: 0x040000C3 RID: 195
        private static readonly Vector3 upVector = new Vector3(0f, 1f, 0f);

        // Token: 0x040000C4 RID: 196
        private static readonly Vector3 downVector = new Vector3(0f, -1f, 0f);

        // Token: 0x040000C5 RID: 197
        private static readonly Vector3 leftVector = new Vector3(-1f, 0f, 0f);

        // Token: 0x040000C6 RID: 198
        private static readonly Vector3 rightVector = new Vector3(1f, 0f, 0f);

        // Token: 0x040000C7 RID: 199
        private static readonly Vector3 forwardVector = new Vector3(0f, 0f, 1f);

        // Token: 0x040000C8 RID: 200
        private static readonly Vector3 backVector = new Vector3(0f, 0f, -1f);

        // Token: 0x040000C9 RID: 201
        private static readonly Vector3 positiveInfinityVector = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);

        // Token: 0x040000CA RID: 202
        private static readonly Vector3 negativeInfinityVector = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
    }

    public struct Vector2 {
        /// <summary>
        ///   <para>X component of the vector.</para>
        /// </summary>
        // Token: 0x040000BE RID: 190
        public float x;

        /// <summary>
        ///   <para>Y component of the vector.</para>
        /// </summary>
        // Token: 0x040000BF RID: 191
        public float y;

        public Vector2(float x, float y) {
            this.x = x;
            this.y = y;
        }
    }

    public struct Quaternion {
        /// <summary>
        ///   <para>Constructs new Quaternion with given x,y,z,w components.</para>
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        /// <param name="w"></param>
        // Token: 0x06000909 RID: 2313 RVA: 0x0000BD23 File Offset: 0x00009F23
        public Quaternion(float x, float y, float z, float w) {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;

        }
        /// <summary>
        ///   <para>X component of the Quaternion. Don't modify this directly unless you know quaternions inside out.</para>
        /// </summary>
        // Token: 0x040000CB RID: 203
        public float x;

        /// <summary>
        ///   <para>Y component of the Quaternion. Don't modify this directly unless you know quaternions inside out.</para>
        /// </summary>
        // Token: 0x040000CC RID: 204
        public float y;

        /// <summary>
        ///   <para>Z component of the Quaternion. Don't modify this directly unless you know quaternions inside out.</para>
        /// </summary>
        // Token: 0x040000CD RID: 205
        public float z;

        /// <summary>
        ///   <para>W component of the Quaternion. Don't modify this directly unless you know quaternions inside out.</para>
        /// </summary>
        // Token: 0x040000CE RID: 206
        public float w;
    }
}