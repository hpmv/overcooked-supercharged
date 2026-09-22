// Normal local session loading adapted from bridge/SessionSetup.cs (independent
// Oc2Tas session adapter). Compiled outside the permanent patch; no rule patches.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameModes;
using SuperchargedPatch.AlteredComponents;
using SuperchargedPatch.Authoring;
using SuperchargedPatch.Bridge;
using Team17.Online;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SuperchargedPatch.Authoring.Modules
{
    public sealed class LevelSessionModule : IAuthoringModule
    {
        private const BindingFlags Static = BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private const BindingFlags Instance = BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private readonly Type bridge = typeof(NativeSessionBridge), setup = typeof(SessionSetup);
        private LevelSessionRunner runner;
        private Coroutine pending;
        private bool disposed;
        private string stage = "idle", error = "";
        private LevelCandidate selected;
        private int dlc, seed;
        private float deadline;
        private readonly List<object> transitions = new List<object>();
        private StoryTimerPolicy timerPolicy;
        private StoryTutorialSkipPolicy tutorialPolicy;
        public string Name { get { return "native-main-one-one-session-v3-early-tutorial-skip"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation, Dictionary<string,object> args)
        {
            if (disposed) throw new ObjectDisposedException("LevelSessionModule");
            if (args == null) args = new Dictionary<string,object>();
            if (operation == "status") { if(args.Count != 0) throw new ArgumentException("status takes no arguments."); return Status(); }
            if (operation != "load-main-1-1") throw new ArgumentException("Use status or load-main-1-1.");
            if (pending != null) throw new InvalidOperationException("A native session transition is already running.");
            foreach (string key in args.Keys) if (key != "seed") throw new ArgumentException("Only seed is accepted; level and player count are fixed.");
            seed = ReadSeed(args);
            Preflight();
            if(timerPolicy == null) { timerPolicy = new StoryTimerPolicy(Record); timerPolicy.Install(); }
            if(tutorialPolicy == null) { tutorialPolicy = new StoryTutorialSkipPolicy(()=>stage); tutorialPolicy.Install(); }
            tutorialPolicy.BeginRequest();
            // All controls stay under X's existing neutral/loading fence. Its own
            // LateUpdate resumes native loading and FinishLoad refreshes metadata,
            // pauses at InLevel, and exposes the original arm handshake.
            NativeSessionBridge.ForceNeutral("external-main-one-one-load");
            Set(bridge,"holdPause",false); Set(bridge,"loadComplete",false);
            Set(bridge,"lastError",""); Set(bridge,"readyUnityFrame",-1);
            deadline = Time.realtimeSinceStartup + 150f;
            Set(bridge,"loadingDeadline",deadline);
            Set(setup,"error",""); Set(setup,"waitingFor","");
            SetStage("external_main_1_1_start");
            Set(bridge,"loading",true);
            InvokeCore("SuperchargedPatch.Helpers","Resume",Type.EmptyTypes,new object[0]);
            ActiveStateCollector.ClearCacheAfterWarp(0);
            StateInvalidityManager.InvalidReason = "";
            WarpableRoundData.ConfigureSeedForNextRound(seed);
            var obj = new GameObject("Native main 1-1 session module");
            UnityEngine.Object.DontDestroyOnLoad(obj);
            runner = obj.AddComponent<LevelSessionRunner>();
            pending = runner.StartCoroutine(Guard(Run()));
            return Status();
        }

        private static int ReadSeed(Dictionary<string,object> args)
        {
            object raw;
            if (!args.TryGetValue("seed",out raw)) return 0;
            if (!(raw is int) && !(raw is long)) throw new ArgumentException("seed must be an Int32 integer.");
            long value = Convert.ToInt64(raw);
            if (value < Int32.MinValue || value > Int32.MaxValue) throw new ArgumentException("seed is outside Int32.");
            return (int)value;
        }

        private void Preflight()
        {
            if (!SaveIsolation.IsInstalled) throw new InvalidOperationException("Existing isolated save profile is required.");
            // Resolve all reflected core boundaries before changing loader state.
            foreach(string n in new[]{"holdPause","loadComplete","lastError","readyUnityFrame","loadingDeadline","loading","root"}) Field(bridge,n,true);
            foreach(string n in new[]{"stage","error","waitingFor","pending"}) Field(setup,n,true);
            Method(bridge,"FinishLoad",new[]{typeof(bool),typeof(string)},true);
            Method(setup,"ValidateFourLocalUsers",Type.EmptyTypes,true);
            Method(Core("SuperchargedPatch.Helpers"),"Resume",Type.EmptyTypes,true);
            Method(Native("ServerMessenger"),"LoadLevel",new[]{typeof(string),typeof(GameState),typeof(bool),typeof(GameState)},true);
            Method(Native("ServerMessenger"),"LoadLevel",new[]{typeof(uint),typeof(uint),typeof(GameState),typeof(GameState)},true);
            Method(Native("ServerMessenger"),"SetupCoopSession",new[]{typeof(int),typeof(GameProgress.GameProgressData),typeof(bool[]),typeof(SessionConfig)},true);
            if((bool)Get(bridge,"loading") || Get(setup,"pending") != null) throw new InvalidOperationException("Existing bridge/session setup is busy.");
            if(!NativeSessionBridge.KitchenReady) throw new InvalidOperationException("Start from an existing ready native kitchen.");
            if((bool)InvokeNative("ConnectionStatus","IsInSession",Type.EmptyTypes,new object[0]))
                throw new InvalidOperationException("This adapter requires the existing offline local session.");
            ValidatePlayers();
        }

        private IEnumerator Guard(IEnumerator routine)
        {
            yield return null; // Execute must retain the Coroutine before completion.
            while (true)
            {
                bool more = false; object current = null; Exception failure = null;
                try { more = routine.MoveNext(); if(more) current = routine.Current; }
                catch(Exception ex) { failure = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex; }
                if (failure != null)
                {
                    if(tutorialPolicy!=null)tutorialPolicy.CancelRequest("level-session-failed");
                    error = failure.ToString();
                    Set(setup,"error",error); SetStage("failed");
                    Method(bridge,"FinishLoad",new[]{typeof(bool),typeof(string)},true).Invoke(null,new object[]{false,error});
                    Debug.LogError("[LevelSession] " + error);
                    break;
                }
                if (!more) break;
                yield return current;
            }
            pending = null;
        }

        private IEnumerator Run()
        {
            GameSession session = GameUtils.GetGameSession();
            // Base-game -1 is also cross-checked against the live native frontend
            // prefab below. Never reuse Carnival's numeric level index.
            if (session == null || session.DLC != -1)
            {
                SetStage("external_returning_to_frontend");
                ServerGameSetup.Mode = GameMode.OnlineKitchen;
                InvokeNative("ServerMessenger","LoadLevel",new[]{typeof(string),typeof(GameState),typeof(bool),typeof(GameState)},
                    new object[]{"StartScreen",GameState.MainMenu,true,GameState.NotSet});
                while (!FrontendStarted()) { CheckDeadline(); yield return null; }
                // Native Start normally retains the current local profile. Reuse
                // the existing adapter if its normal callbacks require re-engagement.
                SessionSetup.Execute("players");
                while(Get(setup,"pending") != null) { CheckDeadline(); yield return null; }
                if(SessionSetup.Failed) throw new InvalidOperationException(SessionSetup.LastError);
                ValidatePlayers();
                var screen = StartScreenFlow.Instance;
                if(screen != null && (bool)Field(typeof(StartScreenFlow),"m_bCheckingForEngagement",true).GetValue(null))
                {
                    var manager = GameUtils.RequestManager<PlayerManager>();
                    var owner = manager == null ? null : manager.GetUser(EngagementSlot.One);
                    if(owner == null) throw new InvalidOperationException("Native frontend owner is missing.");
                    Field(typeof(StartScreenFlow),"m_bCheckingForEngagement",true).SetValue(null,false);
                    var callback = typeof(StartScreenFlow).GetMethod("OnEngagementFinished",Instance);
                    if(callback == null) throw new MissingMethodException("StartScreenFlow.OnEngagementFinished");
                    callback.Invoke(screen,new object[]{owner});
                }
                int quiet = 0;
                while(quiet < 6)
                {
                    CheckDeadline();
                    bool popup = false;
                    foreach(var p in UnityEngine.Object.FindObjectsOfType<NewContentPopup>())
                        if(p.gameObject.activeInHierarchy) { popup = true; p.OnPopupConfirm(); }
                    var frontend = T17FrontendFlow.Instance;
                    quiet = frontend != null && !frontend.IsCameraTransitioning() && !frontend.BlockFocusKitchen && !popup ? quiet+1 : 0;
                    yield return null;
                }
                SetStage("external_resolving_base_session");
                var prefabs = Field(typeof(T17FrontendFlow),"m_CoopGameSessionPrefabs",false).GetValue(T17FrontendFlow.Instance);
                var property = prefabs.GetType().GetProperty("AllData",Instance);
                if(property == null) throw new MissingMemberException("DLCSerializedGameSessions.AllData");
                GameSession basePrefab = null;
                foreach(GameSession candidate in (GameSession[])property.GetValue(prefabs,null))
                    if(candidate != null && candidate.DLC == -1)
                    { if(basePrefab != null) throw new InvalidOperationException("Ambiguous native base session prefabs."); basePrefab = candidate; }
                if(basePrefab == null) throw new InvalidOperationException("Native frontend has no base cooperative session prefab.");
                dlc = basePrefab.DLC;
                // Validate prefab mapping before the native method replaces the session.
                var progress = basePrefab.GetComponentsInChildren<GameProgress>(true);
                if(progress.Length != 1) throw new InvalidOperationException("Base prefab must contain exactly one native GameProgress.");
                Resolve(progress[0].GetSceneDirectory());
                session = T17FrontendFlow.Instance.StartEmptySession(GameSession.GameType.Cooperative,dlc);
                if(session == null || session.DLC != dlc) throw new InvalidOperationException("Native base session creation failed.");
                yield return null;
            }
            dlc = session.DLC;
            if(dlc != -1 || session.TypeSettings.Type != GameSession.GameType.Cooperative)
                throw new InvalidOperationException("Expected the native base cooperative session.");
            var directory = session.Progress.GetSceneDirectory();
            selected = Resolve(directory);
            var variant = directory.Scenes[selected.Index].GetSceneVarient(4);
            session.GameModeKind = Kind.Campaign;
            session.LevelSettings.SceneDirectoryVarientEntry = variant;
            timerPolicy.ApplySelected(variant.LevelConfig as KitchenLevelConfigBase,"before-native-load");
            tutorialPolicy.Arm();
            session.FillShownMetaDialogStatus();
            SetStage("external_preparing_main_1_1");
            if(!(bool)InvokeNative("ServerMessenger","SetupCoopSession",new[]{typeof(int),typeof(GameProgress.GameProgressData),typeof(bool[]),typeof(SessionConfig)},
                new object[]{dlc,session.Progress.SaveData,session.m_shownMetaDialogs,session.GameModeSessionConfig}))
                throw new InvalidOperationException("Native cooperative session setup failed.");
            yield return null; yield return null;
            ValidatePlayers(); ServerGameSetup.Mode = GameMode.Campaign;
            Scene prior = SceneManager.GetActiveScene();
            SetStage("external_loading_main_1_1");
            InvokeNative("ServerMessenger","LoadLevel",new[]{typeof(uint),typeof(uint),typeof(GameState),typeof(GameState)},
                new object[]{(uint)selected.Index,4u,GameState.LoadKitchen,GameState.RunKitchen});
            yield return null;
            while(!Loaded(prior)) { CheckDeadline(); tutorialPolicy.ObserveCleanup(); yield return null; }
            ValidatePlayers();
            SetStage("kitchen_ready");
            // Original X FinishLoad performs metadata refresh/pause. The scoped
            // config instrumentation avoids the native first-delivery suppressor.
            while(!NativeSessionBridge.KitchenReady) { CheckDeadline(); tutorialPolicy.ObserveCleanup(); yield return null; }
            tutorialPolicy.ObserveCleanup();
            tutorialPolicy.RequireCompleted();
            stage = "complete"; Record();
        }

        private bool FrontendStarted()
        {
            var screen = StartScreenFlow.Instance;
            return screen != null && T17FrontendFlow.Instance != null &&
                Field(typeof(StartScreenFlow),"m_PlayerManager",false).GetValue(screen) != null &&
                Field(typeof(StartScreenFlow),"m_SaveManager",false).GetValue(screen) != null;
        }
        private static LevelCandidate Resolve(SceneDirectoryData directory)
        {
            if(directory == null || directory.Scenes == null) throw new InvalidOperationException("Native scene directory unavailable.");
            var rows = new List<LevelCandidate>();
            for(int i=0;i<directory.Scenes.Length;i++)
            {
                var e=directory.Scenes[i]; if(e==null) continue; var v=e.GetSceneVarient(4);
                rows.Add(new LevelCandidate{Index=i,Label=e.Label,World=e.World.ToString(),Theme=e.Theme.ToString(),Allowed=e.ActuallyAllowed,Hidden=e.IsHidden,
                    Players=v==null?0:v.PlayerCount,Scene=v==null?null:v.SceneName,Config=v==null||v.LevelConfig==null?null:v.LevelConfig.name});
            }
            return LevelSelection.MainOneOne(rows);
        }
        private bool Loaded(Scene prior)
        {
            var current = SceneManager.GetActiveScene();
            if(current.Equals(prior) || !String.Equals(current.name,selected.Scene,StringComparison.OrdinalIgnoreCase) || !MultiplayerController.IsSynchronisationActive()) return false;
            if(ServerUserSystem.m_Users.Count!=4 || ClientUserSystem.m_Users.Count!=4) return false;
            for(int i=0;i<4;i++) if(ServerUserSystem.m_Users._items[i].GameState!=GameState.InLevel || ClientUserSystem.m_Users._items[i].GameState!=GameState.InLevel) return false;
            var chefs=UnityEngine.Object.FindObjectsOfType<PlayerControls>(); if(chefs.Length!=4) return false;
            var seen=new bool[4];
            foreach(var chef in chefs)
            { var p=chef.GetComponent<PlayerIDProvider>(); if(p==null||!p.IsLocallyControlled()) return false; int id=(int)p.GetID(); if(id<0||id>=4||seen[id]) return false; seen[id]=true; }
            var s=GameUtils.GetGameSession();
            if(s==null||s.DLC!=dlc||s.LevelSettings.SceneDirectoryVarientEntry==null||s.LevelSettings.SceneDirectoryVarientEntry.PlayerCount!=4 ||
                !String.Equals(s.LevelSettings.SceneDirectoryVarientEntry.SceneName,selected.Scene,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Loaded native session/variant differs from the selected directory entry.");
            return true;
        }
        private void CheckDeadline()
        {
            if(Time.realtimeSinceStartup>=deadline) throw new TimeoutException("Main 1-1 native load exceeded its fixed150-second bound at "+stage);
            if(!(bool)Get(bridge,"loading") && !NativeSessionBridge.KitchenReady) throw new InvalidOperationException("Core bridge stopped loading: "+Get(bridge,"lastError"));
        }
        private void ValidatePlayers() { Method(setup,"ValidateFourLocalUsers",Type.EmptyTypes,true).Invoke(null,null); }
        private void SetStage(string value) { stage=value; Set(setup,"stage",value); Set(setup,"waitingFor",value); Record(); }
        private object Status() { return new Dictionary<string,object>{{"name",Name},{"stage",stage},{"pending",pending!=null},{"error",error},{"seed",seed},{"dlc",dlc},{"selected",selected},{"scene",SceneManager.GetActiveScene().name},{"coreReady",NativeSessionBridge.KitchenReady},{"immediateStory11Timer",true},{"tutorialSkipSupported",true},{"tutorialSkip",tutorialPolicy==null?null:tutorialPolicy.Diagnostics()},{"timerPolicy",timerPolicy==null?null:timerPolicy.Diagnostics()},{"transitions",transitions.ToArray()}}; }
        private void Record()
        {
            var row=new Dictionary<string,object>{{"utc",DateTime.UtcNow.ToString("o")},{"unityFrame",Time.frameCount},{"stage",stage},{"scene",SceneManager.GetActiveScene().name},{"dlc",dlc},{"selected",selected},{"error",error},{"seed",seed},{"immediateStory11Timer",true},{"timerPolicy",timerPolicy==null?null:timerPolicy.Diagnostics()}};
            transitions.Add(row);
            string json=(string)InvokeCore("SuperchargedPatch.Bridge.JsonText","Serialize",new[]{typeof(object)},new object[]{row});
            File.AppendAllText(Path.Combine((string)Get(bridge,"root"),"artifacts/level-session-transitions.jsonl"),json+Environment.NewLine);
        }
        public void Dispose()
        {
            if(disposed) return;
            if(pending!=null) throw new InvalidOperationException("Cannot dispose an active native level transition.");
            if(tutorialPolicy!=null)tutorialPolicy.Dispose();
            if(timerPolicy!=null)timerPolicy.Dispose();
            disposed=true; if(runner!=null) UnityEngine.Object.Destroy(runner.gameObject);
        }
        private static Type Core(string n) { return typeof(NativeSessionBridge).Assembly.GetType(n,true); }
        private static Type Native(string n) { return typeof(GameSession).Assembly.GetType(n,true); }
        private static FieldInfo Field(Type t,string n,bool isStatic) { var f=t.GetField(n,isStatic?Static:Instance); if(f==null)throw new MissingFieldException(t.FullName,n); return f; }
        private static MethodInfo Method(Type t,string n,Type[] signature,bool isStatic) { var m=t.GetMethod(n,isStatic?Static:Instance,null,signature,null); if(m==null)throw new MissingMethodException(t.FullName,n); return m; }
        private static object Get(Type t,string n) { return Field(t,n,true).GetValue(null); }
        private static void Set(Type t,string n,object v) { Field(t,n,true).SetValue(null,v); }
        private static object InvokeCore(string t,string n,Type[] s,object[] a) { return Method(Core(t),n,s,true).Invoke(null,a); }
        private static object InvokeNative(string t,string n,Type[] s,object[] a) { return Method(Native(t),n,s,true).Invoke(null,a); }
    }
    public sealed class LevelSessionRunner : MonoBehaviour { }
}
