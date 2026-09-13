"""One locked, complete read of the closed V21 trace; no game connection."""
import gzip
import hashlib
import json
import re
from pathlib import Path
from archive_closed_gate_logs import locked_read

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "artifacts/native-round-v21/trial001.jsonl.gz"
OUT = ROOT / "artifacts/v21-feature-lifecycles-v2"
FRAME = re.compile(rb'"gameplayFrame"\s*:\s*(-?\d+)')
DECODER = json.JSONDecoder()


def core(s, ids):
    wanted = set(ids)
    latest = {}
    for ev in s.get("entityRegistration", {}).get("events", []):
        i = (ev.get("entity") or {}).get("entityId")
        if i in wanted and ev.get("sequence", -1) > latest.get(i, {}).get("sequence", -1):
            latest[i] = ev
    fields = ("gameplayFrame", "frame", "timer", "score", "delivered", "deductions", "physicsStepsThisFrame")
    return {**{k: s.get(k) for k in fields}, "chefs": s["chefs"],
            "entities": [e for e in s["entities"] if e["id"] in wanted], "latestRegistry": latest,
            "entityRegistrationInstalled": s.get("entityRegistration", {}).get("installed"),
            "entityRegistrationErrorCount": s.get("entityRegistration", {}).get("errorCount")}


def raw_bowl(line, entity_id):
    start = line.find(b'"entities":[')
    pattern = ('{"id":' + str(entity_id) + ',"layer":').encode()
    start = line.find(pattern, start)
    if start < 0:
        return None
    # Native serializer emits each entity with this fixed prefix. Decode a
    # single complete JSON object, and validate its ID before retaining it.
    e, _ = DECODER.raw_decode(line[start:].decode("utf-8"))
    assert e["id"] == entity_id
    return {k: e.get(k) for k in ("id", "observedOrdinal", "position", "rotation", "active", "contents", "composition", "mixingProgress", "mixingTime")}


def food_signature(e):
    def compact(node):
        if not isinstance(node, dict):
            return node
        return {k: [compact(c) for c in v] if isinstance(v, list) else compact(v)
                for k, v in node.items() if k not in ("progress", "normalisedProgress")}
    return compact(e.get("contents")), compact(e.get("composition"))


