using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BitStream {
    // Token: 0x02000926 RID: 2342
    public class BitStreamWriter {
        // Token: 0x06002C5E RID: 11358 RVA: 0x0001C914 File Offset: 0x0001AB14
        public BitStreamWriter(FastList<byte> bufferToWriteTo) {
            if (bufferToWriteTo == null) {
                throw new ArgumentNullException("bufferToWriteTo");
            }
            this._targetBuffer = bufferToWriteTo;
        }

        public BitStreamWriter()
        {
        }

        // Token: 0x06002C5F RID: 11359 RVA: 0x0001C934 File Offset: 0x0001AB34
        public void Reset(FastList<byte> bufferToWriteTo) {
            this._targetBuffer = bufferToWriteTo;
            this._remaining = 0;
        }

        // Token: 0x06002C60 RID: 11360 RVA: 0x000C8FDC File Offset: 0x000C71DC
        public void Write(string _text, Encoding _encoding) {
            byte[] bytes = _encoding.GetBytes(_text);
            if (bytes.Length * 8 + 10 > this.GetRemainingBitCount()) {
                this.Write(0U, 10);
                return;
            }
            this.Write((uint) bytes.Length, 10);
            this.Write(bytes, 8 * bytes.Length);
        }

        // Token: 0x06002C61 RID: 11361 RVA: 0x000C9028 File Offset: 0x000C7228
        public void Write(ref Quaternion value) {
            Write(value.x);
            Write(value.y);
            Write(value.z);
            Write(value.w);
        }

        // Token: 0x06002C62 RID: 11362 RVA: 0x000C9090 File Offset: 0x000C7290
        public void Write(ref Vector3 value) {
            Write(value.x);
            Write(value.y);
            Write(value.z);
        }

        // Token: 0x06002C63 RID: 11363 RVA: 0x000C90E4 File Offset: 0x000C72E4
        public void Write(ref Vector2 value) {
            Write(value.x);
            Write(value.y);
        }

        // Token: 0x06002C64 RID: 11364 RVA: 0x000C9120 File Offset: 0x000C7320
        public void Write(double value) {
            Write(unchecked((ulong)BitConverter.DoubleToInt64Bits(value)), 64);
        }

        // Token: 0x06002C65 RID: 11365 RVA: 0x000C9140 File Offset: 0x000C7340
        public void Write(float value) {
            Write(unchecked((uint)BitConverter.SingleToInt32Bits(value)), 32);
        }

        // Token: 0x06002C66 RID: 11366 RVA: 0x0001C944 File Offset: 0x0001AB44
        public void Write(bool value) {
            this.Write((!value) ? (byte) 0 : byte.MaxValue, 1);
        }

        // Token: 0x06002C67 RID: 11367 RVA: 0x000C9160 File Offset: 0x000C7360
        public void Write(byte[] bytes, int countOfBits) {
            if (bytes == null || countOfBits <= 0 || countOfBits > bytes.Length << 3) {
                return;
            }
            int num = countOfBits / 8;
            int num2 = countOfBits % 8;
            int i;
            for (i = 0; i < num; i++) {
                this.Write(bytes[i], 8);
            }
            if (num2 > 0) {
                this.Write(bytes[i], num2);
            }
        }

        // Token: 0x06002C68 RID: 11368 RVA: 0x000C91BC File Offset: 0x000C73BC
        public void Write(ulong bits, int countOfBits) {
            if (countOfBits <= 0 || countOfBits > 64) {
                return;
            }
            int i = countOfBits / 8;
            int num = countOfBits % 8;
            while (i >= 0) {
                byte bits2 = (byte) (bits >> i * 8);
                if (num > 0) {
                    this.Write(bits2, num);
                }
                if (i > 0) {
                    num = 8;
                }
                i--;
            }
        }

        // Token: 0x06002C69 RID: 11369 RVA: 0x000C9214 File Offset: 0x000C7414
        public void Write(uint bits, int countOfBits) {
            if (countOfBits <= 0 || countOfBits > 32) {
                return;
            }
            int i = countOfBits / 8;
            int num = countOfBits % 8;
            while (i >= 0) {
                byte bits2 = (byte) (bits >> i * 8);
                if (num > 0) {
                    this.Write(bits2, num);
                }
                if (i > 0) {
                    num = 8;
                }
                i--;
            }
        }

        // Token: 0x06002C6A RID: 11370 RVA: 0x000C926C File Offset: 0x000C746C
        public void WriteReverse(uint bits, int countOfBits) {
            if (countOfBits <= 0 || countOfBits > 32) {
                return;
            }
            int num = countOfBits / 8;
            int num2 = countOfBits % 8;
            if (num2 > 0) {
                num++;
            }
            for (int i = 0; i < num; i++) {
                byte bits2 = (byte) (bits >> i * 8);
                this.Write(bits2, 8);
            }
        }

        // Token: 0x06002C6B RID: 11371 RVA: 0x000C92C0 File Offset: 0x000C74C0
        public void Write(byte bits, int countOfBits) {
            if (countOfBits <= 0 || countOfBits > 8) {
                return;
            }
            if (this._remaining > 0) {
                byte b = this._targetBuffer._items[this._targetBuffer.Count - 1];
                if (countOfBits > this._remaining) {
                    b |= (byte) (((int) bits & 255 >> 8 - countOfBits) >> countOfBits - this._remaining);
                } else {
                    b |= (byte) (((int) bits & 255 >> 8 - countOfBits) << this._remaining - countOfBits);
                }
                this._targetBuffer._items[this._targetBuffer.Count - 1] = b;
            }
            if (countOfBits > this._remaining) {
                this._remaining = 8 - (countOfBits - this._remaining);
                byte b = (byte) (bits << this._remaining);
                this._targetBuffer.Add(b);
            } else {
                this._remaining -= countOfBits;
            }
        }

        // Token: 0x06002C6C RID: 11372 RVA: 0x0001C95E File Offset: 0x0001AB5E
        public int GetUsedBitCount() {
            return this._targetBuffer.Count * 8 - this._remaining;
        }

        // Token: 0x06002C6D RID: 11373 RVA: 0x0001C974 File Offset: 0x0001AB74
        public int GetRemainingBitCount() {
            return (this._targetBuffer.Capacity - this._targetBuffer.Count) * 8 + this._remaining;
        }

        // Token: 0x040024D4 RID: 9428
        private FastList<byte> _targetBuffer;

        // Token: 0x040024D5 RID: 9429
        private int _remaining;
    }
}