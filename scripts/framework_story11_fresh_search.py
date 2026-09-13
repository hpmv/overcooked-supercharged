"""Operator-run Story11 preparation search using five ordinary fresh level loads.

Four native preparation candidates are measured once each; the fastest completed
candidate is replayed from a fifth seed0 load using its recorded four-pad inputs.
This is same-process preparation evidence, not rewind parity or a full-round run.
Importing this module makes no connections. The installed level-session module
owns the authorized immediate150s timer policy; core Carnival restart is unused.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import time

from compare_framework_frames import first_difference
from framework_evidence_journal import EvidenceJournal
from framework_native_search import gameplay_round, native_clock_state
from framework_plate_search import AdvancingTrace, input_report, neutral, require_empty_search_graph, to_raw_request
from framework_rpc import Client, ControllerClient
from framework_story11_search import Runner, candidates, staged_proof, require_timer_policy, select_trial, require


def write(path, value):
    Path(path).write_text(json.dumps(value, indent=2), encoding="utf8")


def counters(bridge):
    result = {"clockRestores": bridge.get("authoringClockRestores"),
              "checkpointRestores": bridge.get("nativeCheckpoints", {}).get("restoreAttempts")}
    require(all(type(v) is int and v >= 0 for v in result.values()), "Missing native authoring counters")
    return result


def loaded_epoch(bridge, previous):
    require(not bridge.get("lastError"), "Native level load reported an error")
    epoch = bridge.get("readyUnityFrame")
    require(type(epoch) is int and epoch > previous, "No new native load completion epoch")
    require(bridge.get("loadComplete") is True and bridge.get("loading") is False
            and bridge.get("paused") is True and bridge.get("inputBlocked") is True,
            "Fresh kitchen must be loaded, paused and fenced")
    return epoch


def native_policy(observation):
    bridge = observation.receipt["bridge"]
    require(observation.state.get("preventInvalidState") is False
            and observation.state.get("freshLevelLoadObserved") is True
            and observation.state.get("needsFreshLevelBaseline") is False,
            "Host lacks a fresh observed baseline or has state correction enabled")
    require(bridge.get("screenWidth") == 1280 and bridge.get("screenHeight") == 720
            and bridge.get("fullScreen") is False and bridge.get("runInBackground") is True,
            "Native windowed1280x720/background policy is not active")
    require(bridge.get("captureFramerate") == 60 and bridge.get("fixedDeltaTime") == .02,
            "Unexpected native time-step configuration")
    rng = observation.round.get("recipeRandom")
    require(isinstance(rng, dict) and rng.get("seed") == 0 and rng.get("authoringWarpCount") == 0,
            "Missing seed0 native scripted-round evidence or an authoring warp occurred")
    require(rng.get("generator") == "ScriptedRoundData.GetNextRecipe" and rng.get("isolatedPerRound") is True,
            "Preparation must retain the native scripted/weighted recipe generator")
    require(observation.round.get("timerSuppressed") is False, "Native timer stopped during preparation")
    require(all(value == 0 for value in observation.round["ledger"].values()),
            "Preparation search requires a zero native score/delivery ledger")


def task_definition(case):
    # Start clocks are measured anew, while all native station/chef/plate selectors
    # and the original ordinary graph must identify the same candidate.
    return {k: v for k, v in case.items() if k not in ("startFrame", "startElapsed")}


def same_round(observation, epoch, original_counters):
    bridge = observation.receipt["bridge"]
    require(bridge.get("readyUnityFrame") == epoch and bridge.get("loadComplete") is True
            and bridge.get("loading") is False, "Candidate crossed its actual native load epoch")
    require(counters(bridge) == original_counters, "Unexpected native authoring restoration during fresh search")
    native_policy(observation)


def fresh_goal_comparison(expected, observed, expected_observation, observation, expected_rows, rows):
    """Named goal projection; raw native identities/private clocks remain separate."""
    fields = ("achieved", "rawPath", "rawComposition", "rawWorkable", "plate", "board", "frames", "nativeSeconds")
    differences = {
        "goal": first_difference({k: expected[k] for k in fields}, {k: observed[k] for k in fields}),
        "inputs": first_difference(expected_rows, rows),
        "nativeRound": first_difference(gameplay_round(expected_observation.round), gameplay_round(observation.round)),
    }
    return {"passed": all(d is None for d in differences.values()), "firstDifferences": differences,
            "originalRawId": expected["rawId"], "replayRawId": observed["rawId"],
            "projection": "fresh-story11-preparation-goal-v1",
            "scope": "Exact native raw-workable goal/path, plate/board, frames, elapsed seconds, orders/ledger and emitted inputs. Raw IDs, body incarnations and absolute private clocks are retained outside this projection. No full endpoint or uninterrupted-run parity claim."}


def raw_differences(left, right):
    return {"hostEntities": first_difference(left.state["entities"], right.state["entities"]),
            "nativePhysics": first_difference(left.receipt["bridge"].get("nativePhysics"), right.receipt["bridge"].get("nativePhysics")),
            "nativeFood": first_difference(left.receipt["detail"], right.receipt["detail"]),
            "privateClocks": first_difference(native_clock_state(left.receipt["bridge"]), native_clock_state(right.receipt["bridge"])),
            "scope": "Unmodified first differences only; full raw receipts are in the observation journal. These differences are not waived or labeled full-state equality."}


class FreshRunner(Runner):
    def __init__(self, args, bridge, host, journal):
        super().__init__(args, bridge, host, journal)
        self.original_counters = None
        self.summary.update(classification="Same-process fresh native Story11 preparation search and selected exact-input replay",
                            searchCompleted=False, replayCompleted=False, freshLoads=[], seed=0,
                            warmupFrames=30, candidateCount=4, authoringWarpRequests=0,
                            optimizationScope="One trial per candidate: raw ingredient fetch-to-board plus parallel clean-plate pickup. No delivery or full-round score claim.")

    def execute(self, request, label, start):
        cursor = AdvancingTrace(self.args.trace)
        try:
            return super().execute(request, label, start)
        except Exception as error:
            # The stable Runner writes exact coverage when the host settles.
            # Also retain unqualified raw rows when it fails before that point,
            # including during warmup or the final fixed-input replay.
            evidence = {"validation": "failure-only-unqualified", "startFrame": start.frame,
                        "trace": str(self.args.trace), "error": str(error)}
            try:
                cursor.read()
            except Exception as read_error:
                evidence["readError"] = str(read_error)
            evidence.update(rawExchanges=cursor.rows, traceByteOffset=cursor.offset,
                            pausedBlocksSkipped=cursor.paused_blocks_skipped)
            write(self.args.out/(label+"-failure-inputs.json"), evidence)
            raise

    def fresh(self, label):
        fenced = self.call("bridge", {"command": "pause"}, label+"-before-load-fence")["bridge"]
        require(fenced.get("inputBlocked") is True, "Native loading needs the neutral input fence")
        previous = fenced.get("readyUnityFrame")
        require(type(previous) is int and previous >= 0 and not fenced.get("loading"),
                "Start from an existing completed native kitchen; module load is not a process bootstrap")
        if self.original_counters is None:
            self.original_counters = counters(fenced)
            self.summary["initialAuthoringCounters"] = self.original_counters
            self.summary["frameworkAssembly"] = fenced.get("frameworkAssembly")
        require(counters(fenced) == self.original_counters, "Unexpected authoring activity between fresh candidates")
        # Do not erase old live spawn claims. The native load retires them first;
        # its neutral fence prevents an old graph driving accepted chef inputs.
        self.call("bridge", {"command": "hot-call", "slot": "level-session",
                             "operation": "load-main-1-1", "args": {"seed": 0}}, label+"-load")
        # Keep this exact owning bridge connection open during the async native
        # transition. Do not hot-call another module until core FinishLoad.
        deadline = time.monotonic()+self.args.load_timeout
        while True:
            bridge = self.call("bridge", {"command": "status"}, label+"-load-status")["bridge"]
            require(not bridge.get("lastError"), "Native Story11 load failed: "+str(bridge.get("lastError")))
            if bridge.get("loadComplete") is True and not bridge.get("loading"):
                epoch = loaded_epoch(bridge, previous)
                break
            if time.monotonic() >= deadline:
                raise TimeoutError("Native Story11 load did not complete within its bounded wait")
            time.sleep(.1)
        self.settled(label+"-fresh-host")
        self.call("controller", {"command": "actions-clear", "all": True}, label+"-fresh-graph-clear")
        initial = self.observe(label+"-fresh")
        same_round(initial, epoch, self.original_counters)
        require_empty_search_graph(initial.state)
        require(0 <= initial.frame <= 2 and math.isfinite(initial.round["elapsed"])
                and 0 <= initial.round["elapsed"] <= .1, "Fresh native pause already advanced beyond the load boundary")
        require(all(len(e["path"]) == 1 for e in initial.entities.values())
                and all(initial.on(c) is None for c in initial.chefs), "Fresh load retained spawned food or a held object")
        timer = self.call("bridge", {"command": "hot-call", "slot": "level-session",
                                    "operation": "status", "args": {}}, label+"-timer-policy")
        require(timer.get("detail", {}).get("result", {}).get("seed") == 0, "Level-session seed is not0")
        policy = require_timer_policy(timer, initial.receipt)
        self.call("bridge", {"command": "arm"}, label+"-arm")
        base, rows = self.execute({"command": "step", "frames": 30}, label+"-warmup", initial)
        same_round(base, epoch, self.original_counters)
        require_empty_search_graph(base.state)
        require(base.frame-initial.frame == 30 and len(rows) == 30 and all(neutral(r, True) for r in rows),
                "Warmup must contain exactly30 observed neutral four-chef frames")
        self.summary["freshLoads"].append({"id": label, "readyUnityFrame": epoch,
            "priorReadyUnityFrame": previous, "initialFrame": initial.frame, "baseFrame": base.frame,
            "initialElapsed": initial.round["elapsed"], "baseElapsed": base.round["elapsed"],
            "timerPolicy": policy, "warmupInputs": input_report(rows), "recipeRandom": base.round["recipeRandom"]})
        self.save()
        return base, epoch

    def attempt(self, case, base, epoch, label):
        trial = {"id": case["id"], "eligible": False, "readyUnityFrame": epoch,
                 "request": case["request"], "startFrame": base.frame, "startElapsed": base.round["elapsed"]}
        try:
            observed, rows = self.execute(case["request"], label, base)
            same_round(observed, epoch, self.original_counters)
            trial.update(input=input_report(rows), goal=staged_proof(observed, case))
            require(observed.state.get("typedActions", {}).get("outcome") == "complete", "Native preparation graph did not complete")
            trial["eligible"] = True
            return trial, (case, base, observed, rows)
        except Exception as error:
            trial["error"] = str(error)
            self.call("bridge", {"command": "pause"}, label+"-failure-fence")
            return trial, None

    def run(self):
        definitions = None
        first_base = None
        history = {}
        for index in range(4):
            base, epoch = self.fresh(f"trial-{index}")
            cases = candidates(base)
            require(len(cases) == 4 and len({c["id"] for c in cases}) == 4, "Expected exactly four observed preparation candidates")
            current = [task_definition(c) for c in cases]
            if definitions is None:
                definitions, first_base = current, base
                write(self.args.out/"cases.json", cases)
            require(current == definitions, "Fresh native load changed a candidate's actual roles/order/ordinary graph")
            require(base.frame == first_base.frame and base.round["elapsed"] == first_base.round["elapsed"],
                    "Fresh candidates did not begin at equal observed native frame/elapsed boundaries")
            write(self.args.out/f"trial-{index}-baseline-raw-differences.json", raw_differences(first_base, base))
            trial, record = self.attempt(cases[index], base, epoch, f"trial-{index}-candidate")
            self.summary["trials"].append(trial)
            if record is not None:
                history[trial["id"]] = record
            self.save()
        selected = select_trial(self.summary["trials"])
        self.summary.update(searchCompleted=True, selected=selected["id"], selectedGoal=selected["goal"])
        self.save()
        case, original_base, original, rows = history[selected["id"]]
        raw = to_raw_request(rows, original_base.chefs)
        write(self.args.out/"selected-raw-request.json", raw)
        base, epoch = self.fresh("selected-replay")
        repeated_case = next((c for c in candidates(base) if c["id"] == case["id"]), None)
        require(repeated_case is not None and task_definition(case) == task_definition(repeated_case), "Replay fresh load changed the selected task")
        require(base.frame == original_base.frame and base.round["elapsed"] == original_base.round["elapsed"],
                "Selected replay did not start at the same frame/native elapsed boundary")
        write(self.args.out/"selected-baseline-raw-differences.json", raw_differences(original_base, base))
        repeated, repeated_rows = self.execute(raw, "selected-fixed-input", base)
        same_round(repeated, epoch, self.original_counters)
        require(repeated.state.get("rawInput", {}).get("outcome") == "complete", "Selected exact-input replay did not complete")
        goal = staged_proof(repeated, repeated_case)
        # The next ordinary delivery runner resumes the actual fifth round, not
        # the historical winning trial. Preserve its freshly observed start
        # clocks and one-element candidate index without replacing cases.json.
        resume_path = self.args.out/"selected-replay-cases.json"
        write(resume_path, [repeated_case])
        self.summary["stagedResume"] = {"cases": str(resume_path.resolve()), "candidate": 0,
            "casesSha256": hashlib.sha256(resume_path.read_bytes()).hexdigest(),
            "readyUnityFrame": epoch, "frame": repeated.frame, "nativeGoal": goal,
            "inputOutcome": repeated.state["rawInput"]["outcome"],
            "scope": "Observed fresh replay is staged for ordinary delivery; resume must independently revalidate its same live native goal."}
        comparison = fresh_goal_comparison(selected["goal"], goal, original, repeated, rows, repeated_rows)
        write(self.args.out/"selected-endpoint-raw-differences.json", raw_differences(original, repeated))
        self.summary.update(replayCompleted=True, selectedFixedInputComparison=comparison,
                            finalAuthoringCounters=counters(repeated.receipt["bridge"]))
        self.save()
        require(comparison["passed"], "Selected fresh-start inputs did not reproduce the exact named native preparation goal/time")
        self.summary["passed"] = True
        self.save()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--trace", type=Path, required=True)
    parser.add_argument("--bridge-port", type=int, default=17636)
    parser.add_argument("--controller-port", type=int, default=17637)
    parser.add_argument("--load-timeout", type=float, default=165)
    args = parser.parse_args()
    if not 10 <= args.load_timeout <= 180 or not args.trace.is_file():
        parser.error("Require an existing owned-host trace and a10..180-second load timeout")
    args.scene, args.search = "s_sushi_1_1", True
    args.out.mkdir(parents=True, exist_ok=False)
    bridge = host = runner = None
    journal = EvidenceJournal(args.out/"observations.jsonl")
    failure = None
    try:
        bridge, host = Client(args.bridge_port), ControllerClient(args.controller_port)
        runner = FreshRunner(args, bridge, host, journal)
        runner.summary["sourceSha256"] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in
            (Path(__file__), Path(__file__).with_name("framework_story11_search.py"),
             Path(__file__).with_name("framework_story11_planner.py"), Path(__file__).with_name("framework_story11_registry.py"),
             Path(__file__).with_name("framework_plate_search.py"))}
        runner.run()
    except Exception as error:
        failure = str(error)
        if runner:
            runner.summary.update(passed=False, error=failure)
    finally:
        if bridge:
            try:
                if runner:
                    runner.call("bridge", {"command": "pause"}, "finally-pause")
                else:
                    bridge.call({"command": "pause"})
            except Exception as error:
                failure = str(error)
                if runner:
                    runner.summary.update(passed=False, pauseError=failure)
        for client in (bridge, host):
            if client:
                client.close()
        journal.finalize(args.out/"observations.json")
        if runner:
            runner.save()
            print(json.dumps(runner.summary, indent=2))
        else:
            write(args.out/"summary.json", {"passed": False, "error": failure, "observationEvidence": journal.describe()})
    return 0 if runner and runner.summary["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
