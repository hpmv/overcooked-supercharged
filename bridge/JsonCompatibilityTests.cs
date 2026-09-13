using System;
using System.Collections;
using System.Collections.Generic;

namespace SuperchargedPatch.Bridge
{
    // CLR-only checks; can be compiled with the exact bridge sources outside Unity.
    public static class JsonCompatibilityTests
    {
        public static int Run()
        {
            int checks = 0;
            var request = JsonRead.Object("{\"version\":1,\"command\":\"restart\",\"seed\":-7}");
            Check(JsonRead.Integer(request, "version", 0) == 1, ref checks);
            Check(JsonRead.Integer(request, "seed", 0) == -7, ref checks);
            Check(JsonRead.Integer(request, "absent", 9) == 9, ref checks);
            Check(JsonRead.Text(request, "command", null) == "restart", ref checks);
            var data = new Dictionary<string, object>();
            data.Add("request", request);
            data.Add("chefs", new object[] { new Chef(0), new Chef(1), new Chef(2), new Chef(3) });
            data.Add("text", "quote\" slash\\ newline\n tab\t \u263a");
            data.Add("null", null);
            data.Add("fraction", 1.25f);
            data.Add("finite", float.NaN);
            var decoded = JsonRead.Object(JsonText.Serialize(data));
            Check(decoded["request"] is Dictionary<string, object>, ref checks);
            Check(decoded["chefs"] is ArrayList, ref checks);
            var chefs = (ArrayList)decoded["chefs"];
            Check(chefs.Count == 4, ref checks);
            for (int i = 0; i < chefs.Count; i++)
            {
                var chef = (Dictionary<string, object>)chefs[i];
                Check(JsonRead.Integer(chef, "player", -1) == i, ref checks);
                Check((bool)chef["local"], ref checks);
                Check(chef["unknown"] == null, ref checks);
                Check(!chef.ContainsKey("GetterMustNeverRun"), ref checks);
            }
            Check((string)decoded["text"] == (string)data["text"], ref checks);
            Check(decoded["null"] == null && decoded["finite"] == null, ref checks);
            Check(((JsonRead.Number)decoded["fraction"]).Value == 1.25, ref checks);
            Check(JsonText.Serialize(JsonRead.Object("{\"integer\":1,\"float\":1.0,\"bool\":true}")) ==
                "{\"integer\":1,\"float\":1.0,\"bool\":true}", ref checks);
            string[] invalid = { "[]", "{\"x\":1,\"x\":2}", "{\"x\":01}", "{\"x\":1e400}", "{\"x\":1,}",
                "{\"x\":true} trailing", "{\"x\":\"\n\"}", "{\"x\":NaN}", "{\"x\":", "{\"x\":[1,]}" };
            foreach (string value in invalid) Reject(delegate { JsonRead.Object(value); }, ref checks);
            Reject(delegate { JsonRead.Integer(JsonRead.Object("{\"version\":true}"), "version", 1); }, ref checks);
            Reject(delegate { JsonRead.Integer(JsonRead.Object("{\"seed\":1.0}"), "seed", 0); }, ref checks);
            Reject(delegate { JsonRead.Integer(JsonRead.Object("{\"seed\":2147483648}"), "seed", 0); }, ref checks);
            Reject(delegate { JsonRead.Text(JsonRead.Object("{\"command\":7}"), "command", null); }, ref checks);
            Reject(delegate { JsonText.Serialize(new Hashtable { { 2, "invalid object key" } }); }, ref checks);
            var queue = new TransportQueue();
            var dead = queue.Open(); var cancelled = queue.Enqueue(dead, "old moving command"); queue.Disconnect(dead);
            var live = queue.Open(); var accepted = queue.Enqueue(live, "new neutral command");
            int releases = 0, dispatches = 0;
            queue.DispatchNext(delegate(TransportConnection owner) { if (owner == dead) releases++; },
                delegate(Envelope envelope) { dispatches++; });
            Check(releases == 1 && dispatches == 0, ref checks);
            queue.DispatchNext(delegate(TransportConnection owner) { releases++; },
                delegate(Envelope envelope) { Check(object.ReferenceEquals(envelope, accepted), ref checks); dispatches++; });
            Check(dispatches == 1 && queue.Capture().requestsCancelledBeforeDispatch == 1, ref checks);
            Check(queue.Capture().lastDispatchedConnectionId == live.Id, ref checks);
            cancelled.complete.Close(); accepted.complete.Close();
            return checks;
        }

        private sealed class Chef
        {
            public int player;
            public bool local = true;
            public float? unknown = null;
            public string GetterMustNeverRun { get { throw new Exception("The wire format is public fields only."); } }
            public Chef(int id) { player = id; }
        }
        private static void Check(bool result, ref int count)
        {
            if (!result) throw new Exception("Bridge JSON/transport check failed at " + count);
            count++;
        }
        private static void Reject(Action action, ref int count)
        {
            bool rejected = false;
            try { action(); } catch (FormatException) { rejected = true; }
            catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, ref count);
        }
    }
}
