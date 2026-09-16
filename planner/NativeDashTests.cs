using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public static class NativeDashTests
{
    public static int Run()
    {
        int checks=0;
        void Check(bool value,string label){if(!value)throw new Exception("FAIL: "+label);checks++;}
        JsonObject State()=>Json.Object("""
        {"scene":"dash-fixture","fixedDeltaTime":0.02,"unityDeltaTime":0.016666667,"framesSinceNoPhysics":0,"chefs":[
        {"playerId":0,"entityId":101,"position":{"x":0,"y":0,"z":0},"forward":{"x":1,"y":0,"z":0},"lastVelocity":{"x":0,"y":0,"z":0},"runSpeed":6,"dashSpeed":12,"dashDuration":0.25,"dashCooldown":0.8,"movementScale":1,"maxSpeed":12,"surfaceSpeedMultiplier":1,"surfaceSlippiness":0,"surfaceSlidiness":0,"groundNormal":{"x":0,"y":1,"z":0},"surfaceVelocity":{"x":0,"y":0,"z":0},"windVelocity":{"x":0,"y":0,"z":0},"dashTimer":-20,"impactTimer":-1,"controlsEnabled":true,"directlyControlled":true,"canAcceptInput":true,"inputSuppressed":false,"aimingThrow":false,"respawning":false,"interactingEntityId":0}]}
        """);
        (RouteRunner,RouteActionHandle) Action(JsonObject state,double target=8)
        {
            var runner=new RouteRunner(_=>throw new Exception("Dash fixture called live game."),null);
            var handle=runner.CreateAction(new JsonObject{["player"]=0,["type"]="navigate",["target"]=new JsonObject{["x"]=target,["z"]=0},["dash"]=true,["timeoutFrames"]=180});
            handle.Action.Motion=new RouteRunner.RouteMotion{Model=new KitchenModel{ChefRadius=.4,Clearance=.03,Regions=[new("fixture",new(-20,20,-20,20),"Synthetic obstacle-free native-movement contract fixture")]},Points=[new(0,0),new(target,0)],Waypoint=1,Target=new(target,0)};
            return(runner,handle);
        }
        (int Edges,int First,int Frames,double X,string? Error) Simulate(JsonObject state,int phase,double target)
        {
            var(runner,action)=Action(state,target);var chef=state["chefs"]![0]!;
            double x=0,cached=0,timer=KitchenModel.N(chef["dashTimer"]),forward=1;
            int edges=0,first=-1,frames=0;bool previousDash=false;
            for(;frames<190&&!action.IsDone;frames++)
            {
                state["framesSinceNoPhysics"]=(frames+phase+5)%6;
                var input=runner.Tick(action,state);
                bool dash=input["dash"]!.GetValue<bool>();
                if(dash){Check(!previousDash,"dash commands preserve fresh press edges");edges++;if(first<0)first=frames;}
                previousDash=dash;
                if((frames+phase)%6!=0)x+=cached*.02;
                double direction=Math.Sign(KitchenModel.N(input["x"]));if(direction!=0)forward=direction;
                // Contract model follows the native source's order: movement
                // blend, timer decrement, then a new edge resets the timer.
                double blend=timer>0?.5*(1-Math.Cos(Math.PI*Math.Clamp(timer/.25,0,1))):0;
                cached=Math.Clamp((1-blend)*direction*6+blend*forward*12,-12,12);
                timer-=1.0/60;
                if(dash&&.25-timer>=.8)timer=.25;
                chef["position"]!["x"]=x;chef["lastVelocity"]!["x"]=cached;chef["forward"]!["x"]=forward;chef["dashTimer"]=timer;
            }
            Check(action.IsDone&&action.Error is null&&Math.Abs(target-x)<=.10001&&Math.Abs(cached)<.0001,"native dash blend and queued physics converge at a settled endpoint");
            return(edges,first,frames,x,action.Error);
        }
        foreach(int phase in Enumerable.Range(0,6))
        {
            var result=Simulate(State(),phase,8);
            Check(result.Edges>=1&&result.Frames<83,"optional native dashes improve a sufficiently long straight route in every physics phase");
        }
        Check(Simulate(State(),0,1).Edges==0,"short navigation cannot start a dash whose native travel will overshoot");
        var missing=State();missing["chefs"]![0]!.AsObject().Remove("dashSpeed");
        Check(Simulate(missing,0,2).Edges==0,"missing native speed telemetry leaves navigation walking");
        var cooldown=State();cooldown["chefs"]![0]!["dashTimer"]=-.1;
        Check(Simulate(cooldown,0,8).First>=26,"native cooldown timestamp is observed before a new dash");
        var slippery=State();slippery["chefs"]![0]!["surfaceSlippiness"]=.2;
        Check(Simulate(slippery,0,2).Edges==0,"unmodelled slippery surface prevents optional dash");
        var wrongHeading=State();wrongHeading["chefs"]![0]!["forward"]=new JsonObject{["x"]=0,["z"]=1};
        var(wrongRunner,wrongAction)=Action(wrongHeading);
        Check(!wrongRunner.Tick(wrongAction,wrongHeading)["dash"]!.GetValue<bool>(),"dash waits for observed heading alignment");
        var incoming=State();incoming["chefs"]!.AsArray().Add(Json.Object("""{"playerId":1,"entityId":102,"position":{"x":1.5,"z":2},"lastVelocity":{"x":0,"z":-12}}"""));
        var(incomingRunner,incomingAction)=Action(incoming);
        Check(!incomingRunner.Tick(incomingAction,incoming)["dash"]!.GetValue<bool>(),"another chef's observed future corridor prevents a dash collision");
        var impact=State();var(impactRunner,impactAction)=Action(impact);var firstInput=impactRunner.Tick(impactAction,impact);
        Check(firstInput["dash"]!.GetValue<bool>(),"clear measured corridor starts one native dash edge");
        impact["chefs"]![0]!["dashTimer"]=.25;impact["chefs"]![0]!["impactTimer"]=.1;
        var stopped=impactRunner.Tick(impactAction,impact);
        Check(impactAction.Error?.Contains("movement changed",StringComparison.Ordinal)==true&&KitchenModel.N(stopped["x"])==0&&!stopped["dash"]!.GetValue<bool>(),"unexpected native impact stops and releases a dash action");
        Console.WriteLine($"PASS: {checks} native dash navigation assertions.");
        return checks;
    }
}
