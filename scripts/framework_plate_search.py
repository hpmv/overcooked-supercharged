"""Small operator-run native search: clear a fixed counter by staging its clean plate.

All candidates execute in the actual game from the same verified process-local
checkpoint. Only existing fixed entities are touched. The selected typed action
graph's emitted four-pad frames are repeated as a fixed raw-input stream, then
exported and replayed through the existing raw-replay API. Every accepted frame,
button edge, native food result and endpoint comparison is retained.

This is an authoring mechanism/preparation achievement, not a recipe delivery,
score forecast, full kitchen search, or fresh-start qualification. Importing the
module makes no connection. The operator supplies the owned host's trace path.
V6 paused-exchanges blocks are skipped only for advancing-input extraction; no
claim is made to reconstruct full native state from this reduced trace view.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import time

from framework_kitchen_planner import Observation, PlanningError, canonical, finite
from framework_native_search import (boundary_matches, compare_boundary, exact_values,
                                    food_trees, require_native_boundary, require_recorded_completion)
from framework_rpc import Client, ControllerClient
from compare_framework_frames import first_difference
from framework_pause_boundary import PauseBoundaryError, native_physics, native_clock_fields, observe_settled_pause
from framework_evidence_journal import EvidenceJournal


def digest(value):
    return hashlib.sha256(canonical(value).encode()).hexdigest()


def require_native_physics(receipt, chef_ids=None):
    """Require the actual resumable native bodies, never a missing==missing pass."""
    try:return native_physics(receipt,chef_ids)
    except PauseBoundaryError as error:raise PlanningError(str(error)) from error


def compare_plate_boundary(expected_state,expected_receipt,state,receipt,chef_ids):
    expected=require_native_physics(expected_receipt,chef_ids)
    observed=require_native_physics(receipt,chef_ids)
    comparison=compare_boundary(expected_state,expected_receipt["bridge"]["nativeRound"],food_trees(expected_receipt),
                                state,receipt["bridge"]["nativeRound"],food_trees(receipt))
    difference=first_difference(expected,observed,"$nativePhysics")
    try:
        expected_clocks=native_clock_fields(expected_receipt);actual_clocks=native_clock_fields(receipt)
    except PauseBoundaryError as error:raise PlanningError(str(error)) from error
    clock_difference=first_difference(expected_clocks,actual_clocks,"$nativePrivateClocks")
    comparison.update(nativePhysicsEqual=difference is None,nativePhysicsFirstDifference=difference,
                      expectedNativePhysicsSha256=digest(expected),observedNativePhysicsSha256=digest(observed),
                      nativePrivateClocksEqual=clock_difference is None,nativePrivateClocksFirstDifference=clock_difference,
                      nativePrivateClocksObserved=expected_clocks is not None and actual_clocks is not None)
    return comparison


def plate_boundary_matches(comparison):
    return boundary_matches(comparison) and comparison.get("nativePhysicsEqual") is True and comparison.get("nativePrivateClocksEqual") is True


def prepare_case(o: Observation, maximum_frames=900):
    if not 120 <= maximum_frames <= 1800:
        raise PlanningError("Use maximum_frames120..1800 for this bounded native comparison")
    if any(len(e["path"]) != 1 for e in o.entities.values()):
        raise PlanningError("Current warp backend admits fixed entities only; raw ingredient search is not enabled")
    for e in o.entities.values():
        if e["className"] == "cannon":
            aux=e.get("nativeCannon") or {}
            if not aux.get("observedAux") or not aux.get("settledForWarp") or aux.get("flying") or aux.get("activeLaunches"):
                raise PlanningError("Both native cannons must have observed inactive warp metadata")
    # Source is the specific counter validated by the native bun-landing probe.
    def counter(x,z):
        found=[]
        for eid,e in o.entities.items():
            if e["className"]!="counter" or eid not in o.initial or eid not in o.registry:continue
            p=o.initial[eid]["Pos"];q=e["position"]
            if max(abs(p["X"]-x),abs(p["Z"]-z),abs(q["x"]-x),abs(q["z"]-z))<=.04 \
                    and "AttachStation" in o.registry[eid].get("Components",[]):found.append(eid)
        if len(found)!=1:raise PlanningError("Staging search requires one exact native fixed counter role")
        return found[0]
    source,target=counter(19.2,-15.6),counter(19.2,-10.8)
    plate=o.on(source)
    if plate is None or o.entity(plate)["className"]!="plate" or o.facts(plate) is None or o.facts(plate)[0] \
            or (o.entity(plate).get("plateLifecycle") or {}).get("phase") or o.on(target) is not None:
        raise PlanningError("Search requires the exact native clean plate on its source and an empty destination")
    chefs=sorted(eid for eid,e in o.entities.items() if e.get("chef") is not None)
    if len(chefs)!=4 or any(o.held(c) is not None for c in chefs):
        raise PlanningError("Search needs four observed empty-handed chefs")
    centers=[c for c in chefs if o.accessible(c,source) and o.accessible(c,target)]
    if len(centers)!=2:raise PlanningError("Expected exactly two legal central candidate chefs")
    candidates=[]
    for chef in centers:
        for dash in (False,True):
            name=f"chef-{chef}-{'dash' if dash else 'walk'}"
            resources=[plate,source,target]
            # These are cardinal floor approaches to the two validated counter
            # roles above, not coordinates inferred from an arbitrary target ID.
            # Q's unprepared pickup pressed while the old cached velocity still
            # moved the chef; it completed its input edge without taking plate10.
            source_pos=o.entity(source)["position"];target_pos=o.entity(target)["position"]
            actions=[{"id":"source-approach","chef":chef,"type":"goto",
                      "x":source_pos["x"]-1.1,"z":source_pos["z"],"resources":resources,
                      "timeoutFrames":maximum_frames//3,"dash":dash},
                     {"id":"source-settle","chef":chef,"type":"wait","frames":6,
                      "resources":resources,"after":["source-approach"],"timeoutFrames":30},
                     {"id":"pickup","chef":chef,"type":"pickup","target":plate,"resources":resources,
                      "after":["source-settle"],"timeoutFrames":maximum_frames//3,"dash":False},
                     {"id":"target-approach","chef":chef,"type":"goto",
                      "x":target_pos["x"],"z":target_pos["z"]-1.2,"resources":resources,
                      "after":["pickup"],"timeoutFrames":maximum_frames//3,"dash":dash},
                     {"id":"target-settle","chef":chef,"type":"wait","frames":6,
                      "resources":resources,"after":["target-approach"],"timeoutFrames":30},
                     {"id":"stage","chef":chef,"type":"place","target":target,"resources":resources,
                      "after":["target-settle"],"timeoutFrames":maximum_frames//3,"dash":False}]
            candidates.append({"id":name,"chef":chef,"dash":dash,
                               "request":{"command":"actions","maximumFrames":maximum_frames,"actions":actions}})
    return {"version":2,"goal":"clear-fixed-bun-landing-counter-and-preserve-clean-plate",
            "classification":"fixed-entity native preparation search; no cooked recipe achievement",
            "frame":o.frame,"elapsed":o.elapsed,"source":source,"target":target,"plate":plate,"chefs":chefs,
            "identities":{str(e):o.token(e) for e in o.entities},
            "fixedParents":{str(e):o.parent(e) for e in o.entities},
            "nativeFoodCompositions":{str(e):f.get("composition") for e,f in o.foods.items()},
            "baseEntitiesHash":digest({str(i):e for i,e in o.entities.items()}),
            "plateComposition":o.foods[plate]["composition"],"ledger":dict(o.native_round["ledger"]),
            "maximumFrames":maximum_frames,"candidates":candidates,
            "dynamicSearchPrerequisite":"Verified rollback of current spawned raw ingredients to fixed baseline, followed by target dynamic-object reconstruction/identity proof"}


def check_goal(case,o: Observation):
    for eid,token in case["identities"].items():
        if o.token(int(eid))!=token:raise PlanningError("Fixed staging identity changed")
    if any(len(e["path"])!=1 for e in o.entities.values()):
        raise PlanningError("This fixed-only search created a spawned entity")
    if not exact_values(o.native_round["ledger"],case["ledger"]):
        raise PlanningError("Plate preparation search changed the native score/delivery ledger")
    if not exact_values(o.foods.get(case["plate"],{}).get("composition"),case["plateComposition"]):
        raise PlanningError("The exact native clean plate composition changed")
    frames=o.frame-case["frame"];seconds=finite(o.elapsed-case["elapsed"],"actual native duration")
    if frames<=0 or frames>case["maximumFrames"]+4 or seconds<=0:
        raise PlanningError("Invalid native action duration")
    # Require both native attachment directions. Child-only parent links can
    # otherwise accept an incomplete/inconsistent station or carry receipt.
    def attached_path(parent):
        return (o.entity(parent).get("data",{}).get("attachment") or {}).get("path",[])
    achieved=o.on(case["source"]) is None and not attached_path(case["source"]) \
        and o.on(case["target"])==case["plate"] and attached_path(case["target"])==o.entity(case["plate"])["path"] \
        and o.parent(case["plate"])==case["target"] \
        and all(o.held(c) is None and not attached_path(c) for c in case["chefs"])
    return {"achieved":achieved,"frames":frames,"nativeSeconds":seconds,
            "plate":case["plate"],"source":case["source"],"target":case["target"],
            "exactNativePlateCompositionPreserved":True,"nativeLedgerUnchanged":True}


def classify_candidate(case,candidate,o: Observation):
    """Ordinary task failure is searchable; an unknown native state is not."""
    if o.snapshot.get("invalidStateReason") or o.snapshot.get("traceFailure") or (o.snapshot.get("trace") or {}).get("failed"):
        raise PlanningError("Candidate has a native invalid-state or trace failure")
    goal=check_goal(case,o)
    if {str(e) for e in o.entities}!=set(case["identities"]):
        raise PlanningError("Fixed candidate changed the live native inventory")
    if not exact_values({str(e):f.get("composition") for e,f in o.foods.items()},case["nativeFoodCompositions"]):
        raise PlanningError("Fixed candidate changed another native food composition")
    for eid,parent in case["fixedParents"].items():
        if int(eid)!=case["plate"] and o.parent(int(eid))!=parent:
            raise PlanningError("Fixed candidate changed an unrelated native attachment")
    if o.parent(case["plate"]) not in {case["source"],case["target"],candidate["chef"]}:
        raise PlanningError("Exact plate left its reserved source/chef/output envelope")
    if o.held(candidate["chef"]) not in {None,case["plate"]} or any(o.held(c) is not None for c in case["chefs"] if c!=candidate["chef"]):
        raise PlanningError("Unexpected native chef catch during fixed plate candidate")
    for entity in o.entities.values():
        if entity["className"]=="cannon":
            aux=entity.get("nativeCannon") or {}
            if not aux.get("observedAux") or not aux.get("settledForWarp"):
                raise PlanningError("Fixed candidate entered an unsupported cannon state")
    action=o.snapshot.get("typedActions") or {};outcome=action.get("outcome");error=action.get("error")
    ordinary_timeout=outcome=="failed" and isinstance(error,str) and (
        error.startswith("Action timeout: ") or error=="Whole graph frame deadline exceeded.")
    if outcome!="complete" and not ordinary_timeout:
        raise PlanningError("Unexpected typed candidate failure: "+str(error or outcome))
    return {"goal":goal,"eligible":outcome=="complete" and goal["achieved"],
            "outcome":outcome,"error":error,
            "classification":"completed native plate relocation" if outcome=="complete" and goal["achieved"] else
                "ordinary bounded timeout" if ordinary_timeout else "completed input graph without native goal",
            "fixedReservationEnvelopeVerified":True,"nextCandidateRequiresExactVerifiedRollback":True}


def normalize_inputs(inputs,chefs):
    if not isinstance(inputs,dict) or {int(i) for i in inputs}!=set(chefs):
        raise PlanningError("Every advancing row must carry exactly four observed native chef inputs")
    result={}
    for chef in sorted(chefs):
        value=inputs.get(str(chef),inputs.get(chef));pad=value.get("Pad")
        if not isinstance(pad,dict):raise PlanningError("Missing ordinary native pad axes")
        normalized={"Pad":{axis:finite(pad.get(axis),axis) for axis in ("X","Y")}}
        if any(abs(v)>1 for v in normalized["Pad"].values()):raise PlanningError("Native pad outside [-1,1]")
        for button in ("Pickup","Interact","Dash"):
            raw=value.get(button,{})
            if any(type(raw.get(k)) is not bool for k in ("Down","JustPressed","JustReleased")):
                raise PlanningError("Missing explicit native button edge fields")
            normalized[button]={k:raw[k] for k in ("Down","JustPressed","JustReleased")}
        result[str(chef)]=normalized
    return result


class AdvancingTrace:
    """Bounded append-only V5/V6 JSONL reader; never rereads the session prefix."""
    def __init__(self,path:Path):
        self.path=Path(path);self.offset=self.path.stat().st_size;self.pending=b"";self.skip_partial=False
        if self.offset:
            with self.path.open("rb") as stream:
                stream.seek(self.offset-1);self.skip_partial=stream.read(1)!=b"\n"
        self.rows=[];self.paused_blocks_skipped=0

    def read(self):
        if self.path.stat().st_size<self.offset:raise PlanningError("Trace was truncated/replaced during native trial")
        with self.path.open("rb") as stream:
            stream.seek(self.offset)
            while True:
                chunk=stream.read(65536)
                if not chunk:break
                self.offset+=len(chunk);self.pending+=chunk
                while b"\n" in self.pending:
                    line,self.pending=self.pending.split(b"\n",1)
                    if self.skip_partial:self.skip_partial=False;continue
                    if not line.strip():continue
                    row=json.loads(line)
                    if row.get("kind")=="paused-exchanges":
                        if row.get("version")!=1 or row.get("encoding")!="brotli-jsonl":raise PlanningError("Unknown compressed paused trace encoding")
                        self.paused_blocks_skipped+=1
                    elif row.get("kind")=="exchange":
                        if row.get("input",{}).get("Input"):self.rows.append(row)
                        if len(self.rows)>40000:raise PlanningError("Bounded advancing input trace limit exceeded")
                if len(self.pending)>4*1024*1024:raise PlanningError("Trace line exceeds bounded reader capacity")

    def frames(self,start,end,chefs):
        self.read();by_frame={}
        for row in self.rows:
            value=row["input"];frame=value.get("NextFrame")
            if not isinstance(frame,int) or not start<frame<=end:continue
            if value.get("Warp") is not None or value.get("ResetOrderSeed") is not None and value.get("__isset",{}).get("resetOrderSeed"):
                raise PlanningError("Candidate input contains an authoring/seed directive")
            inputs=normalize_inputs(value["Input"],chefs)
            if frame in by_frame:raise PlanningError("Duplicate advancing input tag; cannot infer accepted native input")
            by_frame[frame]=inputs
        if set(by_frame)!=set(range(start+1,end+1)):
            raise PlanningError("Advancing trace does not yet contain every exact accepted logical frame")
        return [{"ordinal":i-start-1,"nextFrame":i,"inputs":by_frame[i]} for i in sorted(by_frame)]


def neutral(row,edges=False):
    return all(all(v==0 for v in pad["Pad"].values()) and
               not any(pad[b]["Down"] or pad[b]["JustPressed"] or edges and pad[b]["JustReleased"] for b in ("Pickup","Interact","Dash"))
               for pad in row["inputs"].values())


def to_raw_request(rows,chefs):
    if len(rows)<3 or not neutral(rows[-2]) or not neutral(rows[-1],True):
        raise PlanningError("Selected typed graph lacks the exact two-frame neutral release tail")
    segments=[];previous={str(c):{b:False for b in ("Pickup","Interact","Dash")} for c in chefs}
    for index,row in enumerate(rows):
        if row["ordinal"]!=index:raise PlanningError("Noncontiguous selected input recording")
        pads=normalize_inputs(row["inputs"],chefs)
        for chef,pad in pads.items():
            for button in previous[chef]:
                expected={"Down":pad[button]["Down"],"JustPressed":pad[button]["Down"] and not previous[chef][button],
                          "JustReleased":previous[chef][button] and not pad[button]["Down"]}
                if pad[button]!=expected:raise PlanningError("Selected native edges are inconsistent with preceding held state")
                previous[chef][button]=pad[button]["Down"]
        if index>=len(rows)-2:continue
        values={chef:{"x":p["Pad"]["X"],"y":p["Pad"]["Y"],"pickup":p["Pickup"]["Down"],
                      "interact":p["Interact"]["Down"],"dash":p["Dash"]["Down"]} for chef,p in pads.items()}
        if segments and segments[-1]["chefs"]==values:segments[-1]["frames"]+=1
        else:segments.append({"frames":1,"chefs":values})
    if len(segments)>1024:raise PlanningError("Selected input exceeds current raw API segment limit")
    return {"command":"raw-input","segments":segments}


def input_report(rows):
    return {"frames":len(rows),"logicalInputSha256":digest([r["inputs"] for r in rows]),
            "pickupPresses":sum(p["Pickup"]["JustPressed"] for r in rows for p in r["inputs"].values()),
            "dashPresses":sum(p["Dash"]["JustPressed"] for r in rows for p in r["inputs"].values()),
            "movingChefFrames":sum(any(v!=0 for v in p["Pad"].values()) for r in rows for p in r["inputs"].values())}


def settled_status(host, inspect, record_status, timeout=90):
    """Poll the lightweight host API; materialize one full paused boundary."""
    deadline=time.monotonic()+timeout
    while True:
        state=host.status();record_status(state)
        if state.get("errors") or state.get("state")=="Error" or state.get("traceFailure"):
            raise PlanningError(json.dumps(state))
        if state.get("state")=="Paused" and not state.get("requestPending"):
            full=inspect()
            if full.get("errors") or full.get("state")!="Paused" or full.get("requestPending") or full.get("traceFailure"):
                raise PlanningError("Full inspection lost its error-free pause: "+json.dumps(full))
            return full
        if time.monotonic()>deadline:raise TimeoutError("Native action did not reach a settled pause")
        time.sleep(.03)


def require_empty_search_graph(state):
    if any(row.get("actions") for row in state.get("actionGraph",{}).get("chefs",[])) \
            or (state.get("typedActions") or {}).get("active") or (state.get("rawInput") or {}).get("active"):
        raise PlanningError("Clear the previous action graph at the fenced fresh pause before starting checkpoint search")


def search_artifact_name(out, kind, frame, suffix):
    # The host retains create-new checkpoints/recordings across normal restarts.
    # The output directory itself is also create-new, so its identity namespaces
    # these files without weakening either overwrite guard.
    identity=hashlib.sha256(str(Path(out).resolve()).encode('utf8')).hexdigest()[:16]
    return f"plate-search-{identity}-{kind}-{frame}.{suffix}"


def capture_input_evidence(cursor,start,end,chefs,path,timeout=3):
    """Persist original input exchanges even if extraction or later checks fail."""
    result={"trace":str(cursor.path),"startExclusive":start,"endInclusive":end,"validation":"pending"}
    deadline=time.monotonic()+timeout
    try:
        while True:
            try:
                rows=cursor.frames(start,end,chefs);break
            except PlanningError as error:
                if "does not yet contain" not in str(error) or time.monotonic()>deadline:raise
                time.sleep(.02)
        result.update(inputs=rows,validation="exact-four-pad-frame-coverage")
        return rows
    except Exception as error:
        result.update(validation="failed",error=str(error));raise
    finally:
        result.update(rawExchanges=cursor.rows,traceByteOffset=cursor.offset,
                      pausedBlocksSkipped=cursor.paused_blocks_skipped)
        Path(path).write_text(json.dumps(result,separators=(',',':')),encoding='utf8')


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out",type=Path,required=True)
    parser.add_argument("--trace",type=Path,required=True)
    parser.add_argument("--bridge-port",type=int,default=17636)
    parser.add_argument("--controller-port",type=int,default=17637)
    parser.add_argument("--maximum-frames",type=int,default=900)
    parser.add_argument("--candidates",type=int,default=4,choices=range(1,5))
    args=parser.parse_args()
    if not 120<=args.maximum_frames<=1800:parser.error("Use maximum-frames120..1800 for this bounded native search")
    args.out.mkdir(parents=True,exist_ok=False)
    bridge=host=None;summary={"passed":False,"classification":"native fixed-plate preparation search with selected fixed-input replay","trials":[]}
    evidence=EvidenceJournal(args.out/"observations.jsonl");began=time.monotonic();serial=0

    def save():
        summary["observationEvidence"]=evidence.describe()
        (args.out/"summary.json").write_text(json.dumps(summary,indent=2))

    def call(target,request,label):
        try:result=(bridge if target=="bridge" else host).call(request)
        except Exception as error:
            evidence.append({"label":label,"target":target,"request":request,"error":str(error),"wallSeconds":time.monotonic()-began})
            raise
        evidence.append({"label":label,"target":target,"request":request,"response":result,"wallSeconds":time.monotonic()-began})
        return result

    def settled(label):
        def record_status(state):
            evidence.append({"label":label+"-status","target":"controller","request":{"command":"status"},
                             "response":state,"wallSeconds":time.monotonic()-began})
        return settled_status(host,lambda:call("controller",{"command":"inspect","full":True},label),record_status)

    def native(label):
        sample=0;frame_reads=0
        def read_receipt():
            nonlocal sample
            value=call("bridge",{"command":"food"},f"{label}-raw-{sample:03d}");sample+=1
            require_native_boundary(value);return value
        def read_frame():
            nonlocal frame_reads
            value=call("controller",{"command":"status"},f"{label}-frame-{frame_reads:03d}");frame_reads+=1
            if value.get("state")!="Paused" or value.get("requestPending") or value.get("errors") or value.get("traceFailure"):
                raise PlanningError("Pause observation lost its settled controller frame")
            return value.get("frame")
        proof_path=args.out/f"{label}-pause-boundary.json"
        try:
            result=observe_settled_pause(read_receipt,read_frame)
            if native_clock_fields(result["receipt"]) is None:
                result["proof"].update(passed=False,error="Current plate search requires S native private clock observations")
                raise PauseBoundaryError("Current plate search requires S native private clock observations",result["proof"])
        except PauseBoundaryError as error:
            proof_path.write_text(json.dumps(error.report,indent=2))
            summary["failedPauseBoundary"]={"label":label,"error":str(error),"proof":str(proof_path)}
            raise PlanningError(str(error)) from error
        proof_path.write_text(json.dumps(result["proof"],indent=2))
        evidence.append({"label":label,"target":"settled-pause-observation","response":result["receipt"],
                         "proof":str(proof_path),"classification":result["proof"]["classification"]})
        return result["receipt"]

    try:
        bridge=Client(args.bridge_port);host=ControllerClient(args.controller_port)
        require_empty_search_graph(settled("initial"));call("bridge",{"command":"arm"},"arm")
        call("controller",{"command":"step","frames":30},"warmup")
        base=settled("base");base_native=native("base-native")
        o=Observation(base,base_native["bridge"]["nativeRound"],base_native)
        case=prepare_case(o,args.maximum_frames);summary["case"]=case
        summary["baseNativePhysicsSha256"]=digest(require_native_physics(base_native,case["chefs"]))
        (args.out/"case.json").write_text(json.dumps(case,indent=2))
        call("controller",{"command":"checkpoint","path":search_artifact_name(args.out,"base",base["frame"],"pb")},"checkpoint")
        save()

        def restore(label):
            nonlocal serial
            before=native(label+"-before")["bridge"]["nativeCheckpoints"]["restoreAttempts"]
            call("controller",{"command":"warp","frame":base["frame"],"development":True},label+"-warp")
            state=settled(label+"-restored");receipt=native(label+"-native")
            restored=receipt["bridge"]["nativeCheckpoints"]["lastRestore"]
            if not restored or not restored.get("verified") or restored["frame"]!=base["frame"] or restored["attempt"]<=before:
                raise PlanningError("No verified native checkpoint acknowledgement")
            # One whole graph per candidate: its previously completed nodes have
            # become future nodes after rewind. Remove them through the host API.
            if state.get("typedActions",{}).get("outcome") not in {None,"none","cleared"}:
                call("controller",{"command":"actions-clear"},label+"-clear-graph")
                state=settled(label+"-cleared")
            require_empty_search_graph(state)
            comparison=compare_plate_boundary(base,base_native,state,receipt,case["chefs"])
            if not plate_boundary_matches(comparison):
                summary["failedRestoreComparison"]=comparison
                raise PlanningError("Restored native/physical baseline differs; search halted")
            serial+=1
            return state,receipt

        trials=[]
        for index,candidate in enumerate(case["candidates"][:args.candidates]):
            if index:restore(f"candidate-{index}")
            cursor=AdvancingTrace(args.trace)
            call("controller",candidate["request"],candidate["id"]+"-request")
            end=settled(candidate["id"]+"-end");end_native=native(candidate["id"]+"-native")
            rows=capture_input_evidence(cursor,base["frame"],end["frame"],case["chefs"],args.out/f"{candidate['id']}-raw-capture.json")
            (args.out/f"{candidate['id']}-inputs.json").write_text(json.dumps(rows,separators=(",",":")))
            observed=Observation(end,end_native["bridge"]["nativeRound"],end_native)
            decision=classify_candidate(case,candidate,observed)
            # Failed graphs need not have a replayable neutral tail. Preserve
            # their exact input evidence, but select/replay only native successes.
            trial={"id":candidate["id"],**decision,"input":input_report(rows),
                   "nativePhysicsSha256":digest(require_native_physics(end_native,case["chefs"]))}
            summary["trials"].append(trial)
            # Persist each failed attempt before later input conversion or a
            # rollback can fail. No candidate disappears because it had no goal.
            save()
            raw=to_raw_request(rows,case["chefs"]) if decision["eligible"] else None
            trials.append((trial,end,end_native,rows,raw))
        eligible=[t for t in trials if t[0]["eligible"]]
        if not eligible:raise PlanningError("No actual candidate achieved the native plate-staging goal")
        selected=min(eligible,key=lambda t:(t[0]["goal"]["nativeSeconds"],t[0]["goal"]["frames"],t[0]["id"]))
        trial,expected,expected_native,expected_rows,raw=selected
        summary["selected"]=trial["id"]
        summary["baseline"]=trials[0][0]["id"]
        baseline=trials[0][0]
        summary["baselineAchieved"]=baseline["eligible"]
        summary["nativeSecondsVersusBaseline"]=baseline["goal"]["nativeSeconds"]-trial["goal"]["nativeSeconds"] if baseline["eligible"] else None
        if not baseline["eligible"]:
            summary["baselineComparisonLimit"]="The first candidate failed; its timeout duration is not a successful completion time or measured time saving."
        (args.out/"selected-raw-request.json").write_text(json.dumps(raw,indent=2))
        restore("selected-fixed-input")
        cursor=AdvancingTrace(args.trace);call("controller",raw,"selected-fixed-input-request")
        fixed=settled("selected-fixed-input-end");fixed_native=native("selected-fixed-input-native")
        actual_rows=capture_input_evidence(cursor,base["frame"],fixed["frame"],case["chefs"],args.out/"selected-fixed-input-raw-capture.json")
        comparison=compare_plate_boundary(expected,expected_native,fixed,fixed_native,case["chefs"])
        comparison["logicalInputsEqual"]=exact_values(expected_rows,actual_rows)
        summary["selectedFixedInputComparison"]=comparison
        save()
        if not plate_boundary_matches(comparison) or not comparison["logicalInputsEqual"]:raise PlanningError("Selected fixed-input repetition differs from the original native candidate")
        exported=call("controller",{"command":"record-input","path":search_artifact_name(args.out,"selected",base["frame"],"json")},"export-selected")
        recording=json.loads(Path(exported["path"]).read_text(encoding="utf-8-sig"))
        require_recorded_completion(fixed,recording,base["frame"])
        (args.out/"selected-recording.json").write_text(json.dumps(recording,indent=2))
        restore("selected-raw-replay")
        cursor=AdvancingTrace(args.trace)
        call("controller",{"command":"raw-replay","recording":recording},"selected-raw-replay-request")
        replay=settled("selected-raw-replay-end");replay_native=native("selected-raw-replay-native")
        replay_rows=capture_input_evidence(cursor,base["frame"],replay["frame"],case["chefs"],args.out/"selected-raw-replay-raw-capture.json")
        require_recorded_completion(replay,recording,base["frame"])
        final=compare_plate_boundary(fixed,fixed_native,replay,replay_native,case["chefs"])
        final["logicalInputsEqual"]=exact_values(actual_rows,replay_rows)
        summary["selectedRecordingReplayComparison"]=final
        summary["selectedNativeGoal"]=check_goal(case,Observation(replay,replay_native["bridge"]["nativeRound"],replay_native))
        summary["recordingSha256"]=recording["sha256"]
        summary["passed"]=plate_boundary_matches(final) and final["logicalInputsEqual"] and summary["selectedNativeGoal"]["achieved"]
    except Exception as error:
        summary["error"]=str(error)
    finally:
        try:
            if bridge is not None:call("bridge",{"command":"pause"},"finally-pause")
        except Exception as error:summary["pauseError"]=str(error);summary["passed"]=False
        for connection in (bridge,host):
            if connection is not None:
                try:connection.close()
                except Exception as error:summary["closeError"]=str(error);summary["passed"]=False
        try:evidence.finalize(args.out/"observations.json")
        except Exception as error:summary.update(passed=False,evidenceFinalizationError=str(error))
        save()
        print(json.dumps(summary,indent=2))
    return 0 if summary["passed"] else 1


if __name__=="__main__":raise SystemExit(main())
