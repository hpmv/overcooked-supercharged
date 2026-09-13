"""Operator-run Story1-1 preparation search and first native delivery.

No connection is made on import. Default mode makes one ordinary first-delivery
attempt. --search evaluates real preparation candidates from a verified native
checkpoint, repeats the selected exact inputs, then commits to delivery. Search
cost is preparation duration, not a claimed full-round or full-delivery optimum.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
from pathlib import Path
import time

from framework_rpc import Client, ControllerClient
from framework_evidence_journal import EvidenceJournal
from framework_pause_boundary import observe_settled_pause, PauseBoundaryError
from framework_plate_search import (AdvancingTrace, capture_input_evidence, input_report,
    to_raw_request, settled_status, search_artifact_name, require_empty_search_graph)
from framework_native_search import gameplay_round, native_clock_state
from compare_framework_frames import first_difference
from framework_story11_planner import Observation, DeliveryPlanner, candidates, staged_proof, require, Blocked, RECIPES
from framework_story11_compare import compare_boundary
from framework_story11_registry import RegistryEvidence, world_proof, epoch


def require_timer_policy(response, receipt):
    result = response.get("detail", {}).get("result", {})
    selected = result.get("selected", {}); policy = result.get("timerPolicy", {})
    require(result.get("stage") == "complete" and not result.get("pending") and not result.get("error")
            and result.get("scene") == "s_sushi_1_1" and result.get("dlc") == -1,
            "Native level-session module is not the completed main-map Story1-1 session")
    require(selected.get("Config") == "Sushi_1_1S_4P" and selected.get("Scene") == "s_sushi_1_1" and selected.get("Players") == 4,
            "Native selected scene/config does not identify four-player Story1-1")
    require(policy.get("installed") is True and policy.get("immediateStory11Timer") is True,
            "User-authorized immediate timer policy is not installed")
    rows = policy.get("receipts") or []
    starts = [i for i,r in enumerate(rows) if r.get("phase") == "before-native-load"]
    require(bool(starts), "Missing native timer policy load receipt")
    current = rows[starts[-1]:]
    require({"before-native-load", "ServerCampaignMode.Begin-prefix", "ClientCampaignMode.Begin-prefix"} <= {r.get("phase") for r in current},
            "Both native campaign mode begin boundaries were not observed")
    require(all(r.get("config") == "Sushi_1_1S_4P" and r.get("original") == 1 and r.get("after") == 0
                and r.get("nativeDurationSeconds") == 150 and r.get("assetFileWritten") is False for r in current),
            "Native immediate timer instrumentation receipt changed another configuration/value")
    native = receipt["bridge"]["nativeRound"]
    require(native.get("timerSuppressed") is False and native.get("timeLimit") == 150 and native.get("configuredDuration") == 150,
            "Live native timer does not match its declared immediate150s policy")
    return {"selected": selected, "receipts": current, "nativeElapsed": native["elapsed"], "scope": "User-authorized removal of the pre-first-delivery timer exemption"}


def select_trial(trials):
    eligible = [t for t in trials if t.get("eligible")]
    require(bool(eligible), "No native candidate completed the preparation goal")
    return min(eligible, key=lambda t: (t["goal"]["nativeSeconds"], t["goal"]["frames"], t["id"]))


def retained_staged_source(path, case, observed):
    journal=Path(path).parent/'observations.jsonl'
    require(journal.exists(), 'Prepared resume requires the original append-only staged observation journal')
    checksum=hashlib.sha256();state=None;found={}
    with journal.open('rb') as source:
        for line in source:
            checksum.update(line);row=json.loads(line)
            if row.get('target')=='controller' and row.get('request',{}).get('command')=='inspect':
                state=row.get('response')
            if row.get('target')!='settled-pause-observation' or state is None:
                continue
            receipt=row.get('response') or {}
            if epoch(receipt)!=epoch(observed.receipt) or state.get('frame',0)>=observed.frame:
                continue
            try:
                old=Observation(state,receipt,observed.receipt['bridge']['session']['scene'])
                proof=staged_proof(old,case)
            except Blocked:
                continue
            found[(proof['rawId'],tuple(proof['rawPath']))]=proof
    require(len(found)==1,'Prepared resume lacks a unique same-session prior staged source proof')
    return next(iter(found.values())),{'path':str(journal.resolve()),'sha256':checksum.hexdigest()}


def resume_case(path, index, observed, allow_prepared=False):
    raw=Path(path).read_bytes();cases=json.loads(raw.decode("utf-8-sig"))
    require(isinstance(cases,list) and 0<=index<len(cases),"Retained staged case selection is absent")
    case=cases[index];spec=RECIPES.get(case.get("head",{}).get("recipe"))
    require(spec is not None and case["spec"].get("prepared")==spec["prepared"],"Retained case is not the same supported native recipe")
    require(any(o["id"]==case["head"]["id"] and o["recipeId"]==case["head"]["recipeId"]
                and o["baseValue"]==case["head"]["baseValue"] for o in observed.round["orders"]),"Retained staged order is no longer active")
    typed=observed.state.get("typedActions") or {};raw_input=observed.state.get("rawInput") or {}
    require(not typed.get('active') and not raw_input.get('active') and
            (typed.get('outcome')=='complete' or raw_input.get('outcome')=='complete'),
            "Resume requires completed typed preparation or exact raw replay")
    item=observed.on(case['board'])
    if allow_prepared and observed.is_food(item,case['spec']['prepared']):
        prepared_path=observed.entities[item]['path']
        require(len(prepared_path)==3 and prepared_path[0]==case['crate'], 'Prepared resume lacks its exact crate/raw/child path')
        raw_path=prepared_path[:-1]
        prior,prior_file=retained_staged_source(path,case,observed)
        raw_id=prior['rawId'];meta=observed.registry[raw_id]
        require(raw_id not in observed.entities and raw_path==prior['rawPath'],
                'Prepared resume does not replace the exact previously observed raw source')
        require(meta.get('Name')==case['spawnName'] and 'ServerWorkableItem' in (meta.get('Components') or [])
                and observed.registry[item].get('Name') in (meta.get('SpawnNames') or []),
                'Prepared resume lacks observed ordered native replacement metadata')
        require(observed.on(case['chef']) is None and observed.on(case['helper'])==case['plate']
                and observed.clean_plate(case['plate']) and observed.round['ledger']==case['initialLedger']
                and observed.frame>case['startFrame'] and observed.round['elapsed']>case['startElapsed'],
                'Prepared resume plate/chef/ledger/time prerequisites changed')
        proof={'rawId':raw_id,'rawPath':raw_path,'preparedId':item,'preparedPath':prepared_path,
               'preparedComposition':observed.composition(item),'phase':'prepare-assembly','priorStagedProof':prior,
               'priorEvidence':prior_file,
               'scope':'Exact current prepared food and retained consumed raw path; no historical removal callback inferred'}
    else:
        proof=staged_proof(observed,case)
    return case,{"sourceCases":str(Path(path).resolve()),"sourceSha256":hashlib.sha256(raw).hexdigest(),"candidate":index,
        "stagedNativeProof":proof,"reinterpretation":"Original raw composition assertion rejected a native ServerWorkableItem with null composition. Current proof uses the unchanged observed crate prefab/path, native object name, eight-stage workable, attachments and ledger. Original failure evidence is unchanged.",
        "noPreparationActionsRepeated":True,"resumeFrame":observed.frame,
        "resumePhase":proof.get('phase','staged')}


def replay_goal_comparison(expected, observed, expected_receipt, receipt, expected_inputs, inputs):
    """Exact native goal/path/time evidence; do not silently remap raw identities."""
    fields = ("rawPath", "rawComposition", "rawWorkable", "plate", "board", "frames", "nativeSeconds")
    comparisons = {
        "goal": first_difference({k: expected[k] for k in fields}, {k: observed[k] for k in fields}),
        "inputs": first_difference(expected_inputs, inputs),
        "nativeRound": first_difference(gameplay_round(expected_receipt["bridge"]["nativeRound"]), gameplay_round(receipt["bridge"]["nativeRound"])),
        "nativePrivateClocks": first_difference(native_clock_state(expected_receipt["bridge"]), native_clock_state(receipt["bridge"])),
    }
    return {"passed": all(v is None for v in comparisons.values()), "firstDifferences": comparisons,
            "originalRawId": expected["rawId"], "replayRawId": observed["rawId"],
            "scope": "Exact inputs, native preparation effect, logical spawn path, food, orders and time. Dynamic native IDs are retained separately; this is not full raw endpoint parity."}


class Runner:
    def __init__(self, args, bridge, host, journal):
        self.args, self.bridge, self.host, self.journal = args, bridge, host, journal
        self.began = time.monotonic(); self.batch_number = 0
        self.registry_evidence = RegistryEvidence()
        self.summary = {"passed": False, "trials": [], "batches": [],
            "classification": "Native Story1-1 first-delivery attempt with optional preparation search",
            "searchEnabled": args.search, "recipePhysicsChanged": False,
            "timerPolicy": "User-authorized immediate native timer, observed rather than set by this runner"}

    def save(self):
        self.summary["observationEvidence"] = self.journal.describe()
        (self.args.out/"summary.json").write_text(json.dumps(self.summary, indent=2), encoding="utf8")

    def call(self, target, request, label):
        row = {"label": label, "target": target, "request": request, "wallSeconds": time.monotonic()-self.began}
        try:
            result = (self.bridge if target == "bridge" else self.host).call(request)
            row["response"] = result
            return result
        except Exception as error:
            row["error"] = str(error); raise
        finally:
            self.journal.append(row)

    def settled(self, label):
        def status(value):
            self.journal.append({"label": label+"-status", "target": "controller", "request": {"command": "status"}, "response": value})
        return settled_status(self.host, lambda: self.call("controller", {"command": "inspect", "full": True}, label), status)

    def observe(self, label):
        state = self.settled(label+"-host")
        def frame():
            s = self.call("controller", {"command": "status"}, label+"-frame")
            require(s.get("state") == "Paused" and not s.get("requestPending") and not s.get("errors") and not s.get("traceFailure"), "Native pause lost during observation")
            return s["frame"]
        try:
            boundary = observe_settled_pause(lambda: self.call("bridge", {"command": "food"}, label+"-native-sample"), frame)
        except PauseBoundaryError as error:
            (self.args.out/(label+"-pause.json")).write_text(json.dumps(error.report, indent=2)); raise
        (self.args.out/(label+"-pause.json")).write_text(json.dumps(boundary["proof"], indent=2))
        # Read current headless poses after the last native paused receipt.
        current = self.settled(label+"-current")
        require(current["frame"] == state["frame"], "Observation changed native frame")
        receipt = boundary["receipt"]
        self.journal.append({"label": label, "target": "settled-pause-observation", "response": receipt})
        current, receipt = self.reconcile_registry(current, receipt, label)
        observed = Observation(current, receipt, self.args.scene)
        captured = self.registry_evidence.capture(current, receipt)
        if captured:
            self.journal.append({"label": label+"-proxy-identities", "target": "observed-native-proxy-associations", "response": captured})
        return observed

    def reconcile_registry(self, current, receipt, label):
        requests = self.registry_evidence.requests(current, receipt)
        if not requests:
            return current, receipt
        before = world_proof(current, receipt)
        was_armed = receipt['bridge'].get('inputBlocked') is False
        fenced = self.call('bridge', {'command': 'pause'}, label+'-registry-fence')
        require(fenced.get('bridge', {}).get('inputBlocked') is True and fenced['bridge'].get('holdPause') is True,
                'Native registry observation requires its explicit input fence')
        publications = []
        for number, request in enumerate(requests):
            response = self.call('bridge', {'command': 'hot-call', 'slot': 'registry-observer',
                'operation': request['operation'], 'args': request['args']}, label+f'-registry-{number}')
            publication = self.registry_evidence.validate_publication(request, response)
            publications.append((request, publication))
        deadline = time.monotonic()+3
        while True:
            current = self.settled(label+'-registry-received')
            require(current['frame'] == before['frame'], 'Registry observation publication advanced the native frame')
            if all(self.registry_evidence.received(request, result, current) for request, result in publications):
                break
            require(time.monotonic() < deadline, 'Published registry observation did not reach the paused host')
            time.sleep(.01)
        receipt = self.call('bridge', {'command': 'food'}, label+'-registry-native-after')
        difference = first_difference(before, world_proof(current, receipt))
        proof = {'requests': requests, 'publications': [p for _, p in publications],
                 'nativeWorldFirstDifference': difference, 'frame': current['frame'],
                 'scope': 'Observed metadata/current absence only; historical native removal events are not inferred'}
        self.journal.append({'label': label+'-registry-proof', 'target': 'registry-observation-proof', 'response': proof})
        self.summary.setdefault('registryObservations', []).append(proof)
        self.save()
        require(difference is None, 'Registry observation changed native world/food/order/clock evidence')
        if was_armed:
            self.call('bridge', {'command': 'arm'}, label+'-registry-rearm')
            # Keep the food/physics proof while accurately reporting the restored
            # fence policy in the following complete native observation.
            receipt = self.call('bridge', {'command': 'food'}, label+'-registry-rearmed-native')
            require(first_difference(before, world_proof(current, receipt)) is None,
                    'Rearming after registry observation changed native gameplay state')
        self.journal.append({'label': label+'-registry-observed', 'target': 'settled-pause-observation', 'response': receipt})
        return current, receipt

    def execute(self, request, label, start):
        cursor = AdvancingTrace(self.args.trace)
        self.call("controller", request, label+"-request")
        # Capture the raw emitted rows before semantic validation, including
        # ordinary timeout and unsuccessful native edge evidence.
        end = self.settled(label+"-end")
        rows = capture_input_evidence(cursor, start.frame, end["frame"], start.chefs, self.args.out/(label+"-raw-capture.json"))
        self.summary["batches"].append({"id": label, "request": request, "startFrame": start.frame, "endFrame": end["frame"], "input": input_report(rows)})
        self.save()
        return self.observe(label+"-observed"), rows

    def restore(self, base, label):
        before = self.call("bridge", {"command": "food"}, label+"-before")["bridge"]["nativeCheckpoints"]["restoreAttempts"]
        self.call("controller", {"command": "warp", "frame": base.frame, "development": True}, label+"-warp")
        restored = self.observe(label+"-restore")
        proof = restored.receipt["bridge"]["nativeCheckpoints"].get("lastRestore") or {}
        require(proof.get("verified") is True and proof.get("frame") == base.frame and proof.get("attempt", -1) > before, "Missing fresh verified native checkpoint acknowledgement")
        self.call("controller", {"command": "actions-clear", "all": True}, label+"-clear")
        cleared = self.observe(label+"-cleared")
        require_empty_search_graph(cleared.state)
        comparison = compare_boundary(base.state, base.receipt, cleared.state, cleared.receipt)
        (self.args.out/(label+"-comparison.json")).write_text(json.dumps(comparison, indent=2))
        require(comparison["passed"], "Native baseline rollback exceeded an explicit physical bound or changed exact logical/food/order/time state")
        return cleared

    def run(self):
        initial = self.settled("initial")
        resume=getattr(self.args,"resume_staged",None) or getattr(self.args,"resume_observed",None)
        if not resume:require_empty_search_graph(initial)
        else:require(not self.args.search and self.args.warmup==0,"Resume-staged requires --warmup 0 and cannot run search")
        self.call("bridge", {"command": "pause"}, "timer-policy-fence")
        timer = self.call("bridge", {"command": "hot-call", "slot": "level-session", "operation": "status", "args": {}}, "timer-policy")
        self.call("bridge", {"command": "arm"}, "arm")
        if self.args.warmup:
            self.call("controller", {"command": "step", "frames": self.args.warmup}, "warmup")
        base = self.observe("base")
        self.summary["timerPolicyProof"] = require_timer_policy(timer, base.receipt)
        require(base.round.get("timerSuppressed") is False, "Immediate native Story1-1 timer is not active")
        require(all(v == 0 for v in base.round["ledger"].values()), "First-delivery attempt requires a fresh zero ledger")
        if resume:
            case,proof=resume_case(resume,self.args.candidate,base,allow_prepared=bool(getattr(self.args,'resume_observed',None)))
            self.summary.update(resumedStaged=proof,selected=case["id"],baseFrame=base.frame,optimizationScope="Resume of one already completed native prep; no new search or preparation replay")
            self.save();return self.finish_delivery(case,base,proof)
        cases = candidates(base)
        (self.args.out/"cases.json").write_text(json.dumps(cases, indent=2))
        self.summary["baseFrame"] = base.frame
        if self.args.search:
            require(base.round.get("recipeRandom") is not None, "Native ScriptedRoundData checkpoint adapter has not produced RNG/history evidence")
            self.call("controller", {"command": "checkpoint", "path": search_artifact_name(self.args.out, "story11-base", base.frame, "pb")}, "checkpoint")
        history = {}
        selected_case = cases[self.args.candidate]
        for index, case in enumerate(cases[:self.args.candidates] if self.args.search else [selected_case]):
            current = self.restore(base, "candidate-"+str(index)) if index else base
            observed, rows = self.execute(case["request"], case["id"], current)
            trial = {"id": case["id"], "eligible": False, "input": input_report(rows)}
            try:
                trial["goal"] = staged_proof(observed, case)
                require(observed.state.get("typedActions", {}).get("outcome") == "complete", "Preparation graph did not complete")
                trial["eligible"] = True
            except Blocked as error:
                trial["error"] = str(error)
            self.summary["trials"].append(trial); self.save()
            history[case["id"]] = (case, observed, rows)
            if not self.args.search:
                require(trial["eligible"], trial.get("error", "Preparation failed"))
        selected = select_trial(self.summary["trials"])
        selected_case, current, rows = history[selected["id"]]
        self.summary["selected"] = selected["id"]
        baseline = self.summary["trials"][0]
        self.summary["preparationSecondsSaved"] = baseline["goal"]["nativeSeconds"]-selected["goal"]["nativeSeconds"] if baseline["eligible"] else None
        self.summary["optimizationScope"] = "Measured fetch-to-board plus parallel plate pickup only; whole-delivery time is reported separately."
        if self.args.search:
            raw = to_raw_request(rows, base.chefs)
            (self.args.out/"selected-raw-request.json").write_text(json.dumps(raw, indent=2))
            restored = self.restore(base, "selected")
            repeated, repeated_rows = self.execute(raw, "selected-fixed-input", restored)
            repeated_goal = staged_proof(repeated, selected_case)
            comparison = replay_goal_comparison(selected["goal"], repeated_goal, current.receipt, repeated.receipt, rows, repeated_rows)
            self.summary["selectedFixedInputComparison"] = comparison
            # Preserve unnormalized physical/identity differences even though
            # the replay qualification above is explicitly a native goal proof.
            endpoint = compare_boundary(current.state, current.receipt, repeated.state, repeated.receipt)
            (self.args.out/"selected-endpoint-raw-differences.json").write_text(json.dumps(endpoint, indent=2))
            self.save(); require(comparison["passed"], "Selected raw inputs did not reproduce exact native preparation goal/time")
            current = repeated
        return self.finish_delivery(selected_case,current)

    def finish_delivery(self, selected_case, current, resume=None):
        planner = DeliveryPlanner(copy.deepcopy(selected_case))
        if resume and resume['resumePhase']=='prepare-assembly':
            proof=resume['stagedNativeProof']
            planner.phase='prepare-assembly';planner.source=proof['preparedId']
            planner.prepared_composition=proof['preparedComposition']
            planner.raw_source=proof['rawId'];planner.raw_path=proof['rawPath']
        origin=current.frame
        for number in range(64):
            planned = planner.advance(current)
            if planned.get("complete"):
                self.summary.update(passed=True, delivery=planned["proof"], nativeDeliverySeconds=current.round["elapsed"]-selected_case["startElapsed"])
                self.save(); return
            request = planned["request"]
            # Independent batches get globally unique graph IDs. Retain all
            # prior definitions; no subsequent checkpoint branch is attempted.
            if request["command"] == "actions":
                request = copy.deepcopy(request); prefix = f"delivery-{origin}-{number}-"
                for node in request["actions"]:
                    node["id"] = prefix+node["id"]
                    if "after" in node: node["after"] = [prefix+x for x in node["after"]]
            current, _ = self.execute(request, f"delivery-{origin}-{number:02d}", current)
        raise Blocked("First delivery exceeded its bounded action-batch count")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--trace", required=True, type=Path)
    parser.add_argument("--scene", default="s_sushi_1_1")
    parser.add_argument("--bridge-port", type=int, default=17636)
    parser.add_argument("--controller-port", type=int, default=17637)
    parser.add_argument("--warmup", type=int, default=30, choices=range(0,61))
    parser.add_argument("--search", action="store_true")
    parser.add_argument("--candidates", type=int, default=4, choices=range(1,5))
    parser.add_argument("--candidate", type=int, default=0, choices=range(4))
    resume=parser.add_mutually_exclusive_group()
    resume.add_argument("--resume-staged",type=Path,help="Retained cases.json after completed typed/raw preparation; requires --warmup 0")
    resume.add_argument("--resume-observed",type=Path,help="Resume exact raw or already chopped native preparation; requires --warmup 0")
    args = parser.parse_args(); args.out.mkdir(parents=True, exist_ok=False)
    bridge = host = runner = None; journal = EvidenceJournal(args.out/"observations.jsonl")
    failure = None
    try:
        bridge = Client(args.bridge_port); host = ControllerClient(args.controller_port)
        runner = Runner(args, bridge, host, journal); runner.run()
    except Exception as error:
        failure = str(error)
        if runner: runner.summary.update(passed=False, error=failure)
    finally:
        if bridge:
            try:
                if runner: runner.call("bridge", {"command":"pause"}, "finally-pause")
                else: bridge.call({"command":"pause"})
            except Exception as error:
                failure = str(error)
                if runner: runner.summary.update(passed=False, pauseError=failure)
        for client in (bridge, host):
            if client: client.close()
        journal.finalize(args.out/"observations.json")
        if runner:
            runner.save(); print(json.dumps(runner.summary, indent=2))
        else:
            (args.out/"summary.json").write_text(json.dumps({"passed":False,"error":failure,"observationEvidence":journal.describe()}, indent=2))
    return 0 if runner and runner.summary["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
