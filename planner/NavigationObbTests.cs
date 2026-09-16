using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public static class NavigationObbTests
{
    public static int Run()
    {
        int checks=0;
        void Check(bool condition,string label){if(!condition)throw new Exception("FAIL: "+label);checks++;}
        // Observed returned bowl from planner-four-a-state.json: its collider
        // belongs to this exact entity root and is upright, not an unknown child.
        var entity=Json.Object("""{"hierarchyPath":"station/Attach/bowl","rotation":{"x":0,"y":0.158900067,"z":0,"w":0.9872947}}""");
        var collider=Json.Object("""{"hierarchyPath":"station/Attach/bowl","type":"BoxCollider","trigger":false,"layer":14,"center":{"x":23.9919968,"y":0.85,"z":-10.9949989},"size":{"x":0.884285,"y":0.5,"z":0.884285}}""");
        var shape=KitchenModel.ReconstructOrientedBox(collider,entity);
        Check(shape is not null&&Math.Abs(shape.Value.HalfX-.35002)<.00001&&Math.Abs(shape.Value.HalfZ-.35002)<.00001,"native AABB and observed quaternion recover measured .7-square bowl");
        var box=shape!.Value;var p=KitchenModel.Position(collider["center"]);var size=KitchenModel.Position(collider["size"]);
        var obstacle=new KitchenObstacle("bowl",6,new(p.X-size.X/2,p.X+size.X/2,p.Z-size.Z/2,p.Z+size.Z/2),"measured bowl",Oriented:box);
        var model=new KitchenModel{ChefRadius=.4,Regions=[new("test",new(10,30,-25,-5),"fixture floor")],Obstacles=[obstacle]};
        var start=new Point2(23.688467,-11.7999954);var target=new Point2(22.6,-13);
        Check(Math.Abs(Math.Sqrt(Navigation.ObstacleDistanceSquared(start,obstacle))-.50958)<.0001,"actual rotated-box clearance is measured rather than AABB overlap");
        var aabbModel=new KitchenModel{ChefRadius=.4,Regions=model.Regions,Obstacles=[obstacle with{Oriented=null}]};
        Check(!Navigation.FindPath(aabbModel,start,target).Success,"regression fixture reproduces old AABB false blockage");
        var path=Navigation.FindPath(model,start,target);
        Check(path.Success&&path.Points.Length==2,"native-clear returned-bowl start can depart directly");
        Check(path.Points.Zip(path.Points.Skip(1),(a,b)=>Navigation.SegmentClear(model,a,b,"test")).All(v=>v),"departure sweep retains full circle clearance");
        Check(!Navigation.IsWalkable(model,p,"test"),"layer14 attachment remains a physical obstacle");
        Check(!Navigation.SegmentClear(model,new(p.X-2,p.Z),new(p.X+2,p.Z),"test"),"swept circle cannot pass through a rotated box");
        Point2 World(double x,double z)=>new(p.X+box.CosYaw*x+box.SinYaw*z,p.Z-box.SinYaw*x+box.CosYaw*z);
        var contact=World(0,-box.HalfZ-model.ChefRadius+.00001);
        Check(Navigation.SegmentClear(model,contact,World(0,-box.HalfZ-model.ChefRadius-.2),"test"),"observed native-radius contact can leave an oriented box outward");
        Check(!Navigation.SegmentClear(model,contact,World(0,-box.HalfZ-model.ChefRadius+.1),"test"),"oriented contact recovery cannot move inward");
        var changed=collider.DeepClone().AsObject();changed["hierarchyPath"]="station/Attach/bowl/unknown-local-child";
        Check(KitchenModel.ReconstructOrientedBox(changed,entity) is null,"unknown child-local rotation retains conservative AABB");
        changed=collider.DeepClone().AsObject();changed["type"]="SphereCollider";
        Check(KitchenModel.ReconstructOrientedBox(changed,entity) is null,"non-box collider is never reconstructed as box");
        changed=collider.DeepClone().AsObject();changed["trigger"]=true;
        Check(KitchenModel.ReconstructOrientedBox(changed,entity) is null,"trigger proxy does not assert native solid orientation");
        var tilted=entity.DeepClone().AsObject();tilted["rotation"]=new JsonObject{["x"]=.1,["y"]=0,["z"]=0,["w"]=Math.Sqrt(.99)};
        Check(KitchenModel.ReconstructOrientedBox(collider,tilted) is null,"tilted collider retains AABB");
        var singular=entity.DeepClone().AsObject();singular["rotation"]=new JsonObject{["x"]=0,["y"]=Math.Sin(Math.PI/8),["z"]=0,["w"]=Math.Cos(Math.PI/8)};
        Check(KitchenModel.ReconstructOrientedBox(collider,singular) is null,"near-45-degree ill-conditioned inverse retains AABB");
        changed=collider.DeepClone().AsObject();changed["size"]!["x"]=.1;changed["size"]!["z"]=10;
        Check(KitchenModel.ReconstructOrientedBox(changed,entity) is null,"inconsistent size/orientation cannot produce a negative-size box");
        var invalid=entity.DeepClone().AsObject();invalid["rotation"]!["w"]=10;
        Check(KitchenModel.ReconstructOrientedBox(collider,invalid) is null,"invalid quaternion retains AABB");
        Console.WriteLine($"PASS: {checks} oriented-box navigation safety assertions.");
        return checks;
    }
}
