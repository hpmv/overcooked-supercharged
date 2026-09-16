using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public static class NavigationTests
{
    public static void Run(KitchenModel model,JsonObject snapshot)
    {
        int checks=0;
        void Check(bool condition,string message){if(!condition)throw new Exception("FAIL: "+message);checks++;}
        Check(model.Regions.Length==5,"five platforms inferred");
        Check(Math.Abs(model.TilePitch-1.2)<.02,"grid pitch measured from runtime");
        Check(Math.Abs(model.ChefRadius-.4)<.02,"chef collider radius measured");
        var center=model.Regions.Single(r=>r.Id=="center").FloorBounds;
        Check(Math.Abs(center.MinX-15.6)<.02&&Math.Abs(center.MaxX-25.2)<.02,"inner counter wall columns");
        Check(model.RegionAt(new(12,-16)) is null&&model.RegionAt(new(29,-16)) is null,"both void strips excluded");
        Check(model.Stations.All(s=>!s.Name.EndsWith("_Rigidbody",StringComparison.Ordinal)),"physics clones excluded");
        Check(model.Stations.Count(s=>s.Role=="ingredient-source")==7,"seven ingredient crates resolved");
        Check(model.Resolve("Flour").Regions.Contains("upper-right"),"flour region derived");
        Check(model.Stations.Count(s=>s.Role=="chop")==4,"four boards resolved");
        Check(model.Resolve("sink").Regions.Contains("lower-left"),"sink region derived");
        Check(model.Resolve("delivery").Regions.Contains("lower-right"),"delivery region derived");
        Check(model.Stations.SelectMany(s=>s.Approaches).All(a=>Navigation.IsWalkable(model,a.Position,a.Region)),"every generated approach collision-free");
        Check(!Navigation.FindPath(model,new(12,-13),new(12,-19)).Success,"cross-void walk refused");
        Check(!Navigation.FindPath(model,new(12,-13),new(18,-13)).Success,"counter-boundary crossing refused");
        Check(!Navigation.FindPath(model,new(18,-18),new(20.4,-16)).Success,"plate island target blocked");
        var around=Navigation.FindPath(model,new(18,-18),new(23,-14));
        Check(around.Success&&around.Points.Length>=3,"A-star routes around plate island");
        Check(around.Points.Zip(around.Points.Skip(1),(a,b)=>Navigation.SegmentClear(model,a,b,"center")).All(x=>x),"every smoothed segment collision-free");
        var safe=Navigation.FindPath(model,new(17,-19),new(23,-19));
        Check(safe.Success&&safe.Points.Length==2,"open corridor simplifies to direct segment");
        var chef=KitchenModel.SnapshotState(snapshot)["chefs"]!.AsArray().OfType<JsonObject>().Single(c=>c["playerId"]!.GetValue<int>()==0);
        Check(Navigation.ToStation(model,KitchenModel.Position(chef["position"]),model.Stations.First(s=>s.Role=="cook-station")).Success,"observed central chef can approach cooking station");
        Check(model.Transitions.Count(t=>t.Kind=="cannon")==2&&model.Transitions.Count(t=>t.Kind=="portal")==2,"transition sources discovered");
        Check(model.Transitions.Where(t=>t.ToRegion is null).All(t=>!t.Verified),"missing destination regions never asserted verified");
        Check(Navigation.IsWalkable(model,new(13.16,-14.4),"upper-left"),"native cannon landing tile includes its open half-tile edge");
        var crate=model.Resolve("Frankfurter");var crateObstacle=model.Obstacles.First(o=>o.EntityId==crate.EntityId);
        var contact=new Point2(crate.Position.X,crateObstacle.Bounds.MinZ-model.ChefRadius+.00001);
        var departed=new Point2(contact.X,contact.Z-.2);
        Check(Navigation.FindPath(model,contact,departed).Success,"observed native collider contact can depart outward");
        Check(!Navigation.SegmentClear(model,contact,new(contact.X,contact.Z+.1),"upper-left"),"contact escape cannot move into a station");

        var expanded=snapshot.DeepClone().AsObject();var expandedState=KitchenModel.SnapshotState(expanded);
        var expandedEntities=expandedState["entities"]!.AsArray().OfType<JsonObject>().ToArray();
        var chefEntity=expandedEntities.First(e=>KitchenModel.Components(e).Contains("PlayerIDProvider"));
        var chefColliders=chefEntity["colliders"]!.AsArray();
        chefColliders.Add(Json.Object("""
        {"type":"CapsuleCollider","enabled":true,"active":true,"trigger":false,"layer":15,"hierarchyPath":"Chefs/Player 1/Attach/TestFood","center":{"x":18,"y":1,"z":-17},"size":{"x":1.8,"y":0.3,"z":0.6}}
        """));
        chefColliders.Add(Json.Object("""
        {"type":"CapsuleCollider","enabled":true,"active":false,"trigger":false,"layer":8,"center":{"x":18,"y":1,"z":-17},"size":{"x":3,"y":2,"z":3}}
        """));
        var expandedModel=KitchenModel.Build(expanded);
        Check(Math.Abs(expandedModel.ChefRadius-model.ChefRadius)<.000001,"held child capsules and inactive capsules do not inflate chef radius");
        var passenger=expandedState["chefs"]!.AsArray().OfType<JsonObject>().Single(c=>c["entityId"]!.GetValue<int>()==chefEntity["id"]!.GetValue<int>());
        passenger["controlsEnabled"]=false;
        var rootCapsule=chefColliders.OfType<JsonObject>().First(c=>c["type"]?.ToString()=="CapsuleCollider");
        rootCapsule["size"]!["x"]=2.4;
        Check(Math.Abs(KitchenModel.Build(expanded).ChefRadius-model.ChefRadius)<.000001,"disabled sideways cannon passenger does not inflate active chef radius");
        var leftPortal=expandedModel.Stations.Single(s=>s.Role=="portal"&&s.Regions.Contains("lower-left"));
        var rightReceiver=expandedModel.Stations.Single(s=>s.Role=="portal-receiver"&&s.Regions.Contains("upper-right"));
        var nativePortal=expandedEntities.Single(e=>e["id"]!.GetValue<int>()==leftPortal.EntityId);
        nativePortal["portalDestinationId"]=rightReceiver.EntityId;nativePortal["portalSenderCount"]=1;
        var linked=KitchenModel.Build(expanded).Transitions.Single(t=>t.SourceKey==leftPortal.Key);
        Check(linked.Verified&&linked.ToRegion=="upper-right","native portal destinations become verified cross-platform graph edges");
        Console.WriteLine($"PASS: {checks} measured-navigation assertions.");
    }
}
