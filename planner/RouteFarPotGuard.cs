using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class RouteRunner
{
    private bool CheckConditionalFarPot(Active action,ThrowState data,JsonNode state)
    {
        if(action.Spec["farPotLane"] is not JsonObject guard||action.Stage is "throw-await-flight" or "throw-await-arrival")return true;
        int selected=data.VesselId==0?IdOf(action.Spec,"targetEntityId"):data.VesselId;
        string? invalid=Player(action.Spec)!=2||selected!=FarPotLane.Id(guard,"farPot")?"guard selector does not identify chef2 and the proven far pot":
            FarPotLane.Invalid(state.AsObject(),guard,true);
        if(invalid is null)return true;
        var chef=Chef(state,Player(action.Spec));
        bool armed=data.ConditionalGuardArmed||Flag(chef,"aimingThrow")||
            action.Stage=="throw-clear-use-press"&&!Flag(chef,"useSuppressed");
        trace?.Event("conditionalFarPotGuardRejected",new JsonObject { ["player"]=Player(action.Spec),["stage"]=action.Stage,["armed"]=armed,
            ["reason"]=invalid,["frame"]=state["frame"]?.DeepClone(),["cleanupMayReleaseIngredient"]=armed });
        if(armed)throw ThrowFailure(action,data,state,"conditional far-pot lane changed after arming; candidate failed; neutral cleanup may release ingredient: "+invalid);
        string? fallbackInvalid=FarPotLane.FallbackInvalid(state.AsObject(),guard);
        if(fallbackInvalid is not null)throw ThrowFailure(action,data,state,"conditional far-pot lane invalid before arming and reserved fallback unavailable: "+fallbackInvalid);
        // No unsuppressed Use press has occurred, so the neutral transition is
        // not a throw release. Continue this action as an ordinary native place.
        int fallback=FarPotLane.Id(guard,"fallback");
        action.Spec["type"]="place";action.Spec["station"]=fallback.ToString();
        action.Spec.Remove("targetEntityId");action.Spec.Remove("farPotLane");action.Spec.Remove("nativeSupplyHome");
        action.Motion=null;Stage(action,"resolve");throwStates.Remove(action);
        trace?.Event("conditionalFarPotFallback",new JsonObject { ["counter"]=fallback,["reason"]=invalid,["frame"]=state["frame"]?.DeepClone() });
        return false;
    }
}
