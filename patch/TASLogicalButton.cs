using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Hpmv;
using UnityEngine;
using Team17.Online.Multiplayer.Messaging;

namespace SuperchargedPatch
{
    public enum TASLogicalButtonType { Pickup, Use, Dash }
    public enum TASLogicalValueType { MovementX, MovementY }

    // Input device only. Native LogicalButtonBase supplies edge claims and held time.
    public sealed class TASLogicalButton : LogicalButtonBase
    {
        private readonly int playerEntityId;
        private readonly TASLogicalButtonType type;
        private readonly ILogicalButton original;
        private readonly GameObject owner;
        private readonly int ownerInstanceId;
        private readonly string deviceKey;
        private static readonly Dictionary<string, TASLogicalButton> devices = new Dictionary<string, TASLogicalButton>();
        private static readonly List<HistoryOwner> histories = new List<HistoryOwner>();
        private static Dictionary<int, PadState> pads = new Dictionary<int, PadState>();
        private static bool emulationActive;
        private static int resetGeneration;
        private static long controlSchemesObserved;
        private static long controlSchemeChainsObserved;
        private static long controlSchemeGatesObserved;
        private static long controlSchemeGatesNewlyTracked;
        private static long pickupPollSequence;
        private static long inputApplicationSequence;
        private static long neutralPreparationSequence;
        private static long focusDecisionSequence;
        private static int lastAppliedControllerFrame = -1;
        private static string nextInputReason;
        private static string lastInputReason = "startup";
        private static readonly List<Dictionary<string, object>> pickupPolls = new List<Dictionary<string, object>>();
        private static readonly List<Dictionary<string, object>> inputApplications = new List<Dictionary<string, object>>();
        private static readonly List<Dictionary<string, object>> neutralPreparations = new List<Dictionary<string, object>>();
        private static readonly List<Dictionary<string, object>> focusDecisions = new List<Dictionary<string, object>>();
        public static bool IsEmulatedChef(int entityId) { return emulationActive && pads.ContainsKey(entityId); }
        private static readonly FieldInfo pressClaimed = NativeField("m_pressClaimed");
        private static readonly FieldInfo releaseClaimed = NativeField("m_releaseClaimed");
        private static readonly FieldInfo buttonDownTime = NativeField("m_buttonDownTime");
        private static readonly FieldInfo buttonDownLength = NativeField("m_buttonDownLength");
        private static readonly FieldInfo down = NativeField("m_down");
        private static readonly FieldInfo gateChild = typeof(GateLogicalButton).GetField("m_childButton", BindingFlags.Instance | BindingFlags.NonPublic);

        private TASLogicalButton(int entityId, TASLogicalButtonType buttonType, ILogicalButton source, GameObject chef)
        {
            playerEntityId = entityId; type = buttonType; original = source;
            owner = chef; ownerInstanceId = chef.GetInstanceID();
            deviceKey = entityId + ":" + (int)buttonType;
        }

        public static ILogicalButton GetOrCreate(int entityId, TASLogicalButtonType type, ILogicalButton original, GameObject owner)
        {
            if (original == null || owner == null) throw new ArgumentException("A TAS device requires the native source and chef.");
            string key = entityId + ":" + (int)type;
            TASLogicalButton device;
            if (devices.TryGetValue(key, out device) && device.owner == owner && device.ownerInstanceId == owner.GetInstanceID())
                return device;
            device = new TASLogicalButton(entityId, type, original, owner);
            devices[key] = device;
            TrackHistory(device, device);
            return device;
        }

