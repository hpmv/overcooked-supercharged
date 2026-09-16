using SuperchargedPatch;
using Team17.Online.Multiplayer.Messaging;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Reflection.Emit;
using System.Reflection.Metadata.Ecma335;
int checks=0;
void Check(bool value,string message) { if(!value) throw new Exception(message);checks++; }
void Reject(Action action,string message) { try{action();}catch(InvalidOperationException){checks++;return;}throw new Exception("Expected rejection: "+message); }
string Guard(int flag) => NativeCannonWarpGuard.UnsettledReason(flag==0,flag==1,flag==2,flag==3?1:0,flag==4,flag==5,flag==6,flag==7);
Check(Guard(-1)==null,"Fully inactive native cannon accepted without requiring stale loadedObject=null.");
for(int i=0;i<8;i++) Check(Guard(i)!=null,"Native active phase "+i+" rejects.");
Check(NativeCannonWarpGuard.UnsettledReason(false,false,false,-1,false,false,false,false)!=null,"Invalid iterator count rejects.");
var chefs=Enumerable.Range(103,4).Select(id=> {
    var c=new ClientPlayerControlsImpl_Default();UnityEngine.Object.Chefs.Add(c);
    EntitySerialisationRegistry.Entries[c.gameObject]=new(){m_Header=new(){m_uEntityID=(uint)id}};
    c.TestSet((id-103)*.125f);c.gameObject.Controls.ControlScheme.TestSet(id%2==0);return c;
}).ToArray();
var saved=NativeChefInteractionCheckpoint.CaptureAll();
Check(saved.Length==4,"All four exact native chef identities captured.");
float[] original=chefs.Select(c=>c.Timestamp).ToArray();bool[] suppression=chefs.Select(c=>c.gameObject.Controls.ControlScheme.IsUseSuppressed()).ToArray();
foreach(var c in chefs){c.TestSet(99f);c.gameObject.Controls.ControlScheme.TestSet(false);}
Check(!NativeChefInteractionCheckpoint.SameBoundary(saved,NativeChefInteractionCheckpoint.CaptureAll()),"Later pickup/suppression changes are observable.");
NativeChefInteractionCheckpoint.Restore(saved);NativeChefInteractionCheckpoint.VerifyRestored(saved);
for(int i=0;i<4;i++) {
    Check(chefs[i].Timestamp==original[i],"Exact ClientTime pickup timestamp restored, not rebased against Unity time.");
    Check(chefs[i].gameObject.Controls.ControlScheme.IsUseSuppressed()==suppression[i],"Native suppression restored for chef"+i);
}
// Demonstrated F failure shape: rewound clock is below the later pickup deadline.
chefs[0].TestSet(100f);Check(!(1f>=chefs[0].Timestamp),"Later pickup deadline blocks the native >= gate.");
NativeChefInteractionCheckpoint.Restore(saved);Check(1f>=chefs[0].Timestamp,"Restored timestamp admits the same native >= gate after clock rewind.");
var scheme=chefs[0].gameObject.Controls.ControlScheme;chefs[0].gameObject.Controls.ControlScheme=new();
chefs[1].TestSet(55f);Reject(()=>NativeChefInteractionCheckpoint.Restore(saved),"Changed exact control-scheme instance.");
Check(chefs[1].Timestamp==55f,"Identity rejection precedes every field mutation.");chefs[0].gameObject.Controls.ControlScheme=scheme;
var entry=EntitySerialisationRegistry.Entries[chefs[0].gameObject];entry.m_Header.m_uEntityID=999;
Reject(()=>NativeChefInteractionCheckpoint.Restore(saved),"Changed native entity ID.");entry.m_Header.m_uEntityID=103;
EntitySerialisationRegistry.Entries.Remove(chefs[0].gameObject);Reject(()=>NativeChefInteractionCheckpoint.Restore(saved),"Native chef no longer registered.");EntitySerialisationRegistry.Entries[chefs[0].gameObject]=entry;
UnityEngine.Object.Chefs.RemoveAt(3);Reject(()=>NativeChefInteractionCheckpoint.Restore(saved),"Changed native chef membership.");UnityEngine.Object.Chefs.Add(chefs[3]);
chefs[0].TestSet(float.NaN);Reject(()=>NativeChefInteractionCheckpoint.CaptureAll(),"Nonfinite native cooldown rejects.");chefs[0].TestSet(0);
NativeChefInteractionCheckpoint.Restore(saved);NativeChefInteractionCheckpoint.VerifyRestored(saved);Check(true,"Exact restore passes after failed probes without corrupting the saved snapshot.");
var served=new UnityEngine.GameObject();
var servedEntry=new EntitySerialisationEntry {m_GameObject=served,m_Header=new(){m_uEntityID=200}};
EntitySerialisationRegistry.Entries.Add(served,servedEntry);
SuperchargedPatch.AlteredComponents.NativePlateLifecycle.ObserveServed(served);
Check(EntitySerialisationRegistry.GetEntry(served)==servedEntry,"Serving never unregisters the actual native plate.");
Check(SuperchargedPatch.AlteredComponents.NativePlateLifecycle.PendingDeliveryFades==1 && AuxMessageSender.Messages.Count==1 && EntityRetirementMessageSender.Messages.Count==0,"Served receipt retains registration until native removal.");
SuperchargedPatch.AlteredComponents.NativePlateLifecycle.ObserveServed(served);
Check(AuxMessageSender.Messages.Count==1,"Repeated served callback does not duplicate lifecycle receipt.");
var removing=SuperchargedPatch.AlteredComponents.NativePlateLifecycle.BeforeRemoval(servedEntry);
SuperchargedPatch.AlteredComponents.NativePlateLifecycle.AfterRemoval(removing);
Check(EntityRetirementMessageSender.Messages.Count==0,"Native entry still present cannot be retired.");
EntitySerialisationRegistry.Entries.Remove(served);
SuperchargedPatch.AlteredComponents.NativePlateLifecycle.AfterRemoval(removing);
Check(SuperchargedPatch.AlteredComponents.NativePlateLifecycle.PendingDeliveryFades==0 && EntityRetirementMessageSender.Messages.SequenceEqual(new uint[]{200}),"Actual native removal releases the fade and emits exact retirement.");
Check(((SuperchargedPatch.AlteredComponents.PlateLifecycleAuxMessage)AuxMessageSender.Messages.Last().Message).Phase==2,"Removal is a separate phase-two receipt.");
EntitySerialisationRegistry.Entries.Add(served,servedEntry);
SuperchargedPatch.AlteredComponents.NativePlateLifecycle.ObserveServed(served);
SuperchargedPatch.AlteredComponents.NativePlateLifecycle.RegistryCleared();
Check(SuperchargedPatch.AlteredComponents.NativePlateLifecycle.PendingDeliveryFades==0 && EntityRetirementMessageSender.Messages.Count==1,"Native registry Clear resets observer metadata without invented per-entity retirement.");
SuperchargedPatch.AlteredComponents.NativePlateLifecycle.ObserveServed(served);
removing=SuperchargedPatch.AlteredComponents.NativePlateLifecycle.BeforeRemoval(servedEntry);
EntitySerialisationRegistry.Entries.Remove(served);
var successor=new UnityEngine.GameObject();
EntitySerialisationRegistry.Entries.Add(successor,new(){m_GameObject=successor,m_Header=new(){m_uEntityID=200}});
SuperchargedPatch.AlteredComponents.NativePlateLifecycle.AfterRemoval(removing);
Check(EntityRetirementMessageSender.Messages.Count==1 && EntitySerialisationRegistry.GetEntry(successor)!=null,"Reentrant native ID reuse cannot retire the new incarnation.");
Check(SuperchargedPatch.AlteredComponents.NativePlateLifecycle.LastObservationError.Length>0 && SuperchargedPatch.AlteredComponents.NativePlateLifecycle.PendingDeliveryFades==0,"Reentrant reuse is diagnosed without a stale old fade lock.");
SuperchargedPatch.AlteredComponents.NativePlateLifecycle.RegistryCleared();
var lifecycleWriter=new BitStream.BitStreamWriter();
new SuperchargedPatch.AlteredComponents.PlateLifecycleAuxMessage {Phase=2,InstanceBits=0xfedcba98u}.Serialise(lifecycleWriter);
Check(lifecycleWriter.Integers.SequenceEqual(new[]{(1u,32),(2u,32),(0xfedcba98u,32)}),"Lifecycle writes exact 32-bit integers through the native overload shape, including high-bit IDs.");
using(var pe=new PEReader(File.OpenRead("runtime/Overcooked2_Data/Managed/Assembly-CSharp.dll"))) {
    var metadata=pe.GetMetadataReader();
    byte[] FieldSignature(string typeName,string fieldName) {
        var type=metadata.TypeDefinitions.Select(h=>metadata.GetTypeDefinition(h)).Single(t=>metadata.GetString(t.Name)==typeName);
        var field=type.GetFields().Select(h=>metadata.GetFieldDefinition(h)).Single(f=>metadata.GetString(f.Name)==fieldName);
        return metadata.GetBlobBytes(field.Signature);
    }
    Check(FieldSignature("ClientPlayerControlsImpl_Default","m_lastPickupTimestamp").SequenceEqual(new byte[]{6,12}),"Installed native private pickup field is exactly Float32.");
    Check(FieldSignature("ControlSchemeData","m_supressUse").SequenceEqual(new byte[]{6,2}),"Installed native private use suppression is exactly Boolean.");
    foreach(var pair in new[]{("ServerCannon","m_flying"),("ServerCannon","m_readyToLaunch"),("ClientCannonPlayerHandler","m_inCannon")})
        Check(FieldSignature(pair.Item1,pair.Item2).SequenceEqual(new byte[]{6,2}),"Installed native cannon guard field has exact Boolean schema: "+pair);
    foreach(var pair in new[]{("ServerCannon","m_loadedObject"),("ServerCannon","m_message"),("ServerCannon","OnInteractionEnd"),
        ("ClientCannon","m_launches"),("ClientCannon","m_loadedObject"),("ClientCannon","m_message"),("ClientCannon","m_exitPosition"),("ClientCannon","m_exitRotation"),
        ("ServerSessionInteractable","m_session"),("ClientCannonPlayerHandler","m_controls")})
        Check(FieldSignature(pair.Item1,pair.Item2).Length>1,"Installed native field exists: "+pair);
    foreach(var pair in new[]{("ServerPilotRotation","m_angle"),("ServerPilotRotation","m_startAngle"),("ServerPilotRotation","m_startRightDirection"),("ServerPilotRotation","m_message"),
        ("ServerPilotMovement","m_controlScheme"),("ClientPilotRotation","m_nextRotation"),("ClientPilotRotation","m_message"),("ClientPilotMovement","m_avatar"),
        ("PilotMovement","m_previousPose"),("PilotMovement","m_previousPoseDifference"),("PilotMovement","m_velocityAverage"),("PilotMovement","m_belowThresholdCounter"),
        ("PilotRotation","m_bEstimateVelocityInX"),("PilotRotation","m_directionModifier")})
        Check(FieldSignature(pair.Item1,pair.Item2).Length>1,"Installed native pilot checkpoint field exists: "+pair);
}
using(var pe=new PEReader(File.OpenRead("artifacts/framework-build/SuperchargedPatch.dll"))) {
    var metadata=pe.GetMetadataReader();
    var opcodes=typeof(OpCodes).GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static)
        .Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)).ToDictionary(o=>unchecked((ushort)o.Value));
    foreach(var target in new[]{("NativeCannonAuxMessage",3,5,2),("PlateLifecycleAuxMessage",3,0,0)}) {
        var type=metadata.TypeDefinitions.Select(h=>metadata.GetTypeDefinition(h)).Single(t=>metadata.GetString(t.Name)==target.Item1);
        var method=type.GetMethods().Select(h=>metadata.GetMethodDefinition(h)).Single(m=>metadata.GetString(m.Name)=="Serialise");
        var il=pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();var signatures=new List<string>();
        for(int offset=0;offset<il.Length;) {
            ushort value=il[offset++];if(value==0xfe)value=(ushort)(0xfe00|il[offset++]);
            var op=opcodes[value];
            if(op.OperandType==OperandType.InlineMethod) {
                var handle=MetadataTokens.EntityHandle(BitConverter.ToInt32(il,offset));
                if(handle.Kind==HandleKind.MemberReference) {
                    var member=metadata.GetMemberReference((MemberReferenceHandle)handle);
                    if(metadata.GetString(member.Name)=="Write")signatures.Add(Convert.ToHexString(metadata.GetBlobBytes(member.Signature)));
                }
            }
            offset+=op.OperandType switch {
                OperandType.InlineNone=>0, OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar=>1,
                OperandType.InlineVar=>2, OperandType.InlineI8 or OperandType.InlineR=>8,
                OperandType.InlineSwitch=>4+4*BitConverter.ToInt32(il,offset), _=>4};
        }
        Check(signatures.Count(s=>s=="2002010908")==target.Item2,target.Item1+" compiled native UInt32/count overload calls");
        Check(signatures.Count(s=>s=="20010102")==target.Item3,target.Item1+" compiled native Boolean overload calls");
        Check(signatures.Count(s=>s=="2001010C")==target.Item4,target.Item1+" compiled native Float32 overload calls only for actual float fields");
        Check(signatures.Count==target.Item2+target.Item3+target.Item4,target.Item1+" no unexpected wire writer overloads");
    }
    var attributes=metadata.CustomAttributes.Select(h=>Encoding.UTF8.GetString(metadata.GetBlobBytes(metadata.GetCustomAttribute(h).Value))).ToArray();
    foreach(var banned in new[]{("SerialisationRegistry","RegisterMessageType"),("EntitySerialisationRegistry","AddSynchronisedType"),
        ("ServerSynchronisationScheduler","SynchroniseList"),("ServerWorldObjectSynchroniser","GetServerUpdate"),("ClientKitchenLoader","CheckStarted"),("MeshLerper","Update")})
        Check(!attributes.Any(a=>a.Contains(banned.Item1)&&a.Contains(banned.Item2)),"Built plugin does not patch original native method: "+banned);
}
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{checks,scope="Production checkpoint/guard code with controlled registry/object fixtures; no native execution claim."}));
