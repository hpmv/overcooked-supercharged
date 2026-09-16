#!/usr/bin/env python3
"""Build the proven Story 1-1 f444->f1048 logical-input suffix.

This intentionally reconstructs input from the original raw-input requests in an
observations JSON file.  It does not consult the controller exchange trace, whose
duplicate frame observations can be ambiguous around pauses.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path


CHEFS = ("43", "44", "45", "46")
BUTTONS = ("Pickup", "Interact", "Dash")
WIRE_BUTTONS = {"Pickup": "pickup", "Interact": "interact", "Dash": "dash"}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def neutral_wire() -> dict[str, dict[str, object]]:
    return {
        chef: {"x": 0.0, "y": 0.0, "pickup": False, "interact": False, "dash": False}
        for chef in CHEFS
    }


def logical_row(
    wire: dict[str, dict[str, object]], previous: dict[str, dict[str, bool]]
) -> dict[str, dict[str, object]]:
    require(set(wire) == set(CHEFS), "Raw frame does not contain exactly chefs 43..46")
    result: dict[str, dict[str, object]] = {}
    for chef in CHEFS:
        pad = wire[chef]
        require(
            all(name in pad for name in ("x", "y", "pickup", "interact", "dash")),
            f"Raw frame for chef {chef} is incomplete",
        )
        value: dict[str, object] = {
            "Pad": {"X": pad["x"], "Y": pad["y"]},
        }
        for button in BUTTONS:
            down = bool(pad[WIRE_BUTTONS[button]])
            was_down = previous[chef][button]
            value[button] = {
                "Down": down,
                "JustPressed": down and not was_down,
                "JustReleased": was_down and not down,
            }
            previous[chef][button] = down
        result[chef] = value
    return result


def expand_request(request: dict[str, object]) -> list[dict[str, dict[str, object]]]:
    require(request.get("command") == "raw-input", "Observation is not a raw-input request")
    expanded: list[dict[str, dict[str, object]]] = []
    for index, segment in enumerate(request.get("segments", [])):
        require(isinstance(segment, dict), f"Segment {index} is not an object")
        frames = segment.get("frames")
        chefs = segment.get("chefs")
        require(isinstance(frames, int) and frames > 0, f"Segment {index} has invalid frames")
        require(isinstance(chefs, dict), f"Segment {index} has invalid chefs")
        for _ in range(frames):
            expanded.append(chefs)
    return expanded


def normalize_recorded_input(value: dict[str, object]) -> dict[str, object]:
    result: dict[str, object] = {
        "Pad": {"X": value["Pad"]["X"], "Y": value["Pad"]["Y"]},
    }
    for button in BUTTONS:
        result[button] = {
            key: bool(value[button][key]) for key in ("Down", "JustPressed", "JustReleased")
        }
    return result


def is_neutral(inputs: dict[str, dict[str, object]]) -> bool:
    return all(
        value["Pad"]["X"] == 0
        and value["Pad"]["Y"] == 0
        and all(not value[button]["Down"] for button in BUTTONS)
        for value in inputs.values()
    )


def validate_edges(rows: list[dict[str, object]]) -> None:
    previous = {chef: {button: False for button in BUTTONS} for chef in CHEFS}
    for ordinal, row in enumerate(rows):
        require(row["ordinal"] == ordinal, f"Noncontiguous ordinal at {ordinal}")
        require(row["nextFrame"] == 445 + ordinal, f"Noncontiguous nextFrame at {ordinal}")
        inputs = row["inputs"]
        require(set(inputs) == set(CHEFS), f"Frame {row['nextFrame']} lacks exact four-pad coverage")
        for chef in CHEFS:
            value = inputs[chef]
            for button in BUTTONS:
                state = value[button]
                down = state["Down"]
                require(
                    state["JustPressed"] == (down and not previous[chef][button]),
                    f"Invalid {button} JustPressed edge at frame {row['nextFrame']} chef {chef}",
                )
                require(
                    state["JustReleased"] == (previous[chef][button] and not down),
                    f"Invalid {button} JustReleased edge at frame {row['nextFrame']} chef {chef}",
                )
                previous[chef][button] = down


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("observations", type=Path)
    parser.add_argument("recording", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    observations = json.loads(args.observations.read_text(encoding="utf-8-sig"))
    recording = json.loads(args.recording.read_text(encoding="utf-8-sig"))
    require(isinstance(observations, list), "Observations root must be a list")

    requests: dict[int, dict[str, object]] = {}
    for prefix in range(8, 15):
        matches = [
            item for item in observations
            if item.get("label") == f"prefix-{prefix}-input"
            and item.get("target") == "controller"
        ]
        require(len(matches) == 1, f"Expected one prefix-{prefix}-input observation")
        requests[prefix] = matches[0]["request"]

    expanded = {prefix: expand_request(request) for prefix, request in requests.items()}
    expected_lengths = {8: 50, 9: 61, 10: 60, 11: 95, 12: 1, 13: 30, 14: 208}
    require(
        {prefix: len(frames) for prefix, frames in expanded.items()} == expected_lengths,
        "Proven raw-input request lengths changed",
    )

    previous = {chef: {button: False for button in BUTTONS} for chef in CHEFS}
    logical: list[dict[str, dict[str, object]]] = []

    # Prefix 8 began from a neutral boundary at f436.  Simulate its consumed
    # first eight payload frames only to establish the exact button edge state;
    # f444 is the checkpoint and therefore not emitted.
    for wire in expanded[8][:8]:
        logical_row(wire, previous)
    for wire in expanded[8][8:]:
        logical.append(logical_row(wire, previous))
    logical.append(logical_row(neutral_wire(), previous))
    logical.append(logical_row(neutral_wire(), previous))

    for prefix in range(9, 15):
        for wire in expanded[prefix]:
            logical.append(logical_row(wire, previous))
        logical.append(logical_row(neutral_wire(), previous))
        logical.append(logical_row(neutral_wire(), previous))

    # The successful probe then advanced 90 ordinary neutral frames.
    for _ in range(90):
        logical.append(logical_row(neutral_wire(), previous))

    require(recording.get("version") == 1, "Unexpected recording version")
    require(
        recording.get("kind") == "supercharged-logical-input-recording",
        "Unexpected recording kind",
    )
    require(recording.get("payloadFrames") == 1 and recording.get("releaseFrames") == 2,
            "Expected one pickup payload plus two release frames")
    recorded_frames = recording.get("frames")
    require(isinstance(recorded_frames, list) and len(recorded_frames) == 3,
            "Expected exactly three recording frames")
    for ordinal, frame in enumerate(recorded_frames):
        require(frame.get("ordinal") == ordinal, "Recording ordinals are not contiguous")
        inputs = frame.get("inputs")
        require(isinstance(inputs, dict) and set(inputs) == set(CHEFS),
                "Recording lacks exact four-pad coverage")
        normalized = {chef: normalize_recorded_input(inputs[chef]) for chef in CHEFS}
        # The recording was made from the same neutral boundary.  Verify its
        # stored edges before accepting it as the suffix tail.
        expected = logical_row(
            {
                chef: {
                    "x": normalized[chef]["Pad"]["X"],
                    "y": normalized[chef]["Pad"]["Y"],
                    "pickup": normalized[chef]["Pickup"]["Down"],
                    "interact": normalized[chef]["Interact"]["Down"],
                    "dash": normalized[chef]["Dash"]["Down"],
                }
                for chef in CHEFS
            },
            previous,
        )
        require(normalized == expected, f"Recording frame {ordinal} has inconsistent button edges")
        logical.append(normalized)

    require(len(logical) == 604, f"Expected 604 rows, got {len(logical)}")
    rows = [
        {"ordinal": ordinal, "nextFrame": 445 + ordinal, "inputs": inputs}
        for ordinal, inputs in enumerate(logical)
    ]
    validate_edges(rows)
    require(rows[-1]["nextFrame"] == 1048, "Capture does not end at f1048")
    require(is_neutral(rows[-2]["inputs"]) and is_neutral(rows[-1]["inputs"]),
            "Capture lacks two terminal neutral frames")

    output = {
        "sourceObservations": str(args.observations.resolve()),
        "sourceRecording": str(args.recording.resolve()),
        "construction": "raw-input-request-boundaries; prefix8 payload 9..50; prefixes9..14; 90 neutral; recording",
        "startExclusive": 444,
        "endInclusive": 1048,
        "chefs": [int(chef) for chef in CHEFS],
        "validation": "exact-four-pad-frame-coverage",
        "inputs": rows,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(output, separators=(",", ":")) + "\n", encoding="utf-8")
    digest = hashlib.sha256(args.output.read_bytes()).hexdigest().upper()
    print(json.dumps({"output": str(args.output.resolve()), "rows": len(rows),
                      "firstFrame": rows[0]["nextFrame"], "lastFrame": rows[-1]["nextFrame"],
                      "sha256": digest}, indent=2))


if __name__ == "__main__":
    main()
