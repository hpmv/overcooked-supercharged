"""Bounded Story 1-1 mid-delivery-fade rewind/replay parity probe.

This is deliberately not a search driver.  It executes the one proven route,
warps directly from f1048 to the retained f444 snapshot, replays the exact
f445..f1048 logical inputs, and compares the two endpoints.
"""

import argparse
import hashlib
import json
from pathlib import Path
import time

from compare_framework_frames import first_difference
from framework_input_probe import (advancing_background_comparison, delivery_outcome,
                                   load_prefix_requests, native_physics_comparison,
                                   require_advancing_background)
from framework_native_search import (exact_values, gameplay_round, native_clock_state,
                                     require_native_boundary, require_recorded_completion)
from framework_pause_boundary import PauseBoundaryError, observe_settled_pause
from framework_rpc import Client, ControllerClient
from framework_story11_registry import RegistryEvidence, world_proof


SCRIPT = Path(__file__).resolve()
WORKSPACE = SCRIPT.parents[2]
DEFAULT_SOURCE = (WORKSPACE / "artifacts/framework-migration/story11-kinematic-native-v31-live-r1"
                  / "second-delivery-f1045-r44s-history-r1/observations.json")
DEFAULT_SUFFIX = (WORKSPACE / "artifacts/framework-migration/story11-kinematic-native-v32-live-r1"
                  / "capture-proven-f444-to-f1048-r1.json")
CHEFS = {"43", "44", "45", "46"}
BUTTONS = ("Pickup", "Interact", "Dash")


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def request_frames(request):
    segments = request.get("segments") if isinstance(request, dict) else None
    require(request.get("command") == "raw-input" and isinstance(segments, list) and segments,
            "Every proven prefix must be a nonempty raw-input request.")
    total = 0
    for segment in segments:
        require(isinstance(segment, dict) and type(segment.get("frames")) is int and
                segment["frames"] > 0 and isinstance(segment.get("chefs"), dict) and
                set(segment["chefs"]) == CHEFS,
                "A proven prefix segment is malformed or lacks exact four-chef coverage.")
        total += segment["frames"]
    require(total <= 36000, "A proven prefix exceeds the raw-input bound.")
    return total


def load_delivery_recording(path):
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    require(value.get("version") == 1 and
            value.get("kind") == "supercharged-logical-input-recording" and
            value.get("payloadFrames") == 1 and value.get("releaseFrames") == 2 and
            len(value.get("frames", [])) == 3,
            "The proven delivery recording must contain one payload and two release frames.")
    for index, row in enumerate(value["frames"]):
        require(row.get("ordinal") == index and set(row.get("inputs", {})) == CHEFS,
                "The delivery recording is not contiguous exact four-pad input.")
    return value


def neutral(row):
    return all(value["Pad"]["X"] == 0 and value["Pad"]["Y"] == 0 and
               not any(value[button]["Down"] for button in BUTTONS)
               for value in row["inputs"].values())


def load_suffix(path, target):
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    rows = value.get("inputs", [])
    require(value.get("validation") == "exact-four-pad-frame-coverage" and len(rows) == 604,
            "The suffix must be the exact 604-row four-pad capture.")
    for index, row in enumerate(rows):
        require(row.get("ordinal") == index and row.get("nextFrame") == target + 1 + index and
                set(row.get("inputs", {})) == CHEFS,
                "The suffix is not contiguous or lacks exact four-pad coverage.")
    require(neutral(rows[-2]) and neutral(rows[-1]),
            "The suffix lacks its two terminal neutral release frames.")
    # The controller deterministically adds the two release rows.  Supplying
    # only rows[:-2] prevents two extra frames and ends exactly at f1048.
    segments = []
    for row in rows[:-2]:
        segments.append({"frames": 1, "chefs": {
            chef: {
                "x": value["Pad"]["X"], "y": value["Pad"]["Y"],
                "pickup": value["Pickup"]["Down"],
                "interact": value["Interact"]["Down"],
                "dash": value["Dash"]["Down"],
            } for chef, value in row["inputs"].items()
        }})
    return value, {"command": "raw-input", "segments": segments}


