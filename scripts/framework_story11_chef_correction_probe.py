"""Paired Story11 idle rewind diagnostics using existing read-only Inspection.

Four-chef input is neutral; world restoration is the explicitly requested
authoring warp, optional diagnostic preconditioning warps, and any explicitly
loaded checkpoint sidecars. Every paired boundary uses the same
pause/fence/inspection procedure. This measures those interrupted
continuations, not an uninterrupted run or full parity.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import time

from compare_framework_frames import first_difference
from framework_native_search import exact_values, gameplay_round, native_clock_state, require_native_boundary, require_paused
from framework_pause_boundary import observe_settled_pause
from framework_rpc import Client, ControllerClient


NS = "Team17.Online.Multiplayer.Messaging."
# Actual local-host chefs use ClientOnTheServerChefSynchroniser, which inherits
# ClientWorldObjectSynchroniser directly. Remote ClientChef correction fields
# do not exist on that object and must not be guessed into an inspection.
SPECS = (
    ("ClientOnTheServerChefSynchroniser", NS + "ClientWorldObjectSynchroniser", "field",
     ("m_Transform", "m_Parent", "m_ParentEntityID", "m_bHasParent", "m_ServerPosition",
      "m_Lerper", "m_bPaused", "m_PendingResumeData", "m_bHasEverReceived")),
    ("ServerChefSynchroniser", NS + "ServerWorldObjectSynchroniser", "field",
     ("m_Transform", "m_ServerData", "m_CachedParentTransform", "m_LastUnreliableActiveSend",
      "m_bSentReliableRestPosition", "m_bStartedSynchronising", "m_bSleepAllowed", "m_bActive",
      "m_bSyncPositions", "m_bParentChanged", "m_bPaused")),
    ("GroundCast", "GroundCast", "field",
     ("m_groundCollider", "m_groundPoint", "m_groundNormal", "m_groundDistance", "m_isCurrent",
      "m_collider", "m_Transform", "m_radius", "m_offset", "m_landscapeMask")),
    ("SurfaceMovable", "SurfaceMovable", "field", ("m_surfaceVelocity", "m_surface", "m_prevSurface")),
    ("CapsuleCollider", "UnityEngine.CapsuleCollider", "property", ("center", "radius", "height", "direction")),
    ("Rigidbody", "UnityEngine.Rigidbody", "property",
     ("position", "rotation", "velocity", "angularVelocity", "isKinematic", "useGravity",
      "centerOfMass", "worldCenterOfMass", "inertiaTensor", "inertiaTensorRotation", "mass", "constraints")),
    ("ClientPlayerControlsImpl_Default", "ClientPlayerControlsImpl_Default", "field",
     ("m_lastVelocity", "m_LeftOverTime", "m_timeOffGround", "m_isFalling", "m_movementInputSuppressed")),
    ("PlayerControls", "PlayerControls", "field", ("m_groundCast", "m_currentPhysicsSurface", "m_bApplyGravity")),
)


def inspection_operations(chefs, pins=None):
    if not chefs or len(set(chefs)) != len(chefs) or not set(chefs) <= {43, 44, 45, 46}:
        raise ValueError("Select unique observed Story11 chefs43..46")
    result = []
    for chef in chefs:
        for component, declaring, kind, names in SPECS:
            args = {"entityId": chef, "component": component,
                    "members": [{"kind": kind, "name": n, "declaringType": declaring} for n in names]}
            if component == "CapsuleCollider":
                args["members"] += [{"kind": "property", "name": n, "declaringType": "UnityEngine.Collider"}
                                    for n in ("contactOffset", "enabled", "isTrigger", "bounds", "attachedRigidbody", "sharedMaterial")]
            if component == "Rigidbody":
                args["members"] += [{"kind": "property", "name": n, "declaringType": "UnityEngine.Rigidbody"}
                                    for n in ("maxDepenetrationVelocity", "solverIterations", "solverVelocityIterations", "sleepThreshold")]
            if pins is not None:
                obj, part = pins[(chef, component)]
                args.update(expectedObjectId=obj, expectedComponentId=part)
            result.append({"operation": "inspect", "args": args})
    if len(result) > 32:
        raise ValueError("Inspection batch bound exceeded")
    return result


def inspection_result(response, operations):
    result = response["detail"]["result"]
    rows = result.get("operations", [])
    if result.get("ok") is not True or len(rows) != len(operations):
        raise RuntimeError("Incomplete or rejected read-only inspection batch")
    pins, values = {}, {}
    for row, op in zip(rows, operations):
        args = op["args"]
        if row.get("ok") is not True or row.get("mutationAttempted") is not False or row["entityId"] != args["entityId"]:
            raise RuntimeError("Inspection was not an exact successful read-only operation")
        expected = {(s["declaringType"], s["name"]) for s in args["members"]}
        observed = row["after"]["values"]
        # Native Inspector describes members as declaringType/name/kind/value.
        found = {(v["declaringType"], v["name"]) for v in observed}
        if found != expected or len(observed) != len(expected) or any("readError" in v for v in observed):
            raise RuntimeError("Native inspection did not read every selected field")
        if not exact_values(row["before"]["values"], observed):
            raise RuntimeError("Selected native values changed during a read-only inspection")
        key = (row["entityId"], args["component"])
        pins[key] = (row["objectInstanceId"], row["componentInstanceId"])
        values[str(key[0]) + "/" + key[1]] = observed
    return pins, values


def compare_boundary(a, b):
    return {
        "entities": first_difference(a["state"]["entities"], b["state"]["entities"]),
        "nativePhysics": first_difference(a["native"]["nativePhysics"], b["native"]["nativePhysics"]),
        "nativeFood": first_difference(a["food"]["entities"], b["food"]["entities"]),
        "nativeRound": first_difference(gameplay_round(a["native"]["nativeRound"]), gameplay_round(b["native"]["nativeRound"])),
        "nativeClocks": first_difference(native_clock_state(a["native"]), native_clock_state(b["native"])),
        "inspection": first_difference(a["inspection"], b["inspection"]),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--warmup", type=int, default=30)
    parser.add_argument("--frames", type=int, default=60)
    parser.add_argument("--precondition-warps", type=int, default=0)
    parser.add_argument("--chefs", default="43,44,45,46")
    parser.add_argument("--bridge-port", type=int, default=17636)
    parser.add_argument("--controller-port", type=int, default=17637)
    parser.add_argument("--inspection-slot", default="inspection")
    parser.add_argument("--native-trace-path", type=Path)
    parser.add_argument("--native-trace-sha256")
    parser.add_argument("--native-trace-mask", type=int, default=12)
    parser.add_argument("--native-trace-slot", default="native-physics-trace")
    parser.add_argument("--suspend-actor-rebuild-during-warp", action="store_true")
    parser.add_argument("--restore-contact-manager-free-stack", action="store_true")
    parser.add_argument("--actor-rebuild-slot", default="rigidbody-actor-rebuild")
    args = parser.parse_args()
    if args.warmup not in range(0, 301) or args.warmup == 1 or not 2 <= args.frames <= 300:
        parser.error("Use warmup0 or2..300, and continuation2..300 frames")
    if args.precondition_warps not in range(0, 4):
        parser.error("Use zero to three preconditioning warps")
    chefs = [int(s) for s in args.chefs.split(",")]
    inspection_operations(chefs)
    if bool(args.native_trace_path) != bool(args.native_trace_sha256):
        parser.error("Supply both --native-trace-path and --native-trace-sha256")
    if args.native_trace_path and not 1 <= args.native_trace_mask <= 31:
        parser.error("Use native trace mask 1..31")
    args.out.mkdir(parents=True, exist_ok=False)
    key = hashlib.sha256(str(args.out.resolve()).encode()).hexdigest()[:16]
    bridge = host = None
    summary = {"passed": False, "completed": False,
               "classification": "Paired native local-chef idle authoring diagnostic",
               "scriptSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), "chefs": chefs,
               "preconditionWarps": args.precondition_warps, "preconditionRestores": [],
               "actorRebuildSuspendedDuringWarp": args.suspend_actor_rebuild_during_warp,
               "contactManagerFreeStackRestored": args.restore_contact_manager_free_stack}
    journal = (args.out / "observations.jsonl").open("x", encoding="utf8")
    began = time.monotonic()
    pins = None
    trace_active = False
    actor_rebuild_suspended = False
    actor_rebuild_resume_args = None
    contact_pool_pending = False
    contact_pool_start = None
    automatic_contact_pool_restore = False

    def call(target, request, label, retain=True):
        row = {"label": label, "target": target, "request": request, "wallSeconds": time.monotonic() - began}
        try:
            row["response"] = (bridge if target == "bridge" else host).call(request)
            return row["response"]
        except Exception as error:
            row["error"] = str(error)
            raise
        finally:
            if retain:
                journal.write(json.dumps(row, separators=(",", ":")) + "\n")
                journal.flush()

    def trace(operation, label, values=None, retain=True):
        return call("bridge", {"command": "hot-call", "slot": args.native_trace_slot,
                               "operation": operation, "args": values or {}}, label, retain)

    def mark(code, value, label):
        if trace_active:
            trace("mark", label, {"code": code, "value": value})

    def settled(label):
        deadline = time.monotonic() + 25
        while True:
            value = host.status()
            if value.get("errors") or value.get("traceFailure") or value.get("state") == "Error":
                raise RuntimeError(json.dumps(value))
            if value.get("state") == "Paused" and not value.get("requestPending"):
                value = call("controller", {"command": "inspect", "full": True}, label)
                require_paused(value)
                return value
            if time.monotonic() > deadline:
                raise TimeoutError("Host did not reach its bounded neutral pause")
            time.sleep(.025)

    def read_frame():
        value = host.status()
        require_paused(value)
        return value["frame"]

    def boundary(label):
        nonlocal pins
        state = settled(label + "-host")
        fenced = call("bridge", {"command": "pause"}, label + "-fence")["bridge"]
        if not fenced["paused"] or not fenced["inputBlocked"]:
            raise RuntimeError("Inspection requires paused, fenced ordinary inputs")
        result = observe_settled_pause(lambda: call("bridge", {"command": "food"}, label + "-food"),
                                      read_frame, expected_frame=state["frame"], chef_ids=(43, 44, 45, 46))
        receipt = result["receipt"]
        require_native_boundary(receipt)
        # The first controller acknowledgement can precede consumption of a
        # queued kinematic MovePosition target. This helper's contract is the
        # settled pause, so recapture the controller projection only after the
        # same stable native-physics ticks used for every other subsystem.
        state = call("controller", {"command": "inspect", "full": True}, label + "-settled-host")
        require_paused(state)
        if state["frame"] != result["proof"]["frame"]:
            raise RuntimeError("Controller frame changed after settled pause observation")
        operations = inspection_operations(chefs, pins)
        inspected = call("bridge", {"command": "hot-call", "slot": args.inspection_slot,
                                    "operation": "batch", "args": {"operations": operations}}, label + "-inspection")
        observed_pins, values = inspection_result(inspected, operations)
        if pins is not None and pins != observed_pins:
            raise RuntimeError("Actual chef/component incarnations changed within the same round")
        pins = observed_pins
        after = call("bridge", {"command": "food"}, label + "-after-inspection")
        if read_frame() != state["frame"]:
            raise RuntimeError("Logical frame advanced while inspecting")
        before_native, after_native = receipt["bridge"], after["bridge"]
        for field in ("nativePhysics",):
            if not exact_values(before_native[field], after_native[field]):
                raise RuntimeError("Native physics changed during settled inspection")
        if not exact_values(native_clock_state(before_native), native_clock_state(after_native)) or not exact_values(
                gameplay_round(before_native["nativeRound"]), gameplay_round(after_native["nativeRound"])):
            raise RuntimeError("Native gameplay clocks changed during inspection")
        value = {"state": state, "native": before_native, "food": receipt["detail"],
                 "inspection": values, "pauseProof": result["proof"]}
        (args.out / (label + ".json")).write_text(json.dumps(value, indent=2), encoding="utf8")
        return value

    def step(count, label):
        start = read_frame()
        call("bridge", {"command": "arm"}, label + "-arm")
        call("controller", {"command": "step", "frames": count}, label + "-step")
        state = settled(label + "-stepped")
        if state["frame"] != start + count:
            raise RuntimeError("Observed advancing frame count differs")
        return boundary(label)

    try:
        bridge, host = Client(args.bridge_port), ControllerClient(args.controller_port)
        initial = settled("initial")
        native = call("bridge", {"command": "status"}, "initial-native")["bridge"]
        session, round_data = native["session"], native["nativeRound"]
        if not initial.get("freshLevelLoadObserved") or not native.get("loadComplete") or native.get("fullScreen") or \
                session.get("scene") != "s_sushi_1_1" or session.get("serverUsers") != 4 or \
                session.get("clientUsers") != 4 or round_data.get("configuredDuration") != 150 or \
                round_data.get("timerSuppressed") is not False or round_data["ledger"]["deliveries"] != 0 or \
                native.get("captureFramerate") != 60 or native.get("fixedDeltaTime") != .02:
            raise RuntimeError("Requires fresh four-chef windowed Story11, immediate150s timer, zero deliveries and60/50 timing")
        if (initial.get("typedActions") or {}).get("active") or (initial.get("rawInput") or {}).get("active") or \
                any(c.get("actions") for c in initial.get("actionGraph", {}).get("chefs", [])):
            raise RuntimeError("Clear the previous action graph before this neutral diagnostic")
        if args.suspend_actor_rebuild_during_warp:
            actor_status = call("bridge", {"command": "hot-call", "slot": args.actor_rebuild_slot,
                                           "operation": "status", "args": {}}, "actor-rebuild-status")
            actor_value = actor_status["detail"]["result"]
            if actor_value.get("active") is not True or actor_value.get("automaticChefs") is not True or \
                    actor_value.get("automaticGroundCollider") is not False or not actor_value.get("nativePath") or \
                    not actor_value.get("nativeSha256"):
                raise RuntimeError("Warp suspension requires the active chefs-only actor-rebuild module")
            actor_rebuild_resume_args = {
                "nativePath": actor_value["nativePath"], "sha256": actor_value["nativeSha256"],
                "automaticChefs": True, "automaticGroundCollider": False,
            }
        if args.restore_contact_manager_free_stack:
            pool_status = call("bridge", {"command": "hot-call", "slot": args.actor_rebuild_slot,
                                          "operation": "status", "args": {}}, "contact-pool-status-before")
            pool_value = pool_status["detail"]["result"]
            if pool_value.get("active") is not True or \
                    pool_value.get("automaticGroundCollider") is not False or \
                    pool_value.get("automaticContactPoolRestore") is not True or \
                    not pool_value.get("contactManagerContext") or \
                    pool_value.get("pendingContactPoolAction") != "none":
                raise RuntimeError("Free-stack restoration requires the active pool hook with an observed idle contact-manager context")
            contact_pool_start = {"captures": pool_value.get("contactPoolCaptures", 0),
                                  "restores": pool_value.get("contactPoolRestores", 0)}
            automatic_contact_pool_restore = pool_value.get("automaticContactPoolRestore") is True
            summary["automaticContactManagerFreeStackRestore"] = automatic_contact_pool_restore
        # Warmup zero deliberately checkpoints the already-settled paused
        # boundary. Resuming first can create a native contact-offset transient
        # which a public Rigidbody pose alone does not encode.
        baseline = boundary("baseline") if args.warmup == 0 else step(args.warmup, "baseline")
        frame = baseline["state"]["frame"]
        if args.native_trace_path:
            activation = trace("activate", "trace-activate", {
                "nativePath": str(args.native_trace_path.resolve()),
                "sha256": args.native_trace_sha256,
                "mask": args.native_trace_mask,
            })
            if activation["detail"]["result"].get("active") is not True:
                raise RuntimeError("Native trace did not activate")
            trace_active = True
            trace("clear", "trace-clear")
            mark(120, frame, "trace-after-warmup")
        call("controller", {"command": "checkpoint", "path": f"chef-correction-{key}-{frame}.pb"}, "checkpoint")
        comparison_baseline = baseline
        for index in range(args.precondition_warps):
            prior_attempt = comparison_baseline["native"]["nativeCheckpoints"]["restoreAttempts"]
            call("bridge", {"command": "arm"}, f"precondition-{index + 1}-arm")
            call("controller", {"command": "warp", "frame": frame, "development": True},
                 f"precondition-{index + 1}-warp")
            conditioned = boundary(f"precondition-{index + 1}")
            restore = conditioned["native"]["nativeCheckpoints"]["lastRestore"]
            comparison = compare_boundary(baseline, conditioned)
            if conditioned["state"]["frame"] != frame or not restore or restore.get("verified") is not True or \
                    restore.get("frame") != frame or restore.get("attempt", -1) <= prior_attempt:
                raise RuntimeError("No new exact preconditioning restore acknowledgement")
            if any(value is not None for value in comparison.values()):
                raise RuntimeError("Preconditioning warp changed the observable checkpoint boundary")
            summary["preconditionRestores"].append({"index": index + 1, "restore": restore,
                                                     "baselineComparison": comparison})
            comparison_baseline = conditioned
        if args.restore_contact_manager_free_stack:
            armed = call("bridge", {"command": "hot-call", "slot": args.actor_rebuild_slot,
                                     "operation": "capture-contact-pool-next", "args": {}},
                         "contact-pool-arm-capture")
            if armed["detail"]["result"].get("pendingContactPoolAction") != "capture":
                raise RuntimeError("Contact-manager free-stack capture did not arm")
            contact_pool_pending = True
        mark(130, frame, "trace-before-original")
        original = step(args.frames, "original")
        contact_pool_pending = False
        mark(140, original["state"]["frame"], "trace-after-original")
        if args.restore_contact_manager_free_stack:
            captured = call("bridge", {"command": "hot-call", "slot": args.actor_rebuild_slot,
                                        "operation": "status", "args": {}}, "contact-pool-status-captured")["detail"]["result"]
            if captured.get("pendingContactPoolAction") != "none" or \
                    captured.get("contactPoolSnapshotCaptured") is not True or \
                    captured.get("contactPoolCaptures") != contact_pool_start["captures"] + 1:
                raise RuntimeError("Contact-manager free-stack snapshot was not captured at the original unpause boundary")
            summary["contactPoolCapture"] = captured.get("contactPoolReceipts", [])[-1]
        attempt = original["native"]["nativeCheckpoints"]["restoreAttempts"]
        mark(150, frame, "trace-before-warp")
        if args.suspend_actor_rebuild_during_warp:
            suspended = call("bridge", {"command": "hot-call", "slot": args.actor_rebuild_slot,
                                        "operation": "deactivate", "args": {}}, "actor-rebuild-suspend")
            if suspended["detail"]["result"].get("active") is not False:
                raise RuntimeError("Actor rebuild did not suspend before warp arm")
            actor_rebuild_suspended = True
        call("bridge", {"command": "arm"}, "warp-arm")
        call("controller", {"command": "warp", "frame": frame, "development": True}, "warp")
        restored = boundary("restored")
        restore = restored["native"]["nativeCheckpoints"]["lastRestore"]
        if restored["state"]["frame"] != frame or not restore or restore.get("verified") is not True or \
                restore.get("frame") != frame or restore.get("attempt", -1) <= attempt:
            raise RuntimeError("No new exact native restore acknowledgement")
        mark(160, frame, "trace-after-warp")
        summary.update(checkpointFrame=frame, baselineComparison=compare_boundary(comparison_baseline, restored), nativeRestore=restore)
        if any(v is not None for k, v in summary["baselineComparison"].items() if k != "inspection"):
            raise RuntimeError("Public native baseline did not restore exactly; no replay submitted")
        if actor_rebuild_suspended:
            resumed = call("bridge", {"command": "hot-call", "slot": args.actor_rebuild_slot,
                                      "operation": "activate", "args": actor_rebuild_resume_args},
                           "actor-rebuild-resume")
            resumed_value = resumed["detail"]["result"]
            if resumed_value.get("active") is not True or resumed_value.get("automaticChefs") is not True or \
                    resumed_value.get("automaticGroundCollider") is not False:
                raise RuntimeError("Actor rebuild did not resume before replay")
            actor_rebuild_suspended = False
        if args.restore_contact_manager_free_stack:
            if automatic_contact_pool_restore:
                scheduled = call("bridge", {"command": "hot-call", "slot": args.actor_rebuild_slot,
                                             "operation": "status", "args": {}},
                                 "contact-pool-status-auto-scheduled")["detail"]["result"]
                if scheduled.get("automaticRestorePending") is not True or \
                        scheduled.get("contactPoolSnapshotFrame") != frame:
                    raise RuntimeError("Successful checkpoint warp did not automatically schedule the matching free-stack restore")
            else:
                armed = call("bridge", {"command": "hot-call", "slot": args.actor_rebuild_slot,
                                          "operation": "restore-contact-pool-next", "args": {}},
                             "contact-pool-arm-restore")
                if armed["detail"]["result"].get("pendingContactPoolAction") != "restore":
                    raise RuntimeError("Contact-manager free-stack restore did not arm")
                contact_pool_pending = True
        mark(170, frame, "trace-before-replay")
        replay = step(args.frames, "replay")
        contact_pool_pending = False
        mark(180, replay["state"]["frame"], "trace-after-replay")
        if args.restore_contact_manager_free_stack:
            pool_after = call("bridge", {"command": "hot-call", "slot": args.actor_rebuild_slot,
                                         "operation": "status", "args": {}}, "contact-pool-status-restored")["detail"]["result"]
            if pool_after.get("pendingContactPoolAction") != "none" or \
                    pool_after.get("contactPoolRestores") != contact_pool_start["restores"] + 1:
                raise RuntimeError("Contact-manager free-stack restore was not applied at the replay unpause boundary")
            summary["contactPoolRestore"] = pool_after.get("contactPoolReceipts", [])[-1]
        if trace_active:
            native_trace = trace("read", "trace-read", {"afterSequence": 0, "max": 32768}, retain=False)
            pair = {"nativeTrace": native_trace,
                    "boundaries": {"original": {"bridge": original["native"]},
                                   "restored": {"bridge": restored["native"]},
                                   "replay": {"bridge": replay["native"]}},
                    "nativeRestore": restore}
            pair_path = args.out / "native-pair.json"
            pair_path.write_text(json.dumps(pair, indent=2), encoding="utf8")
            summary["nativeTracePair"] = {"path": str(pair_path.resolve()),
                                           "sha256": hashlib.sha256(pair_path.read_bytes()).hexdigest(),
                                           "events": len(native_trace["detail"]["result"].get("events", [])),
                                           "droppedEstimate": native_trace["detail"]["result"].get("droppedEstimate")}
        summary.update(completed=True, endFrame=replay["state"]["frame"], endpointComparison=compare_boundary(original, replay))
        summary["passed"] = all(v is None for v in summary["endpointComparison"].values()) and \
            all(v is None for v in summary["baselineComparison"].values())
        summary["scope"] = "Raw physics/clocks/gameplay and selected native caches at four identically instrumented pauses; every-frame parity requires the retained exchange trace. No tolerance. Contact-manager free-stack restoration is reported explicitly when enabled."
        summary["inspectionLimits"] = "Existing Inspector emits identity-only Unity object references and valueOmitted for unsupported structs/messages (including Bounds/WorldObjectMessage). This does not capture referenced ground-collider geometry, PhysX contact manifolds or solver internals."
    except Exception as error:
        summary["error"] = str(error)
    finally:
        try:
            if bridge is not None:
                call("bridge", {"command": "pause"}, "finally-pause")
                if contact_pool_pending:
                    call("bridge", {"command": "hot-call", "slot": args.actor_rebuild_slot,
                                    "operation": "cancel-contact-pool-next", "args": {}},
                         "finally-contact-pool-cancel")
                    contact_pool_pending = False
                if actor_rebuild_suspended:
                    call("bridge", {"command": "hot-call", "slot": args.actor_rebuild_slot,
                                    "operation": "activate", "args": actor_rebuild_resume_args},
                         "finally-actor-rebuild-resume")
                    actor_rebuild_suspended = False
                if trace_active:
                    trace("deactivate", "trace-deactivate")
                    trace_active = False
        except Exception as error:
            summary.update(passed=False, pauseError=str(error))
        for client in (bridge, host):
            try:
                if client is not None:
                    client.close()
            except Exception as error:
                summary.update(passed=False, closeError=str(error))
        journal.close()
        summary["observationSha256"] = hashlib.sha256((args.out / "observations.jsonl").read_bytes()).hexdigest()
        (args.out / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf8")
        print(json.dumps(summary, indent=2))
    return 0 if summary["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
