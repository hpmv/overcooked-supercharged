using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private enum CannonFlightPhase { Firing, Flying }
    private sealed class CannonFlight(int cannon, int button, int firer, int passenger, int passengerEntity,
        int held, string destination, Point2 landing, int frame)
    {
        public readonly int Cannon = cannon, Button = button, Firer = firer, Passenger = passenger,
            PassengerEntity = passengerEntity, Held = held, Started = frame;
        public readonly string Destination = destination;
        public readonly Point2 Landing = landing;
        public readonly Dictionary<int, int> Ordinals = [];
        public readonly HashSet<int> Owned = new[] { cannon, passengerEntity, held }.Where(i => i != 0).ToHashSet();
        public Work? FireWork;
        public RouteActionHandle? FireAction;
        public CannonFlightPhase Phase;
        public int LaunchFrame = -1, LastArrivalFrame = -1, StableArrivalSamples;
        public double LaunchTime;
        public JsonObject? LaunchReceipt;
    }
    private readonly Dictionary<int, CannonFlight> cannonFlights = [];
    private const int CannonArrivalTimeoutFrames = 120;
    private const double CannonArrivalRadius = 1;
    private bool IsCannonArrivalPassenger(int player) => cannonFlights.Values.Any(f => f.Passenger == player);

    private bool CanReserveCannonFlight(int cannon, int passenger)
    {
        if (!options.ReleaseFiringChefOnLaunch) return true;
        int held = Held(passenger), chef = I(chefs[passenger]["entityId"]);
        var target = Entity(cannon)?["cannonTarget"];
        string? destination = target is null ? null : model.RegionAt(KitchenModel.Position(target));
        return destination is not null && workers[passenger] is null && !IsCannonArrivalPassenger(passenger) &&
            Free(cannon, chef, held) && !cannonFlights.Values.Any(f => f.Destination == destination) &&
            new[] { cannon, chef, held }.Where(i => i != 0).All(i => Entity(i)?["observedOrdinal"] is not null);
    }

    private bool StartCannonFire(int player, string name, JsonObject action, int cannon, int button, Action? afterLaunch = null)
    {
        if (!options.ReleaseFiringChefOnLaunch) return Start(player, name, [action], [cannon, button], afterLaunch);
        int passenger = I(action["passengerPlayer"]);
        if (!CanReserveCannonFlight(cannon, passenger) || !Free(button) || workers[player] is not null ||
            Entity(button)?["observedOrdinal"] is null || Entity(cannon)?["cannonState"]?.ToString() != "Load" ||
            B(Entity(cannon)?["cannonFlying"]) || B(chefs[passenger]["controlsEnabled"]) ||
            I(Entity(cannon)?["cannonLoadedEntityId"]) != I(chefs[passenger]["entityId"])) return false;
        var landing = KitchenModel.Position(Entity(cannon)!["cannonTarget"]);
        string destination = model.RegionAt(landing)!;
        if (destination != action["destinationRegion"]?.ToString()) throw new InvalidOperationException("Cannon arrival lease disagrees with the actual native target platform.");
        var flight = new CannonFlight(cannon, button, player, passenger, I(chefs[passenger]["entityId"]), Held(passenger), destination, landing, Frame);
        foreach (int id in flight.Owned.Append(button)) flight.Ordinals.Add(id, I(Entity(id)?["observedOrdinal"]));
        foreach (int id in flight.Owned) reserved.Add(id);
        cannonFlights.Add(cannon, flight);
        action["completeOnLaunch"] = true;
        if (!Start(player, name, [action], [button], () =>
            {
                ConfirmCannonLaunch(flight);
                afterLaunch?.Invoke();
            })) throw new InvalidOperationException("Cannon firing lost its checked chef/button while retaining the flight lease.");
        flight.FireWork = workers[player];
        Log("cannonArrivalReserved", CannonFlightStatus(flight)); return true;
    }

    private void RequireCannonFlightIdentity(CannonFlight flight)
    {
        if (flight.Ordinals.Any(p => !SameObservedEntity(p.Key, p.Value)) || flight.Owned.Any(id => !reserved.Contains(id)) ||
            I(chefs[flight.Passenger]["entityId"]) != flight.PassengerEntity || Held(flight.Passenger) != flight.Held ||
            B(chefs[flight.Passenger]["respawning"]) || workers[flight.Passenger] is not null ||
            workers.OfType<Work>().Any(w => w.OwnedResources.Overlaps(flight.Owned)) ||
            I(Entity(flight.Cannon)?["cannonLoadedEntityId"]) != flight.PassengerEntity ||
            Entity(flight.Cannon)?["cannonTarget"] is null || KitchenModel.Position(Entity(flight.Cannon)!["cannonTarget"]).Distance(flight.Landing) > .0001)
            throw new InvalidOperationException("Cannon arrival lost its exact passenger, held item, cannon, destination or exclusive resources.");
        // Held items are native logical objects; disappearing render proxies do
        // not substitute their identity. No input is emitted for the passenger.
        if (flight.Held != 0 && !B(Entity(flight.Held)?["active"]))
            throw new InvalidOperationException("Cannon passenger's original held item is no longer active.");
    }

    private void ConfirmCannonLaunch(CannonFlight flight)
    {
        RequireCannonFlightIdentity(flight);
        var receipt = flight.FireAction is { } action ? runner.CannonLaunchReceipt(action) : null;
        if (receipt is null || flight.Phase != CannonFlightPhase.Firing || workers[flight.Firer] is not null || Held(flight.Firer) != 0 ||
            reserved.Contains(flight.Button) || !B(receipt["nativeFlying"]) || I(receipt["cannon"]) != flight.Cannon ||
            I(receipt["button"]) != flight.Button || I(receipt["passenger"]) != flight.PassengerEntity ||
            I(receipt["passengerPlayer"]) != flight.Passenger || I(receipt["held"]) != flight.Held ||
            receipt["destinationRegion"]?.ToString() != flight.Destination ||
            KitchenModel.Position(receipt["landingTarget"]).Distance(flight.Landing) > .0001 ||
            !B(Entity(flight.Cannon)?["cannonFlying"]) || Entity(flight.Cannon)?["cannonState"]?.ToString() != "Launched")
            throw new InvalidOperationException("Firing-chef release lacks the original action's accepted native launch and released fire input.");
        foreach (var (id, ordinal) in flight.Ordinals)
        {
            string field = id == flight.Cannon ? "cannonOrdinal" : id == flight.Button ? "buttonOrdinal" : id == flight.PassengerEntity ? "passengerOrdinal" : "heldOrdinal";
            if (I(receipt[field]) != ordinal) throw new InvalidOperationException("Cannon launch receipt substituted an observed native identity.");
        }
        var emitted = (response["inputs"] as JsonArray)?.OfType<JsonObject>().SingleOrDefault(i => I(i["player"]) == flight.Firer);
        if (emitted is null || B(emitted["use"]) || B(emitted["pickup"]) || B(emitted["dash"]) || N(emitted["x"]) != 0 || N(emitted["y"]) != 0)
            throw new InvalidOperationException("Firing input was not released before the chef became available.");
        flight.Phase = CannonFlightPhase.Flying; flight.LaunchFrame = I(receipt["launchFrame"]); flight.LaunchTime = N(receipt["launchClientTime"]);
        flight.LaunchReceipt = receipt; flight.FireWork = null;
        Log("cannonFiringChefReleased", CannonFlightStatus(flight));
    }

    private void AdvanceCannonFlights()
    {
        foreach (var flight in cannonFlights.Values.ToArray())
        {
            RequireCannonFlightIdentity(flight);
            if (flight.Phase == CannonFlightPhase.Firing)
            {
                if (!ReferenceEquals(workers[flight.Firer], flight.FireWork) || flight.FireWork is null ||
                    !flight.FireWork.OwnedResources.SetEquals(new[] { flight.Button }) || !reserved.Contains(flight.Button))
                    throw new InvalidOperationException("Cannon firing job lost its original owner or button reservation before launch.");
                if (flight.FireWork.Active is { } active)
                {
                    if (flight.FireAction is not null && !ReferenceEquals(flight.FireAction, active))
                        throw new InvalidOperationException("Cannon flight replaced its active fire action.");
                    flight.FireAction = active;
                }
                continue;
            }
            if (flight.LaunchFrame < 0 || Frame - flight.LaunchFrame > CannonArrivalTimeoutFrames)
                throw new TimeoutException("Native cannon passenger did not complete its exact arrival within two advancing seconds.");
            var chef = chefs[flight.Passenger];
            bool arrived = !B(Entity(flight.Cannon)?["cannonFlying"]) && B(chef["controlsEnabled"]) && B(chef["directlyControlled"]) &&
                B(chef["canAcceptInput"]) && !B(chef["inputSuppressed"]) && !B(chef["respawning"]) && Region(flight.Passenger) == flight.Destination &&
                N(chef["position"]?["y"]) <= .9 && Position(flight.Passenger).Distance(flight.Landing) <= CannonArrivalRadius;
            if (!arrived) { flight.StableArrivalSamples = 0; flight.LastArrivalFrame = Frame; continue; }
            if (Frame > flight.LastArrivalFrame) { flight.LastArrivalFrame = Frame; flight.StableArrivalSamples++; }
            if (flight.StableArrivalSamples < 2 || N(state["clientTime"]) <= flight.LaunchTime) continue;
            Log("cannonPassengerArrivalConfirmed", CannonFlightStatus(flight));
            foreach (int id in flight.Owned) reserved.Remove(id);
            cannonFlights.Remove(flight.Cannon);
        }
    }

    private JsonObject CannonFlightStatus(CannonFlight flight) => new() { ["cannon"] = flight.Cannon, ["button"] = flight.Button,
        ["firingPlayer"] = flight.Firer, ["passengerPlayer"] = flight.Passenger, ["passengerEntity"] = flight.PassengerEntity,
        ["heldEntity"] = flight.Held, ["destinationRegion"] = flight.Destination, ["phase"] = flight.Phase.ToString(),
        ["landingTarget"] = new JsonObject { ["x"] = flight.Landing.X, ["z"] = flight.Landing.Z }, ["landingRadius"] = CannonArrivalRadius,
        ["frame"] = Frame, ["launchFrame"] = flight.LaunchFrame, ["stableArrivalSamples"] = flight.StableArrivalSamples,
        ["ownedResources"] = new JsonArray(flight.Owned.Order().Select(i => (JsonNode?)JsonValue.Create(i)).ToArray()),
        ["launchReceipt"] = flight.LaunchReceipt?.DeepClone() };
    private JsonArray CannonFlightStatus() => new(cannonFlights.Values.OrderBy(f => f.Cannon).Select(f => (JsonNode?)CannonFlightStatus(f)).ToArray());
}