def module_failure_values(value, path="$"):
    failures = []
    if isinstance(value, dict):
        for key, child in value.items():
            lower = key.lower()
            suspect = lower in ("failure", "error", "lasterror", "resumefailure") or \
                lower.startswith("first") and lower.endswith("difference")
            if suspect and child not in (None, "", False, [], {}):
                failures.append({"path": path + "." + key, "value": child})
            elif isinstance(child, (dict, list)):
                failures.extend(module_failure_values(child, path + "." + key))
    elif isinstance(value, list):
        for index, child in enumerate(value):
            if isinstance(child, (dict, list)):
                failures.extend(module_failure_values(child, f"{path}[{index}]"))
    return failures


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--prefix-source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--suffix-capture", type=Path, default=DEFAULT_SUFFIX)
    parser.add_argument("--delivery-recording", type=Path,
                        help="Defaults to recording.json beside --prefix-source.")
    parser.add_argument("--target-frame", type=int, default=444)
    parser.add_argument("--settle-timeout", type=float, default=120)
    parser.add_argument("--bridge-port", type=int, default=17636)
    parser.add_argument("--controller-port", type=int, default=17637)
    parser.add_argument("--restore-contact-manager-free-stack", action="store_true")
    parser.add_argument("--restore-transform-dispatch", action="store_true")
    parser.add_argument("--actor-rebuild-slot", default="rigidbody-actor-rebuild")
    parser.add_argument("--reconcile-dynamic-registry", action="store_true")
    parser.add_argument("--delivery-fade-slot", default="delivery-fade-checkpoint")
    parser.add_argument("--resume-prefix-index", type=int, choices=(0, 8), default=0,
                        help=("Resume the same bounded route at the exact f436 boundary before "
                              "prefix-8; intended only after a pause-fenced tooling failure."))
    args = parser.parse_args()
    if args.restore_transform_dispatch and not args.restore_contact_manager_free_stack:
        parser.error("--restore-transform-dispatch requires --restore-contact-manager-free-stack")
    if not 1 <= args.settle_timeout <= 600:
        parser.error("--settle-timeout must be 1..600 seconds")
    if args.target_frame != 444:
        parser.error("This bounded proof currently supports only the established f444 target.")

    prefix_source = args.prefix_source.resolve()
    suffix_path = args.suffix_capture.resolve()
    recording_path = (args.delivery_recording or (prefix_source.parent / "recording.json")).resolve()
    prefixes = load_prefix_requests(prefix_source)
    require(len(prefixes) == 15, "The proven route must contain exactly prefix-0..14.")
    prefix_lengths = [request_frames(request) for request in prefixes]
    require(prefix_lengths[8] == 50 and prefix_lengths[9:] == [61, 60, 95, 1, 30, 208],
            "The proven mid-fade suffix request lengths changed.")
    recording = load_delivery_recording(recording_path)
    suffix, suffix_request = load_suffix(suffix_path, args.target_frame)

    args.out.mkdir(parents=True, exist_ok=False)
    began = time.monotonic()
    evidence = []
    summary = {
        "passed": False,
        "classification": "bounded Story11 f444 mid-fade replay parity probe",
        "search": False,
        "targetFrame": args.target_frame,
        "prefixSource": str(prefix_source),
        "prefixSourceSha256": hashlib.sha256(prefix_source.read_bytes()).hexdigest(),
        "suffixCapture": str(suffix_path),
        "suffixCaptureSha256": hashlib.sha256(suffix_path.read_bytes()).hexdigest(),
        "deliveryRecording": str(recording_path),
        "deliveryRecordingSha256": hashlib.sha256(recording_path.read_bytes()).hexdigest(),
        "contactManagerFreeStackRestored": args.restore_contact_manager_free_stack,
        "transformDispatchRestored": args.restore_transform_dispatch,
        "registryReconciliation": args.reconcile_dynamic_registry,
        "resumePrefixIndex": args.resume_prefix_index,
    }
    bridge = host = None
    advancing_lease = None
    registry_evidence = RegistryEvidence() if args.reconcile_dynamic_registry else None

    def save(name, value):
        (args.out / name).write_text(json.dumps(value, indent=2), encoding="utf-8")

    def call(target, request, label):
        result = (bridge if target == "bridge" else host).call(request)
        evidence.append({"label": label, "target": target, "request": request,
                         "response": result, "wallSeconds": time.monotonic() - began})
        return result

    def settled(label):
        nonlocal advancing_lease
        deadline = time.monotonic() + args.settle_timeout
        while True:
            state = host.call({"command": "status"})
            if state.get("errors") or state.get("state") == "Error":
                raise RuntimeError("Controller failed: " + json.dumps(state))
            if state.get("state") == "Paused" and not state.get("requestPending"):
                result = call("controller", {"command": "inspect", "full": True}, label)
                if advancing_lease is not None:
                    after = call("bridge", {"command": "status"},
                                 advancing_lease["label"] + "-background-after")
                    comparison = advancing_background_comparison(
                        advancing_lease["label"], advancing_lease, after)
                    summary.setdefault("advancingBackgroundLeases", []).append(comparison)
                    advancing_lease = None
                    require(comparison["exact"], "Background logical-input lease changed.")
                return result
            if time.monotonic() > deadline:
                raise TimeoutError("Timed out waiting for settled controller: " + json.dumps(state))
            time.sleep(.025)

    def arm_advancing(label):
        nonlocal advancing_lease
        require(advancing_lease is None, "An advancing background lease is already active.")
        status = call("bridge", {"command": "status"}, label + "-background-before")
        before = require_advancing_background(status, label)
        call("bridge", {"command": "arm"}, label)
        advancing_lease = dict(before, label=label)

    def native_observation(label):
        def read_frame():
            state = host.call({"command": "status"})
            require(state.get("state") == "Paused" and not state.get("requestPending") and
                    not state.get("errors"), "Native observation requires a settled controller.")
            return state["frame"]
        try:
            observed = observe_settled_pause(
                lambda: call("bridge", {"command": "food"}, label), read_frame)
        except PauseBoundaryError as error:
            save(label + "-pause-proof.json", error.report)
            raise
        save(label + "-pause-proof.json", observed["proof"])
        return observed["receipt"]

    def reconcile_registry(state, native, label):
        if registry_evidence is None:
            return state, native
        requests = registry_evidence.requests(state, native)
        publications = []
        if requests:
            before = world_proof(state, native)
            call("bridge", {"command": "pause"}, label + "-registry-fence")
            for number, request in enumerate(requests):
                response = call("bridge", {"command": "hot-call", "slot": "registry-observer",
                                "operation": request["operation"], "args": request["args"]},
                                f"{label}-registry-{number}")
                publications.append((request, registry_evidence.validate_publication(request, response)))
            deadline = time.monotonic() + 3
            while True:
                state = settled(label + "-registry-received")
                if all(registry_evidence.received(request, publication, state)
                       for request, publication in publications):
                    break
                if time.monotonic() > deadline:
                    raise TimeoutError("Registry publication did not reach the controller.")
                time.sleep(.01)
            native = call("bridge", {"command": "food"}, label + "-registry-native-after")
            difference = first_difference(before, world_proof(state, native))
            require(difference is None, "Registry publication changed native state: " + json.dumps(difference))
        registry_evidence.capture(state, native)
        return state, native

    def module_statuses(label):
        call("bridge", {"command": "pause"}, label + "-fence")
        bridge_status = call("bridge", {"command": "status"}, label + "-bridge")
        active = {row.get("slot") for row in
                  bridge_status.get("bridge", {}).get("authoringModules", {}).get("active", [])}
        slots = (args.delivery_fade_slot, "chef-animator-checkpoint",
                 args.actor_rebuild_slot, "world-sync-cache", "resume-phase")
        result = {"bridge": bridge_status, "modules": {}}
        for slot in slots:
            if slot in active:
                receipt = call("bridge", {"command": "hot-call", "slot": slot,
                                           "operation": "status", "args": {}},
                               label + "-" + slot)
                result["modules"][slot] = receipt.get("detail", {}).get("result")
        require(args.delivery_fade_slot in result["modules"],
                "The active delivery-fade checkpoint module is unavailable.")
        result["failures"] = {slot: module_failure_values(status)
                              for slot, status in result["modules"].items()
                              if module_failure_values(status)}
        save(label + "-module-statuses.json", result)
        return result

    try:
        bridge = Client(args.bridge_port)
        host = ControllerClient(args.controller_port)
        start_label = "fresh-start" if args.resume_prefix_index == 0 else "resume-prefix-8-start"
        initial = settled(start_label)
        fresh_bridge = call("bridge", {"command": "status"}, start_label + "-bridge")
        frame = 1
        prefix_boundaries = []
        for index in range(args.resume_prefix_index):
            start = frame
            frame += prefix_lengths[index] + 2
            prefix_boundaries.append({"index": index, "start": start, "end": frame,
                                      "payloadFrames": prefix_lengths[index],
                                      "continuedFromPriorProcess": True})
        require(initial.get("frame") == frame and initial.get("state") == "Paused" and
                initial.get("freshLevelLoadObserved") is True and
                "Story11" in str(initial.get("setupSource")),
                "Probe requires the exact paused Story11 route boundary for its selected prefix.")
        require(fresh_bridge.get("bridge", {}).get("session", {}).get("scene") == "s_sushi_1_1",
                "Bridge is not in the Story 1-1 scene.")
        if args.resume_prefix_index:
            resume_native = native_observation(start_label + "-native")
            require_native_boundary(resume_native)
            initial, resume_native = reconcile_registry(initial, resume_native, start_label)
            save(start_label + "-managed.json", initial)
            save(start_label + "-native.json", resume_native)

        for index in range(args.resume_prefix_index, len(prefixes)):
            request = prefixes[index]
            start = frame
            if args.restore_contact_manager_free_stack and index == 8:
                call("bridge", {"command": "pause"}, "target-sidecar-schedule-fence")
                scheduled = call("bridge", {"command": "hot-call",
                                  "slot": args.actor_rebuild_slot,
                                  "operation": "capture-contact-pool-at-frame",
                                  "args": {"frame": args.target_frame}},
                                 "target-sidecar-schedule")["detail"]["result"]
                schedule_receipt = scheduled.get("result", {})
                require(scheduled.get("active") is True and
                        scheduled.get("automaticContactPoolRestore") is True and
                        schedule_receipt.get("pending") == "scheduled-capture" and
                        schedule_receipt.get("currentFrame") == start == 436 and
                        schedule_receipt.get("frame") == args.target_frame,
                        "The exact future-frame contact/Transform sidecar did not arm at f436.")
                summary["scheduledTargetSidecar"] = schedule_receipt
                save("target-sidecar-schedule.json", scheduled)
            arm_advancing(f"prefix-{index}-arm")
            call("controller", request, f"prefix-{index}-input")
            state = settled(f"prefix-{index}-settled")
            frame = start + prefix_lengths[index] + 2
            require(state.get("frame") == frame and
                    state.get("rawInput", {}).get("outcome") == "complete",
                    f"prefix-{index} did not finish at its exact release boundary.")
            prefix_boundaries.append({"index": index, "start": start, "end": frame,
                                      "payloadFrames": prefix_lengths[index]})
            if registry_evidence is not None:
                native = native_observation(f"prefix-{index}-native")
                require_native_boundary(native)
                reconcile_registry(state, native, f"prefix-{index}")
        require(prefix_boundaries[8]["start"] == 436 and
                prefix_boundaries[8]["start"] < args.target_frame < prefix_boundaries[8]["end"] and
                frame == 955,
                "Proven route no longer places f444 inside prefix-8 or no longer ends at f955.")
        summary["prefixBoundaries"] = prefix_boundaries

        arm_advancing("neutral-90-arm")
        call("controller", {"command": "step", "frames": 90}, "neutral-90")
        delivery_base = settled("delivery-base-f1045")
        require(delivery_base.get("frame") == 1045, "The 90 neutral frames did not end at f1045.")
        delivery_base_native = native_observation("delivery-base-native")
        require_native_boundary(delivery_base_native)
        save("delivery-base-managed.json", delivery_base)
        save("delivery-base-native.json", delivery_base_native)

        arm_advancing("delivery-recording-arm")
        call("controller", {"command": "raw-replay", "recording": recording},
             "delivery-recording")
        original = settled("original-f1048")
        require(original.get("frame") == 1048 and
                original.get("rawInput", {}).get("outcome") == "complete",
                "Proven delivery recording did not end at f1048.")
        require_recorded_completion(original, recording, 1045)
        original_native = native_observation("original-native")
        require_native_boundary(original_native)
        original, original_native = reconcile_registry(original, original_native, "original")
        original_delivery = delivery_outcome(delivery_base_native, original_native)
        require(original_delivery["achieved"], "Original branch did not prove exactly one delivery.")
        save("original-managed.json", original)
        save("original-native.json", original_native)
        original_modules = module_statuses("original")

        contact_before = None
        if args.restore_contact_manager_free_stack:
            actor = original_modules["modules"].get(args.actor_rebuild_slot)
            require(isinstance(actor, dict) and actor.get("active") is True and
                    actor.get("automaticContactPoolRestore") is True and
                    (not args.restore_transform_dispatch or
                     actor.get("automaticTransformDispatchRestore") is True),
                    "Requested contact/Transform restoration is not active.")
            checkpoint = call("bridge", {"command": "hot-call", "slot": args.actor_rebuild_slot,
                               "operation": "checkpoint-status", "args": {"frame": args.target_frame}},
                              "target-contact-sidecar")["detail"]["result"].get("result", {})
            require(checkpoint.get("captured") is True and checkpoint.get("coreSnapshotMatches") is True and
                    (not args.restore_transform_dispatch or
                     checkpoint.get("transformDispatchCaptured") is True),
                    "No exact contact/Transform sidecar exists for f444.")
            contact_before = actor

        call("bridge", {"command": "pause"}, "warp-fence")
        call("bridge", {"command": "arm"}, "warp-arm")
        call("controller", {"command": "warp", "frame": args.target_frame,
                            "development": True}, "warp-f444")
        restored = settled("restored-f444")
        restore_status = call("bridge", {"command": "status"}, "restore-native")
        restore = restore_status.get("bridge", {}).get("nativeCheckpoints", {}).get("lastRestore")
        prior_attempts = original_native.get("bridge", {}).get("nativeCheckpoints", {}).get("restoreAttempts", -1)
        require(restored.get("frame") == args.target_frame and isinstance(restore, dict) and
                restore.get("verified") is True and restore.get("frame") == args.target_frame and
                restore.get("attempt", -1) > prior_attempts,
                "Direct f444 warp did not produce a newly verified native restore.")
        restored_native = native_observation("restored-native")
        require_native_boundary(restored_native)
        if registry_evidence is not None:
            summary["registryEvidenceRebranch"] = registry_evidence.rebranch_after_verified_warp(
                restored, restored_native)
        save("restored-managed.json", restored)
        save("restored-native.json", restored_native)
        restored_modules = module_statuses("restored")
        restored_fade = restored_modules["modules"][args.delivery_fade_slot]
        restored_latest_fade = restored_fade.get("latest") or {}
        require(restored_fade.get("active") is True and
                restored_fade.get("pendingNativeDeliveryFades") == 1 and
                restored_fade.get("deliveredObserverEntries") == 1 and
                restored_latest_fade.get("frame") == args.target_frame and
                restored_latest_fade.get("pendingFades") == 1,
                "Restored f444 is not the exact one-pending-fade delivery state.")

        if args.restore_contact_manager_free_stack:
            actor = restored_modules["modules"][args.actor_rebuild_slot]
            require(actor.get("automaticRestorePending") is True and
                    actor.get("contactPoolSnapshotFrame") == args.target_frame,
                    "Verified f444 warp did not schedule its contact sidecar.")

        arm_advancing("replay-604-arm")
        call("controller", suffix_request, "replay-604")
        replay = settled("replay-f1048")
        require(replay.get("frame") == 1048 and
                replay.get("rawInput", {}).get("outcome") == "complete" and
                replay.get("rawInput", {}).get("payloadFrames") == 602 and
                replay.get("rawInput", {}).get("totalFramesIncludingRelease") == 604,
                "Exact suffix replay did not end at f1048 after 602 payload plus two release frames.")
        replay_native = native_observation("replay-native")
        require_native_boundary(replay_native)
        replay, replay_native = reconcile_registry(replay, replay_native, "replay")
        save("replay-managed.json", replay)
        save("replay-native.json", replay_native)
        replay_modules = module_statuses("replay")

        if args.restore_contact_manager_free_stack:
            actor = replay_modules["modules"][args.actor_rebuild_slot]
            require(actor.get("automaticRestorePending") is False and
                    actor.get("contactPoolRestores") == contact_before.get("contactPoolRestores", 0) + 1 and
                    actor.get("dirtyInteractionRestores") == contact_before.get("dirtyInteractionRestores", 0) + 1 and
                    (not args.restore_transform_dispatch or
                     actor.get("transformDispatchRestores") ==
                     contact_before.get("transformDispatchRestores", 0) + 1),
                    "Contact/Transform sidecar was not consumed exactly once by replay.")

        fixed_ids = {entity["id"] for entity in restored["entities"]}
        physics = native_physics_comparison(original_native["bridge"]["nativePhysics"],
                                            replay_native["bridge"]["nativePhysics"],
                                            fixed_ids, replay_native, args.target_frame)
        comparison = {
            "frame": original.get("frame") == replay.get("frame") == 1048,
            "managedEntities": exact_values(original.get("entities"), replay.get("entities")),
            "managedRegistry": exact_values(original.get("registry"), replay.get("registry")),
            "nativeRound": exact_values(gameplay_round(original_native["bridge"]["nativeRound"]),
                                        gameplay_round(replay_native["bridge"]["nativeRound"])),
            "nativeFood": exact_values(original_native["detail"]["entities"],
                                       replay_native["detail"]["entities"]),
            "nativePhysics": physics["equal"] is True,
            "nativeClocks": exact_values(native_clock_state(original_native["bridge"]),
                                        native_clock_state(replay_native["bridge"])),
        }
        changed = {str(identity): {"original": left, "replay": right}
                   for identity, left, right in (
                       (identity,
                        {row["id"]: row for row in original["entities"]}.get(identity),
                        {row["id"]: row for row in replay["entities"]}.get(identity))
                       for identity in ({row["id"] for row in original["entities"]} |
                                        {row["id"] for row in replay["entities"]}))
                   if not exact_values(left, right)}
        save("entity-differences.json", changed)
        replay_delivery = delivery_outcome(restored_native, replay_native)
        module_failures = {
            "original": original_modules["failures"],
            "restored": restored_modules["failures"],
            "replay": replay_modules["failures"],
        }
        summary.update({
            "originalEndFrame": original["frame"],
            "replayEndFrame": replay["frame"],
            "originalDeliveryOutcome": original_delivery,
            "replayDeliveryOutcome": replay_delivery,
            "nativeRestore": restore,
            "comparison": comparison,
            "nativePhysicsComparison": physics,
            "changedEntityIds": sorted(changed, key=int),
            "moduleFailures": module_failures,
            "replayPayloadFrames": 602,
            "replayFramesIncludingRelease": 604,
        })
        summary["passed"] = (all(comparison.values()) and replay_delivery["achieved"] and
                             not changed and not any(module_failures.values()) and
                             all(item["exact"] for item in summary.get("advancingBackgroundLeases", [])))
    except Exception as error:
        summary["error"] = str(error)
    finally:
        try:
            if bridge is not None:
                call("bridge", {"command": "pause"}, "finally-pause")
                if advancing_lease is not None:
                    after = call("bridge", {"command": "status"},
                                 advancing_lease["label"] + "-background-aborted")
                    comparison = advancing_background_comparison(
                        advancing_lease["label"], advancing_lease, after)
                    comparison["segmentSettled"] = False
                    summary.setdefault("advancingBackgroundLeases", []).append(comparison)
        except Exception as error:
            summary["pauseError"] = str(error)
            summary["passed"] = False
        for client in (bridge, host):
            try:
                if client is not None:
                    client.close()
            except Exception as error:
                summary["closeError"] = str(error)
                summary["passed"] = False
        save("observations.json", evidence)
        save("summary.json", summary)
        print(json.dumps(summary, indent=2))
    return 0 if summary["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
