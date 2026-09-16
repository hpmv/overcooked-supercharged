using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed record Reservation(string Resource, int Start, int End, string TaskId);
public sealed class ReservationTable
{
    private readonly List<Reservation> entries = [];
    public IReadOnlyList<Reservation> Entries => entries;
    public int Earliest(IEnumerable<string> resources, int start, int duration)
    {
        if (start < 0 || duration <= 0) throw new ArgumentOutOfRangeException(nameof(duration));
        var wanted = resources.ToHashSet(StringComparer.Ordinal);
        while (true)
        {
            var conflicting = entries.Where(r => wanted.Contains(r.Resource) && r.Start < start + duration && start < r.End).ToArray();
            if (conflicting.Length == 0) return start;
            start = conflicting.Max(r => r.End);
        }
    }
    public void Add(IEnumerable<string> resources, int start, int duration, string taskId)
    {
        var all = resources.Distinct(StringComparer.Ordinal).ToArray();
        if (Earliest(all, start, duration) != start) throw new InvalidOperationException($"Reservation collision for {taskId}.");
        entries.AddRange(all.Select(r => new Reservation(r, start, checked(start + duration), taskId)));
    }
}

public sealed record KitchenTask(string Id, string[] Dependencies, string[] Resources, int[] Chefs, int DurationFrames, int Priority = 0);
public sealed record ScheduledTask(string Id, int Chef, int Start, int End);

public static class KitchenScheduler
{
    public static IReadOnlyList<ScheduledTask> Schedule(IReadOnlyList<KitchenTask> tasks)
    {
        if (tasks.Select(t => t.Id).Distinct().Count() != tasks.Count) throw new ArgumentException("Duplicate task id.");
        var byId = tasks.ToDictionary(t => t.Id);
        foreach (var t in tasks)
        {
            if (t.DurationFrames <= 0 || t.Chefs.Length == 0 || t.Chefs.Any(c => c is < 0 or > 3)) throw new ArgumentException($"Invalid task {t.Id}.");
            if (t.Dependencies.Any(d => !byId.ContainsKey(d))) throw new ArgumentException($"Unknown dependency of {t.Id}.");
        }
        var pending = tasks.ToDictionary(t => t.Id);
        var done = new Dictionary<string, ScheduledTask>();
        var reservations = new ReservationTable();
        while (pending.Count > 0)
        {
            var choices = pending.Values.Where(t => t.Dependencies.All(done.ContainsKey)).SelectMany(t => t.Chefs.Distinct().Select(chef =>
            {
                var ready = t.Dependencies.Select(d => done[d].End).DefaultIfEmpty(0).Max();
                var start = reservations.Earliest(t.Resources.Append("chef:" + chef), ready, t.DurationFrames);
                return (Task: t, Chef: chef, Start: start, End: checked(start + t.DurationFrames));
            })).OrderBy(c => c.Start).ThenByDescending(c => c.Task.Priority).ThenBy(c => c.End).ThenBy(c => c.Task.Id, StringComparer.Ordinal).ThenBy(c => c.Chef).ToArray();
            if (choices.Length == 0) throw new ArgumentException("Task dependency cycle.");
            var next = choices[0];
            reservations.Add(next.Task.Resources.Append("chef:" + next.Chef), next.Start, next.Task.DurationFrames, next.Task.Id);
            done.Add(next.Task.Id, new ScheduledTask(next.Task.Id, next.Chef, next.Start, next.End));
            pending.Remove(next.Task.Id);
        }
        return done.Values.OrderBy(t => t.Start).ThenBy(t => t.Chef).ToArray();
    }
}

public sealed class RouteActionHandle
{
    internal RouteRunner Owner {get;}
    internal RouteRunner.Active Action {get;}
    internal RouteActionHandle(RouteRunner owner,RouteRunner.Active action){Owner=owner;Action=action;}
    public bool IsDone {get;internal set;}
    public string? Error {get;internal set;}
    public int ElapsedFrames=>Action.Frames;
    public string Stage=>Action.Stage;
    public int Player=>Action.Spec["player"]?.GetValue<int>()??0;
    public int? RuntimeEntityId=>Action.Motion?.Station?.EntityId??(Action.WorkEntityId==0?null:Action.WorkEntityId);
    public JsonObject Specification=>Action.Spec.DeepClone().AsObject();
}

