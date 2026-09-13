using System;
using System.Collections.Generic;
using System.Linq;
using SuperchargedPatch;
using UnityEngine;

var checks = new List<string>();
void Check(bool value, string name)
{
    if (!value) throw new Exception("FAILED: " + name);
    checks.Add(name);
}
void Reject(Action action, string name)
{
    try { action(); }
    catch (InvalidOperationException) { checks.Add(name); return; }
    throw new Exception("FAILED: " + name);
}
EntitySerialisationEntry Entry(int id, GameObject obj)
{ return new EntitySerialisationEntry { m_Header = new EntityHeader { m_uEntityID = (uint)id }, m_GameObject = obj }; }
(EntitySerialisationEntry Owner, EntitySerialisationEntry Body) Pair(int ownerId, int bodyId, string suffix)
{
    var bodyObject = new GameObject("body" + suffix);
    var rigidbody = bodyObject.Add(new Rigidbody());
    var ownerObject = new GameObject("owner" + suffix);
    ownerObject.Add(new PhysicalAttachment { m_container = rigidbody });
    return (Entry(ownerId, ownerObject), Entry(bodyId, bodyObject));
}
(EntitySerialisationEntry Chef, EntitySerialisationEntry Owner2, EntitySerialisationEntry Body47,
  EntitySerialisationEntry Owner3, EntitySerialisationEntry Body48) Setup()
{
    var chefObject = new GameObject("chef"); chefObject.Add(new Rigidbody());
    var chef = Entry(43, chefObject); var first = Pair(2, 47, "A"); var second = Pair(3, 48, "B");
    EntitySerialisationRegistry.Set(chef, first.Owner, first.Body, second.Owner, second.Body);
    Hpmv.Injector.Server.CurrentFrameData = new Hpmv.FrameData();
    NativeSceneMetadata.Refresh();
    return (chef, first.Owner, first.Body, second.Owner, second.Body);
}

var initial = Setup();
var membership = NativeSceneMetadata.CaptureCurrentInitialMembership();
Check(membership.RigidBodyIds.SetEquals(new[] { 43, 47, 48 })
    && membership.PhysicalAttachmentIds.SetEquals(new[] { 2, 3 }), "original initial membership captures unchanged");

var dynamicPair = Pair(55, 56, "dynamic");
EntitySerialisationRegistry.Set(initial.Chef, initial.Owner2, initial.Body47, initial.Owner3, initial.Body48,
    dynamicPair.Owner, dynamicPair.Body);
membership = NativeSceneMetadata.CaptureCurrentInitialMembership();
Check(!membership.RigidBodyIds.Contains(56) && !membership.PhysicalAttachmentIds.Contains(55),
    "runtime attachment pair remains excluded from fixed membership");

EntitySerialisationRegistry.Set(initial.Chef, initial.Owner3, initial.Body48, dynamicPair.Owner, dynamicPair.Body);
membership = NativeSceneMetadata.CaptureCurrentInitialMembership();
Check(membership.RigidBodyIds.SetEquals(new[] { 43, 48 })
    && membership.PhysicalAttachmentIds.SetEquals(new[] { 3 }), "whole known owner-body absence is admitted");
NativeSceneMetadata.RequireCurrentInitialMembershipSubset(new[] { 43, 47, 48 }, new[] { 2, 3 });
NativeSceneMetadata.RequireCurrentInitialMembershipExact(new[] { 43, 48 }, new[] { 3 });
checks.Add("missing target-live pair is admitted while exact reduced target verifies");
var absentBodies = NativeSceneMetadata.QualifyTargetAbsentInitialBodyIds(new[] { 43, 48 }, new[] { 3 });
Check(absentBodies.SetEquals(new[] { 47 }), "target/current whole-pair absence qualifies only its fixed body residue");
Check(NativeSceneMetadata.QualifyTargetAbsentInitialBodyIds(new[] { 43, 47, 48 }, new[] { 2, 3 }).Count == 0,
    "historical target membership never qualifies its missing body as ignorable residue");
Reject(() => NativeSceneMetadata.QualifyTargetAbsentInitialBodyIds(new[] { 43, 48 }, new[] { 2, 3 }),
    "half-pair target membership cannot qualify controller residue");
Reject(() => NativeSceneMetadata.QualifyTargetAbsentInitialBodyIds(new[] { 48 }, new[] { 3 }),
    "target cannot omit an unpaired initial Rigidbody");
Reject(() => NativeSceneMetadata.RequireCurrentInitialMembershipExact(new[] { 43, 47, 48 }, new[] { 2, 3 }),
    "missing pair cannot satisfy a full target postcondition");

