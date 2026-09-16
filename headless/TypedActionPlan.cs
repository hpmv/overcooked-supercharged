using Hpmv;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace Supercharged.Headless;

/// <summary>Atomic preflight over original serializable actions; resources become ordinary graph dependencies.</summary>
public sealed class TypedActionPlan
{
    public sealed record Node(string Key, int Id, GameEntityRecord Chef, GameAction Action, int TimeoutFrames, int[] Dependencies, string[] Resources);
    public List<Node> Nodes { get; } = new();
    public int MaximumFrames { get; private set; }
    public static TypedActionPlan Create(GameSetup setup, IReadOnlyDictionary<int, GameEntityRecord> live, int frame, JsonObject request)
    {
        var plan = new TypedActionPlan { MaximumFrames = request["maximumFrames"]?.GetValue<int>() ?? 1800 };
        if (plan.MaximumFrames is < 2 or > 36000) throw new ArgumentException("maximumFrames must be2..36000.");
        var entries = request["actions"]?.AsArray() ?? throw new ArgumentException("actions array required.");
        if (entries.Count is < 1 or > 256) throw new ArgumentException("Use1..256 topologically ordered action nodes.");
        var byKey = new Dictionary<string, Node>(); var lastResource = new Dictionary<string, int>();
        foreach (var entry in entries)
        {
            var row = entry!.AsObject(); string key = row["id"]?.ToString(); string type = row["type"]?.ToString();
            if (string.IsNullOrWhiteSpace(key) || byKey.ContainsKey(key)) throw new ArgumentException("Each action needs a unique nonempty id.");
            int chefId = row["chef"]?.GetValue<int>() ?? 0;
            if (!live.TryGetValue(chefId, out var chef) || !setup.entityRecords.Chefs.ContainsKey(chef) || !chef.existed[frame]) throw new ArgumentException("Action chef must be an observed live chef.");
            int timeout = row["timeoutFrames"]?.GetValue<int>() ?? 600;
            if (timeout is < 2 or > 3600) throw new ArgumentException("Each action timeoutFrames must be2..3600.");
            var dependencies = new HashSet<int>(); var resources = new HashSet<string> { "chef:" + chefId };
            foreach (var predecessor in row["after"]?.AsArray() ?? new JsonArray())
            {
                if (!byKey.TryGetValue(predecessor!.ToString(), out var prior)) throw new ArgumentException("Dependencies must name earlier nodes; cycles and forward references are rejected.");
                dependencies.Add(prior.Id);
            }
            GameEntityRecord RequireLive(int id)
            {
                if (!live.TryGetValue(id, out var found) || !found.existed[frame]) throw new ArgumentException("Target/resource must be an observed live entity: " + id);
                return found;
            }
            foreach (var resource in row["resources"]?.AsArray() ?? new JsonArray()) { int id = resource!.GetValue<int>(); RequireLive(id); resources.Add("entity:" + id); }
            IEntityReference Reference()
            {
                if (row["target"] is not null && row["spawnedBy"] is not null) throw new ArgumentException("Use target or spawnedBy, not both.");
                if (row["target"] is not null) { int id = row["target"]!.GetValue<int>(); resources.Add("entity:" + id); return new LiteralEntityReference(RequireLive(id)); }
                if (row["spawnedBy"] is not null && byKey.TryGetValue(row["spawnedBy"]!.ToString(), out var producer) && producer.Action is InteractAction { ExpectSpawn: true })
                { dependencies.Add(producer.Id); resources.Add("spawn:" + producer.Id); return new SpawnedEntityReference(producer.Id); }
                throw new ArgumentException("Exact observed target or earlier spawn-claiming producer required.");
            }
            double Number(string name, double fallback)
            { double v = row[name]?.Deserialize<double>() ?? fallback; if (!double.IsFinite(v)) throw new ArgumentException("Finite " + name + " required."); return v; }
            float FloatNumber(string name)
            { float v = (float)Number(name, double.NaN); if (!float.IsFinite(v)) throw new ArgumentException(name + " exceeds finite framework float range."); return v; }
            LocationToken Location()
            {
                if (row["target"] is not null || row["spawnedBy"] is not null) return new EntityLocationToken(Reference());
                return new LiteralLocationToken(new(FloatNumber("x"), FloatNumber("z")));
            }
            bool dash = row["dash"]?.GetValue<bool>() == true;
            GameAction action;
            switch (type)
            {
                case "goto":
                    var location = Location();
                    if (location is LiteralLocationToken literal && (chef.position[frame].XZ() - literal.location).Length() > .01 &&
                        (!setup.mapByChef.TryGetValue(chefId, out var map) || map.FindPath(chef.position[frame].XZ(), new() { literal.location }).Count < 2))
                        throw new InvalidOperationException("No initial framework path to goto target.");
                    action = new GotoAction { DesiredPos = location, DisallowDash = !dash, DisallowOvershoot = true }; break;
                case "pickup":
                case "place":
                case "interact":
                    action = new InteractAction { Subject = Reference(), IsPickup = type == "pickup", Primary = type != "interact",
                        RequireObservedTransfer = type is "pickup" or "place",
                        ExpectSpawn = type == "pickup" && row["expectSpawn"]?.GetValue<bool>() == true, DisallowDash = !dash, DisallowOvershoot = true }; break;
                case "prepare-primary":
                    if (row["expectSpawn"]?.GetValue<bool>() == true) throw new ArgumentException("prepare-primary does not issue an interaction or claim a spawn.");
                    action = new InteractAction { Subject = Reference(), Primary = true, Prepare = true,
                        RequireObservedTransfer = false, DisallowDash = !dash, DisallowOvershoot = true }; break;
                case "throw": action = new ThrowAction { Location = Location() }; break;
                case "drop": action = new DropAction { Location = Location() }; break;
                case "wait":
                    int frames = row["frames"]?.GetValue<int>() ?? 0;
                    if (frames < 1 || frames >= timeout) throw new ArgumentException("wait frames must be positive and smaller than timeoutFrames.");
                    action = new WaitAction { NumFrames = frames }; break;
                case "wait-progress":
                    var subject = Reference(); double progress = Number("progress", double.NaN);
                    if (progress < 0) throw new ArgumentException("Nonnegative progress required.");
                    action = row["progressType"]?.ToString() switch {
                        "cooking" => new WaitForCookingProgressAction { Entity = subject, CookingProgress = progress },
                        "mixing" => new WaitForMixingProgressAction { Entity = subject, MixingProgress = progress },
                        "chopping" => new WaitForChoppingProgressAction { Entity = subject, ChoppingProgress = progress },
                        "washing" => new WaitForWashingProgressAction { Entity = subject, WashingProgress = progress },
                        _ => throw new ArgumentException("progressType must be cooking, mixing, chopping, or washing.") }; break;
                case "pilot-rotation": action = new PilotRotationAction { PilotRotationEntity = Reference(), TargetAngle = FloatNumber("angle") }; break;
                default: throw new ArgumentException("Unsupported original action type: " + type);
            }
            foreach (string resource in resources) if (lastResource.TryGetValue(resource, out int predecessor)) dependencies.Add(predecessor);
            int actionId = checked(setup.sequences.NextId + plan.Nodes.Count); action.ActionId = actionId; action.Chef = chef;
            // Exercise original checkpoint serialization before any mutation.
            action.ToProto();
            var node = new Node(key, actionId, chef, action, timeout, dependencies.Order().ToArray(), resources.Order().ToArray());
            plan.Nodes.Add(node); byKey.Add(key, node); foreach (string resource in resources) lastResource[resource] = actionId;
        }
        return plan;
    }
    public void Install(GameSetup setup)
    {
        if (setup.sequences.NextId != Nodes[0].Id) throw new InvalidOperationException("Action graph changed after preflight.");
        foreach (var node in Nodes)
        {
            int index = setup.sequences.ChefIndexByChef[node.Chef];
            int id = setup.sequences.InsertAction((index, setup.sequences.Actions[index].Count), node.Action);
            setup.sequences.NodeById[id].Deps.AddRange(node.Dependencies);
        }
    }
}
