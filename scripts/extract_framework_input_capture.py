"""Extract one exact advancing-input interval from a framework trace epoch."""
from __future__ import annotations

import argparse
import json
from pathlib import Path

from compare_framework_frames import records
from framework_plate_search import normalize_inputs


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--trace", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--start-exclusive", required=True, type=int)
    parser.add_argument("--end-inclusive", required=True, type=int)
    parser.add_argument("--epoch", type=int, default=0)
    parser.add_argument("--chefs", default="43,44,45,46")
    parser.add_argument("--prefer-last-duplicate", action="store_true",
                        help="For a trace spanning a scene replacement, keep the later conflicting input tag.")
    args = parser.parse_args()
    if args.out.exists():
        parser.error("Output already exists")
    if args.start_exclusive < 0 or args.end_inclusive <= args.start_exclusive:
        parser.error("Use a nonempty positive frame interval")
    if args.epoch < 0:
        parser.error("Epoch must be nonnegative")
    chefs = [int(value) for value in args.chefs.split(",")]
    if not chefs or len(set(chefs)) != len(chefs):
        parser.error("Chefs must be distinct comma-separated integers")

    epoch = 0
    by_frame = {}
    witnesses = {}
    for row, where in records(args.trace):
        if row.get("kind") != "exchange":
            continue
        request = row.get("input", {})
        if request.get("Warp") is not None:
            epoch += 1
            continue
        if epoch != args.epoch or not request.get("Input"):
            continue
        frame = request.get("NextFrame")
        if not isinstance(frame, int) or not args.start_exclusive < frame <= args.end_inclusive:
            continue
        normalized = normalize_inputs(request["Input"], chefs)
        if frame in by_frame:
            if by_frame[frame] != normalized:
                if not args.prefer_last_duplicate:
                    raise ValueError(f"Conflicting advancing input for frame {frame}: {witnesses[frame]} and {where}")
                by_frame[frame] = normalized
                witnesses[frame] = where
            continue
        by_frame[frame] = normalized
        witnesses[frame] = where

    expected = set(range(args.start_exclusive + 1, args.end_inclusive + 1))
    if set(by_frame) != expected:
        missing = sorted(expected - set(by_frame))
        extra = sorted(set(by_frame) - expected)
        raise ValueError(f"Trace interval is incomplete; missing={missing[:20]}, extra={extra[:20]}")
    rows = [
        {"ordinal": frame - args.start_exclusive - 1, "nextFrame": frame, "inputs": by_frame[frame]}
        for frame in sorted(by_frame)
    ]
    report = {
        "trace": str(args.trace.resolve()),
        "epoch": args.epoch,
        "startExclusive": args.start_exclusive,
        "endInclusive": args.end_inclusive,
        "chefs": chefs,
        "validation": "exact-four-pad-frame-coverage",
        "inputs": rows,
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, separators=(",", ":")), encoding="utf-8")
    print(json.dumps({key: value for key, value in report.items() if key != "inputs"}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