        // Observation-only postfixes register native gates, which have independent
        // LogicalButtonBase histories. Neither the gate nor its callback is replaced.
        public static void ObserveNativeGate(ILogicalButton source, ILogicalButton result)
        {
            var device = ObservedDevice(source);
            var gate = result as LogicalButtonBase;
            if (device != null && gate != null && gate.GetType() == typeof(GateLogicalButton) && gateChild != null &&
                ReferenceEquals(gateChild.GetValue(gate), source)) TrackHistory(gate, device);
        }
        // Observe the exact button chains assigned to native PlayerControls. Tiny
        // GetGated helpers may be inlined before Harmony can see their postfixes;
        // the nontrivial native setter is the authoritative, behavior-neutral
        // point at which the final control scheme becomes visible.
        public static void ObserveControlScheme(PlayerControls.ControlSchemeData scheme)
        {
            if (scheme == null) return;
            controlSchemesObserved++;
            ObserveControlSchemeChain(scheme.m_pickupButton, TASLogicalButtonType.Pickup);
            ObserveControlSchemeChain(scheme.m_worksurfaceUseButton, TASLogicalButtonType.Use);
            ObserveControlSchemeChain(scheme.m_dashButton, TASLogicalButtonType.Dash);
        }
        private static void ObserveControlSchemeChain(ILogicalButton head, TASLogicalButtonType expected)
        {
            var gates = new List<LogicalButtonBase>();
            ILogicalButton cursor = head;
            for (int depth = 0; depth < 8; depth++)
            {
                var device = cursor as TASLogicalButton;
                if (device != null)
                {
                    if (device.type != expected) return;
                    controlSchemeChainsObserved++;
                    foreach (var gate in gates)
                    {
                        controlSchemeGatesObserved++;
                        if (ObservedDevice(gate) == null) controlSchemeGatesNewlyTracked++;
                        TrackHistory(gate, device);
                    }
                    return;
                }
                var nativeGate = cursor as GateLogicalButton;
                if (nativeGate == null || gateChild == null) return;
                var child = gateChild.GetValue(nativeGate) as ILogicalButton;
                if (child == null || ReferenceEquals(child, cursor)) return;
                gates.Add(nativeGate);
                cursor = child;
            }
        }
        public static Dictionary<string, object> Diagnostics()
        {
            return new Dictionary<string, object> {
                { "devices", devices.Count }, { "liveHistories", LiveHistories().Count },
                { "controlSchemesObserved", controlSchemesObserved },
                { "controlSchemeChainsObserved", controlSchemeChainsObserved },
                { "controlSchemeGatesObserved", controlSchemeGatesObserved },
                { "controlSchemeGatesNewlyTracked", controlSchemeGatesNewlyTracked },
                { "lastAppliedControllerFrame", lastAppliedControllerFrame },
                { "lastInputReason", lastInputReason },
                { "inputApplications", new List<Dictionary<string, object>>(inputApplications) },
                { "neutralPreparations", new List<Dictionary<string, object>>(neutralPreparations) },
                { "focusDecisions", new List<Dictionary<string, object>>(focusDecisions) },
                { "pickupPolls", new List<Dictionary<string, object>>(pickupPolls) }
            };
        }
        // Exact replacement for the one native Update_Carry JustPressed call.
        // The button is evaluated once; the return value and private history are
        // observed around that same evaluation only when its TAS pickup level is
        // currently down (or a release from that level is still pending).
        public static bool ObservePickupJustPressed(ILogicalButton button, GameObject consumer)
        {
            TASLogicalButton device;
            var before = DescribeChain(button, out device);
            bool trace = device != null && device.type == TASLogicalButtonType.Pickup &&
                (device.IsDown() || ChainWasDown(before));
            bool result = button.JustPressed();
            if (trace)
            {
                TASLogicalButton ignored;
                var receipt = new Dictionary<string, object> {
                    { "sequence", ++pickupPollSequence }, { "unityFrame", Time.frameCount },
                    { "unityTime", Time.time }, { "applicationFocused", Application.isFocused },
                    { "controllerFrame", lastAppliedControllerFrame }, { "inputReason", lastInputReason },
                    { "consumerEntity", consumer == null ? -1 : (int)EntitySerialisationRegistry.GetId(consumer) },
                    { "deviceEntity", device.playerEntityId }, { "inputLevel", device.IsDown() },
                    { "result", result }, { "before", before }, { "after", DescribeChain(button, out ignored) }
                };
                pickupPolls.Add(receipt);
                if (pickupPolls.Count > 128) pickupPolls.RemoveAt(0);
            }
            return result;
        }
        // The authoring pause prevents PlayerControls.Update_Carry from polling
        // its outer native gates. ForceNeutral updates the TAS devices, then this
        // applies the same accepted neutral sample to every verified native gate
        // history before a new segment is armed. No gate callback is invoked and
        // no press/release event is manufactured.
        public static void PrepareNeutralHistoriesForInput(string reason)
        {
            int updated = 0, priorClaimedPresses = 0, priorDown = 0;
            var rows = new List<Dictionary<string, object>>();
            foreach (var history in LiveHistories())
            {
                var button = history.Button.Target as LogicalButtonBase;
                if (button == null || !IsVerifiedEmulatedButton(button)) continue;
                var value = ReadHistory(button);
                if (value.PressClaimed) priorClaimedPresses++;
                if (value.Down) priorDown++;
                value.PressClaimed = false;
                value.Down = false;
                WriteHistory(button, value, 0);
                if (history.Device.type == TASLogicalButtonType.Pickup)
                    rows.Add(HistoryRow(button, history.Device));
                updated++;
            }
            var receipt = new Dictionary<string, object> {
                { "sequence", ++neutralPreparationSequence }, { "unityFrame", Time.frameCount },
                { "unityTime", Time.time }, { "applicationFocused", Application.isFocused },
                { "reason", reason ?? "unspecified" }, { "updated", updated },
                { "priorClaimedPresses", priorClaimedPresses }, { "priorDown", priorDown },
                { "pickupHistoriesAfter", rows }
            };
            neutralPreparations.Add(receipt);
            if (neutralPreparations.Count > 64) neutralPreparations.RemoveAt(0);
        }
        private static List<Dictionary<string, object>> DescribeChain(ILogicalButton head, out TASLogicalButton device)
        {
            device = null;
            var result = new List<Dictionary<string, object>>();
            ILogicalButton cursor = head;
            for (int depth = 0; depth < 8 && cursor != null; depth++)
            {
                var logical = cursor as LogicalButtonBase;
                var currentDevice = cursor as TASLogicalButton;
                if (logical == null) break;
                var history = ReadHistory(logical);
                result.Add(new Dictionary<string, object> {
                    { "depth", depth }, { "identity", RuntimeHelpers.GetHashCode(logical) },
                    { "type", logical.GetType().FullName },
                    { "verified", IsVerifiedEmulatedButton(logical) },
                    { "down", history.Down }, { "pressClaimed", history.PressClaimed },
                    { "releaseClaimed", history.ReleaseClaimed },
                    { "downTime", history.DownTime }, { "downLength", history.DownLength }
                });
                if (currentDevice != null) { device = currentDevice; break; }
                if (logical.GetType() != typeof(GateLogicalButton) || gateChild == null) break;
                var child = gateChild.GetValue(logical) as ILogicalButton;
                if (child == null || ReferenceEquals(child, cursor)) break;
                cursor = child;
            }
            return result;
        }
        private static bool ChainWasDown(List<Dictionary<string, object>> chain)
        {
            foreach (var layer in chain)
                if (layer.ContainsKey("down") && (bool)layer["down"]) return true;
            return false;
        }
        private static TASLogicalButton ObservedDevice(ILogicalButton button)
        {
            foreach (var history in histories)
                if (ReferenceEquals(history.Button.Target, button)) return history.Device;
            return null;
        }
        internal static void ObserveFocusDecision(LogicalButtonBase button, bool verified)
        {
            var device = ObservedDevice(button);
            if (device == null || device.type != TASLogicalButtonType.Pickup || !device.IsDown()) return;
            var history = ReadHistory(button);
            var receipt = new Dictionary<string, object> {
                { "sequence", ++focusDecisionSequence }, { "unityFrame", Time.frameCount },
                { "unityTime", Time.time }, { "controllerFrame", lastAppliedControllerFrame },
                { "inputReason", lastInputReason }, { "buttonType", button.GetType().FullName },
                { "deviceEntity", device.playerEntityId }, { "verified", verified },
                { "down", history.Down }, { "pressClaimed", history.PressClaimed },
                { "releaseClaimed", history.ReleaseClaimed }
            };
            if (!verified) receipt["failure"] = VerificationFailure(button, device);
            focusDecisions.Add(receipt);
            if (focusDecisions.Count > 128) focusDecisions.RemoveAt(0);
        }
        private static string VerificationFailure(LogicalButtonBase button, TASLogicalButton device)
        {
            if (!emulationActive) return "emulation-inactive";
            if (device.owner == null) return "owner-destroyed";
            if (device.owner.GetInstanceID() != device.ownerInstanceId) return "owner-incarnation";
            if (!pads.ContainsKey(device.playerEntityId)) return "pad-absent";
            TASLogicalButton current;
            if (!devices.TryGetValue(device.deviceKey, out current) || !ReferenceEquals(current, device)) return "device-superseded";
            if ((int)EntitySerialisationRegistry.GetId(device.owner) != device.playerEntityId) return "registry-owner";
            var provider = device.owner.GetComponent<PlayerIDProvider>();
            if (provider == null) return "provider-missing";
            if (!provider.IsLocallyControlled()) return "provider-remote";
            ILogicalButton cursor = button;
            for (int depth = 0; depth < 8; depth++)
            {
                if (ReferenceEquals(cursor, device)) return "verified-inconsistent";
                if (cursor == null || cursor.GetType() != typeof(GateLogicalButton)) return "chain-type";
                if (gateChild == null) return "chain-field";
                if (!ReferenceEquals(ObservedDevice(cursor), device)) return "chain-untracked";
                cursor = gateChild.GetValue(cursor) as ILogicalButton;
            }
            return "chain-depth";
        }
        internal static bool IsVerifiedEmulatedButton(LogicalButtonBase button)
        {
            if (button == null || !emulationActive) return false;
            var device = ObservedDevice(button);
            TASLogicalButton current;
            if (device == null || device.owner == null || device.owner.GetInstanceID() != device.ownerInstanceId ||
                !pads.ContainsKey(device.playerEntityId) || !devices.TryGetValue(device.deviceKey, out current) ||
                !ReferenceEquals(current, device) || (int)EntitySerialisationRegistry.GetId(device.owner) != device.playerEntityId)
                return false;
            var provider = device.owner.GetComponent<PlayerIDProvider>();
            if (provider == null || !provider.IsLocallyControlled()) return false;
            // Follow only observed native gates; never evaluate or replace their
            // callback. A changed child, foreign wrapper, or cycle fails closed.
            ILogicalButton cursor = button;
            for (int depth = 0; depth < 8; depth++)
            {
                if (ReferenceEquals(cursor, device)) return true;
                if (cursor == null || cursor.GetType() != typeof(GateLogicalButton) || gateChild == null ||
                    !ReferenceEquals(ObservedDevice(cursor), device)) return false;
                cursor = gateChild.GetValue(cursor) as ILogicalButton;
            }
            return false;
        }
        private static void TrackHistory(LogicalButtonBase button, TASLogicalButton device)
        {
            foreach (var history in histories)
                if (ReferenceEquals(history.Button.Target, button)) return;
            histories.Add(new HistoryOwner { Button = new WeakReference(button), Device = device });
        }