public sealed partial class RouteRunner(Func<JsonObject, Task<JsonObject>> call, TraceWriter? trace)
{
    public bool ContinuousWaypoints { get; set; }
    public bool StationaryTargetTransfers { get; set; }
    internal sealed class Active(JsonObject spec)
    {
        public JsonObject Spec { get; } = spec;
        public int Frames { get; set; }
        public int StableFrames { get; set; }
        public bool Done { get; set; }
        public RouteMotion? Motion { get; set; }
        public string Stage { get; set; }="resolve";
        public int StageStarted { get; set; }
        public int InitialHeld { get; set; }
        public int Retries { get; set; }
        public string? BeforeAttachment { get; set; }
        public int BeforeScore { get; set; }
        public int WorkEntityId {get;set;}
        public int InitialWorkEntityId {get;set;}
        public double LastWorkProgress {get;set;}=-1;
        public int LastProgressFrame {get;set;}
    }

    /// <summary>Create independent chef action state without connecting to or advancing the game.</summary>
    public RouteActionHandle CreateAction(JsonObject specification)
    {
        var action=new Active(specification.DeepClone().AsObject());_=Player(action.Spec);
        if (ContinuousWaypoints && action.Spec["continuousWaypoints"] is null) action.Spec["continuousWaypoints"] = true;
        if(action.Spec["type"] is null)throw new ArgumentException("Action needs type.");
        if (StationaryTargetTransfers && !action.Spec.ContainsKey("stationaryTargetTransfer") &&
            action.Spec["type"]!.ToString() is "take" or "place" or "combine" or "apply" or "assemble")
            action.Spec["stationaryTargetTransfer"] = true;
        trace?.Event("actionCreated",action.Spec);
        return new RouteActionHandle(this,action);
    }

    /// <summary>
    /// Call once per actual simulation frame, then apply the returned input.
    /// The tick setting IsDone returns a required neutral/release frame. Replace
    /// the handle on the following frame, after applying that result. Inspect
    /// Error on completion; failed actions also return neutral and retain audit.
    /// </summary>
    public JsonObject Tick(RouteActionHandle handle,JsonNode snapshot)
    {
        if(!ReferenceEquals(handle.Owner,this))throw new ArgumentException("Action belongs to another runner.");
        if(handle.IsDone)return Inputs.Neutral(handle.Player);
        var action=handle.Action;var state=snapshot["state"]??snapshot;
        JsonObject input;
        try
        {
            if(action.Frames>(action.Spec["timeoutFrames"]?.GetValue<int>()??1200))throw new TimeoutException("Action exceeded frame timeout.");
            input=Update(action,state);
            if(action.Done)
            {
                input=Inputs.Neutral(handle.Player);handle.IsDone=true;
                trace?.Event("actionComplete",new JsonObject{["action"]=action.Spec.DeepClone(),["frames"]=action.Frames,["runtimeEntityId"]=handle.RuntimeEntityId,["frame"]=state["frame"]?.DeepClone()});
            }
        }
        catch(Exception error)
        {
            handle.Error=error.Message;handle.IsDone=true;action.Done=true;input=Inputs.Neutral(handle.Player);
            trace?.Event("actionFailure",new JsonObject{["action"]=action.Spec.DeepClone(),["stage"]=action.Stage,["error"]=error.Message,["state"]=state.DeepClone()});
        }
        action.Frames++;return input;
    }

    public async Task<JsonObject> RunAsync(JsonObject route)
    {
        var state = await call(Json.Request("inspect"));
        var phases = route["phases"]?.AsArray() ?? throw new ArgumentException("Route needs phases array.");
        foreach (var phaseNode in phases)
        {
            var phase = phaseNode!.AsObject();
            var active = (phase["actions"]?.AsArray() ?? throw new ArgumentException("Phase needs actions.")).Select(a => new Active(a!.AsObject())).ToArray();
            if (active.Select(a => Player(a.Spec)).Distinct().Count() != active.Length) throw new ArgumentException("Each phase allows only one action per chef.");
            trace?.Event("phaseStart", phase["name"]);
            while (active.Any(a => !a.Done))
            {
                var inputs = Inputs.AllNeutral();
                foreach (var action in active.Where(a => !a.Done))
                {
                    var timeout = action.Spec["timeoutFrames"]?.GetValue<int>() ?? 1200;
                    if (action.Frames > timeout)
                    {
                        trace?.Event("actionTimeout", new JsonObject { ["action"] = action.Spec.DeepClone(), ["state"] = state.DeepClone() });
                        await call(Inputs.Step(1));
                        throw new TimeoutException($"Action timed out: {action.Spec.ToJsonString()}");
                    }
                    JsonObject input;
                    try { input = Update(action, state["state"] ?? state); }
                    catch(Exception error)
                    {
                        trace?.Event("actionFailure",new JsonObject{["action"]=action.Spec.DeepClone(),["stage"]=action.Stage,["error"]=error.Message,["state"]=state.DeepClone()});
                        await call(Inputs.Step(1));
                        throw;
                    }
                    inputs[Player(action.Spec)] = input;
                    if(action.Done)trace?.Event("actionComplete",new JsonObject{["action"]=action.Spec.DeepClone(),["frames"]=action.Frames,["runtimeEntityId"]=action.Motion?.Station?.EntityId,["frame"]=state["frame"]?.DeepClone()});
                }
                if (active.All(a => a.Done)) break;
                state = await call(Inputs.Step(1, inputs));
                foreach (var action in active.Where(a => !a.Done)) action.Frames++;
            }
            // Release edges between phases and capture state for the next phase.
            state = await call(Inputs.Step(1));
            trace?.Event("phaseComplete", new JsonObject { ["name"] = phase["name"]?.DeepClone(), ["frame"] = state["frame"]?.DeepClone() });
        }
        return state;
    }

