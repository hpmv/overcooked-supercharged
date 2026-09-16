"""File-only goal, trace and selected-input replay preflight tests."""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/"scripts"))
from test_framework_kitchen_planner import Fixture,tree
import framework_plate_search as ps
import framework_kitchen_planner as kp


def setup():
    f=Fixture()
    f.e(95)["position"]["z"]=-15.6;f.e(10)["position"]["z"]=-15.6
    for collection in (f.s["registry"],f.s["initialRegistry"]):
        for entry in collection:
            if entry["EntityId"]==95:entry["Pos"]["Z"]=-15.6
            if entry["EntityId"] in [90,95]:entry["Components"]=["AttachStation"]
    # This local plate fixture now models both native attachment directions.
    # The general scheduler fixture intentionally models only child ownership.
    old_parent=f.parent
    def paired_parent(eid,target):
        path=f.e(eid)["path"]
        for entity in f.s["entities"]:
            if entity["data"].get("attachment",{}).get("path")==path:
                entity["data"].pop("attachment")
        old_parent(eid,target)
        f.e(target)["data"]["attachment"]={"path":list(path)}
    f.parent=paired_parent
    f.e(95)["data"]["attachment"]={"path":[10]}
    return f


def frames():
    result=[]
    previous=False
    for i,down in enumerate([True,False,False,False]):
        inputs={}
        for chef in (103,104,105,106):
            pressed=down and chef==103
            inputs[str(chef)]={"Pad":{"X":0,"Y":0},"Pickup":{"Down":pressed,"JustPressed":pressed and not previous,"JustReleased":previous and not pressed and chef==103},
                               "Interact":{"Down":False,"JustPressed":False,"JustReleased":False},"Dash":{"Down":False,"JustPressed":False,"JustReleased":False}}
        result.append({"ordinal":i,"nextFrame":101+i,"inputs":inputs});previous=down
    return result


def physics_receipt(f):
    bodies=[]
    for chef in (103,104,105,106):
        zero={"x":0,"y":0,"z":0}
        bodies.append({"entityId":chef,"bodyInstanceId":-chef,"position":copy.deepcopy(f.e(chef)["position"]),
                       "rotation":{"x":0,"y":0,"z":0,"w":1},"rawVelocity":dict(zero),"rawAngularVelocity":dict(zero),
                       "resumeVelocity":dict(zero),"resumeAngularVelocity":dict(zero),"rawIsKinematic":True,
                       "rawUseGravity":False,"sleeping":False,"frozenByNativeTimeManager":True,
                       "resumeIsKinematic":False,"resumeUseGravity":False})
    return {"bridge":{"nativeRound":copy.deepcopy(f.n),"nativePhysics":{
        "source":"native-rigidbody-and-TimeManager.FrozenPhysicsData","bodies":bodies}},
        "detail":{"entities":copy.deepcopy(list(f.food.values()))}}


