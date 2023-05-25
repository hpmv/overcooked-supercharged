using HarmonyLib;
using System;
using System.Reflection;

namespace SuperchargedPatch
{
    public static class ClientPlayerControlsImpl_DefaultExt
    {
        private static readonly Type type = typeof(ClientPlayerControlsImpl_Default);

        private static readonly FieldInfo f_m_controls = AccessTools.Field(type, "m_controls");
        public static PlayerControls m_controls(this ClientPlayerControlsImpl_Default instance)
        {
            return (PlayerControls)f_m_controls.GetValue(instance);
        }

        private static readonly FieldInfo f_m_predictedInteracted = AccessTools.Field(type, "m_predictedInteracted");
        public static ClientInteractable m_predictedInteracted(this ClientPlayerControlsImpl_Default instance)
        {
            return f_m_predictedInteracted.GetValue(instance) as ClientInteractable;
        }
        public static void set_m_predictedInteracted(this ClientPlayerControlsImpl_Default instance, ClientInteractable value)
        {
            f_m_predictedInteracted.SetValue(instance, value);
        }

        private static readonly FieldInfo f_m_lastVelocity = AccessTools.Field(type, "m_lastVelocity");
        public static UnityEngine.Vector3 m_lastVelocity(this ClientPlayerControlsImpl_Default instance)
        {
            return (UnityEngine.Vector3)f_m_lastVelocity.GetValue(instance);
        }
        public static void set_m_lastVelocity(this ClientPlayerControlsImpl_Default instance, UnityEngine.Vector3 value)
        {
            f_m_lastVelocity.SetValue(instance, value);
        }

        private static readonly FieldInfo f_m_dashTimer = AccessTools.Field(type, "m_dashTimer");
        public static float m_dashTimer(this ClientPlayerControlsImpl_Default instance)
        {
            return (float)f_m_dashTimer.GetValue(instance);
        }
        public static void set_m_dashTimer(this ClientPlayerControlsImpl_Default instance, float value)
        {
            f_m_dashTimer.SetValue(instance, value);
        }

        private static readonly FieldInfo f_m_aimingThrow = AccessTools.Field(type, "m_aimingThrow");
        public static bool m_aimingThrow(this ClientPlayerControlsImpl_Default instance)
        {
            return (bool)f_m_aimingThrow.GetValue(instance);
        }
        public static void set_m_aimingThrow(this ClientPlayerControlsImpl_Default instance, bool value)
        {
            f_m_aimingThrow.SetValue(instance, value);
        }

        private static readonly FieldInfo f_m_movementInputSuppressed = AccessTools.Field(type, "m_movementInputSuppressed");
        public static bool m_movementInputSuppressed(this ClientPlayerControlsImpl_Default instance)
        {
            return (bool)f_m_movementInputSuppressed.GetValue(instance);
        }
        public static void set_m_movementInputSuppressed(this ClientPlayerControlsImpl_Default instance, bool value)
        {
            f_m_movementInputSuppressed.SetValue(instance, value);
        }

        private static readonly FieldInfo f_m_lastMoveInputDirection = AccessTools.Field(type, "m_lastMoveInputDirection");
        public static UnityEngine.Vector3 m_lastMoveInputDirection(this ClientPlayerControlsImpl_Default instance)
        {
            return (UnityEngine.Vector3)f_m_lastMoveInputDirection.GetValue(instance);
        }
        public static void set_m_lastMoveInputDirection(this ClientPlayerControlsImpl_Default instance, UnityEngine.Vector3 value)
        {
            f_m_lastMoveInputDirection.SetValue(instance, value);
        }

        private static readonly FieldInfo f_m_impactStartTime = AccessTools.Field(type, "m_impactStartTime");
        public static float m_impactStartTime(this ClientPlayerControlsImpl_Default instance)
        {
            return (float)f_m_impactStartTime.GetValue(instance);
        }
        public static void set_m_impactStartTime(this ClientPlayerControlsImpl_Default instance, float value)
        {
            f_m_impactStartTime.SetValue(instance, value);
        }

        private static readonly FieldInfo f_m_impactTimer = AccessTools.Field(type, "m_impactTimer");
        public static float m_impactTimer(this ClientPlayerControlsImpl_Default instance)
        {
            return (float)f_m_impactTimer.GetValue(instance);
        }
        public static void set_m_impactTimer(this ClientPlayerControlsImpl_Default instance, float value)
        {
            f_m_impactTimer.SetValue(instance, value);
        }

        private static readonly FieldInfo f_m_impactVelocity = AccessTools.Field(type, "m_impactVelocity");
        public static UnityEngine.Vector3 m_impactVelocity(this ClientPlayerControlsImpl_Default instance)
        {
            return (UnityEngine.Vector3)f_m_impactVelocity.GetValue(instance);
        }
        public static void set_m_impactVelocity(this ClientPlayerControlsImpl_Default instance, UnityEngine.Vector3 value)
        {
            f_m_impactVelocity.SetValue(instance, value);
        }

        private static readonly FieldInfo f_m_LeftOverTime = AccessTools.Field(type, "m_LeftOverTime");
        public static float m_LeftOverTime(this ClientPlayerControlsImpl_Default instance)
        {
            return (float)f_m_LeftOverTime.GetValue(instance);
        }
        public static void set_m_LeftOverTime(this ClientPlayerControlsImpl_Default instance, float value)
        {
            f_m_LeftOverTime.SetValue(instance, value);
        }
    }

    public static class ServerPlayerControlsImpl_DefaultExt
    {
        private static readonly Type type = typeof(ServerPlayerControlsImpl_Default);
        private static readonly FieldInfo f_m_lastInteracted = AccessTools.Field(type, "m_lastInteracted");
        public static void set_m_lastInteracted(this ServerPlayerControlsImpl_Default instance, ServerInteractable interactable)
        {
            f_m_lastInteracted.SetValue(instance, interactable);
        }
    }
}
