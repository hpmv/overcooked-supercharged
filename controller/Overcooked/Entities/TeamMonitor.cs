using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

// Token: 0x0200084C RID: 2124
[Serializable]
public class TeamMonitor {
    // Token: 0x0200084D RID: 2125
    public class TeamScoreStats : Serialisable {
        // Token: 0x0600286F RID: 10351 RVA: 0x0001A299 File Offset: 0x00018499
        public int GetTotalScore() {
            return this.TotalBaseScore + this.TotalTipsScore - this.TotalTimeExpireDeductions;
        }

        // Token: 0x06002870 RID: 10352 RVA: 0x000BBFF4 File Offset: 0x000BA1F4
        public bool Copy(TeamMonitor.TeamScoreStats _other) {
            this.TotalBaseScore = _other.TotalBaseScore;
            this.TotalTipsScore = _other.TotalTipsScore;
            this.TotalMultiplier = _other.TotalMultiplier;
            this.TotalCombo = _other.TotalCombo;
            this.TotalTimeExpireDeductions = _other.TotalTimeExpireDeductions;
            this.ComboMaintained = _other.ComboMaintained;
            this.TotalSuccessfulDeliveries = _other.TotalSuccessfulDeliveries;
            return true;
        }

        // Token: 0x06002871 RID: 10353 RVA: 0x000BC058 File Offset: 0x000BA258
        public void Serialise(BitStreamWriter writer) {
            writer.Write((uint) this.TotalBaseScore, 16);
            writer.Write((uint) this.TotalTipsScore, 16);
            writer.Write((uint) this.TotalMultiplier, 3);
            writer.Write((uint) this.TotalCombo, 8);
            writer.Write((uint) this.TotalTimeExpireDeductions, 16);
            writer.Write(this.ComboMaintained);
            writer.Write((uint) this.TotalSuccessfulDeliveries, 8);
        }

        // Token: 0x06002872 RID: 10354 RVA: 0x000BC0C4 File Offset: 0x000BA2C4
        public bool Deserialise(BitStreamReader reader) {
            this.TotalBaseScore = (int) reader.ReadUInt32(16);
            this.TotalTipsScore = (int) reader.ReadUInt32(16);
            this.TotalMultiplier = (int) reader.ReadUInt32(3);
            this.TotalCombo = (int) reader.ReadUInt32(8);
            this.TotalTimeExpireDeductions = (int) reader.ReadUInt32(16);
            this.ComboMaintained = reader.ReadBit();
            this.TotalSuccessfulDeliveries = (int) reader.ReadUInt32(8);
            return true;
        }

        // Token: 0x040020EC RID: 8428
        private const int kBitsPerScore = 16;

        // Token: 0x040020ED RID: 8429
        private const int kBitsPerMultiplier = 3;

        // Token: 0x040020EE RID: 8430
        private const int kBitsPerCombo = 8;

        // Token: 0x040020EF RID: 8431
        private const int kBitsPerDelivery = 8;

        // Token: 0x040020F0 RID: 8432
        public int TotalBaseScore;

        // Token: 0x040020F1 RID: 8433
        public int TotalTipsScore;

        // Token: 0x040020F2 RID: 8434
        public int TotalMultiplier;

        // Token: 0x040020F3 RID: 8435
        public int TotalCombo;

        // Token: 0x040020F4 RID: 8436
        public int TotalTimeExpireDeductions;

        // Token: 0x040020F5 RID: 8437
        public bool ComboMaintained = true;

        // Token: 0x040020F6 RID: 8438
        public int TotalSuccessfulDeliveries;
    }
}