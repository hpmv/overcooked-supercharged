using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SuperchargedPatch
{
    public static class ServerWorkstationExt
    {
        private static FieldInfo f_m_interacters = AccessTools.Field(typeof(ServerWorkstation), "m_interacters");
        private static FieldInfo f_m_item = AccessTools.Field(typeof(ServerWorkstation), "m_item");
        private static MethodInfo m_OnInteracterAdded = AccessTools.Method(typeof(ServerWorkstation), "OnInteracterAdded");
        private static MethodInfo m_OnItemAdded = AccessTools.Method(typeof(ServerWorkstation), "OnItemAdded");
        private static MethodInfo m_OnItemRemovedItem = AccessTools.Method(typeof(ServerWorkstation), "OnItemRemovedItem");
        public static List<ServerWorkstationInteracterExt> Interacters(this ServerWorkstation workstation)
        {
            var interacters = f_m_interacters.GetValue(workstation) as IList;
            var result = new List<ServerWorkstationInteracterExt>();
            foreach (var interacter in interacters)
            {
                result.Add(new ServerWorkstationInteracterExt(interacter));
            }
            return result;
        }

        public static void OnInteracterAdded(this ServerWorkstation workstation, GameObject interacter, Vector2 directionXZ)
        {
            m_OnInteracterAdded.Invoke(workstation, new object[] { interacter, directionXZ });
        }

        public static void OnItemAdded(this ServerWorkstation workstation, IAttachment attachment)
        {
            m_OnItemAdded.Invoke(workstation, new object[] { attachment });
        }

        public static void OnItemRemovedItem(this ServerWorkstation workstation, IAttachment attachment)
        {
            m_OnItemRemovedItem.Invoke(workstation, new object[] { attachment });
        }

        public static ServerWorkableItem Item(this ServerWorkstation workstation)
        {
            return f_m_item.GetValue(workstation) as ServerWorkableItem;
        }
    }

    public class ServerWorkstationInteracterExt
    {
        private object underlying;

        private static Type type = AccessTools.Inner(typeof(ServerWorkstation), "Interacter");
        private static FieldInfo f_m_object = AccessTools.Field(type, "m_object");
        private static FieldInfo f_m_actionTimer = AccessTools.Field(type, "m_actionTimer");

        public ServerWorkstationInteracterExt(object underlying)
        {
            this.underlying = underlying;
        }

        public GameObject m_object
        {
            get
            {
                return f_m_object.GetValue(underlying) as GameObject;
            }
        }

        public float m_actionTimer
        {
            get
            {
                return (float)f_m_actionTimer.GetValue(underlying);
            }
            set
            {
                f_m_actionTimer.SetValue(underlying, value);
            }
        }
    }
}