        // Unity main thread only, once per accepted logical input frame. Reapplying
        // unchanged levels cannot create another edge. Protocol JustPressed/Released
        // hints do not manufacture events. Missing input is neutral once activated.
        public static void MarkNextInputReason(string reason)
        {
            nextInputReason = String.IsNullOrEmpty(reason) ? "unspecified" : reason;
        }
        public static void ApplyInputFrame(InputData input)
        {
            ApplyInputFrame(input, false);
        }
        public static void ApplyInputFrame(InputData input, bool preserveMissingControllerInput)
        {
            string reason = nextInputReason;
            nextInputReason = null;
            if (String.IsNullOrEmpty(reason)) reason = input != null && input.__isset.nextFrame ? "controller-frame" : "direct";
            int controllerFrame = input != null && input.__isset.nextFrame ? input.NextFrame : -1;
            bool preservedMissingInput = preserveMissingControllerInput && input != null && input.Input == null;
            var next = preservedMissingInput
                ? new Dictionary<int, PadState>(pads)
                : new Dictionary<int, PadState>();
            if (!preservedMissingInput && input != null && input.Input != null)
                foreach (var pair in input.Input)
                {
                    if (pair.Value == null) throw new ArgumentException("Null chef input.");
                    var value = pair.Value;
                    double x = value.Pad == null ? 0 : value.Pad.X;
                    double y = value.Pad == null ? 0 : value.Pad.Y;
                    ValidateAxis(x); ValidateAxis(y);
                    next.Add(pair.Key, new PadState {
                        X = (float)x, Y = (float)y,
                        Pickup = value.Pickup != null && value.Pickup.Down,
                        Use = value.Interact != null && value.Interact.Down,
                        Dash = value.Dash != null && value.Dash.Down
                    });
                }
            bool pickupChanged = false, pickupDown = false;
            foreach (var pair in next)
            {
                PadState prior;
                if (pair.Value.Pickup) pickupDown = true;
                if (!pads.TryGetValue(pair.Key, out prior) || prior.Pickup != pair.Value.Pickup) pickupChanged = true;
            }
            foreach (var pair in pads) if (!next.ContainsKey(pair.Key) && pair.Value.Pickup) pickupChanged = true;
            var beforeDownHistories = new List<Dictionary<string, object>>();
            foreach (var owner in LiveHistories())
            {
                PadState incoming;
                var button = owner.Button.Target as LogicalButtonBase;
                if (button != null && owner.Device.type == TASLogicalButtonType.Pickup &&
                    next.TryGetValue(owner.Device.playerEntityId, out incoming) && incoming.Pickup)
                    beforeDownHistories.Add(HistoryRow(button, owner.Device));
            }
            pads = next;
            lastAppliedControllerFrame = controllerFrame;
            lastInputReason = reason;
            if (input != null && input.Input != null) emulationActive = true;
            foreach (var device in devices.Values)
                if (device.owner != null) device.Update(device.IsDown());
            if (pickupChanged || pickupDown || preservedMissingInput && (input.RequestResume || input.RequestPause) ||
                (reason != "controller-frame" && reason != "controller-reply" && reason != "control-pause"))
            {
                var levels = new Dictionary<string, object>();
                foreach (var pair in pads) if (pair.Value.Pickup) levels[pair.Key.ToString()] = true;
                var receipt = new Dictionary<string, object> {
                    { "sequence", ++inputApplicationSequence }, { "unityFrame", Time.frameCount },
                    { "unityTime", Time.time }, { "applicationFocused", Application.isFocused },
                    { "controllerFrame", controllerFrame }, { "reason", reason },
                    { "preservedMissingInput", preservedMissingInput },
                    { "requestResume", input != null && input.RequestResume },
                    { "requestPause", input != null && input.RequestPause }, { "pickupDown", levels }
                };
                if (pickupDown)
                {
                    var afterHistories = new List<Dictionary<string, object>>();
                    foreach (var owner in LiveHistories())
                    {
                        var button = owner.Button.Target as LogicalButtonBase;
                        if (button == null || owner.Device.type != TASLogicalButtonType.Pickup || !owner.Device.IsDown()) continue;
                        afterHistories.Add(HistoryRow(button, owner.Device));
                    }
                    receipt["beforeHistories"] = beforeDownHistories;
                    receipt["afterHistories"] = afterHistories;
                }
                inputApplications.Add(receipt);
                if (inputApplications.Count > 128) inputApplications.RemoveAt(0);
            }
        }
        private static Dictionary<string, object> HistoryRow(LogicalButtonBase button, TASLogicalButton device)
        {
            var history = ReadHistory(button);
            return new Dictionary<string, object> {
                { "identity", RuntimeHelpers.GetHashCode(button) }, { "buttonType", button.GetType().FullName },
                { "deviceEntity", device.playerEntityId }, { "verified", IsVerifiedEmulatedButton(button) },
                { "down", history.Down }, { "pressClaimed", history.PressClaimed },
                { "releaseClaimed", history.ReleaseClaimed }
            };
        }

