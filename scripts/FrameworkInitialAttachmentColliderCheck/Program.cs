using SuperchargedPatch;
using UnityEngine;

var checks=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
void Reject(Action action,string name)
{
    try{action();}catch(InvalidOperationException){checks.Add(name);return;}
    throw new Exception("Expected rejection: "+name);
}
void DestroyTree(GameObject root)
{
    foreach(var value in GameObject.All.Where(value=>ReferenceEquals(value,root)
        ||HasAncestor(value.transform,root.transform)).ToArray())
    {
        foreach(var collider in value.colliders)collider.Destroyed=true;
        value.transform.Destroyed=true;value.Destroyed=true;
    }
}
bool HasAncestor(Transform value,Transform target)
{for(var cursor=value.parent;cursor!=null;cursor=cursor.parent)if(ReferenceEquals(cursor,target))return true;return false;}
GameObject Plate(Transform parent,PhysicMaterial material,string leaf="PlateColliders")
{
    var root=new GameObject("Plate");root.transform.parent=parent;
    var colliders=new GameObject(leaf){layer=15};colliders.transform.parent=root.transform;
    var trigger=colliders.AddCollider<BoxCollider>();trigger.isTrigger=true;
    trigger.center=new(0,.375f,0);trigger.size=new(.7f,.55f,.7f);
    var solid=colliders.AddCollider<BoxCollider>();solid.sharedMaterial=material;
    solid.center=new(0,.05f,0);solid.size=new(.7f,.1f,.7f);
    return root;
}

var chef=new GameObject("Chef");var body=chef.AddBody();
var hand=new GameObject("AttachPoint");hand.transform.parent=chef.transform;
var material=new PhysicMaterial();var oldPlate=Plate(hand.transform,material);
var capsule=chef.AddCollider<CapsuleCollider>();capsule.sharedMaterial=material;chef.layer=8;
var target=NativeBodyColliderCheckpoint.Capture(body);
var owned=target.Where(value=>NativeBodyColliderCheckpoint.HasAncestor(value,oldPlate.transform)).ToArray();
var survivors=target.Except(owned).ToArray();
Check(!NativeBodyColliderCheckpoint.IsDestroyedUnityWrapper(oldPlate.transform),
    "live owner is not classified as a destroyed retained Unity identity");
Check(target.Length==3&&owned.Length==2&&survivors.Length==1,"captured compound chef row isolates exactly two plate colliders");
Check(owned.Select(value=>value.ComponentIndex).SequenceEqual(new[]{0,1}),"same-object plate colliders retain component ordinals");
Check(owned.All(value=>NativeBodyColliderCheckpoint.HasUniqueNonBodyAncestor(value,oldPlate.transform)),
    "captured plate root is a unique non-body ancestor of both shapes");
DestroyTree(oldPlate);
Check(NativeBodyColliderCheckpoint.IsDestroyedUnityWrapper(oldPlate.transform),
    "destroyed owner retains CLR identity while Unity null equality is true");
Check(owned.All(NativeBodyColliderCheckpoint.Destroyed),"destroyed plate collider wrappers remain classifiable by historical ancestry");
NativeBodyColliderCheckpoint.ValidateCaptured(survivors);
NativeBodyColliderCheckpoint.RequireExactMembership(body,survivors);
Check(true,"surviving chef collider validates exactly after destroyed plate shapes are omitted");

var freshPlate=Plate(hand.transform,material);
var hierarchy=NativeBodyColliderCheckpoint.CaptureOwnerHierarchy(body,freshPlate.transform);
Check(hierarchy.Length==2&&hierarchy.All(value=>NativeBodyColliderCheckpoint.HasAncestor(value,freshPlate.transform)),
    "fresh owner hierarchy can be proven before compound-body membership capture");
freshPlate.transform.localPosition=new(9,8,7);
var poseMap=owned.Zip(hierarchy,(target,value)=>new KeyValuePair<NativeBodyColliderCheckpoint.Shape,
    NativeBodyColliderCheckpoint.Shape>(target,value)).ToArray();
Check(NativeBodyColliderCheckpoint.RestoreRecreatedOwnerPoses(poseMap,oldPlate.transform,freshPlate.transform,body)
    &&freshPlate.transform.localPosition.x==oldPlate.transform.localPosition.x,
    "fresh owner-relative collider poses are restored before native shape recapture");
var current=NativeBodyColliderCheckpoint.Capture(body);
var rebound=new NativeBodyColliderCheckpoint.Shape[current.Length];
var used=new HashSet<NativeBodyColliderCheckpoint.Shape>();
for(int i=0;i<current.Length;i++)
{
    if(!NativeBodyColliderCheckpoint.HasAncestor(current[i],freshPlate.transform)){rebound[i]=survivors.Single();continue;}
    var matches=owned.Where(value=>!used.Contains(value)&&NativeBodyColliderCheckpoint.SameRecreatedOwnerTopology(
        value,current[i],oldPlate.transform,freshPlate.transform)).ToArray();
    Check(matches.Length==1,"fresh collider maps uniquely by owner-relative path/type/component ordinal");
    used.Add(matches[0]);rebound[i]=NativeBodyColliderCheckpoint.RebindRecreatedOwnerShape(matches[0],current[i],
        oldPlate.transform,freshPlate.transform,body);
}
Check(used.Count==2&&rebound.Select(value=>value.Collider).SequenceEqual(current.Select(value=>value.Collider)),
    "rebound target is arranged in fresh deterministic capture order");
