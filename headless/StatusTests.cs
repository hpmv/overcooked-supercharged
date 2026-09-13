using Google.Protobuf;
using Hpmv;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Supercharged.Headless;

public static class StatusTests
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("Status: " + label); checks++; }
        void Reject(Action f) { try { f(); } catch (ArgumentException) { checks++; return; } throw new InvalidOperationException("Status mutation arguments were not rejected."); }
        var setup = new Carnival34FourLevel(); var session = new HeadlessSession(setup, "synthetic status fixture", root);
        var core = (RealGameConnector)typeof(HeadlessSession).GetField("core", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        using var memory = new MemoryStream(); using var trace = new TraceStore(memory);
        session.AttachTrace(trace); int controls = 0; session.Control += _ => controls++;
        byte[] beforeSetup = setup.ToProto().ToByteArray(); var before = session.Inspect(true);
        JsonObject Poll() => session.Command(new() { ["command"] = "status", ["full"] = false, ["development"] = false });
        var status = Poll();
        foreach (string field in new[] { "ok", "connected", "traceFailure", "state", "frame", "requestPending", "exchanges", "freshLevelLoadObserved", "needsFreshLevelBaseline", "lastEmpiricalFrame", "movementAction", "movementCompleted", "invalidStateReason", "errors", "rawInput" })
            Check(JsonNode.DeepEquals(before[field], status[field]), "inspection field preserved " + field);
        foreach (string field in new[] { "outcome", "active", "error", "startFrame", "maximumFrames" })
            Check(JsonNode.DeepEquals(before["typedActions"]![field], status["typedActions"]![field]), "typed scalar preserved " + field);
        Check(status["kind"]!.ToString() == "supercharged-headless-status" && (int)status["version"]! == 1, "explicit distinct schema");
        Check(status["registryValidation"] is null && status["entities"] is null && status["actionGraph"] is null && status["typedActions"]!["actions"] is null, "expensive validation and graph content omitted explicitly");
        for (int i = 0; i < 100; i++) Poll();
        Check(beforeSetup.SequenceEqual(setup.ToProto().ToByteArray()) && JsonNode.DeepEquals(before, session.Inspect(true)), "repeated status leaves all setup/history and ordinary inspection unchanged");
        Check(controls == 0 && memory.Length == 0, "polling emits no control or trace rows");
        Reject(() => session.Command(new() { ["command"] = "status", ["full"] = true }));
        Reject(() => session.Command(new() { ["command"] = "status", ["development"] = true }));
        Reject(() => session.Command(new() { ["command"] = "status", ["frame"] = 900 }));
        Check(beforeSetup.SequenceEqual(setup.ToProto().ToByteArray()) && controls == 0 && memory.Length == 0, "rejected arguments leave state/control/evidence unchanged");
        core.State = RealGameState.Running; core.RequestPause();
        status = Poll(); Check((string)status["state"]! == "Running" && (bool)status["requestPending"]!, "poll exposes queued pause without executing it");
        session.ConnectionEnded("synthetic transport failure"); status = Poll();
        Check(!(bool)status["connected"]! && status["errors"]!.AsArray().Single()!.ToString() == "synthetic transport failure", "connection/error diagnosis retained");
        typeof(HeadlessSession).GetField("traceFailure", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, "synthetic disk failure");
        status = Poll(); Check(!(bool)status["ok"]! && (string)status["traceFailure"]! == "synthetic disk failure", "failed trace stays observable and cannot report success");
        Check(controls == 0 && memory.Length == 0, "error-state polling remains read-only");
        return checks;
    }
}
