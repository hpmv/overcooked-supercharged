"""Compose contiguous exact four-pad captures into one raw-input request."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from framework_plate_search import to_raw_request


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("captures", nargs="+", type=Path)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument(
        "--request-array",
        action="store_true",
        help="Wrap the request in an array suitable for --prefix-inputs.",
    )
    args = parser.parse_args()

    rows = []
    expected_frame = None
    chef_ids = None
    for capture_path in args.captures:
        capture = json.loads(capture_path.read_text(encoding="utf-8-sig"))
        if capture.get("validation") != "exact-four-pad-frame-coverage":
            raise ValueError(f"Capture is not exact four-pad coverage: {capture_path}")
        current = capture.get("inputs")
        if not isinstance(current, list) or not current:
            raise ValueError(f"Capture has no input rows: {capture_path}")
        for row in current:
            frame = row.get("nextFrame")
            if type(frame) is not int or (expected_frame is not None and frame != expected_frame):
                raise ValueError(f"Capture frames are not contiguous at {capture_path}: {frame}")
            expected_frame = frame + 1
            ids = tuple(sorted(int(value) for value in row.get("inputs", {})))
            if chef_ids is None:
                chef_ids = ids
            elif ids != chef_ids:
                raise ValueError(f"Chef identities changed at frame {frame}")
            copied = dict(row)
            copied["ordinal"] = len(rows)
            rows.append(copied)

    request = to_raw_request(rows, chef_ids)
    result = [request] if args.request_array else request
    args.out.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({
        "out": str(args.out.resolve()),
        "sourceRows": len(rows),
        "payloadFrames": sum(segment["frames"] for segment in request["segments"]),
        "releaseFrames": 2,
        "startNextFrame": rows[0]["nextFrame"],
        "endNextFrame": rows[-1]["nextFrame"],
        "segments": len(request["segments"]),
    }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