        // Fresh load/disconnect: release levels and consume old claims, retaining
        // emulation ownership once activated. Main thread only, including when paused.
        public static void ResetAll()
        {
            pads = new Dictionary<int, PadState>();
            resetGeneration++;
            pickupPolls.Clear();
            inputApplications.Clear();
            neutralPreparations.Clear();
            focusDecisions.Clear();
            foreach (var history in LiveHistories())
                WriteHistory(history.Button.Target as LogicalButtonBase, new ButtonHistory {
                    PressClaimed = true, ReleaseClaimed = true, DownTime = Time.time
                }, 0);
        }

        // Process-local authoring checkpoint; requires the same chef incarnations
        // and gate objects. This does not restore world state or native clocks.
        public static InputCheckpoint CaptureCheckpoint()
        {
            var checkpoint = new InputCheckpoint {
                NativeTime = Time.time, Generation = resetGeneration, Active = emulationActive,
                Pads = new Dictionary<int, PadState>(pads), Histories = new List<SavedHistory>()
            };
            foreach (var history in LiveHistories())
            {
                var button = history.Button.Target as LogicalButtonBase;
                if (button == null) continue;
                checkpoint.Histories.Add(new SavedHistory {
                    Button = button, Device = history.Device, OwnerInstanceId = history.Device.ownerInstanceId,
                    History = ReadHistory(button)
                });
            }
            return checkpoint;
        }

