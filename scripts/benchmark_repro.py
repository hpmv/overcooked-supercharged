#!/usr/bin/env python3
"""Check exact analyzer equivalence and benchmark a bounded trace prefix.

The baseline is a saved copy of analyze_repro.py from before optimization.
Only the optional --out report is written; input traces and modules are read-only.
"""

import argparse
import copy
import gc
import hashlib
import importlib.util
import json
from pathlib import Path
import random
import time
import tracemalloc


def load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def exact(value):
    # NaN cases are comparator unit fixtures, not writable production reports.
    return json.dumps(value, sort_keys=True, ensure_ascii=False, allow_nan=True)


def check_equal(expected, actual, label):
    if exact(expected) != exact(actual):
        raise AssertionError(label + " changed comparison output: " + exact({"expected": expected, "actual": actual})[:2400])


def fixtures(baseline, current):
    cases = [
        (True, 1), (False, 0), (True, 1.0), (1, 1.0), (0.0, -0.0),
        ({"a": [True]}, {"a": [1]}), ({"a": [1]}, {"a": [1.0]}),
        ({"a": None}, {}), ({"a": {}}, {"a": []}), ([], [None]),
        ({"b": 1, "a": [2, 3]}, {"a": [2.0, 3], "b": 1.0}),
        ({"a": "true", "b": True}, {"a": "true", "b": 1}),
        ({"a": [float("nan")]}, {"a": [float("nan")]}),
        ({"a": float("inf")}, {"a": float("inf")}),
        ({"a": float("inf")}, {"a": float("-inf")}),
        ({"a": 2 ** 80}, {"a": float(2 ** 80)}),
        ({"a": 2 ** 80 + 1}, {"a": float(2 ** 80 + 1)}),
        ({"a": "\u00e9\n\u2603"}, {"a": "\u00e9\n\u2603"}),
        ({"physicsHash": "00000000" * 13}, {"physicsHash": "3f800000" + "00000000" * 12}),
        ({"physicsHash": "invalid"}, {"physicsHash": "different"}),
        ({"a": "x" * 1000}, {}),
    ]
    shared_nan = float("nan")
    cases.append(([shared_nan], [shared_nan]))
    rng = random.Random(19770531)
    leaves = [None, True, False, 0, 1, -1, 1.0, -0.0, .1, "", "true", "a", 2 ** 65]

    def tree(depth=0):
        if depth >= 3 or rng.random() < .45:
            return rng.choice(leaves)
        if rng.random() < .5:
            return [tree(depth + 1) for _ in range(rng.randrange(5))]
        return {chr(97 + i): tree(depth + 1) for i in range(rng.randrange(5))}

    for _ in range(500):
        value = tree()
        cases.append((value, copy.deepcopy(value)))
        cases.append((value, tree()))
    for index, (left, right) in enumerate(cases):
        expected = baseline.first_diff(left, right)
        check_equal(expected, current.first_diff(left, right), "first_diff fixture " + str(index))
        if current.values_equal(left, right) is not (expected is None):
            raise AssertionError("values_equal fixture " + str(index))
        cache = {}
        check_equal(expected, current.first_diff(left, right, _equal_pairs=cache), "cached fixture " + str(index))
        check_equal(expected, current.first_diff(left, right, _equal_pairs=cache), "reused cache fixture " + str(index))
        # The same proven-equal subtree can appear in multiple named views.
        check_equal(baseline.first_diff({"nested": left}, {"nested": right}),
                    current.first_diff({"nested": left}, {"nested": right}, _equal_pairs=cache), "nested cache fixture " + str(index))

    state = {
        "unknown": {"nested": [{"entityId": 11, "id": 11, "heldEntityId": True}, {"food": {"progress": .3, "id": 11}}]},
        "entities": [{"id": 11, "observedOrdinal": 9, "contents": [{"progress": .4, "id": 11}]},
                     {"name": "missing-id", "attachedEntityId": 12}, {"id": None}],
        "chefs": [{"entityId": 12, "heldEntityId": 11, "position": {"x": 1, "z": 2}}],
        "currentRecipeRoundInstanceOrdinal": 3,
        "currentRecipeDraws": [{"roundDrawIndex": 0, "roundInstanceOrdinal": 3, "index": 7,
                                "generator": "RoundData", "recipeIds": [158500], "frequencies": [1],
                                "before": "s0=1", "after": "s0=2", "isolated": True}],
    }
    for mapping in ({}, {11: "anchor:11", 12: "anchor:12"}, {1: "bool-key-legacy"}, {None: "missing-id-legacy"}):
        untouched = exact(state)
        expected = baseline.canonicalize(state, mapping)
        actual = current.canonicalize(state, mapping)
        check_equal(expected, actual, "canonical fixture " + repr(mapping))
        check_equal(baseline.views(expected), current.views(actual), "canonical views " + repr(mapping))
        if exact(state) != untouched:
            raise AssertionError("canonicalize mutated its input")
    for index in range(100):
        value = {"entities": [{"id": i, "extra": tree(), "heldEntityId": i + 1} for i in range(5)],
                 "unknown": tree()}
        untouched = exact(value)
        check_equal(baseline.canonicalize(value, {2: "two", 4: "four"}),
                    current.canonicalize(value, {2: "two", 4: "four"}), "canonical fuzz " + str(index))
        check_equal(baseline.without_progress(value), current.without_progress(value), "food tree " + str(index))
        check_equal(baseline.views(value), current.views(value), "views fuzz " + str(index))
        if exact(value) != untouched:
            raise AssertionError("projection mutated its input")
    return {"comparisonCases": len(cases), "canonicalCases": 104, "inputMutationChecks": 104}