Check(rebound.Where(value=>NativeBodyColliderCheckpoint.HasAncestor(value,freshPlate.transform))
    .Select(value=>value.Geometry[1]).OrderBy(value=>value).SequenceEqual(new[]{.05f,.375f}),
    "historical per-collider geometry is retained on fresh Unity references");
NativeBodyColliderCheckpoint.RequireExactMembership(body,rebound);
Check(true,"fresh compound collider membership/order verifies exactly");

var wrongMaterial=new PhysicMaterial();
var freshSolid=current.Single(value=>NativeBodyColliderCheckpoint.HasAncestor(value,freshPlate.transform)&&!value.Trigger);
freshSolid.Collider.sharedMaterial=wrongMaterial;
var recapturedWrongMaterial=NativeBodyColliderCheckpoint.Capture(body).Single(value=>ReferenceEquals(value.Collider,freshSolid.Collider));
var targetSolid=owned.Single(value=>!value.Trigger);
Reject(()=>NativeBodyColliderCheckpoint.RebindRecreatedOwnerShape(targetSolid,recapturedWrongMaterial,
    oldPlate.transform,freshPlate.transform,body),"material mismatch fails closed");
freshSolid.Collider.sharedMaterial=material;

DestroyTree(freshPlate);
var wrongPathPlate=Plate(hand.transform,material,"WrongColliderNode");
var wrongPath=NativeBodyColliderCheckpoint.Capture(body).First(value=>NativeBodyColliderCheckpoint.HasAncestor(value,wrongPathPlate.transform));
Check(!owned.Any(value=>NativeBodyColliderCheckpoint.SameRecreatedOwnerTopology(value,wrongPath,
    oldPlate.transform,wrongPathPlate.transform)),"owner-relative hierarchy-name mismatch cannot map");

var extra=chef.AddCollider<SphereCollider>();
Reject(()=>NativeBodyColliderCheckpoint.RequireExactMembership(body,rebound),"extra collider membership fails closed");
extra.Destroyed=true;

Hpmv.EntityWarpSpec ContainerSpec(int id=47)=>new Hpmv.EntityWarpSpec {
    EntityId=id,Position=new(){X=1,Y=2,Z=3},Rotation=new(){X=(double).1f,Y=(double).2f,Z=(double).3f,W=(double).4f},
    Velocity=new(){X=4,Y=5,Z=6},AngularVelocity=new(){X=7,Y=8,Z=9},
    __isset=new(){entityId=true,position=true,rotation=true,velocity=true,angularVelocity=true}
};
var containerSpec=ContainerSpec();
Check(NativeInitialAttachmentMissingEntityValidator.ValidatePoseOnly(containerSpec,47),
    "target-absent fixed container accepts the exact pose-only controller residue shape");
Check(!NativeInitialAttachmentMissingEntityValidator.ValidatePoseOnly(ContainerSpec(99),47),
    "target-absent residue validator does not claim an unrelated body ID");
Check(NativeInitialAttachmentMissingEntityValidator.Validate(containerSpec,2,47,new(1,2,3),new(.1f,.2f,.3f,.4f),
    new(4,5,6),new(7,8,9)),"qualified missing container admits its exact pose-only fixed row");
Check(!NativeInitialAttachmentMissingEntityValidator.Validate(ContainerSpec(99),2,47,new(1,2,3),new(.1f,.2f,.3f,.4f),
    new(4,5,6),new(7,8,9)),"unrelated missing fixed entity is not handled by the recreation plan");
Reject(()=>NativeInitialAttachmentMissingEntityValidator.Validate(ContainerSpec(2),2,47,new(1,2,3),new(.1f,.2f,.3f,.4f),
    new(4,5,6),new(7,8,9)),"missing owner fixed row cannot replace its qualified spawn path");
var missingPose=ContainerSpec();missingPose.__isset.angularVelocity=false;
Reject(()=>NativeInitialAttachmentMissingEntityValidator.Validate(missingPose,2,47,new(1,2,3),new(.1f,.2f,.3f,.4f),
    new(4,5,6),new(7,8,9)),"incomplete missing-container pose row fails closed");
var extraBlock=ContainerSpec();extraBlock.__isset.ingredientContainer=true;
Reject(()=>NativeInitialAttachmentMissingEntityValidator.Validate(extraBlock,2,47,new(1,2,3),new(.1f,.2f,.3f,.4f),
    new(4,5,6),new(7,8,9)),"component block on colliderless missing container fails closed");
var wrongPose=ContainerSpec();wrongPose.Position.X=10;
Reject(()=>NativeInitialAttachmentMissingEntityValidator.Validate(wrongPose,2,47,new(1,2,3),new(.1f,.2f,.3f,.4f),
    new(4,5,6),new(7,8,9)),"missing-container pose mismatch fails closed");

Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {passed=true,checkCount=checks.Count,checks},
    new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
