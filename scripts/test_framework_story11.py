"""Offline native-observation contract tests; no simulated scoring claims."""
import copy
import json
from pathlib import Path
import unittest

from framework_story11_planner import Observation, candidates, staged_proof, DeliveryPlanner, Blocked, validate_observed_layout
from framework_story11_compare import compare_projections, tolerance_kind
from framework_story11_search import select_trial, replay_goal_comparison, require_timer_policy, resume_case


def ingredient(name, identity=100):
    return {"type":"IngredientAssembledNode", "id":identity, "name":name}


def fixture():
    entities = []; registry = []
    def entity(i, role, x, z, chef=False, spawn=None):
        entities.append({"id":i,"path":[i],"exists":True,"position":{"x":x,"y":0,"z":z},
                         "data":{},"chef":{} if chef else None})
        registry.append({"EntityId":i,"Components":[role],"SpawnNames":spawn})
    for i,x in ((43,8),(44,10),(45,18),(46,20)): entity(i,"PlayerControls",x,-5,True)
    entity(30,"ServerIngredientContainer",7.2,-3.6,spawn=["SushiFish"])
    entity(29,"ServerIngredientContainer",21.6,-4.8,spawn=["SushiPrawn"])
    entity(27,"Workstation",9.6,-7.2); entity(28,"Workstation",19.2,-7.2)
    entity(33,"PlateStation",21.6,-1.8)
    entity(21,"AttachStation",10.8,-3.6); entity(22,"AttachStation",18,-3.6)
    entity(1,"ServerPlate",10.8,-3.6); entity(2,"ServerPlate",18,-3.6)
    state={"state":"Paused","frame":31,"requestPending":False,"errors":[],
           "registryValidation":{"layoutValid":True,"errors":[],"semanticGaps":[]},
           "entities":entities,"registry":registry,"typedActions":{"outcome":"complete"}}
    receipt={"bridge":{"paused":True,"loadComplete":True,
        "session":{"scene":"s_sushi_1_1","variantPlayers":4,"serverUsers":4,"clientUsers":4,"virtualPads":4},
        "nativeRound":{"available":True,"configuredDuration":150,"timeLimit":150,"elapsed":.5,"timerSuppressed":False,
            "orders":[{"id":1,"recipe":"Sushi_PlainFish","recipeId":22294,"baseValue":20}],
            "ledger":{"deliveries":0,"baseScore":0,"total":0,"tips":0,"deductions":0,"combo":0,"multiplier":0}}},
        "detail":{"source":"native-server-preparation-composition","entities":[
            {"id":i,"composition":{"type":"CompositeAssembledNode","children":[],"optional":[]}} for i in (1,2)]}}
    attach(state,1,21); attach(state,2,22)
    return state,receipt


def attach(state,item,parent):
    e={r["id"]:r for r in state["entities"] if r["exists"]}
    for row in e.values():
        if (row["data"].get("attachment") or {}).get("path")==e[item]["path"]:
            row["data"].pop("attachment")
    e[item]["data"]["attachmentParent"]={"path":e[parent]["path"]}
    e[parent]["data"]["attachment"]={"path":e[item]["path"]}


def observation(s,r): return Observation(s,r,"s_sushi_1_1")


def staged():
    s,r=fixture(); c=candidates(observation(s,r))[0]
    s["frame"]=91;r["bridge"]["nativeRound"]["elapsed"]=1.5
    s["entities"].append({"id":51,"path":[c["crate"],0],"exists":True,"data":{},"position":{"x":9.6,"y":.5,"z":-7.2},"chef":None})
    s["registry"].append({"EntityId":51,"Name":"SushiFish","Components":["ServerWorkableItem"],"SpawnNames":None})
    r["detail"]["entities"].append({"id":51,"name":"SushiFish","composition":None,"workStage":0,"workSubStage":0,"workStages":8})
    attach(s,51,c["board"]);attach(s,c["plate"],c["helper"])
    return s,r,c