def bounded(module, limit):
    original = module.entries

    def entries(path):
        calls = 0
        for item in original(path):
            yield item
            if isinstance(item[1], dict) and "response" in item[1]:
                calls += 1
                if calls >= limit:
                    break
    module.entries = entries
    return original


def run(module, expected, actual, limit, canonical, memory=False):
    original = bounded(module, limit)
    gc.collect()
    if memory:
        tracemalloc.start()
    start = time.perf_counter()
    try:
        report = module.analyze(expected, actual, canonical)
        elapsed = time.perf_counter() - start
        peak = tracemalloc.get_traced_memory()[1] if memory else None
    finally:
        module.entries = original
        if memory:
            tracemalloc.stop()
    return report, elapsed, peak


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--current", type=Path, default=Path(__file__).with_name("analyze_repro.py"))
    parser.add_argument("--expected", type=Path, required=True)
    parser.add_argument("--actual", type=Path, required=True)
    parser.add_argument("--calls", type=int, default=120)
    parser.add_argument("--memory-calls", type=int, default=16)
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()
    if args.calls < 1 or args.memory_calls < 1:
        parser.error("Prefix limits must be positive")
    if args.out and args.out.resolve() in {p.resolve() for p in [args.baseline, args.current, args.expected, args.actual]}:
        parser.error("Output must not overwrite an input")
    baseline = load(args.baseline, "baseline_repro")
    current = load(args.current, "optimized_repro")
    result = {"fixtures": fixtures(baseline, current), "calls": args.calls, "benchmarks": {},
              "sourceHashes": {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in [args.baseline, args.current]}}
    print("Comparator and structural-sharing fixtures passed", flush=True)
    for canonical in (False, True):
        before, before_time, _ = run(baseline, args.expected, args.actual, args.calls, canonical)
        after, after_time, _ = run(current, args.expected, args.actual, args.calls, canonical)
        check_equal(before, after, "complete report canonical=" + str(canonical))
        label = "canonical" if canonical else "raw"
        result["benchmarks"][label] = {"beforeSeconds": before_time, "afterSeconds": after_time,
                                      "speedup": before_time / after_time, "reportExactlyEqual": True,
                                      "comparedSamples": after["comparedSamples"]}
        print(label + ": " + exact(result["benchmarks"][label]), flush=True)
    _, _, before_peak = run(baseline, args.expected, args.actual, args.memory_calls, True, True)
    _, _, after_peak = run(current, args.expected, args.actual, args.memory_calls, True, True)
    result["memory"] = {"calls": args.memory_calls, "beforePeakBytes": before_peak, "afterPeakBytes": after_peak}
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(result, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2, allow_nan=False), flush=True)


if __name__ == "__main__":
    main()
