"""Native queued-disconnect regression probe. Never launches or rebuilds the game.

Run only in a disposable paused kitchen with the new transport diagnostics:
  python scripts/probe_disconnect.py --out artifacts/disconnect-native.json
The probe advances a few neutral native frames. It preserves the original render
settings and leaves all inputs neutral and the game paused, including on failure.
"""
import argparse
import hashlib
import json
from pathlib import Path
import socket
import struct
import time


ROOT = Path(__file__).resolve().parents[1]


def neutral(response):
    pads = response.get("inputs", [])
    return (len(pads) == 4 and sorted(p.get("player") for p in pads) == [0, 1, 2, 3]
            and all(p.get("x") == 0 and p.get("y") == 0
                    and all(p.get(k) is False for k in ("pickup", "use", "dash")) for p in pads))


def decode_exact(sock, count):
    data = bytearray()
    while len(data) < count:
        chunk = sock.recv(count - len(data))
        if not chunk:
            raise ConnectionError("Server closed before a complete response")
        data.extend(chunk)
    return data


class Client:
    def __init__(self, port, timeout, records):
        self.sock = socket.create_connection(("127.0.0.1", port), timeout)
        self.sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        self.sock.settimeout(timeout)
        self.records = records

    def send(self, request):
        payload = json.dumps(request, separators=(",", ":"), allow_nan=False).encode("utf-8")
        self.sock.sendall(struct.pack("<I", len(payload)) + payload)

    def call(self, command, **fields):
        request = {"version": 1, "command": command, **fields}
        self.send(request)
        count, = struct.unpack("<I", decode_exact(self.sock, 4))
        if not 2 <= count <= 16 * 1024 * 1024:
            raise ValueError("Invalid native response size")
        response = json.loads(decode_exact(self.sock, count))
        self.records.append({"request": request, "response": response, "wallMonotonic": time.monotonic()})
        if response.get("ok") is not True:
            raise RuntimeError(response.get("error", "Native call failed"))
        return response

    def close_after_send(self, request):
        self.send(request)
        self.records.append({"request": request, "sentWithoutAwaitingResponse": True, "wallMonotonic": time.monotonic()})
        # FIN follows every request byte. No zero-length or partial command is
        # used to simulate a queued request that the server never actually read.
        self.sock.shutdown(socket.SHUT_WR)
        self.sock.close()

    def close(self):
        self.sock.close()


def verify_attempt(before, after):
    """Pure assertions shared by offline fixtures and the native probe."""
    a, b = before.get("transport", {}), after.get("transport", {})
    required = ("requestsCancelledBeforeDispatch", "activeConnectionId", "requestsDispatched")
    if any(k not in a or k not in b for k in required):
        raise AssertionError("Runtime lacks required connection/cancellation diagnostics")
    cancelled = b["requestsCancelledBeforeDispatch"] - a["requestsCancelledBeforeDispatch"]
    if cancelled != 1:
        raise AssertionError("Queued cancellation not proved: expected +1, observed %s" % cancelled)
    if b.get("lastCancelledConnectionId") != a["activeConnectionId"]:
        raise AssertionError("Cancelled request does not belong to the disconnected connection")
    if b["activeConnectionId"] == a["activeConnectionId"] or b["activeConnectionId"] <= 0:
        raise AssertionError("Inspect did not run on a new connection")
    # The only command after the acknowledged neutral resume is reconnect inspect.
    if b["requestsDispatched"] - a["requestsDispatched"] != 1:
        raise AssertionError("An unexpected command dispatched between resume and reconnect inspect")
    if not neutral(after) or after.get("paused") is not True:
        raise AssertionError("Reconnect observed active input or an unpaused game")
    previous = max((e.get("index", -1) for e in before.get("state", {}).get("gameEvents", [])), default=-1)
    releases = [e for e in after.get("state", {}).get("gameEvents", [])
                if e.get("index", -1) > previous and e.get("kind") == "input_release"
                and e.get("reason") == "controller-disconnected" and e.get("inputsNeutral") is True]
    if len(releases) != 1:
        raise AssertionError("Missing unique native neutral input-release observation")
    return {"cancelledQueuedRequests": cancelled, "oldConnectionId": a["activeConnectionId"],
            "newConnectionId": b["activeConnectionId"], "releaseEventIndex": releases[0]["index"],
            "nativeFramesAdvanced": after["frame"] - before["frame"], "passed": True}


