using System.Collections.Generic;
using Team17.Online.Multiplayer.Messaging;

public class MessageStats {
    public Dictionary<string, int> messageTypeStats = new Dictionary<string, int>();

    public void Clear() {
        messageTypeStats.Clear();
    }

    public void CountMessage(string type) {
        if (!messageTypeStats.ContainsKey(type)) {
            messageTypeStats[type] = 0;
        }
        messageTypeStats[type]++;
    }
}