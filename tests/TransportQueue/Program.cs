using Oc2Tas;
using System.Text.Json;

int checks = 0;
var scenarios = new List<string>();
void Check(bool value, string label)
{
    if (!value) throw new Exception(label);
    checks++;
}
void Scenario(string name, Action body) { body(); scenarios.Add(name); }

Scenario("connected requests retain FIFO and one callback per dispatch", () =>
{
    var q = new TransportQueue(); var owner = q.Open(); var seen = new List<string>();
    var requests = new[] { q.Enqueue(owner, "resume"), q.Enqueue(owner, "step"), q.Enqueue(owner, "inspect") };
    for (int i = 0; i < 3; i++)
    {
        Check(q.DispatchNext(_ => throw new Exception("unexpected release"), e =>
        { seen.Add(e.json); e.result = e.json + "-response"; e.complete.Set(); }), "live dispatch present");
        Check(seen.Count == i + 1, "one live request dispatched");
        Check(requests[i].complete.WaitOne(0) && requests[i].result == requests[i].json + "-response", "response completion remains attached to its envelope");
    }
    Check(string.Join(",", seen) == "resume,step,inspect", "live FIFO preserved");
    Check(!q.DispatchNext(_ => { }, _ => { }), "empty queue has no dispatch");
    Check(q.Capture().requestsDispatched == 3, "live dispatch count exact");
});

Scenario("cancel queued movement before dispatch then inspect on new connection", () =>
{
    var q = new TransportQueue(); var dead = q.Open(); bool moving = false, paused = false;
    q.Enqueue(dead, "resume-neutral");
    q.DispatchNext(_ => { }, _ => { moving = false; paused = false; });
    q.Enqueue(dead, "moving-step"); q.Disconnect(dead);
    var live = q.Open(); q.Enqueue(live, "inspect");
    var events = new List<string>();
    Action<TransportConnection> release = c => { Check(c == dead, "release belongs to disconnected client"); moving = false; paused = true; events.Add("release"); };
    Action<Envelope> execute = e => { Check(e.Owner == live, "dead movement cannot execute"); Check(!moving && paused, "inspect follows neutral paused release"); events.Add(e.json); };
    Check(q.DispatchNext(release, execute), "disconnect dispatched");
    Check(q.DispatchNext(release, execute), "new inspect dispatched after cancelled request skipped");
    Check(string.Join(",", events) == "release,inspect", "release occurs before replacement connection input");
    var s = q.Capture();
    Check(s.requestsCancelledBeforeDispatch == 1 && s.lastCancelledConnectionId == dead.Id, "queued cancellation has owned proof");
    Check(s.requestsDispatched == 2 && s.pendingRequests == 0, "cancelled request was never dispatched");
});

Scenario("already detected disconnect rejects enqueue and emits release once", () =>
{
    var q = new TransportQueue(); var owner = q.Open(); q.Disconnect(owner); q.Disconnect(owner);
    bool rejected = false;
    try { q.Enqueue(owner, "resume"); } catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "enqueue after close rejected");
    int releases = 0;
    q.DispatchNext(_ => releases++, _ => throw new Exception("unexpected execute"));
    Check(!q.DispatchNext(_ => releases++, _ => { }) && releases == 1, "duplicate disconnect is idempotent");
});

Scenario("multiple disconnects preserve independent ownership", () =>
{
    var q = new TransportQueue(); var a = q.Open(); var b = q.Open(); var c = q.Open();
    q.Enqueue(a, "step-a"); q.Enqueue(b, "resume-b"); q.Disconnect(a); q.Disconnect(b); q.Enqueue(c, "step-c");
    var observed = new List<long>();
    for (int i = 0; i < 3; i++) q.DispatchNext(o => observed.Add(-o.Id), e => observed.Add(e.Owner.Id));
    Check(observed.SequenceEqual(new[] { -a.Id, -b.Id, c.Id }), "old releases never erase a newer dispatched command");
    Check(q.Capture().requestsCancelledBeforeDispatch == 2, "both dead requests cancelled");
});

Scenario("dispatch and socket cancellation cannot cross the ownership check", () =>
{
    var q = new TransportQueue(); var owner = q.Open(); q.Enqueue(owner, "step");
    using var entered = new ManualResetEventSlim(); using var cancellationStarted = new ManualResetEventSlim();
    using var cancellationFinished = new ManualResetEventSlim(); using var letDispatchFinish = new ManualResetEventSlim();
    var order = new List<string>();
    var dispatch = Task.Run(() => q.DispatchNext(_ => throw new Exception("early release"), e =>
    {
        entered.Set();
        if (!letDispatchFinish.Wait(3000)) throw new TimeoutException("test dispatch handshake");
        order.Add("already-dispatching-command");
    }));
    Check(entered.Wait(3000), "dispatch entered callback");
    var cancel = Task.Run(() => { cancellationStarted.Set(); q.Disconnect(owner); cancellationFinished.Set(); });
    Check(cancellationStarted.Wait(3000), "socket cancellation started");
    Check(!cancellationFinished.Wait(40), "cancellation cannot pass an in-progress ownership guard");
    letDispatchFinish.Set();
    Check(Task.WaitAll(new[] { dispatch, cancel }, 3000), "dispatch and cancellation complete");
    q.DispatchNext(_ => order.Add("release"), _ => throw new Exception("unexpected command"));
    Check(string.Join(",", order) == "already-dispatching-command,release", "in-progress command precedes its release");
    Check(q.Capture().requestsCancelledBeforeDispatch == 0, "executed request not relabeled queued cancellation");
});

Scenario("cancel worker wins before main-thread dispatch", () =>
{
    for (int i = 0; i < 100; i++)
    {
        var q = new TransportQueue(); var owner = q.Open(); q.Enqueue(owner, "step");
        Task.Run(() => q.Disconnect(owner)).GetAwaiter().GetResult();
        int releases = 0, executes = 0;
        while (q.DispatchNext(_ => releases++, _ => executes++)) { }
        Check(releases == 1 && executes == 0, "cancel-before-dispatch interleaving suppresses command");
    }
});

Scenario("callback failure releases the synchronization lock", () =>
{
    var q = new TransportQueue(); var owner = q.Open(); q.Enqueue(owner, "bad");
    try { q.DispatchNext(_ => { }, _ => throw new FormatException()); } catch (FormatException) { }
    var cancel = Task.Run(() => q.Disconnect(owner));
    Check(cancel.Wait(3000), "failed callback does not deadlock disconnect");
    int released = 0; q.DispatchNext(_ => released++, _ => { });
    Check(released == 1, "failure still permits owned release");
});

Console.WriteLine(JsonSerializer.Serialize(new { passed = true, checks, scenarios }));
