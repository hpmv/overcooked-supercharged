using System;

public static class GameUtils {

    // Token: 0x06004231 RID: 16945 RVA: 0x0002A4FC File Offset: 0x000286FC
    public static int GetRequiredBitCount(int value) {
        return (int) (Math.Log((double) value, 2.0) + 1.0);
    }
}