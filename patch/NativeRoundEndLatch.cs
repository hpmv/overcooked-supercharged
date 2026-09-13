using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Hpmv;
using SuperchargedPatch.Bridge;
using Team17.Online;
using Team17.Online.Multiplayer;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SuperchargedPatch
{
    // Authoring-only transaction around the exact InLevel -> RunLevelOutro edge.
    // It never runs unless a paused checkpoint is explicitly armed.  The latch
    // prevents the first outro MoveNext, whose score/save/UI/audio side effects
    // have no inverse in the original game, while retaining the already scheduled
    // client RunLevel iterator and deferring the server deactivation callback.
    public static class NativeRoundEndLatch
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string[] simpleIteratorFields = { "$this", "$current", "$disposing", "$PC" };
        private static readonly string[] outroIteratorFields = { "<outro>__0", "$this", "$current", "$disposing", "$PC" };
        private static Target target;
        private static string phase = "idle", failure = "";
        private static int nonce, terminalFrame = -1, deferredServerPc = Int32.MinValue;
        private static bool serverDeactivationDeferred, serverTransitionObserved, clientPrefixObserved;
        private static IEnumerator clientRoutineBeforeHandler, dormantOutro;
        private static bool bypassServerDeactivation, restoredCapabilityPending;
        private static int advertisedCapabilityFeatures;
        private static int clientTimerZeroCalls;
        private static readonly List<object> terminalReceipts = new List<object>();
        private static ServerKitchenFlowControllerBase captureFlow;
        private static ClientKitchenFlowControllerBase captureClient;
        private static ClientKitchenLoader captureLoader;
        private static Type captureServerIteratorType, captureClientIteratorType;
        private static NativeIteratorCopy captureServerIterator, captureClientIterator;

        public static bool IsActive { get { return phase == "armed" || phase == "pending-pause" || phase == "held" || phase == "failed"; } }
        public static bool Held { get { return phase == "held"; } }
        public static bool PendingPause { get { return phase == "pending-pause"; } }
        public static bool FlowFreezeRequired { get { return Held || PendingPause || phase == "failed"; } }
        public static int CapabilityFeatures
        {
            get
            {
                int features = 1;
                if (PendingPause || Held) features |= 2;
                if (restoredCapabilityPending) features |= 4;
                if (phase == "failed") features |= 8;
                return features;
            }
        }

        public static object Arm(int frame)
        {
            if (IsActive) throw new InvalidOperationException("A native round-end latch is already active.");
            if (!Helpers.IsPaused()) throw new InvalidOperationException("Round-end latch arming requires a paused game.");
            Target candidate = NativeKitchenCheckpoint.RequireRoundEndTarget(frame);
            candidate.ValidateTargetBoundary();
            candidate.BindCurrentServerRoutine();
            target = candidate;
            phase = "armed"; failure = ""; terminalFrame = -1;
            serverDeactivationDeferred = serverTransitionObserved = clientPrefixObserved = false;
            clientRoutineBeforeHandler = dormantOutro = null;
            deferredServerPc = Int32.MinValue; clientTimerZeroCalls = 0;
            restoredCapabilityPending = false;
            nonce++;
            return Diagnostics();
        }

        public static object Cancel()
        {
            if (!IsActive) return Diagnostics();
            if (phase == "failed")
                throw new InvalidOperationException("A failed round-end latch requires a full game-process restart.");
            if (phase == "armed")
            {
                ClearActive("cancelled-before-terminal");
                return Diagnostics();
            }
            if (!serverDeactivationDeferred || target == null)
                throw new InvalidOperationException("Round-end latch cannot abort an incomplete terminal mutation.");
            try
            {
                bypassServerDeactivation = true;
                Method(target.Flow, "SetRoundBehaviourActivation", new[] { typeof(bool) }).Invoke(target.Flow, new object[] { false });
            }
            finally { bypassServerDeactivation = false; }
            Set(target.Client, "m_isFinished", true);
            Set(target.Client, "m_levelRoutine", null);
            Set(target.Client, "m_outroRoutine", dormantOutro);
            ClearActive("cancelled-at-terminal");
            return Diagnostics();
        }

        internal static void ResetForSceneRefresh()
        {
            if (FlowFreezeRequired)
                throw new InvalidOperationException("Cannot refresh native scene metadata while a pristine round-end latch is held.");
            if (phase == "armed") ClearActive("scene-refresh");
            if (phase != "idle") ClearActive("scene-refresh");
            restoredCapabilityPending = false; advertisedCapabilityFeatures = 0;
            captureFlow = null; captureClient = null; captureLoader = null;
            captureServerIteratorType = captureClientIteratorType = null;
            captureServerIterator = captureClientIterator = null;
        }

        internal static bool TryDeferServerDeactivation(ServerFlowControllerBase flow, bool enabled)
        {
            if (enabled || bypassServerDeactivation || phase != "armed") return false;
            try
            {
                if (target == null || !ReferenceEquals(flow, target.Flow))
                    throw new InvalidOperationException("A different server flow attempted terminal deactivation.");
                if (serverDeactivationDeferred)
                    throw new InvalidOperationException("Server terminal deactivation was requested more than once.");
                target.ValidateRuntimeIdentity();
                if ((GameState)Read(flow, "m_State") != GameState.InLevel || !(bool)Read(flow, "m_inRound"))
                    throw new InvalidOperationException("Server terminal deactivation did not start from active InLevel.");
                if ((bool)Read(flow, "m_skipToEnd") != target.ServerSkipToEnd ||
                    (target.CampaignFlow != null && (bool)Read(target.CampaignFlow, "m_LevelRestartRequested") != target.LevelRestartRequested))
                    throw new InvalidOperationException("Server skip/restart control changed before the natural timer terminal.");
                IEnumerator routine = (IEnumerator)Read(flow, "m_levelRoutine");
                if (!ReferenceEquals(routine, target.ServerLiveRoutine))
                    throw new InvalidOperationException("Server RunRound iterator incarnation changed before terminal.");
                deferredServerPc = IteratorPc(routine);
                if (deferredServerPc != -1)
                    throw new InvalidOperationException("Server deactivation was not called from the executing RunRound terminal continuation.");
                serverDeactivationDeferred = true;
                return true;
            }
            catch (Exception error) { throw Fail("server-deactivation", error); }
        }

        internal static void AfterServerStateChange(ServerFlowControllerBase flow, GameState state)
        {
            if (state != GameState.RunLevelOutro || phase != "armed") return;
            try
            {
                if (!serverDeactivationDeferred || target == null || !ReferenceEquals(flow, target.Flow) ||
                    serverTransitionObserved ||
                    (GameState)Read(flow, "m_State") != GameState.RunLevelOutro ||
                    IteratorPc((IEnumerator)Read(flow, "m_levelRoutine")) != -1 ||
                    !(bool)Read(flow, "m_inRound"))
                    throw new InvalidOperationException("Server RunLevelOutro edge was not the deferred pristine terminal.");
                RequireUsers(ServerUserSystem.m_Users, target.ServerUsers, GameState.RunLevelOutro, true);
                serverTransitionObserved = true;
            }
            catch (Exception error) { throw Fail("server-transition", error); }
        }

        internal static bool BeforeClientGameState(ClientFlowControllerBase client, Serialisable message)
        {
            if (phase != "armed" || !(message is GameStateMessage) ||
                ((GameStateMessage)message).m_State != GameState.RunLevelOutro) return false;
            try
            {
                if (!serverTransitionObserved || target == null || !ReferenceEquals(client, target.Client))
                    throw new InvalidOperationException("Client RunLevelOutro message did not follow the armed server edge.");
                if (clientPrefixObserved)
                    throw new InvalidOperationException("Client RunLevelOutro handler was entered more than once.");
                target.ValidateRuntimeIdentity();
                IEnumerator routine = (IEnumerator)Read(client, "m_levelRoutine");
                if (!ReferenceEquals(routine, target.ClientLiveRoutine) || !target.ClientIterator.Matches(routine) ||
                    Read(client, "m_outroRoutine") != null || (bool)Read(client, "m_isFinished") ||
                    !(bool)Read(client, "m_inRound"))
                    throw new InvalidOperationException("Client RunLevel iterator is not the untouched scheduled target continuation.");
                clientRoutineBeforeHandler = routine;
                clientPrefixObserved = true;
                return true;
            }
            catch (Exception error) { throw Fail("client-handler-prefix", error); }
        }

        internal static void AfterClientGameState(ClientFlowControllerBase client, bool intercepted)
        {
            if (!intercepted) return;
            try
            {
                if (!clientPrefixObserved || target == null || !ReferenceEquals(client, target.Client) ||
                    !(bool)Read(client, "m_isFinished") || Read(client, "m_levelRoutine") != null)
                    throw new InvalidOperationException("Client terminal handler did not produce its expected dormant outro.");
                IEnumerator outro = Read(client, "m_outroRoutine") as IEnumerator;
                ValidateDormantOutro(outro, client);
                dormantOutro = outro;
                Set(client, "m_isFinished", false);
                Set(client, "m_levelRoutine", clientRoutineBeforeHandler);
                Set(client, "m_outroRoutine", null);
                phase = "pending-pause";
            }
            catch (Exception error) { throw Fail("client-handler-postfix", error); }
        }

        internal static bool ApplyPendingPause()
        {
            if (!PendingPause) return false;
            try
            {
                ValidateHeldSource(false);
                terminalFrame = Injector.Server.CurrentFrameData.FrameNumber;
                phase = "held";
                Helpers.Pause();
                if (!Helpers.IsPaused())
                    throw new InvalidOperationException("Pristine terminal pause did not take ownership of the main pause layer.");
                var receipt = new Dictionary<string, object> {
                    { "nonce", nonce }, { "frame", terminalFrame }, { "phase", phase },
                    { "serverIteratorPc", IteratorPc((IEnumerator)Read(target.Flow, "m_levelRoutine")) },
                    { "clientIteratorPc", IteratorPc((IEnumerator)Read(target.Client, "m_levelRoutine")) },
                    { "dormantOutroPc", IteratorPc(dormantOutro) },
                    { "deferredServerPc", deferredServerPc }, { "clientTimerZeroCalls", clientTimerZeroCalls },
                    { "nativeRound", NativeSessionBridge.CaptureNativeRound() },
                    { "food", NativeFoodSnapshot.Capture() },
                    { "physics", NativePhysicsObservation.CaptureRegistry() },
                    { "clocks", NativeKitchenCheckpoint.CaptureNativeClockObservation(target.Flow, target.Client) },
                    { "lifecycle", CaptureLifecycle() }
                };
                terminalReceipts.Add(receipt);
                if (terminalReceipts.Count > 8) terminalReceipts.RemoveAt(0);
                return true;
            }
            catch (Exception error) { throw Fail("terminal-pause", error); }
        }

        internal static bool PrepareRestore(Target requested)
        {
            if (phase == "armed")
                throw new InvalidOperationException("Cancel the armed round-end latch before an ordinary pre-terminal warp.");
            if (!Held) return false;
            if (target == null || !ReferenceEquals(requested, target))
                throw new InvalidOperationException("Held round-end latch belongs to a different checkpoint target.");
            ValidateHeldSource(true);
            return true;
        }

        internal static void Restore(Target requested)
        {
            if (!Held || target == null || !ReferenceEquals(requested, target))
                throw new InvalidOperationException("Round-end lifecycle restore was not prepared for this checkpoint.");
            ValidateHeldSource(true);
            IEnumerator restoredServerRoutine = target.ServerIterator.RestoreCopy();
            Set(target.Flow, "m_levelRoutine", restoredServerRoutine);
            // The generated server coroutine is complete at the held terminal.
            // Rewind installs a value-identical clone, which becomes the live
            // scheduled incarnation for subsequent terminal replays.
            target.ServerLiveRoutine = restoredServerRoutine;
            Set(target.Flow, "m_skipToEnd", target.ServerSkipToEnd);
            Set(target.Flow, "m_State", target.ServerState);
            Set(target.Flow, "m_inRound", target.ServerInRound);
            Set(target.Client, "m_isFinished", target.ClientFinished);
            Set(target.Client, "m_levelRoutine", target.ClientLiveRoutine);
            Set(target.Client, "m_outroRoutine", target.ClientOutroRoutine);
            Set(target.Client, "m_inRound", target.ClientInRound);
            RestoreUsers(ServerUserSystem.m_Users, target.ServerUsers);
            RestoreUsers(ClientUserSystem.m_Users, target.ClientUsers);
            Set(target.Loader, "m_GameState", target.LoaderState);
            if (target.CampaignFlow != null)
                Set(target.CampaignFlow, "m_LevelRestartRequested", target.LevelRestartRequested);
            dormantOutro = clientRoutineBeforeHandler = null;
            serverDeactivationDeferred = serverTransitionObserved = clientPrefixObserved = false;
            phase = "restored";
            restoredCapabilityPending = true;
        }

        internal static int PublishCapabilityFeatures()
        {
            advertisedCapabilityFeatures = CapabilityFeatures;
            return advertisedCapabilityFeatures;
        }

        internal static void CommitCapabilityPublication()
        {
            int features = advertisedCapabilityFeatures;
            advertisedCapabilityFeatures = 0;
            if ((features & 4) == 0) return;
            restoredCapabilityPending = false;
            target = null;
            phase = "restored-acknowledged";
        }

        internal static void ObserveClientTimerZero()
        {
            if (IsActive) clientTimerZeroCalls++;
        }

        public static object Diagnostics()
        {
            return new Dictionary<string, object> {
                { "phase", phase }, { "active", IsActive }, { "held", Held }, { "pendingPause", PendingPause },
                { "nonce", nonce }, { "targetFrame", target == null ? -1 : target.Frame }, { "terminalFrame", terminalFrame },
                { "serverDeactivationDeferred", serverDeactivationDeferred }, { "serverTransitionObserved", serverTransitionObserved },
                { "clientPrefixObserved", clientPrefixObserved }, { "deferredServerPc", deferredServerPc },
                { "clientTimerZeroCalls", clientTimerZeroCalls }, { "failure", failure },
                { "restoredCapabilityPending", restoredCapabilityPending }, { "terminalReceipts", terminalReceipts.ToArray() },
                { "lifecycle", SafeCaptureLifecycle() },
                { "scope", "Explicit local authoring latch only; unarmed forward round-end behavior is unchanged." }
            };
        }

        private static void ValidateHeldSource(bool requirePaused)
        {
            target.ValidateRuntimeIdentity();
            if (requirePaused && !Helpers.IsPaused()) throw new InvalidOperationException("Held terminal restore requires native pause.");
            if (!MultiplayerController.IsSynchronisationActive()) throw new InvalidOperationException("Native synchronisation ended before terminal restore.");
            if (!serverDeactivationDeferred || !serverTransitionObserved || !clientPrefixObserved || dormantOutro == null)
                throw new InvalidOperationException("Pristine terminal latch sequence is incomplete.");
            if ((GameState)Read(target.Flow, "m_State") != GameState.RunLevelOutro ||
                IteratorPc((IEnumerator)Read(target.Flow, "m_levelRoutine")) != -1 ||
                !(bool)Read(target.Flow, "m_inRound") ||
                (bool)Read(target.Flow, "m_skipToEnd") != target.ServerSkipToEnd ||
                (target.CampaignFlow != null && (bool)Read(target.CampaignFlow, "m_LevelRestartRequested") != target.LevelRestartRequested))
                throw new InvalidOperationException("Server advanced beyond the held RunLevelOutro edge.");
            IEnumerator clientRoutine = Read(target.Client, "m_levelRoutine") as IEnumerator;
            if ((bool)Read(target.Client, "m_isFinished") || !ReferenceEquals(clientRoutine, target.ClientLiveRoutine) ||
                !target.ClientIterator.Matches(clientRoutine) || Read(target.Client, "m_outroRoutine") != null ||
                !(bool)Read(target.Client, "m_inRound"))
                throw new InvalidOperationException("Client gameplay continuation changed while terminal was held.");
            ValidateDormantOutro(dormantOutro, target.Client);
            RequireUsers(ServerUserSystem.m_Users, target.ServerUsers, GameState.RunLevelOutro, true);
            RequireUsers(ClientUserSystem.m_Users, target.ClientUsers, GameState.NotSet, false);
            if ((GameState)Read(target.Loader, "m_GameState") != GameState.RunLevelOutro)
                throw new InvalidOperationException("Client kitchen loader did not observe the held RunLevelOutro message.");
            object audio = Read(target.Audio, "m_state");
            if (!Equals(audio, target.AudioState) || Convert.ToString(audio).IndexOf("InLevel", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("Client audio advanced into the irreversible outro.");
            if (clientTimerZeroCalls != 0)
                throw new InvalidOperationException("Client timer Zero ran before the terminal latch.");
        }

        private static object CaptureLifecycle()
        {
            if (target == null) return null;
            return new Dictionary<string, object> {
                { "scene", SceneManager.GetActiveScene().name },
                { "serverState", Convert.ToString(Read(target.Flow, "m_State")) },
                { "serverInRound", Read(target.Flow, "m_inRound") },
                { "serverIteratorPc", IteratorPc(Read(target.Flow, "m_levelRoutine") as IEnumerator) },
                { "clientFinished", Read(target.Client, "m_isFinished") },
                { "clientInRound", Read(target.Client, "m_inRound") },
                { "clientIteratorPc", IteratorPc(Read(target.Client, "m_levelRoutine") as IEnumerator) },
                { "clientOutroPc", IteratorPc(Read(target.Client, "m_outroRoutine") as IEnumerator) },
                { "dormantOutroPc", IteratorPc(dormantOutro) },
                { "loaderState", Convert.ToString(Read(target.Loader, "m_GameState")) },
                { "audioState", Convert.ToString(Read(target.Audio, "m_state")) },
                { "serverUsers", CaptureUsers(ServerUserSystem.m_Users) },
                { "clientUsers", CaptureUsers(ClientUserSystem.m_Users) }
            };
        }

        private static object SafeCaptureLifecycle()
        {
            if (target == null) return null;
            try { return CaptureLifecycle(); }
            catch (Exception error)
            {
                return new Dictionary<string, object> { { "captureError", error.ToString() } };
            }
        }

        private static object CaptureUsers(FastList<User> users)
        {
            var rows = new List<object>();
            for (int i = 0; i < users.Count; i++) rows.Add(new Dictionary<string, object> {
                { "index", i }, { "state", users._items[i].GameState.ToString() },
                { "changedThisFrame", users._items[i].ChangedThisFrame }
            });
            return rows.ToArray();
        }

        private static void ClearActive(string finalPhase)
        {
            target = null; dormantOutro = clientRoutineBeforeHandler = null;
            serverDeactivationDeferred = serverTransitionObserved = clientPrefixObserved = false;
            phase = finalPhase;
        }

        private static Exception Fail(string stage, Exception error)
        {
            failure = stage + ": " + error;
            phase = "failed";
            try { Helpers.Pause(); } catch { }
            StateInvalidityManager.InvalidReason = "AUTHORING_ROUND_END_LATCH_FAILED: " + error.Message;
            return new InvalidOperationException("Native round-end latch failed at " + stage + ".", error);
        }

        private static void ValidateDormantOutro(IEnumerator outro, ClientFlowControllerBase client)
        {
            if (outro == null || outro.GetType().FullName != "ClientFlowControllerBase+<RunLevelEnd>c__Iterator2")
                throw new InvalidOperationException("Native RunLevelEnd iterator type changed.");
            RequireExactFields(outro.GetType(), outroIteratorFields);
            if (IteratorPc(outro) != 0 || Read(outro, "<outro>__0") != null || !ReferenceEquals(Read(outro, "$this"), client))
                throw new InvalidOperationException("Native RunLevelEnd iterator already advanced.");
        }

        private static void RequireUsers(FastList<User> actual, UserState[] expected, GameState required, bool requireState)
        {
            if (actual == null || expected == null || actual.Count != expected.Length)
                throw new InvalidOperationException("Native user list size changed.");
            for (int i = 0; i < expected.Length; i++)
                if (!ReferenceEquals(actual._items[i], expected[i].User) || (requireState && actual._items[i].GameState != required))
                    throw new InvalidOperationException("Native user identity/state changed at index " + i + ".");
        }

        private static void RestoreUsers(FastList<User> actual, UserState[] expected)
        {
            RequireUsers(actual, expected, GameState.NotSet, false);
            for (int i = 0; i < expected.Length; i++)
            {
                Set(actual._items[i], "m_GameState", expected[i].State);
                actual._items[i].ChangedThisFrame = expected[i].Changed;
            }
        }

        private static int IteratorPc(IEnumerator iterator)
        {
            return iterator == null ? Int32.MinValue : Convert.ToInt32(Read(iterator, "$PC"));
        }

        private static object Read(object instance, string name)
        {
            if (instance == null) throw new NullReferenceException("Cannot read " + name + " from a null native object.");
            FieldInfo field = AccessTools.Field(instance.GetType(), name);
            if (field == null) throw new MissingFieldException(instance.GetType().FullName, name);
            return field.GetValue(instance);
        }

        private static void Set(object instance, string name, object value)
        {
            FieldInfo field = AccessTools.Field(instance.GetType(), name);
            if (field == null) throw new MissingFieldException(instance.GetType().FullName, name);
            field.SetValue(instance, value);
        }

        private static MethodInfo Method(object instance, string name, Type[] signature)
        {
            MethodInfo method = AccessTools.Method(instance.GetType(), name, signature);
            if (method == null) throw new MissingMethodException(instance.GetType().FullName, name);
            return method;
        }

        private static void RequireExactFields(Type type, string[] expected)
        {
            string[] actual = type.GetFields(Instance | BindingFlags.DeclaredOnly).Select(field => field.Name).OrderBy(name => name).ToArray();
            if (!actual.SequenceEqual(expected.OrderBy(name => name)))
                throw new InvalidOperationException("Native iterator field layout changed: " + type.FullName);
        }

        private static void EnsureCaptureBindings(ServerKitchenFlowControllerBase flow,
            ClientKitchenFlowControllerBase client, Type serverIteratorType, Type clientIteratorType)
        {
            if (ReferenceEquals(captureFlow, flow) && ReferenceEquals(captureClient, client) &&
                captureServerIteratorType == serverIteratorType && captureClientIteratorType == clientIteratorType &&
                captureLoader != null && captureServerIterator != null && captureClientIterator != null) return;
            ClientKitchenLoader[] loaders = UnityEngine.Object.FindObjectsOfType<ClientKitchenLoader>();
            if (loaders.Length != 1) throw new InvalidOperationException("Expected exactly one native ClientKitchenLoader.");
            captureFlow = flow; captureClient = client; captureLoader = loaders[0];
            captureServerIteratorType = serverIteratorType; captureClientIteratorType = clientIteratorType;
            captureServerIterator = new NativeIteratorCopy(new Dictionary<Type, string[]> { { serverIteratorType, simpleIteratorFields } },
                value => ReferenceEquals(value, flow));
            captureClientIterator = new NativeIteratorCopy(new Dictionary<Type, string[]> { { clientIteratorType, simpleIteratorFields } },
                value => ReferenceEquals(value, client));
        }

        internal sealed class UserState
        {
            internal User User;
            internal GameState State;
            internal bool Changed;
        }

        internal sealed class Target
        {
            internal int Frame, SceneBuildIndex, FlowInstanceId;
            internal string SceneName;
            internal ServerKitchenFlowControllerBase Flow;
            internal ClientKitchenFlowControllerBase Client;
            internal ClientKitchenLoader Loader;
            internal CampaignAudioManager Audio;
            internal object AudioState;
            internal IEnumerator ServerLiveRoutine, ClientLiveRoutine, ClientOutroRoutine;
            internal NativeIteratorCopy.Snapshot ServerIterator, ClientIterator;
            internal GameState ServerState, LoaderState;
            internal bool ServerInRound, ServerSkipToEnd, ClientFinished, ClientInRound;
            internal UserState[] ServerUsers, ClientUsers;
            internal ServerCampaignFlowController CampaignFlow;
            internal bool LevelRestartRequested;

            internal static Target Capture(ServerKitchenFlowControllerBase flow, ClientKitchenFlowControllerBase client, int frame)
            {
                if (flow == null || client == null) throw new InvalidOperationException("Native round lifecycle flow pair is incomplete.");
                IEnumerator serverRoutine = Read(flow, "m_levelRoutine") as IEnumerator;
                IEnumerator clientRoutine = Read(client, "m_levelRoutine") as IEnumerator;
                // The first InLevel message can be observed before either generated
                // coroutine reaches its stable yield.  Keep the ordinary kitchen
                // checkpoint, but do not advertise that transient frame as a
                // round-end lifecycle target.
                if (serverRoutine == null || clientRoutine == null) return null;
                if (serverRoutine.GetType().FullName != "ServerFlowControllerBase+<RunRound>c__Iterator0")
                    throw new InvalidOperationException("Native RunRound iterator type changed.");
                if (clientRoutine.GetType().FullName != "ClientFlowControllerBase+<RunLevel>c__Iterator1")
                    throw new InvalidOperationException("Native RunLevel iterator type changed.");
                RequireExactFields(serverRoutine.GetType(), simpleIteratorFields);
                RequireExactFields(clientRoutine.GetType(), simpleIteratorFields);
                if (IteratorPc(serverRoutine) != 1 || IteratorPc(clientRoutine) != 1) return null;
                EnsureCaptureBindings(flow, client, serverRoutine.GetType(), clientRoutine.GetType());
                CampaignAudioManager audio = (CampaignAudioManager)Read(client, "m_campaignAudioManager");
                if (audio == null) throw new InvalidOperationException("Native campaign audio manager is missing.");
                var result = new Target {
                    Frame = frame, SceneBuildIndex = SceneManager.GetActiveScene().buildIndex,
                    SceneName = SceneManager.GetActiveScene().name, FlowInstanceId = flow.gameObject.GetInstanceID(),
                    Flow = flow, Client = client, Loader = captureLoader, Audio = audio, AudioState = Read(audio, "m_state"),
                    ServerLiveRoutine = serverRoutine, ClientLiveRoutine = clientRoutine,
                    ClientOutroRoutine = Read(client, "m_outroRoutine") as IEnumerator,
                    ServerIterator = captureServerIterator.Capture(serverRoutine), ClientIterator = captureClientIterator.Capture(clientRoutine),
                    ServerState = (GameState)Read(flow, "m_State"), LoaderState = (GameState)Read(captureLoader, "m_GameState"),
                    ServerInRound = (bool)Read(flow, "m_inRound"), ServerSkipToEnd = (bool)Read(flow, "m_skipToEnd"),
                    ClientFinished = (bool)Read(client, "m_isFinished"), ClientInRound = (bool)Read(client, "m_inRound"),
                    ServerUsers = CaptureUserStates(ServerUserSystem.m_Users), ClientUsers = CaptureUserStates(ClientUserSystem.m_Users),
                    CampaignFlow = flow as ServerCampaignFlowController
                };
                if (result.CampaignFlow != null) result.LevelRestartRequested = (bool)Read(result.CampaignFlow, "m_LevelRestartRequested");
                if (!MultiplayerController.IsSynchronisationActive() || result.ServerState != GameState.InLevel ||
                    result.LoaderState != GameState.InLevel || !result.ServerInRound || result.ServerSkipToEnd ||
                    result.LevelRestartRequested ||
                    result.ClientFinished || !result.ClientInRound || result.ClientOutroRoutine != null ||
                    Convert.ToString(result.AudioState).IndexOf("InLevel", StringComparison.Ordinal) < 0 ||
                    result.ServerUsers.Any(user => user.State != GameState.InLevel) ||
                    result.ClientUsers.Any(user => user.State != GameState.InLevel)) return null;
                result.ValidateTargetBoundary();
                return result;
            }

            internal void ValidateRuntimeIdentity()
            {
                if (Flow == null || Client == null || Loader == null || Audio == null ||
                    Flow.gameObject.GetInstanceID() != FlowInstanceId || SceneManager.GetActiveScene().buildIndex != SceneBuildIndex ||
                    !String.Equals(SceneManager.GetActiveScene().name, SceneName, StringComparison.Ordinal) ||
                    !ReferenceEquals(Flow.GetComponent<ClientKitchenFlowControllerBase>(), Client) ||
                    !ReferenceEquals(Read(Client, "m_campaignAudioManager"), Audio))
                    throw new InvalidOperationException("Native round lifecycle scene/flow incarnation changed.");
                RequireUsers(ServerUserSystem.m_Users, ServerUsers, GameState.NotSet, false);
                RequireUsers(ClientUserSystem.m_Users, ClientUsers, GameState.NotSet, false);
            }

            internal void ValidateTargetBoundary()
            {
                ValidateRuntimeIdentity();
                if (!MultiplayerController.IsSynchronisationActive() || ServerState != GameState.InLevel || LoaderState != GameState.InLevel ||
                    !ServerInRound || ServerSkipToEnd || LevelRestartRequested || ClientFinished || !ClientInRound || ClientOutroRoutine != null ||
                    (GameState)Read(Flow, "m_State") != ServerState || (GameState)Read(Loader, "m_GameState") != LoaderState ||
                    !(bool)Read(Flow, "m_inRound") || (bool)Read(Flow, "m_skipToEnd") ||
                    (bool)Read(Client, "m_isFinished") || !(bool)Read(Client, "m_inRound") || Read(Client, "m_outroRoutine") != null ||
                    !ServerIterator.Matches(Read(Flow, "m_levelRoutine") as IEnumerator) ||
                    !ReferenceEquals(Read(Client, "m_levelRoutine"), ClientLiveRoutine) || !ClientIterator.Matches(ClientLiveRoutine) ||
                    Convert.ToString(AudioState).IndexOf("InLevel", StringComparison.Ordinal) < 0)
                    throw new InvalidOperationException("Native round lifecycle checkpoint is not a stable active InLevel boundary.");
                for (int i = 0; i < ServerUsers.Length; i++)
                    if (ServerUsers[i].State != GameState.InLevel || ClientUsers[i].State != GameState.InLevel)
                        throw new InvalidOperationException("Native round lifecycle users are not all InLevel.");
            }

            internal void BindCurrentServerRoutine()
            {
                IEnumerator current = Read(Flow, "m_levelRoutine") as IEnumerator;
                if (current == null || !ServerIterator.Matches(current))
                    throw new InvalidOperationException("Current server RunRound incarnation does not match the checkpoint continuation.");
                ServerLiveRoutine = current;
            }

            internal static bool Same(Target a, Target b)
            {
                if (a == null || b == null) return a == b;
                return a.Frame == b.Frame && a.SceneBuildIndex == b.SceneBuildIndex && a.SceneName == b.SceneName && a.FlowInstanceId == b.FlowInstanceId &&
                    ReferenceEquals(a.Flow, b.Flow) && ReferenceEquals(a.Client, b.Client) && ReferenceEquals(a.Loader, b.Loader) &&
                    a.ServerIterator.Matches(b.ServerIterator) && ReferenceEquals(a.ClientLiveRoutine, b.ClientLiveRoutine) &&
                    a.ClientIterator.Matches(b.ClientIterator) && ReferenceEquals(a.ClientOutroRoutine, b.ClientOutroRoutine) &&
                    ReferenceEquals(a.Audio, b.Audio) && ReferenceEquals(a.CampaignFlow, b.CampaignFlow) &&
                    a.ServerState == b.ServerState && a.LoaderState == b.LoaderState && a.ServerInRound == b.ServerInRound &&
                    a.ServerSkipToEnd == b.ServerSkipToEnd && a.ClientFinished == b.ClientFinished && a.ClientInRound == b.ClientInRound &&
                    a.LevelRestartRequested == b.LevelRestartRequested && Equals(a.AudioState, b.AudioState) &&
                    SameUsers(a.ServerUsers, b.ServerUsers) && SameUsers(a.ClientUsers, b.ClientUsers);
            }

            private static bool SameUsers(UserState[] a, UserState[] b)
            {
                if (a == null || b == null || a.Length != b.Length) return false;
                for (int i = 0; i < a.Length; i++)
                    if (!ReferenceEquals(a[i].User, b[i].User) || a[i].State != b[i].State || a[i].Changed != b[i].Changed) return false;
                return true;
            }

            private static UserState[] CaptureUserStates(FastList<User> users)
            {
                if (users == null || users.Count != 4) throw new InvalidOperationException("Round lifecycle checkpoint requires exactly four users.");
                var result = new UserState[users.Count];
                for (int i = 0; i < result.Length; i++) result[i] = new UserState {
                    User = users._items[i], State = users._items[i].GameState, Changed = users._items[i].ChangedThisFrame
                };
                return result;
            }
        }
    }

    [HarmonyPatch(typeof(ServerFlowControllerBase), "SetRoundBehaviourActivation")]
    public static class NativeRoundEndServerActivationPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ServerFlowControllerBase __instance, bool _enabled)
        {
            return !NativeRoundEndLatch.TryDeferServerDeactivation(__instance, _enabled);
        }
    }

    [HarmonyPatch(typeof(ClientFlowControllerBase), "OnGameStateChanged")]
    public static class NativeRoundEndClientStatePatch
    {
        [HarmonyPrefix]
        public static void Prefix(ClientFlowControllerBase __instance, Serialisable message, ref bool __state)
        {
            __state = NativeRoundEndLatch.BeforeClientGameState(__instance, message);
        }

        [HarmonyPostfix]
        public static void Postfix(ClientFlowControllerBase __instance, bool __state)
        {
            NativeRoundEndLatch.AfterClientGameState(__instance, __state);
        }
    }

    [HarmonyPatch(typeof(ClientRoundTimer), "Zero")]
    public static class NativeRoundEndClientTimerZeroPatch
    {
        [HarmonyPrefix]
        public static void Prefix() { NativeRoundEndLatch.ObserveClientTimerZero(); }
    }
}
