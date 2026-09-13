#!/usr/bin/env python3
"""Verify a closed native cannon-side probe trace; no game connection or control."""
import argparse
from collections import Counter
import gzip
import hashlib
import json
from pathlib import Path


def digest(path):
    h = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1048576), b""):
            h.update(block)
    return h.hexdigest()


def pinned(path):
    return {"path": str(path.resolve()), "sha256": digest(path), "bytes": path.stat().st_size}


def prove(trace, result, route, controller, output):
    if output.exists():
        raise ValueError("Proof already exists; choose a new output.")
    trace_pin, result_pin, route_pin = pinned(trace), pinned(result), pinned(route)
    recipe = json.loads(route.read_text(encoding="utf-8-sig"))
    final = json.loads(result.read_text(encoding="utf-8-sig"))
    final_state = final["state"]
    snapshots, observations, events, requests = [], [], [], Counter()
    movie_hash = hashlib.sha256()
    plugin_hashes = set()
    with gzip.open(trace, "rt", encoding="utf-8-sig") as source:
        for line in source:
            record = json.loads(line)
            if record["kind"] == "event":
                events.append(record)
                continue
            if record["kind"] != "call":
                continue
            request, response = record["request"], record["response"]
            assert response["ok"], "A protocol request failed."
            assert request["command"] in {"inspect", "step"} or (not requests and request["command"] == "restart"), "Probe body contains a command beyond initial restart, inspection or ordinary inputs."
            if request["command"] == "step":
                assert request.get("steps", 1) == 1
            requests[request["command"]] += 1
            movie_hash.update((json.dumps(request, ensure_ascii=False, sort_keys=True, separators=(",", ":"), allow_nan=False) + "\n").encode())
            state = response["state"]
            assert state["scene"] == "s_Day_3_4" and sorted(c["playerId"] for c in state["chefs"]) == [0, 1, 2, 3]
            plugin_hashes.add(state["instrumentation"]["manifest"]["pluginSha256"])
            chef = next(c for c in state["chefs"] if c["playerId"] == 2)
            cannon = next(e for e in state["entities"] if e["id"] == 84)
            observed = {"frame": state["frame"], "gameplayFrame": state["gameplayFrame"], "chefEntityId": chef["entityId"],
                        "position": chef["position"], "controlsEnabled": chef["controlsEnabled"],
                        "directlyControlled": chef["directlyControlled"], "respawning": chef["respawning"],
                        "heldEntityId": chef["heldEntityId"], "cannonState": cannon["cannonState"],
                        "cannonFlying": cannon["cannonFlying"], "cannonLoadedEntityId": cannon["cannonLoadedEntityId"]}
            snapshots.append(observed)
            if not observations or any(observed[k] != observations[-1][k] for k in ("cannonState", "cannonFlying", "cannonLoadedEntityId", "controlsEnabled")):
                observations.append(observed)
    assert events and not any(e["name"] in {"actionFailure", "plannerFailure"} for e in events)
    created = [e["value"] for e in events if e["name"] == "actionCreated"]
    expected = [{**a, "player": j["player"]} for j in recipe["jobs"] for a in j["actions"]]
    assert created == expected, "The current route differs from recorded action creation."
    adjusted = [e["value"] for e in events if e["name"] == "cannonSideApproachAdjusted"]
    assert len(adjusted) == 1 and adjusted[0]["player"] == 2 and adjusted[0]["sourceId"] == 84
    assert abs(adjusted[0]["projected"]["x"] - adjusted[0]["target"]["x"]) < 1e-8
    assert abs(adjusted[0]["projected"]["z"] - adjusted[0]["target"]["z"]) > .2
    boarded = next(s for s in snapshots if s["cannonState"] == "Load" and s["cannonLoadedEntityId"] == s["chefEntityId"] and not s["controlsEnabled"])
    flight = [s for s in snapshots if s["cannonFlying"] and s["cannonLoadedEntityId"] == s["chefEntityId"]]
    assert flight and boarded["frame"] < flight[0]["frame"]
    landed = next(s for s in snapshots if s["frame"] > flight[-1]["frame"] and not s["cannonFlying"] and s["controlsEnabled"] and s["directlyControlled"])
    assert landed["position"]["x"] > 25 and landed["position"]["z"] < -18 and not landed["respawning"]
    completed = [e["value"] for e in events if e["name"] == "transportComplete"]
    board_event = next(e for e in completed if e["evidence"] == "native cannon contains this chef")
    landing_event = next(e for e in completed if e["evidence"] == "native launch finished and passenger regained control on destination platform")
    assert landing_event["passengerId"] == landed["chefEntityId"] and landing_event["destinationRegion"] == "lower-right"
    assert board_event["frame"] >= boarded["frame"] and landing_event["frame"] >= landed["frame"]
    assert final["ok"] and final_state["gameplayFrame"] == snapshots[-1]["gameplayFrame"] == 188
    assert requests["step"] == 188 and sorted(set(s["gameplayFrame"] for s in snapshots)) == list(range(189)), "Native gameplay checkpoints are not complete."
    assert final_state["frame"] == snapshots[-1]["frame"]
    final_chef = next(c for c in final_state["chefs"] if c["playerId"] == 2)
    assert final_chef["position"] == snapshots[-1]["position"] and final_chef["controlsEnabled"] and not final_chef["respawning"]
    assert digest(trace) == trace_pin["sha256"] and digest(result) == result_pin["sha256"] and digest(route) == route_pin["sha256"], "Input artifacts changed during the audit."
    route_copy = output.with_name(output.stem.replace("-proof", "") + "-source.json")
    if route_copy.exists():
        assert route_copy.read_bytes() == route.read_bytes()
    else:
        route_copy.write_bytes(route.read_bytes())
    proof = {"format": "oc2-native-cannon-side-proof", "version": 1, "passed": True,
             "scope": "One native ordinary-input boarding and flight probe after a blocked horizontal side projection; not a full-round or score qualification.",
             "sourceTrace": trace_pin, "result": result_pin, "currentRoute": route_pin, "preservedRoute": pinned(route_copy),
             "controller": pinned(controller), "controllerBundle": [pinned(p) for p in sorted(controller.parent.iterdir()) if p.is_file()],
             "controllerProvenance": "Executing parent identifies this immutable bundle as the command used for the native probe; trace header records version only, not its binary hash.",
             "nativeEmbeddedPluginSha256": sorted(plugin_hashes), "requestCounts": dict(requests),
             "canonicalOriginalRequestSha256": movie_hash.hexdigest(), "sampleCount": len(snapshots),
             "adjustedApproach": adjusted[0], "nativeBoarded": boarded,
             "nativeFlightGameplayFrames": sorted(set(s["gameplayFrame"] for s in flight)),
             "nativeLanding": landed, "transportCompletionObservations": [board_event, landing_event],
             "nativeStateTransitions": observations, "finalGameplayFrame": final_state["gameplayFrame"],
             "finalChefPosition": final_chef["position"], "score": final_state["score"], "delivered": final_state["delivered"],
             "earlierFailureContext": "Attempt a used a tighter floor-edge navigation target/tolerance and failed before this boarding check; its source remains separately preserved. The original GF6020/11.3 regression is covered by the separate captured-state 11-assertion test."}
    output.write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"passed": True, "proof": str(output), "boardedGameplayFrame": boarded["gameplayFrame"],
                      "flight": [flight[0]["gameplayFrame"], flight[-1]["gameplayFrame"]],
                      "landedGameplayFrame": landed["gameplayFrame"], "finalGameplayFrame": final_state["gameplayFrame"],
                      "controllerSha256": proof["controller"]["sha256"]}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for field in ("trace", "result", "route", "controller", "output"):
        parser.add_argument("--" + field, type=Path, required=True)
    args = parser.parse_args()
    prove(args.trace, args.result, args.route, args.controller, args.output)
