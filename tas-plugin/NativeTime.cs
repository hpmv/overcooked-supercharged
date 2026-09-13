using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using Team17.Online;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace Oc2Tas
{
    [Serializable]
    public sealed class RecipeDrawState
    {
        public long index, frame, roundDrawIndex;
        public int roundInstanceOrdinal;
        public string generator, before, after, randomScope;
        public bool isolated;
        public int[] recipeIds = new int[0];
        public int[] frequencies = new int[0];
    }

    [Serializable]
    public sealed class LifecycleMarker
    {
        public long index, frame, fixedFrame;
        public string source, state;
        public int physicsStepsThisFrame, framesSinceNoPhysics;
        public float clientTime, clientDeltaTime, unityTime, unityFixedTime;
    }

    // Replaces the clock source inside the native clock methods, not the methods
    // themselves: native offsets, interpolation, 3-second synchronization, and
    // all game/network event delays still execute through the original code.
    public static class NativeTime
    {
        public static long Frame;
        public static long FixedFrame;
        public static bool Active { get; private set; }
        // Off by default. Enable only after traces demonstrate shared RNG drift.
        public static bool IsolateRecipeRandom;
        // Experimental start alignment: wait for an ordinary render frame with
        // no native physics step before releasing the already-ready loader.
        public static bool AlignStartPhysics = true;
        public static int StartAlignmentWaitFrames { get; private set; }
        public static long StartAlignmentReleaseFrame { get; private set; } = -1;
        public static int Seed { get; private set; }
        public static long LevelFrameZero { get; private set; } = -1;
        public static long LevelFixedFrameZero { get; private set; } = -1;
        public static long IntroFrameZero { get; private set; } = -1;
        public static int PhysicsStepsThisFrame { get; private set; }
        public static int FramesSinceNoPhysics { get; private set; } = -1;
        public static int LevelStartPhysicsPhase { get; private set; } = -1;
        public static float LevelClientTimeZero { get; private set; }
        public static bool ServerRoundActive { get; private set; }
        public static bool ClientRoundActive { get; private set; }
        public static bool LevelReady { get { return LevelFrameZero >= 0 && ServerRoundActive && ClientRoundActive; } }
        public static long GameplayFrame { get { return LevelFrameZero < 0 ? -1 : Frame - LevelFrameZero; } }
        public static long GameplayFixedFrame { get { return LevelFixedFrameZero < 0 ? -1 : FixedFrame - LevelFixedFrameZero; } }
        private static long lastFixedAtAdvance;
        private static readonly List<RecipeDrawState> Draws = new List<RecipeDrawState>();
        private static readonly List<LifecycleMarker> Lifecycle = new List<LifecycleMarker>();
        // Native RoundData assets can outlive a kitchen and serve more than one
        // mutable RoundInstanceData. Key by data identity, never by the asset or
        // the currently selected scene: an outgoing round can still draw while
        // the replacement kitchen loads asynchronously.
        private sealed class RecipeStream
        {
            public int ordinal;
            public long drawCount;
            public UnityEngine.Random.State state;
            public RecipeStream Clone()
            { return new RecipeStream { ordinal = ordinal, drawCount = drawCount, state = state }; }
        }
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) { return object.ReferenceEquals(a, b); }
            public int GetHashCode(object value) { return RuntimeHelpers.GetHashCode(value); }
        }
        private static readonly Dictionary<object, RecipeStream> RecipeStreams =
            new Dictionary<object, RecipeStream>(new ReferenceComparer());
        private static UnityEngine.Random.State isolatedSeedState;
        private static int nextRecipeRoundOrdinal;
        private static readonly MethodInfo RealClock = AccessTools.PropertyGetter(typeof(Time), "realtimeSinceStartup");
        private static readonly MethodInfo TasClock = typeof(NativeTime).GetMethod("Realtime");
        [ThreadStatic] private static int drawDepth;
        [ThreadStatic] private static bool recipeObserverBypass;
        private static int unityThreadId;

        public static void Install(Harmony harmony)
        {
            if (harmony == null) throw new ArgumentNullException("harmony");
            unityThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            PatchClock(harmony, typeof(ClientTime), "Update");
            PatchClock(harmony, typeof(ClientTime), "Time");
            PatchClock(harmony, typeof(ClientTime), "OnTimeSyncReceived");
            PatchClock(harmony, typeof(ServerTime), "Update");
            harmony.Patch(AccessTools.Method(typeof(ServerKitchenLoader), "Update"),
                new HarmonyMethod(typeof(NativeTime).GetMethod("GateKitchenStart", BindingFlags.Static | BindingFlags.NonPublic)));

            PatchObserver(harmony, typeof(ServerKitchenLoader), "ChangeGameState", "ObserveGameState");
            PatchObserver(harmony, typeof(ServerFlowControllerBase), "ChangeGameState", "ObserveGameState");
            PatchObserver(harmony, typeof(ClientKitchenLoader), "OnGameStateChanged", "ObserveReceivedGameState");
            PatchObserver(harmony, typeof(ClientFlowControllerBase), "OnGameStateChanged", "ObserveReceivedGameState");
            PatchObserver(harmony, typeof(ServerFlowControllerBase), "SetRoundBehaviourActivation", "ObserveRoundActivation");
            PatchObserver(harmony, typeof(ClientFlowControllerBase), "SetRoundBehaviourActivation", "ObserveRoundActivation");

            // Each concrete native implementation is observed. Nested calls
            // (for example ScriptedRoundData calling RoundData) form one draw.
            foreach (Type type in typeof(RoundDataBase).Assembly.GetTypes())
            {
                if (!typeof(RoundDataBase).IsAssignableFrom(type)) continue;
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (method.Name != "GetNextRecipe" || method.IsAbstract || method.GetMethodBody() == null) continue;
                    harmony.Patch(method,
                        new HarmonyMethod(typeof(NativeTime).GetMethod("BeforeRecipe", BindingFlags.Static | BindingFlags.NonPublic)),
                        new HarmonyMethod(typeof(NativeTime).GetMethod("AfterRecipe", BindingFlags.Static | BindingFlags.NonPublic)),
                        null,
                        new HarmonyMethod(typeof(NativeTime).GetMethod("RecipeFinally", BindingFlags.Static | BindingFlags.NonPublic)), null);
                }
            }
        }

        private static void PatchClock(Harmony harmony, Type type, string name)
        {
            MethodInfo method = AccessTools.Method(type, name);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            harmony.Patch(method, null, null,
                new HarmonyMethod(typeof(NativeTime).GetMethod("ReplaceRealtime", BindingFlags.Static | BindingFlags.NonPublic)));
        }

        private static void PatchObserver(Harmony harmony, Type type, string name, string observer)
        {
            MethodInfo method = AccessTools.Method(type, name);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            harmony.Patch(method, null,
                new HarmonyMethod(typeof(NativeTime).GetMethod(observer, BindingFlags.Static | BindingFlags.NonPublic)));
        }

        private static IEnumerable<CodeInstruction> ReplaceRealtime(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                    object.Equals(instruction.operand, RealClock))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = TasClock;
                }
                yield return instruction;
            }
        }

        // Must be called immediately BEFORE starting/restarting a native level.
        // A fresh epoch is not safe for resuming an already-running level because
        // that level may contain timestamps established in the previous epoch.
        public static void Begin(int seed)
        {
            if (Math.Abs(Time.fixedDeltaTime - 0.02f) > 0.000001f)
                throw new InvalidOperationException("Expected native physics timestep 0.02; observed " + Time.fixedDeltaTime);
            Seed = seed;
            Frame = 0;
            FixedFrame = 0;
            lastFixedAtAdvance = 0;
            PhysicsStepsThisFrame = 0;
            FramesSinceNoPhysics = -1;
            LevelFrameZero = -1;
            LevelFixedFrameZero = -1;
            IntroFrameZero = -1;
            LevelStartPhysicsPhase = -1;
            LevelClientTimeZero = 0f;
            StartAlignmentWaitFrames = 0;
            StartAlignmentReleaseFrame = -1;
            ServerRoundActive = false;
            ClientRoundActive = false;
            Draws.Clear();
            RecipeStreams.Clear();
            nextRecipeRoundOrdinal = 0;
            Lifecycle.Clear();
            drawDepth = 0;
            EntityRegistrationAudit.BeginRound();
            Telemetry.Reset();
            GameEvents.Reset();
            ResetStaticFloats(typeof(ClientTime), new string[] { "m_fDelta", "m_fLocalRunningTime",
                "m_fLocalTimeLastFrame", "m_fLastReceivedTime", "m_fCurrentOffset", "m_fOldOffset" });
            ResetStaticFloats(typeof(ServerTime), new string[] { "m_fNextSyncTime", "m_fServerTime", "m_fLastTime" });
            UnityEngine.Random.InitState(seed);
            // Copying this value for a new native instance is equivalent to
            // InitState(seed), without advancing or replacing ambient RNG.
            isolatedSeedState = UnityEngine.Random.state;
            Time.captureFramerate = 60;
            Active = true;
        }

        private static void ResetStaticFloats(Type type, string[] names)
        {
            foreach (string name in names)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
                if (field == null || field.FieldType != typeof(float))
                    throw new MissingFieldException(type.FullName, name);
                field.SetValue(null, 0f);
            }
        }

        public static void Advance()
        {
            if (!Active) return;
            Frame++;
            PhysicsStepsThisFrame = (int)(FixedFrame - lastFixedAtAdvance);
            lastFixedAtAdvance = FixedFrame;
            if (PhysicsStepsThisFrame == 0) FramesSinceNoPhysics = 0;
            else if (FramesSinceNoPhysics >= 0) FramesSinceNoPhysics++;
            // Keep native fixedDeltaTime, physics simulation, and TimeManager's
            // pause layers untouched. The host gates whole completed frames.
            Time.captureFramerate = 60;
        }

        public static float Realtime()
        {
            return Active ? (float)(Frame / 60.0) : Time.realtimeSinceStartup;
        }

        public static RecipeDrawState[] GetRecipeDraws()
        {
            return Draws.ToArray();
        }

        public static int CurrentRecipeRoundInstanceOrdinal
        {
            get
            {
                object instance = CurrentRecipeRoundInstance();
                RecipeStream stream;
                return instance != null && RecipeStreams.TryGetValue(instance, out stream) ? stream.ordinal : -1;
            }
        }

        public static RecipeDrawState[] GetCurrentRecipeDraws()
        {
            int ordinal = CurrentRecipeRoundInstanceOrdinal;
            if (ordinal < 0) return new RecipeDrawState[0];
            List<RecipeDrawState> selected = new List<RecipeDrawState>();
            foreach (RecipeDrawState draw in Draws)
                if (draw.roundInstanceOrdinal == ordinal) selected.Add(draw);
            return selected.ToArray();
        }

        private static object CurrentRecipeRoundInstance()
        {
            ServerKitchenFlowControllerBase flow = UnityEngine.Object.FindObjectOfType<ServerKitchenFlowControllerBase>();
            if (flow == null) return null;
            ServerTeamMonitor monitor = flow.GetMonitorForTeam(TeamID.One);
            return monitor == null ? null : NativeFields.Get(monitor.OrdersController, "m_roundInstanceData");
        }

        private static RecipeStream StreamFor(object instance)
        {
            if (instance == null) throw new InvalidOperationException("Native recipe draw has no RoundInstanceData.");
            RecipeStream stream;
            if (!RecipeStreams.TryGetValue(instance, out stream))
            {
                stream = new RecipeStream { ordinal = nextRecipeRoundOrdinal++, state = isolatedSeedState };
                RecipeStreams.Add(instance, stream);
            }
            return stream;
        }

        public static LifecycleMarker[] GetLifecycleMarkers()
        {
            return Lifecycle.ToArray();
        }

        private static bool GateKitchenStart(ServerKitchenLoader __instance)
        {
            if (!Active || !AlignStartPhysics ||
                !object.Equals(NativeFields.Get(__instance, "m_State"), GameState.StartEntities) ||
                !UserSystemUtils.AreAllUsersInGameState(ServerUserSystem.m_Users, GameState.StartedEntities))
                return true;
            if (PhysicsStepsThisFrame == 0)
            {
                StartAlignmentReleaseFrame = Frame;
                AddMarker("TASStartAlignment", "ReleaseReadyLoaderOnNoPhysicsFrame");
                return true;
            }
            // The complete native RunKitchen + StartFlow readiness branch waits
            // together. All other updates, synchronization and physics continue;
            // the native intro, entity state and gameplay delays are untouched.
            StartAlignmentWaitFrames++;
            AddMarker("TASStartAlignment", "WaitForNaturalNoPhysicsFrame");
            return false;
        }

        private static void ObserveGameState(MethodBase __originalMethod, object[] __args)
        {
            if (!Active || __args == null || __args.Length == 0) return;
            string state = Convert.ToString(__args[0]);
            if (__originalMethod.DeclaringType == typeof(ServerFlowControllerBase) && state == "RunLevelIntro" && IntroFrameZero < 0)
                IntroFrameZero = Frame;
            AddMarker(__originalMethod.DeclaringType.Name, state);
        }

        private static void ObserveReceivedGameState(MethodBase __originalMethod, object[] __args)
        {
            if (!Active || __args == null || __args.Length < 2) return;
            AddMarker(__originalMethod.DeclaringType.Name,
                Convert.ToString(NativeFields.Get(__args[1], "m_State")));
        }

        private static void ObserveRoundActivation(object __instance, object[] __args)
        {
            if (!Active || __args == null || __args.Length == 0) return;
            bool enabled = Convert.ToBoolean(__args[0]);
            bool server = __instance is ServerFlowControllerBase;
            if (server) ServerRoundActive = enabled;
            else ClientRoundActive = enabled;
            AddMarker(server ? "ServerRound" : "ClientRound", enabled ? "Active" : "Inactive");
            if (LevelFrameZero < 0 && ServerRoundActive && ClientRoundActive)
            {
                // A read-only epoch: do not alter Time, any entity, the timer,
                // synchronization queues, or intro coroutine. At the next whole
                // frame boundary both native round loops have begun. All later
                // gameplay frames can be compared relative to this boundary.
                LevelFrameZero = Frame;
                LevelFixedFrameZero = FixedFrame;
                LevelStartPhysicsPhase = FramesSinceNoPhysics;
                LevelClientTimeZero = ClientTime.Time();
                AddMarker("TASObservation", "BothRoundsActive");
            }
        }

        private static void AddMarker(string source, string state)
        {
            Lifecycle.Add(new LifecycleMarker { index = Lifecycle.Count, frame = Frame, fixedFrame = FixedFrame,
                source = source, state = state, physicsStepsThisFrame = PhysicsStepsThisFrame,
                framesSinceNoPhysics = FramesSinceNoPhysics, clientTime = ClientTime.Time(),
                clientDeltaTime = ClientTime.DeltaTime(), unityTime = Time.time, unityFixedTime = Time.fixedTime });
        }

        public sealed class DrawContext
        {
            public bool active, outer, finished, isolated;
            public UnityEngine.Random.State ambient;
            public RecipeDrawState record;
            public object instanceData;
            internal object stream;
        }

        // A planning transaction around a fresh RoundData instance only. It
        // suppresses our recipe hooks, never any native order/gameplay method.
        internal sealed class RecipePreviewGuard : IDisposable
        {
            private readonly UnityEngine.Random.State ambient, seedState;
            private readonly Dictionary<object, RecipeStream> streams;
            private readonly int nextOrdinal;
            private readonly RecipeDrawState[] draws;
            private readonly LifecycleMarker[] lifecycle;
            private readonly bool bypass, isolate;
            private readonly int depth;
            private bool disposed;
            internal readonly string AmbientBefore, IsolatedBefore;
            internal readonly long FrameBefore, FixedFrameBefore;
            internal readonly int DrawCountBefore, LifecycleCountBefore;
            internal readonly string RegistryBefore;
            internal readonly int LiveRoundOrdinalBefore;

            internal RecipePreviewGuard()
            {
                if (System.Threading.Thread.CurrentThread.ManagedThreadId != unityThreadId)
                    throw new InvalidOperationException("Recipe preview requires Unity's main thread.");
                if (drawDepth != 0 || recipeObserverBypass)
                    throw new InvalidOperationException("Recipe preview cannot nest inside a native recipe draw or another preview.");
                ambient = UnityEngine.Random.state;
                seedState = isolatedSeedState;
                streams = new Dictionary<object, RecipeStream>(new ReferenceComparer());
                foreach (KeyValuePair<object, RecipeStream> item in RecipeStreams)
                    streams.Add(item.Key, item.Value.Clone());
                nextOrdinal = nextRecipeRoundOrdinal;
                draws = Draws.ToArray();
                lifecycle = Lifecycle.ToArray();
                bypass = recipeObserverBypass;
                isolate = IsolateRecipeRandom;
                depth = drawDepth;
                AmbientBefore = RandomStateFingerprint(ambient);
                IsolatedBefore = IsolatedRecipeStateFingerprint();
                RegistryBefore = RecipeStreamRegistryFingerprint();
                LiveRoundOrdinalBefore = CurrentRecipeRoundInstanceOrdinal;
                FrameBefore = Frame;
                FixedFrameBefore = FixedFrame;
                DrawCountBefore = draws.Length;
                LifecycleCountBefore = lifecycle.Length;
                recipeObserverBypass = true;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                try { UnityEngine.Random.state = ambient; }
                finally
                {
                    isolatedSeedState = seedState;
                    RecipeStreams.Clear();
                    foreach (KeyValuePair<object, RecipeStream> item in streams)
                        RecipeStreams.Add(item.Key, item.Value.Clone());
                    nextRecipeRoundOrdinal = nextOrdinal;
                    Draws.Clear(); Draws.AddRange(draws);
                    Lifecycle.Clear(); Lifecycle.AddRange(lifecycle);
                    IsolateRecipeRandom = isolate;
                    drawDepth = depth;
                    recipeObserverBypass = bypass;
                }
            }
        }

        internal static string IsolatedRecipeStateFingerprint()
        {
            object instance = CurrentRecipeRoundInstance();
            if (instance == null) return "no-live-native-round";
            RecipeStream stream;
            return RandomStateFingerprint(RecipeStreams.TryGetValue(instance, out stream) ? stream.state : isolatedSeedState);
        }

        internal static string RecipeStreamRegistryFingerprint()
        {
            List<RecipeStream> ordered = new List<RecipeStream>(RecipeStreams.Values);
            ordered.Sort(delegate(RecipeStream a, RecipeStream b) { return a.ordinal.CompareTo(b.ordinal); });
            StringBuilder value = new StringBuilder();
            value.Append("next=").Append(nextRecipeRoundOrdinal).Append(";seed=").Append(RandomStateFingerprint(isolatedSeedState));
            foreach (RecipeStream stream in ordered)
                value.Append(';').Append(stream.ordinal).Append('/').Append(stream.drawCount).Append('=').Append(RandomStateFingerprint(stream.state));
            return value.ToString();
        }

        private static void BeforeRecipe(object __instance, object[] __args, ref DrawContext __state)
        {
            __state = new DrawContext();
            if (!Active || recipeObserverBypass) return;
            __state.active = true;
            __state.outer = drawDepth++ == 0;
            if (!__state.outer) return;
            __state.isolated = IsolateRecipeRandom;
            __state.ambient = UnityEngine.Random.state;
            if (__args != null && __args.Length > 0) __state.instanceData = __args[0];
            RecipeStream stream = StreamFor(__state.instanceData);
            __state.stream = stream;
            if (__state.isolated) UnityEngine.Random.state = stream.state;
            __state.record = new RecipeDrawState { index = Draws.Count, frame = Frame,
                generator = __instance.GetType().Name, isolated = __state.isolated,
                roundInstanceOrdinal = stream.ordinal, roundDrawIndex = stream.drawCount++,
                randomScope = __state.isolated ? "native-round-instance" : "native-shared",
                before = RandomStateFingerprint(UnityEngine.Random.state) };
        }

        private static void AfterRecipe(RecipeList.Entry[] __result, DrawContext __state)
        {
            if (__state == null || !__state.active || !__state.outer) return;
            RecipeDrawState record = __state.record;
            record.after = RandomStateFingerprint(UnityEngine.Random.state);
            List<int> recipes = new List<int>();
            if (__result != null)
                foreach (RecipeList.Entry entry in __result)
                    recipes.Add(entry != null && entry.m_order != null ? entry.m_order.m_uID : 0);
            record.recipeIds = recipes.ToArray();
            int[] frequencies = NativeFields.Get(__state.instanceData, "CumulativeFrequencies") as int[];
            if (frequencies != null) record.frequencies = (int[])frequencies.Clone();
            Draws.Add(record);
        }

        private static Exception RecipeFinally(Exception __exception, DrawContext __state)
        {
            if (__state != null && __state.active && !__state.finished)
            {
                __state.finished = true;
                drawDepth--;
                if (__state.outer && __state.isolated)
                {
                    try
                    {
                        RecipeStream stream = __state.stream as RecipeStream;
                        if (stream != null) stream.state = UnityEngine.Random.state;
                    }
                    finally { UnityEngine.Random.state = __state.ambient; }
                }
            }
            return __exception;
        }

        public static string RandomStateFingerprint(UnityEngine.Random.State state)
        {
            // Read the actual native RNG state rather than advancing it with a
            // diagnostic Random.value call. Names/order are stable in this build.
            FieldInfo[] fields = typeof(UnityEngine.Random.State).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Array.Sort(fields, delegate(FieldInfo a, FieldInfo b) { return StringComparer.Ordinal.Compare(a.Name, b.Name); });
            object boxed = state;
            StringBuilder text = new StringBuilder();
            foreach (FieldInfo field in fields)
            {
                if (text.Length > 0) text.Append(':');
                object value = field.GetValue(boxed);
                text.Append(field.Name).Append('=').Append(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }
    }
}
