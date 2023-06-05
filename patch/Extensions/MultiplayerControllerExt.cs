using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Team17.Online.Multiplayer;
using Team17.Online.Multiplayer.Connection;

namespace SuperchargedPatch.Extensions
{
    public static class MultiplayerControllerExt
    {
        private static Type type = typeof(MultiplayerController);
        private static FieldInfo f_m_LocalServer = AccessTools.Field(type, "m_LocalServer");
        private static FieldInfo f_m_LocalClient = AccessTools.Field(type, "m_LocalClient");
        
        public static Server m_LocalServer(this MultiplayerController self)
        {
            return f_m_LocalServer.GetValue(self) as Server;
        }

        public static Client m_LocalClient(this MultiplayerController self)
        {
            return f_m_LocalClient.GetValue(self) as Client;
        }

        public static bool HasAnyPendingBatchedMessages(this MultiplayerController self)
        {
            var server = self.m_LocalServer();
            var client = self.m_LocalClient();
            bool hasPending = false;

            if (server != null)
            {
                var conns = server.m_AllConnections();
                if (conns != null)
                {
                    conns.ForEach(conn =>
                    {
                        if (conn is LocalLoopbackConnection llc)
                        {
                            hasPending |= llc.GetMessageBatcher(true).HasAnyMessages();
                            hasPending |= llc.GetMessageBatcher(false).HasAnyMessages();
                        }
                    });
                }
            }
            if (client != null)
            {
                var conn = client.m_ServerConnection();
                if (conn is LocalLoopbackConnection llc)
                {
                    hasPending |= llc.GetMessageBatcher(true).HasAnyMessages();
                    hasPending |= llc.GetMessageBatcher(false).HasAnyMessages();
                }
            }
            return hasPending;
        }

        public static void FlushAllPendingBatchedMessages(this MultiplayerController self)
        {
            while (self.HasAnyPendingBatchedMessages())
            {
                self.LateUpdate();
            }
        }
    }

    public static class T17ServerExt
    {
        private static Type type = typeof(Server);
        private static FieldInfo f_m_AllConnections = AccessTools.Field(type, "m_AllConnections");

        public static FastList<NetworkConnection> m_AllConnections(this Server self)
        {
            return f_m_AllConnections.GetValue(self) as FastList<NetworkConnection>;
        }
    }

    public static class T17ClientExt
    {
        private static Type type = typeof(Client);
        private static FieldInfo f_m_ServerConnection = AccessTools.Field(type, "m_ServerConnection");

        public static NetworkConnection m_ServerConnection(this Client self)
        {
            return f_m_ServerConnection.GetValue(self) as NetworkConnection;
        }
    }

    public static class LocalLoopbackConnectionExt
    {
        private static Type type = typeof(LocalLoopbackConnection);
        private static MethodInfo m_GetMessageBatcher = AccessTools.Method(type, "GetMessageBatcher");

        public static MessageBatcherBuffer GetMessageBatcher(this LocalLoopbackConnection conn, bool bReliable)
        {
            return m_GetMessageBatcher.Invoke(conn, new object[] {bReliable}) as MessageBatcherBuffer;
        }
    }

    public static class MessageBatcherBufferExt
    {
        private static Type type = typeof(MessageBatcherBuffer);
        private static FieldInfo f_m_Batchers = AccessTools.Field(type, "m_Batchers");

        public static FastList<MessageBatcher> m_Batchers(this MessageBatcherBuffer self)
        {
            return (FastList<MessageBatcher>)f_m_Batchers.GetValue(self);
        }

        public static bool HasAnyMessages(this MessageBatcherBuffer self)
        {
            return self.m_Batchers().Find(batcher => batcher.MessagesBatched > 0) != null;
        }
    }
}
