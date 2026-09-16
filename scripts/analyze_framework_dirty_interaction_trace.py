"""Correlate PhysX dirty-interaction order with contact-manager allocation.

This is an offline, read-only analyzer for NativePhysicsTrace r22 exports.  It
examines the first non-empty NPhaseCore::updateDirtyInteractions call in each
branch, compares its dense set with the subsequent manager-bearing semantic
pairs, and joins every ShapeInstancePairLL::createManager call to the matching
PxsContext::createContactManager descriptor and pool slot.
"""
import argparse
import hashlib
import json
from pathlib import Path


KIND_CONTACT_MANAGER = 52
KIND_DIRTY_HEADER = 72
KIND_DIRTY_ENTRY = 73
KIND_CREATE_MANAGER = 74
UPDATE_STATE_CREATE_MANAGER_RVA = 0xA57C3A


def parse_hex(value):
    if not isinstance(value, str) or not value.startswith("0x"):
        raise ValueError("Expected a hexadecimal pointer string.")
    return int(value, 16)


def pair_key(words):
    if not isinstance(words, list) or len(words) < 5:
        raise ValueError("Trace event does not contain an element pair.")
    values = sorted((parse_hex(words[3]), parse_hex(words[4])))
    return "0x%08X+0x%08X" % (values[0], values[1])


def load_trace(path):
    raw = path.read_bytes()
    document = json.loads(raw.decode("utf-8-sig"))
    result = document.get("result")
    events = result.get("events") if isinstance(result, dict) else None
    if not isinstance(events, list) or not events:
        raise ValueError(f"{path} does not contain native trace events.")
    if result.get("lastError") != 0 or result.get("droppedEstimate") != 0:
        raise ValueError(f"{path} reports a native trace error or dropped events.")
    base = parse_hex(result.get("unityPlayerBase"))
    return {
        "path": str(path.resolve()),
        "sha256": hashlib.sha256(raw).hexdigest(),
        "base": base,
        "events": events,
    }


def first_nonempty_dirty_update(trace):
    events = trace["events"]
    headers = [event for event in events if event.get("kindId") == KIND_DIRTY_HEADER]
    for header_index, header in enumerate(headers):
        payload = header.get("payload")
        stack = header.get("stack")
        if not isinstance(payload, list) or len(payload) < 5 or \
                not isinstance(stack, list) or len(stack) < 8:
            raise ValueError("Dirty-interaction header has an invalid shape.")
        count = parse_hex(payload[2])
        if count == 0:
            continue
        start = int(header["sequence"])
        end = int(headers[header_index + 1]["sequence"]) \
            if header_index + 1 < len(headers) else 1 << 62
        window = [event for event in events
                  if start < int(event["sequence"]) < end]
        entries = [event for event in window
                   if event.get("kindId") == KIND_DIRTY_ENTRY]
        if len(entries) != count:
            raise ValueError("Dense dirty-entry count does not match its header.")
        return header, window, entries
    raise ValueError("Trace has no non-empty dirty-interaction update.")


