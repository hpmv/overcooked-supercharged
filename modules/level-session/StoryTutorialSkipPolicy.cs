using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GameModes;
using HarmonyLib;
using SuperchargedPatch.Bridge;
using Team17.Online;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SuperchargedPatch.Authoring.Modules
{
    // The native popup coroutine already owns every presentation and pause-state
    // transition.  This policy replaces only its 15-second input/timer wait with
    // an already-complete enumerator; RunTutorial then performs its ordinary
    // dismissal callback, one-frame handoff, canvas restore and pause release.
    public sealed class StoryTutorialSkipPolicy : IDisposable
    {
        private const string Owner = "supercharged.module.story11.early-tutorial-skip";
        private const BindingFlags Instance = BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static StoryTutorialSkipPolicy active;
        private readonly FieldInfo popupField = Field(typeof(ClientTutorialPopupController),"m_popup");
        private readonly FieldInfo controllerField = Field(typeof(ClientTutorialPopupController),"m_controller");
        private readonly FieldInfo dismissedField = Field(typeof(ClientTutorialPopupController),"m_dismissed");
        private readonly FieldInfo hudCanvasField = Field(typeof(ClientTutorialPopupController),"m_hudCanvas");
        private readonly FieldInfo hoverCanvasField = Field(typeof(ClientTutorialPopupController),"m_hoverIconCanvas");
        private readonly FieldInfo serverControllerField = Field(typeof(ServerTutorialPopupController),"m_controller");
        private readonly FieldInfo suppressorsField = Field(typeof(TimeManager),"m_arbitrationSupressors");
        private readonly Func<string> stageProvider;
        private Harmony harmony;
        private bool requestOpen,armed,intercepted,cleanupObserved;
        private int generation,prefixCalls,armFrame=-1,interceptFrame=-1,cleanupFrame=-1;
        private int shutdownPrefixCalls,shutdownPostfixCalls;
        private string lastGuard="not-run",lastError="",cancelReason="";
        private bool shutdownPrefixValid;
        private LevelIntroFlowroutine interceptedFlow;
        private ClientTutorialPopupController interceptedController;
        private GameObject interceptedUi;
        private object admission,shutdownPrefixState,shutdownPostfixState,cleanupState;

        public StoryTutorialSkipPolicy(Func<string> stageProvider)
        {
            if(stageProvider==null)throw new ArgumentNullException("stageProvider");
            this.stageProvider=stageProvider;
        }

        public void Install()
        {
            if(active!=null&&!ReferenceEquals(active,this))
                throw new InvalidOperationException("Another Story11 tutorial skip policy is active.");
            var target=typeof(LevelIntroFlowroutine).GetMethod("TutorialDismissRoutine",Instance,null,
                new[]{typeof(GameObject)},null);
            var prefix=typeof(StoryTutorialSkipPolicy).GetMethod("BeforeTutorialDismissRoutine",
                BindingFlags.Static|BindingFlags.Public);
            var shutdown=typeof(ClientTutorialPopupController).GetMethod("Shutdown",Instance,null,Type.EmptyTypes,null);
            var shutdownPrefix=typeof(StoryTutorialSkipPolicy).GetMethod("BeforePopupShutdown",
                BindingFlags.Static|BindingFlags.Public);
            var shutdownPostfix=typeof(StoryTutorialSkipPolicy).GetMethod("AfterPopupShutdown",
                BindingFlags.Static|BindingFlags.Public);
            if(target==null||target.ReturnType!=typeof(IEnumerator)||target.IsStatic||!target.IsPrivate||
                prefix==null||shutdown==null||shutdownPrefix==null||shutdownPostfix==null)
                throw new MissingMethodException("Native LevelIntroFlowroutine.TutorialDismissRoutine boundary.");
            harmony=new Harmony(Owner);active=this;
            try
            {
                harmony.Patch(target,prefix:new HarmonyMethod(prefix));
                harmony.Patch(shutdown,prefix:new HarmonyMethod(shutdownPrefix),postfix:new HarmonyMethod(shutdownPostfix));
            }
            catch { harmony.UnpatchSelf();active=null;harmony=null;throw; }
        }

        public void BeginRequest()
        {
            if(!ReferenceEquals(active,this)||harmony==null)
                throw new InvalidOperationException("Story11 tutorial skip policy is not installed.");
            if(requestOpen||armed)throw new InvalidOperationException("A Story11 tutorial skip request is already active.");
            generation++;
            requestOpen=true;armed=false;intercepted=false;cleanupObserved=false;
            prefixCalls=0;armFrame=-1;interceptFrame=-1;cleanupFrame=-1;
            shutdownPrefixCalls=0;shutdownPostfixCalls=0;shutdownPrefixValid=false;
            lastGuard="not-run";lastError="";cancelReason="";
            interceptedFlow=null;interceptedController=null;interceptedUi=null;
            admission=null;shutdownPrefixState=null;shutdownPostfixState=null;cleanupState=null;
        }

        public void Arm()
        {
            if(!requestOpen||armed)throw new InvalidOperationException("Story11 tutorial skip request is not ready to arm.");
            string reason;
            if(!ExactSelectedSession(false,out reason))
                throw new InvalidOperationException("Cannot arm Story11 tutorial skip: "+reason);
            armed=true;armFrame=Time.frameCount;
        }

        public static bool BeforeTutorialDismissRoutine(LevelIntroFlowroutine __instance,
            GameObject _ui,ref IEnumerator __result)
        {
            var policy=active;
            if(policy==null||!policy.armed)return true;
            policy.prefixCalls++;
            try { return policy.TryIntercept(__instance,_ui,ref __result); }
            catch(Exception ex)
            {
                // Never strand the native popup after it has acquired its pause
                // owners.  Any diagnostic failure falls back to the original wait.
                policy.lastGuard="exception";policy.lastError=ex.ToString();
                return true;
            }
        }

        private bool TryIntercept(LevelIntroFlowroutine flow,GameObject ui,ref IEnumerator result)
        {
            string reason;
            if(intercepted){lastGuard="duplicate-native-dismiss-routine";return true;}
            if(flow==null||ui==null){lastGuard="missing-flow-or-ui";return true;}
            if(stageProvider()!="external_loading_main_1_1")
            {lastGuard="unexpected-level-session-stage";return true;}
            if(!ExactSelectedSession(true,out reason)){lastGuard=reason;return true;}
            if(ClientGameSetup.Mode!=GameMode.Campaign){lastGuard="client-mode-not-campaign";return true;}
            if(IsInOnlineSession()){lastGuard="online-session";return true;}
            if(!MultiplayerController.IsSynchronisationActive())
            {lastGuard="synchronisation-not-active";return true;}
            if(!NativeSessionBridge.InputBlocked||NativeSessionBridge.KitchenReady)
            {lastGuard="bridge-load-fence-not-active";return true;}
            if(ServerUserSystem.m_Users.Count!=4||ClientUserSystem.m_Users.Count!=4)
            {lastGuard="not-four-local-users";return true;}

            var clients=flow.gameObject.GetComponents<ClientTutorialPopupController>();
            if(clients.Length!=1){lastGuard="client-popup-controller-count-"+clients.Length;return true;}
            var client=clients[0];
            var popup=popupField.GetValue(client) as GameObject;
            var controller=controllerField.GetValue(client);
            var hud=hudCanvasField.GetValue(client) as Canvas;
            var hover=hoverCanvasField.GetValue(client) as Canvas;
            if(!ReferenceEquals(popup,ui)){lastGuard="popup-identity-mismatch";return true;}
            if(controller==null){lastGuard="missing-shared-popup-controller";return true;}
            var popupControllers=UnityEngine.Object.FindObjectsOfType<TutorialPopupController>();
            if(popupControllers.Length!=1||!ReferenceEquals(popupControllers[0],controller))
            {lastGuard="shared-popup-controller-count-or-identity";return true;}
            if(!ui.activeSelf||!ui.activeInHierarchy){lastGuard="popup-not-active";return true;}
            if((bool)dismissedField.GetValue(client)){lastGuard="popup-already-dismissed";return true;}
            if(hud==null||hover==null||hud.gameObject.name!="ScalingHUDCanvas"||
                hover.gameObject.name!="HoverIconCanvas"||hud.enabled||hover.enabled)
            {lastGuard="native-canvases-not-disabled";return true;}

            int matchingServers=0;
            foreach(var server in UnityEngine.Object.FindObjectsOfType<ServerTutorialPopupController>())
                if(ReferenceEquals(serverControllerField.GetValue(server),controller))matchingServers++;
            if(matchingServers!=1){lastGuard="shared-server-controller-count-"+matchingServers;return true;}

            var manager=GameUtils.RequireManager<TimeManager>();
            var suppressors=(List<object>[])suppressorsField.GetValue(manager);
            int mainOwners=OwnerCount(suppressors,TimeManager.PauseLayer.Main,client);
            int cameraOwners=OwnerCount(suppressors,TimeManager.PauseLayer.Camera,client);
            int flowMainOwners=OwnerCount(suppressors,TimeManager.PauseLayer.Main,flow);
            if(mainOwners!=1||cameraOwners!=1||flowMainOwners!=1)
            {lastGuard="native-pause-owner-count-main-"+mainOwners+"-camera-"+cameraOwners+
                "-flow-main-"+flowMainOwners;return true;}

            intercepted=true;interceptFrame=Time.frameCount;interceptedFlow=flow;
            interceptedController=client;interceptedUi=ui;
            lastGuard="accepted";
            admission=new Dictionary<string,object>{{"generation",generation},{"unityFrame",interceptFrame},
                {"scene",SceneManager.GetActiveScene().name},{"popupInstanceId",ui.GetInstanceID()},
                {"clientControllerInstanceId",client.GetInstanceID()},{"matchingServerControllers",matchingServers},
                {"mainPauseOwnerCount",mainOwners},{"cameraPauseOwnerCount",cameraOwners},
                {"flowMainPauseOwnerCount",flowMainOwners},
                {"hudCanvasDisabled",!hud.enabled},{"hoverCanvasDisabled",!hover.enabled},
                {"nativeDismissedBefore",false}};
            result=ImmediateComplete();
            return false;
        }

        public static void BeforePopupShutdown(ClientTutorialPopupController __instance)
        {
            var policy=active;
            if(policy==null||!policy.armed||!policy.intercepted||
                !ReferenceEquals(policy.interceptedController,__instance))return;
            try { policy.ObserveShutdownPrefix(__instance); }
            catch(Exception ex) { policy.lastError="shutdown-prefix: "+ex;policy.shutdownPrefixValid=false; }
        }

        public static void AfterPopupShutdown(ClientTutorialPopupController __instance)
        {
            var policy=active;
            if(policy==null||!policy.armed||!policy.intercepted||
                !ReferenceEquals(policy.interceptedController,__instance))return;
            try { policy.ObserveShutdownPostfix(__instance); }
            catch(Exception ex) { policy.lastError="shutdown-postfix: "+ex;policy.cleanupObserved=false; }
        }

        private void ObserveShutdownPrefix(ClientTutorialPopupController client)
        {
            shutdownPrefixCalls++;
            var popup=popupField.GetValue(client) as GameObject;
            var hud=hudCanvasField.GetValue(client) as Canvas;
            var hover=hoverCanvasField.GetValue(client) as Canvas;
            bool dismissed=(bool)dismissedField.GetValue(client);
            var manager=GameUtils.RequireManager<TimeManager>();
            var suppressors=(List<object>[])suppressorsField.GetValue(manager);
            int mainOwners=OwnerCount(suppressors,TimeManager.PauseLayer.Main,client);
            int cameraOwners=OwnerCount(suppressors,TimeManager.PauseLayer.Camera,client);
            int flowMainOwners=OwnerCount(suppressors,TimeManager.PauseLayer.Main,interceptedFlow);
            int delta=Time.frameCount-interceptFrame;
            shutdownPrefixValid=shutdownPrefixCalls==1&&delta==1&&dismissed&&
                ReferenceEquals(popup,interceptedUi)&&popup!=null&&!popup.activeSelf&&!popup.activeInHierarchy&&
                hud!=null&&hud.enabled&&hover!=null&&hover.enabled&&
                mainOwners==0&&cameraOwners==0&&flowMainOwners==1;
            shutdownPrefixState=new Dictionary<string,object>{{"unityFrame",Time.frameCount},
                {"framesAfterIntercept",delta},{"dismissed",dismissed},{"savedPopupStillOwned",ReferenceEquals(popup,interceptedUi)},
                {"popupInactive",popup!=null&&!popup.activeSelf&&!popup.activeInHierarchy},
                {"hudCanvasEnabled",hud!=null&&hud.enabled},{"hoverCanvasEnabled",hover!=null&&hover.enabled},
                {"mainPauseOwnerCount",mainOwners},{"cameraPauseOwnerCount",cameraOwners},
                {"flowMainPauseOwnerCount",flowMainOwners},{"valid",shutdownPrefixValid}};
        }

        private void ObserveShutdownPostfix(ClientTutorialPopupController client)
        {
            shutdownPostfixCalls++;
            bool popupCleared=popupField.GetValue(client)==null;
            shutdownPostfixState=new Dictionary<string,object>{{"unityFrame",Time.frameCount},
                {"popupFieldCleared",popupCleared},{"prefixValid",shutdownPrefixValid},
                {"prefixCalls",shutdownPrefixCalls},{"postfixCalls",shutdownPostfixCalls}};
            cleanupObserved=shutdownPrefixValid&&popupCleared&&shutdownPrefixCalls==1&&shutdownPostfixCalls==1;
            if(cleanupObserved)cleanupFrame=Time.frameCount;
        }

        public void ObserveCleanup()
        {
            if(!intercepted||interceptedController==null)return;
            var popup=popupField.GetValue(interceptedController) as GameObject;
            var hud=hudCanvasField.GetValue(interceptedController) as Canvas;
            var hover=hoverCanvasField.GetValue(interceptedController) as Canvas;
            bool dismissed=(bool)dismissedField.GetValue(interceptedController);
            var manager=GameUtils.RequestManager<TimeManager>();
            var suppressors=manager==null?null:(List<object>[])suppressorsField.GetValue(manager);
            int mainOwners=OwnerCount(suppressors,TimeManager.PauseLayer.Main,interceptedController);
            int cameraOwners=OwnerCount(suppressors,TimeManager.PauseLayer.Camera,interceptedController);
            bool popupReleased=popup==null;
            bool hudEnabled=hud!=null&&hud.enabled,hoverEnabled=hover!=null&&hover.enabled;
            cleanupState=new Dictionary<string,object>{{"generation",generation},{"unityFrame",Time.frameCount},
                {"framesAfterIntercept",Time.frameCount-interceptFrame},{"dismissed",dismissed},
                {"popupFieldCleared",popupReleased},{"originalPopupDestroyed",interceptedUi==null},
                {"hudCanvasEnabled",hudEnabled},{"hoverCanvasEnabled",hoverEnabled},
                {"mainPauseOwnerCount",mainOwners},{"cameraPauseOwnerCount",cameraOwners}};
        }

        public void RequireCompleted()
        {
            ObserveCleanup();
            if(!requestOpen)throw new InvalidOperationException("Story11 tutorial skip request is not active.");
            var manager=GameUtils.RequireManager<TimeManager>();
            var suppressors=(List<object>[])suppressorsField.GetValue(manager);
            int popupMain=OwnerCount(suppressors,TimeManager.PauseLayer.Main,interceptedController);
            int popupCamera=OwnerCount(suppressors,TimeManager.PauseLayer.Camera,interceptedController);
            int flowMain=OwnerCount(suppressors,TimeManager.PauseLayer.Main,interceptedFlow);
            var hud=hudCanvasField.GetValue(interceptedController) as Canvas;
            var hover=hoverCanvasField.GetValue(interceptedController) as Canvas;
            bool finalClean=popupField.GetValue(interceptedController)==null&&popupMain==0&&popupCamera==0&&flowMain==0&&
                hud!=null&&hud.enabled&&hover!=null&&hover.enabled&&!TimeManager.IsPaused(TimeManager.PauseLayer.Camera)&&
                TimeManager.IsPaused(TimeManager.PauseLayer.Main)&&NativeSessionBridge.InputBlocked&&NativeSessionBridge.KitchenReady;
            if(prefixCalls!=1||shutdownPrefixCalls!=1||shutdownPostfixCalls!=1||
                !intercepted||!cleanupObserved||!finalClean)
                throw new InvalidOperationException("Story11 tutorial skip did not complete exactly once: "+
                    "prefixCalls="+prefixCalls+", shutdownPrefixCalls="+shutdownPrefixCalls+
                    ", shutdownPostfixCalls="+shutdownPostfixCalls+", intercepted="+intercepted+
                    ", cleanup="+cleanupObserved+", finalClean="+finalClean+
                    ", guard="+lastGuard+", error="+lastError);
            armed=false;requestOpen=false;
        }

        public void CancelRequest(string reason)
        {
            armed=false;requestOpen=false;cancelReason=reason??"cancelled";
        }

        public object Diagnostics()
        {
            return new Dictionary<string,object>{{"installed",ReferenceEquals(active,this)&&harmony!=null},
                {"scope","offline-base-main-1-1/four-local-players"},{"generation",generation},
                {"requestOpen",requestOpen},{"armed",armed},{"prefixCalls",prefixCalls},
                {"shutdownPrefixCalls",shutdownPrefixCalls},{"shutdownPostfixCalls",shutdownPostfixCalls},
                {"intercepted",intercepted},{"cleanupObserved",cleanupObserved},
                {"armFrame",armFrame},{"interceptFrame",interceptFrame},{"cleanupFrame",cleanupFrame},
                {"lastGuard",lastGuard},{"lastError",lastError},{"cancelReason",cancelReason},
                {"admission",admission},{"shutdownPrefix",shutdownPrefixState},
                {"shutdownPostfix",shutdownPostfixState},{"cleanup",cleanupState},{"waitOnlyMutation",true},
                {"nativeRunTutorialPerformsDismissAndCleanup",true}};
        }

        public void Dispose()
        {
            armed=false;requestOpen=false;
            if(harmony!=null)harmony.UnpatchSelf();
            harmony=null;
            if(ReferenceEquals(active,this))active=null;
        }

        private static IEnumerator ImmediateComplete() { yield break; }

        private static int OwnerCount(List<object>[] suppressors,TimeManager.PauseLayer layer,object owner)
        {
            int index=(int)layer;
            if(suppressors==null||index<0||index>=suppressors.Length||suppressors[index]==null)return 0;
            int count=0;
            foreach(var candidate in suppressors[index])if(ReferenceEquals(candidate,owner))count++;
            return count;
        }

        private static bool ExactSelectedSession(bool requireScene,out string reason)
        {
            var session=GameUtils.GetGameSession();
            var variant=session==null||session.LevelSettings==null?null:session.LevelSettings.SceneDirectoryVarientEntry;
            if(session==null||session.DLC!=-1||session.TypeSettings.Type!=GameSession.GameType.Cooperative)
            {reason="not-base-cooperative-session";return false;}
            if(session.GameModeKind!=Kind.Campaign)
            {reason="session-kind-not-campaign";return false;}
            if(variant==null||variant.PlayerCount!=4||
                !String.Equals(variant.SceneName,LevelSelection.Scene,StringComparison.OrdinalIgnoreCase)||
                variant.LevelConfig==null||variant.LevelConfig.name!="Sushi_1_1S_4P")
            {reason="variant-not-exact-story11-four-player";return false;}
            if(requireScene&&!String.Equals(SceneManager.GetActiveScene().name,LevelSelection.Scene,StringComparison.OrdinalIgnoreCase))
            {reason="active-scene-not-story11";return false;}
            reason="accepted";return true;
        }

        private static FieldInfo Field(Type type,string name)
        {
            var field=type.GetField(name,Instance);
            if(field==null)throw new MissingFieldException(type.FullName,name);
            return field;
        }

        private static bool IsInOnlineSession()
        {
            var type=typeof(GameSession).Assembly.GetType("ConnectionStatus",true);
            var method=type.GetMethod("IsInSession",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,
                null,Type.EmptyTypes,null);
            if(method==null||method.ReturnType!=typeof(bool))
                throw new MissingMethodException("ConnectionStatus.IsInSession");
            return (bool)method.Invoke(null,null);
        }
    }
}
