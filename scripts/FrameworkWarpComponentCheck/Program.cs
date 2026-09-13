using Hpmv;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

var root = Path.GetFullPath(args.Length == 0 ? "." : args[0]);
string PathOf(string relative) => Path.Combine(root, relative);
var loadPath = PathOf("artifacts/framework-migration/native-e/load.json");
var savedPath = PathOf("artifacts/framework-migration/native-e/probe-181.pb");
var inspection = JsonNode.Parse(File.ReadAllText(loadPath))["steps"][3]["response"];
var registry = inspection["registry"].AsArray().ToDictionary(e => e["EntityId"].GetValue<int>());
var inherited = new Dictionary<string, string[]> {
    ["ServerCampaignFlowController"] = new[] { "ServerKitchenFlowControllerBase" },
    ["ServerPlateStack"] = new[] { "ServerPlateStackBase" },
    ["ServerCleanPlateStack"] = new[] { "ServerPlateStackBase" }
};
var blocks = new Dictionary<string, string[]> {
    ["AttachStation"] = new[]{"ServerAttachStation"}, ["ChefCarry"] = new[]{"ServerPlayerAttachmentCarrier"},
    ["Chef"] = new[]{"ClientPlayerControlsImpl_Default"}, ["Cannon"] = new[]{"ServerCannonMod"},
    ["WorkableItem"] = new[]{"ServerWorkableItem"}, ["ThrowableItem"] = new[]{"ServerThrowableItem"},
    ["Terminal"] = new[]{"ServerTerminal"}, ["PilotRotation"] = new[]{"ServerPilotRotation"},
    ["IngredientContainer"] = new[]{"ServerIngredientContainer"}, ["CookingHandler"] = new[]{"ServerCookingHandler"},
    ["CookingStation"] = new[]{"ServerCookingStation"}, ["MixingHandler"] = new[]{"ServerMixingHandler"},
    ["PickupItemSwitcher"] = new[]{"ServerPickupItemSwitcher","ServerPlacementItemSwitcher"},
    ["TriggerColourCycle"] = new[]{"ServerTriggerColourCycle"}, ["Stack"] = new[]{"Stack"},
    ["PlateReturnStation"] = new[]{"ServerPlateReturnStation"}, ["Workstation"] = new[]{"ServerWorkstation"},
    ["WashingStation"] = new[]{"ServerWashingStation"},
    ["KitchenController"] = new[]{"ServerKitchenFlowControllerBase"}, ["PlateReturnController"] = new[]{"ServerKitchenFlowControllerBase"}
};
List<object> Audit(GameSetup setup, int frame, out WarpSpec warp) {
    warp = WarpCalculator.CalculateWarp(setup.entityRecords.FixedEntities.ToDictionary(e => e.path.ids[0]), setup.entityRecords, frame, frame);
    var byId = warp.Entities.Where(e => e.__isset.entityId).ToDictionary(e => e.EntityId);
    var errors = new List<object>();
    foreach (var pair in registry) {
        var components = pair.Value["Components"].AsArray().Select(x => x.GetValue<string>()).ToHashSet();
        foreach (var component in components.ToArray()) if (inherited.TryGetValue(component, out var parents)) foreach(var parent in parents) components.Add(parent);
        byId.TryGetValue(pair.Key, out var spec);
        foreach (var block in blocks) {
            bool native = block.Value.Any(components.Contains);
            bool present = spec != null && typeof(EntityWarpSpec).GetProperty(block.Key).GetValue(spec) != null;
            if (native != present) errors.Add(new { entity = pair.Key, name = pair.Value["Name"].ToString(), block = block.Key, native, present, entityIncluded = spec != null });
        }
    }
    return errors;
}
var saved = Hpmv.Save.GameSetup.Parser.ParseFrom(File.ReadAllBytes(savedPath)).FromProto();
var before = Audit(saved, 181, out var savedWarp);
var current = new Carnival34FourLevel();
var after = Audit(current, 0, out var currentWarp);
int checks = 0;
void Check(bool value, string why) { if (!value) throw new InvalidOperationException(why); checks++; }
Check(registry.Count == 122, "Native-e registry must retain all122 actual records.");
Check(before.Count == 13, "Captured pre-fix checkpoint must expose all13 demonstrated mismatches.");
Check(after.Count == 0, "Current generated warp must cover every registered supported native component exactly.");
foreach(var record in saved.entityRecords.FixedEntities)
    record.prefab = current.entityRecords.GetRecordFromPath(record.path.ids).prefab;
Check(Audit(saved,181,out var repairedCapturedWarp).Count == 0, "Annotation-only repair of actual181 history must remove all component mismatches.");
void Negative(Action<GameSetup> damage, string why) {
    var candidate = new Carnival34FourLevel(); damage(candidate);
    Check(Audit(candidate,0,out _).Count != 0, why);
}
PrefabRecord Prefab(GameSetup setup,int id) => setup.entityRecords.GetRecordFromPath(new[]{id}).prefab;
Negative(s => Prefab(s,14).IsCookingStation=true, "Extra mixer cooking block must reject.");
Negative(s => Prefab(s,72).IsPickupItemSwitcher=false, "Entirely omitted dispenser must reject.");
Negative(s => Prefab(s,73).IsAttachStation=false, "Entirely omitted serving attachment must reject.");
Negative(s => Prefab(s,74).IsPlateReturnStation=false, "Missing dirty-return block must reject.");
Negative(s => Prefab(s,76).IsAttachStation=false, "Missing dryer attachment must reject even with a return block.");
Negative(s => Prefab(s,77).IsAttachStation=false, "Entirely omitted cannon button attachments must reject.");
Negative(s => Prefab(s,79).HasTriggerColorCycle=false, "Missing condiment colour-cycle block must reject.");
Negative(s => Prefab(s,80).IsAttachStation=false, "Entirely omitted bin attachment must reject.");
Negative(s => Prefab(s,75).IsAttachStation=false, "Previously demonstrated sink attachment omission must reject.");
var output = PathOf("artifacts/framework-warp-component-check"); Directory.CreateDirectory(output);
var jsonOptions = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
File.WriteAllText(Path.Combine(output, "captured-warp-181.json"), JsonSerializer.Serialize(savedWarp,jsonOptions));
File.WriteAllText(Path.Combine(output, "current-warp-0.json"), JsonSerializer.Serialize(currentWarp,jsonOptions));
File.WriteAllText(Path.Combine(output, "annotation-repaired-captured-warp-181.json"), JsonSerializer.Serialize(repairedCapturedWarp,jsonOptions));
var report = new { checks, nativeRegistryCount=registry.Count, capturedFrame=181, capturedMismatches=before, currentMismatches=after,
    hashes = new { load=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(loadPath))), saved=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(savedPath))),
        level=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(PathOf("framework/controller/Data/Levels/Carnival34Four.cs")))) },
    scope="Offline native-e registered component/block coverage; not a live warp or physics equivalence proof." };
File.WriteAllText(Path.Combine(output,"report.json"), JsonSerializer.Serialize(report,jsonOptions));
Console.WriteLine(JsonSerializer.Serialize(report,jsonOptions));