def run(args):
    output = Path(args.out).resolve()
    if not output.is_relative_to((ROOT / "artifacts").resolve()):
        raise ValueError("Probe output must be inside workspace artifacts")
    output.parent.mkdir(parents=True, exist_ok=True)
    # Exclusive creation protects earlier evidence even when preflight fails.
    with output.open("x", encoding="utf-8") as destination:
        report = {"format": "oc2-native-queued-disconnect-probe", "version": 1,
                  "passed": False, "attempts": [], "calls": [], "cleanup": {}}
        client = None
        original = None
        plugin = ROOT / "runtime/BepInEx/plugins/Oc2Tas.dll"
        digest = lambda: hashlib.sha256(plugin.read_bytes()).hexdigest()
        try:
            report["pluginSha256"] = digest()
            client = Client(args.port, args.timeout, report["calls"])
            initial = client.call("inspect")
            if initial.get("paused") is not True or not neutral(initial) or initial.get("state", {}).get("gameState") != "InLevel":
                raise ValueError("Probe requires an already paused native kitchen with four neutral pads")
            if "requestsCancelledBeforeDispatch" not in initial.get("transport", {}):
                raise ValueError("Deploy the new source build before running this probe")
            state = initial["state"]
            if state.get("vSyncCount") != 0:
                raise ValueError("Probe requires vSync disabled; this protocol cannot restore a nonzero vSync setting")
            rate = state["targetFrameRate"]
            original = {"width": state["screenWidth"], "height": state["screenHeight"], "renderRate": 0 if rate == -1 else rate}
            if not (640 <= original["width"] <= 3840 and 360 <= original["height"] <= 2160 and 0 <= original["renderRate"] <= 240):
                raise ValueError("Original render settings cannot be restored through this protocol")
            client.call("render", **{**original, "renderRate": 1})
            for attempt in range(args.attempts):
                before = client.call("resume", inputs=[])
                if not neutral(before):
                    raise AssertionError("Neutral resume was not neutral")
                # After a long pause, Unity may complete its first resumed frame
                # immediately despite targetFrameRate=1. That lets an immediate
                # request dispatch before ServerLoop's 100ms disconnect poll.
                # Let that neutral frame pass, then queue inside the subsequent
                # one-second render interval. The counters below, not this
                # timing assumption, determine whether the queued case occurred.
                time.sleep(args.queue_delay)
                report["calls"].append({"neutralWallDelaySeconds": args.queue_delay,
                                        "wallMonotonic": time.monotonic()})
                request = {"version": 1, "command": "step", "steps": 60000,
                           "inputs": [{"player": args.player, "x": 1, "y": 0, "pickup": False, "use": False, "dash": False}]}
                client.close_after_send(request)
                client = None
                client = Client(args.port, args.timeout, report["calls"])
                after = client.call("inspect")
                proof = verify_attempt(before, after)
                report["attempts"].append({"attempt": attempt + 1, **proof})
            if digest() != report["pluginSha256"]:
                raise AssertionError("Runtime plugin bytes changed during the probe")
            report["passed"] = True
        except Exception as error:
            report["error"] = "%s: %s" % (type(error).__name__, error)
            # A failed read may have left a response partially consumed. Reopen
            # before cleanup instead of treating that stream as synchronized.
            if client is not None:
                client.close()
                client = None
        finally:
            try:
                if original is not None:
                    if client is None:
                        client = Client(args.port, args.timeout, report["calls"])
                    paused = client.call("pause")
                    restored = client.call("render", **original)
                    report["cleanup"] = {"originalRenderSettings": original,
                                         "restoreAcknowledged": restored.get("ok") is True,
                                         "paused": paused.get("paused") is True, "inputsNeutral": neutral(paused)}
                    if not report["cleanup"]["paused"] or not report["cleanup"]["inputsNeutral"]:
                        raise AssertionError("Cleanup failed to pause with neutral inputs")
                    expected_rate = -1 if original["renderRate"] == 0 else original["renderRate"]
                    if restored["state"].get("targetFrameRate") != expected_rate:
                        raise AssertionError("Original render cap was not restored")
            except Exception as error:
                report["passed"] = False
                report["cleanup"]["error"] = "%s: %s" % (type(error).__name__, error)
            if client is not None:
                client.close()
            json.dump(report, destination, indent=2, allow_nan=False)
            destination.write("\n")
        print(json.dumps({k: v for k, v in report.items() if k != "calls"}))
        return 0 if report["passed"] else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True)
    parser.add_argument("--port", type=int, default=17634)
    parser.add_argument("--timeout", type=float, default=12)
    parser.add_argument("--attempts", type=int, choices=range(1, 6), default=3)
    parser.add_argument("--player", type=int, choices=range(4), default=0)
    parser.add_argument("--queue-delay", type=float, default=.2,
                        help="Neutral wall seconds after resume before step+FIN (default .2)")
    args = parser.parse_args()
    if not 0 <= args.queue_delay <= .5:
        parser.error("--queue-delay must be between 0 and .5 seconds")
    raise SystemExit(run(args))
