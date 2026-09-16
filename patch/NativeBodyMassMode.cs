using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SuperchargedPatch
{
    // Unity 2017 exposes reset APIs, but no automatic-mode getter. The installed
    // native game has no managed COM/inertia setter sites. Track every managed
    // manual setter from plugin startup and refuse those bodies conservatively.
    public static class NativeBodyMassMode
    {
        private static readonly Dictionary<Rigidbody,string> manual=new Dictionary<Rigidbody,string>();
        private static bool observerContractVerified;
        public static void ObserveManualAssignment(Rigidbody body,string property)
        {
            if(body!=null&&!manual.ContainsKey(body))manual.Add(body,property);
        }
        public static void RequireAutomatic(Rigidbody body)
        {
            if(!observerContractVerified) {
                RequireObserver("centerOfMass",typeof(CenterSetter));
                RequireObserver("inertiaTensor",typeof(TensorSetter));
                RequireObserver("inertiaTensorRotation",typeof(RotationSetter));
                observerContractVerified=true;
            }
            string property;
            if(manual.TryGetValue(body,out property))throw new InvalidOperationException("Native body has an observed manual mass-property assignment: "+property);
        }
        private static void RequireObserver(string property,Type observer)
        {
            var method=typeof(Rigidbody).GetProperty(property).GetSetMethod();
            var info=Harmony.GetPatchInfo(method);
            var expected=observer.GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic);
            if(info==null||!info.Prefixes.Any(p=>p.PatchMethod==expected))
                throw new InvalidOperationException("Native automatic mass-mode observation was not installed: "+property);
        }
        public static object Diagnostics()
        {
            return new Dictionary<string,object>{{"policy","native-default-automatic-with-observed-manual-setter-rejection"},
                {"observerContractVerified",observerContractVerified},{"observedManualBodies",manual.Count},
                {"provenance","Installed managed DLLs contain no native COM/inertia setters; three managed setter wrappers are observed from PatchAll before level load. Unity has no native automatic-mode getter; continuation parity remains required."}};
        }
        [HarmonyPatch(typeof(Rigidbody),"set_centerOfMass")]
        private static class CenterSetter { [HarmonyPrefix] private static void Prefix(Rigidbody __instance){ObserveManualAssignment(__instance,"centerOfMass");} }
        [HarmonyPatch(typeof(Rigidbody),"set_inertiaTensor")]
        private static class TensorSetter { [HarmonyPrefix] private static void Prefix(Rigidbody __instance){ObserveManualAssignment(__instance,"inertiaTensor");} }
        [HarmonyPatch(typeof(Rigidbody),"set_inertiaTensorRotation")]
        private static class RotationSetter { [HarmonyPrefix] private static void Prefix(Rigidbody __instance){ObserveManualAssignment(__instance,"inertiaTensorRotation");} }
    }
}