class PlateSearchTests(unittest.TestCase):
    def test_status_polling_materializes_only_one_full_paused_boundary(self):
        observed=[{"state":"Running","requestPending":True,"frame":1},
                  {"state":"Paused","requestPending":True,"frame":2},
                  {"state":"Paused","requestPending":False,"frame":2}]
        host=Mock();host.status.side_effect=observed
        full={**observed[-1],"entities":[{"id":103}]};inspect=Mock(return_value=full);receipts=[]
        with patch.object(ps.time,"sleep"):
            self.assertIs(ps.settled_status(host,inspect,receipts.append),full)
        self.assertEqual(receipts,observed);self.assertEqual(host.status.call_count,3)
        inspect.assert_called_once_with();host.call.assert_not_called()

    def test_status_errors_trace_failure_and_full_pause_race_fail_closed(self):
        for state in ({"state":"Error"},{"state":"Paused","errors":["native failure"]},
                      {"state":"Paused","traceFailure":"trace stopped"}):
            host=Mock();host.status.return_value=state;inspect=Mock();receipts=[]
            with self.subTest(state=state),self.assertRaises(kp.PlanningError):
                ps.settled_status(host,inspect,receipts.append)
            self.assertEqual(receipts,[state]);inspect.assert_not_called()
        for changed in ({"state":"Running"},{"state":"Paused","requestPending":True},
                        {"state":"Paused","traceFailure":"late failure"}):
            host=Mock();host.status.return_value={"state":"Paused","requestPending":False}
            with self.subTest(changed=changed),self.assertRaises(kp.PlanningError):
                ps.settled_status(host,lambda:changed,lambda _:None)

    def test_lightweight_wait_still_has_bounded_timeout(self):
        host=Mock();host.status.return_value={"state":"Running","requestPending":True};inspect=Mock()
        with self.assertRaises(TimeoutError):ps.settled_status(host,inspect,lambda _:None,timeout=-1)
        inspect.assert_not_called()

    def test_distinct_output_directories_namespace_host_create_new_files(self):
        with tempfile.TemporaryDirectory() as tmp:
            first=Path(tmp)/"run-a";second=Path(tmp)/"run-b"
            names=[ps.search_artifact_name(out,kind,31,suffix) for out in (first,second)
                   for kind,suffix in (("base","pb"),("selected","json"))]
            self.assertEqual(len(set(names)),4)
            self.assertEqual(names[0],ps.search_artifact_name(first,"base",31,"pb"))
            self.assertTrue(all(Path(name).name==name and "31" in name for name in names))

    def test_initial_and_restored_graph_must_be_empty_before_search(self):
        empty={"actionGraph":{"chefs":[{"actions":[]}]},"typedActions":{"active":False},"rawInput":{"active":False}}
        ps.require_empty_search_graph(empty)
        for field in ("actionGraph","typedActions","rawInput"):
            state=copy.deepcopy(empty)
            if field=="actionGraph":state[field]["chefs"][0]["actions"]=[{"id":"old-completed-node"}]
            else:state[field]["active"]=True
            with self.subTest(field=field),self.assertRaises(kp.PlanningError):ps.require_empty_search_graph(state)

    def test_failed_input_validation_retains_original_exchanges_before_conversion(self):
        for mutation in ("duplicate","inconsistent-edge"):
            with self.subTest(mutation=mutation),tempfile.TemporaryDirectory() as tmp:
                trace=Path(tmp)/"exchange.jsonl";trace.write_text("");cursor=ps.AdvancingTrace(trace)
                rows=frames()
                if mutation=="duplicate":rows.append(copy.deepcopy(rows[-1]))
                else:rows[1]["inputs"]["103"]["Pickup"]["JustPressed"]=True
                wire=[{"kind":"exchange","input":{"NextFrame":r["nextFrame"],"Input":r["inputs"]},
                       "output":{"FrameNumber":r["nextFrame"]-1,"ServerMessages":["unchanged-native-receipt"]}} for r in rows]
                trace.write_text("".join(json.dumps(r)+"\n" for r in wire))
                output=Path(tmp)/"raw-capture.json"
                if mutation=="duplicate":
                    with self.assertRaises(kp.PlanningError):ps.capture_input_evidence(cursor,100,104,[103,104,105,106],output)
                else:
                    extracted=ps.capture_input_evidence(cursor,100,104,[103,104,105,106],output)
                    with self.assertRaises(kp.PlanningError):ps.to_raw_request(extracted,[103,104,105,106])
                evidence=json.loads(output.read_text())
                self.assertEqual(evidence["rawExchanges"],wire)
                self.assertEqual(evidence["traceByteOffset"],trace.stat().st_size)
                if mutation=="duplicate":self.assertEqual(evidence["validation"],"failed")
                else:self.assertEqual(evidence["inputs"],rows)

    def test_runner_finalizes_all_receipts_after_rejecting_old_graph_without_arming(self):
        with tempfile.TemporaryDirectory() as tmp:
            output=Path(tmp)/"trial";trace=Path(tmp)/"exchange.jsonl";trace.write_text("")
            bridge=Mock();host=Mock()
            host.status.return_value={"state":"Paused","requestPending":False,"frame":1}
            state={**host.status.return_value,"actionGraph":{"chefs":[{"actions":[{"id":"old"}]}]}}
            host.call.return_value=state;bridge.call.return_value={"bridge":{"paused":True}}
            argv=["framework_plate_search.py","--out",str(output),"--trace",str(trace)]
            with patch.object(sys,"argv",argv),patch.object(ps,"Client",return_value=bridge), \
                    patch.object(ps,"ControllerClient",return_value=host),patch("builtins.print"):
                self.assertEqual(ps.main(),1)
            bridge.call.assert_called_once_with({"command":"pause"})
            host.call.assert_called_once_with({"command":"inspect","full":True})
            journal=output/"observations.jsonl";compat=output/"observations.json"
            rows=[json.loads(line) for line in journal.read_text().splitlines()]
            self.assertEqual(json.loads(compat.read_text()),rows)
            self.assertEqual([r["label"] for r in rows],["initial-status","initial","finally-pause"])
            summary=json.loads((output/"summary.json").read_text())
            self.assertFalse(summary["passed"]);self.assertIn("Clear the previous action graph",summary["error"])
            self.assertEqual(summary["observationEvidence"]["records"],3)
            self.assertEqual(summary["observationEvidence"]["compatibility"]["records"],3)
            self.assertEqual(compat.read_text().count("\n"),1)

    def test_actual_s_goal_requires_output_source_and_chef_inverse_receipts(self):
        folder=ROOT/"artifacts/framework-migration/native-s/plate-search"
        evidence=json.loads((folder/"observations.json").read_text())
        case=json.loads((folder/"case.json").read_text())
        def get(label):return next(r["response"] for r in evidence if r["label"]==label)
        end=get("chef-103-walk-end");native=get("chef-103-walk-native")
        def goal(state):return ps.check_goal(case,kp.Observation(state,native["bridge"]["nativeRound"],native))["achieved"]
        self.assertTrue(goal(end))
        # Actual initial attachment, synthetic elapsed/end frame: a refused
        # native pickup is not made successful by identical replay endpoints.
        no_pickup=copy.deepcopy(get("base"));no_pickup["frame"]=end["frame"]
        self.assertFalse(goal(no_pickup))
        for parent,path in [(case["target"],[12]),(case["target"],[]),(case["source"],[case["plate"]])] \
                +[(chef,[case["plate"]]) for chef in case["chefs"]]:
            with self.subTest(parent=parent,path=path):
                state=copy.deepcopy(end)
                next(e for e in state["entities"] if e["id"]==parent)["data"]["attachment"]={"path":path}
                self.assertFalse(goal(state))

    def test_exact_private_clock_pairs_are_compared_and_partial_data_rejected(self):
        f=setup();receipt=physics_receipt(f)
        receipt["bridge"]["nativeCheckpoints"]={"nativeServerClock":[30.0,27.95,27.95],
                                                 "nativeClientClock":[.016,27.95,27.95,27.0,0.0,0.0]}
        same=ps.compare_plate_boundary(f.s,receipt,f.s,copy.deepcopy(receipt),[103,104,105,106])
        self.assertTrue(ps.plate_boundary_matches(same));self.assertTrue(same["nativePrivateClocksObserved"])
        for key in ("nativeServerClock","nativeClientClock"):
            actual=copy.deepcopy(receipt);actual["bridge"]["nativeCheckpoints"][key][0]+=.0000001
            changed=ps.compare_plate_boundary(f.s,receipt,f.s,actual,[103,104,105,106])
            self.assertFalse(ps.plate_boundary_matches(changed))
            self.assertTrue(changed["nativePrivateClocksFirstDifference"]["field"].startswith("$nativePrivateClocks/"))
        actual=copy.deepcopy(receipt);actual["bridge"].pop("nativeCheckpoints")
        self.assertFalse(ps.plate_boundary_matches(ps.compare_plate_boundary(f.s,receipt,f.s,actual,[103,104,105,106])))
        actual=copy.deepcopy(receipt);actual["bridge"]["nativeCheckpoints"].pop("nativeClientClock")
        with self.assertRaises(kp.PlanningError):ps.compare_plate_boundary(f.s,receipt,f.s,actual,[103,104,105,106])

    def test_native_physics_exact_baseline_and_selected_replay_barrier(self):
        f=setup();receipt=physics_receipt(f)
        result=ps.compare_plate_boundary(f.s,receipt,copy.deepcopy(f.s),copy.deepcopy(receipt),[103,104,105,106])
        self.assertTrue(ps.plate_boundary_matches(result));self.assertTrue(result["nativePhysicsEqual"])
        self.assertEqual(result["expectedNativePhysicsSha256"],result["observedNativePhysicsSha256"])
        result.pop("nativePhysicsEqual")
        self.assertFalse(ps.plate_boundary_matches(result))

    def test_missing_null_empty_or_incomplete_native_physics_never_passes(self):
        for mutation in ("missing","null","empty","source","chef","resume","nonfinite","duplicate"):
            f=setup();receipt=physics_receipt(f);physics=receipt["bridge"]["nativePhysics"]
            if mutation=="missing":receipt["bridge"].pop("nativePhysics")
            elif mutation=="null":receipt["bridge"]["nativePhysics"]=None
            elif mutation=="empty":physics["bodies"]=[]
            elif mutation=="source":physics["source"]="unverified"
            elif mutation=="chef":physics["bodies"].pop()
            elif mutation=="resume":physics["bodies"][0].pop("resumeVelocity")
            elif mutation=="nonfinite":physics["bodies"][0]["position"]["x"]=float("inf")
            else:physics["bodies"].append(copy.deepcopy(physics["bodies"][0]))
            with self.subTest(mutation=mutation),self.assertRaises(kp.PlanningError):
                ps.compare_plate_boundary(f.s,receipt,f.s,receipt,[103,104,105,106])

    def test_resume_velocity_sleep_identity_and_quaternion_changes_fail_exactly(self):
        for mutation in ("resume","sleep","instance","rotation","signed-zero"):
            f=setup();original=physics_receipt(f);actual=copy.deepcopy(original);body=actual["bridge"]["nativePhysics"]["bodies"][0]
            if mutation=="resume":body["resumeVelocity"]["x"]=.000001
            elif mutation=="sleep":body["sleeping"]=True
            elif mutation=="instance":body["bodyInstanceId"]-=1000
            elif mutation=="rotation":body["rotation"]["w"]=-1
            else:body["rawVelocity"]["x"]=-0.0
            result=ps.compare_plate_boundary(f.s,original,f.s,actual,[103,104,105,106])
            with self.subTest(mutation=mutation):
                self.assertFalse(ps.plate_boundary_matches(result));self.assertFalse(result["nativePhysicsEqual"])
                self.assertTrue(result["nativePhysicsFirstDifference"]["field"].startswith("$nativePhysics/bodies/0/"))

    def test_fixed_case_emits_baseline_and_alternate_native_chefs_dash_choices(self):
        f=setup();case=ps.prepare_case(f.observe())
        self.assertEqual(case["plate"],10);self.assertEqual(case["source"],95);self.assertEqual(case["target"],90)
        self.assertEqual(len(case["candidates"]),4)
        self.assertEqual({c["chef"] for c in case["candidates"]},{103,106})
        self.assertEqual(case["candidates"][0]["dash"],False)
        for c in case["candidates"]:
            actions=c["request"]["actions"]
            self.assertEqual([a["type"] for a in actions],["goto","wait","pickup","goto","wait","place"])
            self.assertAlmostEqual(actions[0]["x"],18.1);self.assertEqual(actions[0]["z"],-15.6)
            self.assertAlmostEqual(actions[3]["z"],-12.0)
            self.assertEqual(actions[2]["after"],["source-settle"])
            self.assertEqual(actions[5]["after"],["target-settle"])
            self.assertEqual(actions[2]["target"],10)
            self.assertFalse(actions[2]["dash"]);self.assertFalse(actions[5]["dash"])

    def test_captured_q_timeout_is_a_retained_failure_not_a_success(self):
        path=ROOT/"artifacts/framework-migration/native-q/plate-search/observations.json"
        evidence=json.loads(path.read_text())
        def get(label):return next(r["response"] for r in evidence if r["label"]==label)
        base=get("base");receipt=get("base-native");end=get("chef-103-walk-end");end_native=get("chef-103-walk-native")
        case=ps.prepare_case(kp.Observation(base,receipt["bridge"]["nativeRound"],receipt))
        result=ps.classify_candidate(case,case["candidates"][0],kp.Observation(end,end_native["bridge"]["nativeRound"],end_native))
        self.assertFalse(result["eligible"]);self.assertFalse(result["goal"]["achieved"])
        self.assertEqual(result["classification"],"ordinary bounded timeout")
        self.assertEqual(result["error"],"Action timeout: stage")
        self.assertTrue(result["nextCandidateRequiresExactVerifiedRollback"])
        self.assertEqual(result["goal"]["frames"],463)

    def test_completed_edges_without_native_goal_remain_ineligible(self):
        f=setup();case=ps.prepare_case(f.observe());f.s["frame"]+=20;f.n["elapsed"]+=.4
        f.s["typedActions"]={"outcome":"complete","error":None}
        result=ps.classify_candidate(case,case["candidates"][0],f.observe())
        self.assertFalse(result["eligible"])
        self.assertEqual(result["classification"],"completed input graph without native goal")
        f.parent(10,case["target"])
        self.assertTrue(ps.classify_candidate(case,case["candidates"][0],f.observe())["eligible"])

    def test_timeout_can_retain_plate_on_source_in_own_hands_or_exact_output(self):
        for parent in (95,103,90):
            f=setup();case=ps.prepare_case(f.observe());f.parent(10,parent)
            f.s["frame"]+=100;f.n["elapsed"]+=2
            f.s["typedActions"]={"outcome":"failed","error":"Action timeout: stage"}
            result=ps.classify_candidate(case,case["candidates"][0],f.observe())
            self.assertFalse(result["eligible"])
            self.assertTrue(result["fixedReservationEnvelopeVerified"])

    def test_unexpected_candidate_state_cannot_be_downgraded_to_timeout(self):
        for mutation in ("other-chef","floor","other-attachment","extra-entity","food","error","running","invalid","trace"):
            f=setup();case=ps.prepare_case(f.observe());f.s["frame"]+=100;f.n["elapsed"]+=2
            f.s["typedActions"]={"outcome":"failed","error":"Action timeout: stage"}
            if mutation=="other-chef":f.parent(10,104)
            elif mutation=="floor":f.e(10)["data"].pop("attachmentParent",None)
            elif mutation=="other-attachment":f.parent(90,103)
            elif mutation=="extra-entity":f.add(201,"counter",20,-13)
            elif mutation=="food":f.food[10]["composition"]=tree(kp.BUN)
            elif mutation=="error":f.s["typedActions"]["error"]="Unexpected native interaction failure"
            elif mutation=="running":f.s["typedActions"]["outcome"]="running"
            elif mutation=="invalid":f.s["invalidStateReason"]="native failure"
            else:f.s["traceFailure"]="recording lost"
            with self.subTest(mutation=mutation),self.assertRaises(kp.PlanningError):
                ps.classify_candidate(case,case["candidates"][0],f.observe())

    def test_fixed_only_gate_rejects_new_ingredient_and_unknown_cannon_aux(self):
        f=setup();f.add(200,"bread",path=[60,0])
        with self.assertRaisesRegex(kp.PlanningError,"fixed entities only"):ps.prepare_case(f.observe())
        f=setup();f.add(110,"cannon",8.4,-14.4,nativeCannon={"observedAux":False})
        with self.assertRaisesRegex(kp.PlanningError,"cannons"):ps.prepare_case(f.observe())

    def test_exact_native_clean_plate_goal_and_duration(self):
        f=setup();case=ps.prepare_case(f.observe());f.parent(10,90);f.s["frame"]+=100;f.n["elapsed"]+=100/60
        result=ps.check_goal(case,f.observe())
        self.assertTrue(result["achieved"]);self.assertEqual(result["frames"],100)
        self.assertAlmostEqual(result["nativeSeconds"],100/60)

    def test_goal_rejects_identity_contents_and_ledger_mutation(self):
        for mutation in ("identity","food","ledger"):
            f=setup();case=ps.prepare_case(f.observe());f.parent(10,90);f.s["frame"]+=100;f.n["elapsed"]+=2
            if mutation=="identity":f.e(10)["path"]=[10,1]
            if mutation=="food":f.food[10]["composition"]=tree(kp.BUN)
            if mutation=="ledger":f.n["ledger"]["total"]=1
            with self.subTest(mutation=mutation),self.assertRaises(kp.PlanningError):ps.check_goal(case,f.observe())

    def test_input_conversion_preserves_exact_edges_and_two_release_frames(self):
        rows=frames();raw=ps.to_raw_request(rows,[103,104,105,106])
        self.assertEqual(sum(s["frames"] for s in raw["segments"]),2)
        self.assertTrue(raw["segments"][0]["chefs"]["103"]["pickup"])
        self.assertFalse(raw["segments"][1]["chefs"]["103"]["pickup"])
        self.assertEqual(ps.input_report(rows)["pickupPresses"],1)

    def test_missing_chef_nonfinite_duplicate_edges_and_non_neutral_tail_reject(self):
        for mutation in ("chef","axis","edge","tail","ordinal"):
            rows=frames()
            if mutation=="chef":del rows[0]["inputs"]["104"]
            if mutation=="axis":rows[0]["inputs"]["103"]["Pad"]["X"]=float("nan")
            if mutation=="edge":rows[1]["inputs"]["103"]["Pickup"]["JustPressed"]=True
            if mutation=="tail":rows[-1]["inputs"]["103"]["Pad"]["X"]=.1
            if mutation=="ordinal":rows[1]["ordinal"]=10
            with self.subTest(mutation=mutation),self.assertRaises(kp.PlanningError):ps.to_raw_request(rows,[103,104,105,106])

    def test_v6_incremental_reader_skips_only_paused_blocks_and_retains_all_inputs(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/"exchange.jsonl";path.write_text('{"kind":"session"}\n')
            cursor=ps.AdvancingTrace(path);rows=frames()
            with path.open("a") as stream:
                stream.write(json.dumps({"kind":"paused-exchanges","version":1,"encoding":"brotli-jsonl","count":100,"data":"not read by advancing-only extractor"})+"\n")
                for r in rows:stream.write(json.dumps({"kind":"exchange","input":{"NextFrame":r["nextFrame"],"Input":r["inputs"]}})+"\n")
            actual=cursor.frames(100,104,[103,104,105,106])
            self.assertEqual(actual,rows);self.assertEqual(cursor.paused_blocks_skipped,1)
            self.assertEqual(cursor.frames(100,104,[103,104,105,106]),rows)

    def test_trace_rejects_missing_duplicate_and_authoring_frames(self):
        for mutation in ("missing","duplicate","warp","seed"):
            with self.subTest(mutation=mutation),tempfile.TemporaryDirectory() as folder:
                path=Path(folder)/"e.jsonl";path.write_text("");cursor=ps.AdvancingTrace(path);rows=frames()
                if mutation=="missing":rows.pop()
                if mutation=="duplicate":rows.append(copy.deepcopy(rows[-1]))
                wire=[]
                for r in rows:
                    value={"NextFrame":r["nextFrame"],"Input":r["inputs"]}
                    if mutation=="warp":value["Warp"]={"Frame":1}
                    if mutation=="seed":value.update(ResetOrderSeed=0,__isset={"resetOrderSeed":True})
                    wire.append({"kind":"exchange","input":value})
                path.write_text("".join(json.dumps(r)+"\n" for r in wire))
                with self.assertRaises(kp.PlanningError):cursor.frames(100,104,[103,104,105,106])


if __name__=="__main__":unittest.main()
