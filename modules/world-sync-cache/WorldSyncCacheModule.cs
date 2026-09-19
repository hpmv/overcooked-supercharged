using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using WarpSpec = Hpmv.WarpSpec;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // This external authoring sidecar restores observed native cache state. It
    // neither suppresses a message nor rewrites a native clock or pose.
    public sealed class WorldSyncCacheModule : IAuthoringModule
    {
        private sealed class Item
        {
            internal int Id, ObjectId, SyncId, ParentId;
            internal bool Dynamic, LooseDynamic;
            internal GameObject Object;
            internal ServerWorldObjectSynchroniser Sync;
            internal Transform Transform, Parent;
            internal WorldObjectMessage OriginalMessage, Message;
            internal Vector3 LocalPosition;
            internal Quaternion LocalRotation;
            internal object[] Values;
            internal NativeClientWorldSnapshot Client;
            internal float CaptureLogicalTime, RestResidual;
            internal bool PendingRest, Restored;
        }
        private sealed class Saved
        {
            internal int Frame;
            internal object Snapshot;
            internal Item[] Items;
            internal float LogicalTime;
            internal string Unsupported;
            internal NativeSchedulerSnapshot Scheduler;
        }
        private sealed class PriorCapture { internal object Snapshot; }
        private static readonly string[] cacheNames = {
            "m_CachedParentTransform", "m_LastUnreliableActiveSend", "m_bSentReliableRestPosition",
            "m_bStartedSynchronising", "m_bSleepAllowed", "m_bActive", "m_bSyncPositions", "m_bParentChanged", "m_bPaused"
        };
        private static readonly Type[] cacheTypes = { typeof(Transform), typeof(float), typeof(bool),
            typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool) };
        private static WorldSyncCacheModule active;
        private Harmony harmony;
        private readonly Dictionary<int, Saved> saved = new Dictionary<int, Saved>();
        private object round;
        private FieldInfo historyField, roundField, initialField, fixedAttachmentsField, snapshotLogicalTime, planSnapshot, transformField, messageField;
        private FieldInfo[] cacheFields;
        private MethodInfo capture, prepare, complete, restoreFailure, resume, dynamicSpawn, lateUpdate;
        private Saved pendingDynamicRestore;
        private Saved pendingPausedDynamicTransforms;
        private Saved pendingResumeValidation;
        private Saved resumeValidationTarget;
        private bool resumeValidationInFlight;
        private int referenceResumeValidations, warpResumeValidations;
        private int pausedDynamicTransformCorrections;
        private int captures, restores, rejected, dynamicRestores, survivingDynamicRestores, lastFrame = -1;
        private bool disposed;
        private string lastError;
        private object lastRestore;
        public string Name { get { return "native-world-sync-cache-v19-local-recreated-active-rest"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation, Dictionary<string, object> args)
        {
            if (disposed) throw new ObjectDisposedException("WorldSyncCacheModule");
            if (args != null && args.Count != 0) throw new ArgumentException("World-sync operations take no arguments.");
            if (operation == "activate") Activate();
            else if (operation == "deactivate") Deactivate();
            else if (operation != "status") throw new ArgumentException("Unknown world-sync operation.");
            Saved latest; saved.TryGetValue(lastFrame, out latest);
            return new Dictionary<string, object> {
                {"name",Name},{"apiVersion",1},{"active",ReferenceEquals(active,this)},
                {"frames",saved.Count},{"captures",captures},{"restores",restores},{"rejected",rejected},
                {"lastFrame",lastFrame},{"lastError",lastError},{"lastRestore",lastRestore},
                {"pendingResumeValidation",pendingResumeValidation==null?null:(object)pendingResumeValidation.Frame},
                {"resumeValidationInFlight",resumeValidationInFlight},{"referenceResumeValidations",referenceResumeValidations},
                {"warpResumeValidations",warpResumeValidations},
                {"dynamicRestores",dynamicRestores},{"survivingDynamicRestores",survivingDynamicRestores},
                {"pendingDynamicRestore",pendingDynamicRestore==null?null:(object)pendingDynamicRestore.Frame},
                {"pendingPausedDynamicTransforms",pendingPausedDynamicTransforms==null?null:(object)pendingPausedDynamicTransforms.Frame},
                {"pausedDynamicTransformCorrections",pausedDynamicTransformCorrections},
                {"latest",Describe(latest)},
                {"scope","Initial fixed and admitted dynamic PhysicalAttachment WorldObject caches, exact logical-clock rest timestamps, and exact scheduler cadence/order/urgent/free-ID state. The core substitutes its checkpointed logical clock for both native GetServerUpdate Time.time reads; the strict deadline test, payload, event emission, client handling and all poses remain native. Every admitted settled dynamic PhysicalAttachment checkpoints its client/server parent caches, empty attachment-container Rigidbody/Transform poses, and exact logical owner pose/prediction flags. A local-only owner proven by the requested warp to undergo exact transactional recreation may also retain its exact active pending-rest cache; this preserves local continuation while making no claim about remote packet parity. Attached items require a surviving registered station/chef parent. Loose dynamic items require the exact surviving owner/container incarnation and physical-container parenting; the exact initial delivery plate may instead rebind to its recreated historical container. After rewind only, exact dynamic container Transforms are retained across frozen authoring maintenance while Rigidbody pose/motion/settings remain unchanged. Authenticated replacement pairs are recreated and rebound under their historical owner/body IDs by a transactional rewind-only allocator reservation. All other missing, ambiguous or changed memberships fail closed."}
            };
        }
        private void Activate()
        {
            if (!TimeManager.IsPaused(TimeManager.PauseLayer.Main)) throw new InvalidOperationException("World-sync activation requires native pause.");
            if (harmony != null) return;
            var stat = BindingFlags.Static | BindingFlags.NonPublic;
            var inst = BindingFlags.Instance | BindingFlags.NonPublic;
            historyField = typeof(NativeKitchenCheckpoint).GetField("history", stat);
            roundField = typeof(NativeKitchenCheckpoint).GetField("roundIdentity", stat);
            initialField = typeof(NativeSceneMetadata).GetField("InitialPhysicalAttachmentIds", stat);
            var snapshotType=typeof(NativeKitchenCheckpoint).GetNestedType("Snapshot",BindingFlags.NonPublic);
            fixedAttachmentsField=snapshotType==null?null:snapshotType.GetField("FixedAttachmentPoses",inst);
            snapshotLogicalTime=snapshotType==null?null:snapshotType.GetField("LogicalRealtime",inst);
            planSnapshot = typeof(NativeKitchenCheckpoint.RestorePlan).GetField("snapshot", inst);
            capture = AccessTools.Method(typeof(NativeKitchenCheckpoint), "CaptureFrame", new[] { typeof(int) });
            prepare = AccessTools.Method(typeof(NativeKitchenCheckpoint), "Prepare", new[] { typeof(WarpSpec) });
            complete = AccessTools.Method(typeof(NativeKitchenCheckpoint.RestorePlan), "Complete", Type.EmptyTypes);
            restoreFailure=AccessTools.Method(typeof(NativeKitchenCheckpoint),"RecordRestoreFailure",new[]{typeof(int),typeof(Exception),typeof(bool)});
            Type helpers=typeof(NativeKitchenCheckpoint).Assembly.GetType("SuperchargedPatch.Helpers",true);
            resume=AccessTools.Method(helpers,"Resume",Type.EmptyTypes);
            dynamicSpawn=AccessTools.Method(typeof(NativeDynamicWarpPlan),"Spawn",Type.EmptyTypes);
            Type patcher=typeof(NativeKitchenCheckpoint).Assembly.GetType("SuperchargedPatch.TASPatcher",true);
            lateUpdate=AccessTools.Method(patcher,"LateUpdate",Type.EmptyTypes);
            transformField = AccessTools.Field(typeof(ServerWorldObjectSynchroniser), "m_Transform");
            messageField = AccessTools.Field(typeof(ServerWorldObjectSynchroniser), "m_ServerData");
            cacheFields = cacheNames.Select(n => AccessTools.Field(typeof(ServerWorldObjectSynchroniser), n)).ToArray();
            NativeSchedulerSnapshot.ValidateContract();
            NativeClientWorldSnapshot.ValidateContract();
            if (historyField == null || !typeof(IDictionary).IsAssignableFrom(historyField.FieldType) || roundField == null
                || initialField == null || initialField.FieldType != typeof(HashSet<int>) || planSnapshot == null
                || fixedAttachmentsField==null||fixedAttachmentsField.FieldType!=typeof(NativeAttachmentPoseCheckpoint.Snapshot[])
                || snapshotLogicalTime==null||snapshotLogicalTime.FieldType!=typeof(float)
                || capture == null || prepare == null || complete == null || restoreFailure == null || resume == null || resume.ReturnType!=typeof(void)
                || dynamicSpawn==null||dynamicSpawn.ReturnType!=typeof(void)||lateUpdate==null||lateUpdate.ReturnType!=typeof(void)
                || transformField == null || transformField.FieldType != typeof(Transform)
                || messageField == null || messageField.FieldType != typeof(WorldObjectMessage)
                || cacheFields.Where((f,i) => f == null || f.FieldType != cacheTypes[i]).Any())
                throw new InvalidOperationException("Frozen native/core world-sync checkpoint contract differs.");
            var installed = Harmony.GetPatchInfo(capture);
            if (active != null || (installed != null && installed.Postfixes.Any(p => p.owner.StartsWith("supercharged.authoring.world-sync.", StringComparison.Ordinal))))
                throw new InvalidOperationException("Another world-sync module is still active.");
            harmony = new Harmony("supercharged.authoring.world-sync." + GetType().Assembly.GetName().Name);
            active = this;
            try
            {
                harmony.Patch(capture, prefix: Hook("BeforeCaptureFrame"), postfix: Hook("AfterCaptureFrame"));
                harmony.Patch(prepare, prefix: Hook("BeforePrepare"));
                harmony.Patch(complete, postfix: Hook("AfterComplete"));
                harmony.Patch(restoreFailure,postfix:Hook("AfterRestoreFailure"));
                harmony.Patch(dynamicSpawn,prefix:Hook("BeforeDynamicSpawn"),finalizer:Hook("FinalizeDynamicSpawn"));
                var latePrefix=Hook("BeforeTasLateUpdate");latePrefix.priority=Priority.Last;
                harmony.Patch(lateUpdate,prefix:latePrefix);
                var resumePrefix=Hook("BeforeAuthoringResume");var resumePostfix=Hook("AfterAuthoringResume");
                resumePrefix.priority=Priority.Last;resumePostfix.priority=Priority.Last;
                var resumePatches=Harmony.GetPatchInfo(resume);
                if(resumePatches!=null)
                    resumePrefix.after=resumePatches.Prefixes.Select(p=>p.owner)
                        .Where(owner=>owner.StartsWith("supercharged.authoring.chef-animator-checkpoint.",StringComparison.Ordinal))
                        .Distinct().ToArray();
                harmony.Patch(resume,prefix:resumePrefix,postfix:resumePostfix,finalizer:Hook("FinalizeAuthoringResume"));
            }
            catch { Deactivate(); throw; }
        }
        private HarmonyMethod Hook(string name) { return new HarmonyMethod(GetType().GetMethod(name,BindingFlags.Public|BindingFlags.Static)); }
        public static void BeforeCaptureFrame(int __0, out object __state)
        {
            __state=null;
            var module=active;
            if(module==null)return;
            try { __state=new PriorCapture {Snapshot=((IDictionary)module.historyField.GetValue(null))[__0]}; }
            catch(Exception error) { module.lastError="Capture prefix: "+error.Message; }
        }
        public static void AfterCaptureFrame(int __0, object __state)
        {
            var module = active;
            var prior=__state as PriorCapture;
            if (module == null || prior==null) return;
            // Unsupported metadata remains diagnostic until that target is
            // requested. Ordinary capture/physics must continue unchanged.
            try { module.Capture(__0,prior.Snapshot); }
            catch (Exception error) { module.lastError = "Capture: " + error.Message; }
        }
        private void Capture(int frame,object priorSnapshot)
        {
            object identity = roundField.GetValue(null);
            if (!ReferenceEquals(identity,round)) { saved.Clear(); pendingDynamicRestore=null; pendingPausedDynamicTransforms=null; pendingResumeValidation=null; resumeValidationTarget=null; resumeValidationInFlight=false; round=identity; lastFrame=-1; }
            var history = (IDictionary)historyField.GetValue(null);
            object native = history[frame];
            if (native == null) return; // Core rejected/has not captured this boundary.
            Saved previous;
            if (saved.TryGetValue(frame,out previous) && ReferenceEquals(previous.Snapshot,native)) return;
            // An existing native boundary may predate module activation. Its
            // historical cache cannot be inferred from a later paused poll.
            if(ReferenceEquals(priorSnapshot,native))return;
            if (saved.Count >= 20000) { lastError="World-sync capture bound reached."; return; }
            var value = new Saved {Frame=frame, Snapshot=native, LogicalTime=UnrealTimePatch.CaptureLogicalRealtime()};
            try
            {
                if(Bits((float)snapshotLogicalTime.GetValue(native))!=Bits(value.LogicalTime))
                    throw new InvalidOperationException("Core and WorldObject logical checkpoint clocks differ.");
                var initialIds = (HashSet<int>)initialField.GetValue(null);
                if (initialIds.Count == 0) throw new InvalidOperationException("No observed initial attachment set.");
                var fixedRows=fixedAttachmentsField.GetValue(native) as NativeAttachmentPoseCheckpoint.Snapshot[];
                if(fixedRows==null||fixedRows.Any(row=>row==null))
                    throw new InvalidOperationException("Core fixed-attachment membership is unavailable.");
                var ids=new HashSet<int>(fixedRows.Select(row=>row.EntityId));
                if(ids.Count!=fixedRows.Length||ids.Any(id=>!initialIds.Contains(id)))
                    throw new InvalidOperationException("Core fixed-attachment membership is not a unique subset of the initial set.");
                value.Scheduler=NativeSchedulerSnapshot.Capture(new HashSet<int>(ids));
                var dynamicIds=new HashSet<int>(value.Scheduler.DynamicOwnerIds);
                value.Items = ids.Concat(dynamicIds).Distinct().OrderBy(i=>i)
                    .Select(id=>CaptureItem(id,value.LogicalTime,dynamicIds.Contains(id))).ToArray();
                // Capture cannot know a future WarpSpec yet. Retain the exact
                // active pending-rest state for every candidate attachment,
                // then require the requested warp to prove that this specific
                // owner is recreated before any restore mutation is admitted.
                foreach (var item in value.Items)RequireSupported(item,true);
            }
            catch (Exception error) { value.Unsupported=error.Message; }
            saved[frame]=value; lastFrame=frame; captures++;
        }
        private Item CaptureItem(int id,float captureLogicalTime,bool dynamic=false)
        {
            var entry=EntitySerialisationRegistry.GetEntry((uint)id);
            if (entry == null || entry.m_GameObject == null) throw new InvalidOperationException("Missing attachment " + id);
            var obj=entry.m_GameObject;
            var components=obj.GetComponents<ServerWorldObjectSynchroniser>();
            if (obj.GetComponent<PhysicalAttachment>() == null || components.Length != 1 || components[0].GetType()!=typeof(ServerWorldObjectSynchroniser))
                throw new InvalidOperationException("Unsupported native world-sync role " + id);
            var sync=components[0]; var transform=(Transform)transformField.GetValue(sync);
            var message=(WorldObjectMessage)messageField.GetValue(sync);
            if (transform == null || transform != obj.transform || message == null || message.GetType()!=typeof(WorldObjectMessage))
                throw new InvalidOperationException("Missing native world-sync cache " + id);
            var copy=new WorldObjectMessage(); copy.Copy(message);
            var item=new Item { Id=id,Dynamic=dynamic,Object=obj,ObjectId=obj.GetInstanceID(),Sync=sync,SyncId=sync.GetInstanceID(),
                Transform=transform,Parent=transform.parent,ParentId=transform.parent==null?0:transform.parent.GetInstanceID(),
                OriginalMessage=message,Message=copy,LocalPosition=transform.localPosition,LocalRotation=transform.localRotation,
                Client=new NativeClientWorldSnapshot(id,obj),
                Values=cacheFields.Select(f=>f.GetValue(sync)).ToArray(),CaptureLogicalTime=captureLogicalTime };
            if(dynamic)
            {
                var physical=obj.GetComponent<PhysicalAttachment>();
                var serverPhysical=obj.GetComponent<ServerPhysicalAttachment>();
                var clientPhysical=obj.GetComponent<ClientPhysicalAttachment>();
                bool serverAttached=serverPhysical!=null&&serverPhysical.IsAttached();
                bool clientAttached=clientPhysical!=null&&clientPhysical.IsAttached();
                if(serverPhysical==null||clientPhysical==null||serverAttached!=clientAttached)
                    throw new InvalidOperationException("Dynamic WorldObject attachment state differs at entity "+id);
                if(!serverAttached)
                {
                    var containerObject=physical==null||physical.m_container==null?null:physical.m_container.gameObject;
                    var containerEntry=containerObject==null?null:EntitySerialisationRegistry.GetEntry(containerObject);
                    var containerParent=containerObject==null?null:containerObject.GetComponent<ObjectContainer>();
                    if(containerEntry==null||containerParent==null||!ReferenceEquals(item.Parent,containerObject.transform)
                        ||containerObject.transform.parent!=null||!item.Message.HasParent
                        ||item.Message.ParentEntityID!=containerEntry.m_Header.m_uEntityID)
                        throw new InvalidOperationException("Loose dynamic WorldObject is not settled on its registered container at entity "+id);
                    item.Client.RequireExactParent(containerEntry,containerParent);
                    item.LooseDynamic=true;
                }
            }
            item.PendingRest=!(bool)item.Values[2];
            if(item.PendingRest)item.RestResidual=(((float)item.Values[1]+1f)-captureLogicalTime);
            return item;
        }
        internal static bool IsSupportedCacheLifecycle(bool sentReliable,bool started,bool sleepAllowed,
            bool active,bool syncPositions,bool parentChanged,bool paused,bool localDynamicRecreation)
        {
            if(!started||!sleepAllowed||paused)return false;
            if(sentReliable)return !active&&!parentChanged;
            return syncPositions&&parentChanged&&(!active||localDynamicRecreation);
        }
        private static void RequireSupported(Item item,bool localDynamicRecreation=false)
        {
            bool sentReliable=(bool)item.Values[2],started=(bool)item.Values[3],sleepAllowed=(bool)item.Values[4];
            bool active=(bool)item.Values[5],parentChanged=(bool)item.Values[7],paused=(bool)item.Values[8];
            if (!started || !sleepAllowed || paused)
                throw new InvalidOperationException("Unsupported native WorldObject lifecycle at entity " + item.Id);
            bool syncPositions=(bool)item.Values[6];
            if(sentReliable&&(active||parentChanged))
                throw new InvalidOperationException("Inconsistent completed WorldObject rest state at entity " + item.Id);
            if(!IsSupportedCacheLifecycle(sentReliable,started,sleepAllowed,active,syncPositions,parentChanged,
                    paused,localDynamicRecreation))
                throw new InvalidOperationException("Unsupported non-quiescent pending WorldObject rest state at entity " + item.Id);
            if (!ReferenceEquals(item.Values[0],item.Parent) || !Finite((float)item.Values[1])
                || !Finite(item.Message.LocalPosition) || !Finite(item.Message.LocalRotation)
                || !Finite(item.LocalPosition) || !Finite(item.LocalRotation))
                throw new InvalidOperationException("Invalid WorldObject cache at entity " + item.Id);
            if(item.PendingRest&&(!Finite(item.RestResidual)||item.RestResidual>1.0001f||item.RestResidual<-.1001f))
                throw new InvalidOperationException("Invalid pending WorldObject rest deadline at entity " + item.Id);
        }
        public static void BeforePrepare(WarpSpec __0)
        {
            if (active == null) return;
            active.pendingDynamicRestore=null;
            try { var target=active.ValidateTarget(__0); if(target.Scheduler.DynamicPending)active.pendingDynamicRestore=target; }
            catch(Exception error) { active.rejected++; active.lastError=error.Message; throw; }
        }
        private Saved ValidateTarget(WarpSpec warp)
        {
            Saved target;
            if (warp == null || !saved.TryGetValue(warp.Frame,out target)
                || !ReferenceEquals(round,roundField.GetValue(null))
                || !ReferenceEquals(target.Snapshot,((IDictionary)historyField.GetValue(null))[warp.Frame]))
                throw new InvalidOperationException("No exact observed WorldObject cache for requested native checkpoint.");
            if (target.Unsupported != null) throw new InvalidOperationException("Unsupported WorldObject checkpoint: " + target.Unsupported);
            target.Scheduler.ValidateBeforeRestore(warp);
            foreach (var item in target.Items)
            {
                bool rebindOwner=target.Scheduler.WillRebindOwner(item.Id);
                RequireSupported(item,rebindOwner);
                if(rebindOwner)continue;
                EntitySerialisationEntry historicalParent;Transform historicalTransform;
                if(target.Scheduler.TryGetHistoricalParentRebind(item.Id,out historicalParent,out historicalTransform))
                    ValidateIdentityForParentRebind(item,historicalParent,historicalTransform);
                else ValidateIdentity(item,false);
            }
            return target;
        }
        private void ValidateIdentityForParentRebind(Item item,EntitySerialisationEntry historicalParent,
            Transform historicalTransform)
        {
            item.Client.ValidateForParentRebind(historicalParent);
            var entry=EntitySerialisationRegistry.GetEntry((uint)item.Id);
            if(entry==null||!ReferenceEquals(entry.m_GameObject,item.Object)||item.Object==null
                ||item.Object.GetInstanceID()!=item.ObjectId||item.Sync==null||item.Sync.GetInstanceID()!=item.SyncId
                ||!ReferenceEquals(item.Object.GetComponent<ServerWorldObjectSynchroniser>(),item.Sync)
                ||!ReferenceEquals(transformField.GetValue(item.Sync),item.Transform)
                ||!ReferenceEquals(messageField.GetValue(item.Sync),item.OriginalMessage)
                ||historicalParent==null||historicalParent.m_Header.m_uEntityID==0||ReferenceEquals(historicalTransform,null)
                ||!ReferenceEquals(item.Parent,historicalTransform)||!ReferenceEquals(item.Values[0],historicalTransform)
                ||!item.Message.HasParent||item.Message.ParentEntityID!=historicalParent.m_Header.m_uEntityID)
                throw new InvalidOperationException("WorldObject dependent parent is not the admitted historical incarnation: "+item.Id);
        }
        private void ValidateIdentity(Item item,bool requirePose)
        {
            item.Client.Validate();
            var entry=EntitySerialisationRegistry.GetEntry((uint)item.Id);
            if (entry == null || !ReferenceEquals(entry.m_GameObject,item.Object) || item.Object == null || item.Object.GetInstanceID()!=item.ObjectId
                || item.Sync == null || item.Sync.GetInstanceID()!=item.SyncId || !ReferenceEquals(item.Object.GetComponent<ServerWorldObjectSynchroniser>(),item.Sync)
                || !ReferenceEquals(transformField.GetValue(item.Sync),item.Transform) || !ReferenceEquals(messageField.GetValue(item.Sync),item.OriginalMessage)
                || (item.ParentId!=0 && (item.Parent==null || item.Parent.GetInstanceID()!=item.ParentId)))
                throw new InvalidOperationException("WorldObject incarnation changed: " + item.Id);
            if (requirePose && (!ReferenceEquals(item.Transform.parent,item.Parent)
                || !Exact(item.Transform.localPosition,item.LocalPosition) || !Exact(item.Transform.localRotation,item.LocalRotation)))
                throw new InvalidOperationException("Native attachment pose must already match before WorldObject cache restore: " + item.Id);
        }
        private Item RebindItem(Item target,EntitySerialisationEntry replacementParent=null,
            Transform replacementTransform=null)
        {
            var entry=EntitySerialisationRegistry.GetEntry((uint)target.Id);
            var obj=entry==null?null:entry.m_GameObject;
            var components=obj==null?new ServerWorldObjectSynchroniser[0]:obj.GetComponents<ServerWorldObjectSynchroniser>();
            if(obj==null||obj.GetComponent<PhysicalAttachment>()==null||components.Length!=1
                ||components[0].GetType()!=typeof(ServerWorldObjectSynchroniser))
                throw new InvalidOperationException("Recreated dynamic WorldObject role differs: "+target.Id);
            var sync=components[0];var transform=(Transform)transformField.GetValue(sync);
            var message=(WorldObjectMessage)messageField.GetValue(sync);
            if(transform==null||transform!=obj.transform||message==null||message.GetType()!=typeof(WorldObjectMessage))
                throw new InvalidOperationException("Recreated dynamic WorldObject cache differs: "+target.Id);
            bool rebindParent=replacementParent!=null||replacementTransform!=null;
            if(rebindParent&&(replacementParent==null||replacementParent.m_GameObject==null
                ||replacementParent.m_Header.m_uEntityID==0||replacementTransform==null
                ||!HasAncestor(replacementTransform,replacementParent.m_GameObject.transform)
                ||!target.Message.HasParent||target.Message.ParentEntityID!=replacementParent.m_Header.m_uEntityID))
                throw new InvalidOperationException("Recreated dynamic WorldObject parent differs: "+target.Id);
            var values=(object[])target.Values.Clone();if(rebindParent)values[0]=replacementTransform;
            return new Item {Id=target.Id,Dynamic=target.Dynamic,LooseDynamic=target.LooseDynamic,Object=obj,ObjectId=obj.GetInstanceID(),Sync=sync,SyncId=sync.GetInstanceID(),
                Transform=transform,Parent=rebindParent?replacementTransform:target.Parent,
                ParentId=rebindParent?replacementTransform.GetInstanceID():target.ParentId,OriginalMessage=message,Message=target.Message,
                LocalPosition=target.LocalPosition,LocalRotation=target.LocalRotation,Values=values,
                Client=rebindParent?target.Client.Rebind(obj,replacementParent):target.Client.Rebind(obj),CaptureLogicalTime=target.CaptureLogicalTime,RestResidual=target.RestResidual,
                PendingRest=target.PendingRest,
                Restored=target.Restored};
        }
        public static void AfterComplete(NativeKitchenCheckpoint.RestorePlan __instance)
        {
            if (active == null) return;
            active.Restore(__instance);
        }
        private void Restore(NativeKitchenCheckpoint.RestorePlan plan)
        {
            object native=planSnapshot.GetValue(plan);
            var target=saved.Values.SingleOrDefault(s=>ReferenceEquals(s.Snapshot,native));
            if (target == null || target.Unsupported != null) throw new InvalidOperationException("Completed warp has no supported exact WorldObject cache.");
            float restoreLogicalTime=UnrealTimePatch.CaptureLogicalRealtime();
            if(Bits(restoreLogicalTime)!=Bits(target.LogicalTime))
                throw new InvalidOperationException("Restored WorldObject logical clock differs from checkpoint.");
            // Fixed identities must still validate before the first dynamic
            // body/pose write. Dynamic owner pose is then restored by the
            // scheduler sidecar and a recreated incarnation is rebound before
            // its value-only WorldObject cache is written.
            foreach(var item in target.Items.Where(item=>!item.Dynamic&&!target.Scheduler.WillRebindOwner(item.Id)))ValidateIdentity(item,true);
            survivingDynamicRestores+=target.Scheduler.RestoreDynamicAfterCore();
            for(int i=0;i<target.Items.Length;i++)
            {
                var item=target.Items[i];
                EntitySerialisationEntry replacementParent;Transform replacementTransform;
                bool rebindParent=target.Scheduler.TryGetCurrentParentRebind(item.Id,out replacementParent,out replacementTransform);
                var entry=EntitySerialisationRegistry.GetEntry((uint)item.Id);
                if(entry==null||!ReferenceEquals(entry.m_GameObject,item.Object))
                {
                    if(!target.Scheduler.WillRebindOwner(item.Id))
                        throw new InvalidOperationException("WorldObject incarnation changed without an admitted rebind: "+item.Id);
                    item=RebindItem(item,rebindParent?replacementParent:null,rebindParent?replacementTransform:null);
                    target.Items[i]=item;continue;
                }
                if(rebindParent)
                {
                    RebindItemParent(item,replacementParent,replacementTransform);
                    continue;
                }
            }
            foreach(var item in target.Items)ValidateIdentity(item,true);
            target.Scheduler.Validate();
            var clientsBefore=target.Items.Select(i=>i.Client.DescribeCurrent()).ToArray();
            var restoredValues=target.Items.Select(item=>(object[])item.Values.Clone()).ToArray();
            foreach(var item in target.Items)
            {
                item.OriginalMessage.Copy(item.Message);
                item.Restored=true;
                int itemIndex=Array.IndexOf(target.Items,item);
                for(int i=0;i<cacheFields.Length;i++) cacheFields[i].SetValue(item.Sync,restoredValues[itemIndex][i]);
                item.Client.Restore();
            }
            foreach(var item in target.Items)
            {
                var observed=CaptureItem(item.Id,restoreLogicalTime,item.Dynamic);
                if(!SameMessage(observed.Message,item.Message) || !observed.Values.SequenceEqual(item.Values))
                    throw new InvalidOperationException("WorldObject cache restore verification failed: " + item.Id);
            }
            target.Scheduler.Restore();
            if(target.Scheduler.DynamicPending){target.Scheduler.FinishDynamicRestore();dynamicRestores++;}
            pendingPausedDynamicTransforms=target;
            pendingDynamicRestore=null;
            foreach(int frame in saved.Keys.Where(f=>f>target.Frame).ToArray()) saved.Remove(frame);
            lastFrame=target.Frame; restores++; lastError=null;
            pendingResumeValidation=target;
            lastRestore=new Dictionary<string,object>{{"frame",target.Frame},{"count",target.Items.Length},{"verified",true},
                {"clientsBefore",clientsBefore},{"clientsAfter",target.Items.Select(i=>i.Client.DescribeCurrent()).ToArray()},
                {"capturedLogicalTime",target.LogicalTime},{"restoreLogicalTime",restoreLogicalTime},
                {"exactRestTimestamps",target.Items.Count(i=>i.PendingRest)},{"items",Describe(target)}};
        }
        private static void RebindItemParent(Item item,EntitySerialisationEntry replacementParent,
            Transform replacementTransform)
        {
            if(item==null||replacementParent==null||replacementParent.m_GameObject==null
                ||replacementParent.m_Header.m_uEntityID==0||replacementTransform==null
                ||!HasAncestor(replacementTransform,replacementParent.m_GameObject.transform)
                ||!item.Message.HasParent||item.Message.ParentEntityID!=replacementParent.m_Header.m_uEntityID)
                throw new InvalidOperationException("Recreated WorldObject parent differs: "+(item==null?0:item.Id));
            var client=item.Client.RebindParent(replacementParent);
            var values=(object[])item.Values.Clone();values[0]=replacementTransform;
            item.Parent=replacementTransform;item.ParentId=replacementTransform.GetInstanceID();
            item.Values=values;item.Client=client;
        }
        private static bool HasAncestor(Transform child,Transform parent)
        {
            for(var value=child;value!=null;value=value.parent)if(ReferenceEquals(value,parent))return true;
            return false;
        }
        public static void AfterRestoreFailure()
        {
            var module=active;if(module!=null){if(module.pendingDynamicRestore!=null)module.pendingDynamicRestore.Scheduler.AbortDynamicRestore();module.pendingDynamicRestore=null;module.pendingPausedDynamicTransforms=null;module.pendingResumeValidation=null;module.resumeValidationTarget=null;module.resumeValidationInFlight=false;}
        }
        public static void BeforeTasLateUpdate()
        {
            var module=active;var target=module==null?null:module.pendingPausedDynamicTransforms;
            if(target==null)return;
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main)){module.pendingPausedDynamicTransforms=null;return;}
            try
            {
                int changed=target.Scheduler.RestorePausedDynamicContainerTransforms();
                if(changed!=0)module.pausedDynamicTransformCorrections+=changed;
            }
            catch(Exception error)
            {
                // A failed authoring restore is already unusable.  Retrying the
                // same strict correction on every paused LateUpdate only hides
                // the first mismatch behind an exception loop and prevents the
                // controller from collecting module diagnostics.  Successful
                // restores and ordinary forward gameplay never take this path.
                module.pendingPausedDynamicTransforms=null;
                module.lastError="Paused dynamic Transform correction: "+error.Message;
                throw;
            }
        }
        public static void BeforeDynamicSpawn(NativeDynamicWarpPlan __instance)
        {
            var module=active;if(module==null||module.pendingDynamicRestore==null)return;
            module.pendingDynamicRestore.Scheduler.BeforeDynamicSpawn(__instance);
        }
        public static Exception FinalizeDynamicSpawn(NativeDynamicWarpPlan __instance,Exception __exception)
        {
            var module=active;if(module==null||module.pendingDynamicRestore==null)return __exception;
            var result=module.pendingDynamicRestore.Scheduler.FinalizeDynamicSpawn(__instance,__exception);
            if(result!=null)module.lastError="Dynamic spawn: "+result.Message;
            return result;
        }
        public static void BeforeAuthoringResume()
        {
            var module=active;if(module==null)return;
            if(!PlainAuthoringResume(Hpmv.Injector.Server.CurrentInput))return;
            var current=module.CurrentBoundaryForReferenceResume();
            var target=module.pendingResumeValidation??current;
            if(target==null)return;
            if(module.resumeValidationInFlight)
                throw new InvalidOperationException("A WorldObject resume validation is already in flight.");
            if(module.pendingResumeValidation!=null&&!ReferenceEquals(current,module.pendingResumeValidation))
                throw new InvalidOperationException("Restored WorldObject target is not the exact current resume boundary.");
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                throw new InvalidOperationException("WorldObject resume validation requires the retained authoring pause.");
            float now=UnrealTimePatch.CaptureLogicalRealtime();
            if(Bits(now)!=Bits(target.LogicalTime))
                throw new InvalidOperationException("WorldObject logical clock changed during authoring pause.");
            target.Scheduler.VerifyRestored();
            foreach(var item in target.Items)
            {
                module.ValidateIdentity(item,true);ValidateCurrentRestored(item,true);
                for(int i=0;i<module.cacheFields.Length;i++)
                    if(!System.Object.Equals(module.cacheFields[i].GetValue(item.Sync),item.Values[i]))
                        throw new InvalidOperationException("WorldObject cache changed during authoring pause: "+item.Id);
            }
            module.resumeValidationTarget=target;module.resumeValidationInFlight=true;
        }
        public static void AfterAuthoringResume()
        {
            var module=active;if(module==null||!module.resumeValidationInFlight)return;
            if(TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                throw new InvalidOperationException("WorldObject resume validation passed but main pause did not release.");
            if(ReferenceEquals(module.pendingResumeValidation,module.resumeValidationTarget))
            {module.pendingResumeValidation=null;module.warpResumeValidations++;}
            else module.referenceResumeValidations++;
            module.pendingPausedDynamicTransforms=null;
            module.resumeValidationTarget=null;module.resumeValidationInFlight=false;
        }
        public static Exception FinalizeAuthoringResume(Exception __exception)
        {
            var module=active;
            if(module!=null&&__exception!=null&&module.resumeValidationInFlight)
            {module.resumeValidationTarget=null;module.resumeValidationInFlight=false;}
            return __exception;
        }
        private Saved CurrentBoundaryForReferenceResume()
        {
            int frame=Hpmv.Injector.Server.CurrentFrameData.FrameNumber;Saved target;
            var history=(IDictionary)historyField.GetValue(null);
            if(!saved.TryGetValue(frame,out target)||target.Unsupported!=null||
                !ReferenceEquals(target.Snapshot,history[frame]))return null;
            return target;
        }
        private static bool PlainAuthoringResume(Hpmv.InputData input)
        {
            if(input==null||!input.RequestResume||input.RequestPause||input.Warp!=null||!input.__isset.gameSpeed||input.__isset.resetOrderSeed||
                input.Input!=null&&input.Input.Count!=0)return false;
            double phase=input.GameSpeed-1000.0;
            return phase>=0&&phase<6&&phase==Math.Truncate(phase);
        }
        private static void ValidateCurrentRestored(Item item,bool client)
        {
            item.Client.Validate();if(client)item.Client.VerifyRestored();
            if(!SameMessage(item.OriginalMessage,item.Message))
                throw new InvalidOperationException("WorldObject message changed during authoring pause: "+item.Id);
        }
        private static object Describe(Saved s)
        {
            if(s==null)return null;
            return new Dictionary<string,object>{{"frame",s.Frame},{"unsupported",s.Unsupported},{"capturedLogicalTime",s.LogicalTime},{"scheduler",s.Scheduler==null?null:s.Scheduler.Describe()},
                {"items",s.Items==null?null:s.Items.Select(i=>(object)new Dictionary<string,object>{
                    {"id",i.Id},{"dynamic",i.Dynamic},{"looseDynamic",i.LooseDynamic},{"objectId",i.ObjectId},{"syncId",i.SyncId},{"parentInstanceId",i.ParentId},
                    {"messageParent",i.Message.ParentEntityID},{"sentReliable",i.Values[2]},
                    {"lastUnreliableSend",i.Values[1]},{"active",i.Values[5]},{"parentChanged",i.Values[7]},
                    {"localDynamicActivePending",i.Dynamic&&!(bool)i.Values[2]&&(bool)i.Values[5]
                        &&s.Scheduler!=null&&s.Scheduler.WillRebindOwner(i.Id)},
                    {"restDeadlineMode",i.PendingRest?"logical-clock-pending":"completed-timestamp-irrelevant"},
                    {"restResidual",i.PendingRest?(object)i.RestResidual:null},
                    {"timestampRestoredExact",i.Restored}
                    ,{"client",i.Client.Describe()}
                }).ToArray()}};
        }
        private static bool SameMessage(WorldObjectMessage a,WorldObjectMessage b) { return a.HasParent==b.HasParent && a.ParentEntityID==b.ParentEntityID && a.HasPositions==b.HasPositions && Exact(a.LocalPosition,b.LocalPosition) && Exact(a.LocalRotation,b.LocalRotation); }
        private static bool Finite(float x) { return !float.IsNaN(x) && !float.IsInfinity(x); }
        private static bool Finite(Vector3 v) { return Finite(v.x)&&Finite(v.y)&&Finite(v.z); }
        private static bool Finite(Quaternion q) { return Finite(q.x)&&Finite(q.y)&&Finite(q.z)&&Finite(q.w); }
        private static bool Exact(Vector3 a,Vector3 b) { return a.x==b.x&&a.y==b.y&&a.z==b.z; }
        private static bool Exact(Quaternion a,Quaternion b) { return a.x==b.x&&a.y==b.y&&a.z==b.z&&a.w==b.w; }
        private static int Bits(float value) { return BitConverter.ToInt32(BitConverter.GetBytes(value),0); }
        private void Deactivate() { if(harmony!=null)harmony.UnpatchSelf(); harmony=null; if(ReferenceEquals(active,this))active=null; if(pendingDynamicRestore!=null)pendingDynamicRestore.Scheduler.AbortDynamicRestore();pendingDynamicRestore=null; pendingPausedDynamicTransforms=null; pendingResumeValidation=null; resumeValidationTarget=null; resumeValidationInFlight=false; saved.Clear(); }
        public void Dispose() { if(disposed)return; Deactivate(); disposed=true; }
    }
}