def main():
    OUT.mkdir(exist_ok=False)
    compressed, plain = hashlib.sha256(), hashlib.sha256()
    last_line = None
    last_frame = -1
    last_state = None
    first_state = None
    pending_fifo = []
    active = {}
    counts = {}
    events, captures, swaps, bowl_changes = [], [], [], []
    prior_bowls = {}
    lines = calls = native_samples = 0

    class Reader:
        def __init__(self, f): self.f = f
        def read(self, n=-1):
            data = self.f.read(n); compressed.update(data); return data

    def parse_last():
        nonlocal last_state
        if last_state is None:
            last_state = json.loads(last_line)["response"]["state"]
        return last_state

    def capture(name, value):
        state = parse_last()
        target = OUT / (str(len(captures)).zfill(3) + "-" + name + "-gf" + str(last_frame) + ".json")
        # Exact original complete call bytes, not a reconstructed state.
        target.write_bytes(last_line)
        captures.append({"event": name, "frame": last_frame, "path": str(target),
                         "sha256": hashlib.sha256(last_line).hexdigest(), "value": value})
        return state

    with locked_read(SOURCE) as file, gzip.GzipFile(fileobj=Reader(file)) as stream, \
            gzip.open(OUT / "plated-native.jsonl.gz", "wt", encoding="utf-8") as native:
        for line in stream:
            plain.update(line); lines += 1
            prefix = line[:100].replace(b" ", b"")
            if b'"kind":"event"' in prefix:
                event = json.loads(line)
                name, value = event.get("name", ""), event.get("value") or {}
                if name.startswith("nativePlatedOnion") or name == "fifoEmptyBowlsReassigned":
                    counts[name] = counts.get(name, 0) + 1
                    events.append({"frame": last_frame, "name": name, "value": value})
                    s = capture(name, value)
                    if name == "nativePlatedOnionAdmitted":
                        key = value["mealIndex"]
                        active[key] = value
                        ids = list(value["identities"]) + [value["pan"], value["home"], value["unusedParkingCounter"]]
                        native.write(json.dumps({"mealIndex": key, "state": core(s, map(int, ids))}, separators=(",", ":")) + "\n")
                        native_samples += 1
                    elif name == "nativePlatedOnionComplete":
                        active.pop(value["mealIndex"])
                    elif name == "fifoEmptyBowlsReassigned":
                        swap = {"event": value, "before": core(s, [3, 6, 14, 18, 24, 48]), "after": None}
                        swaps.append(swap); pending_fifo.append(swap)
                elif name in ("plannerJobStart", "plannerJobComplete", "plannerJobPaused", "plannerJobResumed", "plannerResourceReleased", "nativePreparedFlavorSupplyStarted", "nativePreparedFlavorSupplyComplete", "plannerFailure", "actionFailure") or name.startswith(("earlyOnion", "nativeSupply", "counterSupply")):
                    events.append({"frame": last_frame, "name": name, "value": value})
                continue
            if b'"kind":"call"' not in prefix:
                continue
            match = FRAME.search(line)
            if match is None:
                continue
            calls += 1
            last_frame = int(match.group(1)); last_line = line; last_state = None
            if first_state is None:
                first_state = core(parse_last(), [3, 6, 14, 18])
            if pending_fifo:
                s = parse_last()
                for swap in pending_fifo:
                    swap["after"] = core(s, [3, 6, 14, 18, 24, 48])
                    swap["nextRequest"] = json.loads(line)["request"]
                pending_fifo.clear()
            if active:
                s = parse_last()
                for index, plan in active.items():
                    ids = list(plan["identities"]) + [plan["pan"], plan["home"], plan["unusedParkingCounter"]]
                    native.write(json.dumps({"mealIndex": index, "state": core(s, map(int, ids))}, separators=(",", ":")) + "\n")
                    native_samples += 1
            for bowl in (3, 6):
                e = raw_bowl(line, bowl)
                if e is None:
                    continue
                signature = food_signature(e)
                if prior_bowls.get(bowl) != signature:
                    prior_bowls[bowl] = signature
                    # Cross-check the fast object projection against the full
                    # native JSON parse at every retained transition.
                    full = next(x for x in parse_last()["entities"] if x["id"] == bowl)
                    assert all(e[k] == full.get(k) for k in e)
                    bowl_changes.append({"frame": last_frame, "bowl": e})
            if calls % 2000 == 0:
                print(json.dumps({"through": last_frame, "platedAdmissions": counts.get("nativePlatedOnionAdmitted", 0), "fifoSwaps": len(swaps)}), flush=True)
    summary = json.loads((ROOT / "artifacts/native-round-v21/summary.json").read_text())
    trial = summary["results"][0]
    assert compressed.hexdigest() == trial["traceSha256"]
    report = {"source": str(SOURCE), "traceSha256": compressed.hexdigest(), "uncompressedSha256": plain.hexdigest(),
              "completeGzipRead": True, "lines": lines, "calls": calls, "lastFrame": last_frame,
              "nativeSamples": native_samples, "counts": counts, "activeUnfinished": list(active),
              "first": first_state, "last": core(parse_last(), [3, 6, 14, 18]), "swaps": swaps,
              "captures": captures, "events": events, "bowlChanges": bowl_changes,
              "controllerSha256": summary["controllerSha256"], "scriptSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
    (OUT / "extraction.json").write_text(json.dumps(report, separators=(",", ":")), encoding="utf-8")
    print(json.dumps({"ok": True, "counts": counts, "nativeSamples": native_samples, "lastFrame": last_frame, "out": str(OUT)}), flush=True)


if __name__ == "__main__": main()
