using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private sealed record SupplyHome(string Role, int Home, int VesselOrdinal, int HomeOrdinal, Point2 VesselPosition, Point2 HomePosition);
    private readonly Dictionary<int, SupplyHome> supplyHomes = [];

    private void CaptureSupplyTopology(bool required = true)
    {
        // These are the measured native receiving lanes. A utensil parked on a
        // counter must never become the "near" utensil by sorting its new pose.
        foreach (var vessel in Stations("pot").Concat(Stations("bowl")))
        {
            int home = AttachmentParent(vessel.EntityId);
            var e = Entity(vessel.EntityId); var h = Entity(home);
            string component = vessel.Role == "pot" ? "CookingStation" : "MixingStation";
            var homePosition = KitchenModel.Position(h?["position"]);
            Point2[] anchors = vessel.Role == "pot" ? [new(16.8, -10.8), new(18, -10.8)] : [new(22.8, -10.8), new(24, -10.8)];
            Point2 expectedVessel = vessel.Role == "pot" ? homePosition : new(homePosition.X - .008, homePosition.Z - .195);
            if (home == 0 || e?["observedOrdinal"] is null || h?["observedOrdinal"] is null ||
                !KitchenModel.Components(h!).Contains(component) || !anchors.Any(a => a.Distance(homePosition) < .05) ||
                vessel.Position.Distance(expectedVessel) > .05) continue;
            supplyHomes.TryAdd(vessel.EntityId, new(vessel.Role, home, I(e!["observedOrdinal"]), I(h!["observedOrdinal"]), vessel.Position, homePosition));
        }
        if (required && supplyHomes.Count != 4)
            throw new InvalidOperationException("Pantry throw topology requires the four native vessels at their measured original processing homes.");
    }

    private int NearSupplyVessel(int player)
    {
        if (supplyHomes.Count == 0) CaptureSupplyTopology(false); // Offline/fresh planner fixtures; unknown poses remain ineligible.
        string role = player == 2 ? "pot" : "bowl";
        double nearX = player == 2 ? 16.8 : 24;
        return supplyHomes.Where(p => p.Value.Role == role && Math.Abs(p.Value.HomePosition.X - nearX) < .05)
            .Select(p => p.Key).SingleOrDefault();
    }

    private JsonObject? DirectSupplyGuard(int player, int vessel)
    {
        if (vessel != NearSupplyVessel(player)) return null;
        return CapturedSupplyHomeGuard(vessel);
    }

    // Identity evidence only. Far-pot admission still requires its separate
    // native lane guard; this helper never grants direct-throw permission.
    private JsonObject? CapturedSupplyHomeGuard(int vessel)
    {
        if (!supplyHomes.TryGetValue(vessel, out var home)) return null;
        var guard = new JsonObject
        {
            ["vessel"] = vessel, ["vesselOrdinal"] = home.VesselOrdinal, ["home"] = home.Home,
            ["homeOrdinal"] = home.HomeOrdinal, ["role"] = home.Role,
            ["vesselPosition"] = new JsonObject { ["x"] = home.VesselPosition.X, ["z"] = home.VesselPosition.Z },
            ["homePosition"] = new JsonObject { ["x"] = home.HomePosition.X, ["z"] = home.HomePosition.Z }
        };
        return NativeSupplyHome.Invalid(state, guard) is null ? guard : null;
    }
}

internal static class NativeSupplyHome
{
    internal static string? Invalid(JsonNode snapshot, JsonObject guard)
    {
        var state = KitchenModel.SnapshotState(snapshot.AsObject());
        var entities = state["entities"]?.AsArray().OfType<JsonObject>().ToArray() ?? [];
        JsonObject? Entity(int id) => entities.SingleOrDefault(e => e["id"]?.GetValue<int>() == id);
        int vessel = guard["vessel"]?.GetValue<int>() ?? 0, home = guard["home"]?.GetValue<int>() ?? 0;
        var e = Entity(vessel); var h = Entity(home);
        if (e?["active"]?.GetValue<bool>() != true || h?["active"]?.GetValue<bool>() != true ||
            e["observedOrdinal"] is null || h["observedOrdinal"] is null || guard["vesselOrdinal"] is null || guard["homeOrdinal"] is null ||
            e["observedOrdinal"]?.ToJsonString() != guard["vesselOrdinal"]?.ToJsonString() ||
            h["observedOrdinal"]?.ToJsonString() != guard["homeOrdinal"]?.ToJsonString()) return "native vessel/home identity changed";
        if (h["attachedEntityId"]?.GetValue<int>() != vessel ||
            entities.Count(p => p["attachedEntityId"]?.GetValue<int>() == vessel) != 1) return "receiving vessel left its original processing home";
        string role = guard["role"]?.ToString() ?? "";
        string component = role == "pot" ? "CookingStation" : "MixingStation";
        if (role is not ("pot" or "bowl") || !KitchenModel.Components(h).Contains(component) ||
            KitchenModel.Position(e["position"]).Distance(KitchenModel.Position(guard["vesselPosition"])) > .05 ||
            KitchenModel.Position(h["position"]).Distance(KitchenModel.Position(guard["homePosition"])) > .05)
            return "receiving vessel/home differs from the measured native throw lane";
        if (state["chefs"]?.AsArray().OfType<JsonObject>().Any(c => c["heldEntityId"]?.GetValue<int>() == vessel) == true)
            return "receiving vessel is held";
        return null;
    }
}
