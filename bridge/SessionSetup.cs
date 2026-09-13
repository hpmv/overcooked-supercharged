// Adapted from the independently authored Oc2Tas bridge in plugin/SessionSetup.cs.
// Only the namespace changes; original session/save/transport semantics are retained.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GameModes;
using InControl;
using Team17.Online;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SuperchargedPatch.Bridge
{
    // Session commands must run on Unity's main thread. This class only performs
    // lobby/device and normal loading operations, never modifies kitchen mechanics.
    public static class SessionSetup
    {
        public const int CarnivalDlc = 8;
        public const int CarnivalLevel = 11;
        public const string CarnivalScene = "s_Day_3_4";
        private static TasSessionRunner runner;
        private static Coroutine pending;
        private static string stage = "idle";
        private static string error = "";
        private static string waitingFor = "";
        public static bool KitchenReady { get { return stage == "kitchen_ready" && pending == null; } }
        public static bool Failed { get { return stage == "failed"; } }
        public static string LastError { get { return error; } }
        private static readonly List<TasLocalPad> pads = new List<TasLocalPad>();
        private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static string Execute(string command)
        {
            command = (command ?? "status").Trim().ToLowerInvariant();
            if (command == "status") return Status();
            if (command == "cancel")
            {
                if (runner != null && pending != null) runner.StopCoroutine(pending);
                pending = null;
                stage = "cancelled";
                return Status();
            }
            if (pending != null) throw new InvalidOperationException("Session setup is already running: " + stage);
            bool load = command == "load" || command == "carnival" || command == "restart";
            if (!load && command != "players" && command != "setup" && command != "engage")
                throw new ArgumentException("Session command must be players, load, restart, cancel or status.");
            if (!SaveIsolation.IsInstalled)
                throw new InvalidOperationException("SaveIsolation.Install must succeed before session setup.");
            EnsureRunner();
            error = "";
            waitingFor = "";
            stage = "waiting_for_bootstrap";
            pending = runner.StartCoroutine(Guard(Run(load, command == "restart")));
            return Status();
        }

        public static string Status()
        {
            GameSession session = GameUtils.GetGameSession();
            SceneDirectoryData.PerPlayerCountDirectoryEntry variant = session == null || session.LevelSettings == null
                ? null : session.LevelSettings.SceneDirectoryVarientEntry;
            return "{\"stage\":" + Quote(stage) + ",\"busy\":" + (pending != null ? "true" : "false")
                + ",\"error\":" + Quote(error)
                + ",\"waitingFor\":" + Quote(waitingFor)
                + ",\"scene\":" + Quote(SceneManager.GetActiveScene().name)
                + ",\"dlc\":" + (session == null ? "-1" : session.DLC.ToString())
                + ",\"variantPlayers\":" + (variant == null ? "0" : variant.PlayerCount.ToString())
                + ",\"variantScene\":" + Quote(variant == null ? "" : variant.SceneName)
                + ",\"serverUsers\":" + ServerUserSystem.m_Users.Count
                + ",\"clientUsers\":" + ClientUserSystem.m_Users.Count
                + ",\"serverStates\":" + Quote(UserStates(ServerUserSystem.m_Users))
                + ",\"clientStates\":" + Quote(UserStates(ClientUserSystem.m_Users))
                + ",\"virtualPads\":" + pads.Count
                + ",\"profile\":" + Quote(SaveIsolation.Root) + "}";
        }

        private static void EnsureRunner()
        {
            if (runner != null) return;
            GameObject host = new GameObject("OC2 TAS Session Setup");
            UnityEngine.Object.DontDestroyOnLoad(host);
            runner = host.AddComponent<TasSessionRunner>();
        }

        private static IEnumerator Guard(IEnumerator routine)
        {
            // One initial yield ensures Execute stores the coroutine before it can complete.
            yield return null;
            while (true)
            {
                object current = null;
                bool more = false;
                Exception failure = null;
                try { more = routine.MoveNext(); if (more) current = routine.Current; }
                catch (Exception ex) { failure = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex; }
                if (failure != null)
                {
                    error = failure.GetType().Name + ": " + failure.Message;
                    stage = "failed";
                    Debug.LogError("[OC2 TAS session] " + failure);
                    break;
                }
                if (!more) break;
                yield return current;
            }
            pending = null;
        }

        private static IEnumerator Run(bool load, bool restart)
        {
            float deadline = Time.realtimeSinceStartup + 45f;
            PlayerManager manager = null;
            while (manager == null || !InputManager.IsSetup || !PlayerInputLookup.IsAwake())
            {
                manager = GameUtils.RequestManager<PlayerManager>();
                CheckDeadline(deadline, "Game input/bootstrap is not ready.");
                yield return null;
            }
            // Input managers are already available during Ident. StartScreenFlow.Start
            // subsequently calls ResetToJustStarted, which disengages slot one. Wait
            // for that native initialization before attaching or engaging any TAS pad.
            // Kitchen restarts already have a session and no start-screen instance.
            if (GameUtils.GetGameSession() == null || StartScreenFlow.Instance != null)
            {
                stage = "waiting_for_start_screen_initialization";
                waitingFor = "StartScreenFlow.Start before controller engagement";
                deadline = Time.realtimeSinceStartup + 60f;
                FieldInfo startPlayer = typeof(StartScreenFlow).GetField("m_PlayerManager", InstanceFlags);
                FieldInfo startSave = typeof(StartScreenFlow).GetField("m_SaveManager", InstanceFlags);
                if (startPlayer == null || startSave == null)
                    throw new MissingFieldException("StartScreenFlow initialization manager fields are unavailable.");
                while (true)
                {
                    StartScreenFlow initializedScreen = StartScreenFlow.Instance;
                    if (initializedScreen != null && System.Object.ReferenceEquals(startPlayer.GetValue(initializedScreen), manager)
                        && startSave.GetValue(initializedScreen) != null)
                    {
                        // Start is synchronous; one further native frame also lets
                        // its reset/user-change messages complete before registration.
                        yield return null;
                        if (initializedScreen == StartScreenFlow.Instance
                            && System.Object.ReferenceEquals(startPlayer.GetValue(initializedScreen), manager)
                            && startSave.GetValue(initializedScreen) != null) break;
                    }
                    CheckDeadline(deadline, "Native StartScreenFlow.Start did not finish before player registration.");
                    yield return null;
                }
                waitingFor = "";
            }
            if ((bool)InvokeInternalStatic("ConnectionStatus", "IsInSession", Type.EmptyTypes, new object[0]))
                throw new InvalidOperationException("TAS setup requires a local session; leave the online session first.");
            if (restart && (GameUtils.GetGameSession() == null || GameUtils.GetGameSession().DLC != CarnivalDlc))
                throw new InvalidOperationException("Restart requires an existing Carnival session; use load first.");
            stage = "registering_players";
            // Retain already engaged users (including the keyboard owner), filling
            // only empty slots with neutral virtual controllers through InControl.
            for (int slot = 0; slot < 4; slot++)
            {
                EngagementSlot engagement = (EngagementSlot)slot;
                if (manager.GetUser(engagement) == null)
                {
                    TasLocalPad pad = new TasLocalPad(slot);
                    pads.Add(pad);
                    InputManager.AttachDevice(pad);
                    yield return null;
                    int padNumber = FindPadNumber(pad);
                    if (padNumber < 0) throw new InvalidOperationException("The game did not register TAS controller " + slot + ".");
                    MethodInfo engage = manager.GetType().GetMethod("EngagePadToSlot", InstanceFlags);
                    if (engage == null) throw new MissingMethodException("PCPlayerManager.EngagePadToSlot");
                    object result = engage.Invoke(manager, new object[] { (ControlPadInput.PadNum)padNumber, engagement });
                    if (result == null || manager.GetUser(engagement) == null)
                        throw new InvalidOperationException("Normal controller engagement rejected slot " + slot + ".");
                    yield return null;
                }
            }

            // Normal engagement callbacks usually register these users themselves.
            // If the local server became ready afterwards, add the same engaged
            // profiles through the native user system and send normal users changed.
            stage = "waiting_for_local_server";
            deadline = Time.realtimeSinceStartup + 45f;
            while (!LocalServerReady())
            {
                CheckDeadline(deadline, "The local server is not ready; complete the game's start screen first.");
                yield return null;
            }
            for (int slot = 0; slot < 4; slot++)
            {
                User existing = UserSystemUtils.FindUser(ServerUserSystem.m_Users, null,
                    ServerUserSystem.s_LocalMachineId, (EngagementSlot)slot);
                if (existing == null)
                {
                    User added = ServerUserSystem.AddUser(true, ServerUserSystem.s_LocalMachineId,
                        null, 0u, 0u, (EngagementSlot)slot, PadSide.Both, TeamID.None, User.PartyPersistance.Remain);
                    if (added == null) throw new InvalidOperationException("Could not register four distinct local users.");
                }
            }
            InvokeInternalStatic("ServerMessenger", "UsersChanged", Type.EmptyTypes, new object[0]);
            deadline = Time.realtimeSinceStartup + 15f;
            while (ClientUserSystem.m_Users.Count != 4)
            {
                CheckDeadline(deadline, "The four-user registration did not synchronize.");
                yield return null;
            }
            ValidateFourLocalUsers();
            if (!load) { stage = "players_ready"; yield break; }

            // Registering a pad directly reaches the same engagement state, but
            // the start-screen owns the callback that loads the local profile and
            // opens the frontend. Complete that native callback for our owner.
            GameSession beforeFrontend = GameUtils.GetGameSession();
            if ((beforeFrontend == null || beforeFrontend.DLC != CarnivalDlc) && T17FrontendFlow.Instance == null)
            {
                StartScreenFlow startScreen = StartScreenFlow.Instance;
                stage = "waiting_for_start_screen";
                deadline = Time.realtimeSinceStartup + 60f;
                while (startScreen == null)
                {
                    CheckDeadline(deadline, "Native start-screen bootstrap did not finish.");
                    yield return null;
                    startScreen = StartScreenFlow.Instance;
                }
                stage = "entering_frontend";
                FieldInfo checking = typeof(StartScreenFlow).GetField("m_bCheckingForEngagement", StaticFlags);
                MethodInfo engaged = typeof(StartScreenFlow).GetMethod("OnEngagementFinished", InstanceFlags);
                if (checking == null || engaged == null)
                    throw new MissingMethodException("StartScreenFlow engagement callback is unavailable.");
                object primaryUser = manager.GetUser(EngagementSlot.One);
                if (primaryUser == null)
                    throw new InvalidOperationException("The start-screen owner was disengaged after registration; cannot load its profile.");
                checking.SetValue(null, false);
                engaged.Invoke(startScreen, new object[] { primaryUser });
                deadline = Time.realtimeSinceStartup + 90f;
                while (T17FrontendFlow.Instance == null)
                {
                    // The first-run safe-area screen is confirmed at its default
                    // size through its normal completion function; saves remain
                    // in the isolated profile. This changes no kitchen rules.
                    SafeAreaAdjuster[] adjusters = UnityEngine.Object.FindObjectsOfType<SafeAreaAdjuster>();
                    foreach (SafeAreaAdjuster adjuster in adjusters)
                        if (adjuster.isActiveAndEnabled && !adjuster.Completed) adjuster.Hide();
                    CheckDeadline(deadline, "Native start-screen profile loading did not open the frontend.");
                    yield return null;
                }
                // Let the normal camera and startup popup coroutine finish. Use
                // the popup's confirm handler rather than deactivating UI objects.
                int quietFrames = 0;
                while (quietFrames < 6)
                {
                    NewContentPopup[] popups = UnityEngine.Object.FindObjectsOfType<NewContentPopup>();
                    bool hadPopup = false;
                    foreach (NewContentPopup popup in popups)
                    {
                        if (!popup.gameObject.activeInHierarchy) continue;
                        hadPopup = true;
                        popup.OnPopupConfirm();
                    }
                    bool ready = T17FrontendFlow.Instance != null
                        && !T17FrontendFlow.Instance.IsCameraTransitioning()
                        && !T17FrontendFlow.Instance.BlockFocusKitchen && !hadPopup;
                    quietFrames = ready ? quietFrames + 1 : 0;
                    CheckDeadline(deadline, "The frontend is waiting for a startup dialog or camera transition.");
                    yield return null;
                }
            }

            stage = "preparing_carnival_session";
            GameSession session = GameUtils.GetGameSession();
            if (session == null || session.DLC != CarnivalDlc)
            {
                DLCManager dlc = GameUtils.RequestManager<DLCManager>();
                if (dlc == null) throw new InvalidOperationException("DLC manager is unavailable.");
                DLCFrontendData carnival = dlc.AllDlc.Find(delegate(DLCFrontendData item) { return item != null && item.m_DLCID == CarnivalDlc; });
                if (carnival == null || !dlc.IsDLCAvailable(carnival))
                    throw new InvalidOperationException("The local installation has not reported Carnival of Chaos as owned and available.");
                if (T17FrontendFlow.Instance == null)
                    throw new InvalidOperationException("Return to the frontend before creating a Carnival session.");
                session = T17FrontendFlow.Instance.StartEmptySession(GameSession.GameType.Cooperative, CarnivalDlc);
                if (session == null) throw new InvalidOperationException("Carnival session prefab is unavailable.");
                session.SaveSlot = 0;
                yield return null;
            }
            SceneDirectoryData directory = session.Progress.GetSceneDirectory();
            if (directory == null || directory.Scenes.Length <= CarnivalLevel)
                throw new InvalidOperationException("Carnival scene directory does not contain level 3-4.");
            SceneDirectoryData.PerPlayerCountDirectoryEntry variant = directory.Scenes[CarnivalLevel].GetSceneVarient(4);
            if (variant == null || !String.Equals(variant.SceneName, CarnivalScene, StringComparison.OrdinalIgnoreCase) || variant.PlayerCount != 4)
                throw new InvalidOperationException("Unexpected Carnival 3-4 four-player scene mapping.");
            session.GameModeKind = Kind.Campaign;
            session.LevelSettings.SceneDirectoryVarientEntry = variant;
            session.FillShownMetaDialogStatus();
            if (!(bool)InvokeInternalStatic("ServerMessenger", "SetupCoopSession",
                new Type[] { typeof(int), typeof(GameProgress.GameProgressData), typeof(bool[]), typeof(SessionConfig) },
                new object[] { CarnivalDlc, session.Progress.SaveData, session.m_shownMetaDialogs, session.GameModeSessionConfig }))
                throw new InvalidOperationException("Native cooperative session setup failed.");
            // Allow setup messages to dispatch before loading by index.
            yield return null;
            yield return null;
            ServerGameSetup.Mode = GameMode.Campaign;
            stage = "loading_carnival_3_4";
            Scene previousScene = SceneManager.GetActiveScene();
            InvokeInternalStatic("ServerMessenger", "LoadLevel",
                new Type[] { typeof(uint), typeof(uint), typeof(GameState), typeof(GameState) },
                new object[] { (uint)CarnivalLevel, 4u, GameState.LoadKitchen, GameState.RunKitchen });
            deadline = Time.realtimeSinceStartup + 90f;
            yield return null;
            while (!HasLoadedKitchen(previousScene))
            {
                CheckDeadline(deadline, "Carnival kitchen did not complete normal loading: " + waitingFor
                    + "; server=" + UserStates(ServerUserSystem.m_Users) + "; client=" + UserStates(ClientUserSystem.m_Users));
                yield return null;
            }
            ValidateFourLocalUsers();
            waitingFor = "";
            stage = "kitchen_ready";
        }

        private static bool HasLoadedKitchen(Scene previousScene)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.Equals(previousScene)) { waitingFor = "new scene instance"; return false; }
            // Asset-bundle scenes may report lower-case names in the installed
            // player even though their scene-directory entries use title case.
            if (!String.Equals(scene.name, CarnivalScene, StringComparison.OrdinalIgnoreCase))
            { waitingFor = "Carnival scene (current " + scene.name + ")"; return false; }
            if (!MultiplayerController.IsSynchronisationActive()) { waitingFor = "entity synchronization"; return false; }
            if (ServerUserSystem.m_Users.Count != 4 || ClientUserSystem.m_Users.Count != 4)
            { waitingFor = "four synchronized users"; return false; }
            for (int i = 0; i < 4; i++)
            {
                // ServerKitchenLoader changes RunKitchen -> RunLevelIntro in the
                // same Update, so exact RunKitchen equality can never be observed.
                // All of these states are after StartEntities; outro is excluded.
                GameState state = ServerUserSystem.m_Users._items[i].GameState;
                if (state != GameState.RunKitchen && state != GameState.RunLevelIntro
                    && state != GameState.RanLevelIntro && state != GameState.InLevel)
                { waitingFor = "completed chef/entity startup for player " + i; return false; }
            }
            PlayerControls[] chefs = UnityEngine.Object.FindObjectsOfType<PlayerControls>();
            if (chefs.Length != 4) { waitingFor = "four chef controls (current " + chefs.Length + ")"; return false; }
            bool[] ids = new bool[4];
            foreach (PlayerControls chef in chefs)
            {
                PlayerIDProvider provider = chef.GetComponent<PlayerIDProvider>();
                if (provider == null || !provider.IsLocallyControlled()) { waitingFor = "local chef ownership"; return false; }
                int id = (int)provider.GetID();
                if (id < 0 || id >= 4 || ids[id]) { waitingFor = "four distinct local chef IDs"; return false; }
                ids[id] = true;
            }
            return true;
        }

        private static string UserStates(FastList<User> users)
        {
            string result = "";
            for (int i = 0; i < users.Count; i++)
                result += (i == 0 ? "" : ",") + users._items[i].GameState;
            return result;
        }

        private static int FindPadNumber(InputDevice device)
        {
            MethodInfo lookup = typeof(PCPadInputProvider).GetMethod("GetActionSet", StaticFlags);
            if (lookup == null) throw new MissingMethodException("PCPadInputProvider.GetActionSet");
            for (int pad = 0; pad < PlayerInputLookup.GetSystemControllerMaximum(); pad++)
            {
                PlayerActionSet actions = lookup.Invoke(null, new object[] { (ControlPadInput.PadNum)pad }) as PlayerActionSet;
                if (actions != null && actions.Device == device) return pad;
            }
            return -1;
        }

        private static bool LocalServerReady()
        {
            MultiplayerController controller = GameUtils.RequestManager<MultiplayerController>();
            if (controller == null) return false;
            MethodInfo isServer = typeof(MultiplayerController).GetMethod("IsServer", InstanceFlags);
            if (isServer == null) throw new MissingMethodException("MultiplayerController.IsServer");
            return (bool)isServer.Invoke(controller, null);
        }

        private static object InvokeInternalStatic(string typeName, string methodName, Type[] signature, object[] arguments)
        {
            Type type = typeof(GameSession).Assembly.GetType(typeName, true);
            MethodInfo method = type.GetMethod(methodName, StaticFlags, null, signature, null);
            if (method == null) throw new MissingMethodException(typeName, methodName);
            return method.Invoke(null, arguments);
        }

        private static void ValidateFourLocalUsers()
        {
            if (ServerUserSystem.m_Users.Count != 4 || ClientUserSystem.m_Users.Count != 4)
                throw new InvalidOperationException("Expected exactly four users on the local client and server.");
            bool[] slots = new bool[4];
            for (int i = 0; i < 4; i++)
            {
                User user = ClientUserSystem.m_Users._items[i];
                int slot = (int)user.Engagement;
                if (!user.IsLocal || slot < 0 || slot >= 4 || slots[slot] || user.PadSide != PadSide.Both)
                    throw new InvalidOperationException("Expected four independent, unsplit local player registrations.");
                slots[slot] = true;
            }
        }

        private static void CheckDeadline(float deadline, string message)
        {
            if (Time.realtimeSinceStartup >= deadline) throw new TimeoutException(message);
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
        }
    }

    public sealed class TasSessionRunner : MonoBehaviour { }

    internal sealed class TasLocalPad : InputDevice
    {
        public TasLocalPad(int slot) : base("OC2 TAS Local Pad " + (slot + 1))
        {
            Meta = "oc2-tas-local-pad-" + slot;
            foreach (InputControlType control in Enum.GetValues(typeof(InputControlType)))
            {
                int index = (int)control;
                if (index > 0 && index < Controls.Length && !HasControl(control))
                    AddControl(control, control.ToString());
            }
        }
    }
}
