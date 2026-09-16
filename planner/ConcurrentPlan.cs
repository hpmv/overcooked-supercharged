using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

/// <summary>Executes dependency jobs through observed actions, reserving chefs and named resources.</summary>
public sealed class ConcurrentPlanRunner(Func<JsonObject,Task<JsonObject>> call,TraceWriter? trace)
{
    private sealed class Job(JsonObject spec)
    {
        public JsonObject Spec=spec;
        public string Id=spec["id"]?.GetValue<string>()??throw new ArgumentException("Job needs id.");
        public int Player=spec["player"]?.GetValue<int>()??throw new ArgumentException("Job needs player.");
        public string[] Dependencies=(spec["dependencies"] as JsonArray)?.Select(n=>n!.GetValue<string>()).ToArray()??[];
        public string[] Resources=(spec["resources"] as JsonArray)?.Select(n=>n!.GetValue<string>()).ToArray()??[];
        public JsonObject[] Actions=(spec["actions"] as JsonArray)?.Select(n=>n!.AsObject()).ToArray()??throw new ArgumentException("Job needs actions.");
        public int Next;
        public bool Started,Done;
        public RouteActionHandle? Active;
    }
    public async Task<JsonObject> RunAsync(JsonObject plan)
    {
        var jobs=(plan["jobs"] as JsonArray)?.Select(n=>new Job(n!.AsObject())).ToArray()??throw new ArgumentException("Plan needs jobs.");
        if(jobs.Select(j=>j.Id).Distinct().Count()!=jobs.Length)throw new ArgumentException("Duplicate job id.");
        if(jobs.Any(j=>j.Player is <0 or >3))throw new ArgumentException("Job player must be 0..3.");
        var lookup=jobs.ToDictionary(j=>j.Id);
        if(jobs.Any(j=>j.Dependencies.Any(d=>!lookup.ContainsKey(d))))throw new ArgumentException("Unknown job dependency.");
        // Validate cycles and resource identifiers before the first game input.
        var visited=new HashSet<string>();var visiting=new HashSet<string>();
        void Visit(Job job){if(visited.Contains(job.Id))return;if(!visiting.Add(job.Id))throw new ArgumentException("Job dependency cycle.");foreach(var id in job.Dependencies)Visit(lookup[id]);visiting.Remove(job.Id);visited.Add(job.Id);}
        foreach(var job in jobs)Visit(job);
        var runner=new RouteRunner(call,trace);var reserved=new Dictionary<string,string>();
        var state=await call(Json.Request("inspect"));
        int elapsed=0,maxFrames=plan["timeoutFrames"]?.GetValue<int>()??16500;
        try
        {
            while(jobs.Any(j=>!j.Done))
            {
                if(elapsed++>=maxFrames)throw new TimeoutException("Concurrent plan frame timeout.");
                foreach(var job in jobs.Where(j=>!j.Started).OrderBy(j=>j.Spec["priority"]?.GetValue<int>()??0).ThenBy(j=>j.Id,StringComparer.Ordinal))
                {
                    if(job.Dependencies.Any(d=>!lookup[d].Done)||jobs.Any(j=>j.Started&&!j.Done&&j.Player==job.Player)||job.Resources.Any(reserved.ContainsKey))continue;
                    job.Started=true;foreach(var key in job.Resources)reserved.Add(key,job.Id);
                    trace?.Event("jobStart",new JsonObject{["id"]=job.Id,["player"]=job.Player,["frame"]=state["state"]?["gameplayFrame"]?.DeepClone(),["resources"]=job.Spec["resources"]?.DeepClone()});
                    Console.Error.WriteLine($"job {job.Id} started (chef {job.Player})");
                }
                var inputs=Inputs.AllNeutral();bool any=false;
                foreach(var job in jobs.Where(j=>j.Started&&!j.Done))
                {
                    any=true;
                    if(job.Active?.IsDone==true)
                    {
                        if(job.Active.Error is { } error)throw new InvalidOperationException($"Job {job.Id}, action {job.Next-1}: {error}");
                        job.Active=null;
                    }
                    if(job.Active is null)
                    {
                        if(job.Next>=job.Actions.Length)
                        {
                            job.Done=true;foreach(var key in job.Resources)reserved.Remove(key);
                            trace?.Event("jobComplete",new JsonObject{["id"]=job.Id,["frame"]=state["state"]?["gameplayFrame"]?.DeepClone()});
                            Console.Error.WriteLine($"job {job.Id} completed");continue;
                        }
                        var action=job.Actions[job.Next++].DeepClone().AsObject();action["player"]=job.Player;
                        action["timeoutFrames"]??=600;
                        job.Active=runner.CreateAction(action);
                    }
                    inputs[job.Player]=runner.Tick(job.Active,state);
                }
                if(!any)throw new InvalidOperationException("No runnable job; dependency/reservation deadlock.");
                state=await call(Inputs.Step(1,inputs));
            }
            return state;
        }
        catch(Exception error)
        {
            trace?.Event("planFailure",new JsonObject{["error"]=error.Message,["state"]=state.DeepClone()});
            await call(Inputs.Step(1));throw;
        }
    }
}
