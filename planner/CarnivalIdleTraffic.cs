using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private bool TrafficControlsReady(int player)
    {
        var chef = chefs[player];
        return B(chef["controlsEnabled"]) && B(chef["directlyControlled"]) && B(chef["canAcceptInput"]) &&
            !B(chef["inputSuppressed"]) && !B(chef["respawning"]) && !B(chef["aimingThrow"]) &&
            chef["dashTimer"] is not null && N(chef["dashTimer"]) <= 0 && chef["lastVelocity"] is not null &&
            KitchenModel.Position(chef["lastVelocity"]).Distance(default) < .05 && !NativeCannonFlight(player);
    }

    private bool IdleTrafficHelper(int player) => workers[player] is null && Held(player) == 0 &&
        !IsSauceParticipant(player) && !IsEarlyOnionParticipant(player) && !IsBakeryParticipant(player) && !IsFryerParticipant(player) && !IsPotParticipant(player) && !HeatSafetyBlocks(player) &&
        !cannonInterruptions.ContainsKey(player) && TrafficControlsReady(player);

    private bool TryBeginIdleTrafficYield()
    {
        if (trafficYield is not null) return false;
        foreach (int winner in Enumerable.Range(0, 4))
        {
            var action = workers[winner]?.Active;
            var motion = action?.Action.Motion;
            // An initial transfer can fail to resolve any approach before it
            // reaches navigate. No runtime station ID exists in that state.
            if (action is not { IsDone: false, Stage: "resolve" or "navigate" } ||
                motion is not { NoPathSince: >= 0 } || action.ElapsedFrames - motion.NoPathSince < 30 ||
                !TrafficControlsReady(winner) || IsSauceParticipant(winner) ||
                action.Specification["station"]?.ToString() is not { } selector) continue;
            var targets = model.Candidates(selector).Where(s => !HeldByAnyone(s.EntityId)).ToArray();
            var blocked = targets.Where(s => Navigation.ToStation(model, Position(winner), s).Success &&
                !Navigation.ToStation(model, Position(winner), s, TrafficObstacles(winner)).Success).ToArray();
            // Do not interrupt a chef if any selected station is already
            // reachable, or if the underlying static kitchen is unreachable.
            if (blocked.Length == 0 || targets.Any(s => Navigation.ToStation(model, Position(winner), s, TrafficObstacles(winner)).Success)) continue;
            foreach (int helper in Enumerable.Range(0, 4).Where(p => p != winner && IdleTrafficHelper(p) && Region(p) == Region(winner)))
            {
                var withoutHelper = TrafficObstacles(winner).Where(o => o.EntityId != I(chefs[helper]["entityId"])).ToArray();
                var opened = blocked.Where(s => Navigation.ToStation(model, Position(winner), s, withoutHelper).Success).ToArray();
                if (opened.Length == 0) continue;
                var obstacles = TrafficObstacles(helper);
                var choices = new List<(Point2 Target, NavigationPath Path, NavigationPath WinnerPath, KitchenStation Station, double Cost)>();
                foreach (double distance in new[] { 1.2, 1.8, 2.4 })
                foreach (int direction in Enumerable.Range(0, 12))
                {
                    double angle = direction * Math.PI / 6;
                    var target = new Point2(Position(helper).X + distance * Math.Cos(angle), Position(helper).Z + distance * Math.Sin(angle));
                    if (model.RegionAt(target) != Region(helper)) continue;
                    var path = Navigation.FindPath(model, Position(helper), target, .15, obstacles);
                    if (!path.Success) continue;
                    foreach (var station in opened)
                    {
                        var resumed = Navigation.ToStation(model, Position(winner), station, TrafficObstacles(winner, helper, target));
                        if (resumed.Success) choices.Add((target, path, resumed, station, path.Length + resumed.Length));
                    }
                }
                if (choices.Count == 0) continue;
                var choice = choices.OrderBy(c => c.Cost).ThenBy(c => c.Target.X).ThenBy(c => c.Target.Z).ThenBy(c => c.Station.Key, StringComparer.Ordinal).First();
                var spec = new JsonObject { ["type"] = "navigate", ["player"] = helper,
                    ["target"] = new JsonObject { ["x"] = choice.Target.X, ["z"] = choice.Target.Z }, ["timeoutFrames"] = 180,
                    ["dash"] = false, ["shortDash"] = false };
                // This empty work owns the helper slot while the ordinary
                // walking action is ticked by TrafficYield. It owns no entity
                // leases, cannot complete through CompleteWork, and prevents
                // every ordinary/heat/bakery dispatcher from double-booking it.
                var idleWork = new Work("idle-traffic-yield", [], [], null);
                workers[helper] = idleWork;
                trafficYield = new TrafficYield(winner, helper, action, null, runner.CreateAction(spec), choice.Target, Frame, idleWork);
                Log("plannerIdleTrafficYieldStart", new JsonObject { ["player"] = helper, ["winner"] = winner, ["frame"] = Frame,
                    ["winningJob"] = workers[winner]!.Name, ["winningAction"] = action.Specification,
                    ["stationId"] = choice.Station.EntityId, ["waitedActionFrames"] = action.ElapsedFrames - motion.NoPathSince,
                    ["target"] = JsonSerializer.SerializeToNode(choice.Target), ["path"] = JsonSerializer.SerializeToNode(choice.Path),
                    ["winnerPathAfterYield"] = JsonSerializer.SerializeToNode(choice.WinnerPath),
                    ["reason"] = "An idle empty-handed chef alone blocks a statically reachable station; both relocation and resumed route retain full measured collision clearance." });
                return true;
            }
        }
        return false;
    }
}
