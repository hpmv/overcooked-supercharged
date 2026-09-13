"""Observe a stable native pause boundary without advancing or modifying the game.

The caller supplies read-only receipt/frame callbacks. Every receipt is retained,
including the initial acknowledgement's unsmoothed physics and sleep flags.
This condition qualifies settled-pause comparison, never raw-acknowledgement
identity. Active-frame parity remains a separate strict trace comparison.
"""
from __future__ import annotations

import copy
import math
import time

from compare_framework_frames import first_difference, digest


class PauseBoundaryError(ValueError):
    def __init__(self, message, report=None):
        super().__init__(message)
        self.report = report


def native_physics(receipt, chef_ids=None):
    value = receipt.get("bridge", {}).get("nativePhysics")
    if not isinstance(value, dict) or value.get("source") != "native-rigidbody-and-TimeManager.FrozenPhysicsData":
        raise PauseBoundaryError("Missing or unsupported nativePhysics observation")
    bodies = value.get("bodies")
    if not isinstance(bodies, list) or not bodies:
        raise PauseBoundaryError("Native physics observation contains no bodies")
    found = set()
    vectors = ("position", "rawVelocity", "rawAngularVelocity", "resumeVelocity", "resumeAngularVelocity")
    flags = ("rawIsKinematic", "rawUseGravity", "sleeping", "frozenByNativeTimeManager", "resumeIsKinematic", "resumeUseGravity")
    for body in bodies:
        if not isinstance(body, dict):
            raise PauseBoundaryError("Invalid native physics body")
        entity, instance = body.get("entityId"), body.get("bodyInstanceId")
        if type(entity) is not int or entity < 0 or entity in found or type(instance) is not int or instance == 0:
            raise PauseBoundaryError("Invalid or duplicate native physics identity")
        found.add(entity)
        for key in vectors + ("rotation",):
            point = body.get(key)
            axes = ("x", "y", "z", "w") if key == "rotation" else ("x", "y", "z")
            if not isinstance(point, dict) or any(type(point.get(axis)) not in (int, float) or not math.isfinite(point[axis]) for axis in axes):
                raise PauseBoundaryError("Missing/nonfinite native physical field: " + key)
        if any(type(body.get(key)) is not bool for key in flags):
            raise PauseBoundaryError("Missing native frozen-body or sleep state")
    if chef_ids is not None and not set(chef_ids).issubset(found):
        raise PauseBoundaryError("Native physics lacks one or more of the four actual chefs")
    return value


def _finite(value, name):
    if type(value) not in (int, float) or not math.isfinite(value):
        raise PauseBoundaryError("Missing/nonfinite " + name)
    return value


def native_clock_fields(receipt):
    """S exposes exact native private clocks. Historical R may omit both."""
    checkpoint=(receipt.get("bridge") or {}).get("nativeCheckpoints") or {}
    server_present="nativeServerClock" in checkpoint
    client_present="nativeClientClock" in checkpoint
    if not server_present and not client_present:
        return None
    if server_present!=client_present:
        raise PauseBoundaryError("Only one native private clock observation is present")
    result={key:checkpoint.get(key) for key in ("nativeServerClock","nativeClientClock")}
    for key,count in (("nativeServerClock",3),("nativeClientClock",6)):
        values=result[key]
        if not isinstance(values,list) or len(values)!=count:
            raise PauseBoundaryError("Incomplete native private clock observation: "+key)
        for value in values:_finite(value,key)
    return result


