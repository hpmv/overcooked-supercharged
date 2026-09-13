using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Captured native poses/items and explicit planner ownership; alternatives are offline decisions, not simulated native timing.</summary>
    public static int NearestCentralSelfTest(JsonObject at494,JsonObject at495,JsonObject at6936,JsonObject at7784)
    {
        int count=0;
        void Check(bool condition,string message){if(!condition)throw new InvalidOperationException("Nearest central regression: "+message);count++;}
        CarnivalPlanner Make(JsonObject snapshot)
        {
            var p=new CarnivalPlanner(_=>throw new InvalidOperationException("Offline nearest fixture attempted I/O."),null)
            {response=snapshot.DeepClone().AsObject(),options=new(NearestCentralTaskPreference:true,BufferChoppedBuns:true,ReleaseFiringChefOnLaunch:true),
                recipes=Enumerable.Repeat(CarnivalRecipes.GetRecipe(296560),96).ToArray()};
            p.Refresh();p.runner=new RouteRunner(_=>throw new InvalidOperationException("Offline nearest fixture attempted I/O."),null);
            return p;
        }
        CarnivalPlanner Raw()
        {
            var p=Make(at494);p.recipes[7]=CarnivalRecipes.GetRecipe(130976);p.bowlAssignments[3]=7;p.bowlFlavors[3]=CarnivalRecipes.Raspberry.Id;
            p.counterSupplies[48]=(3,CarnivalRecipes.Flour.Id);
            // Recorded P2 supplier still owns the workable bun board until495.
            p.workers[2]=new Work("supply-HotdogBun",[],[23,68],null);p.reserved.UnionWith([23,68]);return p;
        }
        var baseline=Raw();baseline.options=baseline.options with{NearestCentralTaskPreference=false};
        Check(baseline.RelayRawIntoVessel(0)&&baseline.workers[0]!.OwnedResources.SetEquals([141,48,3]),"default-off preserves actual494 player0 raw relay and exact resources");
        var p=Raw();string native=p.response.ToJsonString();var before=p.reserved.Order().ToArray();
        Check(!p.RelayRawIntoVessel(0)&&p.workers[0] is null&&p.workers[3] is null,"farther player0 defers the original exact raw candidate without creating either Work");
        Check(native==p.response.ToJsonString()&&p.reserved.Order().SequenceEqual(before)&&p.counterSupplies[48]==(3,CarnivalRecipes.Flour.Id),"preference changes neither observations, reservations nor the raw address");
        var preference=p.centralTaskPreferences[3];
        Check(preference.Target==48&&preference.PreferredPlayer==3&&preference.CurrentSeconds>preference.PreferredSeconds+.9,"actual full-clearance494 paths favor nearby player3 by a strict margin");
        Check(p.RelayRawIntoVessel(3)&&p.workers[3]!.OwnedResources.SetEquals([141,48,3])&&p.workers[0] is null,"nearer chef starts the unchanged original relay without mutual deferral");
        Check(p.workers[3]!.Actions.Select(a=>a["type"]!.ToString()).SequenceEqual(new[]{"take","place"})&&I(p.workers[3]!.Actions.First()["station"])==48&&I(p.workers[3]!.Actions.Last()["station"])==3,"selected Work keeps the same exact source, vessel and legal actions");
        // Next-frame native observation comes from the original policy; only
        // the explicit counterfactual Work assignment is retained here. No
        // position or velocity is corrected to manufacture a successful path.
        var chosen=p.workers[3];p.response=at495.DeepClone().AsObject();p.Refresh();p.workers[2]=null;p.reserved.ExceptWith([23,68]);
        Check(p.BufferChoppedBun(0)&&ReferenceEquals(p.workers[3],chosen)&&p.workers[0]!.Name=="buffer-native-chopped-bun","now-free left chef may take the newly prepared bun while exact right relay remains owned");
        Check(!p.workers[0]!.OwnedResources.Overlaps(chosen!.OwnedResources),"concurrent original callbacks retain disjoint native sources and destinations");
        var dispatch=Raw();dispatch.Central(0);
        Check(dispatch.workers[0] is null&&dispatch.centralTaskPreferences.ContainsKey(3),"full ordinary player0 priority chain defers the actual494 relay");
        dispatch.Central(3);
        Check(dispatch.workers[3]?.Name=="load-addressed-counter-ingredient"&&dispatch.workers[3]!.OwnedResources.SetEquals([141,48,3]),"full following player3 priority chain admits that same exact relay");
        var fire=Make(at6936);native=fire.response.ToJsonString();
        Check(!fire.FireLoaded(0)&&fire.cannonFlights.Count==0&&!fire.reserved.Contains(77),"farther firer defers without claiming passenger, landing or button");
        Check(fire.FireLoaded(3)&&fire.workers[3]?.Name=="fire-right"&&fire.workers[0] is null,"actual nearby chef3 accepts the exact right cannon task");
        Check(fire.cannonFlights[85].Firer==3&&fire.cannonFlights[85].Passenger==1&&fire.workers[3]!.OwnedResources.SetEquals([77])&&
            fire.cannonFlights[85].Owned.Contains(I(fire.chefs[1]["entityId"])),"original flight/passenger ownership and firing-button lease remain intact");
        Check(native==fire.response.ToJsonString(),"firer selection never alters the boarded native passenger or cannon state");
        var buffer=Make(at7784);
        Check(!buffer.BufferChoppedBun(0)&&buffer.bufferedBun==0,"farther buffer chef defers before publishing a buffer or acquiring its source");
        Check(buffer.BufferChoppedBun(3)&&buffer.bufferedBun==311&&buffer.workers[3]!.OwnedResources.Contains(23),"closer chef starts the existing exact bun-buffer lifecycle");
        foreach(string mutation in new[]{"other-busy","other-held","other-suppressed","other-disabled","other-moving","other-region","other-heat-blocked","self-heat-blocked","persistent-owner","missing-speed","bad-speed","source-identity","source-reserved","nearer-obstructed","slower-other","earlier-preference","higher-clean-job","higher-addressed-raw"})
        {
            var q=Raw();
            switch(mutation)
            {
                case "other-busy":q.workers[3]=new Work("earlier-job",[],[],null);break;
                case "other-held":q.chefs[3]["heldEntityId"]=13;break;
                case "other-suppressed":q.chefs[3]["inputSuppressed"]=true;break;
                case "other-disabled":q.chefs[3]["controlsEnabled"]=false;break;
                case "other-moving":q.chefs[3]["lastVelocity"]!["x"]=6;break;
                case "other-region":q.chefs[3]["position"]!["x"]=28;break;
                case "other-heat-blocked":q.heatSafetyBlocked.Add(3);break;
                case "self-heat-blocked":q.heatSafetyBlocked.Add(0);break;
                case "persistent-owner":q.earlyOnion=new EarlyOnionLease(4,4,16,61,3,3,q.Frame);break;
                case "missing-speed":q.chefs[3].Remove("runSpeed");break;
                case "bad-speed":q.chefs[3]["runSpeed"]=-1;break;
                case "source-identity":q.Entity(48)!.Remove("observedOrdinal");break;
                case "source-reserved":q.reserved.Add(48);break;
                case "nearer-obstructed":q.chefs[3]["position"]=q.Entity(48)!["position"]!.DeepClone();break;
                case "slower-other":q.chefs[3]["runSpeed"]=.1;break; // farther time despite shorter geometry
                case "earlier-preference":q.centralTaskPreferences[3]=new(q.Frame,0,3,CentralPreferenceKind.Fire,77,76,[77],2,.1);break;
                case "higher-clean-job":q.Entity(44)!["attachedEntityId"]=10;break;
                case "higher-addressed-raw":break;
            }
            if(mutation=="higher-addressed-raw")
            {
                // Actual494 already contains the earlier exact raw141 address.
                Check(!q.PreferOtherCentral(CentralPreferenceKind.BunBuffer,0,23,23,143,32),"higher addressed raw job blocks a lower buffer preference");
            }
            else Check(!q.PreferOtherCentral(CentralPreferenceKind.RawRelay,0,48,141,48,3),"no deferral when "+mutation);
        }
        var heat=Raw();heat.Entity(7)!["cookingProgress"]=11;
        Check(!heat.PreferOtherCentral(CentralPreferenceKind.RawRelay,0,48,141,48,3),"unowned native heat obligation always retains arbitration priority");
        var tie=Raw();var ownPath=Navigation.ToStation(tie.model,tie.Position(0),tie.Station(48)!,tie.TrafficObstacles(0));
        var otherPath=Navigation.ToStation(tie.model,tie.Position(3),tie.Station(48)!,tie.TrafficObstacles(3));
        // Explicit synthetic speed equality; native speed is never changed by
        // the planner or by any live-game command in this offline fixture.
        tie.chefs[3]["runSpeed"]=N(tie.chefs[0]["runSpeed"])*otherPath.Length/ownPath.Length;
        Check(!tie.PreferOtherCentral(CentralPreferenceKind.RawRelay,0,48,141,48,3),"equal estimated costs preserve original player0-first order");
        var higher=Make(at6936);higher.PreferOtherCentral(CentralPreferenceKind.Fire,0,77,85,77);
        Check(!higher.PreferOtherCentral(CentralPreferenceKind.RawRelay,0,48,48,3),"loaded higher-priority cannon cannot be replaced by another hint for the same helper");
        var failedPromise=Raw();failedPromise.RelayRawIntoVessel(0);failedPromise.workers[3]=new Work("actual-higher-priority-work",[],[],null);failedPromise.state["gameplayFrame"]=495;
        Check(failedPromise.RelayRawIntoVessel(0)&&failedPromise.workers[0]!.Name=="load-addressed-counter-ingredient","if the other chef takes higher-priority work, original unowned candidate starts next frame without repeated deferral");
        return count;
    }
}