initial = Setup();
EntitySerialisationRegistry.Set(initial.Owner2, initial.Body47, initial.Owner3, initial.Body48);
Reject(() => NativeSceneMetadata.CaptureCurrentInitialMembership(), "missing unpaired chef fails closed");
initial = Setup();
EntitySerialisationRegistry.Set(initial.Chef, initial.Body47, initial.Owner3, initial.Body48);
Reject(() => NativeSceneMetadata.CaptureCurrentInitialMembership(), "half-missing owner-body pair fails closed");
initial = Setup();
EntitySerialisationRegistry.Set(initial.Chef, initial.Owner2, initial.Owner3, initial.Body48);
Reject(() => NativeSceneMetadata.CaptureCurrentInitialMembership(), "opposite half-missing owner-body pair fails closed");

initial = Setup();
((PhysicalAttachment)initial.Owner2.m_GameObject.GetComponent<PhysicalAttachment>()).m_container =
    initial.Body48.m_GameObject.GetComponent<Rigidbody>();
Reject(() => NativeSceneMetadata.CaptureCurrentInitialMembership(), "changed container link fails closed");

initial = Setup();
var reused = Pair(2, 47, "reuse");
EntitySerialisationRegistry.Set(initial.Chef, reused.Owner, reused.Body, initial.Owner3, initial.Body48);
Reject(() => NativeSceneMetadata.CaptureCurrentInitialMembership(), "unproved same-ID incarnation fails closed");

initial = Setup();
EntitySerialisationRegistry.Set(initial.Chef, initial.Owner3, initial.Body48);
var recreated = Pair(2, 47, "recreated");
EntitySerialisationRegistry.Set(initial.Chef, recreated.Owner, recreated.Body, initial.Owner3, initial.Body48);
NativeSceneMetadata.RebindInitialPhysicalAttachmentLineage(initial.Owner2, recreated.Owner, initial.Body47, recreated.Body);
membership = NativeSceneMetadata.CaptureCurrentInitialMembership();
Check(membership.RigidBodyIds.SetEquals(new[] { 43, 47, 48 })
    && membership.PhysicalAttachmentIds.SetEquals(new[] { 2, 3 }), "qualified recreation joins both lineages");
Reject(() => NativeSceneMetadata.RebindInitialPhysicalAttachmentLineage(initial.Owner2, recreated.Owner, initial.Body47, recreated.Body),
    "same recreation cannot commit twice");

initial = Setup();
EntitySerialisationRegistry.Set(initial.Chef, initial.Owner3, initial.Body48);
recreated = Pair(2, 47, "atomic");
EntitySerialisationRegistry.Set(initial.Chef, recreated.Owner, recreated.Body, initial.Owner3, initial.Body48);
Reject(() => NativeSceneMetadata.RebindInitialPhysicalAttachmentLineage(initial.Owner2, recreated.Owner,
    initial.Body47, initial.Body48), "invalid pair rebind rejects before either lineage changes");
NativeSceneMetadata.RebindInitialPhysicalAttachmentLineage(initial.Owner2, recreated.Owner, initial.Body47, recreated.Body);
checks.Add("both lineage sides remain available after rejected atomic commit");

initial = Setup();
Reject(() => NativeSceneMetadata.RequireCurrentInitialMembershipSubset(new[] { 43, 48 }, new[] { 3 }),
    "live fixed pair absent from target is rejected");
Reject(() => NativeSceneMetadata.QualifyTargetAbsentInitialBodyIds(new[] { 43, 48 }, new[] { 3 }),
    "live fixed pair cannot qualify as absent controller residue");
NativeSceneMetadata.InitialRigidBodyIds.Add(999);
Reject(() => NativeSceneMetadata.CaptureCurrentInitialMembership(), "mutable initial-set corruption fails closed");

initial = Setup();
var oldOwner = initial.Owner2; var oldBody = initial.Body47;
var newGeneration = Setup();
EntitySerialisationRegistry.Set(newGeneration.Chef, newGeneration.Owner3, newGeneration.Body48);
recreated = Pair(2, 47, "new-generation");
EntitySerialisationRegistry.Set(newGeneration.Chef, recreated.Owner, recreated.Body,
    newGeneration.Owner3, newGeneration.Body48);
Reject(() => NativeSceneMetadata.RebindInitialPhysicalAttachmentLineage(oldOwner, recreated.Owner, oldBody, recreated.Body),
    "refresh rejects a prior-generation lineage source");

Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { ok = true, count = checks.Count, checks }));
