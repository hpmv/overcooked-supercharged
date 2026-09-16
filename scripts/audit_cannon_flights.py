"""Read-only audit of production cannon arrival obligations, including active trace prefixes."""
import argparse
import collections
import gzip
import hashlib
import json
import math
from pathlib import Path


def neutral(value):
    return value is not None and value.get("x") == 0 and value.get("y") == 0 and all(value.get(k) is False for k in ("pickup", "use", "dash"))


def audit(path):
    initial_stat = path.stat()
    active, complete, history, errors = {}, [], collections.deque(maxlen=4), []
    calls, last_frame, truncated = 0, None, False
    plugin_hashes = set()

    def check(value, message):
        if not value:
            errors.append({"frame": last_frame, "error": message})

    def arrival(row, flight):
        cannon = row["entities"].get(flight["cannon"], {})
        chef = row["chefs"].get(flight["passengerPlayer"], {})
        p, target = chef.get("position", {}), flight["landingTarget"]
        return (cannon.get("cannonFlying") is False and all(chef.get(k) is True for k in ("controlsEnabled", "directlyControlled", "canAcceptInput"))
                and chef.get("inputSuppressed") is False and chef.get("respawning") is False
                and p.get("y", math.inf) <= .9 and math.hypot(p.get("x", math.inf) - target["x"], p.get("z", math.inf) - target["z"]) <= flight["landingRadius"])

    try:
        with gzip.open(path, "rt", encoding="utf-8-sig") as reader:
            for line in reader:
                try:
                    record = json.loads(line)
                except json.JSONDecodeError:
                    truncated = True
                    break
                if record.get("kind") == "call":
                    response = record.get("response", {})
                    state = response.get("state", {})
                    if not state.get("levelReady"):
                        continue
                    calls += 1
                    last_frame = state["gameplayFrame"]
                    entities = {e["id"]: e for e in state.get("entities", [])}
                    row = {"frame": last_frame, "time": state.get("clientTime"),
                           "chefs": {c["playerId"]: c for c in state["chefs"]},
                           "entities": {k: {f: e.get(f) for f in ("id", "observedOrdinal", "cannonFlying", "cannonState")} for k, e in entities.items()},
                           "inputs": {i["player"]: i for i in response.get("inputs", [])}}
                    plugin_hashes.add(state.get("instrumentation", {}).get("manifest", {}).get("pluginSha256"))
                    for cannon, flight in active.items():
                        p = flight["passengerPlayer"]
                        chef = row["chefs"].get(p, {})
                        check(chef.get("entityId") == flight["passengerEntity"] and chef.get("heldEntityId") == flight["heldEntity"], "exact passenger or carried item changed while arrival obligation existed")
                        check(neutral(row["inputs"].get(p)), "passenger input was not neutral while arrival obligation existed")
                        for entity, ordinal in flight["identities"].items():
                            check(row["entities"].get(entity, {}).get("observedOrdinal") == ordinal, "native entity identity changed during cannon obligation")
                        if flight.get("releasedFrame") is not None:
                            flight["observedFlightFrames"] += int(row["entities"].get(cannon, {}).get("cannonFlying") is True)
                            f = flight["firingPlayer"]
                            if history and row["entities"].get(cannon, {}).get("cannonFlying") is True and history[-1]["entities"].get(cannon, {}).get("cannonFlying") is True:
                                current = row["chefs"][f]["position"]
                                previous = history[-1]["chefs"][f]["position"]
                                if math.hypot(current["x"] - previous["x"], current["z"] - previous["z"]) > .005:
                                    flight["firerMovingDuringFlightFrames"].append(last_frame)
                    history.append(row)
                elif record.get("kind") == "event":
                    name, value = record.get("name"), record.get("value", {})
                    if name not in ("cannonArrivalReserved", "cannonFiringChefReleased", "cannonPassengerArrivalConfirmed"):
                        continue
                    cannon = value["cannon"]
                    check(history and history[-1]["frame"] == value["frame"], "production event is not bound to its last completed native frame")
                    if not history:
                        continue
                    row = history[-1]
                    if name == "cannonArrivalReserved":
                        check(cannon not in active, "same cannon acquired twice before arrival")
                        flight = dict(value)
                        flight["reservedFrame"] = value["frame"]
                        flight["releasedFrame"] = None
                        flight["observedFlightFrames"] = 0
                        flight["firerMovingDuringFlightFrames"] = []
                        resources = [cannon, value["passengerEntity"]] + ([value["heldEntity"]] if value["heldEntity"] else [])
                        check(set(value["ownedResources"]) == set(resources) and value["button"] not in resources, "persistent resources do not match cannon/passenger/carried item")
                        flight["identities"] = {entity: row["entities"].get(entity, {}).get("observedOrdinal") for entity in resources}
                        check(all(o is not None for o in flight["identities"].values()), "flight identity telemetry is absent")
                        active[cannon] = flight
                    elif cannon not in active:
                        check(False, "release/arrival lacks a preceding production reservation")
                    elif name == "cannonFiringChefReleased":
                        flight = active[cannon]
                        receipt = value.get("launchReceipt") or {}
                        check(flight["releasedFrame"] is None and receipt.get("nativeFlying") is True, "release duplicated or lacks native flying receipt")
                        check(receipt.get("passenger") == flight["passengerEntity"] and receipt.get("held") == flight["heldEntity"], "launch receipt does not match reserved passenger/item")
                        launched = next((h for h in history if h["frame"] == receipt.get("launchFrame")), None)
                        check(launched is not None and launched["entities"].get(cannon, {}).get("cannonFlying") is True and launched["entities"].get(cannon, {}).get("cannonState") == "Launched", "launch receipt lacks the matching actual native flying frame")
                        check(neutral(row["inputs"].get(value["firingPlayer"])), "firing chef was released without a completed neutral input")
                        check(set(value["ownedResources"]) == set(flight["ownedResources"]), "persistent ownership changed at firer release")
                        flight["releasedFrame"] = value["frame"]
                        flight["launchFrame"] = receipt.get("launchFrame")
                    else:
                        flight = active[cannon]
                        check(flight["releasedFrame"] is not None and value["stableArrivalSamples"] >= 2, "arrival precedes launch release or lacks two samples")
                        check(len(history) >= 2 and history[-2]["frame"] < row["frame"] and arrival(history[-2], flight) and arrival(row, flight), "arrival event is not supported by two advancing native controlled landing samples")
                        check(flight.get("launchFrame") is not None and 0 < value["frame"] - flight["launchFrame"] <= 120, "arrival exceeds its bounded native flight window")
                        flight["arrivalFrame"] = value["frame"]
                        flight["earlyReleaseFrames"] = value["frame"] - flight["releasedFrame"] if flight["releasedFrame"] is not None else None
                        complete.append(flight)
                        del active[cannon]
    except (EOFError, OSError):
        truncated = True
    final_stat = path.stat()
    unchanged = (initial_stat.st_size, initial_stat.st_mtime_ns) == (final_stat.st_size, final_stat.st_mtime_ns)
    closed = not truncated and unchanged
    source_hash = None
    if closed:
        with path.open("rb") as exact_source:
            source_hash = hashlib.file_digest(exact_source, "sha256").hexdigest()
    return {"ok": not errors, "scope": "closed trace" if closed else "read-only completed-record prefix of an active trace",
            "trace": str(path.resolve()), "traceSha256": source_hash, "calls": calls, "lastGameplayFrame": last_frame,
            "pluginSha256": sorted(h for h in plugin_hashes if h), "completedFlights": complete,
            "pendingFlights": list(active.values()), "errors": errors,
            "classification": "Production cannon lease/native-input observations; full-score and exact replay qualification are separate"}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--trace", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--require-complete", action="store_true", help="Reject active prefixes or unresolved arrival obligations.")
    args = parser.parse_args()
    if args.trace.resolve() == args.out.resolve():
        parser.error("Output must not overwrite the source trace.")
    report = audit(args.trace)
    if args.require_complete and (report["scope"] != "closed trace" or report["pendingFlights"] or not report["completedFlights"]):
        report["ok"] = False
        report["errors"].append({"error": "Complete production proof requires a closed unchanged trace and at least one flight with no pending obligations."})
    args.out.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({"ok": report["ok"], "scope": report["scope"], "lastGameplayFrame": report["lastGameplayFrame"],
                      "complete": len(report["completedFlights"]), "pending": len(report["pendingFlights"]), "errors": report["errors"][:5]}))
    raise SystemExit(0 if report["ok"] else 1)
