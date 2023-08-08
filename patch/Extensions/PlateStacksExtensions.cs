using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace SuperchargedPatch.Extensions
{
    public static class ServerPlateStackBaseExt
    {
        private static readonly Type type = typeof(ServerPlateStackBase);
        private static readonly FieldInfo f_m_stack = AccessTools.Field(type, "m_stack");
        
        public static ServerStack m_stack(this ServerPlateStackBase self)
        {
            return f_m_stack.GetValue(self) as ServerStack;
        }

        public static void BaseAddToStackAfterTheFact(this ServerPlateStackBase self, GameObject obj)
        {
            self.m_stack().AddToStack(obj);
        }

        public static List<GameObject> BaseRemoveAllFromStack(this ServerPlateStackBase self)
        {
            var objects = new List<GameObject>();
            var stack = self.m_stack();
            while (stack.GetSize() > 0)
            {
                objects.Add(stack.RemoveFromStack());
            }
            return objects;
        }

        public static void AddToStackAfterTheFact(this ServerPlateStackBase self, GameObject obj)
        {
            if (self is ServerDirtyPlateStack sdps)
            {
                sdps.AddToStackAfterTheFact(obj);
            }
            else if (self is ServerCleanPlateStack scps)
            {
                scps.AddToStackAfterTheFact(obj);
            }
            else
            {
                throw new Exception("Unknown ServerPlateStackBase type: " + self.GetType().Name);
            }
        }

        public static List<GameObject> RemoveAllFromStack(this ServerPlateStackBase self)
        {
            if (self is ServerDirtyPlateStack sdps)
            {
                return sdps.RemoveAllFromStack();
            }
            else if (self is ServerCleanPlateStack scps)
            {
                return scps.RemoveAllFromStack();
            }
            else
            {
                throw new Exception("Unknown ServerPlateStackBase type: " + self.GetType().Name);
            }
        }
    }

    public static class ClientPlateStackBaseExt
    {
        private static readonly Type type = typeof(ClientPlateStackBase);
        private static readonly FieldInfo f_m_stack = AccessTools.Field(type, "m_stack");
        private static readonly MethodInfo m_NotifyPlateAdded = AccessTools.Method(type, "NotifyPlateAdded");
        private static readonly MethodInfo m_NotifyPlateRemoved = AccessTools.Method(type, "NotifyPlateRemoved");

        public static ClientStack m_stack(this ClientPlateStackBase self)
        {
            return f_m_stack.GetValue(self) as ClientStack;
        }

        public static void BaseAddToStackAfterTheFact(this ClientPlateStackBase self, GameObject obj)
        {
            self.m_stack().AddToStack(obj);
        }

        public static List<GameObject> BaseRemoveAllFromStack(this ClientPlateStackBase self)
        {
            var objects = new List<GameObject>();
            var stack = self.m_stack();
            while (stack.GetSize() > 0)
            {
                objects.Add(stack.RemoveFromStack());
            }
            return objects;
        }

        public static void NotifyPlateAdded(this ClientPlateStackBase self, GameObject obj)
        {
            m_NotifyPlateAdded.Invoke(self, new object[] { obj });
        }

        public static void NotifyPlateRemoved(this ClientPlateStackBase self, GameObject obj)
        {
            m_NotifyPlateRemoved.Invoke(self, new object[] { obj });
        }

        public static void AddToStackAfterTheFact(this ClientPlateStackBase self, GameObject obj)
        {
            if (self is ClientDirtyPlateStack cdps)
            {
                cdps.AddToStackAfterTheFact(obj);
            }
            else if (self is ClientCleanPlateStack ccps)
            {
                ccps.AddToStackAfterTheFact(obj);
            }
            else
            {
                throw new Exception("Unknown ClientPlateStackBase type: " + self.GetType().Name);
            }
        }

        public static List<GameObject> RemoveAllFromStack(this ClientPlateStackBase self)
        {
            if (self is ClientDirtyPlateStack cdps)
            {
                return cdps.RemoveAllFromStack();
            }
            else if (self is ClientCleanPlateStack ccps)
            {
                return ccps.RemoveAllFromStack();
            }
            else
            {
                throw new Exception("Unknown ClientPlateStackBase type: " + self.GetType().Name);
            }
        }
    }

    public static class ServerDirtyPlateStackExt
    {
        private static readonly Type type = typeof(ServerDirtyPlateStack);
        private static readonly FieldInfo f_m_washable = AccessTools.Field(type, "m_washable");

        public static ServerWashable m_washable(this ServerDirtyPlateStack self)
        {
            return f_m_washable.GetValue(self) as ServerWashable;
        }

        public static void AddToStackAfterTheFact(this ServerDirtyPlateStack self, GameObject obj)
        {
            self.BaseAddToStackAfterTheFact(obj);
            var stack = self.m_stack();
            var m_washable = self.m_washable();
            if (m_washable != null)
            {
                Washable washable = self.gameObject.RequireComponent<Washable>();
                m_washable.SetDuration(washable.m_duration * stack.GetSize());
            }
        }

        public static List<GameObject> RemoveAllFromStack(this ServerDirtyPlateStack self)
        {
            return self.BaseRemoveAllFromStack();
        }
    }

    public static class ClientDirtyPlateStackExt
    {
        private static readonly Type type = typeof(ClientDirtyPlateStack);
        private static readonly FieldInfo f_m_highlight = AccessTools.Field(type, "m_highlight");

        public static void AddToStackAfterTheFact(this ClientDirtyPlateStack self, GameObject obj)
        {
            self.BaseAddToStackAfterTheFact(obj);
            var highlight = f_m_highlight.GetValue(self) as ClientAnticipateInteractionHighlight;
            highlight?.RebuildHighlightMaterials();
            self.NotifyPlateAdded(obj);
        }

        public static List<GameObject> RemoveAllFromStack(this ClientDirtyPlateStack self)
        {
            var removed = self.BaseRemoveAllFromStack();
            var highlight = f_m_highlight.GetValue(self) as ClientAnticipateInteractionHighlight;
            highlight?.RebuildHighlightMaterials();
            foreach (var obj in removed)
            {
                self.NotifyPlateRemoved(obj);
            }
            return removed;
        }
    }

    public static class ServerCleanPlateStackExt
    {
        public static void AddToStackAfterTheFact(this ServerCleanPlateStack self, GameObject obj)
        {
            self.BaseAddToStackAfterTheFact(obj);
            ServerHandlePickupReferral serverHandlePickupReferral = obj.RequestComponent<ServerHandlePickupReferral>();
            if (serverHandlePickupReferral != null)
            {
                serverHandlePickupReferral.SetHandlePickupReferree(self);
            }
            ServerHandlePlacementReferral serverHandlePlacementReferral = obj.RequestComponent<ServerHandlePlacementReferral>();
            if (serverHandlePlacementReferral != null)
            {
                serverHandlePlacementReferral.SetHandlePlacementReferree(self);
            }

            ServerUtensilRespawnBehaviour serverUtensilRespawnBehaviour = self.gameObject.RequestComponent<ServerUtensilRespawnBehaviour>();
            ServerUtensilRespawnBehaviour serverUtensilRespawnBehaviour2 = obj.RequestComponent<ServerUtensilRespawnBehaviour>();
            if (serverUtensilRespawnBehaviour != null && serverUtensilRespawnBehaviour2 != null)
            {
                serverUtensilRespawnBehaviour2.SetIdealRespawnLocation(serverUtensilRespawnBehaviour.GetIdealRespawnLocation());
            }
        }

        public static List<GameObject> RemoveAllFromStack(this ServerCleanPlateStack self)
        {
            var removed = self.BaseRemoveAllFromStack();
            foreach (var obj in removed)
            {
                ServerHandlePickupReferral serverHandlePickupReferral = obj.RequestComponent<ServerHandlePickupReferral>();
                if (serverHandlePickupReferral != null && ReferenceEquals(serverHandlePickupReferral.GetHandlePickupReferree(), self))
                {
                    serverHandlePickupReferral.SetHandlePickupReferree(null);
                }
                ServerHandlePlacementReferral serverHandlePlacementReferral = obj.RequestComponent<ServerHandlePlacementReferral>();
                if (serverHandlePlacementReferral != null && ReferenceEquals(serverHandlePlacementReferral.GetHandlePlacementReferree(), self))
                {
                    serverHandlePlacementReferral.SetHandlePlacementReferree(null);
                }
            }
            return removed;
        }
    }

    public static class ClientCleanPlateStackExt
    {
        private static readonly Type type = typeof(ClientCleanPlateStack);
        private static readonly FieldInfo f_m_delayedSetupPlates = AccessTools.Field(type, "m_delayedSetupPlates");

        public static void AddToStackAfterTheFact(this ClientCleanPlateStack self, GameObject obj)
        {
            self.BaseAddToStackAfterTheFact(obj);
            ClientHandlePickupReferral clientHandlePickupReferral = obj.RequestComponent<ClientHandlePickupReferral>();
            if (clientHandlePickupReferral != null)
            {
                clientHandlePickupReferral.SetHandlePickupReferree(self);
            }
            ClientHandlePlacementReferral clientHandlePlacementReferral = obj.RequestComponent<ClientHandlePlacementReferral>();
            if (clientHandlePlacementReferral != null)
            {
                clientHandlePlacementReferral.SetHandlePlacementReferree(self);
            }
            self.NotifyPlateAdded(obj);
        }

        public static List<GameObject> RemoveAllFromStack(this ClientCleanPlateStack self)
        {
            var delayedSetupPlates = f_m_delayedSetupPlates.GetValue(self) as List<GameObject>;
            delayedSetupPlates.Clear();
            var removed = self.BaseRemoveAllFromStack();
            foreach (var obj in removed)
            {
                ClientHandlePickupReferral clientHandlePickupReferral = obj.RequestComponent<ClientHandlePickupReferral>();
                if (clientHandlePickupReferral != null && ReferenceEquals(clientHandlePickupReferral.GetHandlePickupReferree(), self))
                {
                    clientHandlePickupReferral.SetHandlePickupReferree(null);
                }
                ClientHandlePlacementReferral clientHandlePlacementReferral = obj.RequestComponent<ClientHandlePlacementReferral>();
                if (clientHandlePlacementReferral != null && ReferenceEquals(clientHandlePlacementReferral.GetHandlePlacementReferree(), self))
                {
                    clientHandlePlacementReferral.SetHandlePlacementReferree(null);
                }
                self.NotifyPlateRemoved(obj);
            }
            return removed;
        }
    }
}
