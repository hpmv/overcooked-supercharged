using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Hpmv;
using SuperchargedPatch.Authoring;
using SuperchargedPatch.Extensions;

namespace SuperchargedPatch.Authoring.Modules
{
    // External CLR2 module only. No native clock/counter/physics setter and no
    // Helpers.Resume patch. The original handler still performs the one resume.
    public sealed class ResumePhaseModule : IAuthoringModule
    {
        private sealed class Origin
        {
            internal InjectorServer Server;
            internal InputData Input, AcceptedInput;
            internal long Epoch, Exchange;
            internal int Frame, Phase, TargetPhase;
            internal bool Paused;
            internal object RestoreToken;
            internal AnimatorContract Animator;
            internal int AnimatorStatus = -1;
            internal bool AnimatorOrdinary, ReleaseArmed, CommitStarted;
            internal int ReleaseUnityFrame = -1, ReleasePhase = -1;
        }
        private sealed class AnimatorContract
        {
            internal Assembly Assembly;
            internal MethodInfo Advance, Acknowledge, Abort;
            internal string Identity;
        }
        private readonly object gate = new object();
        private readonly List<Origin> origins = new List<Origin>();
        private static ResumePhaseModule active;
        private Harmony harmony;
        private MethodInfo publish, accept, late, commit, pause;
        private FieldInfo observationValue, observationEpoch, observationExchange, serverEpoch, serverSync, serverCurrentInput;
        private Origin held, releaseInFlight;
        private int heldCallbacks, heldInvocations, lastWaitUnityFrame = -1;
        private bool disposed;
        private string failure;
        private long observedResumes, acceptedResumeMappings, acceptedResumesWithoutOrigin, allowedResumes, deferredCallbacks, evictedOrigins;
        private long animatorStageHolds, animatorCoordinatedResumes, commitStarts, commitAcknowledgements;
        private readonly List<object> receipts = new List<object>();
        private object lastPublishedResume, lastAcceptedResume, lastGateFailure;
        private const double ResumePhaseMetadataBase = 1000.0;
        public string Name { get { return "native-resume-phase-v4-explicit-target"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation, Dictionary<string, object> args)
        {
            if (disposed) throw new ObjectDisposedException("ResumePhaseModule");
            if (args != null && args.Count != 0) throw new ArgumentException("Resume-phase operations accept no arguments.");
            if (operation == "activate") Activate();
            else if (operation == "deactivate") Deactivate();
            else if (operation != "status") throw new ArgumentException("Unknown resume-phase operation: " + operation);
            lock (gate) return new Dictionary<string, object> {
                {"name",Name},{"apiVersion",1},{"active",ReferenceEquals(active,this)},{"failure",failure},
                {"period",6},{"maximumHeldCallbacks",12},{"observedResumes",observedResumes},
                {"acceptedResumeMappings",acceptedResumeMappings},{"acceptedResumesWithoutOrigin",acceptedResumesWithoutOrigin},
                {"allowedResumes",allowedResumes},{"deferredCallbacks",deferredCallbacks},{"evictedOrigins",evictedOrigins},
                {"pendingOrigins",origins.Count},{"holdingResume",held!=null},{"heldCallbacks",heldCallbacks},
                {"heldInvocations",heldInvocations},{"releaseInFlight",releaseInFlight!=null},
                {"animatorStageHolds",animatorStageHolds},{"animatorCoordinatedResumes",animatorCoordinatedResumes},
                {"commitStarts",commitStarts},{"commitAcknowledgements",commitAcknowledgements},
                {"heldAnimatorContract",held==null||held.Animator==null?null:held.Animator.Identity},
                {"heldAnimatorStatus",held==null?-1:held.AnimatorStatus},
                {"pendingOriginIdentities",OriginDiagnostics()},{"lastPublishedResume",lastPublishedResume},
                {"lastAcceptedResume",lastAcceptedResume},{"lastGateFailure",lastGateFailure},
                {"receipts",receipts.ToArray()},
                {"policy","Bind the exact published RPC resume through InjectorServer.Accept to the exact retained input. The controller carries its saved six-phase target in the otherwise-unused GameSpeed field of this plain-resume envelope; no RPC sampling guess is made. A rewind may advance the active Chef Animator coordinator across distinct ordinary paused Unity frames; only its status2 arms the original Helpers.Resume at the exact target. A successful release is acknowledged only after exactly one InjectorServer.CommitFrame. No phase/time normalization."}
            };
        }
        private void Activate()
        {
            if (!TimeManager.IsPaused(TimeManager.PauseLayer.Main)) throw new InvalidOperationException("Resume-phase activation requires native pause.");
            RequireTiming();
            if (harmony != null) return;
            if (active != null) throw new InvalidOperationException("A resume-phase instance is already active.");
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var server = typeof(InjectorServer);
            var observation = server.GetNestedType("Observation",BindingFlags.NonPublic);
            if (observation == null) throw new InvalidOperationException("Expected frozen-X observation envelope is absent.");
            observationValue = observation.GetField("Value",flags);
            observationEpoch = observation.GetField("Epoch",flags);
            observationExchange = observation.GetField("Exchange",flags);
            serverEpoch = server.GetField("connectionEpoch",flags); serverSync = server.GetField("sync",flags);
            serverCurrentInput = server.GetField("currentInput",flags);
            publish = server.GetMethod("PublishReply",flags,null,new[]{observation,typeof(InputData),typeof(bool)},null);
            accept = server.GetMethod("Accept",flags,null,new[]{typeof(InputData),typeof(bool)},null);
            commit = server.GetMethod("CommitFrame",BindingFlags.Instance|BindingFlags.Public,null,Type.EmptyTypes,null);
            late = typeof(ControllerHandler).GetMethod("LateUpdate",BindingFlags.Static|BindingFlags.Public);
            Type helpers=typeof(ControllerHandler).Assembly.GetType("SuperchargedPatch.Helpers",true);
            pause=helpers.GetMethod("Pause",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,null,Type.EmptyTypes,null);
            if (observationValue == null || observationValue.FieldType != typeof(OutputData) ||
                observationEpoch == null || observationEpoch.FieldType != typeof(long) ||
                observationExchange == null || observationExchange.FieldType != typeof(long) ||
                serverEpoch == null || serverEpoch.FieldType != typeof(long) || serverSync == null ||
                serverCurrentInput == null || serverCurrentInput.FieldType != typeof(InputData) ||
                publish == null || publish.ReturnType != typeof(void) || accept == null || accept.ReturnType != typeof(void) ||
                commit == null || commit.ReturnType != typeof(void) || late == null || late.ReturnType != typeof(void) ||
                pause == null || pause.ReturnType != typeof(void))
                throw new InvalidOperationException("Frozen-X input pump contract differs; no hook installed.");
            var installed = Harmony.GetPatchInfo(late);
            if(installed!=null)
                foreach(var patch in installed.Prefixes)
                    if(patch.owner.StartsWith("supercharged.authoring.resume-phase.",StringComparison.Ordinal))
                        throw new InvalidOperationException("Another resume-phase revision is still installed.");
            harmony = new Harmony("supercharged.authoring.resume-phase." + GetType().Assembly.GetName().Name);
            active = this;
            try
            {
                harmony.Patch(publish,prefix:new HarmonyMethod(GetType().GetMethod("ObserveReply",BindingFlags.Static|BindingFlags.Public)));
                harmony.Patch(accept,postfix:new HarmonyMethod(GetType().GetMethod("ObserveAccept",BindingFlags.Static|BindingFlags.Public)));
                var latePrefix=new HarmonyMethod(GetType().GetMethod("BeforeLateUpdate",BindingFlags.Static|BindingFlags.Public));
                var latePostfix=new HarmonyMethod(GetType().GetMethod("AfterLateUpdate",BindingFlags.Static|BindingFlags.Public));
                var lateFinalizer=new HarmonyMethod(GetType().GetMethod("FinalizeLateUpdate",BindingFlags.Static|BindingFlags.Public));
                latePrefix.priority=Priority.First;latePostfix.priority=Priority.Last;lateFinalizer.priority=Priority.Last;
                harmony.Patch(late,prefix:latePrefix,postfix:latePostfix,finalizer:lateFinalizer);
                harmony.Patch(commit,
                    prefix:new HarmonyMethod(GetType().GetMethod("BeforeCommitFrame",BindingFlags.Static|BindingFlags.Public)),
                    postfix:new HarmonyMethod(GetType().GetMethod("AfterCommitFrame",BindingFlags.Static|BindingFlags.Public)));
            }
            catch { harmony.UnpatchSelf(); harmony=null; active=null; throw; }
        }
        private static void RequireTiming()
        {
            if (UnityEngine.Time.captureFramerate != 60 || UnityEngine.Time.fixedDeltaTime != 0.02f)
                throw new InvalidOperationException("Resume phase requires observed capture60/native fixed50 schedule.");
        }
        // RPC worker: only immutable managed envelope fields and a locked local
        // record. Publication itself retains all original epoch/exchange checks.
        public static void ObserveReply(InjectorServer __instance, object __0, InputData __1, bool __2)
        {
            var module = active;
            if (module == null || !__2 || __1 == null || !__1.RequestResume) return;
            var output = (OutputData)module.observationValue.GetValue(__0);
            int targetPhase;
            bool hasTarget=TryResumeTarget(__1,out targetPhase);
            var origin = new Origin {Server=__instance,Input=__1,Epoch=(long)module.observationEpoch.GetValue(__0),
                Exchange=(long)module.observationExchange.GetValue(__0),Frame=output.FrameNumber,
                Phase=output.FramesSinceLastNoPhysicsFrame,TargetPhase=hasTarget?targetPhase:-1,
                Paused=output.LastFramePaused&&output.NextFramePaused};
            lock(module.gate)
            {
                if (!ReferenceEquals(active,module)) return;
                module.observedResumes++; module.origins.Add(origin);
                module.lastPublishedResume=module.Identity("publish",origin,__1,null);
                if(module.origins.Count>16){module.origins.RemoveAt(0);module.evictedOrigins++;}
            }
        }
        // Main thread: bind the exact reply object observed at publication to
        // the exact object retained by InjectorServer after its existing input
        // filter. This preserves provenance even if the filter substitutes an
        // equivalent object; it does not modify either input or the filter.
        public static void ObserveAccept(InjectorServer __instance, InputData __0, bool __1)
        {
            var module=active;
            if(module==null || __instance==null || __0==null || !__1)return;
            InputData accepted=(InputData)module.serverCurrentInput.GetValue(__instance);
            lock(module.gate)
            {
                if(!ReferenceEquals(active,module))return;
                Origin origin=null;
                for(int i=0;i<module.origins.Count;i++)
                    if(ReferenceEquals(module.origins[i].Input,__0)&&ReferenceEquals(module.origins[i].Server,__instance))
                    {origin=module.origins[i];break;}
                if(origin!=null)
                {
                    origin.AcceptedInput=accepted;
                    module.acceptedResumeMappings++;
                }
                else if(__0.RequestResume || accepted!=null&&accepted.RequestResume)
                    module.acceptedResumesWithoutOrigin++;
                if(__0.RequestResume || accepted!=null&&accepted.RequestResume)
                    module.lastAcceptedResume=module.Identity("accept",origin,__0,accepted);
            }
        }
        public static bool BeforeLateUpdate()
        {
            var module = active;
            if (module == null) return true;
            if (!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
            {
                if(module.failure!=null)module.Fail(module.failure);
                else if(module.HasReleaseInFlight())module.Fail("A released resume callback escaped without commit acknowledgement while main pause was already clear.");
                return true;
            }
            try { return module.GateResume(); }
            catch(Exception error) { module.Fail(error.ToString()); return true; }
        }
        private bool GateResume()
        {
            if(failure!=null)return true;
            if(HasReleaseInFlight())throw new InvalidOperationException("A released resume callback re-entered before commit acknowledgement.");
            InputData input;
            var server=Injector.Server;
            if(!server.TryGetCurrentInput(out input))
            {
                lock(gate)if(held!=null)
                    throw new InvalidOperationException("The exact accepted resume disappeared while its multiphase restore was held.");
                // Do not let the original body poll a second time: a reply
                // arriving during its flush could otherwise bypass this guard.
                ControllerHandler.MultiplayerController?.FlushAllPendingBatchedMessages();
                server.SkipPausedCallback();
                return false;
            }
            if(input==null || !input.RequestResume)
            {
                lock(gate)if(held!=null)
                    throw new InvalidOperationException("The filtered accepted input changed while an exact resume transaction was held.");
                return true;
            }
            RequireTiming();
            InputData storedInput=(InputData)serverCurrentInput.GetValue(server);
            if(storedInput==null||!ReferenceEquals(input,storedInput)||!storedInput.RequestResume)
                throw new InvalidOperationException("The resume is not the exact InputData object retained by InjectorServer.Accept.");
            Origin origin=null;int matches=0;
            lock(gate)
                for(int i=0;i<origins.Count;i++)
                    if(origins[i].AcceptedInput!=null&&ReferenceEquals(origins[i].AcceptedInput,storedInput)&&
                        ReferenceEquals(origins[i].Server,server)){origin=origins[i];matches++;}
            if(matches!=1 || origin==null || !origin.Paused || origin.Phase<0 || origin.Phase>=6 ||
                origin.TargetPhase<0 || origin.TargetPhase>=6)
            {
                lock(gate)lastGateFailure=new Dictionary<string,object> {
                    {"inputIdentity",Id(input)},{"storedInputIdentity",Id(storedInput)},{"serverIdentity",Id(server)},
                    {"requestResume",input!=null&&input.RequestResume},{"exactAcceptedMatches",matches},{"origins",OriginDiagnostics()}
                };
                throw new InvalidOperationException("Resume has no exact valid paused RPC origin.");
            }
            if(!PlainResume(input))
                throw new InvalidOperationException("Combined resume directives are outside this plain alignment transaction.");
            lock(serverSync.GetValue(server))
                if((long)serverEpoch.GetValue(server)!=origin.Epoch)
                    throw new InvalidOperationException("Controller generation changed while resume was pending.");
            int phase=server.CurrentFrameData.FramesSinceLastNoPhysicsFrame;
            if(phase<0 || phase>=6) throw new InvalidOperationException("Current native callback phase is outside observed six-frame schedule.");
            bool initialize=false;
            lock(gate)
            {
                if(held!=null&&!ReferenceEquals(held,origin)) throw new InvalidOperationException("Pending resume identity changed before execution.");
                if(held==null)
                {
                    held=origin;heldCallbacks=0;heldInvocations=0;lastWaitUnityFrame=-1;
                    origin.RestoreToken=new object();initialize=true;
                }
            }
            if(initialize)
            {
                origin.Animator=ResolveAnimatorContract();
                if(origin.Animator==null){origin.AnimatorOrdinary=true;origin.AnimatorStatus=0;}
            }
            int target=origin.TargetPhase;
            if(!origin.AnimatorOrdinary)
            {
                int status=AdvanceAnimator(origin,phase,UnityEngine.Time.frameCount,phase==target);
                origin.AnimatorStatus=status;
                if(status==0)origin.AnimatorOrdinary=true;
                else if(status==1)
                {
                    animatorStageHolds++;
                    return HoldPausedCallback(server);
                }
                else if(status==2)
                {
                    if(phase!=target)throw new InvalidOperationException("Chef Animator armed finalization outside the exact resume target phase.");
                    animatorCoordinatedResumes++;
                    return ArmRelease(origin,phase);
                }
                else throw new InvalidOperationException("Chef Animator resume coordinator returned unknown status "+status+".");
            }
            if(phase==target)return ArmRelease(origin,phase);
            return HoldPausedCallback(server);
        }

        private static bool PlainResume(InputData input)
        {
            int targetPhase;
            return TryResumeTarget(input,out targetPhase)&&!input.RequestPause&&input.Warp==null&&
                !input.__isset.resetOrderSeed&&
                (input.Input==null||input.Input.Count==0);
        }

        private static bool TryResumeTarget(InputData input,out int targetPhase)
        {
            targetPhase=-1;
            if(input==null||!input.RequestResume||!input.__isset.gameSpeed)return false;
            double encoded=input.GameSpeed-ResumePhaseMetadataBase;
            if(encoded<0||encoded>=6||encoded!=Math.Truncate(encoded))return false;
            targetPhase=(int)encoded;
            return true;
        }

        private bool HoldPausedCallback(InjectorServer server)
        {
            lock(gate)
            {
                heldInvocations++;
                if(lastWaitUnityFrame!=UnityEngine.Time.frameCount)
                {
                    heldCallbacks++;lastWaitUnityFrame=UnityEngine.Time.frameCount;
                }
                if(heldCallbacks>12)
                    throw new InvalidOperationException("Resume did not complete Animator staging and reach its intended native phase within twelve distinct Unity frames.");
                if(heldInvocations>48)
                    throw new InvalidOperationException("Resume staging re-entered more than forty-eight callbacks without completing.");
                deferredCallbacks++;
            }
            // This is the complete skipped ControllerHandler callback. It keeps
            // the accepted object cached and permits the ordinary paused Unity
            // PlayerLoop (including Animator/Playable maintenance) to run again.
            ControllerHandler.MultiplayerController?.FlushAllPendingBatchedMessages();
            server.SkipPausedCallback();
            return false;
        }

        private bool ArmRelease(Origin origin,int phase)
        {
            lock(gate)
            {
                if(!ReferenceEquals(held,origin)||releaseInFlight!=null)
                    throw new InvalidOperationException("Resume release ownership changed while finalization was armed.");
                origin.ReleaseArmed=true;origin.ReleaseUnityFrame=UnityEngine.Time.frameCount;origin.ReleasePhase=phase;
                origin.CommitStarted=false;releaseInFlight=origin;
            }
            // The unchanged ControllerHandler now performs its flush, capture,
            // one Helpers.Resume call and one CommitFrame call. Success is not
            // recorded until the CommitFrame postfix acknowledges it.
            return true;
        }

        private AnimatorContract ResolveAnimatorContract()
        {
            const string typeName="SuperchargedPatch.Authoring.Modules.ChefAnimatorCheckpointModule";
            BindingFlags flags=BindingFlags.Public|BindingFlags.Static|BindingFlags.DeclaredOnly;
            Type[] advanceParameters={typeof(object),typeof(InjectorServer),typeof(InputData),typeof(long),typeof(long),
                typeof(int),typeof(int),typeof(int),typeof(int),typeof(int),typeof(bool)};
            AnimatorContract selected=null;
            foreach(Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type=assembly.GetType(typeName,false);
                if(type==null)continue;
                MethodInfo version=type.GetMethod("ResumeCoordinationVersion",flags,null,Type.EmptyTypes,null);
                if(version==null)continue; // Earlier observer-only revisions have no coordination contract.
                if(version.ReturnType!=typeof(int))
                    throw new InvalidOperationException("Chef Animator resume coordination version method has the wrong return type in "+assembly.FullName+".");
                int value=(int)InvokeStatic(version,null);
                if(value==0)continue;
                if(value!=2)throw new InvalidOperationException("Unsupported active Chef Animator resume coordination version "+value+" in "+assembly.FullName+".");
                MethodInfo advance=type.GetMethod("AdvanceAcceptedResumeRestore",flags,null,advanceParameters,null);
                MethodInfo acknowledge=type.GetMethod("AcknowledgeAcceptedResumeRestore",flags,null,new[]{typeof(object)},null);
                MethodInfo abort=type.GetMethod("AbortAcceptedResumeRestore",flags,null,new[]{typeof(object),typeof(string)},null);
                if(advance==null||advance.ReturnType!=typeof(int)||acknowledge==null||acknowledge.ReturnType!=typeof(void)||
                    abort==null||abort.ReturnType!=typeof(void))
                    throw new InvalidOperationException("Active Chef Animator resume coordination contract has an unexpected signature in "+assembly.FullName+".");
                if(selected!=null)
                    throw new InvalidOperationException("More than one active Chef Animator resume coordination provider is loaded.");
                selected=new AnimatorContract {Assembly=assembly,Advance=advance,Acknowledge=acknowledge,Abort=abort,
                    Identity=assembly.FullName+"|mvid="+assembly.ManifestModule.ModuleVersionId.ToString("D")};
            }
            MethodInfo helpersResume=pause.DeclaringType.GetMethod("Resume",
                BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,null,Type.EmptyTypes,null);
            int installedChefPrefixes=0;
            Patches patches=helpersResume==null?null:Harmony.GetPatchInfo(helpersResume);
            if(patches!=null)
                foreach(Patch patch in patches.Prefixes)
                    if(patch.owner.StartsWith("supercharged.authoring.chef-animator-checkpoint.",StringComparison.Ordinal))
                        installedChefPrefixes++;
            if(selected==null&&installedChefPrefixes!=0)
                throw new InvalidOperationException("An Animator checkpoint owns Helpers.Resume but exposes no active version-1 coordination contract.");
            if(selected!=null&&installedChefPrefixes!=1)
                throw new InvalidOperationException("The active Animator coordination provider does not have exactly one Helpers.Resume prefix.");
            return selected;
        }

        private int AdvanceAnimator(Origin origin,int phase,int unityFrame,bool mayArm)
        {
            if(origin.Animator==null)throw new InvalidOperationException("Chef Animator coordination was requested without a resolved provider.");
            object value=InvokeStatic(origin.Animator.Advance,new object[]{origin.RestoreToken,origin.Server,origin.AcceptedInput,
                origin.Epoch,origin.Exchange,origin.Frame,origin.Phase,origin.TargetPhase,phase,unityFrame,mayArm});
            if(!(value is int))throw new InvalidOperationException("Chef Animator advance returned a non-integer status.");
            return (int)value;
        }

        private static object InvokeStatic(MethodInfo method,object[] arguments)
        {
            try{return method.Invoke(null,arguments);}
            catch(TargetInvocationException error)
            {
                Exception cause=error.InnerException??error;
                throw new InvalidOperationException(method.DeclaringType.FullName+"."+method.Name+" failed: "+cause.Message,cause);
            }
        }

        private bool HasReleaseInFlight()
        {
            lock(gate)return releaseInFlight!=null;
        }

        public static void BeforeCommitFrame(InjectorServer __instance)
        {
            ResumePhaseModule module=active;
            if(module==null)return;
            lock(module.gate)
            {
                Origin origin=module.releaseInFlight;
                if(origin==null)return;
                if(!ReferenceEquals(origin.Server,__instance))
                    throw new InvalidOperationException("A different InjectorServer attempted to commit the released resume callback.");
                if(origin.CommitStarted)
                    throw new InvalidOperationException("The released resume callback attempted more than one CommitFrame.");
                InputData stored=(InputData)module.serverCurrentInput.GetValue(__instance);
                if(!ReferenceEquals(stored,origin.AcceptedInput)||!PlainResume(stored))
                    throw new InvalidOperationException("The exact accepted resume object changed before CommitFrame.");
                if(UnityEngine.Time.frameCount!=origin.ReleaseUnityFrame||
                    __instance.CurrentFrameData.FramesSinceLastNoPhysicsFrame!=origin.ReleasePhase)
                    throw new InvalidOperationException("Resume phase or Unity frame changed before CommitFrame.");
                if(TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                    throw new InvalidOperationException("CommitFrame was reached before the original Helpers.Resume released main pause.");
                origin.CommitStarted=true;module.commitStarts++;
            }
        }

        public static void AfterCommitFrame(InjectorServer __instance)
        {
            ResumePhaseModule module=active;
            if(module==null)return;
            Origin origin;
            lock(module.gate)origin=module.releaseInFlight;
            if(origin==null)return;
            if(!ReferenceEquals(origin.Server,__instance)||!origin.CommitStarted)
                throw new InvalidOperationException("Resume CommitFrame acknowledgement does not match its released transaction.");
            if(TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                throw new InvalidOperationException("Resume CommitFrame completed without releasing the main pause.");
            if(origin.AnimatorStatus==2)
            {
                if(origin.Animator==null)throw new InvalidOperationException("Coordinated Animator resume lost its provider before acknowledgement.");
                InvokeStatic(origin.Animator.Acknowledge,new[]{origin.RestoreToken});
            }
            lock(module.gate)
            {
                if(!ReferenceEquals(module.releaseInFlight,origin)||!ReferenceEquals(module.held,origin))
                    throw new InvalidOperationException("Resume ownership changed during CommitFrame acknowledgement.");
                int target=origin.TargetPhase;
                module.receipts.Add(new Dictionary<string,object>{{"epoch",origin.Epoch},{"exchange",origin.Exchange},
                    {"frame",origin.Frame},{"observedRpcPhase",origin.Phase},{"targetPhase",target},{"executedPhase",origin.ReleasePhase},
                    {"heldCallbacks",module.heldCallbacks},{"heldInvocations",module.heldInvocations},
                    {"unityFrame",origin.ReleaseUnityFrame},{"animatorStatus",origin.AnimatorStatus},
                    {"animatorContract",origin.Animator==null?null:origin.Animator.Identity},{"commitCount",1}});
                if(module.receipts.Count>64)module.receipts.RemoveAt(0);
                module.allowedResumes++;module.commitAcknowledgements++;
                module.origins.Remove(origin);module.releaseInFlight=null;module.held=null;
                module.heldCallbacks=0;module.heldInvocations=0;module.lastWaitUnityFrame=-1;
            }
        }

        public static void AfterLateUpdate()
        {
            ResumePhaseModule module=active;
            if(module!=null&&module.HasReleaseInFlight())
                throw new InvalidOperationException("Released resume LateUpdate returned without exactly one acknowledged CommitFrame.");
        }

        public static Exception FinalizeLateUpdate(Exception __exception)
        {
            ResumePhaseModule module=active;
            if(module!=null&&__exception!=null&&module.HasReleaseInFlight())
                module.Fail("Released resume callback failed before exact CommitFrame acknowledgement: "+__exception);
            return __exception;
        }

        private void Fail(string reason)
        {
            Origin abortOrigin;
            lock(gate)
            {
                if(failure==null)failure=String.IsNullOrEmpty(reason)?"Unspecified resume coordination failure.":reason;
                abortOrigin=releaseInFlight??held;
                releaseInFlight=null;held=null;heldCallbacks=0;heldInvocations=0;lastWaitUnityFrame=-1;origins.Clear();
            }
            if(abortOrigin!=null&&abortOrigin.Animator!=null&&!abortOrigin.AnimatorOrdinary)
            {
                try{InvokeStatic(abortOrigin.Animator.Abort,new object[]{abortOrigin.RestoreToken,failure});}
                catch(Exception abortError)
                {
                    lock(gate)failure=failure+" | Chef Animator abort also failed: "+abortError.Message;
                }
            }
            StateInvalidityManager.InvalidReason="AUTHORING_RESUME_PHASE_FAILED: "+failure;
            try{Bridge.NativeSessionBridge.ForceNeutral("resume-phase-failed");}
            catch(Exception neutralError)
            {
                lock(gate)failure=failure+" | Neutral fence failed: "+neutralError.Message;
                StateInvalidityManager.InvalidReason="AUTHORING_RESUME_PHASE_FAILED: "+failure;
            }
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
            {
                try{pause.Invoke(null,null);}
                catch(Exception pauseError)
                {
                    lock(gate)failure=failure+" | Main-pause recovery failed: "+pauseError.Message;
                    StateInvalidityManager.InvalidReason="AUTHORING_RESUME_PHASE_FAILED: "+failure;
                }
            }
            // CurrentInput now filters to the bridge's existing neutral pause.
            // Let the original body report it; AwaitingResume must fail visibly.
        }
        private void Deactivate()
        {
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main))throw new InvalidOperationException("Resume-phase retirement requires native pause.");
            bool pending;lock(gate)pending=held!=null||releaseInFlight!=null;
            if(pending)Fail("Module retired with an accepted resume still pending.");
            if(ReferenceEquals(active,this))active=null;
            if(harmony!=null){harmony.UnpatchSelf();harmony=null;}
            lock(gate){origins.Clear();held=null;releaseInFlight=null;heldCallbacks=0;heldInvocations=0;lastWaitUnityFrame=-1;}
        }
        private object Identity(string stage,Origin origin,InputData published,InputData accepted)
        {
            return new Dictionary<string,object> {
                {"stage",stage},{"serverIdentity",Id(origin==null?null:origin.Server)},
                {"publishedInputIdentity",Id(published)},{"acceptedInputIdentity",Id(accepted)},
                {"originEpoch",origin==null?(long?)null:origin.Epoch},{"originExchange",origin==null?(long?)null:origin.Exchange},
                {"originFrame",origin==null?(int?)null:origin.Frame},{"observedRpcPhase",origin==null?(int?)null:origin.Phase},
                {"targetPhase",origin==null?(int?)null:origin.TargetPhase},
                {"originPaused",origin==null?(bool?)null:origin.Paused},
                {"animatorStatus",origin==null?(int?)null:origin.AnimatorStatus},
                {"animatorContract",origin==null||origin.Animator==null?null:origin.Animator.Identity},
                {"releaseArmed",origin!=null&&origin.ReleaseArmed},{"commitStarted",origin!=null&&origin.CommitStarted}
            };
        }
        private object[] OriginDiagnostics()
        {
            var result=new object[origins.Count];
            for(int i=0;i<origins.Count;i++)
            {
                Origin origin=origins[i];
                result[i]=new Dictionary<string,object> {
                    {"serverIdentity",Id(origin.Server)},{"publishedInputIdentity",Id(origin.Input)},
                    {"acceptedInputIdentity",Id(origin.AcceptedInput)},{"epoch",origin.Epoch},{"exchange",origin.Exchange},
                    {"frame",origin.Frame},{"observedRpcPhase",origin.Phase},{"targetPhase",origin.TargetPhase},{"paused",origin.Paused},
                    {"animatorStatus",origin.AnimatorStatus},{"animatorContract",origin.Animator==null?null:origin.Animator.Identity},
                    {"releaseArmed",origin.ReleaseArmed},{"commitStarted",origin.CommitStarted}
                };
            }
            return result;
        }
        private static int Id(object value){return value==null?0:RuntimeHelpers.GetHashCode(value);}
        public void Dispose(){if(disposed)return;Deactivate();disposed=true;}
    }
}