        public static void RestoreCheckpoint(InputCheckpoint checkpoint)
        {
            ValidateCheckpoint(checkpoint);
            float clockOffset = Time.time - checkpoint.NativeTime;
            pads = new Dictionary<int, PadState>(checkpoint.Pads);
            emulationActive = checkpoint.Active;
            foreach (var saved in checkpoint.Histories) WriteHistory(saved.Button, saved.History, clockOffset);
        }

        public static void ValidateCheckpoint(InputCheckpoint checkpoint)
        {
            if (checkpoint == null || checkpoint.Generation != resetGeneration)
                throw new InvalidOperationException("Input checkpoint is from another reset generation.");
            var current = LiveHistories();
            if (current.Count != checkpoint.Histories.Count)
                throw new InvalidOperationException("Native input/gate membership changed since checkpoint.");
            foreach (var saved in checkpoint.Histories)
            {
                bool found = false;
                foreach (var live in current)
                    if (ReferenceEquals(live.Button.Target, saved.Button) && ReferenceEquals(live.Device, saved.Device)) found = true;
                if (!found || saved.Device.owner == null || saved.Device.owner.GetInstanceID() != saved.OwnerInstanceId)
                    throw new InvalidOperationException("Chef incarnation or native input gate changed since checkpoint.");
            }
            float clockOffset = Time.time - checkpoint.NativeTime;
            if (float.IsNaN(clockOffset) || float.IsInfinity(clockOffset)) throw new InvalidOperationException("Invalid native checkpoint clock.");
        }

