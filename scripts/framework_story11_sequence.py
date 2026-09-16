"""Bounded ordered Story1-1 delivery continuation; no rewind or corrections.

Default: three ordinary native deliveries using the four initial clean plates.
Returned stacks are deliberately outside admission until their actual stack-top
and pickup-referral observations have a native proof. This is not a full-round
loop or a throughput claim. First-dish mechanics remain separately validated.
"""
import argparse
import copy
import json
from pathlib import Path

from framework_rpc import Client, ControllerClient
from framework_evidence_journal import EvidenceJournal
from framework_story11_search import Runner, require_timer_policy, resume_case
from framework_story11_planner import Observation, DeliveryPlanner, candidates, staged_proof, require, Blocked
from framework_story11_delivery_events import delivery_events, verify_delivery


class InitialPlateObservation(Observation):
    def clean_plate(self,eid):
        # Only plates observed as fixed originals on direct native stations.
        if len(self.entities[eid]["path"])!=1:return False
        parent=self.parent(eid)
        if parent is None or parent in self.chefs:return False
        if "ServerPlateStackBase" in self.components(parent) or "ServerCleanPlateStack" in self.components(parent):return False
        return super().clean_plate(eid)


def choose_next(o, dash=False):
    view=InitialPlateObservation(o.state,o.receipt,o.receipt["bridge"]["session"]["scene"])
    require(all(o.on(c) is None for c in o.chefs),"Sequential delivery requires four observed empty hands at its next admission")
    options=[c for c in candidates(view) if c["dash"] is dash]
    require(bool(options),"No initial clean plate remains; returned stack pickup is not yet natively validated")
    return options[0]


def unique_request(request, prefix):
    request=copy.deepcopy(request)
    if request["command"]=="actions":
        for node in request["actions"]:
            node["id"]=prefix+node["id"]
            if "after" in node:node["after"]=[prefix+x for x in node["after"]]
    return request


def require_completed_history(state):
    for chef in (state.get("actionGraph") or {}).get("chefs",[]):
        for action in chef.get("actions") or []:
            end=(action.get("predictions") or {}).get("endFrame")
            require(type(end) is int and type(state.get("frame")) is int and end<state["frame"],
                    "A prior unfinished/future graph would execute during sequential warmup")


