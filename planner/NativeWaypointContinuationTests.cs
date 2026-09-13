using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public static class NativeWaypointContinuationTests
{
    public static int Run()
    {
        int count = 0;
        void Check(bool yes, string message) { if (!yes) throw new Exception(message); count++; }
        JsonObject State() => Json.Object("""
        {"scene":"waypoint-fixture","fixedDeltaTime":0.02,"unityDeltaTime":0.016666667,"framesSinceNoPhysics":0,"chefs":[
        {"playerId":0,"entityId":101,"position":{"x":0,"y":0,"z":0},"forward":{"x":1,"y":0,"z":0},"lastVelocity":{"x":0,"y":0,"z":0},"runSpeed":6,"dashSpeed":18,"dashDuration":0.3,"dashCooldown":0.4,"movementScale":1,"maxSpeed":18,"turnSpeed":20,"surfaceSpeedMultiplier":1,"surfaceSlippiness":0,"surfaceSlidiness":0,"groundNormal":{"x":0,"y":1,"z":0},"surfaceVelocity":{"x":0,"y":0,"z":0},"windVelocity":{"x":0,"y":0,"z":0},"dashTimer":-20,"impactTimer":-1,"controlsEnabled":true,"directlyControlled":true,"canAcceptInput":true,"inputSuppressed":false,"aimingThrow":false,"respawning":false,"interactingEntityId":0}]}
        """);
        (RouteRunner Runner, RouteActionHandle Action) Action(bool enabled, Point2[] points, KitchenObstacle[]? obstacles = null)
        {
            var runner = new RouteRunner(_ => throw new Exception("Offline waypoint test cannot call the game"), null);
            var action = runner.CreateAction(new JsonObject { ["player"] = 0, ["type"] = "navigate", ["timeoutFrames"] = 300,
                ["target"] = new JsonObject { ["x"] = points[^1].X, ["z"] = points[^1].Z }, ["continuousWaypoints"] = enabled });
            action.Action.Motion = new RouteRunner.RouteMotion { Model = new KitchenModel { ChefRadius = .4, Clearance = .03,
                Regions = [new("test", new(-10, 10, -10, 10), "Synthetic measured native movement contract")], Obstacles = obstacles ?? [] },
                Points = points, Waypoint = 1, Target = points[^1] };
            return (runner, action);
        }
        (int Frames, int Continued) Simulate(bool enabled, int phase, Point2[] points)
        {
            var (runner, action) = Action(enabled, points); var state = State(); var chef = state["chefs"]![0]!;
            float x = 0, z = 0, vx = 0, vz = 0; int frames = 0, continued = 0;
            for (; frames < 310 && !action.IsDone; frames++)
            {
                state["framesSinceNoPhysics"] = (frames + phase) % 6;
                int old = action.Action.Motion!.Waypoint; bool braking = action.Action.Motion.Braking;
                var input = runner.Tick(action, state);
                if (!braking && action.Action.Motion.Waypoint > old) continued++;
                if ((frames + phase) % 6 != 5) { x += vx * .02f; z += vz * .02f; }
                float dx = (float)KitchenModel.N(input["x"]), dz = -(float)KitchenModel.N(input["y"]);
                float length = MathF.Sqrt(dx * dx + dz * dz);
                vx = length > 0 ? dx / length * 6 : 0; vz = length > 0 ? dz / length * 6 : 0;
                chef["position"]!["x"] = (double)x; chef["position"]!["z"] = (double)z;
                chef["lastVelocity"]!["x"] = (double)vx; chef["lastVelocity"]!["z"] = (double)vz;
            }
            Check(action.IsDone && action.Error is null && new Point2(x,z).Distance(points[^1]) <= .10001 && vx == 0 && vz == 0,
                "Native float32 queued velocity reaches the final target settled in every phase");
            return (frames, continued);
        }
        foreach (int phase in Enumerable.Range(0,6))
            foreach (Point2[] path in new Point2[][] { [new(0,0), new(2,0), new(2,2)], [new(0,0), new(2,0), new(4,2)], [new(0,0), new(2,0), new(2,-2)] })
            {
                var baseline = Simulate(false, phase, path); var candidate = Simulate(true, phase, path);
                Check(candidate.Continued == 1 && baseline.Continued == 0 && candidate.Frames < baseline.Frames,
                    "Clear intermediate corners avoid one settle while final targets retain braking");
            }
        bool Corner(Action<JsonObject, RouteActionHandle> alter, KitchenObstacle[]? obstacles = null)
        {
            var state = State(); var chef = state["chefs"]![0]!; chef["position"]!["x"] = 1.92; chef["lastVelocity"]!["x"] = 6;
            var (runner, action) = Action(true, [new(0,0), new(2,0), new(2,2)], obstacles); alter(state,action);
            runner.Tick(action,state); return action.Action.Motion!.Waypoint == 2;
        }
        Check(Corner((s,a)=>{}), "Clear walking corner is admitted");
        Check(!Corner((s,a)=>a.Action.Spec["continuousWaypoints"]=false), "Default/disabled mode retains old input path");
        Check(!Corner((s,a)=>a.Action.Motion!.Points=[new(0,0),new(2,0)]), "Final endpoint always brakes");
        Check(!Corner((s,a)=>s["chefs"]![0]!["dashTimer"]=.2), "Active native dash must settle through existing dash controller");
        Check(!Corner((s,a)=>s["chefs"]![0]!["lastVelocity"]!["x"]=18), "Unmodeled fast cached displacement rejected");
        Check(!Corner((s,a)=>s["chefs"]![0]!["controlsEnabled"]=false), "Closed native controls rejected");
        Check(!Corner((s,a)=>s["chefs"]![0]!["surfaceSlippiness"]=.5), "Unmodeled slippery ground rejected");
        Check(!Corner((s,a)=>s.AsObject().Remove("framesSinceNoPhysics")), "Missing physics phase rejected");
        Check(!Corner((s,a)=>s["fixedDeltaTime"]=.01), "Changed native physics clock rejected");
        Check(!Corner((s,a)=>s["chefs"]![0]!.AsObject().Remove("lastVelocity")), "Missing queued velocity rejected");
        Check(!Corner((s,a)=>a.Action.Spec["ignoreChefs"]=true), "Live chef clearance cannot be bypassed");
        Check(!Corner((s,a)=>{}, [new("next-lane-blocked",200,new(1.5,2.5,.5,1.5),"Solid next segment")]), "Blocked next corridor retains braking");
        var configured = new RouteRunner(_ => throw new Exception("No game calls"), null);
        var spec = new JsonObject { ["type"] = "navigate", ["player"] = 0 };
        Check(configured.CreateAction(spec).Specification["continuousWaypoints"] is null, "Default runner preserves the original action specification");
        configured.ContinuousWaypoints = true;
        Check(configured.CreateAction(spec).Specification["continuousWaypoints"]!.GetValue<bool>() && spec["continuousWaypoints"] is null,
            "Configured runner records its option on an independent action copy");
        spec["continuousWaypoints"] = false;
        Check(!configured.CreateAction(spec).Specification["continuousWaypoints"]!.GetValue<bool>(), "Explicit per-action disabling is retained");
        return count;
    }
}
