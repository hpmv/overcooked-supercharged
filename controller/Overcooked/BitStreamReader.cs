using System;
using System.Text;
using UnityEngine;

namespace BitStream {
    // Token: 0x02000925 RID: 2341
    public class BitStreamReader {
        // Token: 0x06002C48 RID: 11336 RVA: 0x0001C823 File Offset: 0x0001AA23
        public BitStreamReader(byte[] buffer) {
            this._byteArray = buffer;
            this._bufferLengthInBits = (uint) (buffer.Length * 8);
        }

        // Token: 0x06002C49 RID: 11337 RVA: 0x0001C83D File Offset: 0x0001AA3D
        public BitStreamReader(byte[] buffer, int startIndex) {
            if (startIndex < 0 || startIndex >= buffer.Length) {
                throw new ArgumentOutOfRangeException("startIndex");
            }
            this._byteArray = buffer;
            this._byteArrayIndex = startIndex;
            this._bufferLengthInBits = (uint) ((buffer.Length - startIndex) * 8);
        }

        // Token: 0x06002C4A RID: 11338 RVA: 0x0001C87B File Offset: 0x0001AA7B
        public BitStreamReader(byte[] buffer, uint bufferLengthInBits) : this(buffer) {
            if ((ulong) bufferLengthInBits > (ulong) ((long) (buffer.Length * 8))) {
                return;
            }
            this._bufferLengthInBits = bufferLengthInBits;
        }

        // Token: 0x06002C4B RID: 11339 RVA: 0x0001C899 File Offset: 0x0001AA99
        public void Reset(byte[] buffer) {
            this._byteArray = buffer;
            this._bufferLengthInBits = (uint) (buffer.Length * 8);
            this._byteArrayIndex = 0;
            this._partialByte = 0;
            this._cbitsInPartialByte = 0;
        }

        // Token: 0x06002C4C RID: 11340 RVA: 0x0001C8C2 File Offset: 0x0001AAC2
        public void Reset(byte[] buffer, int usedLength) {
            this.Reset(buffer);
            this._bufferLengthInBits = (uint) (usedLength * 8);
        }

        // Token: 0x06002C4D RID: 11341 RVA: 0x0001C8D4 File Offset: 0x0001AAD4
        public void AdvanceToNextByteBoundary() {
            if (this._cbitsInPartialByte > 0) {
                this._cbitsInPartialByte = 0;
                this._partialByte = 0;
            }
        }

        // Token: 0x06002C4E RID: 11342 RVA: 0x0001C8F0 File Offset: 0x0001AAF0
        public void SkipToByteIndex(int index) {
            this._byteArrayIndex = index;
            this.AdvanceToNextByteBoundary();
        }

        // Token: 0x06002C4F RID: 11343 RVA: 0x000C8B80 File Offset: 0x000C6D80
        public long ReadUInt64(int countOfBits) {
            if (countOfBits > 64 || countOfBits <= 0) {
                return 0L;
            }
            long num = 0L;
            while (countOfBits > 0) {
                int num2 = 8;
                if (countOfBits < 8) {
                    num2 = countOfBits;
                }
                num <<= num2;
                byte b = this.ReadByte(num2);
                num |= (long) b;
                countOfBits -= num2;
            }
            return num;
        }

        // Token: 0x06002C50 RID: 11344 RVA: 0x000C8BD4 File Offset: 0x000C6DD4
        public ushort ReadUInt16(int countOfBits) {
            if (countOfBits > 16 || countOfBits <= 0) {
                return 0;
            }
            ushort num = 0;
            while (countOfBits > 0) {
                int num2 = 8;
                if (countOfBits < 8) {
                    num2 = countOfBits;
                }
                num = (ushort) (num << num2);
                byte b = this.ReadByte(num2);
                num |= (ushort) b;
                countOfBits -= num2;
            }
            return num;
        }

        // Token: 0x06002C51 RID: 11345 RVA: 0x000C8C28 File Offset: 0x000C6E28
        public uint ReadUInt16Reverse(int countOfBits) {
            if (countOfBits > 16 || countOfBits <= 0) {
                return 0U;
            }
            ushort num = 0;
            int num2 = 0;
            while (countOfBits > 0) {
                int num3 = 8;
                if (countOfBits < 8) {
                    num3 = countOfBits;
                }
                ushort num4 = (ushort) this.ReadByte(num3);
                num4 = (ushort) (num4 << num2 * 8);
                num |= num4;
                num2++;
                countOfBits -= num3;
            }
            return (uint) num;
        }

        // Token: 0x06002C52 RID: 11346 RVA: 0x000C8C84 File Offset: 0x000C6E84
        public uint ReadUInt32(int countOfBits) {
            if (countOfBits > 32 || countOfBits <= 0) {
                return 0U;
            }
            uint num = 0U;
            while (countOfBits > 0) {
                int num2 = 8;
                if (countOfBits < 8) {
                    num2 = countOfBits;
                }
                num <<= num2;
                byte b = this.ReadByte(num2);
                num |= (uint) b;
                countOfBits -= num2;
            }
            return num;
        }