def analyze_branch(trace):
    header, window, dirty_entries = first_nonempty_dirty_update(trace)
    expected_return = trace["base"] + UPDATE_STATE_CREATE_MANAGER_RVA
    creates = [event for event in window
               if event.get("kindId") == KIND_CREATE_MANAGER and
               parse_hex(event.get("returnAddress")) == expected_return]
    if not creates:
        raise ValueError("Dirty update contains no updateState createManager calls.")

    event_by_sequence = {int(event["sequence"]): event for event in window}
    allocations = []
    for create in creates:
        allocation = event_by_sequence.get(int(create["sequence"]) + 1)
        if allocation is None or allocation.get("kindId") != KIND_CONTACT_MANAGER:
            raise ValueError("createManager is not followed by its contact-manager receipt.")
        create_self = create.get("self")
        allocation_stack = allocation.get("stack")
        if not isinstance(allocation_stack, list) or len(allocation_stack) < 7 or \
                allocation_stack[6] != create_self:
            raise ValueError("Contact descriptor userData does not equal the SIP pointer.")
        allocation_payload = allocation.get("payload")
        if not isinstance(allocation_payload, list) or not allocation_payload:
            raise ValueError("Contact-manager receipt has no pool index.")
        allocations.append({
            "dirtyPair": pair_key(create.get("stack")),
            "shapeInstancePair": create_self,
            "manager": allocation.get("self"),
            "poolIndex": parse_hex(allocation_payload[0]),
            "createSequence": int(create["sequence"]),
            "allocationSequence": int(allocation["sequence"]),
        })

    create_pairs = [item["dirtyPair"] for item in allocations]
    create_pair_set = set(create_pairs)
    if len(create_pair_set) != len(create_pairs):
        raise ValueError("Dirty-route createManager pair keys are not unique.")

    candidate_vtables = {}
    for entry in dirty_entries:
        payload = entry.get("payload")
        if pair_key(payload) in create_pair_set:
            candidate_vtables[payload[0]] = candidate_vtables.get(payload[0], 0) + 1
    ranked_vtables = sorted(candidate_vtables.items(), key=lambda item: item[1], reverse=True)
    if not ranked_vtables or (len(ranked_vtables) > 1 and
                              ranked_vtables[0][1] == ranked_vtables[1][1]):
        raise ValueError("Could not identify the manager-bearing dirty interaction type.")
    manager_dirty_vtable = ranked_vtables[0][0]

    eligible_entries = []
    for entry in dirty_entries:
        payload = entry.get("payload")
        key = pair_key(payload)
        if key in create_pair_set and payload[0] == manager_dirty_vtable:
            eligible_entries.append({
                "denseIndex": parse_hex(entry["stack"][3]),
                "interaction": entry.get("self"),
                "dirtyPair": key,
            })
    dense_pairs = [item["dirtyPair"] for item in eligible_entries]
    if sorted(dense_pairs) != sorted(create_pairs):
        raise ValueError("Dirty and createManager semantic pair sets do not match.")

    stack = header["stack"]
    payload = header["payload"]
    return {
        "trace": {"path": trace["path"], "sha256": trace["sha256"]},
        "headerSequence": int(header["sequence"]),
        "nphaseCore": header.get("self"),
        "set": stack[0],
        "buffer": stack[1],
        "entries": stack[2],
        "entriesNext": stack[3],
        "hash": stack[4],
        "entriesCapacity": parse_hex(stack[5]),
        "hashSize": parse_hex(stack[6]),
        "loadFactorBits": stack[7],
        "freeList": parse_hex(payload[0]),
        "timestamp": parse_hex(payload[1]),
        "count": parse_hex(payload[2]),
        "ownerScene": payload[3],
        "ownerFlagsByte": parse_hex(payload[4]),
        "ownerGlobalDirtyBitsClear": (parse_hex(payload[4]) & 6) == 0,
        "densePointers": [entry.get("self") for entry in dirty_entries],
        "denseStateByPointer": {entry.get("self"): {
            "dirtyPair": pair_key(entry.get("payload")),
            "rawPrefix": entry.get("payload"),
        } for entry in dirty_entries},
        "eligibleDenseEntries": eligible_entries,
        "managerDirtyVtable": manager_dirty_vtable,
        "denseManagerPairOrder": dense_pairs,
        "createManagerPairOrder": create_pairs,
        "managerOrderMatchesDenseOrder": dense_pairs == create_pairs,
        "allocations": allocations,
        "descriptorCorrelationExact": True,
    }


