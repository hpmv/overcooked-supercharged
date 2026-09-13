using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public static class NavigationContactTests
{
    public static int Run()
    {
        int checks=0;
        void Check(bool condition,string label){if(!condition)throw new Exception("FAIL: "+label);checks++;}
        // Directly measured previous states for first discrete input drift:
        // planner-four-b versus native-eight-a, gameplay frame1666.
        var contact=new Point2(19.5255013,-14.6019974);
        var destination=new Point2(20.05,-11.870000152);
        var counter=new KitchenObstacle("counter38",38,new(18.59999962,19.80000038,-16.19999888,-14.99999812),"recorded active native box AABB");
        double distance=Math.Sqrt(Navigation.ObstacleDistanceSquared(contact,counter));
        Check(Math.Abs(distance-.39800072000000064)<1e-12,"captured counter contact distance");
        foreach(double radius in new[]{.400001526,.400000572})
        {
            var model=new KitchenModel{ChefRadius=radius,Regions=[new("center",new(15.6,25.2,-21.6,-10.8),"measured platform")],Obstacles=[counter]};
            Check(Navigation.FindPath(model,contact,destination).Success,"both captured capsule radii permit outward departure");
            Check(Navigation.SegmentClear(model,contact,destination,"center"),"swept outward contact uses the same bounded allowance");
            Check(!Navigation.IsWalkable(model,contact,"center"),"existing contact is not admitted as a newly approached destination");
            Check(!Navigation.SegmentClear(model,destination,contact,"center"),"full destination clearance remains unchanged");
            var deeper=new Point2(contact.X,counter.Bounds.MaxZ+radius-.002-Navigation.DepartureNumericAllowance-.00001);
            Check(!Navigation.FindPath(model,deeper,destination).Success,"physical penetration beyond allowance still rejects departure");
            Check(!Navigation.SegmentClear(model,contact,new(19.5,-17),"center"),"contact cannot travel inward through the counter");
            var crossing=new KitchenObstacle("other",99,new(19,21,-13,-12.7),"new physical obstacle");
            Check(!Navigation.SegmentClear(model,contact,destination,"center",[crossing]),"allowance cannot cross another obstacle");
        }
        Console.WriteLine($"PASS: {checks} numeric-contact navigation safety assertions.");
        return checks;
    }

    // Optional actual-snapshot regression; reads the diagnostic artifact only.
    // No network, game calls, or snapshot mutation occurs.
    public static int RunCapturedPair(string path)
    {
        var pair=JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        if(pair.Count!=2)throw new ArgumentException("Expected the captured pair of request-difference records.");
        foreach(var row in pair)
        {
            var state=row!["previousState"]!.AsObject();
            if(state["gameplayFrame"]!.GetValue<int>()!=1666)throw new ArgumentException("Expected actual gameplay frame1666.");
            var model=KitchenModel.Build(state);
            var chef=state["chefs"]!.AsArray().Single(c=>c!["playerId"]!.GetValue<int>()==0)!;
            var position=KitchenModel.Position(chef["position"]);
            var station=model.Stations.Single(s=>s.EntityId==72);
            var result=Navigation.ToStation(model,position,station);
            if(!result.Success)throw new Exception("FAIL: captured geometry still rejects outward departure: "+result.Error);
            if(!Navigation.SegmentClear(model,position,result.Points[1],"center"))throw new Exception("FAIL: captured first segment rejects departure.");
        }
        Console.WriteLine("PASS: both actual frame1666 snapshots admit a safe outward departure.");
        return 4;
    }
}
