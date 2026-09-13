using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    // V6 GF10853 briefly restored control between cannon release and the next
    // native flight update. Its position still labelled the departure island.
    // Loaded IDs persist after landing, so only an actively flying match blocks
    // new jobs; native arrival and normal controls decide when work can resume.
    private bool NativeCannonFlight(int player) => IsCannonArrivalPassenger(player) || Stations("cannon").Any(s =>
        B(Entity(s.EntityId)?["cannonFlying"]) &&
        I(Entity(s.EntityId)?["cannonLoadedEntityId"]) == I(chefs[player]["entityId"]));

    /// <summary>Recorded launch-race regression; reads snapshots and never calls the game.</summary>
    public static int TransitDispatchSelfTest(JsonObject launchSnapshot, JsonObject initialSnapshot)
    {
        int count = 0;
        void Check(bool value, string description)
        { if (!value) throw new InvalidOperationException("Transit dispatch regression: " + description); count++; }
        CarnivalPlanner Make(JsonObject snapshot)
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline transit fixture attempted I/O."), null)
            { response = snapshot.DeepClone().AsObject(), options = new(), recipes = CarnivalRecipes.All.ToArray() };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh(); p.delivered = 0;
            p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline transit action attempted I/O."), null);
            return p;
        }
        var observed = Make(launchSnapshot);
        int right = observed.CannonId("right");
        Check(observed.Frame == 10853 && observed.Region(1) == "upper-right" && B(observed.chefs[1]["controlsEnabled"]) &&
            B(observed.Entity(right)?["cannonFlying"]) && observed.NativeCannonFlight(1),
            "actual V6 launch has an enabled chef still labelled upper-right during its matching native cannon flight");
        observed.BakeryAndWash(observed.Region(1));
        Check(observed.workers[1] is null && observed.reserved.Count == 0,
            "one-frame launch control restoration cannot create a supply or washing job");

        foreach (int player in new[] { 1, 2 })
        {
            var p = Make(initialSnapshot);
            int cannon = p.CannonId(player == 1 ? "right" : "left");
            p.Entity(cannon)!["cannonLoadedEntityId"] = I(p.chefs[player]["entityId"]);
            p.Entity(cannon)!["cannonState"] = "Launched";
            p.Entity(cannon)!["cannonFlying"] = true;
            p.chefs[player]["controlsEnabled"] = true;
            void Dispatch()
            {
                if (player == 1) p.BakeryAndWash(p.Region(player));
                else p.PantryAndService(p.Region(player));
            }
            Dispatch();
            Check(p.workers[player] is null, "enabled in-flight chef " + player + " remains neutral between native flight updates");
            p.Entity(cannon)!["cannonFlying"] = false;
            Dispatch();
            Check(p.workers[player] is not null && !p.NativeCannonFlight(player),
                "landed chef " + player + " resumes work despite the native cannon retaining its loaded entity ID");
        }
        var unrelated = Make(initialSnapshot);
        right = unrelated.CannonId("right");
        unrelated.Entity(right)!["cannonFlying"] = true;
        unrelated.Entity(right)!["cannonLoadedEntityId"] = I(unrelated.chefs[1]["entityId"]);
        unrelated.PantryAndService(unrelated.Region(2));
        Check(unrelated.workers[2] is not null && !unrelated.NativeCannonFlight(2),
            "another chef's flight does not suppress independent pantry work");
        return count;
    }
}