def compare_branches(original, replay):
    original_pointers = original["densePointers"]
    replay_pointers = replay["densePointers"]
    original_map = {item["dirtyPair"]: item["poolIndex"]
                    for item in original["allocations"]}
    replay_map = {item["dirtyPair"]: item["poolIndex"]
                  for item in replay["allocations"]}
    original_sip_map = {item["dirtyPair"]: item["shapeInstancePair"]
                        for item in original["allocations"]}
    replay_sip_map = {item["dirtyPair"]: item["shapeInstancePair"]
                      for item in replay["allocations"]}
    original_dense_state = original["denseStateByPointer"]
    replay_dense_state = replay["denseStateByPointer"]
    changed_dense_objects = [{
        "interaction": pointer,
        "originalPair": original_dense_state[pointer]["dirtyPair"],
        "replayPair": replay_dense_state[pointer]["dirtyPair"],
        "originalRawPrefix": original_dense_state[pointer]["rawPrefix"],
        "replayRawPrefix": replay_dense_state[pointer]["rawPrefix"],
    } for pointer in original_dense_state if pointer in replay_dense_state and
        original_dense_state[pointer] != replay_dense_state[pointer]]
    changed = [{
        "dirtyPair": key,
        "originalPoolIndex": original_map[key],
        "replayPoolIndex": replay_map[key],
    } for key in original_map if key in replay_map and
        original_map[key] != replay_map[key]]
    checks = {
        "sameNPhaseCore": original["nphaseCore"] == replay["nphaseCore"],
        "sameContainerStorage": all(original[name] == replay[name]
                                    for name in ("set", "buffer", "entries",
                                                 "entriesNext", "hash")),
        "sameContainerShape": all(original[name] == replay[name]
                                  for name in ("entriesCapacity", "hashSize",
                                               "loadFactorBits", "count")),
        "sameOwnerScene": original["ownerScene"] == replay["ownerScene"],
        "ownerGlobalDirtyBitsClear": original["ownerGlobalDirtyBitsClear"] and
                                     replay["ownerGlobalDirtyBitsClear"],
        "sameDensePointerSet": set(original_pointers) == set(replay_pointers),
        "densePointerOrderDiffers": original_pointers != replay_pointers,
        "sameDenseSemanticPairMultiset":
            sorted(value["dirtyPair"] for value in original_dense_state.values()) ==
            sorted(value["dirtyPair"] for value in replay_dense_state.values()),
        "denseObjectContentsDiffer": bool(changed_dense_objects),
        "sameManagerPairSet": set(original_map) == set(replay_map),
        "sameShapeInstancePairSet": set(original_sip_map.values()) ==
                                    set(replay_sip_map.values()),
        "shapeInstancePairContentsDiffer": original_sip_map != replay_sip_map,
        "samePoolSequence": [item["poolIndex"] for item in original["allocations"]] ==
                            [item["poolIndex"] for item in replay["allocations"]],
        "pairToPoolMappingDiffers": bool(changed),
        "originalDescriptorCorrelationExact": original["descriptorCorrelationExact"],
        "replayDescriptorCorrelationExact": replay["descriptorCorrelationExact"],
    }
    return {
        "passed": all(checks.values()),
        "classification": "read-only PhysX dirty-interaction allocation-order proof",
        "checks": checks,
        "denseManagerOrderRelation": {
            "originalMatchesDenseOrder": original["managerOrderMatchesDenseOrder"],
            "replayMatchesDenseOrder": replay["managerOrderMatchesDenseOrder"],
            "qualification": "The r22 entry probe snapshots the dense container at "
                             "updateDirtyInteractions entry. A mismatch means another "
                             "activation/list traversal determines createManager order; "
                             "it is not a trace-integrity failure.",
        },
        "changedDenseObjects": changed_dense_objects,
        "changedDenseObjectCount": len(changed_dense_objects),
        "changedPairMappings": changed,
        "changedPairMappingCount": len(changed),
        "original": original,
        "replay": replay,
        "scope": "The first non-empty NPhaseCore dirty-set snapshot is compared with "
                 "the subsequent ShapeInstancePairLL::createManager order. Every "
                 "manager call is correlated exactly through descriptor userData and "
                 "contact-manager pool index. No game or native physics state is written.",
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--original", type=Path, required=True)
    parser.add_argument("--replay", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = compare_branches(
            analyze_branch(load_trace(args.original)),
            analyze_branch(load_trace(args.replay)),
        )
    except Exception as exc:
        result = {
            "passed": False,
            "classification": "read-only PhysX dirty-interaction allocation-order proof",
            "error": str(exc),
        }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))
    raise SystemExit(0 if result.get("passed") else 1)


if __name__ == "__main__":
    main()