class PlannerTests(unittest.TestCase):
    def test_exact_dynamic_graph_mapping_and_proxy_warning(self):
        s,r,c=staged()
        s["registry"].append({"EntityId":52,"Components":["Rigidbody","ObjectContainer"],"SyncEntityTypes":[47]})
        warning="Unmodeled registered object has a physical collider; layout needs review."
        s["registryValidation"]={"layoutValid":False,"errors":[{"id":i,"reason":warning} for i in (51,52,60)],"semanticGaps":[]}
        s["graphMappingValidation"]={"ok":True,"frame":91,"fixedMappingsValidated":True,
            "spawnedMappings":[{"id":51,"path":[30,0]}],"observedPhysicalContainers":[{"nativeId":52,"logicalPath":[30,0]}],"removedNativeIds":[60]}
        self.assertTrue(staged_proof(observation(s,r),c)["achieved"])
        for mutation in ("missing","stale","unmapped","owner","fixed","live-removed","path","component"):
            a=copy.deepcopy(s);g=a["graphMappingValidation"]
            if mutation=="missing":a.pop("graphMappingValidation")
            if mutation=="stale":g["frame"]-=1
            if mutation=="unmapped":a["registryValidation"]["errors"].append({"id":70,"reason":warning})
            if mutation=="owner":g["observedPhysicalContainers"][0]["logicalPath"]=[29,0]
            if mutation=="fixed":a["registryValidation"]["errors"].append({"id":30,"reason":"Fixed component changed"})
            if mutation=="live-removed":g["removedNativeIds"].append(51)
            if mutation=="path":g["spawnedMappings"][0]["path"]=[30,1]
            if mutation=="component":a["registry"][-1]["Components"]=[]
            with self.subTest(mutation=mutation),self.assertRaises(Blocked):observation(a,r)

    def test_observed_candidates_and_parallel_dependencies(self):
        s,r=fixture(); cs=candidates(observation(s,r));self.assertEqual(len(cs),4)
        self.assertEqual({c["crate"] for c in cs},{30})
        for c in cs:
            self.assertNotEqual(c["chef"],c["helper"])
            fetch,board,plate=c["request"]["actions"]
            self.assertTrue(fetch["expectSpawn"]);self.assertEqual(board["after"],["fetch"])
            self.assertNotIn("after",plate)

    def test_missing_metadata_blocks_no_coordinate_guess(self):
        s,r=fixture()
        for row in s["registry"]:row["SpawnNames"]=None
        with self.assertRaisesRegex(Blocked,"SpawnNames"): candidates(observation(s,r))

    def test_unsupported_order_and_duration(self):
        s,r=fixture();r["bridge"]["nativeRound"]["orders"][0]["recipe"]="Sushi_RiceFish"
        with self.assertRaises(Blocked):candidates(observation(s,r))
        r["bridge"]["nativeRound"]["timeLimit"]=270
        with self.assertRaises(Blocked):observation(s,r)

    def test_two_sided_stage_and_spawn_ownership(self):
        s,r,c=staged(); self.assertTrue(staged_proof(observation(s,r),c)["achieved"])
        for mutation in ("inverse","crate","food","ledger","helper"):
            a,b=copy.deepcopy(s),copy.deepcopy(r);rows={x["id"]:x for x in a["entities"]}
            if mutation=="inverse":rows[c["board"]]["data"]["attachment"]={"path":[777]}
            if mutation=="crate":
                rows[51]["path"]=[29,0];rows[c["board"]]["data"]["attachment"]={"path":[29,0]}
            if mutation=="food":b["detail"]["entities"][-1]["composition"]=ingredient("Prawn")
            if mutation=="ledger":b["bridge"]["nativeRound"]["ledger"]["deductions"]=1
            if mutation=="helper":attach(a,c["plate"],c["chef"])
            with self.subTest(mutation=mutation), self.assertRaises(Blocked):staged_proof(observation(a,b),c)

    def test_native_chop_does_not_complete_on_input_alone(self):
        s,r,c=staged();p=DeliveryPlanner(c);result=p.advance(observation(s,r))
        self.assertEqual([x["type"] for x in result["request"]["actions"]],["interact","wait"])
        s["frame"]+=60;r["bridge"]["nativeRound"]["elapsed"]+=1
        e={x["id"]:x for x in s["entities"]};e[c["chef"]]["chef"]["interactingEntity"]={"path":[c["board"]]}
        result=p.advance(observation(s,r));self.assertEqual([x["type"] for x in result["request"]["actions"]],["wait"])
        s["frame"]+=481
        with self.assertRaisesRegex(Blocked,"timeout"):p.advance(observation(s,r))

    def test_complete_native_assembly_and_delivery_lifecycle(self):
        s,r,c=staged();p=DeliveryPlanner(c);p.advance(observation(s,r))
        next(x for x in s["entities"] if x["id"]==51)["exists"]=False
        s["entities"].append({"id":53,"path":[30,0,0],"exists":True,"data":{},"position":{"x":9.6,"y":.5,"z":-7.2},"chef":None})
        s["registry"].append({"EntityId":53,"Components":["ServerIngredientContainer"],"SpawnNames":None})
        r["detail"]["entities"].append({"id":53,"composition":ingredient("SushiFish",23600)})
        attach(s,53,c["board"])
        request=p.advance(observation(s,r));self.assertEqual(request["request"]["actions"][0]["type"],"prepare-primary")
        with self.assertRaisesRegex(Blocked,"target"):p.advance(observation(s,r))
        e={x["id"]:x for x in s["entities"]};e[c["helper"]]["chef"]["highlightedForPickup"]={"path":[c["board"]]}
        pulse=p.advance(observation(s,r));self.assertEqual(pulse["request"]["command"],"raw-input")
        with self.assertRaisesRegex(Blocked,"consume"):p.advance(observation(s,r))
        e[53]["exists"]=False
        for row in r["detail"]["entities"]:
            if row["id"]==c["plate"]:row["composition"]=ingredient("SushiFish",23600)
        attach(s,c["plate"],c["board"])
        recover=p.advance(observation(s,r));self.assertEqual(recover["request"]["actions"][0]["type"],"pickup")
        attach(s,c["plate"],c["helper"])
        self.assertEqual(p.advance(observation(s,r))["request"]["actions"][0]["target"],c["serve"])
        e[c["helper"]]["chef"]["highlightedForPickup"]={"path":[c["serve"]]}
        p.advance(observation(s,r))
        with self.assertRaisesRegex(Blocked,"ledger"):p.advance(observation(s,r))
        attach(s,c["plate"],c["serve"])
        n=r["bridge"]["nativeRound"];n["orders"]=[];n["ledger"].update(deliveries=1,baseScore=20,total=28,tips=8)
        proof=p.advance(observation(s,r));self.assertTrue(proof["complete"]);self.assertEqual(proof["proof"]["ingredient"],[(23600,"SushiFish")])

    def test_exact_food_consumption_rejects_same_id_still_live(self):
        s,r,c=staged();p=DeliveryPlanner(c,phase="assembly-proof",source=51,prepared_composition=[(23600,"SushiFish")])
        r["detail"]["entities"][0]["composition"]=ingredient("SushiFish",23600)
        with self.assertRaisesRegex(Blocked,"consume"):p.advance(observation(s,r))

    def test_actual_discovery_metadata_and_first_order(self):
        root=Path(__file__).resolve().parents[1]
        p=root/"artifacts/framework-migration/story11-discovery-b/load.json"
        if not p.exists():self.skipTest("Captured native discovery is not present")
        steps=json.loads(p.read_text())["steps"]
        native=steps[5]["response"]["bridge"]["nativeRound"]
        registry=steps[6]["response"]["registry"]
        self.assertEqual(native["configuredDuration"],150);self.assertFalse(native["timerSuppressed"])
        self.assertEqual(native["orders"][0]["recipe"],"Sushi_PlainFish")
        self.assertEqual([(x["EntityId"],x["SpawnNames"]) for x in registry if x.get("SpawnNames")==["SushiFish"]],[(30,["SushiFish"])])
        timer=steps[4]["response"];receipt=steps[5]["response"]
        self.assertEqual(require_timer_policy(timer,receipt)["selected"]["Config"],"Sushi_1_1S_4P")
        for field,value in (("original",0),("after",1),("assetFileWritten",True),("nativeDurationSeconds",270)):
            altered=copy.deepcopy(timer)
            altered["detail"]["result"]["timerPolicy"]["receipts"][-1][field]=value
            with self.subTest(field=field),self.assertRaises(Blocked):require_timer_policy(altered,receipt)

    def test_actual_staged_workable_resume_and_identity_mutations(self):
        folder=Path(__file__).resolve().parents[1]/"artifacts/framework-migration/story11-first-delivery-b"
        if not (folder/"observations.json").exists():self.skipTest("Captured native staged fish is not present")
        rows=json.loads((folder/"observations.json").read_text())
        s=next(x["response"] for x in reversed(rows) if x["target"]=="controller" and x.get("request",{}).get("command")=="inspect")
        r=next(x["response"] for x in reversed(rows) if x["target"]=="settled-pause-observation")
        case,proof=resume_case(folder/"cases.json",0,observation(s,r))
        self.assertEqual(proof["stagedNativeProof"]["frames"],59)
        self.assertIsNone(proof["stagedNativeProof"]["rawComposition"])
        self.assertEqual(proof["stagedNativeProof"]["rawWorkable"]["name"],"SushiFish")
        for key,value in (("name","SushiPrawn"),("workStages",7),("workStage",9),("composition",ingredient("UncookedFish"))):
            changed=copy.deepcopy(r)
            next(x for x in changed["detail"]["entities"] if x["id"]==51)[key]=value
            with self.subTest(key=key),self.assertRaises(Blocked):resume_case(folder/"cases.json",0,observation(s,changed))