        private static List<HistoryOwner> LiveHistories()
        {
            var live = new List<HistoryOwner>();
            for (int i = histories.Count - 1; i >= 0; i--)
            {
                var history = histories[i];
                if (!history.Button.IsAlive || history.Device.owner == null) histories.RemoveAt(i);
                else live.Add(history);
            }
            return live;
        }
        internal static bool TryAxis(int entityId, TASLogicalValueType type, out float value)
        {
            PadState pad;
            value = pads.TryGetValue(entityId, out pad) ? (type == TASLogicalValueType.MovementX ? pad.X : pad.Y) : 0;
            return emulationActive;
        }
        public override bool IsDown()
        {
            if (!emulationActive) return original.IsDown();
            PadState pad;
            if (owner == null || !pads.TryGetValue(playerEntityId, out pad)) return false;
            return type == TASLogicalButtonType.Pickup ? pad.Pickup : type == TASLogicalButtonType.Use ? pad.Use : pad.Dash;
        }
        public override void GetLogicTreeData(out AcyclicGraph<ILogicalElement, LogicalLinkInfo> graph, out AcyclicGraph<ILogicalElement, LogicalLinkInfo>.Node head)
        {
            original.GetLogicTreeData(out graph, out head);
        }
        private static void ValidateAxis(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < -1 || value > 1)
                throw new ArgumentException("TAS pad axes must be finite and within [-1, 1].");
        }
        private static FieldInfo NativeField(string name)
        {
            var field = typeof(LogicalButtonBase).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(typeof(LogicalButtonBase).FullName, name);
            return field;
        }
        private static ButtonHistory ReadHistory(LogicalButtonBase button)
        {
            return new ButtonHistory {
                PressClaimed = (bool)pressClaimed.GetValue(button), ReleaseClaimed = (bool)releaseClaimed.GetValue(button),
                DownTime = (float)buttonDownTime.GetValue(button), DownLength = (float)buttonDownLength.GetValue(button), Down = (bool)down.GetValue(button)
            };
        }
        private static void WriteHistory(LogicalButtonBase button, ButtonHistory history, float clockOffset)
        {
            pressClaimed.SetValue(button, history.PressClaimed); releaseClaimed.SetValue(button, history.ReleaseClaimed);
            buttonDownTime.SetValue(button, history.DownTime + clockOffset); buttonDownLength.SetValue(button, history.DownLength); down.SetValue(button, history.Down);
        }
        internal struct PadState { public float X, Y; public bool Pickup, Use, Dash; }
        internal struct ButtonHistory { public bool PressClaimed, ReleaseClaimed, Down; public float DownTime, DownLength; }
        private sealed class HistoryOwner { public WeakReference Button; public TASLogicalButton Device; }
        internal sealed class SavedHistory { public LogicalButtonBase Button; public TASLogicalButton Device; public int OwnerInstanceId; public ButtonHistory History; }
        public sealed class InputCheckpoint
        {
            internal float NativeTime;
            internal int Generation;
            internal bool Active;
            internal Dictionary<int, PadState> Pads;
            internal List<SavedHistory> Histories;
            public float CapturedNativeTime { get { return NativeTime; } }
            public int ResetGeneration { get { return Generation; } }
            public int ButtonHistoryCount { get { return Histories.Count; } }
        }
    }

    public sealed class TASLogicalValue : ILogicalValue
    {
        private readonly int playerEntityId;
        private readonly TASLogicalValueType type;
        private readonly ILogicalValue original;
        private readonly GameObject owner;
        public TASLogicalValue(int entityId, TASLogicalValueType valueType, ILogicalValue source, GameObject chef) { playerEntityId = entityId; type = valueType; original = source; owner = chef; }
        public void GetLogicTreeData(out AcyclicGraph<ILogicalElement, LogicalLinkInfo> graph, out AcyclicGraph<ILogicalElement, LogicalLinkInfo>.Node head) { original.GetLogicTreeData(out graph, out head); }
        public float GetValue() { float value; return TASLogicalButton.TryAxis(playerEntityId, type, out value) ? (owner == null ? 0 : value) : original.GetValue(); }
    }
}