    private JsonObject Update(Active action, JsonNode state)
    {
        var spec = action.Spec;
        var player = Player(spec);
        var input = Inputs.Neutral(player);
        var type = spec["type"]?.ToString() ?? throw new ArgumentException("Action needs type.");
        switch (type)
        {
            case "navigate":
            {
                action.Done=DriveNavigation(action,state,input);
                break;
            }
            case "face":
            {
                if(action.Frames>=(spec["durationFrames"]?.GetValue<int>()??3)){action.Done=true;break;}
                Face(action,state,input);
                break;
            }
            case "take":
            case "place":
            case "combine":
            case "apply":
            case "assemble":
            {
                Transfer(action,state,input,type=="take");
                break;
            }
            case "chop":
            {
                Chop(action,state,input);break;
            }
            case "wash":
            {
                Wash(action,state,input);break;
            }
            case "switch-condiment":
            {
                SwitchCondiment(action,state,input);break;
            }
            case "throw":
            {
                Throw(action,state,input);break;
            }
            case "aim-cannon":
            case "board-cannon":
            case "fire-cannon":
            case "portal":
            {
                Transport(action,state,input,type);break;
            }
            case "cook":
            case "mix":
            {
                WaitForPreparation(action,state,type);break;
            }
            case "pulse":
            case "hold":
            {
                var duration = type == "pulse" ? 1 : spec["durationFrames"]?.GetValue<int>() ?? 1;
                if (action.Frames >= duration) { action.Done = true; break; }
                var button = spec["button"]?.ToString() ?? "use";
                if (button is not ("use" or "pickup" or "dash")) throw new ArgumentException("Unknown button: " + button);
                if (spec["requireTargetId"] is { } targetId)
                {
                    var chef = (state["chefs"] as JsonArray)?.OfType<JsonObject>().SingleOrDefault(c => c["playerId"]?.GetValue<int>() == player);
                    var targetField = spec["targetField"]?.ToString() ?? (button == "pickup" ? "pickupTargetId" : "useTargetId");
                    if (chef?[targetField]?.GetValue<int>() != targetId.GetValue<int>()) throw new InvalidOperationException($"Chef {player} has wrong {targetField}; input was not sent.");
                }
                input[button] = true;
                break;
            }
            case "wait":
            {
                if (spec["path"] is { } path)
                {
                    var observed = Json.At(state, path.ToString());
                    var matched = spec["atLeast"] is { } minimum ? observed is not null && Number(observed) >= Number(minimum) : JsonNode.DeepEquals(observed, spec["equals"]);
                    if (matched)
                    {
                        action.StableFrames++;
                        if (action.StableFrames >= (spec["stableFrames"]?.GetValue<int>() ?? 1))
                        {
                            trace?.Event("guardMatched", new JsonObject { ["path"] = path.ToString(), ["observed"] = observed?.DeepClone(), ["actionFrames"] = action.Frames });
                            action.Done = true;
                        }
                    }
                    else action.StableFrames = 0;
                }
                else action.Done = action.Frames >= (spec["durationFrames"]?.GetValue<int>() ?? 1);
                break;
            }
            default: throw new ArgumentException("Unknown action type: " + type);
        }
        return input;
    }
    private static double Number(JsonNode? n) => KitchenModel.N(n);
    private static int Player(JsonObject spec)
    {
        var p = spec["player"]?.GetValue<int>() ?? 0;
        return p is >= 0 and <= 3 ? p : throw new ArgumentOutOfRangeException(nameof(spec), "Player must be 0 through 3.");
    }
}
