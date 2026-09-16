using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed record NativeDashProfile(double RunSpeed,double DashSpeed,double Duration,double Cooldown,double MovementScale,double MaximumSpeed,double SurfaceMultiplier)
{
    public double RunVelocity=>Math.Min(MaximumSpeed,RunSpeed*MovementScale)*SurfaceMultiplier;
    public double PeakVelocity=>Math.Min(MaximumSpeed,Math.Max(RunSpeed,DashSpeed)*MovementScale)*SurfaceMultiplier;
    public static bool TryRead(JsonObject chef,out NativeDashProfile? profile,out string reason)
    {
        profile=null;
        foreach(string field in new[]{"runSpeed","dashSpeed","dashDuration","dashCooldown","movementScale","maxSpeed","surfaceSpeedMultiplier","surfaceSlippiness","surfaceSlidiness","groundNormal","surfaceVelocity","windVelocity"})
            if(chef[field] is null){reason="missing native "+field;return false;}
        double N(string field)=>KitchenModel.N(chef[field]);
        if(new[]{"runSpeed","dashSpeed","dashDuration","movementScale","maxSpeed","surfaceSpeedMultiplier"}.Any(f=>!double.IsFinite(N(f))||N(f)<=0)||N("dashCooldown")<0||!double.IsFinite(N("dashCooldown")))
        {reason="invalid native movement parameters";return false;}
        if(N("dashDuration")>3){reason="native dash duration exceeds bounded prediction horizon";return false;}
        if(Math.Abs(N("surfaceSlippiness"))>.0001||Math.Abs(N("surfaceSlidiness"))>.0001||Math.Abs(KitchenModel.N(chef["groundNormal"]?["y"])-1)>.001||Length(chef["surfaceVelocity"])>.01||Length(chef["windVelocity"])>.01)
        {reason="dash prediction requires observed flat stationary non-slippery ground";return false;}
        profile=new(N("runSpeed"),N("dashSpeed"),N("dashDuration"),N("dashCooldown"),N("movementScale"),N("maxSpeed"),N("surfaceSpeedMultiplier"));
        reason="ready";return true;
    }
    private static double Length(JsonNode? v)=>Math.Sqrt(Math.Pow(KitchenModel.N(v?["x"]),2)+Math.Pow(KitchenModel.N(v?["y"]),2)+Math.Pow(KitchenModel.N(v?["z"]),2));
    public double Velocity(double timer,bool directional)
    {
        // Native MathUtils.SinusoidalSCurve, applied before the native speed
        // clamp and stationary surface multiplier.
        double blend=timer<=0?0:.5*(1-Math.Cos(Math.PI*Math.Clamp(timer/Duration,0,1)));
        double target=(directional?RunSpeed*(1-blend):0)+DashSpeed*blend;
        return Math.Min(MaximumSpeed,target*MovementScale)*SurfaceMultiplier;
    }
    public double FullTravelBound(double cachedSpeed)=>TravelBound(Duration,cachedSpeed,true,true);
    // One directional dash-edge Update aligns the native forward vector and
    // still caches walking velocity. Every later Update receives neutral axes,
    // so native dash inertia decays through its unchanged sinusoidal blend.
    // The small allowance covers float32 native timer/cosine rounding.
    public double ShortCoastTravelBound(double cachedSpeed)=>TravelBound(Duration,cachedSpeed,false,true)+.0001;
    public double NeutralCoastBound(double timer,double cachedSpeed)=>TravelBound(Math.Max(0,timer),cachedSpeed,false,false);
    private double TravelBound(double remaining,double initialVelocity,bool directional,bool newDash)
    {
        // Maximise over every observed 60/50 phase. A new press is handled
        // after movement, so its first Update still caches walking velocity.
        // Two tail updates include the final queued physics displacement.
        double maximum=0;int updates=(int)Math.Ceiling(remaining*60)+3;
        for(int phase=0;phase<6;phase++)
        {
            double timer=remaining,velocity=initialVelocity,distance=0;
            for(int frame=0;frame<updates;frame++)
            {
                if((phase+frame)%6!=5)distance+=velocity*.02;
                if(frame==0&&newDash)velocity=RunVelocity;
                else{velocity=Velocity(timer,directional);timer-=1.0/60;}
            }
            maximum=Math.Max(maximum,distance);
        }
        return maximum;
    }
}

