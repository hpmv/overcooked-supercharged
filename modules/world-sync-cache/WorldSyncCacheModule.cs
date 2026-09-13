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
            internal float CaptureUnityTime, DeadlineOffset, RestoredLastUnreliableSend;
            internal bool TranslateRestDeadline, Restored;
        }
        private sealed class Saved
        {
            internal int Frame;
            internal object Snapshot;
            internal Item[] Items;
            internal float UnityTime;
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
        private FieldInfo historyField, roundField, initialField, fixedAttachmentsField, planSnapshot, transformField, messageField;
        private FieldInfo[] cacheFields;
        private MethodInfo capture, prepare, complete, restoreFailure, resume, dynamicSpawn, lateUpdate;
        private Saved pendingResumeRebase;
        private Saved pendingDynamicRestore;
        private Saved resumeRebaseTarget;
        private Saved pendingPausedDynamicTransforms;
        private bool resumeRebaseInFlight;
        private int referenceResumeRebases,warpResumeRebases,pausedDynamicTransformCorrections;
        private int captures, restores, rejected, dynamicRestores, survivingDynamicRestores, lastFrame = -1;
        private bool disposed;
        private string lastError;
        private object lastRestore;
        public string Name { get { return "native-world-sync-cache-v16-returned-stack-recreation"; } }
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
                {"pendingResumeRebase",pendingResumeRebase==null?null:(object)pendingResumeRebase.Frame},
                {"resumeRebaseInFlight",resumeRebaseInFlight},{"referenceResumeRebases",referenceResumeRebases},
                {"warpResumeRebases",warpResumeRebases},
                {"dynamicRestores",dynamicRestores},{"survivingDynamicRestores",survivingDynamicRestores},
                {"pendingDynamicRestore",pendingDynamicRestore==null?null:(object)pendingDynamicRestore.Frame},
                {"pendingPausedDynamicTransforms",pendingPausedDynamicTransforms==null?null:(object)pendingPausedDynamicTransforms.Frame},
                {"pausedDynamicTransformCorrections",pausedDynamicTransformCorrections},
                {"latest",Describe(latest)},
                {"scope","Initial fixed and admitted dynamic PhysicalAttachment WorldObject caches, relative pending-rest deadlines, and exact scheduler cadence/order/urgent/free-ID state. Every admitted settled dynamic PhysicalAttachment checkpoints its client/server parent caches, empty attachment-container Rigidbody/Transform poses, and exact logical owner pose/prediction flags. Attached items require a surviving registered station/chef parent. Loose items require the exact surviving owner/container incarnation and physical-container parenting; detached cross-incarnation recreation remains unsupported. After rewind only, the exact dynamic container Transform is retained across frozen authoring maintenance while Rigidbody pose/motion/settings remain unchanged. An attached one-step replacement may instead be recreated and rebound under its historical owner/body IDs by a transactional rewind-only allocator reservation. All other missing, ambiguous or changed memberships fail closed. Native Update, delays, event emission and clocks unchanged."}
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
            if (!ReferenceEquals(identity,round)) { saved.Clear(); pendingResumeRebase=null; pendingDynamicRestore=null; pendingPausedDynamicTransforms=null; resumeRebaseTarget=null; resumeRebaseInFlight=false; round=identity; lastFrame=-1; }
            var history = (IDictionary)historyField.GetValue(null);
            object native = history[frame];
            if (native == null) return; // Core rejected/has not captured this boundary.
            Saved previous;
            if (saved.TryGetValue(frame,out previous) && ReferenceEquals(previous.Snapshot,native)) return;
            // An existing native boundary may predate module activation. Its
            // historical cache cannot be inferred from a later paused poll.
            if(ReferenceEquals(priorSnapshot,native))return;
            if (saved.Count >= 20000) { lastError="World-sync capture bound reached."; return; }
            var value = new Saved {Frame=frame, Snapshot=native, UnityTime=Time.time};
            try
            {
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
                    .Select(id=>CaptureItem(id,value.UnityTime,dynamicIds.Contains(id))).ToArray();
                foreach (var item in value.Items) RequireSupported(item);
            }
            catch (Exception error) { value.Unsupported=error.Message; }
            saved[frame]=value; lastFrame=frame; captures++;
        }
        private Item CaptureItem(int id,float captureUnityTime,bool dynamic=false)
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
                Values=cacheFields.Select(f=>f.GetValue(sync)).ToArray(),CaptureUnityTime=captureUnityTime };
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
            item.TranslateRestDeadline=!(bool)item.Values[2];
            if(item.TranslateRestDeadline)item.DeadlineOffset=(((float)item.Values[1]+1f)-captureUnityTime);
            return item;
        }
        private static void RequireSupported(Item item)
        {
            bool sentReliable=(bool)item.Values[2],started=(bool)item.Values[3],sleepAllowed=(bool)item.Values[4];
            bool active=(bool)item.Values[5],parentChanged=(bool)item.Values[7],paused=(bool)item.Values[8];
            if (!started || !sleepAllowed || paused)
                throw new InvalidOperationException("Unsupported native WorldObject lifecycle at entity " + item.Id);
            bool syncPositions=(bool)item.Values[6];
            if(sentReliable&&(active||parentChanged))
                throw new InvalidOperationException("Inconsistent completed WorldObject rest state at entity " + item.Id);
            if(!sentReliable&&(active||!parentChanged||!syncPositions))
                throw new InvalidOperationException("Unsupported non-quiescent pending WorldObject rest state at entity " + item.Id);
            if (!ReferenceEquals(item.Values[0],item.Parent) || !Finite((float)item.Values[1])
                || !Finite(item.Message.LocalPosition) || !Finite(item.Message.LocalRotation)
                || !Finite(item.LocalPosition) || !Finite(item.LocalRotation))
                throw new InvalidOperationException("Invalid WorldObject cache at entity " + item.Id);
            if(item.TranslateRestDeadline&&(!Finite(item.DeadlineOffset)||item.DeadlineOffset>1.0001f))
                throw new InvalidOperationException("Invalid pending WorldObject rest deadline at entity " + item.Id);
        }
        public static void BeforePrepare(WarpSpec __0)
        {
            if (active == null) return;
            active.pendingResumeRebase=null;
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
                RequireSupported(item);
                if(target.Scheduler.WillRebindOwner(item.Id))continue;
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
        private Item RebindItem(Item target)
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
            return new Item {Id=target.Id,Dynamic=target.Dynamic,LooseDynamic=target.LooseDynamic,Object=obj,ObjectId=obj.GetInstanceID(),Sync=sync,SyncId=sync.GetInstanceID(),
                Transform=transform,Parent=target.Parent,ParentId=target.ParentId,OriginalMessage=message,Message=target.Message,
                LocalPosition=target.LocalPosition,LocalRotation=target.LocalRotation,Values=(object[])target.Values.Clone(),
                Client=target.Client.Rebind(obj),CaptureUnityTime=target.CaptureUnityTime,DeadlineOffset=target.DeadlineOffset,
                RestoredLastUnreliableSend=target.RestoredLastUnreliableSend,TranslateRestDeadline=target.TranslateRestDeadline,
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
                if(target.Scheduler.TryGetCurrentParentRebind(item.Id,out replacementParent,out replacementTransform))
                {
                    RebindItemParent(item,replacementParent,replacementTransform);
                    continue;
                }
                var entry=EntitySerialisationRegistry.GetEntry((uint)item.Id);
                if(entry==null||!ReferenceEquals(entry.m_GameObject,item.Object))
                {
                    if(!target.Scheduler.WillRebindOwner(item.Id))
                        throw new InvalidOperationException("WorldObject incarnation changed without an admitted rebind: "+item.Id);
                    target.Items[i]=RebindItem(item);
                }
            }
            foreach(var item in target.Items)ValidateIdentity(item,true);
            target.Scheduler.Validate();
            var clientsBefore=target.Items.Select(i=>i.Client.DescribeCurrent()).ToArray();
            float restoreUnityTime=Time.time;
            var restoredValues=target.Items.Select(item=>RestoredValues(item,restoreUnityTime)).ToArray();
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
                var observed=CaptureItem(item.Id,restoreUnityTime,item.Dynamic);
                var expected=(object[])item.Values.Clone();
                if(item.TranslateRestDeadline)expected[1]=item.RestoredLastUnreliableSend;
                if(!SameMessage(observed.Message,item.Message) || !observed.Values.SequenceEqual(expected))
                    throw new InvalidOperationException("WorldObject cache restore verification failed: " + item.Id);
            }
            target.Scheduler.Restore();
            if(target.Scheduler.DynamicPending){target.Scheduler.FinishDynamicRestore();dynamicRestores++;}
            pendingPausedDynamicTransforms=target;
            pendingDynamicRestore=null;
            foreach(int frame in saved.Keys.Where(f=>f>target.Frame).ToArray()) saved.Remove(frame);
            lastFrame=target.Frame; restores++; lastError=null;
            pendingResumeRebase=target.Items.Any(i=>i.TranslateRestDeadline)?target:null;
            lastRestore=new Dictionary<string,object>{{"frame",target.Frame},{"count",target.Items.Length},{"verified",true},
                {"clientsBefore",clientsBefore},{"clientsAfter",target.Items.Select(i=>i.Client.DescribeCurrent()).ToArray()},
                {"capturedUnityTime",target.UnityTime},{"restoreUnityTime",restoreUnityTime},{"currentUnityTime",Time.time},
                {"translatedRestDeadlines",target.Items.Count(i=>i.TranslateRestDeadline)},{"items",Describe(target)}};
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
        private static object[] RestoredValues(Item item,float restoreUnityTime)
        {
            var restored=(object[])item.Values.Clone();
            if(!item.TranslateRestDeadline)return restored;
            float translated=(restoreUnityTime+item.DeadlineOffset)-1f;
            float observedOffset=(translated+1f)-restoreUnityTime;
            if(!Finite(translated)||Bits(observedOffset)!=Bits(item.DeadlineOffset))
                throw new InvalidOperationException("Pending WorldObject rest deadline cannot be rebased bit-exactly: "+item.Id);
            item.RestoredLastUnreliableSend=translated;restored[1]=translated;
            return restored;
        }
        public static void AfterRestoreFailure()
        {
            var module=active;if(module!=null){if(module.pendingDynamicRestore!=null)module.pendingDynamicRestore.Scheduler.AbortDynamicRestore();module.pendingDynamicRestore=null;module.pendingPausedDynamicTransforms=null;module.pendingResumeRebase=null;module.resumeRebaseTarget=null;module.resumeRebaseInFlight=false;}
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
            catch(Exception error){module.lastError="Paused dynamic Transform correction: "+error.Message;throw;}
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
            var target=module.pendingResumeRebase??module.CurrentBoundaryForReferenceResume();
            if(target==null||!target.Items.Any(item=>item.TranslateRestDeadline))return;
            if(module.resumeRebaseInFlight)throw new InvalidOperationException("A WorldObject resume deadline rebase is already in flight.");
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                throw new InvalidOperationException("WorldObject deadline rebase requires the retained authoring pause.");
            float now=Time.time;
            target.Scheduler.VerifyRestored();
            var values=target.Items.Select(item=>RestoredValues(item,now)).ToArray();
            for(int itemIndex=0;itemIndex<target.Items.Length;itemIndex++)
            {
                var item=target.Items[itemIndex];module.ValidateIdentity(item,true);ValidateCurrentRestored(item,true);
                for(int i=0;i<module.cacheFields.Length;i++)
                    if(i!=1&&!System.Object.Equals(module.cacheFields[i].GetValue(item.Sync),item.Values[i]))
                        throw new InvalidOperationException("WorldObject cache changed during authoring pause: "+item.Id);
            }
            for(int itemIndex=0;itemIndex<target.Items.Length;itemIndex++)
            {
                var item=target.Items[itemIndex];
                if(!item.TranslateRestDeadline)continue;
                module.cacheFields[1].SetValue(item.Sync,values[itemIndex][1]);
                if(Bits((float)module.cacheFields[1].GetValue(item.Sync))!=Bits((float)values[itemIndex][1]))
                    throw new InvalidOperationException("WorldObject resume deadline write differs: "+item.Id);
            }
            module.resumeRebaseTarget=target;module.resumeRebaseInFlight=true;
        }
        public static void AfterAuthoringResume()
        {
            var module=active;if(module==null||!module.resumeRebaseInFlight)return;
            if(TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                throw new InvalidOperationException("WorldObject resume deadline was rebased but main pause did not release.");
            if(ReferenceEquals(module.pendingResumeRebase,module.resumeRebaseTarget)){module.pendingResumeRebase=null;module.warpResumeRebases++;}
            else module.referenceResumeRebases++;
            module.pendingPausedDynamicTransforms=null;
            module.resumeRebaseTarget=null;module.resumeRebaseInFlight=false;
        }
        public static Exception FinalizeAuthoringResume(Exception __exception)
        {
            var module=active;
            if(module!=null&&__exception!=null&&module.resumeRebaseInFlight)
            {module.resumeRebaseTarget=null;module.resumeRebaseInFlight=false;}
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
            return new Dictionary<string,object>{{"frame",s.Frame},{"unsupported",s.Unsupported},{"capturedUnityTime",s.UnityTime},{"scheduler",s.Scheduler==null?null:s.Scheduler.Describe()},
                {"items",s.Items==null?null:s.Items.Select(i=>(object)new Dictionary<string,object>{
                    {"id",i.Id},{"dynamic",i.Dynamic},{"looseDynamic",i.LooseDynamic},{"objectId",i.ObjectId},{"syncId",i.SyncId},{"parentInstanceId",i.ParentId},
                    {"messageParent",i.Message.ParentEntityID},{"sentReliable",i.Values[2]},
                    {"lastUnreliableSend",i.Values[1]},{"active",i.Values[5]},{"parentChanged",i.Values[7]},
                    {"restDeadlineMode",i.TranslateRestDeadline?"relative-pending":"completed-absolute-irrelevant"},
                    {"deadlineOffset",i.TranslateRestDeadline?(object)i.DeadlineOffset:null},
                    {"restoredLastUnreliableSend",i.Restored?(object)i.RestoredLastUnreliableSend:null}
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
        private void Deactivate() { if(harmony!=null)harmony.UnpatchSelf(); harmony=null; if(ReferenceEquals(active,this))active=null; pendingResumeRebase=null; if(pendingDynamicRestore!=null)pendingDynamicRestore.Scheduler.AbortDynamicRestore();pendingDynamicRestore=null; pendingPausedDynamicTransforms=null; resumeRebaseTarget=null; resumeRebaseInFlight=false; saved.Clear(); }
        public void Dispose() { if(disposed)return; Deactivate(); disposed=true; }
    }
}
