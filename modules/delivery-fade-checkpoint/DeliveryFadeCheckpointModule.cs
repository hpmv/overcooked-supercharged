using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SuperchargedPatch.AlteredComponents;
using SuperchargedPatch.Authoring;
using SuperchargedPatch.Bridge;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // Captures the native delivery iterator and applies only authoring-time,
    // fail-closed inverse restoration. Ordinary delivery execution is observed
    // but never intercepted or changed.
    public sealed class DeliveryFadeCheckpointModule : IAuthoringModule
    {
        private const BindingFlags Instance = BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;

        private sealed class Sequence
        {
            internal ClientPlateStation Station;
            internal ClientPlate Plate;
            internal GameObject PlateObject;
            internal PlateStation.DeliveryFX Effects;
            internal IEnumerator Iterator;
            internal int StationId, PlateId, PlateObjectId;
            internal string IteratorType;
            internal object FactoryPreimage;
            internal RendererState[] PresentationRenderers;
            internal ClientAttachedOrderCosmeticDecisions PresentationOwner;
            internal int PresentationOwnerId;
            internal GameObject PresentationContainer;
            internal int PresentationContainerId;
            internal string PresentationContainerKey;
            internal bool PresentationContainerPhysicsFree;
            internal Material[] MaterialsBeforeMoveNext;
            internal readonly HashSet<Material> OwnedMaterials=new HashSet<Material>();
            internal bool MaterialOwnershipComplete;
            internal int EntityId;
            internal IEnumerator SchedulerIterator;
            internal Coroutine SchedulerCoroutine;
        }

        private sealed class SequenceFrameState
        {
            internal Sequence Source;
            internal int EntityId,Pc;
            internal bool Disposing,Errored,CurrentIsNull,CurrentIsStationWait;
            internal float Progress;
            internal Collider[] Colliders;
            internal Rigidbody Body;
            internal MeshRenderer[] Renderers;
            internal GameObject Pfx;
            internal bool PfxAlive,PfxDetached;
        }

        private sealed class PlateState
        {
            internal int EntityId,ObjectId,ComponentId,ParentId;
            internal string Name;
            internal int Layer;
            internal bool ActiveSelf,ActiveInHierarchy;
            internal GameObject Object;
            internal ClientPlate Plate;
            internal Transform Parent;
            internal Vector3 LocalPosition,LocalScale;
            internal Quaternion LocalRotation;
            internal ColliderState[] Colliders;
            internal RendererState[] Renderers;
            internal ClientAttachedOrderCosmeticDecisions PresentationOwner;
            internal int PresentationOwnerId;
            internal GameObject PresentationContainer;
            internal int PresentationContainerId;
            internal string PresentationContainerKey;
            internal string PresentationCompositionFingerprint;
            internal bool PresentationContainerPhysicsFree;
            internal string[] ComponentTypes,ServerSynchroniserTypes,ClientSynchroniserTypes;
            internal ExternalUiState ExternalUi;
        }

        private sealed class ColliderState
        {
            internal Collider Collider;
            internal int InstanceId;
            internal string HierarchyKey,TypeName;
            internal bool Enabled,Trigger,ActiveSelf,ActiveInHierarchy;
        }

        private sealed class ExternalUiState
        {
            internal Component Controller;
            internal int ControllerId;
            internal string ControllerType;
            internal UnityEngine.Object Instance;
            internal int InstanceId;
            internal string InstanceType;
            internal GameObject Object;
            internal int ObjectId,ParentId,Layer;
            internal string Name;
            internal Transform Parent;
            internal bool ActiveSelf,ActiveInHierarchy;
            internal string[] ComponentTypes;
        }

        private sealed class RendererState
        {
            internal MeshRenderer Renderer;
            internal int InstanceId;
            internal string HierarchyKey;
            internal bool InPresentationContainer;
            internal string PresentationKey;
            internal int SharedMeshId;
            internal Vector3 LocalPosition,LocalScale;
            internal Quaternion LocalRotation;
            internal bool ActiveSelf,ActiveInHierarchy;
            internal int Layer;
            internal bool Enabled;
            internal Material[] Materials;
            internal int[] MaterialIds,ShaderIds;
            internal bool[] HasMode,HasAlpha;
            internal float[] Mode,Alpha;
        }

        private sealed class FrameState
        {
            internal int Frame;
            internal object CoreSnapshot;
            internal int PendingFades;
            internal object[] Sequences;
            internal SequenceFrameState[] SequenceStates;
            internal PlateState[] Plates;
        }

        private sealed class RestoreCandidate
        {
            internal int Frame;
            internal object CoreSnapshot;
            internal FrameState Target;
            internal PlateState TargetPlate;
            internal Sequence Current;
            internal Sequence Rebound;
            internal GameObject Pfx;
            internal bool DestroyedRoot;
            internal bool Applied;
            internal int ForcedPresentationStartComponentId;
            internal int[] DestroyedMaterialIds;
            internal int[] DiscardedReturnEntityIds;
            internal object Receipt;
            internal bool ActiveTargetComposite;
            internal Sequence CancelCurrent;
            internal PlateState CancelTargetPlate;
            internal SequenceFrameState TargetSequence;
            internal GameObject CancelPfx;
            internal Sequence ReboundActive;
            internal int[] ReboundHistoryFrames;
            internal int[] DiscardedIncompatibleHistoryFrames;
            internal object[] DiscardedIncompatibleHistory;
            internal bool ActiveEarlyApplied;
            internal GameObject RecreatedPlateObject;
            internal ClientPlate RecreatedPlate;
        }

        private sealed class Entity2HistoryRebind
        {
            internal int Frame;
            internal PlateState Plate;
            internal RendererState[] Renderers;
            internal ColliderState[] Colliders;
            internal SequenceFrameState[] Sequences;
        }

        private sealed class ForcedPresentationStart
        {
            internal ComboCosmeticDecisions Component;
            internal GameObject Outer,Inner;
            internal IClientOrderDefinition Order;
            internal RendererSceneInfo RendererInfo;
            internal bool Completed;
        }

        private static DeliveryFadeCheckpointModule active;
        private readonly List<Sequence> sequences = new List<Sequence>();
        private readonly SortedDictionary<int,FrameState> history = new SortedDictionary<int,FrameState>();
        private Harmony harmony;
        private FieldInfo coreHistory, coreRoundIdentity, delivered, planSnapshot, coreDeliveryFades, stationTrigger, attachedContainer, ingredientContentUiInstance;
        private FieldInfo mealContainer,mealOrderDefinition,mealRendererInfo,comboPrefabLookup;
        private MethodInfo comboStart,deliveryFactory,restoreFixedBodyPoses;
        private object observedRoundIdentity;
        private int lastFrame = -1;
        private long factories, captures, duplicates, resets, restores, historyRestores;
        private bool prepareMask, completeInProgress, admissionObserverRemoved;
        private object maskedCoreSnapshot;
        private int maskedDeliveryFadeCount;
        private GameObject admissionPlate;
        private int admissionInstance;
        private RestoreCandidate preparing, pending;
        private FrameState preparingHistory, pendingHistory;
        private PlateState resumePresentationTarget;
        private Sequence resumePresentationCurrent;
        private Sequence resumeActiveSequence;
        private Dictionary<string,object> resumeActiveReceipt;
        private object lastResumePresentationRestore;
        private readonly List<object> restoreReceipts=new List<object>();
        private readonly List<ForcedPresentationStart> suppressedNaturalPresentationStarts=new List<ForcedPresentationStart>();
        private ComboCosmeticDecisions manualPresentationStart;
        private long forcedPresentationStarts,suppressedNaturalStarts;
        private object lastPresentationLifecycleCompletion;
        private string failure;
        private object lastRendererMapFailure;
        private object lastRendererMaterialFailure;
        private object lastRecreatedPrefixValidation;
        private object lastCancellationPresentationRetirement;
        private object lastExternalUiFailure,lastAbsentTargetExternalUiRetirement;
        private bool disposed;

        public string Name { get { return "delivery-fade-checkpoint-r18b-external-ui-diagnostics"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation, Dictionary<string,object> args)
        {
            if (disposed) throw new ObjectDisposedException("DeliveryFadeCheckpointModule");
            if (args == null) args=new Dictionary<string,object>();
            if (args.Count != 0) throw new ArgumentException("Delivery fade checkpoint operations take no arguments.");
            if (operation == "activate") Activate();
            else if (operation == "deactivate") Deactivate();
            else if (operation == "clear") Clear();
            else if (operation != "status") throw new ArgumentException("Use activate, deactivate, clear or status.");
            return Status(operation);
        }

        private void Activate()
        {
            RequireFence();
            if (ReferenceEquals(active,this)) return;
            if (active != null) throw new InvalidOperationException("Another delivery fade checkpoint module is active.");
            var factory = AccessTools.DeclaredMethod(typeof(ClientPlateStation),"DeliverySequence",
                new[]{typeof(ClientPlate),typeof(PlateStation.DeliveryFX)});
            var iteratorType=typeof(ClientPlateStation).GetNestedTypes(Instance)
                .SingleOrDefault(value=>value.Name.Contains("<DeliverySequence>"));
            var moveNext=iteratorType==null?null:AccessTools.DeclaredMethod(iteratorType,"MoveNext",Type.EmptyTypes);
            var capture = AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint),"CaptureFrame",new[]{typeof(int)});
            var prepare = AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint),"Prepare",new[]{typeof(Hpmv.WarpSpec)});
            var complete = AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint.RestorePlan),"Complete",Type.EmptyTypes);
            restoreFixedBodyPoses=AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint.RestorePlan),"RestoreFixedBodyPoses",Type.EmptyTypes);
            var captureBoundary = AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint),"Capture",
                new[]{typeof(ServerKitchenFlowControllerBase),typeof(int)});
            var restoreFailure = AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint),"RecordRestoreFailure",
                new[]{typeof(int),typeof(Exception),typeof(bool)});
            Type helpers=typeof(NativeSessionBridge).Assembly.GetType("SuperchargedPatch.Helpers",true);
            var resume=AccessTools.DeclaredMethod(helpers,"Resume",Type.EmptyTypes);
            coreHistory = typeof(NativeKitchenCheckpoint).GetField("history",Static);
            coreRoundIdentity = typeof(NativeKitchenCheckpoint).GetField("roundIdentity",Static);
            delivered = typeof(NativePlateLifecycle).GetField("delivered",Static);
            planSnapshot=typeof(NativeKitchenCheckpoint.RestorePlan).GetField("snapshot",Instance);
            coreDeliveryFades=planSnapshot==null?null:planSnapshot.FieldType.GetField("DeliveryFades",Instance);
            stationTrigger=typeof(PlateStation).GetField("m_onFoodDeliveredTrigger",Instance);
            attachedContainer=FindField(typeof(ClientAttachedOrderCosmeticDecisions),"m_container");
            ingredientContentUiInstance=FindField(typeof(ClientIngredientContentGUI),"m_uiInstance");
            comboStart=AccessTools.DeclaredMethod(typeof(ComboCosmeticDecisions),"Start",Type.EmptyTypes);
            deliveryFactory=factory;
            mealContainer=FindField(typeof(MealCosmeticDecisions),"m_container");
            mealOrderDefinition=FindField(typeof(MealCosmeticDecisions),"m_iOrderDefinition");
            mealRendererInfo=FindField(typeof(MealCosmeticDecisions),"m_rendererSceneInfo");
            comboPrefabLookup=FindField(typeof(ComboCosmeticDecisions),"m_comboPrefabLookup");
            if (factory == null || !typeof(IEnumerator).IsAssignableFrom(factory.ReturnType) || iteratorType==null || moveNext==null
                || moveNext.ReturnType!=typeof(bool) || capture == null || prepare==null
                || complete==null || restoreFixedBodyPoses==null || restoreFixedBodyPoses.ReturnType!=typeof(void)
                || captureBoundary==null || restoreFailure==null || resume==null || resume.ReturnType!=typeof(void)
                || coreHistory == null || !typeof(IDictionary).IsAssignableFrom(coreHistory.FieldType)
                || coreRoundIdentity == null || delivered == null || !typeof(IDictionary).IsAssignableFrom(delivered.FieldType)
                || planSnapshot==null || coreDeliveryFades==null || coreDeliveryFades.FieldType!=typeof(int)
                || stationTrigger==null || stationTrigger.FieldType!=typeof(string)
                || attachedContainer==null || attachedContainer.FieldType!=typeof(GameObject)
                || ingredientContentUiInstance==null
                || !typeof(UnityEngine.Object).IsAssignableFrom(ingredientContentUiInstance.FieldType)
                || comboStart==null||comboStart.ReturnType!=typeof(void)
                || mealContainer==null||mealContainer.FieldType!=typeof(GameObject)
                || mealOrderDefinition==null||!typeof(IClientOrderDefinition).IsAssignableFrom(mealOrderDefinition.FieldType)
                || mealRendererInfo==null||mealRendererInfo.FieldType!=typeof(RendererSceneInfo)
                || comboPrefabLookup==null||comboPrefabLookup.FieldType!=typeof(ComboOrderToPrefabLookup))
                throw new InvalidOperationException("Installed delivery/checkpoint lifecycle contract differs.");
            harmony = new Harmony("supercharged.authoring.delivery-fade-checkpoint."+GetType().Assembly.GetName().Name);
            active = this;
            try
            {
                harmony.Patch(factory,postfix:Hook("AfterDeliverySequenceFactory"));
                harmony.Patch(moveNext,prefix:Hook("BeforeDeliveryMoveNext"),postfix:Hook("AfterDeliveryMoveNext"));
                harmony.Patch(capture,postfix:Hook("AfterCaptureFrame"));
                var preparePrefix=Hook("BeforePrepare");preparePrefix.priority=Priority.First;
                harmony.Patch(prepare,prefix:preparePrefix,postfix:Hook("AfterPrepare"),finalizer:Hook("FinalizePrepare"));
                harmony.Patch(restoreFixedBodyPoses,prefix:Hook("BeforeRestoreFixedBodyPoses"));
                harmony.Patch(complete,prefix:Hook("BeforeComplete"),postfix:Hook("AfterComplete"),finalizer:Hook("FinalizeComplete"));
                harmony.Patch(captureBoundary,prefix:Hook("BeforeCheckpointCapture"));
                harmony.Patch(restoreFailure,postfix:Hook("AfterRestoreFailure"));
                var comboStartPrefix=Hook("BeforeComboCosmeticStart");comboStartPrefix.priority=Priority.First;
                harmony.Patch(comboStart,prefix:comboStartPrefix);
                var resumePrefix=Hook("BeforeAuthoringResume");resumePrefix.priority=Priority.First;
                var resumePostfix=Hook("AfterAuthoringResume");resumePostfix.priority=Priority.Last;
                harmony.Patch(resume,prefix:resumePrefix,postfix:resumePostfix);
            }
            catch { Deactivate(); throw; }
        }

        private HarmonyMethod Hook(string name)
        {
            return new HarmonyMethod(GetType().GetMethod(name,BindingFlags.Public|BindingFlags.Static));
        }

        public static bool BeforeComboCosmeticStart(ComboCosmeticDecisions __instance)
        {
            var module=active;
            if(module==null)return true;
            if(ReferenceEquals(module.manualPresentationStart,__instance))
            {
                if(module.suppressedNaturalPresentationStarts.Any(value=>ReferenceEquals(value.Component,__instance)))
                    throw new InvalidOperationException("Exact authoring presentation Start entered twice.");
                module.suppressedNaturalPresentationStarts.Add(new ForcedPresentationStart { Component=__instance });
                return true;
            }
            int index=module.suppressedNaturalPresentationStarts.FindIndex(value=>ReferenceEquals(value.Component,__instance));
            if(index<0)return true;
            var marker=module.suppressedNaturalPresentationStarts[index];
            if(!marker.Completed||!Alive(marker.Component)||!Alive(marker.Outer)||!Alive(marker.Inner)
                ||!Alive(marker.RendererInfo)||!ReferenceEquals(marker.Component.gameObject,marker.Outer)
                ||!ReferenceEquals(module.mealContainer.GetValue(marker.Component),marker.Inner)
                ||!ReferenceEquals(module.mealOrderDefinition.GetValue(marker.Component),marker.Order)
                ||!ReferenceEquals(module.mealRendererInfo.GetValue(marker.Component),marker.RendererInfo))
            {
                module.failure="Exact authoring presentation Start changed before Unity duplicate suppression.";
                throw new InvalidOperationException(module.failure);
            }
            module.suppressedNaturalPresentationStarts.RemoveAt(index);
            module.suppressedNaturalStarts++;
            module.lastPresentationLifecycleCompletion=Map(
                "phase","natural-duplicate-suppressed","component",ObjectIdentity(__instance),
                "remainingSuppressionCount",module.suppressedNaturalPresentationStarts.Count,
                "scope","Only the Unity-scheduled duplicate of an exact authoring-forced virgin sushi Start is skipped once.");
            return false;
        }

        public static void AfterDeliverySequenceFactory(ClientPlateStation __instance, ClientPlate __0,
            PlateStation.DeliveryFX __1, IEnumerator __result)
        {
            var module=active;
            if(module==null)return;
            try { module.ObserveFactory(__instance,__0,__1,__result); module.failure=null; }
            catch(Exception error) { module.failure=error.ToString(); throw; }
        }

        public static void BeforeDeliveryMoveNext(object __instance)
        {
            var module=active;if(module==null)return;
            try
            {
                var sequence=module.sequences.SingleOrDefault(value=>ReferenceEquals(value.Iterator,__instance));
                if(sequence==null||sequence.MaterialOwnershipComplete)return;
                sequence.MaterialsBeforeMoveNext=Resources.FindObjectsOfTypeAll<Material>();
            }
            catch(Exception error){module.failure=error.ToString();}
        }

        public static void AfterDeliveryMoveNext(object __instance)
        {
            var module=active;if(module==null)return;
            try
            {
                var sequence=module.sequences.SingleOrDefault(value=>ReferenceEquals(value.Iterator,__instance));
                if(sequence==null||sequence.MaterialOwnershipComplete||sequence.MaterialsBeforeMoveNext==null)return;
                var before=new HashSet<Material>(sequence.MaterialsBeforeMoveNext);
                foreach(var material in Resources.FindObjectsOfTypeAll<Material>())
                    if(!before.Contains(material))sequence.OwnedMaterials.Add(material);
                sequence.MaterialsBeforeMoveNext=null;
                object renderers=Field(sequence.Iterator.GetType(),"<allRenderers>__0").GetValue(sequence.Iterator);
                sequence.MaterialOwnershipComplete=renderers!=null;
            }
            catch(Exception error){module.failure=error.ToString();}
        }

        public static void AfterCaptureFrame(int __0)
        {
            var module=active;
            if(module==null||!NativeSessionBridge.KitchenReady)return;
            try { module.Capture(__0); module.failure=null; }
            catch(Exception error) { module.failure=error.ToString(); }
        }

        public static void BeforePrepare(Hpmv.WarpSpec __0)
        {
            var module=active;if(module==null)return;
            module.RestoreTargetDeliveryFadeMask();module.preparing=null;module.preparingHistory=null;
            if(__0==null)return;
            int current=NativePlateLifecycle.PendingDeliveryFades;
            FrameState target;
            if(!module.history.TryGetValue(__0.Frame,out target)||!ReferenceEquals(target.CoreSnapshot,module.CoreSnapshot(__0.Frame)))
                throw new InvalidOperationException("No exact delivery-fade sidecar exists for output frame "+__0.Frame+".");
            module.preparingHistory=target;
            if(current==0&&target.PendingFades==0)
            {
                if(module.TargetPlatesRemainExact(target))return;
                module.preparing=module.PrepareDestroyedTarget(__0,target);
                return;
            }
            if(current==1&&target.PendingFades==1)
            {
                module.preparing=module.PrepareActiveDestroyedTarget(__0,target);
                module.MaskTargetDeliveryFade(target);
                module.TemporarilyRemoveAdmissionObserver();
                return;
            }
            if(target.PendingFades!=0)
                throw new InvalidOperationException("Rewind targets inside a delivered-plate fade remain outside this checkpoint scope.");
            module.preparing=module.PrepareInactiveTarget(__0.Frame,target,current);
            module.TemporarilyRemoveAdmissionObserver();
        }

        public static void AfterPrepare(NativeKitchenCheckpoint.RestorePlan __result)
        {
            var module=active;if(module==null)return;
            try
            {
                if(module.preparingHistory!=null)
                {
                    if(__result==null||!ReferenceEquals(module.planSnapshot.GetValue(__result),module.preparingHistory.CoreSnapshot))
                        throw new InvalidOperationException("Prepared native restore plan differs from the delivery-fade history sidecar.");
                    module.pendingHistory=module.preparingHistory;
                }
                if(module.preparing!=null)
                {
                    if(__result==null||!ReferenceEquals(module.planSnapshot.GetValue(__result),module.preparing.CoreSnapshot))
                        throw new InvalidOperationException("Prepared native restore plan differs from the delivery-fade sidecar.");
                    module.pending=module.preparing;
                }
            }
            finally
            {
                module.RestoreTargetDeliveryFadeMask();module.RestoreAdmissionObserver();
                module.preparing=null;module.preparingHistory=null;
            }
        }

        public static Exception FinalizePrepare(Exception __exception)
        {
            var module=active;
            if(module!=null)
            {
                module.RestoreTargetDeliveryFadeMask();
                module.RestoreAdmissionObserver();
                module.preparing=null;module.preparingHistory=null;
                if(__exception!=null){module.pending=null;module.pendingHistory=null;}
            }
            return __exception;
        }

        public static void BeforeComplete(NativeKitchenCheckpoint.RestorePlan __instance)
        {
            var module=active;if(module==null||module.pendingHistory==null)return;
            if(__instance==null||!ReferenceEquals(module.planSnapshot.GetValue(__instance),module.pendingHistory.CoreSnapshot))
                throw new InvalidOperationException("Completing native restore plan differs from the delivery-fade history sidecar.");
            if(module.pending==null)return;
            module.completeInProgress=!module.pending.Applied;
        }

        public static void BeforeRestoreFixedBodyPoses(NativeKitchenCheckpoint.RestorePlan __instance)
        {
            var module=active;
            if(module==null||module.pendingHistory==null||module.pending==null
                ||!module.pending.ActiveTargetComposite||module.pending.Applied)return;
            if(__instance==null||!ReferenceEquals(module.planSnapshot.GetValue(__instance),module.pendingHistory.CoreSnapshot)
                ||!ReferenceEquals(module.pending.CoreSnapshot,module.pendingHistory.CoreSnapshot))
                throw new InvalidOperationException("Early active-delivery restore plan differs from the delivery-fade sidecar.");
            // The f444 target's recreated initial plate is detached on its own
            // registered body. Advance the authenticated factory replacement
            // to the saved PC2 fade topology before the core certifies that
            // body as colliderless and rebinds its historical checkpoint row.
            module.ApplyRestore(module.pending);
        }

        public static void BeforeCheckpointCapture()
        {
            var module=active;
            if(module==null||!module.completeInProgress||module.pending==null)return;
            if(module.pending.Applied){module.completeInProgress=false;return;}
            module.ApplyRestore(module.pending);
            module.completeInProgress=false;
        }

        public static void AfterComplete(NativeKitchenCheckpoint.RestorePlan __instance)
        {
            var module=active;if(module==null||module.pendingHistory==null)return;
            try
            {
                if(__instance==null||!ReferenceEquals(module.planSnapshot.GetValue(__instance),module.pendingHistory.CoreSnapshot))
                    throw new InvalidOperationException("Completed native restore plan differs from the delivery-fade history sidecar.");
                if(module.pending!=null)
                {
                    if(module.completeInProgress)
                        throw new InvalidOperationException("Native checkpoint completion did not reach its delivery lifecycle verification capture.");
                    module.VerifyRestored(module.pending);
                    if(module.resumePresentationTarget!=null||module.resumeActiveSequence!=null||module.resumeActiveReceipt!=null)
                        throw new InvalidOperationException("A prior delivery presentation restore is still pending at the resume boundary.");
                    module.resumePresentationTarget=module.pending.TargetPlate;
                    module.resumeActiveSequence=module.pending.ActiveTargetComposite?module.pending.ReboundActive:null;
                    module.resumeActiveReceipt=module.pending.ActiveTargetComposite
                        ?module.pending.Receipt as Dictionary<string,object>:null;
                    module.restores++;
                }
                module.PruneAfter(module.pendingHistory.Frame);
                module.historyRestores++;module.failure=null;
            }
            catch(Exception error) { module.failure=error.ToString();throw; }
            finally { module.completeInProgress=false;module.pending=null;module.pendingHistory=null; }
        }

        public static Exception FinalizeComplete(Exception __exception)
        {
            var module=active;
            if(module!=null)
            {
                module.completeInProgress=false;
                if(__exception!=null)
                {
                    module.failure=__exception.ToString();module.pending=null;
                    module.pendingHistory=null;
                    module.resumePresentationTarget=null;module.resumePresentationCurrent=null;
                    module.resumeActiveSequence=null;module.resumeActiveReceipt=null;
                }
            }
            return __exception;
        }

        public static void AfterRestoreFailure()
        {
            var module=active;if(module==null)return;
            module.RestoreTargetDeliveryFadeMask();module.completeInProgress=false;module.preparing=null;module.pending=null;
            module.preparingHistory=null;module.pendingHistory=null;
            module.resumePresentationTarget=null;module.resumePresentationCurrent=null;
            module.resumeActiveSequence=null;module.resumeActiveReceipt=null;
            module.RestoreAdmissionObserver();
        }

        public static void BeforeAuthoringResume()
        {
            var module=active;if(module==null||module.resumePresentationTarget==null)return;
            try
            {
                var target=module.resumePresentationTarget;
                var current=module.CaptureCurrentPresentation(target);
                module.RestorePlatePresentation(target,current,true);
                module.VerifyPlate(target,current);
                RebindLiveExternalUi(target,module.ValidateLiveExternalUi(target,target.Object));
                module.VerifyExternalUi(target.ExternalUi);
                module.resumePresentationCurrent=current;
                module.lastResumePresentationRestore=Map("frame",Hpmv.Injector.Server.CurrentFrameData.FrameNumber,"entityId",target.EntityId,
                    "targetContainerId",target.PresentationContainerId,
                    "currentContainerId",current.PresentationContainerId,
                    "rendererIds",current.PresentationRenderers.Select(value=>value.InstanceId).ToArray(),
                    "verifiedBeforeResume",true,"verifiedAfterResume",false,
                    "scope","Exact target materials and bounded local scale restored onto a proven ClientAttachedOrderCosmeticDecisions presentation reincarnation; no physics component is mutable.");
                module.failure=null;
            }
            catch(Exception error){module.failure=error.ToString();throw;}
        }

        public static void AfterAuthoringResume()
        {
            var module=active;if(module==null||module.resumePresentationTarget==null)return;
            try
            {
                if(module.resumePresentationCurrent==null)
                    throw new InvalidOperationException("Delivery presentation resume prefix did not produce a verified mapping.");
                module.VerifyPlate(module.resumePresentationTarget,module.resumePresentationCurrent);
                RebindLiveExternalUi(module.resumePresentationTarget,
                    module.ValidateLiveExternalUi(module.resumePresentationTarget,module.resumePresentationTarget.Object));
                module.VerifyExternalUi(module.resumePresentationTarget.ExternalUi);
                var receipt=module.lastResumePresentationRestore as Dictionary<string,object>;
                if(receipt!=null)receipt["verifiedAfterResume"]=true;
                if(module.resumeActiveSequence!=null)
                {
                    var sequence=module.resumeActiveSequence;
                    if(sequence.SchedulerIterator!=null||sequence.SchedulerCoroutine!=null)
                        throw new InvalidOperationException("Restored delivery iterator is already scheduled.");
                    var relay=new DeliveryFadeResumeRelay(sequence.Iterator);
                    var coroutine=sequence.Station.StartCoroutine(relay);
                    if(!relay.Primed||coroutine==null||IteratorPc(sequence)!=2
                        ||IteratorProgress(sequence)!=.3f||Field(sequence.Iterator.GetType(),"$current").GetValue(sequence.Iterator)!=null)
                    {
                        sequence.Station.StopCoroutine(relay);
                        throw new InvalidOperationException("Restored delivery relay did not prime without advancing the PC2 iterator.");
                    }
                    sequence.SchedulerIterator=relay;sequence.SchedulerCoroutine=coroutine;
                    if(receipt!=null)receipt["activeIteratorRelayScheduled"]=true;
                    if(module.resumeActiveReceipt!=null)
                    {
                        module.resumeActiveReceipt["activeIteratorRelayScheduled"]=true;
                        module.resumeActiveReceipt["iteratorPcAtSchedule"]=IteratorPc(sequence);
                        module.resumeActiveReceipt["iteratorProgressAtSchedule"]=IteratorProgress(sequence);
                    }
                }
                module.resumePresentationTarget=null;module.resumePresentationCurrent=null;
                module.resumeActiveSequence=null;module.resumeActiveReceipt=null;module.failure=null;
            }
            catch(Exception error){module.failure=error.ToString();throw;}
        }

        private void ObserveFactory(ClientPlateStation station,ClientPlate plate,PlateStation.DeliveryFX effects,IEnumerator iterator)
        {
            if(station==null||plate==null||effects==null||iterator==null)
                throw new InvalidOperationException("DeliverySequence returned an incomplete iterator identity.");
            Type type=iterator.GetType();
            RequireIteratorLayout(type);
            if(sequences.Any(value=>ReferenceEquals(value.Iterator,iterator)))
                throw new InvalidOperationException("DeliverySequence returned a duplicate iterator instance.");
            var renderers=ComponentsRecursive<MeshRenderer>(plate.transform);
            var owner=plate.GetComponent<ClientAttachedOrderCosmeticDecisions>();
            var container=GetPresentationContainer(owner);
            var entry=EntitySerialisationRegistry.GetEntry(plate.gameObject);
            int entityId=entry==null?0:(int)entry.m_Header.m_uEntityID;
            sequences.Add(new Sequence { Station=station,Plate=plate,Effects=effects,Iterator=iterator,
                PlateObject=plate.gameObject,StationId=station.GetInstanceID(),PlateId=plate.GetInstanceID(),
                PlateObjectId=plate.gameObject.GetInstanceID(),EntityId=entityId,IteratorType=type.FullName,
                FactoryPreimage=ObservePlate(plate.gameObject),
                PresentationOwner=owner,PresentationOwnerId=owner==null?0:owner.GetInstanceID(),
                PresentationContainer=container,PresentationContainerId=Alive(container)?container.GetInstanceID():0,
                PresentationContainerKey=Alive(container)?TransformHierarchyKey(plate.transform,container.transform):null,
                PresentationContainerPhysicsFree=Alive(container)&&PhysicsFreePresentation(container),
                PresentationRenderers=renderers.Select(value=>CaptureRenderer(value,plate.transform,container)).ToArray() });
            factories++;
        }

        private void Capture(int frame)
        {
            object round=coreRoundIdentity.GetValue(null);
            if(!ReferenceEquals(round,observedRoundIdentity)||frame<lastFrame)
            {
                history.Clear();sequences.Clear();observedRoundIdentity=round;lastFrame=-1;resets++;
                resumePresentationTarget=null;resumePresentationCurrent=null;
                suppressedNaturalPresentationStarts.RemoveAll(value=>!Alive(value.Component));
                if(suppressedNaturalPresentationStarts.Count!=0)
                    throw new InvalidOperationException("A live forced presentation Start still awaits its exact Unity duplicate suppression.");
            }
            object core=CoreSnapshot(frame);
            if(core==null)throw new InvalidOperationException("Native checkpoint is absent for delivery fade frame "+frame+".");
            FrameState previous;
            if(history.TryGetValue(frame,out previous)&&ReferenceEquals(previous.CoreSnapshot,core))
            {
                duplicates++;return;
            }
            var plates=CapturePlates();
            var state=new FrameState { Frame=frame,CoreSnapshot=core,PendingFades=NativePlateLifecycle.PendingDeliveryFades,
                Sequences=sequences.Select(ObserveSequence).ToArray(),SequenceStates=sequences.Select(CaptureSequenceState).ToArray(),Plates=plates };
            history[frame]=state;lastFrame=Math.Max(lastFrame,frame);captures++;
        }

        private SequenceFrameState CaptureSequenceState(Sequence sequence)
        {
            Type type=sequence.Iterator.GetType();RequireIteratorLayout(type);
            object current=Field(type,"$current").GetValue(sequence.Iterator);
            var pfx=Field(type,"<pfx>__1").GetValue(sequence.Iterator) as GameObject;
            return new SequenceFrameState {
                Source=sequence,EntityId=sequence.EntityId,
                Pc=Convert.ToInt32(Field(type,"$PC").GetValue(sequence.Iterator)),
                Disposing=Convert.ToBoolean(Field(type,"$disposing").GetValue(sequence.Iterator)),
                Errored=Convert.ToBoolean(Field(type,"<errored>__0").GetValue(sequence.Iterator)),
                CurrentIsNull=current==null,
                CurrentIsStationWait=Alive(sequence.Station)&&ReferenceEquals(current,StationWait(sequence.Station)),
                Progress=Convert.ToSingle(Field(type,"<progress>__0").GetValue(sequence.Iterator)),
                Colliders=Field(type,"<colliders>__0").GetValue(sequence.Iterator) as Collider[],
                Body=Field(type,"<rigidBody>__0").GetValue(sequence.Iterator) as Rigidbody,
                Renderers=Field(type,"<allRenderers>__0").GetValue(sequence.Iterator) as MeshRenderer[],
                Pfx=pfx,PfxAlive=Alive(pfx),PfxDetached=Alive(pfx)&&pfx.transform.parent==null
            };
        }

        private PlateState[] CapturePlates()
        {
            var result=new List<PlateState>();var entries=EntitySerialisationRegistry.m_EntitiesList;
            for(int i=0;i<entries.Count;i++)
            {
                var entry=entries._items[i];if(entry==null||entry.m_GameObject==null)continue;
                var plate=entry.m_GameObject.GetComponent<ClientPlate>();if(plate==null)continue;
                result.Add(CapturePlate(entry,plate));
            }
            return result.OrderBy(value=>value.EntityId).ToArray();
        }

        private PlateState CapturePlate(EntitySerialisationEntry entry,ClientPlate plate)
        {
            var obj=plate.gameObject;var colliders=ComponentsRecursive<Collider>(obj.transform);
            var renderers=ComponentsRecursive<MeshRenderer>(obj.transform);
            var owner=plate.GetComponent<ClientAttachedOrderCosmeticDecisions>();
            var container=GetPresentationContainer(owner);
            return new PlateState { EntityId=(int)entry.m_Header.m_uEntityID,ObjectId=obj.GetInstanceID(),ComponentId=plate.GetInstanceID(),
                Name=obj.name,Layer=obj.layer,ActiveSelf=obj.activeSelf,ActiveInHierarchy=obj.activeInHierarchy,
                Object=obj,Plate=plate,Parent=obj.transform.parent,ParentId=obj.transform.parent==null?0:obj.transform.parent.GetInstanceID(),
                LocalPosition=obj.transform.localPosition,LocalRotation=obj.transform.localRotation,LocalScale=obj.transform.localScale,
                PresentationOwner=owner,PresentationOwnerId=owner==null?0:owner.GetInstanceID(),
                PresentationContainer=container,PresentationContainerId=Alive(container)?container.GetInstanceID():0,
                PresentationContainerKey=Alive(container)?TransformHierarchyKey(obj.transform,container.transform):null,
                PresentationCompositionFingerprint=PresentationCompositionFingerprint(container),
                PresentationContainerPhysicsFree=Alive(container)&&PhysicsFreePresentation(container),
                ComponentTypes=ComponentTypeNames(obj),
                ServerSynchroniserTypes=entry.m_ServerSynchronisedComponents._items.Take(entry.m_ServerSynchronisedComponents.Count)
                    .Select(value=>value.GetType().FullName).ToArray(),
                ClientSynchroniserTypes=entry.m_ClientSynchronisedComponents._items.Take(entry.m_ClientSynchronisedComponents.Count)
                    .Select(value=>value.GetType().FullName).ToArray(),
                ExternalUi=CaptureExternalUi(obj),
                Colliders=colliders.Select(value=>CaptureCollider(value,obj.transform)).ToArray(),
                Renderers=renderers.Select(value=>CaptureRenderer(value,obj.transform,container)).ToArray() };
        }

        private static ColliderState CaptureCollider(Collider value,Transform root)
        {
            return new ColliderState { Collider=value,InstanceId=value.GetInstanceID(),HierarchyKey=ColliderHierarchyKey(root,value),
                TypeName=value.GetType().FullName,Enabled=value.enabled,Trigger=value.isTrigger,
                ActiveSelf=value.gameObject.activeSelf,ActiveInHierarchy=value.gameObject.activeInHierarchy };
        }

        private static RendererState CaptureRenderer(MeshRenderer value,Transform root,GameObject presentationContainer)
        {
            var materials=value.sharedMaterials;
            bool inPresentation=Alive(presentationContainer)&&IsDescendant(value.transform,presentationContainer);
            var meshFilter=value.GetComponent<MeshFilter>();
            return new RendererState { Renderer=value,InstanceId=value.GetInstanceID(),HierarchyKey=RendererHierarchyKey(root,value),
                InPresentationContainer=inPresentation,
                PresentationKey=inPresentation?RendererHierarchyKey(presentationContainer.transform,value):null,
                SharedMeshId=meshFilter==null||meshFilter.sharedMesh==null?0:meshFilter.sharedMesh.GetInstanceID(),
                LocalPosition=value.transform.localPosition,LocalRotation=value.transform.localRotation,LocalScale=value.transform.localScale,
                ActiveSelf=value.gameObject.activeSelf,ActiveInHierarchy=value.gameObject.activeInHierarchy,Layer=value.gameObject.layer,
                Enabled=value.enabled,Materials=materials,
                MaterialIds=materials.Select(m=>m==null?0:m.GetInstanceID()).ToArray(),
                ShaderIds=materials.Select(m=>m==null||m.shader==null?0:m.shader.GetInstanceID()).ToArray(),
                HasMode=materials.Select(m=>m!=null&&m.HasProperty("_Mode")).ToArray(),
                Mode=materials.Select(m=>m!=null&&m.HasProperty("_Mode")?m.GetFloat("_Mode"):Single.NaN).ToArray(),
                HasAlpha=materials.Select(m=>m!=null&&m.HasProperty("_Alpha")).ToArray(),
                Alpha=materials.Select(m=>m!=null&&m.HasProperty("_Alpha")?m.GetFloat("_Alpha"):Single.NaN).ToArray() };
        }

        private object ObserveSequence(Sequence sequence)
        {
            Type type=sequence.Iterator.GetType();RequireIteratorLayout(type);
            var stationObject=Alive(sequence.Station)?sequence.Station.gameObject:null;
            var plateObject=Alive(sequence.Plate)?sequence.Plate.gameObject:null;
            object iteratorPlate=Field(type,"plate").GetValue(sequence.Iterator);
            object iteratorStation=Field(type,"$this").GetValue(sequence.Iterator);
            object current=Field(type,"$current").GetValue(sequence.Iterator);
            object pfxValue=Field(type,"<pfx>__1").GetValue(sequence.Iterator);
            var pfx=pfxValue as GameObject;
            object renderersValue=Field(type,"<allRenderers>__0").GetValue(sequence.Iterator);
            var iteratorRenderers=renderersValue as MeshRenderer[];
            var colliders=plateObject==null?new Collider[0]:ComponentsRecursive<Collider>(plateObject.transform);
            var renderers=plateObject==null?new MeshRenderer[0]:ComponentsRecursive<MeshRenderer>(plateObject.transform);
            var body=plateObject==null?null:plateObject.GetComponent<Rigidbody>();
            int entityId=0;var entry=plateObject==null?null:EntitySerialisationRegistry.GetEntry(plateObject);
            if(entry!=null)entityId=(int)entry.m_Header.m_uEntityID;
            return Map(
                "stationInstanceId",sequence.StationId,"stationAlive",Alive(sequence.Station),"stationPath",stationObject==null?null:PathOf(stationObject.transform),
                "plateComponentInstanceId",sequence.PlateId,"plateAlive",Alive(sequence.Plate),"plateObjectInstanceId",plateObject==null?0:plateObject.GetInstanceID(),
                "plateEntityId",entityId,"platePath",plateObject==null?null:PathOf(plateObject.transform),
                "plateParentInstanceId",plateObject==null||plateObject.transform.parent==null?0:plateObject.transform.parent.GetInstanceID(),
                "plateLocalPosition",plateObject==null?null:(object)Point(plateObject.transform.localPosition),
                "plateLocalRotation",plateObject==null?null:(object)Rotation(plateObject.transform.localRotation),
                "plateLocalScale",plateObject==null?null:(object)Point(plateObject.transform.localScale),
                "iteratorType",sequence.IteratorType,"iteratorPlateExact",ReferenceEquals(iteratorPlate,sequence.Plate),
                "iteratorStationExact",ReferenceEquals(iteratorStation,sequence.Station),
                "effects",Map("pfxPrefab",ObjectIdentity(sequence.Effects.m_deliverPFXPrefab),
                    "pfxDelay",sequence.Effects.m_pfxToFadeDelayTime,"fadeShader",ObjectIdentity(sequence.Effects.m_fadeOutShader),
                    "fadeTime",sequence.Effects.m_fadeTime),"factoryPreimage",sequence.FactoryPreimage,
                "pc",Convert.ToInt32(Field(type,"$PC").GetValue(sequence.Iterator)),
                "disposing",Convert.ToBoolean(Field(type,"$disposing").GetValue(sequence.Iterator)),
                "current",ObjectIdentity(current),"currentIsStationWait",Alive(sequence.Station)&&ReferenceEquals(current,StationWait(sequence.Station)),
                "progress",Convert.ToSingle(Field(type,"<progress>__0").GetValue(sequence.Iterator)),
                "pfx",ObjectIdentity(pfx),"pfxParentInstanceId",Alive(pfx)&&pfx.transform.parent!=null?pfx.transform.parent.GetInstanceID():0,
                "iteratorRendererArrayNull",renderersValue==null,"iteratorRendererCount",iteratorRenderers==null?-1:iteratorRenderers.Length,
                "iteratorRendererIds",iteratorRenderers==null?null:(object)iteratorRenderers.Select(value=>value==null?0:value.GetInstanceID()).ToArray(),
                "presentationOwnerId",sequence.PresentationOwnerId,
                "presentationContainerId",sequence.PresentationContainerId,
                "presentationContainerAlive",Alive(sequence.PresentationContainer),
                "presentationContainerKey",sequence.PresentationContainerKey,
                "presentationContainerPhysicsFree",sequence.PresentationContainerPhysicsFree,
                "presentationRenderers",sequence.PresentationRenderers==null?null:(object)sequence.PresentationRenderers.Select(EncodeRendererState).ToArray(),
                "colliders",colliders.Select(EncodeCollider).ToArray(),"body",EncodeBody(body),
                "renderers",renderers.Select(EncodeRenderer).ToArray());
        }

        private object ObservePlate(GameObject plateObject)
        {
            var colliders=ComponentsRecursive<Collider>(plateObject.transform);
            var renderers=ComponentsRecursive<MeshRenderer>(plateObject.transform);
            var body=plateObject.GetComponent<Rigidbody>();
            int entityId=0;var entry=EntitySerialisationRegistry.GetEntry(plateObject);
            if(entry!=null)entityId=(int)entry.m_Header.m_uEntityID;
            return Map("plateObjectInstanceId",plateObject.GetInstanceID(),"plateEntityId",entityId,"path",PathOf(plateObject.transform),
                "parentInstanceId",plateObject.transform.parent==null?0:plateObject.transform.parent.GetInstanceID(),
                "localPosition",Point(plateObject.transform.localPosition),"localRotation",Rotation(plateObject.transform.localRotation),
                "localScale",Point(plateObject.transform.localScale),"colliders",colliders.Select(EncodeCollider).ToArray(),
                "body",EncodeBody(body),"renderers",renderers.Select(EncodeRenderer).ToArray());
        }

        private object EncodeCollider(Collider value)
        {
            return Map("instanceId",value.GetInstanceID(),"type",value.GetType().FullName,"path",PathOf(value.transform),
                "enabled",value.enabled,"trigger",value.isTrigger,"attachedBodyInstanceId",value.attachedRigidbody==null?0:value.attachedRigidbody.GetInstanceID(),
                "activeSelf",value.gameObject.activeSelf,"activeInHierarchy",value.gameObject.activeInHierarchy);
        }

        private object EncodeBody(Rigidbody value)
        {
            if(value==null)return null;
            return Map("instanceId",value.GetInstanceID(),"kinematic",value.isKinematic,"detectCollisions",value.detectCollisions,
                "useGravity",value.useGravity,"sleeping",value.IsSleeping(),"position",Point(value.position),"rotation",Rotation(value.rotation),
                "velocity",Point(value.velocity),"angularVelocity",Point(value.angularVelocity));
        }

        private object EncodeRenderer(MeshRenderer value)
        {
            var materials=value.sharedMaterials;
            return Map("instanceId",value.GetInstanceID(),"path",PathOf(value.transform),"enabled",value.enabled,
                "materialInstanceIds",materials.Select(m=>m==null?0:m.GetInstanceID()).ToArray(),
                "shaderInstanceIds",materials.Select(m=>m==null||m.shader==null?0:m.shader.GetInstanceID()).ToArray(),
                "hasMode",materials.Select(m=>m!=null&&m.HasProperty("_Mode")).ToArray(),
                "mode",materials.Select(m=>m!=null&&m.HasProperty("_Mode")?m.GetFloat("_Mode"):Single.NaN).ToArray(),
                "hasAlpha",materials.Select(m=>m!=null&&m.HasProperty("_Alpha")).ToArray(),
                "alpha",materials.Select(m=>m!=null&&m.HasProperty("_Alpha")?m.GetFloat("_Alpha"):Single.NaN).ToArray());
        }

        private object EncodeRendererState(RendererState value)
        {
            return Map("instanceId",value.InstanceId,"alive",Alive(value.Renderer),"hierarchyKey",value.HierarchyKey,
                "inPresentationContainer",value.InPresentationContainer,"presentationKey",value.PresentationKey,
                "sharedMeshId",value.SharedMeshId,"localPosition",Point(value.LocalPosition),
                "localRotation",Rotation(value.LocalRotation),"localScale",Point(value.LocalScale),
                "activeSelf",value.ActiveSelf,"activeInHierarchy",value.ActiveInHierarchy,"layer",value.Layer,
                "enabled",value.Enabled,"materialInstanceIds",value.MaterialIds,"shaderInstanceIds",value.ShaderIds,
                "hasMode",value.HasMode,"mode",value.Mode,"hasAlpha",value.HasAlpha,"alpha",value.Alpha);
        }

        private bool TargetPlatesRemainExact(FrameState target)
        {
            if(target.Plates==null)return false;
            foreach(var plate in target.Plates)
            {
                var entry=EntitySerialisationRegistry.GetEntry((uint)plate.EntityId);
                if(entry==null||!Alive(plate.Object)||!Alive(plate.Plate)
                    ||!ReferenceEquals(entry.m_GameObject,plate.Object)||!ReferenceEquals(plate.Plate.gameObject,plate.Object)
                    ||plate.Object.GetInstanceID()!=plate.ObjectId||plate.Plate.GetInstanceID()!=plate.ComponentId)return false;
            }
            return true;
        }

        private RestoreCandidate PrepareDestroyedTarget(Hpmv.WarpSpec warp,FrameState target)
        {
            if(NativePlateLifecycle.PendingDeliveryFades!=0||DeliveredCount()!=0)
                throw new InvalidOperationException("Destroyed delivery restore requires an empty terminal delivery observer.");
            if(NativePlateLifecycle.LastObservationError.Length!=0)
                throw new InvalidOperationException("Native plate lifecycle observation is incomplete: "+NativePlateLifecycle.LastObservationError);
            if(warp.Entities==null||!DeliveryFadeReincarnationContract.IsDisjointFutureDeletionSet(
                warp.EntitiesToDelete,target.Plates==null?null:target.Plates.Select(value=>value.EntityId).ToArray()))
                throw new InvalidOperationException("Destroyed delivery restore requires distinct positive future-only deletions disjoint from every target plate.");
            var discardedReturnEntityIds=warp.EntitiesToDelete.OrderBy(value=>value).ToArray();
            var spawned=warp.Entities.Where(value=>value!=null&&!value.__isset.entityId
                &&value.SpawningPath!=null&&value.SpawningPath.Count!=0).ToArray();
            int plateSpawn=DeliveryFadeReincarnationContract.ExactStory11Entity2PlateSpawnIndex(
                spawned.Select(value=>new DeliveryFadeSpawnShape {
                    SpawningPath=value.SpawningPath,
                    LogicalPath=value.__isset.entityPathReference&&value.EntityPathReference!=null
                        ?value.EntityPathReference.Ids:null,
                    HasIngredientContainer=value.IngredientContainer!=null }).ToArray());
            if(plateSpawn<0)
                throw new InvalidOperationException("Destroyed delivery restore requires exactly one unambiguous Story 1-1 plate factory [34,0,0], path [2], and contents data; unrelated core spawns are allowed.");
            int entityId=2;
            var targets=target.Plates.Where(value=>value.EntityId==entityId).ToArray();
            if(targets.Length!=1)throw new InvalidOperationException("Destroyed delivery target does not contain exact historical plate entity 2.");
            var targetPlate=targets[0];
            if(!DeliveryFadeReincarnationContract.HasManagedReference(targetPlate.Object)
                ||!DeliveryFadeReincarnationContract.HasManagedReference(targetPlate.Plate)
                ||EntitySerialisationRegistry.GetEntry((uint)entityId)!=null||Alive(targetPlate.Object)||Alive(targetPlate.Plate)
                ||targetPlate.ObjectId==0||targetPlate.ComponentId==0)
                throw new InvalidOperationException("Historical delivered plate is not in the required destroyed-current state.");
            if(!DeliveryFadeReincarnationContract.HasManagedReference(targetPlate.Parent)||targetPlate.ParentId==0
                ||Alive(targetPlate.Parent)&&targetPlate.Parent.GetInstanceID()!=targetPlate.ParentId)
                throw new InvalidOperationException("Historical delivered plate attachment parent identity is absent or stale.");
            foreach(var other in target.Plates.Where(value=>!ReferenceEquals(value,targetPlate)))
                ValidatePlateIncarnation(other,other.Plate);
            if(targetPlate.ComponentTypes==null||targetPlate.ComponentTypes.Count(value=>value==typeof(ClientPlate).FullName)!=1
                ||targetPlate.ComponentTypes.Count(value=>value==typeof(ClientAttachedOrderCosmeticDecisions).FullName)!=1
                ||targetPlate.ComponentTypes.Count(value=>value==typeof(ClientIngredientContentGUI).FullName)!=1
                ||targetPlate.ComponentTypes.Count(value=>value==typeof(PhysicalAttachment).FullName)!=1
                ||targetPlate.ComponentTypes.Count(value=>value==DeliveryFadeReincarnationContract.PathMarkerType)>1)
                throw new InvalidOperationException("Historical delivered plate component proof is incomplete.");
            ValidateDestroyedExternalUiPreimage(targetPlate.ExternalUi);
            var matches=sequences.Where(value=>ReferenceEquals(value.Plate,targetPlate.Plate)
                &&ReferenceEquals(value.PlateObject,targetPlate.Object)&&value.PlateId==targetPlate.ComponentId
                &&value.PlateObjectId==targetPlate.ObjectId).ToArray();
            if(matches.Length!=1)throw new InvalidOperationException("Exactly one captured terminal delivery iterator must own the destroyed plate.");
            var sequence=matches[0];
            ValidateTerminalSequence(sequence,targetPlate);
            ValidateStory11Station(sequence);
            var pfx=IteratorPfx(sequence);
            ValidateOwnedMaterialIsolation(sequence,targetPlate,pfx);
            return new RestoreCandidate { Frame=warp.Frame,CoreSnapshot=target.CoreSnapshot,Target=target,
                TargetPlate=targetPlate,Current=sequence,Pfx=pfx,DestroyedRoot=true,
                DiscardedReturnEntityIds=discardedReturnEntityIds };
        }

        private RestoreCandidate PrepareActiveDestroyedTarget(Hpmv.WarpSpec warp,FrameState target)
        {
            if(NativePlateLifecycle.PendingDeliveryFades!=1||DeliveredCount()!=1||target.PendingFades!=1)
                throw new InvalidOperationException("Active destroyed-delivery restore requires one current and one target fade.");
            if(NativePlateLifecycle.LastObservationError.Length!=0)
                throw new InvalidOperationException("Native plate lifecycle observation is incomplete: "+NativePlateLifecycle.LastObservationError);
            if(warp.Entities==null||!DeliveryFadeReincarnationContract.IsDisjointFutureDeletionSet(
                warp.EntitiesToDelete,target.Plates==null?null:target.Plates.Select(value=>value.EntityId).ToArray()))
                throw new InvalidOperationException("Active destroyed-delivery restore requires distinct future-only deletions.");
            var spawned=warp.Entities.Where(value=>value!=null&&!value.__isset.entityId
                &&value.SpawningPath!=null&&value.SpawningPath.Count!=0).ToArray();
            int plateSpawn=DeliveryFadeReincarnationContract.ExactStory11Entity2PlateSpawnIndex(
                spawned.Select(value=>new DeliveryFadeSpawnShape {
                    SpawningPath=value.SpawningPath,
                    LogicalPath=value.__isset.entityPathReference&&value.EntityPathReference!=null
                        ?value.EntityPathReference.Ids:null,
                    HasIngredientContainer=value.IngredientContainer!=null }).ToArray());
            if(plateSpawn<0)
                throw new InvalidOperationException("Active destroyed-delivery restore requires exactly one unambiguous Story 1-1 plate entity 2 recreation; unrelated core spawns are allowed.");
            var targets=target.Plates.Where(value=>value.EntityId==2).ToArray();
            if(targets.Length!=1)throw new InvalidOperationException("Active delivery target lacks historical plate entity 2.");
            var targetPlate=targets[0];
            if(!DeliveryFadeReincarnationContract.HasManagedReference(targetPlate.Object)
                ||!DeliveryFadeReincarnationContract.HasManagedReference(targetPlate.Plate)
                ||EntitySerialisationRegistry.GetEntry((uint)2)!=null||Alive(targetPlate.Object)||Alive(targetPlate.Plate))
                throw new InvalidOperationException("Active delivery target plate entity 2 is not destroyed in the current world.");
            if(!DeliveryFadeReincarnationContract.HasManagedReference(targetPlate.Parent)||targetPlate.ParentId==0
                ||Alive(targetPlate.Parent)&&targetPlate.Parent.GetInstanceID()!=targetPlate.ParentId)
                throw new InvalidOperationException("Active delivery target attachment parent identity is absent or stale.");
            foreach(var other in target.Plates.Where(value=>!ReferenceEquals(value,targetPlate)))
                ValidatePlateIncarnation(other,other.Plate);

            var targetSequences=(target.SequenceStates??new SequenceFrameState[0]).Where(value=>value.EntityId==2).ToArray();
            if(targetSequences.Length!=1)throw new InvalidOperationException("Active delivery target lacks one entity 2 iterator snapshot.");
            var targetSequence=targetSequences[0];var source=targetSequence.Source;
            if(source==null||source.EntityId!=2)throw new InvalidOperationException("Active delivery target iterator lineage is absent.");
            ValidateTerminalSequenceForActiveTarget(source,targetPlate);ValidateStory11Station(source);
            ValidateExactActiveTarget(targetSequence,targetPlate);
            ValidatePfxGameplayFree(source.Effects.m_deliverPFXPrefab);

            var map=(IDictionary)delivered.GetValue(null);
            var current=sequences.Where(value=>Alive(value.Station)&&Alive(value.Plate)
                &&value.EntityId==1&&map.Contains(value.Plate.gameObject)).ToArray();
            if(current.Length!=1)throw new InvalidOperationException("Exactly one current entity 1 fade must be canceled.");
            var cancel=current[0];ValidateLiveSequenceSelf(cancel);ValidateStory11Station(cancel);
            int instance=Convert.ToInt32(map[cancel.Plate.gameObject]);
            if(instance!=cancel.Plate.gameObject.GetInstanceID())
                throw new InvalidOperationException("Current delivery observer incarnation differs.");
            var cancelTargets=target.Plates.Where(value=>value.EntityId==1).ToArray();
            if(cancelTargets.Length!=1)throw new InvalidOperationException("Active delivery target lacks inactive plate entity 1.");
            ValidatePlateIncarnation(cancelTargets[0],cancel.Plate);
            if(ReferenceEquals(cancel,source)||cancel.EntityId==source.EntityId)
                throw new InvalidOperationException("Canceled and recreated delivery lineages overlap.");
            return new RestoreCandidate { Frame=warp.Frame,CoreSnapshot=target.CoreSnapshot,Target=target,
                TargetPlate=targetPlate,Current=source,Pfx=IteratorPfx(source),DestroyedRoot=true,
                ActiveTargetComposite=true,CancelCurrent=cancel,CancelTargetPlate=cancelTargets[0],
                TargetSequence=targetSequence,CancelPfx=IteratorPfx(cancel),
                DiscardedReturnEntityIds=warp.EntitiesToDelete.OrderBy(value=>value).ToArray() };
        }

        private void ValidateExactActiveTarget(SequenceFrameState state,PlateState plate)
        {
            if(state==null||state.Source==null||plate==null||plate.Colliders==null||plate.Renderers==null
                ||state.Colliders==null||state.Renderers==null
                ||!DeliveryFadeReincarnationContract.IsExactStory11ActiveFadeState(
                    state.EntityId,state.Pc,state.Disposing,state.Errored,state.CurrentIsNull,state.CurrentIsStationWait,
                    state.Progress,state.PfxAlive,state.PfxDetached,state.Source.Effects.m_pfxToFadeDelayTime,
                    state.Source.Effects.m_fadeTime,plate.Colliders.Select(value=>value.Enabled).ToArray(),
                    plate.Renderers.SelectMany(value=>value.HasAlpha.Select((has,index)=>has?value.Alpha[index]:Single.NaN)).ToArray()))
                throw new InvalidOperationException("Target is not the exact proven Story 1-1 entity 2 PC2 fade state.");
            if(state.Body!=null||state.Colliders.Length!=plate.Colliders.Length||state.Renderers.Length!=plate.Renderers.Length
                ||!ExactReferenceMembership(state.Colliders,plate.Colliders.Select(value=>value.Collider).ToArray())
                ||!ExactReferenceMembership(state.Renderers,plate.Renderers.Select(value=>value.Renderer).ToArray())
                ||!ReferenceEquals(state.Pfx,IteratorPfx(state.Source))
                ||state.Source.Effects.m_fadeOutShader==null)
                throw new InvalidOperationException("Target active delivery iterator membership differs.");
            foreach(var renderer in plate.Renderers)
                for(int i=0;i<renderer.Materials.Length;i++)
                    if(renderer.Materials[i]==null||!ReferenceEquals(renderer.Materials[i].shader,state.Source.Effects.m_fadeOutShader)
                        ||!state.Source.OwnedMaterials.Contains(renderer.Materials[i]))
                        throw new InvalidOperationException("Target active delivery material is not an owned fade material.");
        }

        private void ValidateLiveSequenceSelf(Sequence sequence)
        {
            Type type=sequence.Iterator.GetType();RequireIteratorLayout(type);
            int pc=IteratorPc(sequence);float progress=IteratorProgress(sequence);
            var obj=sequence.Plate.gameObject;
            var colliders=ComponentsRecursive<Collider>(obj.transform);
            var iteratorColliders=Field(type,"<colliders>__0").GetValue(sequence.Iterator) as Collider[];
            var renderers=ComponentsRecursive<MeshRenderer>(obj.transform);
            var iteratorRenderers=Field(type,"<allRenderers>__0").GetValue(sequence.Iterator) as MeshRenderer[];
            if(pc!=2||!Finite(progress)||progress<0f||progress>=1f
                ||Convert.ToBoolean(Field(type,"$disposing").GetValue(sequence.Iterator))
                ||Convert.ToBoolean(Field(type,"<errored>__0").GetValue(sequence.Iterator))
                ||Field(type,"$current").GetValue(sequence.Iterator)!=null
                ||Field(type,"<rigidBody>__0").GetValue(sequence.Iterator)!=null
                ||!sequence.MaterialOwnershipComplete||!Alive(IteratorPfx(sequence))
                ||iteratorColliders==null||iteratorRenderers==null
                ||!ExactReferenceMembership(iteratorColliders,colliders)
                ||!ExactReferenceMembership(iteratorRenderers,renderers)
                ||colliders.Any(value=>value.enabled)||renderers.Length!=sequence.PresentationRenderers.Length)
                throw new InvalidOperationException("Current entity 1 delivery is not one exact live PC2 fade.");
            foreach(var renderer in renderers)
                foreach(var material in renderer.sharedMaterials)
                    if(material==null||!ReferenceEquals(material.shader,sequence.Effects.m_fadeOutShader)
                        ||!sequence.OwnedMaterials.Contains(material))
                        throw new InvalidOperationException("Current entity 1 fade material ownership differs.");
        }

        private void ValidateTerminalSequence(Sequence sequence,PlateState target)
        {
            Type type=sequence.Iterator.GetType();RequireIteratorLayout(type);
            if(!Alive(sequence.Station)||sequence.Station.GetInstanceID()!=sequence.StationId
                ||Alive(sequence.Plate)||Alive(sequence.PlateObject)
                ||!ReferenceEquals(Field(type,"plate").GetValue(sequence.Iterator),target.Plate)
                ||!ReferenceEquals(Field(type,"$this").GetValue(sequence.Iterator),sequence.Station)
                ||!ReferenceEquals(Field(type,"deliveryFx").GetValue(sequence.Iterator),sequence.Effects)
                ||Convert.ToBoolean(Field(type,"$disposing").GetValue(sequence.Iterator))
                ||Convert.ToBoolean(Field(type,"<errored>__0").GetValue(sequence.Iterator))
                ||Convert.ToInt32(Field(type,"$PC").GetValue(sequence.Iterator))!=-1
                ||Field(type,"$current").GetValue(sequence.Iterator)!=null
                ||!DeliveryFadeReincarnationContract.IsTerminalFadeProgress(
                    Convert.ToSingle(Field(type,"<progress>__0").GetValue(sequence.Iterator))))
                throw new InvalidOperationException("Delivery iterator is not the exact successful terminal sequence for the destroyed plate.");
            if(Field(type,"<rigidBody>__0").GetValue(sequence.Iterator)!=null||!sequence.MaterialOwnershipComplete
                ||sequence.MaterialsBeforeMoveNext!=null)
                throw new InvalidOperationException("Terminal delivery iterator body/material ownership proof is incomplete.");
            var iteratorColliders=Field(type,"<colliders>__0").GetValue(sequence.Iterator) as Collider[];
            var iteratorRenderers=Field(type,"<allRenderers>__0").GetValue(sequence.Iterator) as MeshRenderer[];
            if(sequence.PresentationRenderers==null||sequence.PresentationRenderers.Length!=target.Renderers.Length
                ||!ExactReferenceMembership(iteratorColliders,target.Colliders.Select(value=>value.Collider).ToArray())
                ||!ExactReferenceMembership(iteratorRenderers,sequence.PresentationRenderers.Select(value=>value.Renderer).ToArray()))
                throw new InvalidOperationException("Terminal delivery iterator does not retain the exact destroyed plate collider/renderer membership.");
            foreach(var collider in target.Colliders)
                if(!DeliveryFadeReincarnationContract.HasManagedReference(collider.Collider)||Alive(collider.Collider))
                    throw new InvalidOperationException("A historical destroyed-plate collider wrapper is absent or unexpectedly live.");
            if(!DeliveryFadeReincarnationContract.HasManagedReference(target.PresentationOwner)||Alive(target.PresentationOwner)
                ||!DeliveryFadeReincarnationContract.HasManagedReference(target.PresentationContainer)
                ||Alive(target.PresentationContainer)||target.PresentationOwnerId==0||target.PresentationContainerId==0
                ||!target.PresentationContainerPhysicsFree
                )
                throw new InvalidOperationException("Destroyed plate presentation preimage is incomplete.");
            ValidateTerminalPresentationPreimage(sequence,target);
            foreach(var renderer in target.Renderers)
            {
                if(!DeliveryFadeReincarnationContract.HasManagedReference(renderer.Renderer)||Alive(renderer.Renderer))
                    throw new InvalidOperationException("A historical destroyed-plate renderer wrapper is absent or unexpectedly live.");
                for(int i=0;i<renderer.Materials.Length;i++)
                    if(DeliveryFadeReincarnationContract.HasManagedReference(renderer.Materials[i])!=(renderer.MaterialIds[i]!=0)
                        ||DeliveryFadeReincarnationContract.HasManagedReference(renderer.Materials[i])&&!Alive(renderer.Materials[i]))
                        throw new InvalidOperationException("A target plate material required for reincarnation is no longer live.");
            }
        }

        private void ValidateTerminalSequenceForActiveTarget(Sequence sequence,PlateState target)
        {
            Type type=sequence.Iterator.GetType();RequireIteratorLayout(type);
            if(!Alive(sequence.Station)||Alive(sequence.Plate)||Alive(sequence.PlateObject)
                ||!ReferenceEquals(Field(type,"plate").GetValue(sequence.Iterator),target.Plate)
                ||!ReferenceEquals(Field(type,"$this").GetValue(sequence.Iterator),sequence.Station)
                ||!ReferenceEquals(Field(type,"deliveryFx").GetValue(sequence.Iterator),sequence.Effects)
                ||Convert.ToBoolean(Field(type,"$disposing").GetValue(sequence.Iterator))
                ||Convert.ToBoolean(Field(type,"<errored>__0").GetValue(sequence.Iterator))
                ||IteratorPc(sequence)!=-1||Field(type,"$current").GetValue(sequence.Iterator)!=null
                ||!DeliveryFadeReincarnationContract.IsTerminalFadeProgress(IteratorProgress(sequence))
                ||Field(type,"<rigidBody>__0").GetValue(sequence.Iterator)!=null
                ||!sequence.MaterialOwnershipComplete||sequence.MaterialsBeforeMoveNext!=null)
                throw new InvalidOperationException("Entity 2 source is not its exact successful terminal delivery sequence.");
            var iteratorColliders=Field(type,"<colliders>__0").GetValue(sequence.Iterator) as Collider[];
            var iteratorRenderers=Field(type,"<allRenderers>__0").GetValue(sequence.Iterator) as MeshRenderer[];
            if(sequence.PresentationRenderers==null||sequence.PresentationRenderers.Length!=target.Renderers.Length
                ||!ExactReferenceMembership(iteratorColliders,target.Colliders.Select(value=>value.Collider).ToArray())
                ||!ExactReferenceMembership(iteratorRenderers,sequence.PresentationRenderers.Select(value=>value.Renderer).ToArray())
                ||!ReferenceEquals(sequence.PresentationOwner,target.PresentationOwner)
                ||sequence.PresentationOwnerId!=target.PresentationOwnerId
                ||sequence.PresentationContainerKey!=target.PresentationContainerKey
                ||!sequence.PresentationContainerPhysicsFree)
                throw new InvalidOperationException("Entity 2 terminal delivery membership differs from active target lineage.");
            foreach(var collider in target.Colliders)
                if(!DeliveryFadeReincarnationContract.HasManagedReference(collider.Collider)||Alive(collider.Collider))
                    throw new InvalidOperationException("Active target collider wrapper is absent or unexpectedly live.");
            foreach(var renderer in target.Renderers)
            {
                if(!DeliveryFadeReincarnationContract.HasManagedReference(renderer.Renderer)||Alive(renderer.Renderer))
                    throw new InvalidOperationException("Active target renderer wrapper is absent or unexpectedly live.");
                foreach(var material in renderer.Materials)
                    if(!DeliveryFadeReincarnationContract.HasManagedReference(material)||!Alive(material))
                        throw new InvalidOperationException("Active target fade material is no longer live.");
            }
        }

        private static void ValidateTerminalPresentationPreimage(Sequence sequence,PlateState target)
        {
            var current=MapRenderers(target.Renderers,sequence.PresentationRenderers);
            if(!ReferenceEquals(sequence.PresentationOwner,target.PresentationOwner)
                ||sequence.PresentationOwnerId!=target.PresentationOwnerId
                ||!DeliveryFadeReincarnationContract.HasManagedReference(sequence.PresentationContainer)
                ||Alive(sequence.PresentationContainer)||sequence.PresentationContainerId==0
                ||sequence.PresentationContainerKey!=target.PresentationContainerKey
                ||!sequence.PresentationContainerPhysicsFree)
                throw new InvalidOperationException("Terminal delivery presentation owner/container proof differs from the target.");
            int reincarnated=0;
            for(int i=0;i<current.Length;i++)
            {
                var value=current[i];var saved=target.Renderers[i];bool exact=ReferenceEquals(value.Renderer,saved.Renderer);
                if(!DeliveryFadeReincarnationContract.HasManagedReference(value.Renderer)||Alive(value.Renderer)
                    ||value.HierarchyKey!=saved.HierarchyKey||value.Enabled!=saved.Enabled
                    ||value.InPresentationContainer!=saved.InPresentationContainer
                    ||value.PresentationKey!=saved.PresentationKey||value.SharedMeshId!=saved.SharedMeshId
                    ||!Same(value.LocalPosition,saved.LocalPosition)||!Same(value.LocalRotation,saved.LocalRotation)
                    ||!Same(value.LocalScale,saved.LocalScale)||value.ActiveSelf!=saved.ActiveSelf
                    ||value.ActiveInHierarchy!=saved.ActiveInHierarchy||value.Layer!=saved.Layer
                    ||value.Materials.Length!=saved.Materials.Length)
                    throw new InvalidOperationException("Terminal delivery renderer preimage differs at "+saved.HierarchyKey+".");
                if(!exact)
                {
                    reincarnated++;
                    if(!saved.InPresentationContainer||value.InstanceId==saved.InstanceId)
                        throw new InvalidOperationException("Terminal renderer replacement is not the captured attached-order cosmetic reincarnation.");
                }
                for(int j=0;j<value.Materials.Length;j++)
                    if(value.MaterialIds[j]!=saved.MaterialIds[j]||value.ShaderIds[j]!=saved.ShaderIds[j]
                        ||value.HasMode[j]!=saved.HasMode[j]||value.HasAlpha[j]!=saved.HasAlpha[j]
                        ||value.HasMode[j]&&value.Mode[j]!=saved.Mode[j]
                        ||value.HasAlpha[j]&&value.Alpha[j]!=saved.Alpha[j])
                        throw new InvalidOperationException("Terminal delivery material preimage differs at "+saved.HierarchyKey+", slot "+j+".");
            }
            if(reincarnated>1||(reincarnated==1&&(target.Renderers.Count(value=>value.InPresentationContainer)!=1
                ||current.Count(value=>value.InPresentationContainer)!=1
                ||sequence.PresentationContainerId==target.PresentationContainerId
                ||ReferenceEquals(sequence.PresentationContainer,target.PresentationContainer))))
                throw new InvalidOperationException("Terminal delivery supports at most the one proven attached-order cosmetic reincarnation.");
        }

        private static bool ExactReferenceMembership<T>(T[] current,T[] target) where T:class
        {
            if(current==null||target==null||current.Length!=target.Length)return false;
            return target.All(value=>current.Count(item=>ReferenceEquals(item,value))==1)
                &&current.All(value=>target.Count(item=>ReferenceEquals(item,value))==1);
        }

        private void ValidateStory11Station(Sequence sequence)
        {
            var nativeStation=sequence.Station.GetComponent<PlateStation>();
            if(nativeStation==null||!ReferenceEquals(nativeStation.m_deliveryEffects,sequence.Effects)
                ||(string)stationTrigger.GetValue(nativeStation)!=String.Empty)
                throw new InvalidOperationException("Only the unchanged Story 1-1 delivery station with no follow-up trigger is supported.");
        }

        private static void ValidatePfxGameplayFree(GameObject root)
        {
            if(!Alive(root))throw new InvalidOperationException("Story 1-1 delivery PFX prefab is absent.");
            var systems=ComponentsRecursive<ParticleSystem>(root.transform);
            if(systems.Length!=6)throw new InvalidOperationException("Story 1-1 delivery PFX particle topology differs.");
            foreach(var system in systems)
                if(system.collision.enabled||system.trigger.enabled)
                    throw new InvalidOperationException("Delivery PFX particle collision/trigger behavior is outside mechanical rewind scope.");
            var autoDestructs=ComponentsRecursive<Component>(root.transform)
                .Where(component=>component!=null&&component.GetType().FullName=="AutoDestructParticleSystem").ToArray();
            if(autoDestructs.Length!=2)
                throw new InvalidOperationException("Story 1-1 delivery PFX auto-destruction topology differs.");
            foreach(var component in ComponentsRecursive<Component>(root.transform))
                if(component==null||(!(component is Transform)&&!(component is ParticleSystem)
                    &&!(component is ParticleSystemRenderer)&&component.GetType().FullName!="AutoDestructParticleSystem"))
                    throw new InvalidOperationException("Delivery PFX contains a gameplay-capable or missing component: "
                        +(component==null?"<missing>":component.GetType().FullName));
            if(ComponentsRecursive<Collider>(root.transform).Length!=0
                ||ComponentsRecursive<Rigidbody>(root.transform).Length!=0
                ||ComponentsRecursive<Animator>(root.transform).Length!=0
                ||ComponentsRecursive<ServerWorldObjectSynchroniser>(root.transform).Length!=0
                ||ComponentsRecursive<ClientWorldObjectSynchroniser>(root.transform).Length!=0)
                throw new InvalidOperationException("Delivery PFX is not mechanically inert.");
        }

        private RestoreCandidate PrepareInactiveTarget(int frame,FrameState target,int currentFades)
        {
            if(currentFades!=1||NativePlateLifecycle.PendingDeliveryFades!=1||DeliveredCount()!=1)
                throw new InvalidOperationException("Inactive delivery target restore requires exactly one current delivered plate fade.");
            if(NativePlateLifecycle.LastObservationError.Length!=0)
                throw new InvalidOperationException("Native plate lifecycle observation is incomplete: "+NativePlateLifecycle.LastObservationError);
            var map=(IDictionary)delivered.GetValue(null);
            var live=sequences.Where(value=>Alive(value.Station)&&Alive(value.Plate)&&map.Contains(value.Plate.gameObject)).ToArray();
            if(live.Length!=1)
                throw new InvalidOperationException("Exactly one captured live delivery iterator must own the current fade.");
            var sequence=live[0];var obj=sequence.Plate.gameObject;
            int instance=Convert.ToInt32(map[obj]);
            if(instance!=obj.GetInstanceID())
                throw new InvalidOperationException("Delivered plate observer incarnation differs from the live plate.");
            var entry=EntitySerialisationRegistry.GetEntry(obj);
            if(entry==null)throw new InvalidOperationException("Delivered plate is no longer registered.");
            int entity=(int)entry.m_Header.m_uEntityID;
            var targets=target.Plates.Where(value=>value.EntityId==entity).ToArray();
            if(targets.Length!=1)throw new InvalidOperationException("Target checkpoint does not contain the same delivered plate entity.");
            var targetPlate=targets[0];
            ValidatePlateIncarnation(targetPlate,sequence.Plate);
            ValidateSequenceForRestore(sequence,targetPlate);
            ValidateStory11Station(sequence);
            return new RestoreCandidate { Frame=frame,CoreSnapshot=target.CoreSnapshot,Target=target,
                TargetPlate=targetPlate,Current=sequence,Pfx=IteratorPfx(sequence) };
        }

        private void ValidateSequenceForRestore(Sequence sequence,PlateState target)
        {
            Type type=sequence.Iterator.GetType();RequireIteratorLayout(type);
            if(!ReferenceEquals(Field(type,"plate").GetValue(sequence.Iterator),sequence.Plate)
                ||!ReferenceEquals(Field(type,"$this").GetValue(sequence.Iterator),sequence.Station)
                ||!ReferenceEquals(Field(type,"deliveryFx").GetValue(sequence.Iterator),sequence.Effects)
                ||Convert.ToBoolean(Field(type,"$disposing").GetValue(sequence.Iterator)))
                throw new InvalidOperationException("Delivery iterator identity or disposal state differs.");
            int pc=Convert.ToInt32(Field(type,"$PC").GetValue(sequence.Iterator));
            float progress=Convert.ToSingle(Field(type,"<progress>__0").GetValue(sequence.Iterator));
            if((pc!=1&&pc!=2)||!Finite(progress)||progress<0f||progress>=1f)
                throw new InvalidOperationException("Only a live pre-destruction delivery wait/fade iterator is supported.");
            object current=Field(type,"$current").GetValue(sequence.Iterator);
            var iteratorRenderers=Field(type,"<allRenderers>__0").GetValue(sequence.Iterator) as MeshRenderer[];
            if(pc==1)
            {
                if(!ReferenceEquals(current,StationWait(sequence.Station))||iteratorRenderers!=null||progress!=0f)
                    throw new InvalidOperationException("Delivery wait iterator state differs from the supported boundary.");
            }
            else
            {
                if(current!=null||iteratorRenderers==null||!sequence.MaterialOwnershipComplete
                    ||iteratorRenderers.Length!=target.Renderers.Length)
                    throw new InvalidOperationException("Delivery fade iterator state/material ownership is incomplete.");
                ValidatePresentationAgainstTarget(sequence,target);
                for(int i=0;i<sequence.PresentationRenderers.Length;i++)
                {
                    var renderer=sequence.PresentationRenderers[i].Renderer;
                    if(iteratorRenderers.Count(value=>ReferenceEquals(value,renderer))!=1)
                        throw new InvalidOperationException("Delivery fade renderer membership differs from its exact pre-fade presentation.");
                }
            }
            var pfx=IteratorPfx(sequence);
            if(sequence.Effects.m_deliverPFXPrefab!=null&&!Alive(pfx))
                throw new InvalidOperationException("Supported delivery fade requires its exact live detached PFX.");
            ValidateCurrentPlateAgainstTarget(sequence,target,pc);
            ValidateLiveExternalUi(target,sequence.Plate.gameObject);
            ValidateOwnedMaterialIsolation(sequence,target,pfx);
        }

        private void ValidateCurrentPlateAgainstTarget(Sequence sequence,PlateState target,int pc)
        {
            ValidatePlateIncarnation(target,sequence.Plate);
            var obj=sequence.Plate.gameObject;
            if(obj.GetComponent<Rigidbody>()!=null)
                throw new InvalidOperationException("Direct-Rigidbody delivered plates are not covered by this restore.");
            var colliders=ComponentsRecursive<Collider>(obj.transform);
            if(colliders.Length!=target.Colliders.Length)throw new InvalidOperationException("Delivered plate collider count differs.");
            bool allFadeDisabled=true,allTargetEnabled=true;
            for(int i=0;i<colliders.Length;i++)
            {
                var saved=target.Colliders[i];var current=colliders[i];
                if(!ReferenceEquals(saved.Collider,current)||current.GetInstanceID()!=saved.InstanceId||current.isTrigger!=saved.Trigger
                    ||current.gameObject.activeSelf!=saved.ActiveSelf||current.gameObject.activeInHierarchy!=saved.ActiveInHierarchy)
                    throw new InvalidOperationException("Delivered plate collider identity/static state differs.");
                allFadeDisabled&=!current.enabled;
                allTargetEnabled&=current.enabled==saved.Enabled;
            }
            if(!allFadeDisabled&&!allTargetEnabled)
                throw new InvalidOperationException("Delivered plate colliders are neither the exact fade-disabled nor restored target state.");
            var presentation=sequence.PresentationRenderers;
            var renderers=ComponentsRecursive<MeshRenderer>(obj.transform);
            if(presentation==null||renderers.Length!=target.Renderers.Length||renderers.Length!=presentation.Length)
                throw new InvalidOperationException("Delivered plate renderer count differs.");
            for(int i=0;i<renderers.Length;i++)
            {
                var saved=presentation[i];var renderer=renderers[i];
                if(!ReferenceEquals(saved.Renderer,renderer)||renderer.GetInstanceID()!=saved.InstanceId
                    ||RendererHierarchyKey(obj.transform,renderer)!=saved.HierarchyKey||renderer.enabled!=saved.Enabled)
                    throw new InvalidOperationException("Delivered plate renderer identity/state differs.");
                var materials=renderer.sharedMaterials;
                if(materials.Length!=saved.Materials.Length)throw new InvalidOperationException("Delivered plate material count differs.");
                for(int j=0;j<materials.Length;j++)
                {
                    if(pc==1)
                    {
                        if(MaterialId(materials[j])!=saved.MaterialIds[j])
                            throw new InvalidOperationException("Delivery wait unexpectedly changed a plate material.");
                    }
                    else if(materials[j]==null||!ReferenceEquals(materials[j].shader,sequence.Effects.m_fadeOutShader)
                        ||!materials[j].HasProperty("_Alpha")
                        ||ReferenceEquals(materials[j],saved.Materials[j])||!sequence.OwnedMaterials.Contains(materials[j]))
                        throw new InvalidOperationException("Delivery fade material is not an exactly owned fade clone.");
                }
            }
        }

        private void ValidateOwnedMaterialIsolation(Sequence sequence,PlateState target,GameObject pfx)
        {
            var targetMaterials=new HashSet<Material>(target.Renderers.SelectMany(value=>value.Materials));
            var plateRenderers=new HashSet<Renderer>(sequence.PresentationRenderers.Select(value=>(Renderer)value.Renderer));
            foreach(var renderer in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if(renderer==null||plateRenderers.Contains(renderer)||IsDescendant(renderer.transform,pfx))continue;
                foreach(var material in renderer.sharedMaterials)
                    if(material!=null&&sequence.OwnedMaterials.Contains(material)&&!targetMaterials.Contains(material))
                        throw new InvalidOperationException("A delivery-owned material is referenced outside the delivered plate/PFX.");
            }
        }

        private void ApplyRestore(RestoreCandidate candidate)
        {
            if(candidate.Applied)throw new InvalidOperationException("Delivery fade restore was invoked twice.");
            if(candidate.ActiveTargetComposite)
            {
                ApplyActiveDestroyedRestore(candidate);
                return;
            }
            if(candidate.DestroyedRoot)
            {
                ApplyDestroyedRestore(candidate);
                return;
            }
            ValidateSequenceForRestore(candidate.Current,candidate.TargetPlate);
            var receipt=Map("frame",candidate.Frame,"entityId",candidate.TargetPlate.EntityId,"pcBefore",IteratorPc(candidate.Current),
                "progressBefore",IteratorProgress(candidate.Current),"pfxBefore",ObjectIdentity(candidate.Pfx),
                "ownedMaterialIds",candidate.Current.OwnedMaterials.Where(Alive).Select(value=>value.GetInstanceID()).OrderBy(value=>value).ToArray(),
                "verified",false,"scope","Authoring-only inverse restore from one live delivered-plate fade to an exact no-fade checkpoint.");
            restoreReceipts.Add(receipt);if(restoreReceipts.Count>64)restoreReceipts.RemoveAt(0);
            candidate.Receipt=receipt;
            candidate.Current.Station.StopCoroutine(candidate.Current.Iterator);
            if(Alive(candidate.Pfx))UnityEngine.Object.DestroyImmediate(candidate.Pfx);
            RestorePlatePresentation(candidate.TargetPlate,candidate.Current);
            RebindLiveExternalUi(candidate.TargetPlate,ValidateLiveExternalUi(candidate.TargetPlate,candidate.TargetPlate.Object));
            var map=(IDictionary)delivered.GetValue(null);
            if(!map.Contains(candidate.TargetPlate.Object)||Convert.ToInt32(map[candidate.TargetPlate.Object])!=candidate.TargetPlate.ObjectId)
                throw new InvalidOperationException("Delivered plate observer changed before authoring removal.");
            map.Remove(candidate.TargetPlate.Object);
            var destroyed=new List<int>();
            foreach(var material in candidate.Current.OwnedMaterials.Where(Alive).ToArray())
            {
                if(MaterialReferenced(material))throw new InvalidOperationException("Delivery-owned material remained referenced after presentation restore.");
                destroyed.Add(material.GetInstanceID());UnityEngine.Object.DestroyImmediate(material);
            }
            candidate.DestroyedMaterialIds=destroyed.OrderBy(value=>value).ToArray();candidate.Applied=true;
            receipt["pcAfterStop"]=IteratorPc(candidate.Current);receipt["destroyedMaterialIds"]=candidate.DestroyedMaterialIds;
            receipt["pfxAliveAfter"]=Alive(candidate.Pfx);receipt["pendingFadesAfter"]=NativePlateLifecycle.PendingDeliveryFades;
        }

        private void ApplyActiveDestroyedRestore(RestoreCandidate candidate)
        {
            if(candidate.CancelCurrent==null||candidate.CancelTargetPlate==null||candidate.TargetSequence==null)
                throw new InvalidOperationException("Active delivery composite restore plan is incomplete.");
            if(candidate.ActiveEarlyApplied)
            {
                CompleteActiveDestroyedRestore(candidate);
                return;
            }
            var cancel=candidate.CancelCurrent;
            ValidateActiveCancellationPreimage(candidate);
            StopSequence(cancel);
            if(Alive(candidate.CancelPfx))UnityEngine.Object.DestroyImmediate(candidate.CancelPfx);
            var observer=(IDictionary)delivered.GetValue(null);
            if(observer.Count!=1||!observer.Contains(candidate.CancelTargetPlate.Object)
                ||Convert.ToInt32(observer[candidate.CancelTargetPlate.Object])!=candidate.CancelTargetPlate.ObjectId)
                throw new InvalidOperationException("Entity 1 delivery observer changed before composite cancellation.");
            observer.Remove(candidate.CancelTargetPlate.Object);

            ValidateTerminalSequenceForActiveTarget(candidate.Current,candidate.TargetPlate);
            var entry=EntitySerialisationRegistry.GetEntry((uint)candidate.TargetPlate.EntityId);
            var obj=entry==null?null:entry.m_GameObject;
            var plate=Alive(obj)?obj.GetComponent<ClientPlate>():null;
            if(entry==null||plate==null)throw new InvalidOperationException("Native factory did not recreate active historical plate entity 2.");
            var current=CapturePlate(entry,plate);
            ValidateRecreatedPlateMechanicalPrefix(candidate.TargetPlate,current);
            var mappedColliders=MapColliders(candidate.TargetPlate.Colliders,current.Colliders);
            for(int i=0;i<mappedColliders.Length;i++)
            {
                var saved=candidate.TargetPlate.Colliders[i];var collider=mappedColliders[i].Collider;
                if(collider.isTrigger!=saved.Trigger)collider.isTrigger=saved.Trigger;
                if(collider.enabled!=saved.Enabled)collider.enabled=saved.Enabled;
                if(collider.isTrigger!=saved.Trigger||collider.enabled!=saved.Enabled)
                    throw new InvalidOperationException("Early recreated delivery collider did not restore exactly at "+saved.HierarchyKey+".");
            }
            // The queued local contents event has not created the cosmetic
            // container at this hook. Restore only target collider geometry,
            // then the exact root scale, before core body-pose certification.
            RestoreRecreatedRootScale(candidate.TargetPlate,current);
            if(observer.Count!=0)throw new InvalidOperationException("Delivery observer was not empty before active target rebind.");
            observer.Add(obj,obj.GetInstanceID());
            candidate.RecreatedPlateObject=obj;candidate.RecreatedPlate=plate;candidate.ActiveEarlyApplied=true;
            var receipt=Map("frame",candidate.Frame,"mode","active-composite","canceledEntityId",1,
                "recreatedEntityId",2,"targetPc",2,"targetProgress",candidate.TargetSequence.Progress,
                "targetAlpha",candidate.TargetPlate.Renderers.SelectMany(value=>value.Alpha).ToArray(),
                "destroyedCancelMaterialIds",null,
                "destroyedSourceMaterialIds",null,
                "earlyColliderRestore",true,"latePresentationRestore",false,
                "pfxMechanicallyInert",false,"pfxVisualStateExact",false,
                "activeIteratorRelayScheduled",false,"verified",false,
                "scope","Authoring-only Story 1-1 entity1 fade cancellation plus early entity2 collider/scale restoration; presentation and PC2 iterator reconstruction wait for the queued local contents event.");
            restoreReceipts.Add(receipt);if(restoreReceipts.Count>64)restoreReceipts.RemoveAt(0);
            candidate.Receipt=receipt;
        }

        private void CompleteActiveDestroyedRestore(RestoreCandidate candidate)
        {
            if(!candidate.ActiveEarlyApplied||candidate.Applied
                ||!Alive(candidate.RecreatedPlateObject)||!Alive(candidate.RecreatedPlate)
                ||!ReferenceEquals(candidate.RecreatedPlate.gameObject,candidate.RecreatedPlateObject)
                ||candidate.ReboundActive!=null||candidate.Rebound!=null)
                throw new InvalidOperationException("Active delivery composite late restore phase is not prepared exactly once.");
            if(Alive(candidate.CancelPfx))
                throw new InvalidOperationException("Canceled entity 1 delivery PFX survived the early restore phase.");
            var observer=(IDictionary)delivered.GetValue(null);
            if(observer.Count!=1||observer.Contains(candidate.CancelTargetPlate.Object)
                ||!observer.Contains(candidate.RecreatedPlateObject)
                ||Convert.ToInt32(observer[candidate.RecreatedPlateObject])!=candidate.RecreatedPlateObject.GetInstanceID())
                throw new InvalidOperationException("Active delivery observer changed before late entity 1 cancellation.");

            // ServerIngredientContainer restoration queues the corresponding
            // local presentation update.  The early body-pose hook runs before
            // Warp's following message flush, so entity 1 can still contain its
            // future sushi renderer there.  At the final checkpoint capture the
            // flush has retired that container; now require the exact f444 keys
            // before restoring materials and releasing all transient ownership.
            Sequence cancelPresentation;
            lastCancellationPresentationRetirement=RetireFlushedCancellationPresentation(candidate,out cancelPresentation);
            RestorePlatePresentation(candidate.CancelTargetPlate,cancelPresentation,true);
            var restoredCancelPresentation=CaptureCurrentPresentation(candidate.CancelTargetPlate);
            ValidatePresentationAgainstTarget(restoredCancelPresentation,candidate.CancelTargetPlate);
            lastAbsentTargetExternalUiRetirement=
                RetireAbsentTargetExternalUi(candidate.CancelTargetPlate);
            RebindLiveExternalUi(candidate.CancelTargetPlate,
                ValidateLiveExternalUi(candidate.CancelTargetPlate,candidate.CancelTargetPlate.Object));
            var destroyedCancelMaterials=DestroyUnreferencedOwnedMaterials(
                candidate.CancelCurrent,new HashSet<Material>());
            var receipt=candidate.Receipt as Dictionary<string,object>;
            if(receipt==null)throw new InvalidOperationException("Active delivery composite restore receipt is absent.");
            receipt["destroyedCancelMaterialIds"]=destroyedCancelMaterials;
            receipt["lateCancellationAfterMessageFlush"]=true;
            receipt["cancellationPresentationRetirement"]=lastCancellationPresentationRetirement;
            receipt["absentTargetExternalUiRetirement"]=lastAbsentTargetExternalUiRetirement;

            // The same flush creates entity 2's attached-order container. Only
            // now build its fade iterator, so the iterator captures both the
            // plate and sushi renderers. Collider state was already restored
            // early for core physics certification.
            var entry=EntitySerialisationRegistry.GetEntry((uint)candidate.TargetPlate.EntityId);
            if(entry==null||!ReferenceEquals(entry.m_GameObject,candidate.RecreatedPlateObject)
                ||!ReferenceEquals(candidate.RecreatedPlateObject.GetComponent<ClientPlate>(),candidate.RecreatedPlate))
                throw new InvalidOperationException("Recreated entity 2 incarnation changed before late presentation restore.");
            var current=CapturePlate(entry,candidate.RecreatedPlate);
            ValidateRecreatedPlatePrefix(candidate.TargetPlate,current);
            if(DeliveryFadeReincarnationContract.ExactUniqueKeyMap(
                candidate.TargetPlate.Renderers.Select(value=>value.HierarchyKey).ToArray(),
                current.Renderers.Select(value=>value.HierarchyKey).ToArray())==null)
            {
                var preFadeBase=candidate.TargetSequence.Source.PresentationRenderers
                    .Single(value=>!value.InPresentationContainer);
                candidate.ForcedPresentationStartComponentId=CompleteExactVirginSushiPresentation(
                    candidate.TargetPlate,current,preFadeBase);
                current=CapturePlate(entry,candidate.RecreatedPlate);
            }
            var mappedRenderers=ValidateRecreatedActivePlateTopology(candidate.TargetPlate,current);
            var mappedColliders=MapColliders(candidate.TargetPlate.Colliders,current.Colliders);
            for(int i=0;i<mappedColliders.Length;i++)
            {
                var saved=candidate.TargetPlate.Colliders[i];var collider=mappedColliders[i].Collider;
                if(collider.isTrigger!=saved.Trigger||collider.enabled!=saved.Enabled)
                    throw new InvalidOperationException("Late recreated delivery collider changed after early exact restoration at "+saved.HierarchyKey+".");
            }

            int beforeFactories=sequences.Count;
            var iterator=deliveryFactory.Invoke(candidate.Current.Station,
                new object[]{candidate.RecreatedPlate,candidate.Current.Effects}) as IEnumerator;
            if(iterator==null||sequences.Count!=beforeFactories+1)
                throw new InvalidOperationException("Pure delivery iterator factory did not produce one observed sequence.");
            var rebound=sequences.Single(value=>ReferenceEquals(value.Iterator,iterator));
            if(rebound.EntityId!=2||!ReferenceEquals(rebound.Plate,candidate.RecreatedPlate)
                ||!ReferenceEquals(rebound.Station,candidate.Current.Station))
                throw new InvalidOperationException("Recreated active delivery iterator identity differs.");
            if(!iterator.MoveNext()||IteratorPc(rebound)!=1||!ReferenceEquals(iterator.Current,StationWait(rebound.Station)))
                throw new InvalidOperationException("Recreated delivery iterator did not reach its zero-delay wait boundary.");
            if(rebound.Effects.m_pfxToFadeDelayTime!=0f||!iterator.MoveNext()||IteratorPc(rebound)!=2||iterator.Current!=null)
                throw new InvalidOperationException("Recreated delivery iterator did not reach PC2 under the zero-delay contract.");
            var newPfx=IteratorPfx(rebound);ValidatePfxGameplayFree(newPfx);
            if(!Alive(newPfx)||newPfx.transform.parent!=null)
                throw new InvalidOperationException("Recreated delivery PFX is not live and detached.");
            RestoreRecreatedRootScale(candidate.TargetPlate,current);

            var preservedMaterials=new HashSet<Material>(candidate.TargetPlate.Renderers
                .SelectMany(value=>value.Materials).Where(DeliveryFadeReincarnationContract.HasManagedReference));
            var freshFadeMaterials=ValidateFreshFadeMaterialOwnership(rebound,mappedRenderers,
                candidate.TargetPlate,preservedMaterials);
            for(int i=0;i<mappedRenderers.Length;i++)
            {
                var saved=candidate.TargetPlate.Renderers[i];var renderer=mappedRenderers[i].Renderer;
                if(!Same(renderer.transform.localScale,saved.LocalScale))renderer.transform.localScale=saved.LocalScale;
                renderer.sharedMaterials=saved.Materials;renderer.enabled=saved.Enabled;
                for(int j=0;j<saved.Materials.Length;j++)
                {
                    var material=saved.Materials[j];
                    if(saved.HasMode[j])material.SetFloat("_Mode",saved.Mode[j]);
                    if(saved.HasAlpha[j])material.SetFloat("_Alpha",saved.Alpha[j]);
                }
            }
            foreach(var material in freshFadeMaterials.Where(value=>!preservedMaterials.Contains(value)).ToArray())
            {
                if(!rebound.OwnedMaterials.Remove(material))
                    throw new InvalidOperationException("Fresh replacement fade material ownership disappeared before destruction.");
                if(MaterialReferenced(material))throw new InvalidOperationException("Fresh replacement fade material remained referenced.");
                UnityEngine.Object.DestroyImmediate(material);
            }
            foreach(var material in preservedMaterials)rebound.OwnedMaterials.Add(material);
            foreach(var material in preservedMaterials)
                if(!candidate.Current.OwnedMaterials.Remove(material))
                    throw new InvalidOperationException("Historical target fade material ownership disappeared before transfer.");
            var destroyedSourceMaterials=DestroyUnreferencedOwnedMaterials(candidate.Current,preservedMaterials);

            Type iteratorType=iterator.GetType();
            Field(iteratorType,"<progress>__0").SetValue(iterator,candidate.TargetSequence.Progress);
            Field(iteratorType,"<errored>__0").SetValue(iterator,candidate.TargetSequence.Errored);
            Field(iteratorType,"$disposing").SetValue(iterator,false);
            Field(iteratorType,"$current").SetValue(iterator,null);
            Field(iteratorType,"$PC").SetValue(iterator,2);

            var restored=CapturePlate(entry,candidate.RecreatedPlate);
            RebindRetainedEntity2History(candidate,restored,rebound,newPfx);
            candidate.Rebound=CaptureCurrentPresentation(candidate.TargetPlate);
            candidate.ReboundActive=rebound;candidate.Pfx=newPfx;
            receipt["destroyedSourceMaterialIds"]=destroyedSourceMaterials;
            receipt["reboundHistoryFrames"]=candidate.ReboundHistoryFrames;
            receipt["discardedIncompatibleHistoryFrames"]=candidate.DiscardedIncompatibleHistoryFrames;
            receipt["discardedIncompatibleHistory"]=candidate.DiscardedIncompatibleHistory;
            receipt["forcedPresentationStartComponentId"]=candidate.ForcedPresentationStartComponentId;
            receipt["latePresentationRestore"]=true;
            receipt["pfxMechanicallyInert"]=true;
            receipt["scope"]="Authoring-only Story 1-1 entity1 fade cancellation plus split-phase entity2 recreation: exact collider/scale state before core physics certification, then presentation and PC2 iterator after the queued local contents event; particle playback is recreated, not pixel-exact.";
            candidate.Applied=true;
        }

        private static HashSet<Material> ValidateFreshFadeMaterialOwnership(Sequence sequence,
            RendererState[] mappedRenderers,PlateState target,HashSet<Material> preserved)
        {
            Type type=sequence.Iterator.GetType();var iteratorRenderers=Field(type,"<allRenderers>__0").GetValue(sequence.Iterator) as MeshRenderer[];
            if(!sequence.MaterialOwnershipComplete||sequence.MaterialsBeforeMoveNext!=null
                ||iteratorRenderers==null||mappedRenderers==null||target==null||target.Renderers==null
                ||mappedRenderers.Length!=target.Renderers.Length
                ||!ExactReferenceMembership(iteratorRenderers,mappedRenderers.Select(value=>value.Renderer).ToArray()))
                throw new InvalidOperationException("Fresh delivery iterator material ownership proof is incomplete.");
            var slots=new List<Material>();float? alpha=null;
            for(int i=0;i<mappedRenderers.Length;i++)
            {
                var materials=mappedRenderers[i].Renderer.sharedMaterials;var saved=target.Renderers[i];
                if(materials.Length!=saved.Materials.Length)
                    throw new InvalidOperationException("Fresh fade material slot count differs at "+saved.HierarchyKey+".");
                for(int j=0;j<materials.Length;j++)
                {
                    var material=materials[j];
                    if(!Alive(material)||preserved.Contains(material)||!sequence.OwnedMaterials.Contains(material)
                        ||!ReferenceEquals(material.shader,sequence.Effects.m_fadeOutShader)
                        ||!material.HasProperty("_Alpha")||material.HasProperty("_Mode")!=saved.HasMode[j]
                        ||saved.HasMode[j]&&material.GetFloat("_Mode")!=saved.Mode[j])
                        throw new InvalidOperationException("Fresh fade material is not an exact owned replacement at "
                            +saved.HierarchyKey+", slot "+j+".");
                    float value=material.GetFloat("_Alpha");
                    if(!Finite(value)||value<0f||value>1f||(alpha.HasValue&&value!=alpha.Value))
                        throw new InvalidOperationException("Fresh fade material alpha state differs across the recreated plate.");
                    alpha=value;slots.Add(material);
                }
            }
            var result=new HashSet<Material>(slots);
            if(!DeliveryFadeReincarnationContract.IsNonEmptyDistinctSubset(
                    result.Select(MaterialId).ToArray(),sequence.OwnedMaterials.Where(Alive).Select(MaterialId).ToArray())
                ||result.Any(material=>!sequence.OwnedMaterials.Contains(material)))
                throw new InvalidOperationException("Fresh fade material set is not a nonempty subset of iterator ownership.");
            return result;
        }

        private void ValidateActiveCancellationPreimage(RestoreCandidate candidate)
        {
            var sequence=candidate.CancelCurrent;var obj=sequence==null?null:sequence.PlateObject;
            var observer=(IDictionary)delivered.GetValue(null);
            bool contains=sequence!=null&&Alive(obj)&&observer.Contains(obj);
            int observed=contains?Convert.ToInt32(observer[obj]):0;
            if(sequence==null||!Alive(obj)||!Alive(sequence.Plate)||sequence.EntityId!=1
                ||!ReferenceEquals(sequence.Plate.gameObject,obj)||obj.GetInstanceID()!=sequence.PlateObjectId
                ||sequence.Plate.GetInstanceID()!=sequence.PlateId
                ||!DeliveryFadeReincarnationContract.IsExactObserverEntry(
                    observer.Count,contains,observed,sequence.PlateObjectId))
                throw new InvalidOperationException("Current entity 1 observer/incarnation changed before destructive cancellation.");
            var entry=EntitySerialisationRegistry.GetEntry((uint)1);
            Type type=sequence.Iterator.GetType();RequireIteratorLayout(type);
            var iteratorColliders=Field(type,"<colliders>__0").GetValue(sequence.Iterator) as Collider[];
            var iteratorRenderers=Field(type,"<allRenderers>__0").GetValue(sequence.Iterator) as MeshRenderer[];
            var pfx=IteratorPfx(sequence);
            if(entry==null||!ReferenceEquals(entry.m_GameObject,obj)
                ||!Alive(sequence.Station)||sequence.Station.GetInstanceID()!=sequence.StationId
                ||!ReferenceEquals(Field(type,"plate").GetValue(sequence.Iterator),sequence.Plate)
                ||!ReferenceEquals(Field(type,"$this").GetValue(sequence.Iterator),sequence.Station)
                ||!ReferenceEquals(Field(type,"deliveryFx").GetValue(sequence.Iterator),sequence.Effects)
                ||IteratorPc(sequence)!=2||!Finite(IteratorProgress(sequence))
                ||IteratorProgress(sequence)<0f||IteratorProgress(sequence)>=1f
                ||Convert.ToBoolean(Field(type,"$disposing").GetValue(sequence.Iterator))
                ||Convert.ToBoolean(Field(type,"<errored>__0").GetValue(sequence.Iterator))
                ||Field(type,"$current").GetValue(sequence.Iterator)!=null
                ||Field(type,"<rigidBody>__0").GetValue(sequence.Iterator)!=null
                ||iteratorColliders==null||iteratorRenderers==null
                ||!ExactReferenceMembership(iteratorColliders,ComponentsRecursive<Collider>(obj.transform))
                ||!sequence.MaterialOwnershipComplete||sequence.MaterialsBeforeMoveNext!=null
                ||!ReferenceEquals(pfx,candidate.CancelPfx)||!Alive(pfx)||pfx.transform.parent!=null)
                throw new InvalidOperationException("Current entity 1 iterator source changed before destructive cancellation.");
            ValidateStory11Station(sequence);
            ValidatePfxGameplayFree(sequence.Effects.m_deliverPFXPrefab);
            ValidatePfxGameplayFree(pfx);
            var owned=sequence.OwnedMaterials.ToArray();
            if(owned.Length==0||owned.Any(material=>!Alive(material)
                    ||!ReferenceEquals(material.shader,sequence.Effects.m_fadeOutShader)
                    ||!material.HasProperty("_Alpha")||!Finite(material.GetFloat("_Alpha"))
                    ||material.GetFloat("_Alpha")<0f||material.GetFloat("_Alpha")>1f))
                throw new InvalidOperationException("Current entity 1 fade material ownership/state changed before cancellation.");
            var liveFadeMaterials=new List<Material>();
            foreach(var renderer in iteratorRenderers.Where(Alive))
                foreach(var material in renderer.sharedMaterials)
                {
                    if(!Alive(material)||!sequence.OwnedMaterials.Contains(material)
                        ||!ReferenceEquals(material.shader,sequence.Effects.m_fadeOutShader)
                        ||!material.HasProperty("_Alpha"))
                        throw new InvalidOperationException("Current entity 1 live renderer no longer uses its owned fade material.");
                    liveFadeMaterials.Add(material);
                }
            // Unity's first delivery MoveNext can create additional owned
            // material instances which are no longer renderer-referenced.
            // They remain cleanup obligations, but their stale alpha is not
            // visual state.  Exact alpha uniformity applies only to the owned
            // materials still driving live fade renderers.
            if(liveFadeMaterials.Count==0||liveFadeMaterials.Select(material=>material.GetFloat("_Alpha"))
                .Distinct().Count()!=1)
                throw new InvalidOperationException("Current entity 1 live fade material alpha state differs before cancellation.");
            ValidateOwnedMaterialIsolation(sequence,candidate.CancelTargetPlate,pfx);
        }

        private static void StopSequence(Sequence sequence)
        {
            var scheduled=sequence.SchedulerIterator??sequence.Iterator;
            sequence.Station.StopCoroutine(scheduled);
            sequence.SchedulerIterator=null;sequence.SchedulerCoroutine=null;
        }

        private object RetireFlushedCancellationPresentation(RestoreCandidate candidate,out Sequence targetPresentation)
        {
            var target=candidate.CancelTargetPlate;var sequence=candidate.CancelCurrent;
            var obj=sequence==null?null:sequence.PlateObject;
            var owner=Alive(obj)?obj.GetComponent<ClientAttachedOrderCosmeticDecisions>():null;
            var container=sequence==null?null:sequence.PresentationContainer;
            var expectedExtras=sequence==null||sequence.PresentationRenderers==null
                ?new RendererState[0]
                :sequence.PresentationRenderers.Where(value=>value.InPresentationContainer).ToArray();
            targetPresentation=null;
            if(target==null||sequence==null||target.EntityId!=1||sequence.EntityId!=1
                ||!Alive(obj)||!Alive(sequence.Plate)||!ReferenceEquals(sequence.Plate.gameObject,obj)
                ||!Alive(owner)||!ReferenceEquals(owner,sequence.PresentationOwner)
                ||owner.GetInstanceID()!=sequence.PresentationOwnerId
                ||!ReferenceEquals(owner,target.PresentationOwner)||owner.GetInstanceID()!=target.PresentationOwnerId
                ||!ReferenceEquals(target.PresentationContainer,null)||target.PresentationContainerId!=0
                ||target.PresentationContainerKey!=null||target.PresentationCompositionFingerprint!=null
                ||target.Renderers==null||target.Renderers.Length!=1
                ||target.Renderers.Any(value=>value.InPresentationContainer)
                ||expectedExtras.Length!=1||!Alive(container)
                ||container.GetInstanceID()!=sequence.PresentationContainerId
                ||TransformHierarchyKey(obj.transform,container.transform)!=sequence.PresentationContainerKey
                ||!sequence.PresentationContainerPhysicsFree||!PhysicsFreePresentation(container)
                ||!IsDescendant(container.transform,obj)
                ||!ReferenceEquals(GetPresentationContainer(owner),null))
                throw new InvalidOperationException("Flushed entity 1 presentation retirement preimage differs from the exact composite rewind.");
            ValidatePlateIncarnation(target,sequence.Plate);

            var containerRenderers=ComponentsRecursive<MeshRenderer>(container.transform);
            var currentRenderers=ComponentsRecursive<MeshRenderer>(obj.transform);
            var extra=expectedExtras[0];
            var sourceSurvivors=sequence.PresentationRenderers.Where(value=>!value.InPresentationContainer).ToArray();
            var mappedSourceSurvivors=MapRenderers(target.Renderers,sourceSurvivors);
            if(containerRenderers.Length!=1||!ReferenceEquals(containerRenderers[0],extra.Renderer)
                ||!Alive(extra.Renderer)||extra.Renderer.GetInstanceID()!=extra.InstanceId
                ||RendererHierarchyKey(obj.transform,extra.Renderer)!=extra.HierarchyKey
                ||sequence.PresentationRenderers.Length!=target.Renderers.Length+expectedExtras.Length
                ||currentRenderers.Length!=target.Renderers.Length+expectedExtras.Length
                ||currentRenderers.Count(value=>ReferenceEquals(value,extra.Renderer))!=1)
                throw new InvalidOperationException("Flushed entity 1 presentation is not the exact delivery-owned outgoing renderer hierarchy.");
            var survivorStates=currentRenderers.Where(value=>!ReferenceEquals(value,extra.Renderer))
                .Select(value=>CaptureRenderer(value,obj.transform,null)).ToArray();
            var mappedSurvivors=MapRenderers(target.Renderers,survivorStates);
            for(int i=0;i<mappedSurvivors.Length;i++)
            {
                var live=mappedSurvivors[i];var saved=target.Renderers[i];var source=mappedSourceSurvivors[i];
                if(!ReferenceEquals(live.Renderer,saved.Renderer)||live.InstanceId!=saved.InstanceId
                    ||!ReferenceEquals(source.Renderer,saved.Renderer)||source.InstanceId!=saved.InstanceId
                    ||live.SharedMeshId!=saved.SharedMeshId||live.Enabled!=saved.Enabled
                    ||!Same(live.LocalPosition,saved.LocalPosition)||!Same(live.LocalRotation,saved.LocalRotation)
                    ||!Same(live.LocalScale,saved.LocalScale)||live.ActiveSelf!=saved.ActiveSelf
                    ||live.ActiveInHierarchy!=saved.ActiveInHierarchy||live.Layer!=saved.Layer
                    ||live.Materials.Length!=saved.Materials.Length
                    ||source.Materials.Length!=saved.Materials.Length)
                    throw new InvalidOperationException("Flushed entity 1 surviving plate renderer differs from the exact target topology.");
                for(int j=0;j<live.Materials.Length;j++)
                    if(!Alive(live.Materials[j])||!sequence.OwnedMaterials.Contains(live.Materials[j])
                        ||!ReferenceEquals(live.Materials[j].shader,sequence.Effects.m_fadeOutShader)
                        ||!live.Materials[j].HasProperty("_Alpha")
                        ||!ReferenceEquals(source.Materials[j],saved.Materials[j])
                        ||source.MaterialIds[j]!=saved.MaterialIds[j]
                        ||!DeliveryFadeReincarnationContract.HasManagedReference(saved.Materials[j])
                        ||!Alive(saved.Materials[j])||sequence.OwnedMaterials.Contains(saved.Materials[j]))
                        throw new InvalidOperationException("Flushed entity 1 surviving plate material ownership differs from the exact fade-to-target swap.");
            }
            foreach(var material in extra.Renderer.sharedMaterials)
                if(!Alive(material)||!sequence.OwnedMaterials.Contains(material)
                    ||!ReferenceEquals(material.shader,sequence.Effects.m_fadeOutShader)
                    ||!material.HasProperty("_Alpha"))
                    throw new InvalidOperationException("Flushed entity 1 outgoing cosmetic renderer lost exact fade-material ownership.");

            targetPresentation=new Sequence {Plate=sequence.Plate,PlateId=sequence.PlateId,
                PlateObject=obj,PlateObjectId=sequence.PlateObjectId,
                PresentationOwner=owner,PresentationOwnerId=owner.GetInstanceID(),
                PresentationContainer=null,PresentationContainerId=0,PresentationContainerKey=null,
                PresentationContainerPhysicsFree=false,PresentationRenderers=mappedSourceSurvivors};

            var result=Map("containerId",container.GetInstanceID(),"containerKey",sequence.PresentationContainerKey,
                "rendererIds",expectedExtras.Select(value=>value.InstanceId).ToArray(),
                "ownerContainerCleared",true,"physicsFree",true,"destroyedImmediately",false,"verified",false,
                "scope","Authoring-only completion of the already-flushed entity 1 contents removal: the exact old physics-free cosmetic container is still alive until Unity end-of-frame destruction, so the rewind retires it synchronously before target-boundary certification.");
            UnityEngine.Object.DestroyImmediate(container);
            if(Alive(container)||expectedExtras.Any(value=>Alive(value.Renderer))
                ||!ReferenceEquals(GetPresentationContainer(owner),null))
                throw new InvalidOperationException("Flushed entity 1 presentation did not retire synchronously.");
            var remaining=ComponentsRecursive<MeshRenderer>(obj.transform)
                .Select(value=>CaptureRenderer(value,obj.transform,null)).ToArray();
            MapRenderers(target.Renderers,remaining);
            result["destroyedImmediately"]=true;result["verified"]=true;
            return result;
        }

        private static int[] DestroyUnreferencedOwnedMaterials(Sequence sequence,HashSet<Material> preserve)
        {
            var destroyed=new List<int>();
            foreach(var material in sequence.OwnedMaterials.Where(Alive).ToArray())
            {
                if(preserve!=null&&preserve.Contains(material))continue;
                if(MaterialReferenced(material))throw new InvalidOperationException("Delivery-owned material remains referenced during composite cleanup.");
                destroyed.Add(material.GetInstanceID());UnityEngine.Object.DestroyImmediate(material);
            }
            return destroyed.OrderBy(value=>value).ToArray();
        }

        private RendererState[] ValidateRecreatedActivePlateTopology(PlateState target,PlateState current)
        {
            ValidateRecreatedPlatePrefix(target,current);
            var mapped=MapRenderers(target.Renderers,current.Renderers);
            for(int i=0;i<mapped.Length;i++)
            {
                var saved=target.Renderers[i];var value=mapped[i];
                if(value.HierarchyKey!=saved.HierarchyKey||value.InPresentationContainer!=saved.InPresentationContainer
                    ||value.PresentationKey!=saved.PresentationKey||value.SharedMeshId!=saved.SharedMeshId
                    ||!Same(value.LocalPosition,saved.LocalPosition)||!Same(value.LocalRotation,saved.LocalRotation)
                    ||(!Same(value.LocalScale,saved.LocalScale)&&!BoundedScaleResidual(value.LocalScale,saved.LocalScale))
                    ||value.ActiveSelf!=saved.ActiveSelf||value.ActiveInHierarchy!=saved.ActiveInHierarchy
                    ||value.Layer!=saved.Layer||value.Enabled!=saved.Enabled
                    ||value.Materials.Length!=saved.Materials.Length)
                    throw new InvalidOperationException("Recreated active delivered plate renderer topology differs at "+saved.HierarchyKey+".");
            }
            return mapped;
        }

        private void ApplyDestroyedRestore(RestoreCandidate candidate)
        {
            ValidateTerminalSequence(candidate.Current,candidate.TargetPlate);
            var entry=EntitySerialisationRegistry.GetEntry((uint)candidate.TargetPlate.EntityId);
            var obj=entry==null?null:entry.m_GameObject;
            var plate=Alive(obj)?obj.GetComponent<ClientPlate>():null;
            if(entry==null||plate==null)throw new InvalidOperationException("Native factory did not recreate historical delivered plate entity 2.");
            var current=CapturePlate(entry,plate);
            ValidateRecreatedPlatePrefix(candidate.TargetPlate,current);
            var mappedColliders=MapColliders(candidate.TargetPlate.Colliders,current.Colliders);
            ValidateTargetRendererMaterialPrerequisites(candidate.TargetPlate.Renderers);
            if(DeliveryFadeReincarnationContract.ExactUniqueKeyMap(
                candidate.TargetPlate.Renderers.Select(value=>value.HierarchyKey).ToArray(),
                current.Renderers.Select(value=>value.HierarchyKey).ToArray())==null)
            {
                candidate.ForcedPresentationStartComponentId=CompleteExactVirginSushiPresentation(candidate.TargetPlate,current);
                current=CapturePlate(entry,plate);
            }
            var mappedRenderers=ValidateRecreatedPlate(candidate.TargetPlate,current,false);
            mappedColliders=MapColliders(candidate.TargetPlate.Colliders,current.Colliders);
            var receipt=Map("frame",candidate.Frame,"entityId",candidate.TargetPlate.EntityId,
                "oldObjectId",candidate.TargetPlate.ObjectId,"newObjectId",current.ObjectId,
                "factoryPath",new[]{34,0,0},"logicalPath",new[]{candidate.TargetPlate.EntityId},
                "pcBefore",IteratorPc(candidate.Current),"progressBefore",IteratorProgress(candidate.Current),
                "pfxBefore",ObjectIdentity(candidate.Pfx),
                "ownedMaterialIds",candidate.Current.OwnedMaterials.Where(Alive).Select(value=>value.GetInstanceID()).OrderBy(value=>value).ToArray(),
                "discardedReturnEntityIds",candidate.DiscardedReturnEntityIds??new int[0],
                "forcedPresentationStart",candidate.ForcedPresentationStartComponentId!=0,
                "forcedPresentationStartComponentId",candidate.ForcedPresentationStartComponentId,
                "verified",false,"scope","Authoring-only reincarnation of the exact destroyed Story 1-1 plate through its native factory; gameplay components and synchronisers are proof-only, while target presentation references are restored.");
            restoreReceipts.Add(receipt);if(restoreReceipts.Count>64)restoreReceipts.RemoveAt(0);
            candidate.Receipt=receipt;

            // All gameplay/root/factory checks precede mutation. Only target
            // collider flags, exact renderer references and a bounded
            // physics-free presentation scale are then restored.
            RestoreRecreatedRootScale(candidate.TargetPlate,current);
            var correctedPresentationScales=new List<object>();
            for(int i=0;i<mappedRenderers.Length;i++)
            {
                var saved=candidate.TargetPlate.Renderers[i];var renderer=mappedRenderers[i].Renderer;
                if(!Same(renderer.transform.localScale,saved.LocalScale))
                {
                    var beforeScale=renderer.transform.localScale;
                    renderer.transform.localScale=saved.LocalScale;
                    if(!Same(renderer.transform.localScale,saved.LocalScale))
                        throw new InvalidOperationException("Recreated delivered plate presentation scale did not restore exactly.");
                    correctedPresentationScales.Add(Map("hierarchyKey",saved.HierarchyKey,
                        "before",Point(beforeScale),"after",Point(renderer.transform.localScale)));
                }
                renderer.sharedMaterials=saved.Materials;renderer.enabled=saved.Enabled;
                for(int j=0;j<saved.Materials.Length;j++)
                {
                    var material=saved.Materials[j];if(!DeliveryFadeReincarnationContract.HasManagedReference(material))continue;
                    if(saved.HasMode[j])material.SetFloat("_Mode",saved.Mode[j]);
                    if(saved.HasAlpha[j])material.SetFloat("_Alpha",saved.Alpha[j]);
                }
            }
            receipt["correctedPresentationScales"]=correctedPresentationScales.ToArray();
            for(int i=0;i<mappedColliders.Length;i++)
            {
                var saved=candidate.TargetPlate.Colliders[i];var collider=mappedColliders[i].Collider;
                if(collider.isTrigger!=saved.Trigger)collider.isTrigger=saved.Trigger;
                if(collider.enabled!=saved.Enabled)collider.enabled=saved.Enabled;
            }
            if(Alive(candidate.Pfx))UnityEngine.Object.DestroyImmediate(candidate.Pfx);
            var destroyed=new List<int>();
            foreach(var material in candidate.Current.OwnedMaterials.Where(Alive).ToArray())
            {
                if(MaterialReferenced(material))throw new InvalidOperationException("Delivery-owned fade material remained referenced after destroyed-root reincarnation.");
                destroyed.Add(material.GetInstanceID());UnityEngine.Object.DestroyImmediate(material);
            }

            var restored=CapturePlate(entry,plate);
            var restoredRenderers=ValidateRecreatedPlate(candidate.TargetPlate,restored,true);
            var restoredColliders=MapColliders(candidate.TargetPlate.Colliders,restored.Colliders);
            RebindPlateState(candidate.TargetPlate,restored,restoredRenderers,restoredColliders);
            candidate.Rebound=CaptureCurrentPresentation(candidate.TargetPlate);
            candidate.DestroyedMaterialIds=destroyed.OrderBy(value=>value).ToArray();candidate.Applied=true;
            receipt["newPlateComponentId"]=candidate.TargetPlate.ComponentId;
            receipt["newPresentationOwnerId"]=candidate.TargetPlate.PresentationOwnerId;
            receipt["newPresentationContainerId"]=candidate.TargetPlate.PresentationContainerId;
            receipt["newExternalUiObjectId"]=candidate.TargetPlate.ExternalUi.ObjectId;
            receipt["destroyedMaterialIds"]=candidate.DestroyedMaterialIds;
            receipt["pfxAliveAfter"]=Alive(candidate.Pfx);receipt["pendingFadesAfter"]=NativePlateLifecycle.PendingDeliveryFades;
        }

        private int CompleteExactVirginSushiPresentation(PlateState target,PlateState current,
            RendererState preFadeBase=null)
        {
            ValidateRecreatedPlatePrefix(target,current);
            var container=current.PresentationContainer;
            var sushiComponents=Alive(container)?ComponentsRecursive<SushiCosmeticDecisions>(container.transform):new SushiCosmeticDecisions[0];
            var assignableComponents=Alive(container)?ComponentsRecursive<AssignableOrderDefinition>(container.transform):new AssignableOrderDefinition[0];
            var sushi=sushiComponents.Length==1?sushiComponents[0]:null;
            var assignable=assignableComponents.Length==1?assignableComponents[0]:null;
            object lifecycleContainer=sushi==null?null:mealContainer.GetValue(sushi);
            object lifecycleOrder=sushi==null?null:mealOrderDefinition.GetValue(sushi);
            object lifecycleRendererInfo=sushi==null?null:mealRendererInfo.GetValue(sushi);
            AssembledDefinitionNode composition=assignable==null?null:assignable.GetOrderComposition();
            string assignedComposition=CompositionFingerprint(composition);
            var lookup=sushi==null?null:comboPrefabLookup.GetValue(sushi) as ComboOrderToPrefabLookup;
            if(!DeliveryFadeReincarnationContract.IsExactVirginStory11SushiPresentation(
                    target.Renderers.Select(value=>value.HierarchyKey).ToArray(),
                    target.Renderers.Select(value=>value.InPresentationContainer).ToArray(),
                    current.Renderers.Select(value=>value.HierarchyKey).ToArray(),
                    current.Renderers.Select(value=>value.InPresentationContainer).ToArray(),
                    current.PresentationContainerKey,sushiComponents.Length,assignableComponents.Length,
                    ReferenceEquals(lifecycleContainer,null),ReferenceEquals(lifecycleOrder,null),
                    ReferenceEquals(lifecycleRendererInfo,null),target.PresentationCompositionFingerprint,assignedComposition)
                ||sushi==null||assignable==null||sushi.GetType()!=typeof(SushiCosmeticDecisions)
                ||!ReferenceEquals(sushi.gameObject,container)||!ReferenceEquals(assignable.gameObject,container)
                ||!sushi.enabled||!container.activeSelf||!container.activeInHierarchy
                ||!Alive(lookup)
                ||suppressedNaturalPresentationStarts.Any(value=>ReferenceEquals(value.Component,sushi))
                ||!ReferenceEquals(manualPresentationStart,null))
                throw new InvalidOperationException("Recreated sushi presentation is not the exact virgin pending-Start lifecycle.");

            var targetBase=target.Renderers.Single(value=>!value.InPresentationContainer);
            var savedBase=preFadeBase??targetBase;
            if(preFadeBase!=null&&(preFadeBase.InPresentationContainer
                ||!ReferenceEquals(preFadeBase.Renderer,targetBase.Renderer)
                ||preFadeBase.InstanceId!=targetBase.InstanceId
                ||preFadeBase.HierarchyKey!=targetBase.HierarchyKey
                ||preFadeBase.PresentationKey!=targetBase.PresentationKey
                ||preFadeBase.SharedMeshId!=targetBase.SharedMeshId
                ||!Same(preFadeBase.LocalPosition,targetBase.LocalPosition)
                ||!Same(preFadeBase.LocalRotation,targetBase.LocalRotation)
                ||!Same(preFadeBase.LocalScale,targetBase.LocalScale)
                ||preFadeBase.ActiveSelf!=targetBase.ActiveSelf
                ||preFadeBase.ActiveInHierarchy!=targetBase.ActiveInHierarchy
                ||preFadeBase.Layer!=targetBase.Layer||preFadeBase.Enabled!=targetBase.Enabled))
                throw new InvalidOperationException("Active target pre-fade plate renderer lineage differs from the f444 topology.");
            ValidateTargetRendererMaterialPrerequisites(new[]{savedBase});
            ValidateRecreatedRenderer(savedBase,current.Renderers.Single(value=>!value.InPresentationContainer),false);
            var before=Map("component",ObjectIdentity(sushi),"container",ObjectIdentity(container),
                "compositionType",composition.GetType().FullName,"rendererKeys",current.Renderers.Select(value=>value.HierarchyKey).ToArray());
            manualPresentationStart=sushi;
            try
            {
                comboStart.Invoke(sushi,new object[0]);
            }
            catch(TargetInvocationException error)
            {
                throw new InvalidOperationException("Exact authoring sushi presentation Start failed.",error.InnerException??error);
            }
            finally { manualPresentationStart=null; }

            var inner=mealContainer.GetValue(sushi) as GameObject;
            var order=mealOrderDefinition.GetValue(sushi) as IClientOrderDefinition;
            var rendererInfo=mealRendererInfo.GetValue(sushi) as RendererSceneInfo;
            if(!Alive(inner)||inner.name!="IngredientContainer"||!ReferenceEquals(inner.transform.parent,container.transform)
                ||!ReferenceEquals(order,assignable)||!Alive(rendererInfo)||!ReferenceEquals(rendererInfo.gameObject,container)
                ||!PhysicsFreePresentation(container))
                throw new InvalidOperationException("Forced sushi presentation Start did not produce its exact initialized physics-free lifecycle.");
            var marker=suppressedNaturalPresentationStarts.SingleOrDefault(value=>ReferenceEquals(value.Component,sushi));
            if(marker==null)throw new InvalidOperationException("Forced sushi presentation Start did not enter the patched lifecycle body.");
            marker.Outer=container;marker.Inner=inner;marker.Order=order;marker.RendererInfo=rendererInfo;marker.Completed=true;
            forcedPresentationStarts++;
            lastPresentationLifecycleCompletion=Map("phase","authoring-start-completed","before",before,
                "component",ObjectIdentity(sushi),"innerContainer",ObjectIdentity(inner),
                "rendererInfo",ObjectIdentity(rendererInfo),"pendingNaturalSuppression",true,
                "scope","Exact virgin Story 1-1 sushi presentation Start completed during authoring restore; its one Unity-scheduled duplicate will be suppressed.");
            return sushi.GetInstanceID();
        }

        private RendererState[] ValidateRecreatedPlate(PlateState target,PlateState current,bool requireTargetMaterials)
        {
            ValidateRecreatedPlatePrefix(target,current);
            var mapped=MapRenderers(target.Renderers,current.Renderers);
            if(target.Renderers.Count(value=>value.InPresentationContainer)!=1
                ||mapped.Count(value=>value.InPresentationContainer)!=1)
                throw new InvalidOperationException("Destroyed-root restore supports exactly one attached-order cosmetic renderer.");
            for(int i=0;i<mapped.Length;i++)ValidateRecreatedRenderer(target.Renderers[i],mapped[i],requireTargetMaterials);
            return mapped;
        }

        private void ValidateRecreatedPlatePrefix(PlateState target,PlateState current)
        {
            ValidateRecreatedPlateMechanicalPrefix(target,current);
            bool targetOwnerReference=DeliveryFadeReincarnationContract.HasManagedReference(target.PresentationOwner);
            bool targetOwnerAlive=Alive(target.PresentationOwner);
            bool currentOwnerReference=!ReferenceEquals(current.PresentationOwner,null);
            bool currentOwnerAlive=Alive(current.PresentationOwner);
            bool sameOwner=ReferenceEquals(current.PresentationOwner,target.PresentationOwner);
            bool targetContainerReference=DeliveryFadeReincarnationContract.HasManagedReference(target.PresentationContainer);
            bool targetContainerAlive=Alive(target.PresentationContainer);
            bool currentContainerAlive=Alive(current.PresentationContainer);
            bool sameContainer=ReferenceEquals(current.PresentationContainer,target.PresentationContainer);
            int presentationColliders=currentContainerAlive?ComponentsRecursive<Collider>(current.PresentationContainer.transform).Length:-1;
            int presentationBodies=currentContainerAlive?ComponentsRecursive<Rigidbody>(current.PresentationContainer.transform).Length:-1;
            int presentationAnimators=currentContainerAlive?ComponentsRecursive<Animator>(current.PresentationContainer.transform).Length:-1;
            int presentationServerSyncs=currentContainerAlive?ComponentsRecursive<ServerWorldObjectSynchroniser>(current.PresentationContainer.transform).Length:-1;
            int presentationClientSyncs=currentContainerAlive?ComponentsRecursive<ClientWorldObjectSynchroniser>(current.PresentationContainer.transform).Length:-1;
            bool presentationExact=targetOwnerReference&&!targetOwnerAlive&&currentOwnerReference&&currentOwnerAlive&&!sameOwner
                &&target.PresentationOwnerId!=0&&current.PresentationOwnerId!=target.PresentationOwnerId
                &&targetContainerReference&&!targetContainerAlive&&target.PresentationContainerId!=0&&currentContainerAlive&&!sameContainer
                &&current.PresentationContainerId!=target.PresentationContainerId
                &&current.PresentationContainerKey==target.PresentationContainerKey
                &&target.PresentationCompositionFingerprint!=null
                &&current.PresentationCompositionFingerprint==target.PresentationCompositionFingerprint
                &&target.PresentationContainerPhysicsFree&&current.PresentationContainerPhysicsFree;
            lastRecreatedPrefixValidation=Map("targetEntityId",target.EntityId,"currentEntityId",current.EntityId,
                "targetOwnerReference",targetOwnerReference,"targetOwnerAlive",targetOwnerAlive,
                "targetOwnerId",target.PresentationOwnerId,"currentOwnerReference",currentOwnerReference,
                "currentOwnerAlive",currentOwnerAlive,"currentOwnerId",current.PresentationOwnerId,"sameOwner",sameOwner,
                "targetContainerReference",targetContainerReference,"targetContainerAlive",targetContainerAlive,
                "targetContainerId",target.PresentationContainerId,"currentContainerAlive",currentContainerAlive,
                "currentContainerId",current.PresentationContainerId,"sameContainer",sameContainer,
                "targetContainerKey",target.PresentationContainerKey,"currentContainerKey",current.PresentationContainerKey,
                "targetComposition",target.PresentationCompositionFingerprint,
                "currentComposition",current.PresentationCompositionFingerprint,
                "targetPhysicsFree",target.PresentationContainerPhysicsFree,
                "currentPhysicsFree",current.PresentationContainerPhysicsFree,
                "currentPresentationColliders",presentationColliders,"currentPresentationBodies",presentationBodies,
                "currentPresentationAnimators",presentationAnimators,"currentPresentationServerSyncs",presentationServerSyncs,
                "currentPresentationClientSyncs",presentationClientSyncs,"exact",presentationExact);
            if(!presentationExact)
                throw new InvalidOperationException("Recreated plate did not produce the exact fresh physics-free ClientAttachedOrderCosmeticDecisions presentation.");
            ValidateRecreatedExternalUi(target.ExternalUi,current.ExternalUi);
        }

        private static void ValidateRecreatedPlateMechanicalPrefix(PlateState target,PlateState current)
        {
            bool rootScaleExact=Same(current.LocalScale,target.LocalScale);
            var entry=EntitySerialisationRegistry.GetEntry((uint)target.EntityId);
            if(entry==null||!ReferenceEquals(entry.m_GameObject,current.Object)||target.EntityId!=current.EntityId
                ||!Alive(current.Object)||!Alive(current.Plate)||ReferenceEquals(current.Object,target.Object)
                ||ReferenceEquals(current.Plate,target.Plate)||current.ObjectId==target.ObjectId||current.ComponentId==target.ComponentId
                ||!ReferenceEquals(current.Plate.gameObject,current.Object)||current.Object.GetComponent<Rigidbody>()!=null)
                throw new InvalidOperationException("Recreated delivered plate does not have the exact historical registration and fresh root incarnation.");
            if(!DeliveryFadeReincarnationContract.IsExactRecreatedComponentTopology(target.ComponentTypes,current.ComponentTypes)
                ||!target.ServerSynchroniserTypes.SequenceEqual(current.ServerSynchroniserTypes)
                ||!target.ClientSynchroniserTypes.SequenceEqual(current.ClientSynchroniserTypes))
                throw new InvalidOperationException("Recreated delivered plate component or synchroniser topology differs from the historical entity.");
            if(!DeliveryFadeReincarnationContract.IsExactAttachmentParentIncarnation(
                    DeliveryFadeReincarnationContract.HasManagedReference(target.Parent),Alive(target.Parent),target.ParentId,
                    Alive(current.Parent),current.ParentId,ReferenceEquals(current.Parent,target.Parent))
                ||!Same(current.LocalPosition,target.LocalPosition)||!Same(current.LocalRotation,target.LocalRotation)
                ||(!rootScaleExact&&!BoundedScaleResidual(current.LocalScale,target.LocalScale))
                ||current.Layer!=target.Layer
                ||current.ActiveSelf!=target.ActiveSelf||current.ActiveInHierarchy!=target.ActiveInHierarchy)
                throw new InvalidOperationException("Recreated delivered plate root transform/active state differs from the target checkpoint.");
        }

        private static void RestoreRecreatedRootScale(PlateState target,PlateState current)
        {
            if(Same(current.Object.transform.localScale,target.LocalScale))return;
            // Factory spawn/reparenting rounds the historical 0.9999999 X/Z
            // scale back to 1. The caller has already proved the finite 1e-6
            // bound; restore exact checkpoint geometry and require readback.
            current.Object.transform.localScale=target.LocalScale;
            current.LocalScale=current.Object.transform.localScale;
            if(!Same(current.LocalScale,target.LocalScale))
                throw new InvalidOperationException("Recreated delivered plate root scale did not restore exactly.");
        }

        private static void ValidateTargetRendererMaterialPrerequisites(RendererState[] renderers)
        {
            if(renderers==null)throw new InvalidOperationException("Target renderer material preimage is absent.");
            foreach(var saved in renderers)
            {
                int count=saved.Materials==null?-1:saved.Materials.Length;
                if(count<0||saved.MaterialIds==null||saved.ShaderIds==null||saved.HasMode==null||saved.Mode==null
                    ||saved.HasAlpha==null||saved.Alpha==null||saved.MaterialIds.Length!=count||saved.ShaderIds.Length!=count
                    ||saved.HasMode.Length!=count||saved.Mode.Length!=count||saved.HasAlpha.Length!=count||saved.Alpha.Length!=count)
                    throw new InvalidOperationException("Target renderer material arrays are incomplete at "+saved.HierarchyKey+".");
                for(int j=0;j<count;j++)
                {
                    var material=saved.Materials[j];bool present=DeliveryFadeReincarnationContract.HasManagedReference(material);
                    if(present&&(!Alive(material)||MaterialId(material)!=saved.MaterialIds[j]
                            ||ShaderId(material)!=saved.ShaderIds[j]
                            ||material.HasProperty("_Mode")!=saved.HasMode[j]
                            ||material.HasProperty("_Alpha")!=saved.HasAlpha[j]
                            ||saved.HasMode[j]&&material.GetFloat("_Mode")!=saved.Mode[j]
                            ||saved.HasAlpha[j]&&material.GetFloat("_Alpha")!=saved.Alpha[j])
                        ||!present&&(saved.MaterialIds[j]!=0||saved.ShaderIds[j]!=0||saved.HasMode[j]||saved.HasAlpha[j]))
                        throw new InvalidOperationException("Target renderer material changed before restoration at "+saved.HierarchyKey+", slot "+j+".");
                }
            }
        }

        private static void ValidateRecreatedRenderer(RendererState saved,RendererState value,bool requireTargetMaterials)
        {
            if(!Alive(value.Renderer)||ReferenceEquals(value.Renderer,saved.Renderer)
                ||value.InstanceId==saved.InstanceId||value.HierarchyKey!=saved.HierarchyKey
                ||value.InPresentationContainer!=saved.InPresentationContainer
                ||value.PresentationKey!=saved.PresentationKey||value.SharedMeshId!=saved.SharedMeshId
                ||!Same(value.LocalPosition,saved.LocalPosition)||!Same(value.LocalRotation,saved.LocalRotation)
                ||(!Same(value.LocalScale,saved.LocalScale)
                    &&(requireTargetMaterials||!saved.InPresentationContainer
                        ||!BoundedScaleResidual(value.LocalScale,saved.LocalScale)))
                ||value.ActiveSelf!=saved.ActiveSelf
                ||value.ActiveInHierarchy!=saved.ActiveInHierarchy||value.Layer!=saved.Layer
                ||value.Enabled!=saved.Enabled||value.Materials.Length!=saved.Materials.Length)
                throw new InvalidOperationException("Recreated delivered plate renderer topology/mesh/pose/layer/enabled/active state differs at "+saved.HierarchyKey+".");
            for(int j=0;j<value.Materials.Length;j++)
            {
                var targetMaterial=saved.Materials[j];var material=value.Materials[j];
                if(DeliveryFadeReincarnationContract.HasManagedReference(targetMaterial)&&!Alive(targetMaterial))
                    throw new InvalidOperationException("Target plate material reference required for restoration is no longer live.");
                if(DeliveryFadeReincarnationContract.HasManagedReference(targetMaterial)
                        !=DeliveryFadeReincarnationContract.HasManagedReference(material)
                    ||DeliveryFadeReincarnationContract.HasManagedReference(material)&&!Alive(material)
                    ||ShaderId(material)!=saved.ShaderIds[j]
                    ||value.HasMode[j]!=saved.HasMode[j]||value.HasAlpha[j]!=saved.HasAlpha[j]
                    ||value.HasMode[j]&&value.Mode[j]!=saved.Mode[j]
                    ||value.HasAlpha[j]&&value.Alpha[j]!=saved.Alpha[j]
                    ||requireTargetMaterials&&MaterialId(material)!=saved.MaterialIds[j])
                {
                    var module=active;
                    if(module!=null)module.lastRendererMaterialFailure=Map(
                        "hierarchyKey",saved.HierarchyKey,"slot",j,"requireTargetMaterials",requireTargetMaterials,
                        "target",MaterialProofIdentity(targetMaterial,saved,j),
                        "current",MaterialProofIdentity(material,value,j),
                        "scope","Read-only exact material proof captured immediately before fail-closed rejection.");
                    throw new InvalidOperationException("Recreated delivered plate material proof differs at "+saved.HierarchyKey+", slot "+j+".");
                }
            }
        }

        private static object MaterialProofIdentity(Material material,RendererState state,int index)
        {
            bool reference=DeliveryFadeReincarnationContract.HasManagedReference(material),alive=reference&&Alive(material);
            return Map("hasManagedReference",reference,"alive",alive,"instanceId",alive?MaterialId(material):0,
                "capturedInstanceId",state.MaterialIds[index],"shaderId",alive?ShaderId(material):0,
                "capturedShaderId",state.ShaderIds[index],"hasMode",alive&&material.HasProperty("_Mode"),
                "capturedHasMode",state.HasMode[index],"mode",alive&&material.HasProperty("_Mode")?(object)material.GetFloat("_Mode"):null,
                "capturedMode",state.HasMode[index]?(object)state.Mode[index]:null,
                "hasAlpha",alive&&material.HasProperty("_Alpha"),"capturedHasAlpha",state.HasAlpha[index],
                "alpha",alive&&material.HasProperty("_Alpha")?(object)material.GetFloat("_Alpha"):null,
                "capturedAlpha",state.HasAlpha[index]?(object)state.Alpha[index]:null);
        }

        private static RendererState[] MapRenderers(RendererState[] target,RendererState[] current)
        {
            var map=DeliveryFadeReincarnationContract.ExactUniqueKeyMap(
                target.Select(value=>value.HierarchyKey).ToArray(),current.Select(value=>value.HierarchyKey).ToArray());
            if(map==null)
            {
                var module=active;
                if(module!=null)module.lastRendererMapFailure=Map(
                    "target",target.Select(RendererMapIdentity).ToArray(),
                    "current",current.Select(RendererMapIdentity).ToArray(),
                    "scope","Read-only synchronous renderer-key inputs captured immediately before fail-closed rejection; no game state mutation.");
                throw new InvalidOperationException("Recreated delivered plate renderer hierarchy keys are missing, extra, or duplicated.");
            }
            return map.Select(value=>current[value]).ToArray();
        }

        private static object RendererMapIdentity(RendererState value)
        {
            return Map("hierarchyKey",value==null?null:value.HierarchyKey,
                "presentationKey",value==null?null:value.PresentationKey,
                "inPresentationContainer",value!=null&&value.InPresentationContainer,
                "instanceId",value==null?0:value.InstanceId,
                "alive",value!=null&&Alive(value.Renderer),
                "sharedMeshId",value==null?0:value.SharedMeshId);
        }

        private static ColliderState[] MapColliders(ColliderState[] target,ColliderState[] current)
        {
            var map=DeliveryFadeReincarnationContract.ExactUniqueKeyMap(
                target.Select(value=>value.HierarchyKey).ToArray(),current.Select(value=>value.HierarchyKey).ToArray());
            if(map==null)throw new InvalidOperationException("Recreated delivered plate collider hierarchy keys are missing, extra, or duplicated.");
            var result=map.Select(value=>current[value]).ToArray();
            for(int i=0;i<result.Length;i++)
                if(result[i].TypeName!=target[i].TypeName||result[i].ActiveSelf!=target[i].ActiveSelf
                    ||result[i].ActiveInHierarchy!=target[i].ActiveInHierarchy)
                    throw new InvalidOperationException("Recreated delivered plate collider topology/static state differs at "+target[i].HierarchyKey+".");
            return result;
        }

        private void RebindRetainedEntity2History(RestoreCandidate candidate,PlateState current,
            Sequence rebound,GameObject newPfx)
        {
            var plans=new List<Entity2HistoryRebind>();var discarded=new List<int>();var discardReasons=new List<object>();
            GameObject oldObject=candidate.TargetPlate.Object;ClientPlate oldPlate=candidate.TargetPlate.Plate;
            int oldObjectId=candidate.TargetPlate.ObjectId,oldComponentId=candidate.TargetPlate.ComponentId;
            foreach(var pair in history.Where(value=>value.Key<=candidate.Frame).ToArray())
            {
                FrameState frame=pair.Value;
                var plates=(frame.Plates??new PlateState[0]).Where(value=>value.EntityId==2).ToArray();
                var sequenceStates=(frame.SequenceStates??new SequenceFrameState[0]).Where(value=>value.EntityId==2).ToArray();
                try
                {
                    if(plates.Length==0)
                    {
                        if(sequenceStates.Length!=0)throw new InvalidOperationException("Entity 2 iterator exists without its plate snapshot.");
                        continue;
                    }
                    if(plates.Length!=1||sequenceStates.Length>1)
                        throw new InvalidOperationException("Entity 2 historical cardinality differs.");
                    var saved=plates[0];
                    if(!ReferenceEquals(saved.Object,oldObject)||!ReferenceEquals(saved.Plate,oldPlate)
                        ||saved.ObjectId!=oldObjectId||saved.ComponentId!=oldComponentId)
                        throw new InvalidOperationException("Entity 2 history refers to an unknown incarnation.");
                    RendererState[] renderers;ColliderState[] colliders;
                    ValidateHistoricalEntity2PlateMapping(saved,current,out renderers,out colliders);
                    foreach(var state in sequenceStates)
                        ValidateHistoricalEntity2SequenceMapping(state,saved,candidate.Current);
                    plans.Add(new Entity2HistoryRebind { Frame=pair.Key,Plate=saved,
                        Renderers=renderers,Colliders=colliders,Sequences=sequenceStates });
                }
                catch(Exception error)
                {
                    if(pair.Key==candidate.Frame)
                        throw new InvalidOperationException("Target entity 2 history cannot be rebound safely.",error);
                    discarded.Add(pair.Key);
                    discardReasons.Add(Map("frame",pair.Key,"reason",error.Message,"type",error.GetType().FullName));
                }
            }
            var targetPlan=plans.SingleOrDefault(value=>value.Frame==candidate.Frame);
            if(targetPlan==null||!ReferenceEquals(targetPlan.Plate,candidate.TargetPlate)
                ||targetPlan.Sequences.Length!=1||!ReferenceEquals(targetPlan.Sequences[0],candidate.TargetSequence))
                throw new InvalidOperationException("Exact target entity 2 history rebind plan is absent.");

            Type iteratorType=rebound.Iterator.GetType();
            var reboundColliders=Field(iteratorType,"<colliders>__0").GetValue(rebound.Iterator) as Collider[];
            var reboundRenderers=Field(iteratorType,"<allRenderers>__0").GetValue(rebound.Iterator) as MeshRenderer[];
            var reboundBody=Field(iteratorType,"<rigidBody>__0").GetValue(rebound.Iterator) as Rigidbody;
            if(reboundColliders==null||reboundRenderers==null||reboundBody!=null
                ||!ExactReferenceMembership(reboundColliders,current.Colliders.Select(value=>value.Collider).ToArray())
                ||!ExactReferenceMembership(reboundRenderers,current.Renderers.Select(value=>value.Renderer).ToArray()))
                throw new InvalidOperationException("Rebound entity 2 iterator component membership differs.");

            foreach(int frame in discarded.Distinct().OrderBy(value=>value).ToArray())history.Remove(frame);
            foreach(var plan in plans)
            {
                RebindHistoricalPlateIdentity(plan.Plate,current,plan.Renderers,plan.Colliders);
                foreach(var state in plan.Sequences)
                {
                    state.Source=rebound;
                    state.Colliders=state.Colliders==null?null:reboundColliders;
                    state.Body=null;
                    state.Renderers=state.Renderers==null?null:reboundRenderers;
                    state.Pfx=state.PfxAlive?newPfx:null;
                }
            }
            candidate.ReboundHistoryFrames=plans.Select(value=>value.Frame).OrderBy(value=>value).ToArray();
            candidate.DiscardedIncompatibleHistoryFrames=discarded.Distinct().OrderBy(value=>value).ToArray();
            candidate.DiscardedIncompatibleHistory=discardReasons.OrderBy(value=>Convert.ToInt32(((Dictionary<string,object>)value)["frame"])).ToArray();
        }

        private static void ValidateHistoricalEntity2PlateMapping(PlateState saved,PlateState current,
            out RendererState[] renderers,out ColliderState[] colliders)
        {
            if(saved==null||current==null||saved.EntityId!=2||current.EntityId!=2
                ||!Alive(current.Object)||!Alive(current.Plate)||ReferenceEquals(saved.Object,current.Object)
                ||ReferenceEquals(saved.Plate,current.Plate)||saved.Name!=current.Name
                ||!DeliveryFadeReincarnationContract.IsExactRecreatedComponentTopology(saved.ComponentTypes,current.ComponentTypes)
                ||!saved.ServerSynchroniserTypes.SequenceEqual(current.ServerSynchroniserTypes)
                ||!saved.ClientSynchroniserTypes.SequenceEqual(current.ClientSynchroniserTypes)
                ||!DeliveryFadeReincarnationContract.HasManagedReference(saved.PresentationOwner)
                ||!Alive(current.PresentationOwner)||ReferenceEquals(saved.PresentationOwner,current.PresentationOwner)
                ||!DeliveryFadeReincarnationContract.HasManagedReference(saved.PresentationContainer)
                ||!Alive(current.PresentationContainer)||ReferenceEquals(saved.PresentationContainer,current.PresentationContainer)
                ||saved.PresentationContainerKey!=current.PresentationContainerKey
                ||saved.PresentationCompositionFingerprint!=current.PresentationCompositionFingerprint
                ||!saved.PresentationContainerPhysicsFree||!current.PresentationContainerPhysicsFree)
                throw new InvalidOperationException("Historical entity 2 root/presentation topology is not logically compatible.");
            var rendererMap=DeliveryFadeReincarnationContract.ExactUniqueKeyMap(
                saved.Renderers.Select(value=>value.HierarchyKey).ToArray(),
                current.Renderers.Select(value=>value.HierarchyKey).ToArray());
            if(rendererMap==null)throw new InvalidOperationException("Historical entity 2 renderer keys differ.");
            renderers=rendererMap.Select(value=>current.Renderers[value]).ToArray();
            for(int i=0;i<renderers.Length;i++)
                if(renderers[i].SharedMeshId!=saved.Renderers[i].SharedMeshId
                    ||renderers[i].InPresentationContainer!=saved.Renderers[i].InPresentationContainer
                    ||renderers[i].PresentationKey!=saved.Renderers[i].PresentationKey
                    ||renderers[i].Materials.Length!=saved.Renderers[i].Materials.Length)
                    throw new InvalidOperationException("Historical entity 2 renderer structure differs at "+saved.Renderers[i].HierarchyKey+".");
            var colliderMap=DeliveryFadeReincarnationContract.ExactUniqueKeyMap(
                saved.Colliders.Select(value=>value.HierarchyKey).ToArray(),
                current.Colliders.Select(value=>value.HierarchyKey).ToArray());
            if(colliderMap==null)throw new InvalidOperationException("Historical entity 2 collider keys differ.");
            colliders=colliderMap.Select(value=>current.Colliders[value]).ToArray();
            for(int i=0;i<colliders.Length;i++)
                if(colliders[i].TypeName!=saved.Colliders[i].TypeName)
                    throw new InvalidOperationException("Historical entity 2 collider type differs at "+saved.Colliders[i].HierarchyKey+".");
            ValidateHistoricalExternalUiMapping(saved.ExternalUi,current.ExternalUi);
        }

        private static void ValidateHistoricalExternalUiMapping(ExternalUiState saved,ExternalUiState current)
        {
            if(saved==null||current==null||!DeliveryFadeReincarnationContract.HasManagedReference(saved.Controller)
                ||!DeliveryFadeReincarnationContract.HasManagedReference(saved.Instance)
                ||!DeliveryFadeReincarnationContract.HasManagedReference(saved.Object)
                ||!Alive(current.Controller)||!Alive(current.Instance)||!Alive(current.Object)
                ||ReferenceEquals(saved.Controller,current.Controller)||ReferenceEquals(saved.Instance,current.Instance)
                ||ReferenceEquals(saved.Object,current.Object)||saved.ControllerType!=current.ControllerType
                ||saved.InstanceType!=current.InstanceType||saved.Name!=current.Name
                ||!ReferenceEquals(saved.Parent,current.Parent)||saved.ParentId!=current.ParentId
                ||saved.ComponentTypes==null||current.ComponentTypes==null
                ||!saved.ComponentTypes.SequenceEqual(current.ComponentTypes))
                throw new InvalidOperationException("Historical entity 2 external UI topology is not logically compatible.");
        }

        private static void ValidateHistoricalEntity2SequenceMapping(SequenceFrameState state,PlateState plate,Sequence source)
        {
            if(state==null||state.Source==null||!ReferenceEquals(state.Source,source)||state.EntityId!=2
                ||state.Disposing||state.Errored||state.Body!=null||!Finite(state.Progress))
                throw new InvalidOperationException("Historical entity 2 iterator source is not logically compatible.");
            var plateColliders=plate.Colliders.Select(value=>value.Collider).ToArray();
            var plateRenderers=plate.Renderers.Select(value=>value.Renderer).ToArray();
            if(state.Colliders!=null&&!ExactReferenceMembership(state.Colliders,plateColliders)
                ||state.Renderers!=null&&!ExactReferenceMembership(state.Renderers,plateRenderers))
                throw new InvalidOperationException("Historical entity 2 iterator component membership differs.");
            if(!DeliveryFadeReincarnationContract.IsRebindableDeliveryPhase(
                state.Pc,state.CurrentIsNull,state.CurrentIsStationWait,state.Progress,
                state.Colliders!=null,state.Renderers!=null,state.PfxAlive,state.PfxDetached,
                DeliveryFadeReincarnationContract.HasManagedReference(state.Pfx)))
                throw new InvalidOperationException("Historical entity 2 iterator phase cannot be rebound safely.");
        }

        private static void RebindHistoricalPlateIdentity(PlateState saved,PlateState current,
            RendererState[] renderers,ColliderState[] colliders)
        {
            saved.Object=current.Object;saved.ObjectId=current.ObjectId;saved.Plate=current.Plate;saved.ComponentId=current.ComponentId;
            saved.Parent=current.Parent;saved.ParentId=current.ParentId;
            saved.PresentationOwner=current.PresentationOwner;saved.PresentationOwnerId=current.PresentationOwnerId;
            saved.PresentationContainer=current.PresentationContainer;saved.PresentationContainerId=current.PresentationContainerId;
            for(int i=0;i<saved.Renderers.Length;i++)
            { saved.Renderers[i].Renderer=renderers[i].Renderer;saved.Renderers[i].InstanceId=renderers[i].InstanceId; }
            for(int i=0;i<saved.Colliders.Length;i++)
            { saved.Colliders[i].Collider=colliders[i].Collider;saved.Colliders[i].InstanceId=colliders[i].InstanceId; }
            saved.ExternalUi.Controller=current.ExternalUi.Controller;saved.ExternalUi.ControllerId=current.ExternalUi.ControllerId;
            saved.ExternalUi.Instance=current.ExternalUi.Instance;saved.ExternalUi.InstanceId=current.ExternalUi.InstanceId;
            saved.ExternalUi.Object=current.ExternalUi.Object;saved.ExternalUi.ObjectId=current.ExternalUi.ObjectId;
        }

        private static void RebindPlateState(PlateState target,PlateState current,RendererState[] renderers,ColliderState[] colliders)
        {
            target.Object=current.Object;target.ObjectId=current.ObjectId;target.Plate=current.Plate;target.ComponentId=current.ComponentId;
            target.Parent=current.Parent;target.ParentId=current.ParentId;
            target.PresentationOwner=current.PresentationOwner;target.PresentationOwnerId=current.PresentationOwnerId;
            target.PresentationContainer=current.PresentationContainer;target.PresentationContainerId=current.PresentationContainerId;
            for(int i=0;i<target.Renderers.Length;i++)
            { target.Renderers[i].Renderer=renderers[i].Renderer;target.Renderers[i].InstanceId=renderers[i].InstanceId; }
            for(int i=0;i<target.Colliders.Length;i++)
            { target.Colliders[i].Collider=colliders[i].Collider;target.Colliders[i].InstanceId=colliders[i].InstanceId; }
            RebindExternalUi(target.ExternalUi,current.ExternalUi);
        }

        private void RestorePlatePresentation(PlateState target,Sequence sequence,bool allowReincarnatedScaleCorrection=false)
        {
            ValidatePlateIncarnation(target,target.Plate);
            ValidatePresentationAgainstTarget(sequence,target,allowReincarnatedScaleCorrection);
            var presentation=sequence.PresentationRenderers;
            for(int i=0;i<presentation.Length;i++)
            {
                var current=presentation[i];var saved=target.Renderers[i];
                if(!ReferenceEquals(current.Renderer,saved.Renderer)&&!Same(current.LocalScale,saved.LocalScale))
                {
                    current.Renderer.transform.localScale=saved.LocalScale;
                    current.LocalScale=current.Renderer.transform.localScale;
                }
                current.Renderer.sharedMaterials=saved.Materials;current.Renderer.enabled=saved.Enabled;
                for(int j=0;j<saved.Materials.Length;j++)
                {
                    var material=saved.Materials[j];if(!DeliveryFadeReincarnationContract.HasManagedReference(material))continue;
                    if(saved.HasMode[j])material.SetFloat("_Mode",saved.Mode[j]);
                    if(saved.HasAlpha[j])material.SetFloat("_Alpha",saved.Alpha[j]);
                }
            }
            foreach(var saved in target.Colliders)
            {
                if(saved.Collider.isTrigger!=saved.Trigger)saved.Collider.isTrigger=saved.Trigger;
                if(saved.Collider.enabled!=saved.Enabled)saved.Collider.enabled=saved.Enabled;
            }
        }

        private void VerifyRestored(RestoreCandidate candidate)
        {
            if(candidate.ActiveTargetComposite)
            {
                VerifyActiveCompositeRestored(candidate);
                return;
            }
            if(!candidate.Applied||NativePlateLifecycle.PendingDeliveryFades!=0||DeliveredCount()!=0||Alive(candidate.Pfx))
                throw new InvalidOperationException("Delivered plate coroutine/PFX/observer was not fully retired by rewind.");
            VerifyPlate(candidate.TargetPlate,candidate.DestroyedRoot?candidate.Rebound:candidate.Current);
            if(candidate.DestroyedRoot)VerifyExternalUi(candidate.TargetPlate.ExternalUi);
            foreach(var material in candidate.Current.OwnedMaterials)
                if(Alive(material))throw new InvalidOperationException("A delivery-owned transient material survived rewind.");
            var receipt=candidate.Receipt as Dictionary<string,object>;
            if(receipt!=null){receipt["verified"]=true;receipt["platePresentationExact"]=true;
                if(candidate.DestroyedRoot)receipt["externalUiReboundExact"]=true;}
        }

        private void VerifyActiveCompositeRestored(RestoreCandidate candidate)
        {
            if(!candidate.Applied||candidate.ReboundActive==null||candidate.Rebound==null
                ||NativePlateLifecycle.PendingDeliveryFades!=1||DeliveredCount()!=1
                ||Alive(candidate.CancelPfx)||!Alive(candidate.Pfx)||candidate.Pfx.transform.parent!=null)
                throw new InvalidOperationException("Active delivery composite lifecycle did not restore exactly.");
            var map=(IDictionary)delivered.GetValue(null);
            if(map.Count!=1||!map.Contains(candidate.TargetPlate.Object)
                ||Convert.ToInt32(map[candidate.TargetPlate.Object])!=candidate.TargetPlate.ObjectId)
                throw new InvalidOperationException("Active delivery target observer did not rebind exactly.");
            var canceled=CaptureCurrentPresentation(candidate.CancelTargetPlate);
            VerifyPlate(candidate.CancelTargetPlate,canceled);
            VerifyExternalUi(candidate.CancelTargetPlate.ExternalUi);
            VerifyPlate(candidate.TargetPlate,candidate.Rebound);
            VerifyExternalUi(candidate.TargetPlate.ExternalUi);
            var sequence=candidate.ReboundActive;Type type=sequence.Iterator.GetType();
            if(sequence.SchedulerIterator!=null||sequence.SchedulerCoroutine!=null
                ||IteratorPc(sequence)!=2||IteratorProgress(sequence)!=candidate.TargetSequence.Progress
                ||Convert.ToBoolean(Field(type,"$disposing").GetValue(sequence.Iterator))
                ||Convert.ToBoolean(Field(type,"<errored>__0").GetValue(sequence.Iterator))
                ||Field(type,"$current").GetValue(sequence.Iterator)!=null
                ||!ReferenceEquals(IteratorPfx(sequence),candidate.Pfx))
                throw new InvalidOperationException("Recreated active delivery iterator state differs from f444.");
            ValidateExactActiveTarget(candidate.TargetSequence,candidate.TargetPlate);
            ValidatePfxGameplayFree(candidate.Pfx);
            foreach(var material in candidate.CancelCurrent.OwnedMaterials)
                if(Alive(material))throw new InvalidOperationException("Canceled entity 1 fade material survived composite rewind.");
            foreach(var material in candidate.Current.OwnedMaterials)
                if(Alive(material)&&!sequence.OwnedMaterials.Contains(material))
                    throw new InvalidOperationException("Old entity 2 delivery material survived outside the rebound sequence.");
            var receipt=candidate.Receipt as Dictionary<string,object>;
            if(receipt!=null){receipt["verified"]=true;receipt["platePresentationExact"]=true;
                receipt["externalUiReboundExact"]=true;receipt["pendingFadesAfter"]=1;}
        }

        private void VerifyPlate(PlateState saved,Sequence sequence)
        {
            ValidatePlateIncarnation(saved,saved.Plate);var obj=saved.Object;
            if(!ReferenceEquals(obj.transform.parent,saved.Parent)||!Same(obj.transform.localPosition,saved.LocalPosition)
                ||!Same(obj.transform.localRotation,saved.LocalRotation)||!Same(obj.transform.localScale,saved.LocalScale))
                throw new InvalidOperationException("Restored delivered plate transform differs from the target checkpoint.");
            var colliders=ComponentsRecursive<Collider>(obj.transform);
            if(colliders.Length!=saved.Colliders.Length)throw new InvalidOperationException("Restored delivered plate collider count differs.");
            var currentColliders=colliders.Select(value=>CaptureCollider(value,obj.transform)).ToArray();
            var mappedColliders=MapColliders(saved.Colliders,currentColliders);
            for(int i=0;i<mappedColliders.Length;i++)
            {
                var target=saved.Colliders[i];var current=mappedColliders[i].Collider;
                if(!ReferenceEquals(target.Collider,current)||current.GetInstanceID()!=target.InstanceId||current.enabled!=target.Enabled
                    ||current.isTrigger!=target.Trigger||current.gameObject.activeSelf!=target.ActiveSelf
                    ||current.gameObject.activeInHierarchy!=target.ActiveInHierarchy)
                    throw new InvalidOperationException("Restored delivered plate collider state differs.");
            }
            ValidatePresentationAgainstTarget(sequence,saved);
            var presentation=sequence.PresentationRenderers;
            var renderers=ComponentsRecursive<MeshRenderer>(obj.transform);
            if(renderers.Length!=saved.Renderers.Length||renderers.Length!=presentation.Length)
                throw new InvalidOperationException("Restored delivered plate renderer count differs.");
            var currentRenderers=renderers.Select(value=>CaptureRenderer(value,obj.transform,sequence.PresentationContainer)).ToArray();
            var mappedRenderers=MapRenderers(saved.Renderers,currentRenderers);
            for(int i=0;i<mappedRenderers.Length;i++)
            {
                var mapped=presentation[i];var target=saved.Renderers[i];var current=mappedRenderers[i].Renderer;var materials=current.sharedMaterials;
                if(!ReferenceEquals(mapped.Renderer,current)||current.GetInstanceID()!=mapped.InstanceId
                    ||RendererHierarchyKey(obj.transform,current)!=target.HierarchyKey||current.enabled!=target.Enabled
                    ||mappedRenderers[i].SharedMeshId!=target.SharedMeshId
                    ||!Same(mappedRenderers[i].LocalPosition,target.LocalPosition)
                    ||!Same(mappedRenderers[i].LocalRotation,target.LocalRotation)
                    ||!Same(mappedRenderers[i].LocalScale,target.LocalScale)
                    ||mappedRenderers[i].ActiveSelf!=target.ActiveSelf
                    ||mappedRenderers[i].ActiveInHierarchy!=target.ActiveInHierarchy
                    ||mappedRenderers[i].Layer!=target.Layer
                    ||materials.Length!=target.Materials.Length)throw new InvalidOperationException("Restored delivered plate renderer state differs.");
                for(int j=0;j<materials.Length;j++)
                    if(MaterialId(materials[j])!=target.MaterialIds[j]
                        ||ShaderId(materials[j])!=target.ShaderIds[j]
                        ||target.HasMode[j]&&materials[j].GetFloat("_Mode")!=target.Mode[j]
                        ||target.HasAlpha[j]&&materials[j].GetFloat("_Alpha")!=target.Alpha[j])
                        throw new InvalidOperationException("Restored delivered plate material differs from the target checkpoint.");
            }
        }

        private static void ValidatePresentationAgainstTarget(Sequence sequence,PlateState target,bool allowReincarnatedScaleCorrection=false)
        {
            var presentation=sequence.PresentationRenderers;
            if(presentation==null||presentation.Length!=target.Renderers.Length)
                throw new InvalidOperationException("Delivery pre-fade renderer presentation count differs from the target plate.");
            presentation=MapRenderers(target.Renderers,presentation);
            sequence.PresentationRenderers=presentation;
            int reincarnated=0;
            for(int i=0;i<presentation.Length;i++)
            {
                var current=presentation[i];var saved=target.Renderers[i];
                if(!Alive(current.Renderer)||current.Renderer.GetInstanceID()!=current.InstanceId
                    ||current.HierarchyKey!=saved.HierarchyKey||current.Enabled!=saved.Enabled
                    ||current.SharedMeshId!=saved.SharedMeshId
                    ||!Same(current.LocalPosition,saved.LocalPosition)||!Same(current.LocalRotation,saved.LocalRotation)
                    ||(ReferenceEquals(current.Renderer,saved.Renderer)&&!Same(current.LocalScale,saved.LocalScale))
                    ||current.ActiveSelf!=saved.ActiveSelf||current.ActiveInHierarchy!=saved.ActiveInHierarchy
                    ||current.Layer!=saved.Layer||current.Materials.Length!=saved.Materials.Length)
                    throw new InvalidOperationException("Delivery pre-fade renderer topology/state differs from the target plate.");
                bool exactRenderer=ReferenceEquals(current.Renderer,saved.Renderer)&&current.InstanceId==saved.InstanceId;
                if(!exactRenderer)
                {
                    reincarnated++;
                    if(!DeliveryFadeReincarnationContract.HasManagedReference(saved.Renderer)||Alive(saved.Renderer)
                        ||!saved.InPresentationContainer||!current.InPresentationContainer
                        ||!DeliveryFadeReincarnationContract.HasManagedReference(target.PresentationOwner)||!Alive(target.PresentationOwner)
                        ||!ReferenceEquals(sequence.PresentationOwner,target.PresentationOwner)
                        ||sequence.PresentationOwnerId!=target.PresentationOwnerId
                        ||!DeliveryFadeReincarnationContract.HasManagedReference(target.PresentationContainer)
                        ||Alive(target.PresentationContainer)||target.PresentationContainerId==0
                        ||!Alive(sequence.PresentationContainer)
                        ||sequence.PresentationContainerId==0||sequence.PresentationContainerId==target.PresentationContainerId
                        ||sequence.PresentationContainerKey!=target.PresentationContainerKey
                        ||!target.PresentationContainerPhysicsFree||!sequence.PresentationContainerPhysicsFree
                        ||current.PresentationKey!=saved.PresentationKey
                        ||current.SharedMeshId!=saved.SharedMeshId
                        ||!Same(current.LocalPosition,saved.LocalPosition)||!Same(current.LocalRotation,saved.LocalRotation)
                        ||(!Same(current.LocalScale,saved.LocalScale)
                            &&(!allowReincarnatedScaleCorrection||!BoundedScaleResidual(current.LocalScale,saved.LocalScale)))
                        ||current.ActiveSelf!=saved.ActiveSelf
                        ||current.ActiveInHierarchy!=saved.ActiveInHierarchy||current.Layer!=saved.Layer)
                        throw new InvalidOperationException("Delivery renderer replacement is not the exact physics-free ClientAttachedOrderCosmeticDecisions presentation reincarnation.");
                }
                for(int j=0;j<current.Materials.Length;j++)
                    if((exactRenderer&&current.MaterialIds[j]!=saved.MaterialIds[j])
                        ||current.ShaderIds[j]!=saved.ShaderIds[j]
                        ||current.HasMode[j]!=saved.HasMode[j]||current.HasAlpha[j]!=saved.HasAlpha[j]
                        ||current.HasMode[j]&&current.Mode[j]!=saved.Mode[j]
                        ||current.HasAlpha[j]&&current.Alpha[j]!=saved.Alpha[j])
                        throw new InvalidOperationException("Delivery pre-fade renderer material differs from the target plate at renderer "
                            +i+", material "+j+": live material/shader "+current.MaterialIds[j]+"/"+current.ShaderIds[j]
                            +", target "+saved.MaterialIds[j]+"/"+saved.ShaderIds[j]+".");
            }
            if(reincarnated!=0&&(reincarnated!=1
                ||target.Renderers.Count(value=>value.InPresentationContainer)!=1
                ||presentation.Count(value=>value.InPresentationContainer)!=1))
                throw new InvalidOperationException("Only the proven one-renderer Story 1-1 attached-order cosmetic reincarnation is supported.");
        }

        private static void ValidatePlateIncarnation(PlateState target,ClientPlate plate)
        {
            if(!Alive(target.Object)||!Alive(target.Plate)||!ReferenceEquals(target.Plate,plate)||!ReferenceEquals(target.Object,plate.gameObject)
                ||target.Object.GetInstanceID()!=target.ObjectId||target.Plate.GetInstanceID()!=target.ComponentId)
                throw new InvalidOperationException("Delivered plate incarnation differs from the target checkpoint.");
            var entry=EntitySerialisationRegistry.GetEntry((uint)target.EntityId);
            if(entry==null||!ReferenceEquals(entry.m_GameObject,target.Object))
                throw new InvalidOperationException("Delivered plate registration differs from the target checkpoint.");
        }

        private Sequence CaptureCurrentPresentation(PlateState target)
        {
            ValidatePlateIncarnation(target,target.Plate);
            var owner=target.Object.GetComponent<ClientAttachedOrderCosmeticDecisions>();
            var container=GetPresentationContainer(owner);
            var renderers=ComponentsRecursive<MeshRenderer>(target.Object.transform);
            var captured=renderers.Select(value=>CaptureRenderer(value,target.Object.transform,container)).ToArray();
            var mapped=MapRenderers(target.Renderers,captured);
            return new Sequence { Plate=target.Plate,PlateId=target.ComponentId,
                PlateObject=target.Object,PlateObjectId=target.ObjectId,
                PresentationOwner=owner,PresentationOwnerId=owner==null?0:owner.GetInstanceID(),
                PresentationContainer=container,PresentationContainerId=Alive(container)?container.GetInstanceID():0,
                PresentationContainerKey=Alive(container)?TransformHierarchyKey(target.Object.transform,container.transform):null,
                PresentationContainerPhysicsFree=Alive(container)&&PhysicsFreePresentation(container),
                PresentationRenderers=mapped };
        }

        private GameObject GetPresentationContainer(ClientAttachedOrderCosmeticDecisions owner)
        {
            return owner==null?null:attachedContainer.GetValue(owner) as GameObject;
        }

        private ExternalUiState CaptureExternalUi(GameObject plateObject)
        {
            var controller=plateObject.GetComponent<ClientIngredientContentGUI>();
            if(controller==null)return null;
            var instance=ingredientContentUiInstance.GetValue(controller) as UnityEngine.Object;
            var component=instance as Component;var obj=Alive(component)?component.gameObject:null;
            return new ExternalUiState { Controller=controller,ControllerId=controller.GetInstanceID(),ControllerType=controller.GetType().FullName,
                Instance=instance,InstanceId=Alive(instance)?instance.GetInstanceID():0,
                InstanceType=ReferenceEquals(instance,null)?null:instance.GetType().FullName,
                Object=obj,ObjectId=Alive(obj)?obj.GetInstanceID():0,Name=Alive(obj)?obj.name:null,
                Parent=Alive(obj)?obj.transform.parent:null,ParentId=Alive(obj)&&obj.transform.parent!=null?obj.transform.parent.GetInstanceID():0,
                Layer=Alive(obj)?obj.layer:0,ActiveSelf=Alive(obj)&&obj.activeSelf,ActiveInHierarchy=Alive(obj)&&obj.activeInHierarchy,
                ComponentTypes=Alive(obj)?ComponentTypeNames(obj):new string[0] };
        }

        private static void ValidateDestroyedExternalUiPreimage(ExternalUiState target)
        {
            if(target==null||ReferenceEquals(target.Controller,null)||Alive(target.Controller)||target.ControllerId==0
                ||ReferenceEquals(target.Instance,null)||Alive(target.Instance)||target.InstanceId==0
                ||ReferenceEquals(target.Object,null)||Alive(target.Object)||target.ObjectId==0
                ||ReferenceEquals(target.Parent,null)||!Alive(target.Parent)||target.Parent.GetInstanceID()!=target.ParentId
                ||target.ComponentTypes==null||target.ComponentTypes.Length==0)
                throw new InvalidOperationException("Historical ClientIngredientContentGUI hover UI is not an exact destroyed external preimage.");
        }

        private static void ValidateRecreatedExternalUi(ExternalUiState target,ExternalUiState current)
        {
            if(target==null||current==null||!Alive(current.Controller)||!Alive(current.Instance)||!Alive(current.Object)
                ||ReferenceEquals(target.Controller,current.Controller)||target.ControllerId==current.ControllerId
                ||ReferenceEquals(target.Instance,current.Instance)||target.InstanceId==current.InstanceId
                ||ReferenceEquals(target.Object,current.Object)||target.ObjectId==current.ObjectId
                ||target.ControllerType!=current.ControllerType||target.InstanceType!=current.InstanceType
                ||target.Name!=current.Name||!ReferenceEquals(target.Parent,current.Parent)||target.ParentId!=current.ParentId
                ||target.Layer!=current.Layer||target.ActiveSelf!=current.ActiveSelf
                ||target.ActiveInHierarchy!=current.ActiveInHierarchy
                ||!target.ComponentTypes.SequenceEqual(current.ComponentTypes))
                throw new InvalidOperationException("Recreated ClientIngredientContentGUI hover UI does not match the destroyed external target structure/state.");
        }

        private object RetireAbsentTargetExternalUi(PlateState target)
        {
            if(target==null||target.EntityId!=1||!Alive(target.Object)||target.ExternalUi==null)
                throw new InvalidOperationException("Absent-target hover UI retirement requires exact live entity 1 ownership.");
            var saved=target.ExternalUi;
            var current=CaptureExternalUi(target.Object);
            var component=current==null?null:current.Instance as Component;
            string[] standardComponents={typeof(RectTransform).FullName,typeof(CanvasRenderer).FullName,
                typeof(IngredientContentsUIContainer).FullName,"RectTransformExtension",
                "HoverIconUIController","UI_Move"};
            if(!NullExternalUiInstance(saved)||current==null||!Alive(saved.Controller)
                ||!ReferenceEquals(saved.Controller,current.Controller)||saved.ControllerId!=current.ControllerId
                ||!Alive(current.Instance)||!Alive(current.Object)||component==null
                ||!ReferenceEquals(component.gameObject,current.Object)
                ||current.InstanceType!=typeof(IngredientContentsUIContainer).FullName
                ||current.Name!="IngredientsContentsUI(Clone)"||current.Layer!=5
                ||current.ActiveSelf||current.ActiveInHierarchy||!Alive(current.Parent)
                ||current.Object.transform.parent!=current.Parent||current.ParentId!=current.Parent.GetInstanceID()
                ||current.Object.transform.IsChildOf(target.Object.transform)
                ||current.ComponentTypes==null||!current.ComponentTypes.SequenceEqual(standardComponents)
                ||!PhysicsFreePresentation(current.Object))
                throw new InvalidOperationException("Abandoned entity 1 hover UI is outside the exact inactive standard prefab contract.");
            int owners=Resources.FindObjectsOfTypeAll<ClientIngredientContentGUI>()
                .Count(value=>value!=null&&ReferenceEquals(ingredientContentUiInstance.GetValue(value),current.Instance));
            if(owners!=1)
                throw new InvalidOperationException("Abandoned entity 1 hover UI does not have one exact controller owner.");
            int instanceId=current.InstanceId,objectId=current.ObjectId,parentId=current.ParentId;
            ingredientContentUiInstance.SetValue(saved.Controller,null);
            if(!ReferenceEquals(ingredientContentUiInstance.GetValue(saved.Controller),null))
                throw new InvalidOperationException("Entity 1 hover UI owner field did not clear exactly.");
            UnityEngine.Object.DestroyImmediate(current.Object);
            if(Alive(current.Instance)||Alive(current.Object))
                throw new InvalidOperationException("Entity 1 abandoned hover UI survived synchronous authoring retirement.");
            var after=CaptureExternalUi(target.Object);
            if(after==null||!ReferenceEquals(after.Controller,saved.Controller)||
                after.ControllerId!=saved.ControllerId||!NullExternalUiInstance(after))
                throw new InvalidOperationException("Entity 1 hover UI did not return to the exact absent target structure.");
            return Map("controllerId",saved.ControllerId,"instanceId",instanceId,"objectId",objectId,
                "parentId",parentId,"ownerCount",owners,"physicsFree",true,
                "destroyedImmediately",true,"verified",true,
                "scope","Authoring-only retirement of the exact inactive external hover UI first created on the abandoned future; the saved f444 controller field is CLR-null and ordinary gameplay lazily recreates the standard prefab on the next non-empty contents update.");
        }

        private static bool NullExternalUiInstance(ExternalUiState value)
        {
            return value!=null&&ReferenceEquals(value.Instance,null)&&value.InstanceId==0&&value.InstanceType==null
                &&ReferenceEquals(value.Object,null)&&value.ObjectId==0&&value.Name==null
                &&ReferenceEquals(value.Parent,null)&&value.ParentId==0&&value.Layer==0
                &&!value.ActiveSelf&&!value.ActiveInHierarchy&&value.ComponentTypes!=null
                &&value.ComponentTypes.Length==0;
        }

        private ExternalUiState ValidateLiveExternalUi(PlateState target,GameObject plateObject)
        {
            var current=CaptureExternalUi(plateObject);
            var saved=target.ExternalUi;
            if(saved!=null&&current!=null&&Alive(saved.Controller)&&Alive(current.Controller)
                &&ReferenceEquals(saved.Controller,current.Controller)&&saved.ControllerId==current.ControllerId
                &&saved.ControllerType==current.ControllerType&&NullExternalUiInstance(saved)
                &&NullExternalUiInstance(current))
            {
                lastExternalUiFailure=null;
                return current;
            }
            if(saved==null||current==null||!Alive(saved.Controller)||!Alive(current.Controller)
                ||!ReferenceEquals(saved.Controller,current.Controller)||saved.ControllerId!=current.ControllerId
                ||!Alive(current.Instance)||!Alive(current.Object)
                ||saved.ControllerType!=current.ControllerType||saved.InstanceType!=current.InstanceType
                ||saved.Name!=current.Name||!ReferenceEquals(saved.Parent,current.Parent)||saved.ParentId!=current.ParentId
                ||saved.Layer!=current.Layer||saved.ActiveSelf!=current.ActiveSelf
                ||saved.ActiveInHierarchy!=current.ActiveInHierarchy
                ||!saved.ComponentTypes.SequenceEqual(current.ComponentTypes))
            {
                lastExternalUiFailure=Map("phase","structure","target",ExternalUiIdentity(saved),
                    "current",ExternalUiIdentity(current),
                    "scope","Read-only exact ClientIngredientContentGUI target/current identities captured immediately before fail-closed rejection.");
                throw new InvalidOperationException("Live ClientIngredientContentGUI hover UI does not match the target structure/state.");
            }
            if(Alive(saved.Instance))
            {
                if(!ReferenceEquals(saved.Instance,current.Instance)||saved.InstanceId!=current.InstanceId
                    ||!ReferenceEquals(saved.Object,current.Object)||saved.ObjectId!=current.ObjectId)
                {
                    lastExternalUiFailure=Map("phase","live-target-identity","target",ExternalUiIdentity(saved),
                        "current",ExternalUiIdentity(current),
                        "scope","Read-only exact ClientIngredientContentGUI target/current identities captured immediately before fail-closed rejection.");
                    throw new InvalidOperationException("A live target hover UI was unexpectedly replaced.");
                }
            }
            else if(ReferenceEquals(saved.Instance,current.Instance)||saved.InstanceId==current.InstanceId
                ||ReferenceEquals(saved.Object,current.Object)||saved.ObjectId==current.ObjectId)
            {
                lastExternalUiFailure=Map("phase","destroyed-target-identity","target",ExternalUiIdentity(saved),
                    "current",ExternalUiIdentity(current),
                    "scope","Read-only exact ClientIngredientContentGUI target/current identities captured immediately before fail-closed rejection.");
                throw new InvalidOperationException("Hover UI replacement is not a fresh standard ClientIngredientContentGUI incarnation.");
            }
            return current;
        }

        private static object ExternalUiIdentity(ExternalUiState value)
        {
            if(value==null)return null;
            return Map("controllerReference",DeliveryFadeReincarnationContract.HasManagedReference(value.Controller),
                "controllerAlive",Alive(value.Controller),"controllerId",value.ControllerId,
                "controllerLiveId",Alive(value.Controller)?value.Controller.GetInstanceID():0,"controllerType",value.ControllerType,
                "instanceReference",DeliveryFadeReincarnationContract.HasManagedReference(value.Instance),
                "instanceAlive",Alive(value.Instance),"instanceId",value.InstanceId,
                "instanceLiveId",Alive(value.Instance)?value.Instance.GetInstanceID():0,"instanceType",value.InstanceType,
                "objectReference",DeliveryFadeReincarnationContract.HasManagedReference(value.Object),
                "objectAlive",Alive(value.Object),"objectId",value.ObjectId,
                "objectLiveId",Alive(value.Object)?value.Object.GetInstanceID():0,"name",value.Name,
                "parentReference",!ReferenceEquals(value.Parent,null),"parentAlive",Alive(value.Parent),
                "parentId",value.ParentId,"parentLiveId",Alive(value.Parent)?value.Parent.GetInstanceID():0,
                "layer",value.Layer,"activeSelf",value.ActiveSelf,"activeInHierarchy",value.ActiveInHierarchy,
                "componentTypes",value.ComponentTypes);
        }

        private static void RebindLiveExternalUi(PlateState target,ExternalUiState current)
        {
            if(!ReferenceEquals(target.ExternalUi.Instance,current.Instance))RebindExternalUi(target.ExternalUi,current);
        }

        private void VerifyExternalUi(ExternalUiState saved)
        {
            if(saved==null||!Alive(saved.Controller)||saved.Controller.GetInstanceID()!=saved.ControllerId)
                throw new InvalidOperationException("Rebound ClientIngredientContentGUI controller is absent.");
            var current=CaptureExternalUi(saved.Controller.gameObject);
            if(current==null||!ReferenceEquals(current.Controller,saved.Controller)
                ||!ReferenceEquals(current.Instance,saved.Instance)||!ReferenceEquals(current.Object,saved.Object)
                ||current.InstanceId!=saved.InstanceId||current.ObjectId!=saved.ObjectId
                ||current.ControllerType!=saved.ControllerType||current.InstanceType!=saved.InstanceType
                ||current.Name!=saved.Name||!ReferenceEquals(current.Parent,saved.Parent)||current.ParentId!=saved.ParentId
                ||current.Layer!=saved.Layer||current.ActiveSelf!=saved.ActiveSelf
                ||current.ActiveInHierarchy!=saved.ActiveInHierarchy
                ||!current.ComponentTypes.SequenceEqual(saved.ComponentTypes))
                throw new InvalidOperationException("Rebound ClientIngredientContentGUI hover UI changed before checkpoint completion.");
        }

        private static void RebindExternalUi(ExternalUiState target,ExternalUiState current)
        {
            target.Controller=current.Controller;target.ControllerId=current.ControllerId;
            target.Instance=current.Instance;target.InstanceId=current.InstanceId;
            target.Object=current.Object;target.ObjectId=current.ObjectId;
            target.Parent=current.Parent;target.ParentId=current.ParentId;
        }

        private void PruneAfter(int frame)
        {
            foreach(int key in history.Keys.Where(value=>value>frame).ToArray())history.Remove(key);
            if(pending!=null)
            {
                sequences.Remove(pending.Current);
                if(pending.ActiveTargetComposite)sequences.Remove(pending.CancelCurrent);
            }
            lastFrame=frame;
        }

        private static GameObject IteratorPfx(Sequence sequence)
        {
            return Field(sequence.Iterator.GetType(),"<pfx>__1").GetValue(sequence.Iterator) as GameObject;
        }
        private static int IteratorPc(Sequence sequence) { return Convert.ToInt32(Field(sequence.Iterator.GetType(),"$PC").GetValue(sequence.Iterator)); }
        private static float IteratorProgress(Sequence sequence) { return Convert.ToSingle(Field(sequence.Iterator.GetType(),"<progress>__0").GetValue(sequence.Iterator)); }
        private static int MaterialId(Material value) { return value==null?0:value.GetInstanceID(); }
        private static int ShaderId(Material value) { return value==null||value.shader==null?0:value.shader.GetInstanceID(); }
        private static bool MaterialReferenced(Material material)
        {
            int id=MaterialId(material);
            foreach(var renderer in Resources.FindObjectsOfTypeAll<Renderer>())
                if(renderer!=null&&renderer.sharedMaterials.Any(value=>MaterialId(value)==id))return true;
            return false;
        }
        private static bool IsDescendant(Transform value,GameObject root)
        {
            if(!Alive(root))return false;for(var item=value;item!=null;item=item.parent)if(ReferenceEquals(item,root.transform))return true;return false;
        }

        private object Status(string operation)
        {
            return Map("name",Name,"operation",operation,"active",ReferenceEquals(active,this),"readOnlyDuringForwardPlay",true,
                "restoreScope","Authoring-only one live pre-destruction fade, one exact terminal Story 1-1 destroyed plate recreated through factory [34,0,0] at historical path [2], or the proven Story 1-1 f1048-to-f444 composite (cancel entity 1 and recreate entity 2 at PC2/progress 0.3). Other mid-fade targets reject. The destroyed plate's retained attachment-parent wrapper is admitted only as either the same live incarnation or a distinct live factory-recreated incarnation, then rebound with the plate. Compatible retained entity 2 histories through the target frame are rebound to the fresh incarnation; incompatible earlier sidecars are discarded fail-closed with per-frame reasons in the restore receipt. Root components, server/client synchronisers, attachment pose, renderer topology/mesh/pose/layer/active state, materials and external ingredient UI fail closed; recreated particle playback is mechanically guarded but not pixel-exact.",
                "factories",factories,"captures",captures,"duplicates",duplicates,"resets",resets,"restores",restores,
                "historyRestores",historyRestores,"lastFrame",lastFrame,
                "historyFrames",history.Keys.ToArray(),"trackedSequences",sequences.Count,"pendingNativeDeliveryFades",NativePlateLifecycle.PendingDeliveryFades,
                "deliveredObserverEntries",DeliveredCount(),"prepareMask",prepareMask,"admissionObserverRemoved",admissionObserverRemoved,
                "completeInProgress",completeInProgress,
                "pendingRestoreFrame",pending==null?-1:pending.Frame,
                "pendingHistoryRestoreFrame",pendingHistory==null?-1:pendingHistory.Frame,
                "resumePresentationPending",resumePresentationTarget!=null,
                "lastResumePresentationRestore",lastResumePresentationRestore,
                "forcedPresentationStarts",forcedPresentationStarts,
                "suppressedNaturalStarts",suppressedNaturalStarts,
                "pendingNaturalStartSuppressions",suppressedNaturalPresentationStarts.Where(value=>Alive(value.Component)).Select(value=>value.Component.GetInstanceID()).ToArray(),
                "lastPresentationLifecycleCompletion",lastPresentationLifecycleCompletion,
                "restoreReceipts",restoreReceipts.ToArray(),"failure",failure,
                "lastRendererMapFailure",lastRendererMapFailure,
                "lastRendererMaterialFailure",lastRendererMaterialFailure,
                "lastRecreatedPrefixValidation",lastRecreatedPrefixValidation,
                "lastCancellationPresentationRetirement",lastCancellationPresentationRetirement,
                "lastAbsentTargetExternalUiRetirement",lastAbsentTargetExternalUiRetirement,
                "lastExternalUiFailure",lastExternalUiFailure,
                "latest",history.Count==0?null:(object)Encode(history[history.Keys.Max()]));
        }

        private object Encode(FrameState state)
        {
            return Map("frame",state.Frame,"coreSnapshotExact",ReferenceEquals(state.CoreSnapshot,CoreSnapshot(state.Frame)),
                "pendingFades",state.PendingFades,"plateCount",state.Plates==null?0:state.Plates.Length,"sequences",state.Sequences);
        }

        private int DeliveredCount()
        {
            return delivered==null?-1:((IDictionary)delivered.GetValue(null)).Count;
        }

        private void TemporarilyRemoveAdmissionObserver()
        {
            if(preparing==null||admissionObserverRemoved)throw new InvalidOperationException("Delivery admission observer state is invalid.");
            var sequence=preparing.CancelCurrent??preparing.Current;
            var obj=sequence.Plate.gameObject;var map=(IDictionary)delivered.GetValue(null);
            if(map.Count!=1||!map.Contains(obj)||Convert.ToInt32(map[obj])!=obj.GetInstanceID())
                throw new InvalidOperationException("Delivery admission observer identity changed.");
            admissionPlate=obj;admissionInstance=obj.GetInstanceID();map.Remove(obj);admissionObserverRemoved=true;
            if(map.Count!=0)throw new InvalidOperationException("Delivery admission observer did not become temporarily empty.");
        }

        private void MaskTargetDeliveryFade(FrameState target)
        {
            if(prepareMask||maskedCoreSnapshot!=null||target==null||target.PendingFades!=1
                ||!ReferenceEquals(target.CoreSnapshot,CoreSnapshot(target.Frame))
                ||Convert.ToInt32(coreDeliveryFades.GetValue(target.CoreSnapshot))!=1)
                throw new InvalidOperationException("Delivery-fade target mask preimage differs.");
            maskedCoreSnapshot=target.CoreSnapshot;maskedDeliveryFadeCount=1;
            coreDeliveryFades.SetValue(maskedCoreSnapshot,0);prepareMask=true;
            if(Convert.ToInt32(coreDeliveryFades.GetValue(maskedCoreSnapshot))!=0)
                throw new InvalidOperationException("Delivery-fade target mask did not apply.");
        }

        private void RestoreTargetDeliveryFadeMask()
        {
            if(!prepareMask)
            {
                if(maskedCoreSnapshot!=null)throw new InvalidOperationException("Delivery-fade target mask bookkeeping is inconsistent.");
                return;
            }
            var snapshot=maskedCoreSnapshot;int value=maskedDeliveryFadeCount;
            try
            {
                if(snapshot==null)throw new InvalidOperationException("Delivery-fade target mask snapshot is absent.");
                int before=Convert.ToInt32(coreDeliveryFades.GetValue(snapshot));
                coreDeliveryFades.SetValue(snapshot,value);
                if(Convert.ToInt32(coreDeliveryFades.GetValue(snapshot))!=value)
                    throw new InvalidOperationException("Delivery-fade target mask did not restore.");
                if(before!=0)
                    throw new InvalidOperationException("Delivery-fade target mask changed before restoration; the original value was restored.");
            }
            finally
            {
                prepareMask=false;maskedCoreSnapshot=null;maskedDeliveryFadeCount=0;
            }
        }

        private void RestoreAdmissionObserver()
        {
            if(!admissionObserverRemoved)return;
            var map=(IDictionary)delivered.GetValue(null);
            try
            {
                if(map.Count!=0||map.Contains(admissionPlate))
                    throw new InvalidOperationException("Delivery admission observer cannot be restored exactly.");
                map.Add(admissionPlate,admissionInstance);
            }
            finally
            {
                admissionObserverRemoved=false;admissionPlate=null;admissionInstance=0;
            }
        }

        private object CoreSnapshot(int frame)
        {
            var values=coreHistory==null?null:coreHistory.GetValue(null) as IDictionary;
            return values!=null&&values.Contains(frame)?values[frame]:null;
        }

        private static void RequireIteratorLayout(Type type)
        {
            var required=new Dictionary<string,Type> {
                {"deliveryFx",typeof(PlateStation.DeliveryFX)},{"plate",typeof(ClientPlate)},
                {"<colliders>__0",typeof(Collider[])},{"<rigidBody>__0",typeof(Rigidbody)},
                {"<pfx>__1",typeof(GameObject)},{"<allRenderers>__0",typeof(MeshRenderer[])},
                {"<errored>__0",typeof(bool)},{"<progress>__0",typeof(float)},
                {"$this",typeof(ClientPlateStation)},{"$current",typeof(object)},
                {"$disposing",typeof(bool)},{"$PC",typeof(int)} };
            foreach(var pair in required)
            {
                var field=type.GetField(pair.Key,Instance);
                if(field==null||field.FieldType!=pair.Value)
                    throw new InvalidOperationException("Installed delivery iterator layout differs at "+pair.Key+".");
            }
        }

        private static FieldInfo Field(Type type,string name)
        {
            var value=type.GetField(name,Instance);
            if(value==null)throw new MissingFieldException(type.FullName,name);
            return value;
        }

        private static FieldInfo FindField(Type type,string name)
        {
            for(var current=type;current!=null;current=current.BaseType)
            {
                var value=current.GetField(name,Instance|BindingFlags.DeclaredOnly);
                if(value!=null)return value;
            }
            return null;
        }

        private static object StationWait(ClientPlateStation station)
        {
            var field=typeof(ClientPlateStation).GetField("m_waitForPfxDelay",Instance);
            if(field==null)throw new MissingFieldException(typeof(ClientPlateStation).FullName,"m_waitForPfxDelay");
            return field.GetValue(station);
        }

        private static T[] ComponentsRecursive<T>(Transform root) where T:Component
        {
            var values=new List<T>();var pending=new Stack<Transform>();pending.Push(root);
            while(pending.Count!=0)
            {
                var current=pending.Pop();values.AddRange(current.GetComponents<T>());
                for(int i=current.childCount-1;i>=0;i--)pending.Push(current.GetChild(i));
            }
            return values.ToArray();
        }

        private static bool PhysicsFreePresentation(GameObject root)
        {
            return Alive(root)
                &&ComponentsRecursive<Collider>(root.transform).Length==0
                &&ComponentsRecursive<Rigidbody>(root.transform).Length==0
                &&ComponentsRecursive<Animator>(root.transform).Length==0
                &&ComponentsRecursive<ServerWorldObjectSynchroniser>(root.transform).Length==0
                &&ComponentsRecursive<ClientWorldObjectSynchroniser>(root.transform).Length==0;
        }

        private static string PresentationCompositionFingerprint(GameObject root)
        {
            if(!Alive(root))return null;
            var assignable=ComponentsRecursive<AssignableOrderDefinition>(root.transform);
            if(assignable.Length!=1)return null;
            return CompositionFingerprint(assignable[0].GetOrderComposition());
        }

        private static string CompositionFingerprint(AssembledDefinitionNode value)
        {
            if(ReferenceEquals(value,null))return "clr-null";
            if(ReferenceEquals(value,AssembledDefinitionNode.NullNode))return "NullNode";
            var ingredient=value as IngredientAssembledNode;
            if(ingredient!=null)
                return "Ingredient("+(ingredient.m_ingriedientOrderNode==null?"null":ingredient.m_ingriedientOrderNode.m_uID.ToString())+")";
            var composite=value as CompositeAssembledNode;
            if(composite!=null)
                return "Composite(C=["+String.Join(",",composite.m_composition.Select(CompositionFingerprint).ToArray())+"]"
                    +",O=["+String.Join(",",composite.m_optional.Select(CompositionFingerprint).ToArray())+"])";
            return value.GetType().FullName;
        }

        private static string TransformHierarchyKey(Transform root,Transform value)
        {
            if(root==null||value==null)throw new InvalidOperationException("Transform hierarchy identity is incomplete.");
            var parts=new List<string>();var item=value;
            while(!ReferenceEquals(item,root))
            {
                if(item==null)throw new InvalidOperationException("Transform is outside the delivered plate hierarchy.");
                parts.Add(item.name+"#"+item.GetSiblingIndex());item=item.parent;
            }
            parts.Reverse();return String.Join("/",parts.ToArray());
        }

        private static string RendererHierarchyKey(Transform root,MeshRenderer renderer)
        {
            if(root==null||renderer==null)throw new InvalidOperationException("Renderer hierarchy identity is incomplete.");
            var siblings=renderer.transform.GetComponents<MeshRenderer>();
            int ordinal=Array.FindIndex(siblings,value=>ReferenceEquals(value,renderer));
            if(ordinal<0)throw new InvalidOperationException("Renderer component identity is absent from its Transform.");
            return TransformHierarchyKey(root,renderer.transform)+"@"+ordinal;
        }

        private static string ColliderHierarchyKey(Transform root,Collider collider)
        {
            if(root==null||collider==null)throw new InvalidOperationException("Collider hierarchy identity is incomplete.");
            var siblings=collider.transform.GetComponents<Collider>();
            int ordinal=Array.FindIndex(siblings,value=>ReferenceEquals(value,collider));
            if(ordinal<0)throw new InvalidOperationException("Collider component identity is absent from its Transform.");
            return TransformHierarchyKey(root,collider.transform)+"@"+ordinal;
        }

        private static string[] ComponentTypeNames(GameObject value)
        {
            return value.GetComponents<Component>().Where(component=>component!=null)
                .Select(component=>component.GetType().FullName).ToArray();
        }

        private static object ObjectIdentity(object value)
        {
            if(value==null)return null;
            var unity=value as UnityEngine.Object;
            bool isUnity=!ReferenceEquals(unity,null),alive=!isUnity||Alive(unity);
            return Map("type",value.GetType().FullName,"instanceId",isUnity&&alive?unity.GetInstanceID():0,
                "alive",alive,"name",isUnity&&alive?unity.name:null);
        }

        private static bool Alive(UnityEngine.Object value) { return !ReferenceEquals(value,null)&&value!=null; }
        private static bool Finite(float value) { return !Single.IsNaN(value)&&!Single.IsInfinity(value); }
        private static bool BoundedScaleResidual(Vector3 current,Vector3 target)
        {
            return DeliveryFadeReincarnationContract.IsBoundedPresentationScale(
                current.x,current.y,current.z,target.x,target.y,target.z);
        }
        private static bool Same(Vector3 a,Vector3 b) { return a.x==b.x&&a.y==b.y&&a.z==b.z; }
        private static bool Same(Quaternion a,Quaternion b) { return a.x==b.x&&a.y==b.y&&a.z==b.z&&a.w==b.w; }
        private static object Point(Vector3 value) { return new[]{value.x,value.y,value.z}; }
        private static object Rotation(Quaternion value) { return new[]{value.x,value.y,value.z,value.w}; }
        private static string PathOf(Transform value)
        {
            var names=new List<string>();for(var item=value;item!=null;item=item.parent)names.Add(item.name);
            names.Reverse();return String.Join("/",names.ToArray());
        }
        private static Dictionary<string,object> Map(params object[] values)
        {
            var result=new Dictionary<string,object>();for(int i=0;i<values.Length;i+=2)result.Add((string)values[i],values[i+1]);return result;
        }

        private void Clear()
        {
            RequireFence();
            suppressedNaturalPresentationStarts.RemoveAll(value=>!Alive(value.Component));
            if(suppressedNaturalPresentationStarts.Count!=0)
                throw new InvalidOperationException("Cannot clear while an exact Unity presentation Start suppression is pending.");
            RestoreTargetDeliveryFadeMask();
            RestoreAdmissionObserver();
            history.Clear();sequences.Clear();restoreReceipts.Clear();lastFrame=-1;failure=null;
            completeInProgress=false;preparing=null;pending=null;
            preparingHistory=null;pendingHistory=null;
            resumePresentationTarget=null;resumePresentationCurrent=null;
            resumeActiveSequence=null;resumeActiveReceipt=null;lastResumePresentationRestore=null;
            lastPresentationLifecycleCompletion=null;lastRendererMapFailure=null;lastRendererMaterialFailure=null;
            lastCancellationPresentationRetirement=null;lastAbsentTargetExternalUiRetirement=null;
            lastExternalUiFailure=null;
        }

        private static void RequireFence()
        {
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main)||!NativeSessionBridge.InputBlocked)
                throw new InvalidOperationException("Delivery fade checkpoint activation requires the authoring pause fence.");
        }

        private void Deactivate()
        {
            suppressedNaturalPresentationStarts.RemoveAll(value=>!Alive(value.Component));
            if(suppressedNaturalPresentationStarts.Count!=0)
                throw new InvalidOperationException("Cannot deactivate while an exact Unity presentation Start suppression is pending.");
            RestoreTargetDeliveryFadeMask();
            RestoreAdmissionObserver();
            if(harmony!=null)harmony.UnpatchSelf();harmony=null;
            if(ReferenceEquals(active,this))active=null;
            history.Clear();sequences.Clear();lastFrame=-1;completeInProgress=false;preparing=null;pending=null;
            preparingHistory=null;pendingHistory=null;
            resumePresentationTarget=null;resumePresentationCurrent=null;resumeActiveSequence=null;resumeActiveReceipt=null;
        }

        public void Dispose()
        {
            if(disposed)return;Deactivate();disposed=true;
        }
    }
}
