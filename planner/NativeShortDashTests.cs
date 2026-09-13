using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public static class NativeShortDashTests
{
    public static int Run()
    {
        int checks=0;
        void Check(bool value,string label){if(!value)throw new Exception("FAIL: "+label);checks++;}
        var profile=new NativeDashProfile(6,18,.3,.4,1,18,1);
        // This independent float32 loop follows the installed native source:
        // FixedUpdate consumes the preceding cached vector; Update rotates,
        // computes the sinusoidal blend, decrements time, then claims dash edge.
        double NativeNeutralTravel(int phase,float initialCache)
        {
            float timer=-20,cached=initialCache,x=0;const float delta=1f/60f;
            for(int frame=0;frame<40;frame++)
            {
                if((phase+frame)%6!=5)x+=cached*.02f;
                float direction=frame==0?1:0;
                float blend=timer>0?.5f*(1-MathF.Cos(MathF.PI*Math.Clamp(timer/.3f,0,1))):0;
                cached=Math.Clamp((1-blend)*direction*6+blend*18,0,18);
                timer-=delta;if(frame==0)timer=.3f;
            }
            return x;
        }
        foreach(int phase in Enumerable.Range(0,6))foreach(float cached in new[]{0f,6f,18f})
            Check(NativeNeutralTravel(phase,cached)<=profile.ShortCoastTravelBound(cached),"neutral-coast bound covers native float32 cached velocity and each 60/50 phase");
        Check(profile.ShortCoastTravelBound(6)+.15<3.274&&profile.ShortCoastTravelBound(6)+.15>3.273,"measured native short admission requires about3.274 clear units");
        Check(Enumerable.Range(0,6).Max(p=>NativeNeutralTravel(p,6))>2.8,"continuous2.7-unit estimate cannot justify generic2.8-unit admission");
        JsonObject State(float initialCache)=>Json.Object($$"""
        {"scene":"short-dash-fixture","fixedDeltaTime":0.02,"unityDeltaTime":0.016666667,"framesSinceNoPhysics":0,"chefs":[
        {"playerId":0,"entityId":101,"position":{"x":0,"y":0,"z":0},"forward":{"x":1,"y":0,"z":0},"lastVelocity":{"x":{{initialCache.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"y":0,"z":0},"runSpeed":6,"dashSpeed":18,"dashDuration":0.3,"dashCooldown":0.4,"movementScale":1,"maxSpeed":18,"turnSpeed":20,"surfaceSpeedMultiplier":1,"surfaceSlippiness":0,"surfaceSlidiness":0,"groundNormal":{"x":0,"y":1,"z":0},"surfaceVelocity":{"x":0,"y":0,"z":0},"windVelocity":{"x":0,"y":0,"z":0},"dashTimer":-20,"impactTimer":-1,"controlsEnabled":true,"directlyControlled":true,"canAcceptInput":true,"inputSuppressed":false,"aimingThrow":false,"respawning":false,"interactingEntityId":0}]}
        """);
        (int Frames,int Edges,bool Immediate,double Furthest) Simulate(int phase,float initialCache,double target,bool shortEnabled,bool obstacle=false)
        {
            var state=State(initialCache);var chef=state["chefs"]![0]!;
            var runner=new RouteRunner(_=>throw new Exception("Short dash fixture must never call live game"),null);
            var action=runner.CreateAction(new JsonObject{["player"]=0,["type"]="navigate",["target"]=new JsonObject{["x"]=target,["z"]=0},["dash"]=true,["shortDash"]=shortEnabled,["timeoutFrames"]=180});
            action.Action.Motion=new RouteRunner.RouteMotion{Model=new KitchenModel{ChefRadius=.4,Clearance=.03,Regions=[new("fixture",new(-20,20,-20,20),"Synthetic straight-corridor fixture")],Obstacles=obstacle?[new("blocking-counter",1,new(1,2,-.5,.5),"fixture solid counter")]:[]},Points=[new(0,0),new(target,0)],Waypoint=1,Target=new(target,0)};
            float x=0,cached=initialCache,timer=-20,forward=1;int frames=0,edges=0;bool immediate=false,previousDash=false;double furthest=0;
            for(;frames<190&&!action.IsDone;frames++)
            {
                state["framesSinceNoPhysics"]=(frames+phase)%6;
                var input=runner.Tick(action,state);bool dash=input["dash"]!.GetValue<bool>();
                if(dash){Check(!previousDash,"short dash preserves one claimed edge");if(edges==0)immediate=action.Action.Motion!.Dash?.ImmediateNeutralCoast==true;edges++;}
                if(action.Action.Motion?.Dash?.ImmediateNeutralCoast==true&&!dash)
                    Check(KitchenModel.N(input["x"])==0&&KitchenModel.N(input["y"])==0,"short admission commits every subsequent active-dash Update to neutral axes");
                previousDash=dash;
                if((frames+phase)%6!=5)x+=cached*.02f;
                float direction=(float)Math.Sign(KitchenModel.N(input["x"]));if(direction!=0)forward=direction;
                float blend=timer>0?.5f*(1-MathF.Cos(MathF.PI*Math.Clamp(timer/.3f,0,1))):0;
                cached=Math.Clamp((1-blend)*direction*6+blend*forward*18,-18,18);timer-=1f/60f;
                if(dash&&.3f-timer>=.4f)timer=.3f;
                chef["position"]!["x"]=(double)x;chef["lastVelocity"]!["x"]=(double)cached;chef["forward"]!["x"]=(double)forward;chef["dashTimer"]=(double)timer;
                furthest=Math.Max(furthest,x);
                if(obstacle&&frames>0)break;
            }
            if(!obstacle)Check(action.IsDone&&action.Error is null&&Math.Abs(target-x)<=.10001&&Math.Abs(cached)<.0001,"native short dash finishes settled without position correction");
            return(frames,edges,immediate,furthest);
        }
        foreach(int phase in Enumerable.Range(0,6))foreach(float cached in new[]{0f,6f})
        {
            var walking=Simulate(phase,cached,4.1,false);var shorter=Simulate(phase,cached,4.1,true);
            Check(walking.Edges==0&&shorter.Edges==1&&shorter.Immediate&&shorter.Frames<walking.Frames,"4.10-unit pantry length gains from optional immediate coasting in every measured phase");
            Check(shorter.Furthest<=4.20001,"short-dash pantry route does not overshoot endpoint tolerance");
        }
        Check(Simulate(0,6,2.8,true).Edges==0,"unsafe2.8-unit segment remains walking even with shortDash enabled");
        Check(Simulate(0,6,4.1,true,true).Edges==0,"short mode cannot bypass solid geometry guard");
        var full=Simulate(0,0,8,true);Check(full.Edges>=1&&!full.Immediate,"long routes retain existing full-directional dash strategy");
        bool FirstEdge(Action<JsonObject> alter)
        {
            var state=State(6);alter(state["chefs"]![0]!.AsObject());
            var runner=new RouteRunner(_=>throw new Exception("No live calls"),null);
            var action=runner.CreateAction(new JsonObject{["player"]=0,["type"]="navigate",["dash"]=true,["shortDash"]=true,["target"]=new JsonObject{["x"]=4.1,["z"]=0}});
            action.Action.Motion=new RouteRunner.RouteMotion{Model=new KitchenModel{ChefRadius=.4,Regions=[new("fixture",new(-20,20,-20,20),"fixture")]},Points=[new(0,0),new(4.1,0)],Waypoint=1,Target=new(4.1,0)};
            return runner.Tick(action,state)["dash"]!.GetValue<bool>();
        }
        Check(!FirstEdge(c=>c.Remove("turnSpeed")),"short admission requires measured native turning speed");
        Check(!FirstEdge(c=>{c["turnSpeed"]=.01;c["forward"]!["x"]=Math.Cos(Math.PI/90);c["forward"]!["z"]=Math.Sin(Math.PI/90);}),"slow first-frame rotation cannot leave persistent sideways coast");
        Check(!FirstEdge(c=>{c["lastVelocity"]!["x"]=0;c["lastVelocity"]!["z"]=6;}),"unrelated queued sideways motion prevents short admission");
        Check(!FirstEdge(c=>c["lastVelocity"]!["x"]=18),"unexpected above-walking cached speed prevents short admission");
        Console.WriteLine($"PASS: {checks} optional short-dash/native-float phase assertions.");
        return checks;
    }
}