class ToleranceTests(unittest.TestCase):
    def check(self,section,field,a,b):
        return compare_projections({section:{"1":{field:a}}},{section:{"1":{field:b}}})

    def test_observed_small_physical_residual_retained(self):
        r=self.check("bodies","centerOfMass",{"z":.0347953},{"z":.03479535})
        self.assertTrue(r["passed"]);self.assertFalse(r["rawEqual"]);self.assertEqual(len(r["rawDifferences"]),1)

    def test_large_or_unknown_physical_change_rejected(self):
        for section,field,a,b in (("entities","position",{"x":0},{"x":.001}),
                                 ("bodies","mass",1,1.00000001),("bodies","sleeping",False,True),
                                 ("bodies","rotation",{"w":1},{"w":-1}),
                                 ("entities","position",{"w":0},{"w":1e-8})):
            with self.subTest(field=field):self.assertFalse(self.check(section,field,a,b)["passed"])

    def test_logical_food_order_time_and_schema_exact(self):
        for section in ("round","clocks","food"):
            self.assertFalse(compare_projections({section:1},{section:1.00000001})["passed"])
        self.assertFalse(self.check("entities","position",{}, {"x":0})["passed"])
        self.assertFalse(self.check("entities","data",{"attachmentParent":1},{"attachmentParent":2})["passed"])
        self.assertFalse(self.check("entities","position",{"x":0},{"x":float("nan")})["passed"])

    def test_selection_uses_actual_native_cost_not_score_or_timeout(self):
        a={"id":"a","eligible":True,"goal":{"nativeSeconds":2,"frames":120}}
        b={"id":"b","eligible":True,"goal":{"nativeSeconds":1.8,"frames":108}}
        self.assertEqual(select_trial([a,b,{"id":"failed","eligible":False}])["id"],"b")
        with self.assertRaises(Blocked):select_trial([])


if __name__=="__main__":unittest.main()