class SequenceRunner(Runner):
    def run(self):
        initial=self.settled("sequence-initial")
        resume_staged=getattr(self.args,"resume_staged",None)
        resume_observed=getattr(self.args,"resume_observed",None)
        resume_source=resume_staged or resume_observed
        require(not resume_source or self.args.warmup==0,
                "Resumed sequence requires --warmup 0")
        require(not (initial.get("typedActions") or {}).get("active") and not (initial.get("rawInput") or {}).get("active"),"An input operation is still active")
        require_completed_history(initial)
        require(initial.get("preventInvalidState") is False,"Native state correction must remain disabled")
        self.call("bridge",{"command":"pause"},"sequence-timer-fence")
        timer=self.call("bridge",{"command":"hot-call","slot":"level-session","operation":"status","args":{}},"sequence-timer-policy")
        self.call("bridge",{"command":"arm"},"sequence-arm")
        if self.args.warmup:self.call("controller",{"command":"step","frames":self.args.warmup},"sequence-warmup")
        current=self.observe("sequence-base");start_elapsed=current.round["elapsed"]
        require(not current.round.get("timerSuppressed"),"Native immediate timer is not active")
        self.summary["timerPolicyProof"]=require_timer_policy(timer,current.receipt)
        base_restores=current.receipt["bridge"]["authoringClockRestores"]
        self.summary.update(classification="Three sequential native ordered deliveries; no rewind; initial fixed clean plates only",deliveries=[],initialLedger=copy.deepcopy(current.round["ledger"]))
        for meal in range(self.args.deliveries):
            require(current.round["remaining"]>0,"Native round ended before the requested delivery count")
            require(current.receipt["bridge"]["authoringClockRestores"]==base_restores,"Unexpected authoring warp during ordinary sequence")
            if meal==0 and resume_source:
                case,proof=resume_case(resume_source,self.args.candidate,current,allow_prepared=bool(resume_observed))
                self.summary["resumedPreparation"]=proof
                self.summary["classification"] += "; first preparation is independently revalidated from retained native evidence"
            else:
                case=choose_next(current,self.args.dash)
            flow_ids=current.role("ServerCampaignFlowController")
            require(len(flow_ids)==1,"Actual native campaign flow identity is absent or ambiguous")
            prefix=f"meal-{meal}-"
            if not (meal==0 and resume_source):
                request=unique_request(case["request"],prefix)
                current,_=self.execute(request,prefix+"prep",current)
                staged_proof(current,case)
            planner=DeliveryPlanner(case);capture_paths=[]
            if meal==0 and resume_source and proof["resumePhase"]=="prepare-assembly":
                staged=proof["stagedNativeProof"]
                planner.phase="prepare-assembly";planner.source=staged["preparedId"]
                planner.prepared_composition=staged["preparedComposition"]
                planner.raw_source=staged["rawId"];planner.raw_path=staged["rawPath"]
            for n in range(64):
                planned=planner.advance(current)
                if planned.get("complete"):
                    events=delivery_events([json.loads(p.read_text()) for p in capture_paths],current.state["registry"])
                    checked=verify_delivery(case,case["initialLedger"],current.round["ledger"],events,flow_ids[0])
                    self.summary["deliveries"].append(dict(checked,recipe=case["head"]["recipe"],mealFrames=current.frame-case["startFrame"],mealNativeSeconds=current.round["elapsed"]-case["startElapsed"]))
                    self.save();break
                label=prefix+f"batch-{n:02d}"
                was_service=planned["purpose"]=="Ordinary native delivery edge"
                current,_=self.execute(unique_request(planned["request"],label+"-"),label,current)
                if was_service:capture_paths.append(self.args.out/(label+"-raw-capture.json"))
                require(current.round["elapsed"]>=start_elapsed and current.round["configuredDuration"]==150,"Native elapsed time or configured round changed")
                require(current.receipt["bridge"]["authoringClockRestores"]==base_restores,"Unexpected authoring warp during ordinary sequence")
            else:raise Blocked("Sequential meal exceeded bounded batch count")
        self.summary.update(passed=True,finalLedger=current.round["ledger"],nativeSeconds=current.round["elapsed"]-start_elapsed,
            fullRoundAvailable=False,fullRoundBlocker="Returned CleanPlateStack top/referral pickup needs actual observation and native transfer proof; no inferred plate reuse")
        self.save()


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("--out",type=Path,required=True);p.add_argument("--trace",type=Path,required=True)
    p.add_argument("--bridge-port",type=int,default=17636);p.add_argument("--controller-port",type=int,default=17637)
    p.add_argument("--scene",default="s_sushi_1_1");p.add_argument("--warmup",type=int,default=0,choices=range(61))
    p.add_argument("--deliveries",type=int,default=3,choices=range(1,4));p.add_argument("--dash",action="store_true")
    resume=p.add_mutually_exclusive_group()
    resume.add_argument("--resume-staged",type=Path,help="Resume the currently staged exact-input preparation from retained cases")
    resume.add_argument("--resume-observed",type=Path,help="Resume exact currently prepared native food from retained cases")
    p.add_argument("--candidate",type=int,default=0,choices=range(4))
    a=p.parse_args();a.search=False;a.out.mkdir(parents=True,exist_ok=False)
    journal=EvidenceJournal(a.out/"observations.jsonl");bridge=host=runner=None;error=None
    try:
        bridge=Client(a.bridge_port);host=ControllerClient(a.controller_port)
        runner=SequenceRunner(a,bridge,host,journal);runner.run()
    except Exception as exc:
        error=str(exc)
        if runner:runner.summary.update(passed=False,error=error)
    finally:
        if bridge:
            try:
                if runner:runner.call("bridge",{"command":"pause"},"finally-pause")
                else:bridge.call({"command":"pause"})
            except Exception as exc:
                if runner:runner.summary.update(passed=False,pauseError=str(exc))
        for connection in (bridge,host):
            if connection:connection.close()
        journal.finalize(a.out/"observations.json")
        if runner:runner.save();print(json.dumps(runner.summary,indent=2))
        else:(a.out/"summary.json").write_text(json.dumps({"passed":False,"error":error}))
    return 0 if runner and runner.summary["passed"] else 1


if __name__=="__main__":raise SystemExit(main())