public sealed partial class RouteRunner
{
    internal sealed class RouteDash
    {
        public required NativeDashProfile Profile;
        public Point2 Start,Target;
        public int RequestedFrame;
        public bool Observed,Coasting;
        public bool ImmediateNeutralCoast;
        public double PeakObserved;
    }
    private void DashDecision(Active action,string reason)
    {
        if(action.Motion!.LastDashDecision==reason)return;
        action.Motion.LastDashDecision=reason;
        trace?.Event("dashDecision",new JsonObject{["player"]=Player(action.Spec),["reason"]=reason,["actionFrames"]=action.Frames});
    }
    private void MaybeStartDash(Active action,JsonNode state,JsonObject input,Point2 position,Point2 target,double distance)
    {
        if(action.Spec["dash"]?.GetValue<bool>()!=true)return;
        var motion=action.Motion!;var chef=Chef(state,Player(action.Spec));
        if(motion.DashDisabled){DashDecision(action,"earlier dash was not accepted");return;}
        if(!NativeDashProfile.TryRead(chef,out var profile,out string reason)){DashDecision(action,reason);return;}
        double logicalDelta=Number(state["unityDeltaTime"]??state["clientDeltaTime"]);
        if(Math.Abs(logicalDelta-1.0/60)>.00001||Math.Abs(Number(state["fixedDeltaTime"])-.02)>.00001)
        {DashDecision(action,"dash prediction requires observed 60 Hz logical / 50 Hz physics clocks");return;}
        if(chef["dashTimer"] is null||chef["impactTimer"] is null||chef["controlsEnabled"]?.GetValue<bool>()!=true||chef["directlyControlled"]?.GetValue<bool>()!=true||chef["canAcceptInput"]?.GetValue<bool>()!=true||chef["inputSuppressed"]?.GetValue<bool>()!=false||chef["aimingThrow"]?.GetValue<bool>()!=false||chef["respawning"]?.GetValue<bool>()!=false||Number(chef["interactingEntityId"])!=0||Number(chef["impactTimer"])>=0)
        {DashDecision(action,"native control or impact gate is closed");return;}
        double timer=Number(chef["dashTimer"]);
        if(timer>0||profile!.Duration-timer<profile.Cooldown){DashDecision(action,"native dash cooldown is active");return;}
        var forward=KitchenModel.Position(chef["forward"]);double length=forward.Distance(default);
        double alignment=length<.01?-1:((target.X-position.X)*forward.X+(target.Z-position.Z)*forward.Z)/(distance*length);
        if(alignment<Math.Cos(3*Math.PI/180)){DashDecision(action,"chef heading has not aligned to the straight segment");return;}
        double cached=KitchenModel.Position(chef["lastVelocity"]).Distance(default);
        double margin=Math.Max(.05,Number(action.Spec["dashSafetyMargin"]??JsonValue.Create(.15)));
        double fullRequired=profile.FullTravelBound(cached)+margin;
        double required=fullRequired;bool immediateCoast=false;
        if(distance<fullRequired&&action.Spec["shortDash"]?.GetValue<bool>()==true)
        {
            required=profile.ShortCoastTravelBound(cached)+margin;
            immediateCoast=true;
            double turnSpeed=Number(chef["turnSpeed"]);
            if(!double.IsFinite(turnSpeed)||turnSpeed<=0||turnSpeed*logicalDelta+1e-6<Math.Acos(Math.Clamp(alignment,-1,1)))
            {DashDecision(action,"native short dash needs measured turn speed that aligns its directional edge Update");return;}
            var queued=KitchenModel.Position(chef["lastVelocity"]);
            double cachedAlignment=cached<.05?1:((target.X-position.X)*queued.X+(target.Z-position.Z)*queued.Z)/(distance*cached);
            if(cached>profile.RunVelocity+.05||cachedAlignment<Math.Cos(3*Math.PI/180))
            {DashDecision(action,"native short dash waits for an aligned walking-speed cached velocity");return;}
        }
        if(distance<Math.Max(required,Number(action.Spec["dashMinDistance"])))
        {DashDecision(action,"straight segment is shorter than native dash travel plus settling clearance");return;}
        if(motion.Model is null){DashDecision(action,"dash needs measured collision geometry");return;}
        var endpoint=new Point2(position.X+(target.X-position.X)*required/distance,position.Z+(target.Z-position.Z)*required/distance);
        if(!DashCorridorClear(action,state,position,endpoint,profile.Duration+.06))
        {DashDecision(action,"native dash corridor conflicts with geometry or another chef's observed motion");return;}
        motion.Dash=new RouteDash{Profile=profile,Start=position,Target=target,RequestedFrame=action.Frames,Coasting=immediateCoast,ImmediateNeutralCoast=immediateCoast};
        input["dash"]=true;DashDecision(action,"native dash edge requested");
        trace?.Event("navigationDashRequested",new JsonObject{["player"]=Player(action.Spec),["position"]=JsonSerializer.SerializeToNode(position),["target"]=JsonSerializer.SerializeToNode(target),["profile"]=JsonSerializer.SerializeToNode(profile),["requiredClearDistance"]=required,["fullDashRequiredDistance"]=fullRequired,["admission"]=immediateCoast?"immediate_neutral_coast":"full_directional",["nativeTimerBefore"]=timer,["actionFrames"]=action.Frames});
    }
    private bool ContinueDash(Active action,JsonNode state,JsonObject input,Point2 position,Point2 target,double distance)
    {
        var motion=action.Motion!;var dash=motion.Dash;
        if(dash is null)return false;
        var chef=Chef(state,Player(action.Spec));double timer=Number(chef["dashTimer"]);
        double speed=KitchenModel.Position(chef["lastVelocity"]).Distance(default);dash.PeakObserved=Math.Max(dash.PeakObserved,speed);
        if(timer>0)dash.Observed=true;
        if(!dash.Observed&&action.Frames-dash.RequestedFrame>4)
        {
            trace?.Event("navigationDashRejected",new JsonObject{["player"]=Player(action.Spec),["nativeTimer"]=timer,["actionFrames"]=action.Frames});
            motion.Dash=null;motion.DashDisabled=true;return false;
        }
        if(dash.Observed&&timer<=0)
        {
            trace?.Event("navigationDashComplete",new JsonObject{["player"]=Player(action.Spec),["elapsedFrames"]=action.Frames-dash.RequestedFrame,["displacement"]=position.Distance(dash.Start),["peakObservedSpeed"]=dash.PeakObserved,["remainingDistance"]=distance,["coasted"]=dash.Coasting,["immediateNeutralCoast"]=dash.ImmediateNeutralCoast});
            motion.Dash=null;return false;
        }
        if(action.Frames-dash.RequestedFrame>Math.Ceiling(dash.Profile.Duration*60)+8)
            throw new InvalidOperationException("Native dash timer exceeded its observed configured duration.");
        if(!NativeDashProfile.TryRead(chef,out var observedProfile,out var reason)||observedProfile!=dash.Profile||Number(chef["impactTimer"])>0||speed>dash.Profile.PeakVelocity+.05)
        {
            trace?.Event("navigationDashInterrupted",new JsonObject{["player"]=Player(action.Spec),["reason"]=reason,["nativeTimer"]=timer,["speed"]=speed,["impactTimer"]=chef["impactTimer"]?.DeepClone(),["position"]=JsonSerializer.SerializeToNode(position)});
            throw new InvalidOperationException("Native dash movement changed unexpectedly; stopped with an observed diagnostic.");
        }
        double stopping=dash.Profile.NeutralCoastBound(timer,speed)+.08;
        if(distance<=stopping+.10)dash.Coasting=true;
        if(!dash.Coasting)
        {
            double ahead=Math.Min(distance,Math.Max(stopping,.35));
            var endpoint=new Point2(position.X+(target.X-position.X)*ahead/distance,position.Z+(target.Z-position.Z)*ahead/distance);
            if(!DashCorridorClear(action,state,position,endpoint,Math.Max(0,timer)+.04))
            {
                dash.Coasting=true;
                trace?.Event("navigationDashCoast",new JsonObject{["player"]=Player(action.Spec),["reason"]="live corridor changed",["remainingDistance"]=distance,["neutralStoppingBound"]=stopping,["actionFrames"]=action.Frames});
            }
        }
        if(!dash.Coasting)SetMovement(action,input,position,target,1);
        return true;
    }
    private static bool DashCorridorClear(Active action,JsonNode state,Point2 start,Point2 end,double horizon)
    {
        var model=action.Motion!.Model!;var region=model.RegionAt(start);
        if(region is null)return false;
        var padded=new KitchenModel{Regions=model.Regions,Obstacles=model.Obstacles,ChefRadius=model.ChefRadius,Clearance=model.Clearance+.08};
        var others=(state["chefs"] as JsonArray)?.OfType<JsonObject>().Where(c=>c["playerId"]?.GetValue<int>()!=Player(action.Spec)).Select(c=>
        {
            var p=KitchenModel.Position(c["position"]);var velocity=KitchenModel.Position(c["lastVelocity"]);double r=model.ChefRadius+.08;
            var future=new Point2(p.X+velocity.X*horizon,p.Z+velocity.Z*horizon);
            return new KitchenObstacle("dash-chef:"+c["playerId"],c["entityId"]?.GetValue<int>()??0,new(Math.Min(p.X,future.X)-r,Math.Max(p.X,future.X)+r,Math.Min(p.Z,future.Z)-r,Math.Max(p.Z,future.Z)+r),"Observed chef swept motion over native dash duration",true);
        }).ToArray()??[];
        return Navigation.SegmentClear(padded,start,end,region,others);
    }
}
