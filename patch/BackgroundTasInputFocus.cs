using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch
{
    // Virtual pads do not require an OS foreground window. Retain the native
    // CanButtonBePressed body, including direct-control suppression and menus.
    [HarmonyPatch(typeof(PlayerControls), "CanButtonBePressed")]
    public static class BackgroundTasInputFocus
    {
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("OC2SC_BACKGROUND_INPUT") != "0";
        public static long UnfocusedVirtualChecks { get; private set; }
        public static long UnfocusedLogicalChecks { get; private set; }

        internal static bool IsFocusedForLogicalButton(LogicalButtonBase button)
        {
            if (Application.isFocused) return true;
            if (!Enabled || !TASLogicalButton.IsVerifiedEmulatedButton(button)) return false;
            UnfocusedLogicalChecks++;
            return true;
        }

        private static bool IsFocusedForControls(PlayerControls controls)
        {
            if (Application.isFocused) return true;
            if (!Enabled || controls == null) return false;
            var provider = controls.GetComponent<PlayerIDProvider>();
            if (provider == null || !provider.IsLocallyControlled()) return false;
            int entity = (int)EntitySerialisationRegistry.GetId(controls.gameObject);
            if (!TASLogicalButton.IsEmulatedChef(entity)) return false;
            UnfocusedVirtualChecks++;
            return true;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(Application), "isFocused");
            var replacement = AccessTools.Method(typeof(BackgroundTasInputFocus), "IsFocusedForControls");
            int matches = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call && Equals(instruction.operand, getter))
                {
                    // Preserve branches/exception boundaries originally pointing
                    // at the getter by moving them to the new first instruction.
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    load.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    yield return load;
                    instruction.operand = replacement;
                    matches++;
                }
                yield return instruction;
            }
            if (matches != 1) throw new InvalidOperationException("Expected exactly one native PlayerControls focus check.");
        }
    }

    // Native devices and each native gate keep their own press/release claims.
    // Replace only the focus read: Update, IsDown, and gate callbacks still run.
    [HarmonyPatch(typeof(LogicalButtonBase), "CanProcessInput")]
    public static class BackgroundTasLogicalInputFocus
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(Application), "isFocused");
            var replacement = AccessTools.Method(typeof(BackgroundTasInputFocus), "IsFocusedForLogicalButton");
            int matches = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call && Equals(instruction.operand, getter))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    load.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    yield return load;
                    instruction.operand = replacement;
                    matches++;
                }
                yield return instruction;
            }
            if (matches != 1) throw new InvalidOperationException("Expected exactly one native LogicalButtonBase focus check.");
        }
    }
}