        // Token: 0x06002C53 RID: 11347 RVA: 0x000C8CD4 File Offset: 0x000C6ED4
        public uint ReadUInt32Reverse(int countOfBits) {
            if (countOfBits > 32 || countOfBits <= 0) {
                return 0U;
            }
            uint num = 0U;
            int num2 = 0;
            while (countOfBits > 0) {
                int num3 = 8;
                if (countOfBits < 8) {
                    num3 = countOfBits;
                }
                uint num4 = (uint) this.ReadByte(num3);
                num4 <<= num2 * 8;
                num |= num4;
                num2++;
                countOfBits -= num3;
            }
            return num;
        }

        // Token: 0x06002C54 RID: 11348 RVA: 0x000C8D2C File Offset: 0x000C6F2C
        public bool ReadBit() {
            byte b = this.ReadByte(1);
            return (b & 1) == 1;
        }

        // Token: 0x06002C55 RID: 11349 RVA: 0x000C8D48 File Offset: 0x000C6F48
        public byte ReadByte(int countOfBits) {
            if (this.EndOfStream) {
                return 0;
            }
            if (countOfBits > 8 || countOfBits <= 0) {
                return 0;
            }
            if ((long) countOfBits > (long) ((ulong) this._bufferLengthInBits)) {
                return 0;
            }
            this._bufferLengthInBits -= (uint) countOfBits;
            byte b;
            if (this._cbitsInPartialByte >= countOfBits) {
                int num = 8 - countOfBits;
                b = (byte) (this._partialByte >> num);
                this._partialByte = (byte) (this._partialByte << countOfBits);
                this._cbitsInPartialByte -= countOfBits;
            } else {
                byte b2 = this._byteArray[this._byteArrayIndex];
                this._byteArrayIndex++;
                int num2 = 8 - countOfBits;
                b = (byte) (this._partialByte >> num2);
                int num3 = Math.Abs(countOfBits - this._cbitsInPartialByte - 8);
                b |= (byte) (b2 >> num3);
                this._partialByte = (byte) (b2 << countOfBits - this._cbitsInPartialByte);
                this._cbitsInPartialByte = 8 - (countOfBits - this._cbitsInPartialByte);
            }
            return b;
        }

        // Token: 0x06002C56 RID: 11350 RVA: 0x000C8E44 File Offset: 0x000C7044
        public float ReadFloat32() {
            return (float) this.ReadUInt32(32);
        }

        // Token: 0x06002C57 RID: 11351 RVA: 0x000C8E68 File Offset: 0x000C7068
        public double ReadFloat64() {
            return (double) this.ReadUInt64(64);
        }

        // Token: 0x06002C58 RID: 11352 RVA: 0x000C8E90 File Offset: 0x000C7090
        public void ReadVector3(ref Vector3 value) {
            uint num = this.ReadUInt32(32);
            value.x = (float) num;
            num = this.ReadUInt32(32);
            value.y = (float) num;
            num = this.ReadUInt32(32);
            value.z = (float) num;
        }

        // Token: 0x06002C59 RID: 11353 RVA: 0x000C8EDC File Offset: 0x000C70DC
        public void ReadVector2(ref Vector2 value) {
            uint num = this.ReadUInt32(32);
            value.x = (float) num;
            num = this.ReadUInt32(32);
            value.y = (float) num;
        }

        // Token: 0x06002C5A RID: 11354 RVA: 0x000C8F14 File Offset: 0x000C7114
        public void ReadQuaternion(ref Quaternion value) {
            uint num = this.ReadUInt32(32);
            value.x = (float) num;
            num = this.ReadUInt32(32);
            value.y = (float) num;
            num = this.ReadUInt32(32);
            value.z = (float) num;
            num = this.ReadUInt32(32);
            value.w = (float) num;
        }

        // Token: 0x06002C5B RID: 11355 RVA: 0x000C8F74 File Offset: 0x000C7174
        public string ReadString(Encoding _encoding) {
            if (this.EndOfStream) {
                return string.Empty;
            }
            uint num = this.ReadUInt32(10);
            if (num == 0U) {
                return string.Empty;
            }
            byte[] array = new byte[num];
            int num2 = 0;
            while ((long) num2 < (long) ((ulong) num)) {
                byte b = this.ReadByte(8);
                array[num2] = b;
                num2++;
            }
            return _encoding.GetString(array);
        }

        // Token: 0x1700041B RID: 1051
        // (get) Token: 0x06002C5C RID: 11356 RVA: 0x0001C8FF File Offset: 0x0001AAFF
        public bool EndOfStream {
            get {
                return 0U == this._bufferLengthInBits;
            }
        }

        // Token: 0x1700041C RID: 1052
        // (get) Token: 0x06002C5D RID: 11357 RVA: 0x0001C90A File Offset: 0x0001AB0A
        public int CurrentIndex {
            get {
                return this._byteArrayIndex - 1;
            }
        }

        // Token: 0x040024CF RID: 9423
        private byte[] _byteArray;

        // Token: 0x040024D0 RID: 9424
        private uint _bufferLengthInBits;

        // Token: 0x040024D1 RID: 9425
        private int _byteArrayIndex;

        // Token: 0x040024D2 RID: 9426
        private byte _partialByte;

        // Token: 0x040024D3 RID: 9427
        private int _cbitsInPartialByte;
    }
}