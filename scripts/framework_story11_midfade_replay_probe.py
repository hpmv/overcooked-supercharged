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


class ReadinessAuditComplete(Exception):
    """Internal control flow after the requested no-resume audit boundary."""


class PrefixGateComplete(Exception):
    """Internal control flow after an explicitly bounded route-prefix gate."""


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


TERMINAL_FAILURE_FIELDS = {
    "delivery-fade-checkpoint": ("failure",),
    "chef-animator-checkpoint": (
        "failure", "resumeFailure", "liveError",
        "scheduledResumeReadyCaptureFailure", "resumePrefixObserverFailure",
        "chefRandomizeFailure",
    ),
    "rigidbody-actor-rebuild": ("failure",),
    "world-sync-cache": ("lastError",),
    "resume-phase": ("failure",),
    "body-restore": (
        "nativeShapePoseCaptureFailure", "nativeShapeGeometryRebindFailure",
        "nativeShapeGeometryRebindPoisoned",
    ),
}


def module_failure_values(slot, value, path="$"):
    """Return only provider-declared terminal state, never diagnostic history."""
    if not isinstance(value, dict):
        return []
    fields = TERMINAL_FAILURE_FIELDS.get(
        slot, ("failure", "resumeFailure", "lastError"))
    return [{"path": path + "." + key, "value": value.get(key)}
            for key in fields
            if value.get(key) not in (None, "", False, [], {})]


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
    parser.add_argument("--animator-slot", default="chef-animator-checkpoint")
    parser.add_argument("--reconcile-dynamic-registry", action="store_true")
    parser.add_argument("--delivery-fade-slot", default="delivery-fade-checkpoint")
    parser.add_argument("--body-restore-slot", default="body-restore")
    parser.add_argument("--readiness-audit-only", action="store_true",
                        help=("Stop at restored f444 after writing source-paused and target-paused "
                              "readiness reports; do not arm or run the suffix."))
    parser.add_argument("--first-replay-island-audit", action="store_true",
                        help=("After restoring f444, passively capture the exact first f445 "
                              "PhysX island pre/post transition, then stop without replaying the suffix."))
    parser.add_argument("--resume-prefix-index", type=int, choices=(0, 1, 8), default=0,
                        help=("Resume the same bounded route at the exact f60 boundary before "
                              "prefix-1, or the exact f436 boundary before prefix-8; intended "
                              "only after a pause-fenced bounded gate or tooling failure."))
    parser.add_argument("--stop-after-prefix-index", type=int, choices=range(15),
                        help=("Stop successfully at the exact settled boundary after this prefix. "
                              "This is a diagnostic route gate, not a parity result."))
    args = parser.parse_args()
    if args.restore_transform_dispatch and not args.restore_contact_manager_free_stack:
        parser.error("--restore-transform-dispatch requires --restore-contact-manager-free-stack")
    if args.readiness_audit_only and not args.restore_contact_manager_free_stack:
        parser.error("--readiness-audit-only requires --restore-contact-manager-free-stack")
    if args.first_replay_island_audit and not args.restore_contact_manager_free_stack:
        parser.error("--first-replay-island-audit requires --restore-contact-manager-free-stack")
    if args.readiness_audit_only and args.first_replay_island_audit:
        parser.error("Choose either --readiness-audit-only or --first-replay-island-audit")
    if (args.stop_after_prefix_index is not None and
            args.stop_after_prefix_index < args.resume_prefix_index):
        parser.error("--stop-after-prefix-index must not precede --resume-prefix-index")
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
        "stopAfterPrefixIndex": args.stop_after_prefix_index,
        "readinessAuditOnly": args.readiness_audit_only,
        "firstReplayIslandAudit": args.first_replay_island_audit,
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
        slots = (args.delivery_fade_slot, args.animator_slot,
                 args.actor_rebuild_slot, "world-sync-cache", "resume-phase",
                 args.body_restore_slot)
        result = {"bridge": bridge_status, "modules": {}}
        for slot in slots:
            if slot in active:
                receipt = call("bridge", {"command": "hot-call", "slot": slot,
                                           "operation": "status", "args": {}},
                               label + "-" + slot)
                result["modules"][slot] = receipt.get("detail", {}).get("result")
        require(args.delivery_fade_slot in result["modules"],
                "The active delivery-fade checkpoint module is unavailable.")
        result["failures"] = {slot: module_failure_values(slot, status)
                              for slot, status in result["modules"].items()
                              if module_failure_values(slot, status)}
        save(label + "-module-statuses.json", result)
        return result

    def actor_readiness(phase, label):
        request = {"command": "hot-call", "slot": args.actor_rebuild_slot,
                   "operation": "audit-restore-readiness",
                   "args": {"phase": phase, "sourceFrame": 1048,
                            "frame": args.target_frame}}
        try:
            response = call("bridge", request, label)
            result = response.get("detail", {}).get("result")
            require(isinstance(result, dict) and result.get("schemaVersion") == 1,
                    "Actor readiness provider returned no versioned report.")
            return result
        except Exception as error:
            return {
                "schemaVersion": 1,
                "provider": "authoring-rigidbody-actor-rebuild-v1",
                "phase": phase,
                "sourceFrame": 1048,
                "targetFrame": args.target_frame,
                "passed": False,
                "complete": False,
                "checks": [{
                    "id": "rigidbody.provider-call",
                    "module": "rigidbody-actor-rebuild",
                    "phase": phase,
                    "status": "fail",
                    "severity": "blocker",
                    "code": "READINESS_PROVIDER_FAILED",
                    "message": f"{type(error).__name__}: {error}",
                    "evidence": None,
                    "mutation": {"gameState": False, "moduleState": False,
                                 "nativeState": False},
                }],
                "blockers": ["rigidbody.provider-call"],
                "deferred": [],
                "mutation": {"gameState": False, "moduleState": False,
                             "nativeState": False},
            }

    def aggregate_readiness(phase, actor, module_snapshot):
        checks = list(actor.get("checks", []))
        rigidbody_base_required = (
            "rigidbody.target-binding", "rigidbody.actor-pair-pool",
            "rigidbody.zero-cache-scope", "rigidbody.sphere-pool",
            "rigidbody.transform-dispatch",
            "rigidbody.interaction-registration-order",
            "rigidbody.island-edge-allocator-and-change-queues",
            "rigidbody.island.capture-repeatability",
            "rigidbody.island.layout-identity",
            "rigidbody.island.node-topology",
            "rigidbody.island.edge-topology",
            "rigidbody.island.island-topology",
            "rigidbody.island.allocator-free-order",
            "rigidbody.island.node-bitmaps",
            "rigidbody.island.change-queue-order",
            "rigidbody.island.sip-edge-bindings",
            "rigidbody.island.edge-type-scope",
            "rigidbody.island.body-owner-coherence",
            "rigidbody.island.transition-capture",
            "rigidbody.island.transition-identity",
            "rigidbody.island.transition-pre-state",
            "rigidbody.island.transition-post-state",
            "rigidbody.island.edge-journal",
            "rigidbody.transform-cache-id-pool",
            "rigidbody.broadphase-created-overlap-order",
            "rigidbody.dirty-interaction-live-projection",
            "rigidbody.contact-report-lists-and-buffer",
            "rigidbody.first-output-owner-convergence",
            "rigidbody.audit-state-proof",
        )
        rigidbody_target_required = (
            "rigidbody.target-contact-sidecar", "rigidbody.target-sip-sidecar",
            "rigidbody.target-actor-pair-sidecar",
            "rigidbody.target-actor-pair-report-sidecar",
            "rigidbody.target-nphase-report-sidecar",
            "rigidbody.target-interaction-graph-sidecar",
            "rigidbody.target-transform-cache-sidecar",
            "rigidbody.target-broadphase-transition-sidecar",
            "rigidbody.target-island-sidecar",
            "rigidbody.target-island-transition-sidecar",
            "rigidbody.target-large-sidecar", "rigidbody.target-sphere-sidecar",
            "rigidbody.target-dirty-sidecar", "rigidbody.target-cross-pool-coherence",
            "rigidbody.resolved-plan-rows", "rigidbody.native-audit-repeatability",
            "rigidbody.native.arguments", "rigidbody.native.observer",
            "rigidbody.native.recreate-state", "rigidbody.native.revisions",
            "rigidbody.native.target-buffers", "rigidbody.native.target-uniqueness",
            "rigidbody.native.rows", "rigidbody.native.row-uniqueness",
            "rigidbody.native.sip-capture", "rigidbody.native.sip-identity",
            "rigidbody.native.sip-semantic-counts", "rigidbody.native.sip-membership",
            "rigidbody.native.sip-legacy-arm", "rigidbody.native.contact-capture",
            "rigidbody.native.contact-identity", "rigidbody.native.contact-bitmaps",
            "rigidbody.native.contact-membership", "rigidbody.native.large-capture",
            "rigidbody.native.large-identity", "rigidbody.native.large-membership",
            "rigidbody.native.writability",
            "rigidbody.actor-pair.repeatability", "rigidbody.actor-pair.pool-identity",
            "rigidbody.actor-pair.target-partition", "rigidbody.actor-pair.live-partition",
            "rigidbody.actor-pair.reachability", "rigidbody.actor-pair.reference-counts",
            "rigidbody.actor-pair.target-coherence", "rigidbody.actor-pair.touch-state",
            "rigidbody.actor-pair.internal-flags",
            "rigidbody.actor-pair-report.repeatability",
            "rigidbody.actor-pair.report-data-pool",
            "rigidbody.actor-pair.reuse-decision",
            "rigidbody.actor-pair.allocation-binding",
            "rigidbody.nphase-report.repeatability",
            "rigidbody.nphase-report.layout-identity",
            "rigidbody.nphase-report.target-membership",
            "rigidbody.nphase-report.live-projection",
            "rigidbody.nphase-report.scene-timestamps",
            "rigidbody.interaction-graph.repeatability",
            "rigidbody.interaction-graph.layout-identity",
            "rigidbody.interaction-graph.active-body-order",
            "rigidbody.interaction-graph.global-order",
            "rigidbody.interaction-graph.actor-order-and-cached-indices",
            "rigidbody.interaction-graph.sip-semantic-keys",
            "rigidbody.interaction-graph.pointer-pool-topology",
            "rigidbody.transform-cache.repeatability",
            "rigidbody.transform-cache.layout-identity",
            "rigidbody.transform-cache.free-id-order",
            "rigidbody.transform-cache.binding-and-refcounts",
            "rigidbody.transform-cache.active-transforms",
            "rigidbody.broadphase.capture", "rigidbody.broadphase.identity",
            "rigidbody.broadphase.created-order", "rigidbody.broadphase.deleted-order",
            "rigidbody.broadphase.post-state",
        )
        rigidbody_required = list(rigidbody_base_required)
        if phase == "target-paused":
            rigidbody_required.extend(rigidbody_target_required)
        provider_coverage = actor.get("coverage", {})
        actor_checks = actor.get("checks", [])
        actor_ids = [row.get("id") for row in actor_checks]
        actor_duplicate_ids = sorted({check_id for check_id in actor_ids
                                      if actor_ids.count(check_id) > 1})
        actor_blockers = [row.get("id") for row in actor_checks
                          if row.get("status") == "fail" and
                          row.get("severity") == "blocker"]
        actor_deferred = [row.get("id") for row in actor_checks
                          if row.get("status") == "deferred"]
        provider_contract_ok = (
            actor.get("schemaVersion") == 1 and
            actor.get("provider") == "authoring-rigidbody-actor-rebuild-v1" and
            actor.get("phase") == phase and actor.get("sourceFrame") == 1048 and
            actor.get("targetFrame") == args.target_frame and
            provider_coverage.get("contractVersion") == 6 and
            set(provider_coverage.get("required", [])) == set(rigidbody_required) and
            provider_coverage.get("uncovered") == [] and
            provider_coverage.get("duplicates") == [] and
            not actor_duplicate_ids and
            actor.get("mutation") == {"gameState": False, "moduleState": False,
                                      "nativeState": False} and
            actor.get("passed") == (not actor_blockers) and
            actor.get("complete") == (not actor_deferred)
        )
        checks.append({
            "id": "aggregate.actor-provider-contract",
            "module": "aggregate",
            "phase": phase,
            "status": "pass" if provider_contract_ok else "fail",
            "severity": "blocker",
            "code": ("ACTOR_PROVIDER_CONTRACT_EXACT" if provider_contract_ok else
                     "ACTOR_PROVIDER_CONTRACT_MISMATCH"),
            "message": ("The actor provider matches the aggregate-owned versioned contract."
                        if provider_contract_ok else
                        "The actor provider report can shrink, misstate, or mutate the aggregate contract."),
            "evidence": {"expectedRequired": rigidbody_required,
                         "providerCoverage": provider_coverage,
                         "duplicateIds": actor_duplicate_ids,
                         "reportedPassed": actor.get("passed"),
                         "computedBlockers": actor_blockers,
                         "reportedComplete": actor.get("complete"),
                         "computedDeferred": actor_deferred},
            "mutation": {"gameState": False, "moduleState": False,
                         "nativeState": False},
        })
        module_names = {
            args.animator_slot: "animator",
            "world-sync-cache": "world-sync",
            args.body_restore_slot: "body-restore",
            "resume-phase": "resume-phase",
            args.delivery_fade_slot: "delivery-fade",
        }
        deferred_scopes = {
            "animator": ("staged-native-restore-and-final-controller-memory",
                         "Requires the target maintenance/release lifecycle."),
            "world-sync": ("restored-cache-and-resume-validation",
                           "Requires a dedicated non-mutating provider for the exact WarpSpec."),
            "body-restore": ("post-maintenance-native-body-and-shape-validation",
                             "Requires a pure split from the current restore-time validator."),
            "resume-phase": ("accepted-input-provenance-and-phase-commit",
                             "Exists only at the first real resume boundary."),
            "delivery-fade": ("first-output-fade-convergence",
                              "Exists only after the first advancing output."),
        }
        modules = module_snapshot.get("modules", {})
        for slot, name in module_names.items():
            state = modules.get(slot)
            failures = module_failure_values(slot, state)
            activation_field = "hasRegistrationLease" if name == "body-restore" else "active"
            available = isinstance(state, dict) and state.get(activation_field) is True
            checks.append({
                "id": f"{name}.provider-availability",
                "module": name,
                "phase": phase,
                "status": "pass" if available else "fail",
                "severity": "blocker",
                "code": "PROVIDER_ACTIVE" if available else "PROVIDER_UNAVAILABLE",
                "message": ("The required provider is loaded and active."
                            if available else "The required provider is absent or inactive."),
                "evidence": {"slot": slot, "activationField": activation_field,
                             "activationValue": (state.get(activation_field)
                                                 if isinstance(state, dict) else None)},
                "mutation": {"gameState": False, "moduleState": False,
                             "nativeState": False},
            })
            checks.append({
                "id": f"{name}.provider-terminal-state",
                "module": name,
                "phase": phase,
                "status": "fail" if failures else "pass",
                "severity": "blocker",
                "code": "PROVIDER_DECLARED_FAILURE" if failures else "NO_DECLARED_TERMINAL_FAILURE",
                "message": ("The provider explicitly reports a terminal failure."
                            if failures else "The provider reports no terminal failure in its declared status fields."),
                "evidence": {"slot": slot, "failures": failures},
                "mutation": {"gameState": False, "moduleState": False,
                             "nativeState": False},
            })
            checks.append({
                "id": f"{name}.provider-health-contract",
                "module": name,
                "phase": phase,
                "status": "deferred",
                "severity": "blocker",
                "code": "VERSIONED_HEALTH_CONTRACT_REQUIRED",
                "message": "Readiness will not infer health from diagnostic field names; this provider needs a versioned health contract.",
                "evidence": {"slot": slot},
                "mutation": {"gameState": False, "moduleState": False,
                             "nativeState": False},
            })
            scope, reason = deferred_scopes[name]
            checks.append({
                "id": f"{name}.{scope}",
                "module": name,
                "phase": phase,
                "status": "deferred",
                "severity": "blocker",
                "code": "READONLY_PROVIDER_REQUIRED",
                "message": reason,
                "evidence": {"requiredPhase": ("first-advancing-output"
                                                if "first-" in scope or name == "resume-phase"
                                                else "target-paused")},
                "mutation": {"gameState": False, "moduleState": False,
                             "nativeState": False},
            })
        required_ids = rigidbody_required + [
            "rigidbody.coverage-manifest", "aggregate.actor-provider-contract"
        ]
        for slot, name in module_names.items():
            scope, _reason = deferred_scopes[name]
            required_ids.extend((
                f"{name}.provider-availability",
                f"{name}.provider-terminal-state",
                f"{name}.provider-health-contract",
                f"{name}.{scope}",
            ))
        emitted_ids = [row.get("id") for row in checks]
        duplicate_ids = sorted({check_id for check_id in emitted_ids
                                if emitted_ids.count(check_id) > 1})
        missing_ids = [check_id for check_id in required_ids
                       if check_id not in emitted_ids]
        for check_id in missing_ids:
            checks.append({
                "id": check_id,
                "module": "aggregate",
                "phase": phase,
                "status": "fail",
                "severity": "blocker",
                "code": "MISSING_REQUIRED_CHECK",
                "message": "The aggregate omitted a required readiness check.",
                "evidence": None,
                "mutation": {"gameState": False, "moduleState": False,
                             "nativeState": False},
            })
        checks.append({
            "id": "aggregate.coverage-manifest",
            "module": "aggregate",
            "phase": phase,
            "status": "fail" if duplicate_ids or missing_ids else "pass",
            "severity": "blocker",
            "code": ("DUPLICATE_CHECK_ID" if duplicate_ids else
                     "MISSING_REQUIRED_CHECK" if missing_ids else
                     "REQUIRED_COVERAGE_ENUMERATED"),
            "message": ("The aggregate emitted duplicate readiness check identifiers."
                        if duplicate_ids else
                        "The aggregate omitted required readiness check identifiers."
                        if missing_ids else
                        "Every required aggregate readiness check is explicitly represented."),
            "evidence": {"contractVersion": 6, "required": required_ids,
                         "uncovered": missing_ids, "duplicates": duplicate_ids},
            "mutation": {"gameState": False, "moduleState": False,
                         "nativeState": False},
        })
        blockers = [row["id"] for row in checks
                    if row.get("status") == "fail" and row.get("severity") == "blocker"]
        deferred = [row["id"] for row in checks if row.get("status") == "deferred"]
        counts = {status: sum(row.get("status") == status for row in checks)
                  for status in ("pass", "fail", "deferred", "not-applicable")}
        return {
            "schemaVersion": 1,
            "classification": "read-only rewind readiness aggregate",
            "phase": phase,
            "sourceFrame": 1048,
            "targetFrame": args.target_frame,
            "passed": not blockers,
            "complete": not deferred,
            "ready": not blockers and not deferred,
            "counts": counts,
            "checks": checks,
            "blockers": blockers,
            "deferred": deferred,
            "coverage": {
                "contractVersion": 6,
                "required": required_ids,
                "uncovered": missing_ids,
                "duplicates": duplicate_ids,
                "rigidbody": "native aggregate provider",
                "animator": "health plus deferred pure provider",
                "worldSync": "health plus deferred pure provider",
                "bodyRestore": "health plus deferred pure provider",
                "resumePhase": "health plus first-resume deferred",
                "deliveryFade": "health plus first-output deferred",
            },
            "mutation": actor.get("mutation", {"gameState": False,
                                                "moduleState": False,
                                                "nativeState": False}),
            "actorProvider": actor,
        }

    try:
        bridge = Client(args.bridge_port)
        host = ControllerClient(args.controller_port)
        start_label = ("fresh-start" if args.resume_prefix_index == 0 else
                       f"resume-prefix-{args.resume_prefix_index}-start")
        initial = settled(start_label)
        fresh_bridge = call("bridge", {"command": "status"}, start_label + "-bridge")
        frame = 1
        prefix_boundaries = []
        target_actor_observation_f488 = None
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
        require(initial.get("resumePhaseMetadataVersion") == 1,
                "Probe requires a controller host that advertises native resume-phase metadata v1.")
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
            if index == 8:
                call("bridge", {"command": "pause"}, "target-checkpoint-schedule-fence")
                animator_schedule = call(
                    "bridge", {"command": "hot-call", "slot": args.animator_slot,
                               "operation": "capture-resume-ready-at-frame",
                               "args": {"frame": args.target_frame}},
                    "target-animator-schedule")["detail"]["result"]
                animator_receipt = animator_schedule.get("lastScheduledResumeReadyCapture", {})
                require(animator_schedule.get("active") is True and
                        animator_schedule.get("scheduledResumeReadyCapturePending") is True and
                        animator_schedule.get("scheduledResumeReadyCaptureFrame") == args.target_frame and
                        animator_schedule.get("scheduledResumeReadyLastObservedFrame") == start == 436 and
                        animator_schedule.get("scheduledResumeReadyCaptureArms") == 1 and
                        animator_receipt.get("pending") == "scheduled-capture" and
                        animator_receipt.get("currentFrame") == start and
                        animator_receipt.get("frame") == args.target_frame and
                        animator_receipt.get("gameStateMutation") is False,
                        "The exact future-frame Animator resume-ready capture did not arm at f436.")
                summary["scheduledAnimatorResumeReady"] = animator_receipt
                save("target-animator-schedule.json", animator_schedule)
            if args.restore_contact_manager_free_stack and index == 8:
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
            if index == 8:
                call("bridge", {"command": "pause"}, "target-animator-capture-check-fence")
                animator_capture = call(
                    "bridge", {"command": "hot-call", "slot": args.animator_slot,
                               "operation": "status", "args": {}},
                    "target-animator-capture-check")["detail"]["result"]
                save("target-animator-capture.json", animator_capture)
                capture_receipt = animator_capture.get("lastScheduledResumeReadyCapture", {})
                require(animator_capture.get("scheduledResumeReadyCapturePending") is False and
                        animator_capture.get("scheduledResumeReadyCaptureFrame") == -1 and
                        animator_capture.get("scheduledResumeReadyLastObservedFrame") == args.target_frame and
                        animator_capture.get("scheduledResumeReadyCaptureTriggers") == 1 and
                        animator_capture.get("scheduledResumeReadyCaptureFailure") is None and
                        args.target_frame in animator_capture.get("resumePrefixReferenceFrames", []) and
                        args.target_frame not in animator_capture.get("ambiguousResumePrefixFrames", []) and
                        capture_receipt.get("exact") is True,
                        "The exact f444 Animator resume-ready capture failed: " +
                        json.dumps({"failure": animator_capture.get("scheduledResumeReadyCaptureFailure"),
                                    "receipt": capture_receipt}))
                if args.restore_contact_manager_free_stack:
                    target_actor_observation_f488 = call(
                        "bridge", {"command": "hot-call", "slot": args.actor_rebuild_slot,
                                   "operation": "checkpoint-status",
                                   "args": {"frame": args.target_frame}},
                        "target-physics-observation-f488")["detail"]["result"]
                    target_checkpoint = target_actor_observation_f488.get("result", {})
                    transform_cache = target_checkpoint.get("transformCache", {})
                    broadphase = target_checkpoint.get("finishBroadPhase", {})
                    island_snapshot = target_checkpoint.get("islandSnapshot", {})
                    island_transition = target_checkpoint.get("islandTransition", {})
                    island_pre = island_transition.get("pre", {}) \
                        if isinstance(island_transition, dict) else {}
                    island_post = island_transition.get("post", {}) \
                        if isinstance(island_transition, dict) else {}
                    require(state.get("frame") == 488 and
                            target_actor_observation_f488.get("active") is True and
                            target_actor_observation_f488.get("finishBroadPhaseObserverInstalled") is True and
                            target_actor_observation_f488.get("finishBroadPhaseCapturePending") is False and
                            target_actor_observation_f488.get("finishBroadPhaseCaptures") == 1 and
                            target_checkpoint.get("captured") is True and
                            target_checkpoint.get("coreSnapshotMatches") is True and
                            isinstance(transform_cache, dict) and
                            isinstance(broadphase, dict) and
                            broadphase.get("expectedPass") == broadphase.get("pass") == 0 and
                            broadphase.get("armedOrdinal") == broadphase.get("observationOrdinal") and
                            isinstance(broadphase.get("observationOrdinal"), int) and
                            broadphase.get("observationOrdinal") > 0 and
                            isinstance(broadphase.get("armedThreadId"), int) and
                            broadphase.get("armedThreadId") > 0 and
                            isinstance(broadphase.get("threadId"), int) and
                            broadphase.get("threadId") > 0 and
                            broadphase.get("droppedObservations") == 0 and
                            broadphase.get("expectedScene") == broadphase.get("observedScene") ==
                            transform_cache.get("ownerScene") and
                            broadphase.get("expectedContext") == broadphase.get("observedContext") ==
                            transform_cache.get("context") and
                            broadphase.get("expectedNPhaseCore") == broadphase.get("observedNPhaseCore") ==
                            transform_cache.get("nphaseCore") and
                            broadphase.get("interactionScene") == transform_cache.get("interactionScene") and
                            broadphase.get("transformCache") == transform_cache.get("transformCache") and
                            isinstance(broadphase.get("created"), list) and
                            isinstance(broadphase.get("deleted"), list) and
                            target_actor_observation_f488.get("islandObserverInstalled") is True and
                            target_actor_observation_f488.get("islandCapturePending") is False and
                            target_actor_observation_f488.get("islandSnapshotCaptures") == 1 and
                            target_actor_observation_f488.get("islandTransitionCaptures") == 1 and
                            isinstance(island_snapshot, dict) and
                            isinstance(island_transition, dict) and
                            isinstance(island_pre, dict) and isinstance(island_post, dict) and
                            island_snapshot.get("phase") == 1 and island_pre.get("phase") == 2 and
                            island_post.get("phase") == 3 and
                            island_snapshot.get("nphaseCore") == transform_cache.get("nphaseCore") and
                            island_snapshot.get("ownerScene") == transform_cache.get("ownerScene") and
                            island_snapshot.get("interactionScene") ==
                            transform_cache.get("interactionScene") and
                            island_snapshot.get("context") == transform_cache.get("context") and
                            island_transition.get("expectedManager") ==
                            island_transition.get("observedManager") ==
                            island_snapshot.get("islandManager") and
                            island_transition.get("expectedContext") ==
                            island_transition.get("observedContext") ==
                            island_snapshot.get("context") and
                            island_transition.get("expectedNPhase") ==
                            island_transition.get("observedNPhase") ==
                            island_snapshot.get("nphaseCore") and
                            island_transition.get("expectedPass") ==
                            island_transition.get("pass") == 0 and
                            island_transition.get("armedOrdinal") ==
                            island_transition.get("observationOrdinal") and
                            isinstance(island_transition.get("observationOrdinal"), int) and
                            island_transition.get("observationOrdinal") > 0 and
                            isinstance(island_snapshot.get("observerSequence"), int) and
                            isinstance(island_transition.get("observerSequence"), int) and
                            island_transition.get("observerSequence") ==
                            island_snapshot.get("observerSequence") + 1 and
                            isinstance(island_transition.get("armedThreadId"), int) and
                            island_transition.get("armedThreadId") > 0 and
                            isinstance(island_transition.get("threadId"), int) and
                            island_transition.get("threadId") > 0 and
                            island_transition.get("threadId") ==
                            island_pre.get("captureThreadId") ==
                            island_post.get("captureThreadId") and
                            island_transition.get("preSnapshotHash") ==
                            island_pre.get("snapshotHash") and
                            island_transition.get("postSnapshotHash") ==
                            island_post.get("snapshotHash") and
                            island_transition.get("journalBeginOrdinal") ==
                            island_snapshot.get("journalEndOrdinal") and
                            island_transition.get("journalEndOrdinal") ==
                            island_post.get("journalEndOrdinal") and
                            island_transition.get("validationFlags") == "0x0000007F" and
                            island_transition.get("inFlight") == 0 and
                            isinstance(island_transition.get("journal"), list) and
                            isinstance(island_transition.get("journalBeginOrdinal"), int) and
                            isinstance(island_transition.get("journalEndOrdinal"), int) and
                            len(island_transition.get("journal", [])) ==
                            island_transition.get("journalEndOrdinal") -
                            island_transition.get("journalBeginOrdinal"),
                            "The f444 snapshot and exact subsequent f445 pass-zero observation were not "
                            "published as one coherent transaction by f488.")
                    summary["capturedPhysicsObservationF488"] = {
                        "transformCacheSnapshotHash": transform_cache.get("snapshotHash"),
                        "broadphaseObservationOrdinal": broadphase.get("observationOrdinal"),
                        "createdCount": len(broadphase.get("created", [])),
                        "deletedCount": len(broadphase.get("deleted", [])),
                        "islandSettledSnapshotHash": island_snapshot.get("snapshotHash"),
                        "islandPreSnapshotHash": island_transition.get("preSnapshotHash"),
                        "islandPostSnapshotHash": island_transition.get("postSnapshotHash"),
                        "islandObservationOrdinal": island_transition.get("observationOrdinal"),
                        "islandJournalRecords": len(island_transition.get("journal", [])),
                    }
                    save("target-physics-observation-f488.json", target_actor_observation_f488)
            if registry_evidence is not None:
                native = native_observation(f"prefix-{index}-native")
                require_native_boundary(native)
                reconcile_registry(state, native, f"prefix-{index}")
            if args.stop_after_prefix_index == index:
                summary.update({
                    "classification": "bounded Story11 route-prefix diagnostic gate",
                    "prefixGateCompleted": True,
                    "prefixGateEndFrame": frame,
                    "prefixBoundaries": prefix_boundaries,
                })
                raise PrefixGateComplete()
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

        animator = original_modules["modules"].get(args.animator_slot)
        animator_receipt = animator.get("lastScheduledResumeReadyCapture", {}) \
            if isinstance(animator, dict) else {}
        require(isinstance(animator, dict) and animator.get("active") is True and
                animator.get("scheduledResumeReadyCapturePending") is False and
                animator.get("scheduledResumeReadyCaptureFrame") == -1 and
                animator.get("scheduledResumeReadyLastObservedFrame") == args.target_frame and
                animator.get("scheduledResumeReadyCaptureArms") == 1 and
                animator.get("scheduledResumeReadyCaptureTriggers") == 1 and
                args.target_frame in animator.get("resumePrefixReferenceFrames", []) and
                args.target_frame not in animator.get("ambiguousResumePrefixFrames", []) and
                animator.get("resumePrefixObserverFailure") is None and
                animator_receipt.get("frame") == args.target_frame and
                animator_receipt.get("boundaryIdentityLinked") is True and
                animator_receipt.get("gameStateMutation") is False and
                animator_receipt.get("exact") is True,
                "No exact unambiguous Animator resume-ready checkpoint exists for f444.")
        summary["capturedAnimatorResumeReady"] = animator_receipt

        contact_before = None
        if args.restore_contact_manager_free_stack:
            actor = original_modules["modules"].get(args.actor_rebuild_slot)
            require(isinstance(actor, dict) and actor.get("active") is True and
                    actor.get("automaticContactPoolRestore") is True and
                    (not args.restore_transform_dispatch or
                     actor.get("automaticTransformDispatchRestore") is True),
                    "Requested contact/Transform restoration is not active.")
            require(isinstance(target_actor_observation_f488, dict) and
                    actor.get("finishBroadPhaseCapturePending") is False and
                    actor.get("finishBroadPhaseCaptures") == 1 and
                    actor.get("finishBroadPhaseSnapshot") ==
                    target_actor_observation_f488.get("finishBroadPhaseSnapshot") and
                    actor.get("transformCacheSnapshot") ==
                    target_actor_observation_f488.get("transformCacheSnapshot") and
                    actor.get("islandCapturePending") is False and
                    actor.get("islandSnapshotCaptures") == 1 and
                    actor.get("islandTransitionCaptures") == 1 and
                    actor.get("islandSnapshot") ==
                    target_actor_observation_f488.get("islandSnapshot") and
                    actor.get("islandTransition") ==
                    target_actor_observation_f488.get("islandTransition"),
                    "The exact f445 observation changed or was replaced between f488 and f1048.")
            checkpoint = call("bridge", {"command": "hot-call", "slot": args.actor_rebuild_slot,
                               "operation": "checkpoint-status", "args": {"frame": args.target_frame}},
                              "target-contact-sidecar")["detail"]["result"].get("result", {})
            require(checkpoint.get("captured") is True and checkpoint.get("coreSnapshotMatches") is True and
                    (not args.restore_transform_dispatch or
                     checkpoint.get("transformDispatchCaptured") is True),
                    "No exact contact/Transform sidecar exists for f444.")
            contact_before = actor

            source_actor_readiness = actor_readiness(
                "source-paused", "source-paused-actor-readiness")
            source_readiness = aggregate_readiness(
                "source-paused", source_actor_readiness, original_modules)
            summary["sourceReadiness"] = {
                "passed": source_readiness["passed"],
                "complete": source_readiness["complete"],
                "counts": source_readiness["counts"],
                "blockers": source_readiness["blockers"],
                "deferred": source_readiness["deferred"],
            }
            save("source-paused-readiness.json", source_readiness)

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

            target_actor_readiness = actor_readiness(
                "target-paused", "target-paused-actor-readiness")
            target_readiness = aggregate_readiness(
                "target-paused", target_actor_readiness, restored_modules)
            summary["targetReadiness"] = {
                "passed": target_readiness["passed"],
                "complete": target_readiness["complete"],
                "counts": target_readiness["counts"],
                "blockers": target_readiness["blockers"],
                "deferred": target_readiness["deferred"],
            }
            save("target-paused-readiness.json", target_readiness)
            if args.readiness_audit_only:
                summary["auditCompleted"] = True
                summary["classification"] = "bounded Story11 f444 read-only rewind readiness audit"
                raise ReadinessAuditComplete()
            if args.first_replay_island_audit:
                armed = call(
                    "bridge",
                    {"command": "hot-call", "slot": args.actor_rebuild_slot,
                     "operation": "arm-first-replay-island-audit", "args": {}},
                    "first-replay-island-audit-arm")
                armed_result = armed.get("detail", {}).get("result", {}).get("result", {})
                require(armed_result.get("armed") is True and
                        armed_result.get("frame") == args.target_frame,
                        "The first-replay island observer did not arm at restored f444.")
                arm_advancing("first-replay-island-audit-step-arm")
                call("controller", {"command": "step", "frames": 1},
                     "first-replay-island-audit-step")
                first_replay = settled("first-replay-island-audit-f445")
                require(first_replay.get("frame") == args.target_frame + 1,
                        "The first-replay island audit did not stop at exact f445.")
                copied = call(
                    "bridge",
                    {"command": "hot-call", "slot": args.actor_rebuild_slot,
                     "operation": "copy-first-replay-island-audit", "args": {}},
                    "first-replay-island-audit-copy")
                copied_result = copied.get("detail", {}).get("result", {}).get("result", {})
                restored_transition = copied_result.get("transition")
                target_transition = target_actor_observation_f488.get("islandTransition")
                require(copied_result.get("captured") is True and
                        copied_result.get("checkpointFrame") == args.target_frame and
                        copied_result.get("observedFrame") == args.target_frame + 1 and
                        isinstance(restored_transition, dict) and
                        isinstance(target_transition, dict),
                        "The exact first-replay island transition was not copied at f445.")
                audit = {
                    "target": target_transition,
                    "restored": restored_transition,
                    "targetPreSnapshotHash": target_transition.get("preSnapshotHash"),
                    "restoredPreSnapshotHash": restored_transition.get("preSnapshotHash"),
                    "targetPostSnapshotHash": target_transition.get("postSnapshotHash"),
                    "restoredPostSnapshotHash": restored_transition.get("postSnapshotHash"),
                    "targetPreLiveContactEdges": target_transition.get("pre", {}).get(
                        "liveContactEdges"),
                    "restoredPreLiveContactEdges": restored_transition.get("pre", {}).get(
                        "liveContactEdges"),
                    "targetPostLiveContactEdges": target_transition.get("post", {}).get(
                        "liveContactEdges"),
                    "restoredPostLiveContactEdges": restored_transition.get("post", {}).get(
                        "liveContactEdges"),
                    "targetJournalRecords": len(target_transition.get("journal", [])),
                    "restoredJournalRecords": len(restored_transition.get("journal", [])),
                }
                save("first-replay-island-transition-audit.json", audit)
                summary["firstReplayIslandTransition"] = {
                    key: value for key, value in audit.items()
                    if key not in ("target", "restored")
                }
                summary["auditCompleted"] = True
                summary["classification"] = (
                    "bounded Story11 f444-to-f445 read-only first-replay island transition audit")
                raise ReadinessAuditComplete()

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
    except (ReadinessAuditComplete, PrefixGateComplete):
        pass
    except Exception as error:
        summary["error"] = str(error)
    finally:
        try:
            if bridge is not None:
                call("bridge", {"command": "pause"}, "finally-pause")
                if summary.get("error"):
                    try:
                        failure_body = call(
                            "bridge",
                            {"command": "hot-call", "slot": args.body_restore_slot,
                             "operation": "status", "args": {}},
                            "failure-body-restore-status")
                        save("failure-body-restore-status.json", failure_body)
                    except Exception as diagnostic_error:
                        summary["failureBodyRestoreStatusError"] = str(diagnostic_error)
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
    bounded_complete = summary.get("auditCompleted") or summary.get("prefixGateCompleted")
    bounded_clean = not any(summary.get(key) for key in
                            ("error", "pauseError", "closeError"))
    return 0 if summary["passed"] or (bounded_complete and bounded_clean) else 1


if __name__ == "__main__":
    raise SystemExit(main())
