using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private sealed class InterceptedSupply(int supplier, Work original, RouteActionHandle originalAction,
        RouteRunner.VesselThrowReceipt receipt, int frame)
    {
        public readonly int Supplier = supplier, StartFrame = frame;
        public readonly Work Original = original;
        public readonly RouteActionHandle OriginalAction = originalAction;
        public readonly RouteRunner.VesselThrowReceipt Receipt = receipt;
        public Work? ReceiverWork;
        public bool ReceiverCompleted;
    }
    private readonly List<InterceptedSupply> interceptedSupplies = [];

    private bool InterceptedSupplierPending(Work work) => interceptedSupplies.Any(l => ReferenceEquals(l.Original, work));

    private JsonObject InterceptedSupplyStatus(InterceptedSupply lease) => new()
    {
        ["supplier"] = lease.Supplier, ["receiver"] = lease.Receipt.Receiver,
        ["source"] = lease.Receipt.Source, ["sourceOrdinal"] = lease.Receipt.SourceOrdinal,
        ["sourceRegistration"] = lease.Receipt.SourceRegistration, ["nativeThrower"] = lease.Receipt.Thrower,
        ["pot"] = lease.Receipt.Vessel, ["home"] = lease.Receipt.Home, ["startFrame"] = lease.StartFrame,
        ["frame"] = Frame, ["receiverCompleted"] = lease.ReceiverCompleted,
        ["originalThrowCompleted"] = lease.OriginalAction.IsDone,
        ["originalOwnedResources"] = new JsonArray(lease.Original.OwnedResources.Order().Select(i => (JsonNode?)JsonValue.Create(i)).ToArray())
    };

    private void RequireInterceptedSupplyOwners(InterceptedSupply lease, bool receiverCompleting = false)
    {
        if (!ReferenceEquals(workers[lease.Supplier], lease.Original) ||
            !ReferenceEquals(lease.Original.Active, lease.OriginalAction) ||
            !lease.Original.OwnedResources.Contains(lease.Receipt.Vessel) || !reserved.Contains(lease.Receipt.Vessel) ||
            !lease.Original.OwnedResources.Contains(lease.Receipt.Home) || !reserved.Contains(lease.Receipt.Home) ||
            !lease.ReceiverCompleted && (receiverCompleting
                ? workers[lease.Receipt.Receiver] is not null || lease.ReceiverWork?.OwnedResources.Count != 0
                : !ReferenceEquals(workers[lease.Receipt.Receiver], lease.ReceiverWork) ||
                  lease.ReceiverWork?.OwnedResources.Contains(lease.Receipt.Source) != true || !reserved.Contains(lease.Receipt.Source)))
            throw new InvalidOperationException("Intercepted pot supply lost its exact supplier/receiver work or owned vessel/home lease.");
    }

    private void AdvanceInterceptedThrows()
    {
        foreach (var lease in interceptedSupplies.ToArray())
        {
            RequireInterceptedSupplyOwners(lease);
            if (Frame - lease.StartFrame > 120)
                throw new TimeoutException("Native intercepted-pot handoff exceeded its bounded two-second recovery window.");
            bool consumed = runner.ValidateInterceptedPotThrow(lease.OriginalAction, lease.Receipt, response);
            if (lease.ReceiverWork?.Active?.Error is { } error)
                throw new InvalidOperationException("Native intercepted-pot placement failed: " + error);
            if (lease.ReceiverCompleted && lease.OriginalAction.IsDone && consumed)
            {
                Log("plannerInterceptedSupplyComplete", InterceptedSupplyStatus(lease));
                interceptedSupplies.Remove(lease);
            }
        }
        foreach (int supplier in new[] { 2 })
        {
            if (workers[supplier] is not { Active: { } active } original || original.Actions.Count != 0 ||
                InterceptedSupplierPending(original) || runner.ObserveInterceptedPotThrow(active, response) is not { } receipt) continue;
            int receiver = receipt.Receiver;
            if (workers[receiver] is not null || Region(receiver) != "center" || !TrafficControlsReady(receiver) ||
                IsSauceParticipant(receiver) || IsEarlyOnionParticipant(receiver) || IsBakeryParticipant(receiver) ||
                IsFryerParticipant(receiver) || IsPotParticipant(receiver) || cannonInterruptions.ContainsKey(receiver) ||
                trafficYield?.Helper == receiver || trafficYield?.Winner == receiver ||
                !original.OwnedResources.Contains(receipt.Vessel) || !reserved.Contains(receipt.Vessel) ||
                !original.OwnedResources.Contains(receipt.Home) || !reserved.Contains(receipt.Home) || !Free(receipt.Source)) continue;
            var station = Station(receipt.Home);
            if (station is null || !station.Regions.Contains("center")) continue;
            var path = Navigation.ToStation(model, Position(receiver), station, TrafficObstacles(receiver));
            double speed = N(chefs[receiver]["runSpeed"]) * N(chefs[receiver]["surfaceSpeedMultiplier"]);
            double seconds = path.Success && speed > 0 ? path.Length / speed + 1 : double.PositiveInfinity;
            if (seconds > 1.5 || receipt.FlightFramesRemaining < 125) continue;
            var lease = new InterceptedSupply(supplier, original, active, receipt, Frame);
            var place = A("place", receipt.Home); place["dash"] = false; place["shortDash"] = false; place["timeoutFrames"] = 110;
            if (!Start(receiver, "return-intercepted-native-sausage", [place], [receipt.Source], () =>
            {
                RequireInterceptedSupplyOwners(lease, true);
                if (Held(receiver) != 0 || !runner.ValidateInterceptedPotThrow(active, receipt, response))
                    throw new InvalidOperationException("Intercepted-pot placement completed without the original ingredient being consumed into its exact pot.");
                lease.ReceiverCompleted = true;
                Log("plannerInterceptedSupplyPlaced", InterceptedSupplyStatus(lease));
            })) continue;
            lease.ReceiverWork = workers[receiver];
            interceptedSupplies.Add(lease);
            var description = InterceptedSupplyStatus(lease);
            description["approachPath"] = JsonSerializer.SerializeToNode(path);
            description["estimatedSeconds"] = seconds;
            description["nativeHomeGuard"] = receipt.HomeGuard.DeepClone();
            Log("plannerInterceptedSupplyStarted", description);
        }
    }
}
