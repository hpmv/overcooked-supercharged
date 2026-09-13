using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public static class NavigationCapsuleTests
{
    /// <summary>Native recorded capsule contacts and analytic collision cases; never issues game requests.</summary>
    public static int SelfTest(JsonObject v7Snapshot, JsonObject priorSnapshot, JsonObject? pantrySnapshot=null)
    {
        int count=0;
        void Check(bool value,string description)
        {if(!value)throw new InvalidOperationException("Capsule navigation regression: "+description);count++;}
        var floor=new KitchenModel {ChefRadius=.4,Clearance=.03,Regions=[new("floor",new(-5,5,-5,5),"fixture")]};
        var circle=new KitchenObstacle("chef",1,new(-.4,.4,-.4,.4),"vertical native capsule",true,CircleRadius:.4);
        var square=circle with {CircleRadius=null};
        Check(Navigation.SegmentClear(floor,new(.6,.6),new(1.5,1.5),"floor",[circle]),"valid diagonal capsule separation permits departure");
        Check(!Navigation.SegmentClear(floor,new(.6,.6),new(1.5,1.5),"floor",[square]),"the former square corner explains the false overlap");
        Check(!Navigation.IsWalkable(floor,new(.55,.55),"floor",[circle]),"overlapping physical chef circles are never walkable");
        Check(!Navigation.SegmentClear(floor,new(-2,0),new(2,0),"floor",[circle]),"a path through another chef's center is rejected");
        Check(!Navigation.SegmentClear(floor,new(-2,.8299),new(2,.8299),"floor",[circle]),"new paths preserve both capsule radii and the full .03 clearance");
        Check(Navigation.SegmentClear(floor,new(-2,.8301),new(2,.8301),"floor",[circle]),"a path beyond the full circular clearance is legal");
        Check(Navigation.SegmentClear(floor,new(.81,0),new(1.2,0),"floor",[circle]),"an existing native contact can depart outward");
        Check(!Navigation.SegmentClear(floor,new(.81,0),new(-1.2,0),"floor",[circle]),"an existing contact cannot depart through the other chef");
        Check(!Navigation.SegmentClear(floor,new(.79,0),new(1.2,0),"floor",[circle]),"a physically overlapping start is rejected instead of silently relaxed");

        var fixtures=new List<(JsonObject Snapshot,(int Player,int Station)[] Targets,string Name)>
        {
            (v7Snapshot,[(0,4),(3,79)],"V7"), (priorSnapshot,[(0,2),(3,56)],"eight-v3")
        };
        if(pantrySnapshot is not null)fixtures.Add((pantrySnapshot,[(3,79)],"pantry-only"));
        foreach(var fixture in fixtures)
        {
            var state=KitchenModel.SnapshotState(fixture.Snapshot);
            var model=KitchenModel.Build(state);
            var chefs=state["chefs"]!.AsArray().OfType<JsonObject>().ToArray();
            Check(model.ChefRadius>=.39999&&model.ChefRadius<.401,"recorded "+fixture.Name+" radius comes from the native capsule telemetry");
            foreach(var target in fixture.Targets)
            {
                var chef=chefs.Single(c=>c["playerId"]!.GetValue<int>()==target.Player);
                var start=KitchenModel.Position(chef["position"]);
                var obstacles=chefs.Where(c=>c!=chef).Select(c=>
                {
                    var p=KitchenModel.Position(c["position"]);double radius=model.ChefRadius;
                    return new KitchenObstacle("chef:"+c["playerId"],c["entityId"]!.GetValue<int>(),new(p.X-radius,p.X+radius,p.Z-radius,p.Z+radius),
                        "recorded native capsule",true,CircleRadius:radius);
                }).ToArray();
                var station=model.Stations.Single(s=>s.EntityId==target.Station);
                var path=Navigation.ToStation(model,start,station,obstacles);
                if(fixture.Name=="eight-v3"&&target.Player==3)
                {
                    Check(!path.Success,"the prior narrow station remains blocked until the other chef clears its approach");
                    continue;
                }
                Check(path.Success,fixture.Name+" chef "+target.Player+" can reach its original native interaction station with circular footprints");
                var runner=new RouteRunner(_=>throw new InvalidOperationException("Offline capsule regression attempted I/O."),null);
                var action=runner.CreateAction(new JsonObject { ["type"]="navigate",["player"]=target.Player,["station"]=target.Station.ToString(),["dash"]=false });
                var input=runner.Tick(action,state);
                Check(action.Error is null&&action.Action.Motion?.Points.Length>0&&
                    new[]{"pickup","use","dash","throw"}.All(key=>input[key]?.GetValue<bool>()!=true),
                    fixture.Name+" real route initialization uses circular obstacles and emits only ordinary movement");
                Check(path.Points.Zip(path.Points.Skip(1)).All(pair=>Navigation.SegmentClear(model,pair.First,pair.Second,path.Region!,obstacles)),
                    fixture.Name+" path retains complete static and dynamic segment collision checks");
                // Independently sample every path segment against the actual
                // center-distance sum. This does not certify live timing.
                double minimum=double.PositiveInfinity;
                foreach(var pair in path.Points.Zip(path.Points.Skip(1)))
                for(int i=0;i<=100;i++)
                {
                    var p=new Point2(pair.First.X+(pair.Second.X-pair.First.X)*i/100,pair.First.Z+(pair.Second.Z-pair.First.Z)*i/100);
                    foreach(var other in chefs.Where(c=>c!=chef))minimum=Math.Min(minimum,p.Distance(KitchenModel.Position(other["position"])));
                }
                Check(minimum>=2*model.ChefRadius-.0021,fixture.Name+" sampled route never penetrates native physical chef circles");
            }
        }
        return count;
    }
}