def observe_settled_pause(read_receipt, read_frame, *, expected_frame=None, chef_ids=None,
                         timeout_seconds=1.0, poll_seconds=0.01, monotonic=time.monotonic, sleep=time.sleep):
    """Return {receipt, proof}; failures expose the same proof on exception.report.

    A distinct increasing FixedTime observation counts once, even if polling
    skipped multiple native physics ticks. Duplicate FixedTime never counts.
    The wall-time budget is checked after each callback as well as before polls;
    the caller remains responsible for bounded RPC transport timeouts.
    """
    if not 0 < timeout_seconds <= 1.0 or not 0 < poll_seconds <= timeout_seconds:
        raise PauseBoundaryError("Use a positive observation budget at most one second")
    start = monotonic()
    proof = {"passed": False, "classification": "settled authoring-pause observation; raw acknowledgement identity not asserted",
             "requiredDistinctStablePhysicsTicks": 2, "timeoutSeconds": timeout_seconds,
             "observations": [], "physicsChanges": []}
    initial_invariants = None
    previous_physics = None
    previous_fixed = None
    stable_ticks = 0
    try:
        while len(proof["observations"]) < 128:
            if monotonic() - start > timeout_seconds:
                raise PauseBoundaryError("Settled native pause observation exceeded its wall-time budget")
            before = read_frame()
            if type(before) is not int or before < 0:
                raise PauseBoundaryError("Missing actual paused controller frame")
            if expected_frame is None:
                expected_frame = before
            # Retain the raw receipt even when the following frame check fails.
            receipt = copy.deepcopy(read_receipt())
            entry = {"frameBefore": before, "receipt": receipt}
            proof["observations"].append(entry)
            after = read_frame()
            entry["frameAfter"] = after
            if before != expected_frame or after != expected_frame or type(after) is not int:
                raise PauseBoundaryError("Gameplay frame changed during paused observation")
            bridge = receipt.get("bridge") or {}
            if receipt.get("ok") is not True or bridge.get("paused") is not True or bridge.get("loadComplete") is not True:
                raise PauseBoundaryError("Native receipt is not a loaded pause")
            clock = bridge.get("logicalClock") or {}
            if clock.get("policy") != "native-float-capture-step-with-authoring-pause-suspension" or clock.get("authoringPauseRequested") is not True:
                raise PauseBoundaryError("Settled comparison requires observed authoring clock suspension")
            native_round = bridge.get("nativeRound") or {}
            if native_round.get("available") is not True:
                raise PauseBoundaryError("Missing native round during paused observation")
            _finite(native_round.get("elapsed"), "native round elapsed")
            fixed = _finite(bridge.get("fixedTime"), "native FixedTime")
            fixed_step = _finite(bridge.get("fixedDeltaTime"), "native fixed step")
            if fixed_step <= 0:
                raise PauseBoundaryError("Native fixed step is not positive")
            ticks = clock.get("eligibleTicks")
            if type(ticks) is not int or ticks < 0:
                raise PauseBoundaryError("Missing actual logical clock tick count")
            invariants = {"frame": expected_frame, "nativeRound": native_round,
                          "logicalRealtime": _finite(bridge.get("logicalRealtime"), "native logical source"),
                          "clockValue": _finite(clock.get("value"), "clock value"), "eligibleTicks": ticks,
                          "step": _finite(clock.get("step"), "capture step"), "fixedStep": fixed_step,
                          "nativePrivateClocks": native_clock_fields(receipt),
                          "frameworkAssembly": bridge.get("frameworkAssembly"), "session": bridge.get("session")}
            if first_difference(invariants["logicalRealtime"], invariants["clockValue"]) is not None:
                raise PauseBoundaryError("Logical source and clock receipt disagree")
            if initial_invariants is None:
                initial_invariants = copy.deepcopy(invariants)
            else:
                change = first_difference(initial_invariants, invariants, "$pauseInvariants")
                if change:
                    proof["invariantFirstDifference"] = change
                    raise PauseBoundaryError("Native frame, timer, clock or session changed during pause")
            physics = native_physics(receipt, chef_ids)
            if previous_fixed is not None and fixed < previous_fixed:
                raise PauseBoundaryError("Native FixedTime went backwards during pause")
            difference = first_difference(previous_physics, physics, "$nativePhysics") if previous_physics is not None else None
            if previous_physics is None or difference or clock.get("authoringPausedThisFrame") is not True:
                if difference:
                    proof["physicsChanges"].append({"observation": len(proof["observations"])-1, "firstDifference": difference})
                stable_ticks = 0
            elif fixed > previous_fixed:
                stable_ticks += 1
            previous_physics = copy.deepcopy(physics)
            previous_fixed = fixed
            entry.update(fixedTime=fixed, stableDistinctPhysicsTicks=stable_ticks, nativePhysicsSha256=digest(physics))
            proof["wallSeconds"] = monotonic() - start
            if proof["wallSeconds"] > timeout_seconds:
                raise PauseBoundaryError("Settled native pause observation exceeded its wall-time budget")
            if stable_ticks >= 2:
                proof.update(passed=True, frame=expected_frame, nativeElapsed=native_round["elapsed"],
                             firstNativePhysicsSha256=proof["observations"][0].get("nativePhysicsSha256"),
                             settledNativePhysicsSha256=digest(physics), rawAcknowledgementPhysicsEqual=
                                 first_difference(native_physics(proof["observations"][0]["receipt"],chef_ids),physics) is None)
                return {"receipt": receipt, "proof": proof}
            sleep(poll_seconds)
        raise PauseBoundaryError("Settled pause observation exceeded its sample bound")
    except Exception as error:
        proof["error"] = str(error)
        proof["wallSeconds"] = monotonic() - start
        if isinstance(error, PauseBoundaryError):
            error.report = proof
            raise
        raise PauseBoundaryError(str(error), proof) from error
