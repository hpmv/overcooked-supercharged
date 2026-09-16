#!/usr/bin/env python3
"""File-only tail audit with one explicitly pinned observer-history projection.

Existing comparisons/traces are never rewritten. This removes exactly the three
complete neutral-disconnect records in manifest.join from the EXPECTED tail only.
It does not renumber later events or remove any other input_release observation.
"""
import argparse
import copy
import gzip
import hashlib
import itertools
import json
from pathlib import Path

import analyze_repro as repro

NAME = "v14ExactThreePinnedNeutralDisconnectRecordsRemovedFromExpectedOnly"
VIEWS = ("gameplayEvents", "nativeEventTrace", "nativeEventSequence", "nativeEventTiming")


def require(value, message):
    if not value:
        raise ValueError(message)


def file_hash(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def canonical_hash(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def project(state, pins):
    events = state["gameEvents"]
    matches = []
    for pin in pins:
        exact = [i for i, event in enumerate(events) if repro.first_diff(event, pin) is None]
        require(len(exact) == 1, "Expected state must contain each complete pinned record exactly once")
        matches.extend(exact)
    require(len(set(matches)) == len(pins), "Pinned records did not identify distinct events")
    return {**state, "gameEvents": [event for i, event in enumerate(events) if i not in matches]}


def tests(pins):
    count = 0
    other = {**pins[0], "index": 999}
    native = {"gameEvents": [*copy.deepcopy(pins), other], "score": 2042, "unknown": True}
    before = canonical_hash(native)
    projected = project(native, pins)
    require(projected["gameEvents"] == [other], "Unpinned input_release must remain"); count += 1
    require(canonical_hash(native) == before, "Projection mutated original state"); count += 1
    require(projected["unknown"] is True and projected["score"] == 2042, "Projection changed another field"); count += 1
    for field, value in (("inputsNeutral", False), ("scoreDelta", 1), ("gameplayFrame", 15522), ("index", 23), ("kind", "delivery")):
        changed = copy.deepcopy(native); changed["gameEvents"][0][field] = value
        try:
            project(changed, pins)
        except ValueError:
            count += 1
        else:
            raise ValueError("Nonmatching pinned event was removed: " + field)
    for changed in ({**native, "gameEvents": native["gameEvents"][1:]}, {**native, "gameEvents": [*native["gameEvents"], pins[0]]}):
        try:
            project(changed, pins)
        except ValueError:
            count += 1
        else:
            raise ValueError("Missing/duplicate pinned event accepted")
    return count


def replay_tail(path, first_line):
    # The existing complete comparison supplies a recorded source line. Skip its
    # prefix bytes without decoding large native states, then independently check
    # every remaining call's frame, request and projection against the whole tail.
    # The entire compressed replay is hashed before and after this bounded audit.
    with gzip.open(path, "rt", encoding="utf-8-sig") as stream:
        for number, line in enumerate(stream, 1):
            if number < first_line:
                continue
            require(len(line) < 8_000_000, "Replay line too large")
            row = json.loads(line)
            if row.get("kind") in ("event", "header"):
                continue
            require(row.get("kind") == "call", "Unexpected replay row")
            response = row["response"]
            require(response.get("ok") is True and isinstance(response.get("state"), dict), "Replay tail call lacks successful native state")
            yield {"line": number, "state": response["state"], "request": row["request"], "inputs": response.get("inputs")}


def main(args):
    out = Path(args.out)
    require(not out.exists(), "Refusing to overwrite an existing proof")
    manifest_path, comparison_path = Path(args.manifest), Path(args.comparison)
    manifest, comparison = json.loads(manifest_path.read_text()), json.loads(comparison_path.read_text())
    pins = manifest["join"]["addedDisconnectObservations"]
    join = manifest["join"]["gameplayFrame"]
    require(join == 15521 and len(pins) == 3 and [p["index"] for p in pins] == [20, 21, 22], "Unexpected exact join record set")
    for pin in pins:
        require(pin["kind"] == "input_release" and pin["reason"] == "controller-disconnected" and pin["inputsNeutral"] is True and pin["gameplayFrame"] == join, "Pinned record is not the specified neutral disconnect")
        require(all(pin[k] == 0 and type(pin[k]) is int for k in ("scoreDelta", "baseScoreDelta", "tipDelta", "deductionDelta", "deliveryDelta")) and pin["scoreApplied"] is False, "Pinned disconnect has a gameplay delta")
    require(manifest["join"]["allOtherStateFieldsIdentical"] is True and manifest["join"]["bothInputSnapshotsNeutral"] is True, "Original join receipt does not attest inert boundary")
    require(comparison["completeSampleAlignment"] and comparison["sameRequestSequence"] and comparison["comparedSamples"] == 16226, "Original complete comparison qualification differs")
    for view in VIEWS:
        c = comparison["comparisons"][view]
        require(c["equalSamples"] == 15524 and c["differentSamples"] == 702 and c["firstDivergence"]["actualSample"]["gameplayFrame"] == join, "Original named view coverage differs: " + view)
    actual_path = Path(args.actual)
    expected_path = Path(args.expected)
    require(actual_path.resolve() == Path(comparison["actual"]["path"]).resolve(), "Replay path differs from original comparison")
    source = manifest["sources"][1]
    require(expected_path.resolve() == Path(source["path"]).resolve() and source["requests"] == 702, "Tail differs from manifest source")
    first_actual = comparison["comparisons"]["nativeEventTrace"]["firstDivergence"]["actualSample"]
    require(first_actual["segment"] == 0 and first_actual["occurrence"] == 1, "Unexpected join occurrence")
    paths = {"manifest": manifest_path, "originalComparison": comparison_path, "expectedTail": expected_path, "actualFullReplay": actual_path,
             "projectionSource": Path(__file__), "baseProjectionSource": Path(repro.__file__)}
    hashes = {name: file_hash(path) for name, path in paths.items()}
    require(hashes["expectedTail"] == source["sha256"] and expected_path.stat().st_size == source["size"], "Expected tail hash/size differs from pinned manifest")
    assertions = tests(pins)
    names = (*VIEWS, "currentRoundRecipeRandom", "requests", "emulatedInputs")
    comparisons = {name: {"equalSamples": 0, "differentSamples": 0, "firstDivergence": None} for name in names}
    raw = {name: {"equalSamples": 0, "differentSamples": 0, "firstDivergence": None} for name in (*VIEWS, "strictState", "physics", "rawPhysicsHashes", "timing")}
    expected_samples = repro.Samples(expected_path)
    count = 0; first = last = None; removed = 0
    expected_request_hash, actual_request_hash = hashlib.sha256(), hashlib.sha256()
    def compare(result, left, right, where):
        diff = repro.first_diff(left, right) if result["firstDivergence"] is None else (None if repro.values_equal(left, right) else True)
        result["equalSamples" if diff is None else "differentSamples"] += 1
        if diff is not None and result["firstDivergence"] is None:
            result["firstDivergence"] = {**where, **diff}
    for e, a in itertools.zip_longest(iter(expected_samples), replay_tail(actual_path, first_actual["line"])):
        require(e is not None and a is not None, "Tail has an unmatched expected or actual sample")
        require(count < 702, "Tail exceeds pinned sample count")
        es, ass = e["state"], a["state"]
        frame = es["gameplayFrame"]
        require(frame == ass["gameplayFrame"] and e["key"][0] == 0, "Tail frame/segment alignment differs")
        if count == 0:
            require(frame == join and a["line"] == first_actual["line"], "First tail sample differs from pinned boundary")
            first = {"frame": frame, "expectedLine": e["line"], "actualLine": a["line"], "fullTraceOccurrence": 1}
        else:
            require(frame - last["frame"] in (0, 1), "Tail has a frame gap or rewind")
        require(e["request"]["command"] in ("inspect", "step"), "Unexpected authored-tail command")
        if e["request"]["command"] == "step":
            require(e["request"]["steps"] == 1, "Nonunit tail step")
        for pad in e["inputs"]:
            require(pad["x"] == 0 and pad["y"] == 0 and all(pad[k] is False for k in ("pickup", "use", "dash")), "Expected tail input snapshot is not neutral")
        require(not any(repro.first_diff(ev, pin) is None for ev in ass["gameEvents"] for pin in pins), "Actual trace unexpectedly contains a complete expected-only pinned record")
        projected = project(es, pins); removed += len(es["gameEvents"]) - len(projected["gameEvents"])
        pv, av, rv = repro.views(projected), repro.views(ass), repro.views(es)
        where = {"tailSample": count, "gameplayFrame": frame, "expectedLine": e["line"], "actualLine": a["line"]}
        for name in (*VIEWS, "currentRoundRecipeRandom"):
            compare(comparisons[name], pv[name], av[name], where)
        for name in raw:
            compare(raw[name], rv[name], av[name], where)
        for name, field in (("requests", "request"), ("emulatedInputs", "inputs")):
            compare(comparisons[name], e[field], a[field], where)
        for h, request in ((expected_request_hash, e["request"]), (actual_request_hash, a["request"])):
            h.update(json.dumps(request, sort_keys=True, separators=(",", ":")).encode() + b"\n")
        last = {"frame": frame, "expectedLine": e["line"], "actualLine": a["line"]}; count += 1
    require(count == 702 and last["frame"] == 16221 and removed == 2106, "Complete702-sample exact-three-removal coverage missing")
    require(all(file_hash(path) == hashes[name] for name, path in paths.items()), "An input artifact changed during read-only analysis")
    success = all(value["differentSamples"] == 0 for value in comparisons.values())
    report = {"format": "oc2-explicit-neutral-join-observer-projection", "version": 1, "projectionName": NAME, "ok": success,
        "projectionDefinition": "Remove exactly the three complete raw records pinned in manifest.join.addedDisconnectObservations from expected gameEvents before applying the existing named analyzer views. Do not remove actual records, other input_release records, event fields, subsequent indices, food, clocks or native state.",
        "comparedTailSamples": count, "uniquePinnedRecords": 3, "recordOccurrencesRemoved": removed, "firstSample": first, "lastSample": last,
        "pinnedRecords": pins, "pinnedRecordsCanonicalSha256": canonical_hash(pins), "comparisonsUnderNamedProjection": comparisons,
        "originalUnprojectedTailComparisons": raw, "retainedFullComparison": str(comparison_path.resolve()),
        "retainedFullComparisonSummary": {name: comparison["comparisons"][name] for name in ("strictState", "physics", "rawPhysicsHashes", "timing", *VIEWS)},
        "expectedTailCanonicalRequestsSha256": expected_request_hash.hexdigest(), "actualTailCanonicalRequestsSha256": actual_request_hash.hexdigest(),
        "fixtureAssertions": assertions, "consumedArtifacts": {name: {"path": str(path.resolve()), "sha256": hashes[name], "bytes": path.stat().st_size} for name, path in paths.items()},
        "qualification": "This explicit observer-history projection covers every702 tail sample. It does not make the original strict/raw comparisons equal, establish bitwise physics identity, upgrade the failed planner trial, or establish the5000-point goal."}
    with out.open("x", encoding="utf-8") as stream:
        json.dump(report, stream, indent=2); stream.write("\n")
    print(json.dumps({"ok": success, "projectionName": NAME, "samples": count, "comparisons": comparisons, "output": str(out), "outputSha256": file_hash(out)}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", default="artifacts/v14-completed-movie-manifest.json")
    parser.add_argument("--comparison", default="artifacts/v14-completed-replay-a-comparison.json")
    parser.add_argument("--expected", default="artifacts/native-round-v14-neutral-tail.jsonl.gz")
    parser.add_argument("--actual", default="artifacts/v14-completed-replay-a.jsonl.gz")
    parser.add_argument("--out", required=True)
    main(parser.parse_args())
