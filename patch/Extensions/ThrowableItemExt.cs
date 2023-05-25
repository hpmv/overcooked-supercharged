using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace SuperchargedPatch.Extensions
{
    public static class ServerThrowableItemExt
    {
        private static Type type = typeof(ServerThrowableItem);
        private static FieldInfo f_m_attachment = AccessTools.Field(type, "m_attachment");
        private static FieldInfo f_m_OnAttachChanged = AccessTools.Field(type, "m_OnAttachChanged");
        private static FieldInfo f_m_pickupReferral = AccessTools.Field(type, "m_pickupReferral");
        private static FieldInfo f_m_flying = AccessTools.Field(type, "m_flying");
        private static FieldInfo f_m_flightTimer = AccessTools.Field(type, "m_flightTimer");
        private static FieldInfo f_m_thrower = AccessTools.Field(type, "m_thrower");
        private static FieldInfo f_m_ThrowStartColliders = AccessTools.Field(type, "m_ThrowStartColliders");
        private static FieldInfo f_m_ignoredCollidersCount = AccessTools.Field(type, "m_ignoredCollidersCount");
        private static FieldInfo f_m_Collider = AccessTools.Field(type, "m_Collider");

        public static IAttachment m_attachment(this ServerThrowableItem self)
        {
            return f_m_attachment.GetValue(self) as IAttachment;
        }

        public static AttachChangedCallback m_OnAttachChanged(this ServerThrowableItem self)
        {
            return f_m_OnAttachChanged.GetValue(self) as AttachChangedCallback;
        }

        public static ServerHandlePickupReferral m_pickupReferral(this ServerThrowableItem self)
        {
            return f_m_pickupReferral.GetValue(self) as ServerHandlePickupReferral;
        }

        public static bool m_flying(this ServerThrowableItem self)
        {
            return (bool)f_m_flying.GetValue(self);
        }

        public static void SetFlying(this ServerThrowableItem self, bool flying)
        {
            f_m_flying.SetValue(self, flying);
        }

        public static float m_flightTimer(this ServerThrowableItem self)
        {
            return (float)f_m_flightTimer.GetValue(self);
        }

        public static void SetFlightTimer(this ServerThrowableItem self, float flightTimer)
        {
            f_m_flightTimer.SetValue(self, flightTimer);
        }

        public static IThrower m_thrower(this ServerThrowableItem self)
        {
            return f_m_thrower.GetValue(self) as IThrower;
        }

        public static void SetThrower(this ServerThrowableItem self, IThrower thrower)
        {
            f_m_thrower.SetValue(self, thrower);
        }

        public static Collider[] m_ThrowStartColliders(this ServerThrowableItem self)
        {
            return f_m_ThrowStartColliders.GetValue(self) as Collider[];
        }

        public static void SetThrowStartColliders(this ServerThrowableItem self, Collider[] throwStartColliders)
        {
            f_m_ThrowStartColliders.SetValue(self, throwStartColliders);
        }

        public static int m_ignoredCollidersCount(this ServerThrowableItem self)
        {
            return (int)f_m_ignoredCollidersCount.GetValue(self);
        }

        public static void SetIgnoredCollidersCount(this ServerThrowableItem self, int ignoredCollidersCount)
        {
            f_m_ignoredCollidersCount.SetValue(self, ignoredCollidersCount);
        }

        public static Collider m_Collider(this ServerThrowableItem self)
        {
            return f_m_Collider.GetValue(self) as Collider;
        }

        public static void OnEnterFlightForWarping(this ServerThrowableItem self)
        {
            self.m_attachment()?.RegisterAttachChangedCallback(self.m_OnAttachChanged());
            self.m_pickupReferral()?.SetHandlePickupReferree(self);
        }

        public static void OnExitFlightForWarping(this ServerThrowableItem self)
        {
            self.m_attachment()?.UnregisterAttachChangedCallback(self.m_OnAttachChanged());
            var referral = self.m_pickupReferral();
            if (referral != null && referral.GetHandlePickupReferree() == self)
            {
                referral?.SetHandlePickupReferree(null);
            }
        }
    }

    public static class ClientThrowableItemExt
    {
        private static Type type = typeof(ClientThrowableItem);
        private static FieldInfo f_m_pickupReferral = AccessTools.Field(type, "m_pickupReferral");
        private static FieldInfo f_m_isFlying = AccessTools.Field(type, "m_isFlying");
        private static FieldInfo f_m_thrower = AccessTools.Field(type, "m_thrower");

        public static ClientHandlePickupReferral m_pickupReferral(this ClientThrowableItem self)
        {
            return f_m_pickupReferral.GetValue(self) as ClientHandlePickupReferral;
        }

        public static void OnEnterFlightForWarping(this ClientThrowableItem self)
        {
            self.m_pickupReferral()?.SetHandlePickupReferree(self);
        }

        public static void OnExitFlightForWarping(this ClientThrowableItem self)
        {
            var referral = self.m_pickupReferral();
            if (referral != null && referral.GetHandlePickupReferree() == self)
            {
                referral?.SetHandlePickupReferree(null);
            }
        }

        public static bool m_isFlying(this ClientThrowableItem self)
        {
            return (bool)f_m_isFlying.GetValue(self);
        }

        public static void SetIsFlying(this ClientThrowableItem self, bool flying)
        {
            f_m_isFlying.SetValue(self, flying);
        }

        public static IClientThrower m_thrower(this ClientThrowableItem self)
        {
            return f_m_thrower.GetValue(self) as IClientThrower;
        }

        public static void SetThrower(this ClientThrowableItem self, IClientThrower thrower)
        {
            f_m_thrower.SetValue(self, thrower);
        }
    }
}
