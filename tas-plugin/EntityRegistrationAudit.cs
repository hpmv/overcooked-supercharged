using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oc2Tas
{
    [Serializable]
    public sealed class RegisteredEntityState
    {
        public int entityId, unityInstanceId;
        public long observedRegistrationSequence = -1;
        public string name, hierarchyPath, scene;
        public bool active;
        internal RegisteredEntityState Copy() { return (RegisteredEntityState)MemberwiseClone(); }
    }

    [Serializable]
    public sealed class EntityRegistryCheckpoint
    {
        public string kind, interpretation;
        public long afterSequence, frame, fixedFrame;
        public int roundEpoch;
        public bool available;
        public RegisteredEntityState[] entities = new RegisteredEntityState[0];
        internal EntityRegistryCheckpoint Copy()
        {
            EntityRegistryCheckpoint copy = (EntityRegistryCheckpoint)MemberwiseClone();
            copy.entities = EntityRegistrationAudit.CopyEntities(entities);
            return copy;
        }
    }

    [Serializable]
    public sealed class EntityRegistrationEvent
    {
        public long sequence, frame, fixedFrame, gameplayFrame;
        public int roundEpoch, unityFrame, registryCountAfter;
        public string kind, method;
        public RegisteredEntityState entity;
        // Clear bypasses RemoveEntry. These are the entries observed immediately
        // before the native bulk clear, not fabricated individual remove calls.
        public RegisteredEntityState[] clearedEntities = new RegisteredEntityState[0];
        internal EntityRegistrationEvent Copy()
        {
            EntityRegistrationEvent copy = (EntityRegistrationEvent)MemberwiseClone();
            copy.entity = entity == null ? null : entity.Copy();
            copy.clearedEntities = EntityRegistrationAudit.CopyEntities(clearedEntities);
            return copy;
        }
    }

    [Serializable]
    public sealed class EntityRegistrationAuditState
    {
        public bool installed, addHookInstalled, removeHookInstalled, clearHookInstalled;
        public bool returnedHistoryLimited, cursorPredatesRoundCheckpoint;
        public string status, error, owner, nativeModuleVersionId, interpretation;
        public int capacity, retainedCount, roundEpoch, returnedLimit;
        public long lastSequence, oldestRetainedSequence, returnedAfterSequence;
        public long roundDropped, lifetimeDropped, lifetimeDiscardedAtRoundBegin, errorCount;
        public EntityRegistryCheckpoint initialPreexisting, roundBegin;
        public EntityRegistrationEvent[] events = new EntityRegistrationEvent[0];
    }

    [Serializable]
    public sealed class LoadedRoundDurationState
    {
        public bool available, loadedRoundDataAvailable, timerAvailable, valuesAgree;
        public float seconds = -1f, loadedRoundDataSeconds = -1f, timerLimitSeconds = -1f;
        public string source, scene, levelConfigName, roundDataType, timerType, error;
    }

    // This observes NETWORK ENTITY REGISTRATION, never Unity object creation.
    // Install from Plugin.Awake before starting a load, on Unity's main thread.
    // The injected calls occur after native dictionary/list writes and before
    // native OnEntryAdded/OnEntryRemoved callbacks; nested registrations retain
    // their actual order. No argument, result, branch, RNG, or game field changes.
    // ObserveClear brackets native Clear because it bypasses RemoveEntry.
    public static class EntityRegistrationAudit
    {
        private const int Capacity = 4096;
        private const int DefaultReturnedEvents = 256;
        private static readonly Queue<EntityRegistrationEvent> History = new Queue<EntityRegistrationEvent>();
        private static readonly Dictionary<uint, long> ActiveRegistrations = new Dictionary<uint, long>();
        private static EntityRegistryCheckpoint initial, roundBegin;
        private static bool installed, addInstalled, removeInstalled, clearInstalled;
        private static int mainThreadId, roundEpoch;
        private static long sequence, roundDropped, lifetimeDropped, discardedAtBegin, errorCount;
        private static string owner = "", lastError = "";

        public static void Install(Harmony harmony)
        {
            if (harmony == null) throw new ArgumentNullException("harmony");
            if (installed) return;
            mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            owner = harmony.Id;
            if (initial == null) initial = Checkpoint("initial-preexisting");
            try
            {
                Type registry = typeof(EntitySerialisationRegistry);
                if (!addInstalled)
                {
                    harmony.Patch(Required(registry, "AddEntry", new Type[] { typeof(GameObject), typeof(uint) }),
                        null, null, Hook("InstrumentAdd"));
                    addInstalled = true;
                }
                if (!removeInstalled)
                {
                    harmony.Patch(Required(registry, "RemoveEntry", new Type[] { typeof(EntitySerialisationEntry) }),
                        null, null, Hook("InstrumentRemove"));
                    removeInstalled = true;
                }
                if (!clearInstalled)
                {
                    harmony.Patch(Required(registry, "Clear", Type.EmptyTypes), Hook("BeforeClear"), Hook("AfterClear"));
                    clearInstalled = true;
                }
                installed = addInstalled && removeInstalled && clearInstalled;
            }
            catch (Exception ex) { Error(ex); throw; }
        }

        private static MethodInfo Required(Type type, string name, Type[] args)
        {
            MethodInfo method = AccessTools.Method(type, name, args);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static HarmonyMethod Hook(string name)
        { return new HarmonyMethod(typeof(EntityRegistrationAudit).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)); }

        private static IEnumerable<CodeInstruction> InstrumentAdd(IEnumerable<CodeInstruction> instructions)
        { return InsertBeforeCallback(instructions, "OnEntryAdded", "Add", "ObserveAdded", true); }

        private static IEnumerable<CodeInstruction> InstrumentRemove(IEnumerable<CodeInstruction> instructions)
        { return InsertBeforeCallback(instructions, "OnEntryRemoved", "Remove", "ObserveRemoved", false); }

        private static IEnumerable<CodeInstruction> InsertBeforeCallback(IEnumerable<CodeInstruction> instructions,
            string callback, string mutation, string observer, bool includeId)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            FieldInfo field = typeof(EntitySerialisationRegistry).GetField(callback);
            int anchor = -1;
            bool nativeListMutationBeforeCallback = false;
            for (int i = 0; i < code.Count; i++)
            {
                MethodInfo call = code[i].operand as MethodInfo;
                if (call != null && call.Name == mutation && call.DeclaringType == typeof(FastList<EntitySerialisationEntry>))
                    nativeListMutationBeforeCallback = true;
                if (code[i].opcode == OpCodes.Ldsfld && object.Equals(code[i].operand, field))
                { anchor = i; break; }
            }
            if (field == null || anchor < 0 || !nativeListMutationBeforeCallback)
                throw new InvalidOperationException("Unsupported native registry IL: no completed list " + mutation + " before " + callback + ".");
            CodeInstruction first = new CodeInstruction(OpCodes.Ldarg_0);
            first.labels.AddRange(code[anchor].labels);
            first.blocks.AddRange(code[anchor].blocks);
            code[anchor].labels.Clear();
            code[anchor].blocks.Clear();
            List<CodeInstruction> added = new List<CodeInstruction>();
            added.Add(first);
            if (includeId) added.Add(new CodeInstruction(OpCodes.Ldarg_1));
            added.Add(new CodeInstruction(OpCodes.Call,
                typeof(EntityRegistrationAudit).GetMethod(observer, BindingFlags.Static | BindingFlags.NonPublic)));
            code.InsertRange(anchor, added);
            return code;
        }

        private static bool OnMainThread()
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId == mainThreadId) return true;
            Error(new InvalidOperationException("Registry observation was called off the Unity thread; event coverage is incomplete."));
            return false;
        }

        private static EntityRegistrationEvent Event(string kind, string method)
        {
            return new EntityRegistrationEvent { sequence = ++sequence, roundEpoch = roundEpoch,
                frame = NativeTime.Frame, fixedFrame = NativeTime.FixedFrame, gameplayFrame = NativeTime.GameplayFrame,
                unityFrame = Time.frameCount, kind = kind, method = method,
                registryCountAfter = EntitySerialisationRegistry.m_Entities == null ? -1 : EntitySerialisationRegistry.m_Entities.Count };
        }

        private static void Append(EntityRegistrationEvent value)
        {
            if (History.Count == Capacity) { History.Dequeue(); roundDropped++; lifetimeDropped++; }
            History.Enqueue(value);
        }

        // Every observer catches its own failure so it cannot replace a native
        // exception or prevent a native callback. Errors invalidate full coverage.
        private static void ObserveAdded(GameObject gameObject, uint entityID)
        {
            try
            {
                if (!OnMainThread()) return;
                EntityRegistrationEvent value = Event("register", "EntitySerialisationRegistry.AddEntry(GameObject,uint)");
                ActiveRegistrations[entityID] = value.sequence;
                value.entity = Identity(entityID, gameObject);
                Append(value);
            }
            catch (Exception ex) { Error(ex); }
        }

        private static void ObserveRemoved(EntitySerialisationEntry entry)
        {
            try
            {
                if (!OnMainThread()) return;
                EntityRegistrationEvent value = Event("remove", "EntitySerialisationRegistry.RemoveEntry(EntitySerialisationEntry)");
                value.entity = Identity(entry.m_Header.m_uEntityID, entry.m_GameObject);
                ActiveRegistrations.Remove(entry.m_Header.m_uEntityID);
                Append(value);
            }
            catch (Exception ex) { Error(ex); }
        }

        private static void BeforeClear(out RegisteredEntityState[] __state)
        {
            __state = null;
            try { if (OnMainThread()) __state = Snapshot(); }
            catch (Exception ex) { Error(ex); }
        }

        private static void AfterClear(RegisteredEntityState[] __state)
        {
            try
            {
                if (!OnMainThread()) return;
                EntityRegistrationEvent value = Event("clear", "EntitySerialisationRegistry.Clear()");
                value.clearedEntities = __state ?? new RegisteredEntityState[0];
                ActiveRegistrations.Clear();
                Append(value);
            }
            catch (Exception ex) { Error(ex); }
        }

        private static RegisteredEntityState Identity(uint id, GameObject gameObject)
        {
            RegisteredEntityState value = new RegisteredEntityState { entityId = (int)id };
            long observed;
            if (ActiveRegistrations.TryGetValue(id, out observed)) value.observedRegistrationSequence = observed;
            if (gameObject != null)
            {
                value.unityInstanceId = gameObject.GetInstanceID();
                value.name = gameObject.name;
                value.scene = gameObject.scene.name;
                value.active = gameObject.activeInHierarchy;
                Transform current = gameObject.transform;
                value.hierarchyPath = current.name;
                // Hierarchy and instance ID are diagnostic anchors, not stable
                // identities or proof of the object's prefab/creation order.
                int depth = 0;
                while (current.parent != null && depth++ < 64)
                { current = current.parent; value.hierarchyPath = current.name + "/" + value.hierarchyPath; }
            }
            return value;
        }

        private static RegisteredEntityState[] Snapshot()
        {
            List<RegisteredEntityState> values = new List<RegisteredEntityState>();
            if (EntitySerialisationRegistry.m_Entities != null)
                foreach (KeyValuePair<uint, EntitySerialisationEntry> pair in EntitySerialisationRegistry.m_Entities)
                    values.Add(Identity(pair.Key, pair.Value == null ? null : pair.Value.m_GameObject));
            values.Sort(delegate(RegisteredEntityState a, RegisteredEntityState b) { return a.entityId.CompareTo(b.entityId); });
            return values.ToArray();
        }

        private static EntityRegistryCheckpoint Checkpoint(string kind)
        {
            EntityRegistryCheckpoint value = new EntityRegistryCheckpoint { kind = kind, afterSequence = sequence,
                roundEpoch = roundEpoch, frame = NativeTime.Frame, fixedFrame = NativeTime.FixedFrame,
                interpretation = "Existing registry snapshot sorted by native entity ID; not registration or Unity creation order. Sequence -1 means registration predates observation." };
            try
            {
                value.available = EntitySerialisationRegistry.m_Entities != null;
                value.entities = Snapshot();
            }
            catch (Exception ex) { Error(ex); }
            return value;
        }

        // Call once at the existing Begin/reset boundary, before native loading.
        // This snapshots even the outgoing kitchen, then discards this observer's
        // history window. It does not reset lifetime sequence, active registration
        // identities, native registry, native ID allocator, or any game state.
        public static void BeginRound()
        {
            if (!OnMainThread()) return;
            roundEpoch++;
            roundBegin = Checkpoint("round-begin-preexisting");
            discardedAtBegin += History.Count;
            History.Clear();
            roundDropped = 0;
        }

        // Default telemetry returns the latest 256 events without consuming them.
        // Use CaptureSince(cursor,limit) to page the retained window (max 4096).
        // Reported limits/drops/checkpoints prevent a partial window being mistaken
        // for a complete lifetime registration log.
        public static EntityRegistrationAuditState Capture()
        { return CaptureSince(Math.Max(0, sequence - DefaultReturnedEvents), DefaultReturnedEvents); }

        public static EntityRegistrationAuditState CaptureSince(long afterSequence, int maxEvents)
        {
            if (maxEvents < 1 || maxEvents > Capacity) throw new ArgumentOutOfRangeException("maxEvents");
            EntityRegistrationAuditState result = new EntityRegistrationAuditState { installed = installed,
                addHookInstalled = addInstalled, removeHookInstalled = removeInstalled, clearHookInstalled = clearInstalled,
                status = !installed ? "not-fully-installed" : errorCount == 0 ? "observing" : "observation-errors",
                error = lastError, errorCount = errorCount, owner = owner,
                nativeModuleVersionId = typeof(EntitySerialisationRegistry).Module.ModuleVersionId.ToString(),
                capacity = Capacity, retainedCount = History.Count, roundEpoch = roundEpoch,
                lastSequence = sequence, oldestRetainedSequence = History.Count == 0 ? -1 : History.Peek().sequence,
                returnedAfterSequence = afterSequence, returnedLimit = maxEvents,
                roundDropped = roundDropped, lifetimeDropped = lifetimeDropped, lifetimeDiscardedAtRoundBegin = discardedAtBegin,
                initialPreexisting = initial == null ? null : initial.Copy(), roundBegin = roundBegin == null ? null : roundBegin.Copy(),
                cursorPredatesRoundCheckpoint = roundBegin != null && afterSequence < roundBegin.afterSequence,
                interpretation = "Actual native network registry writes observed before native registration callbacks. No Unity creation hook. Clear is one bulk operation. Lifetime sequence survives round history resets; frames are in the current TAS clock epoch. Unobserved preexisting entries remain checkpoint-only." };
            List<EntityRegistrationEvent> values = new List<EntityRegistrationEvent>();
            foreach (EntityRegistrationEvent value in History)
            {
                if (value.sequence <= afterSequence) continue;
                if (values.Count == maxEvents) { result.returnedHistoryLimited = true; break; }
                values.Add(value.Copy());
            }
            if (History.Count > 0 && afterSequence >= History.Peek().sequence) result.returnedHistoryLimited = true;
            result.events = values.ToArray();
            return result;
        }

        public static LoadedRoundDurationState CaptureRoundDuration()
        {
            LoadedRoundDurationState result = new LoadedRoundDurationState { source = "ServerRoundTimer.m_timeLimit; loaded OrdersController.m_roundData.m_roundTimer" };
            try
            {
                result.scene = SceneManager.GetActiveScene().name;
                LevelConfigBase level = GameUtils.GetLevelConfig();
                if (level != null) result.levelConfigName = level.name;
                ServerKitchenFlowControllerBase flow = UnityEngine.Object.FindObjectOfType<ServerKitchenFlowControllerBase>();
                if (flow == null) return result;
                IServerRoundTimer timer = flow.RoundTimer;
                if (timer != null)
                {
                    result.timerType = timer.GetType().FullName;
                    object limit = NativeFields.Get(timer, "m_timeLimit");
                    if (limit != null)
                    { result.timerLimitSeconds = Convert.ToSingle(limit); result.timerAvailable = IsDuration(result.timerLimitSeconds); }
                }
                ServerTeamMonitor monitor = flow.GetMonitorForTeam(TeamID.One);
                RoundData round = NativeFields.Get(monitor == null ? null : monitor.OrdersController, "m_roundData") as RoundData;
                if (round != null)
                {
                    result.roundDataType = round.GetType().FullName;
                    result.loadedRoundDataSeconds = round.m_roundTimer;
                    result.loadedRoundDataAvailable = IsDuration(result.loadedRoundDataSeconds);
                }
                result.available = result.timerAvailable;
                if (result.available) result.seconds = result.timerLimitSeconds;
                result.valuesAgree = result.timerAvailable && result.loadedRoundDataAvailable && result.timerLimitSeconds == result.loadedRoundDataSeconds;
            }
            catch (Exception ex) { result.error = ex.GetType().Name + ": " + ex.Message; }
            return result;
        }

        private static bool IsDuration(float value) { return value > 0f && !float.IsInfinity(value) && !float.IsNaN(value); }
        private static void Error(Exception ex) { errorCount++; lastError = ex.GetType().Name + ": " + ex.Message; }
        internal static RegisteredEntityState[] CopyEntities(RegisteredEntityState[] values)
        {
            RegisteredEntityState[] result = new RegisteredEntityState[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = values[i].Copy();
            return result;
        }
    }
}
