"""Planner invariants on controlled observations; no fake game score evidence."""
import copy
import json
from pathlib import Path
import sys
import unittest

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/"scripts"))
import framework_kitchen_planner as kp


def leaf(ingredient):return {"type":"IngredientAssembledNode","id":ingredient}
def tree(*ingredients,state=None,mixed=False):
    value={"type":"CompositeAssembledNode","children":[leaf(i) for i in ingredients],"optional":[]}
    if mixed:value={"type":"MixedCompositeAssembledNode","state":"Mixed","children":[value],"optional":[]}
    if state:value={"type":"CookedCompositeAssembledNode","state":state,"children":[value],"optional":[]}
    return value


class Fixture:
    def __init__(self,*recipes):
        self.s={"state":"Paused","frame":1,"requestPending":False,"errors":[],"freshLevelLoadObserved":True,
                "registryValidation":{"layoutValid":True,"errors":[],"semanticGaps":[]},
                "entities":[],"registry":[],"initialRegistry":[]}
        self.n={"available":True,"gameState":"InLevel","elapsed":.02,"ledger":{"deliveries":0,"baseScore":0,"total":0,"tips":0},
                "orders":[{"id":i+1,"recipeId":r,"remaining":130.,"baseValue":60} for i,r in enumerate(recipes or [158500])]}
        self.food={};self.roles={}
        for i,(name,(kind,x,z,component)) in enumerate(kp.ROLE_SPECS.items(),20):
            self.roles[name]=i;self.add(i,kind,x,z,components=[component])
        for i,(ingredient,kind) in enumerate(kp.CRATE_CLASS.items(),60):
            left=ingredient in {kp.BUN,kp.SAUSAGE,kp.ONION}
            self.add(i,kind,12 if left else 29,-10.8,spawns=[kp.CRATE_SPAWN[ingredient]])
        for i,(kind,x,z) in enumerate([("pot",16.8,-10.8),("pot",18,-10.8),("pan",16.8,-21.6),("pan",18,-21.6),
                                      ("mixer",22.8,-10.8),("mixer",24,-10.8),("frier",22.8,-21.6),("frier",24,-21.6)],2):
            home=80+i;self.add(home,"mixer-station" if kind=="mixer" else "heat-station",x,z)
            self.add(i,kind,x,z,parent=home,composition=tree())
            self.food[i].update(cookingProgress=0,cookingTime=12 if kind in {"pot","pan"} else 10,mixingProgress=0,mixingTime=12)
        for i,x,z in [(103,18,-17),(104,29,-13),(105,12,-13),(106,23,-16)]:
            self.add(i,"chef",x,z,components=["PlayerControls"],chef={"dashTimer":0,"impactTimer":0})
        for i,x,z in [(90,19.2,-10.8),(91,21.6,-10.8),(92,19.2,-21.6),(93,21.6,-21.6)]:self.add(i,"counter",x,z)
        self.add(95,"counter",19.2,-16.8)
        self.add(10,"plate",19.2,-16.8,parent=95,composition=tree())

    def add(self,eid,kind,x=20,z=-15,parent=None,composition=None,components=(),spawns=(),path=None,**extra):
        e={"id":eid,"path":path or [eid],"exists":True,"className":kind,"position":{"x":x,"y":0,"z":z},
           "data":{},"velocity":{"x":0,"y":0,"z":0},"chef":None,**extra}
        if parent is not None:e["data"]["attachmentParent"]={"path":self.e(parent)["path"]}
        r={"EntityId":eid,"Name":kind,"Pos":{"X":x,"Y":0,"Z":z},"Components":list(components),"SpawnNames":list(spawns)}
        self.s["entities"].append(e);self.s["registry"].append(r);self.s["initialRegistry"].append(copy.deepcopy(r))
        if composition is not None:self.food[eid]={"id":eid,"composition":composition}
        return e

    def e(self,eid):return next(e for e in self.s["entities"] if e["id"]==eid)
    def parent(self,eid,target):
        self.e(eid)["data"]["attachmentParent"]={"path":self.e(target)["path"]}
    def remove(self,eid):
        self.s["entities"]=[e for e in self.s["entities"] if e["id"]!=eid]
        self.s["registry"]=[e for e in self.s["registry"] if e["EntityId"]!=eid]
        self.food.pop(eid,None)
    def observe(self):return kp.Observation(copy.deepcopy(self.s),copy.deepcopy(self.n),copy.deepcopy(self.food))
    def receipt(self,batch,frames=10):
        self.s["frame"]+=frames;self.n["elapsed"]+=frames/60
        self.s["typedActions"]={"outcome":"complete","actions":[{"id":a["id"],"endFrame":self.s["frame"]-1} for a in batch["request"]["actions"]]}


class SchedulerTests(unittest.TestCase):
    def test_native_orders_only_and_dag_dependencies(self):
        f=Fixture(47642,228996);p=kp.Scheduler(f.observe());batch=p.advance(f.observe())
        tasks=batch["tasks"]["1"];keys={t["key"] for t in tasks}
        self.assertTrue(all(set(t["after"])<=keys for t in tasks))
        onion=next(t for t in tasks if t["key"]=="order:1:onion:cook")
        self.assertEqual(onion["after"],("order:1:onion:chop",))
        donut=batch["tasks"]["2"]
        self.assertEqual(next(t for t in donut if t["kind"]=="fry")["after"],("order:2:mix",))
        self.assertEqual(len(p.orders),2)

    def test_initial_useful_parallel_suppliers_without_invented_ids(self):
        f=Fixture(158500,228996);p=kp.Scheduler(f.observe());batch=p.advance(f.observe())
        actions=batch["request"]["actions"]
        self.assertEqual({a["chef"] for a in actions},{104,105})
        self.assertTrue(all(a["type"]=="pickup" and a["expectSpawn"] for a in actions))
        for a in actions:self.assertIn(a["target"],{r["EntityId"] for r in f.s["registry"]})
        self.assertEqual(len(p.leases),len(set(p.leases)))

    def test_same_boundary_does_not_emit_duplicate_job_or_input(self):
        f=Fixture();p=kp.Scheduler(f.observe());p.advance(f.observe());count=len(p.jobs)
        again=p.advance(f.observe());self.assertIsNone(again["request"]);self.assertEqual(len(p.jobs),count)

    def test_spawn_requires_original_crate_path_and_exact_held_identity(self):
        f=Fixture();p=kp.Scheduler(f.observe());a=p.advance(f.observe());job=p.jobs[105]
        f.add(200,"bread",parent=105,path=[999,0],composition=tree())
        f.receipt(a);b=p.advance(f.observe())
        self.assertIsNone(b["request"]);self.assertEqual(job.phase,"take")
        f.e(200)["path"]=[job.source,0]
        f.s["frame"]+=1;f.n["elapsed"]+=1/60
        b=p.advance(f.observe());self.assertEqual(job.phase,"put")
        self.assertEqual(b["request"]["actions"][0]["target"],f.roles["bun-board"])
        self.assertEqual(p.claims[f.observe().token(200)],1)
        self.assertEqual(p.leases[200],job.key)

    def test_native_spawn_place_retains_order_claim_and_releases_job(self):
        f=Fixture();p=kp.Scheduler(f.observe());a=p.advance(f.observe());job=p.jobs[105]
        f.add(200,"bread",parent=105,path=[job.source,0],composition=tree())
        f.receipt(a);b=p.advance(f.observe());f.parent(200,f.roles["bun-board"]);f.receipt(b)
        out=p.advance(f.observe())
        self.assertEqual(p.claims[f.observe().token(200)],1)
        self.assertNotIn(job.key,p.leases.values())
        self.assertNotEqual(p.jobs.get(105),job)

    def test_native_postcondition_wait_does_not_repeat_finished_edge(self):
        f=Fixture();p=kp.Scheduler(f.observe());a=p.advance(f.observe());f.receipt(a)
        b=p.advance(f.observe());self.assertIsNone(b["request"])
        f.s["frame"]+=181;f.n["elapsed"]+=181/60
        with self.assertRaisesRegex(kp.PlanningError,"timeout"):p.advance(f.observe())

    def test_id_reuse_and_lost_reservation_fail_closed(self):
        f=Fixture();p=kp.Scheduler(f.observe());p.advance(f.observe());job=p.jobs[105]
        f.e(job.target)["path"]=[job.target,100]
        with self.assertRaisesRegex(kp.PlanningError,"incarnation"):p.advance(f.observe())

    def test_duplicate_wip_not_assigned_twice_and_supply_capped(self):
        f=Fixture();f.add(200,"chopped-bread",parent=f.roles["bun-board"],composition=tree(kp.BUN))
        f.add(201,"chopped-bread",parent=90,composition=tree(kp.BUN))
        p=kp.Scheduler(f.observe());b=p.advance(f.observe())
        self.assertEqual(b["allocation"]["1"],[200])
        self.assertNotIn(f.observe().token(201),p.claims)
        self.assertFalse(any(j.kind=="supply" and j.ingredient==kp.BUN for j in p.jobs.values()))

    def test_owned_material_disappearance_is_not_silent_reallocation(self):
        f=Fixture();f.add(200,"chopped-bread",parent=f.roles["bun-board"],composition=tree(kp.BUN))
        p=kp.Scheduler(f.observe());p.advance(f.observe());f.remove(200)
        with self.assertRaises(kp.PlanningError):p.advance(f.observe())

    def test_exact_mixer_kit_required_before_partial_heat_admission(self):
        f=Fixture(228996)
        f.add(200,"flour",parent=f.roles["right-pass"],composition=tree(kp.FLOUR))
        f.add(201,"chopped-chocolate",parent=f.roles["flavor-board"],composition=tree(kp.CHOCOLATE))
        p=kp.Scheduler(f.observe());p.advance(f.observe())
        self.assertFalse(any(j.kind=="load" for j in p.jobs.values()))
        self.assertTrue(any(j.kind=="supply" and j.ingredient==kp.EGG for j in p.jobs.values()))

    def test_complete_staged_mixer_kit_starts_only_one_exact_load(self):
        f=Fixture(228996)
        for eid,kind,ingredient,role in [(200,"flour",kp.FLOUR,"right-pass"),(201,"egg",kp.EGG,"egg-stage"),(202,"chopped-chocolate",kp.CHOCOLATE,"flavor-board")]:
            f.add(eid,kind,parent=f.roles[role],composition=tree(ingredient))
        p=kp.Scheduler(f.observe());p.advance(f.observe());loads=[j for j in p.jobs.values() if j.kind=="load"]
        self.assertEqual(len(loads),1)
        self.assertIn(loads[0].target,p.homes)

    def test_native_cooked_not_flat_or_progress_only(self):
        f=Fixture();f.food[2].update(composition=tree(kp.SAUSAGE,state="Raw"),cookingProgress=12.5)
        self.assertFalse(kp.Scheduler._ready(f.observe(),2,kp.Counter([kp.SAUSAGE])))
        f.food[2]["composition"]=tree(kp.SAUSAGE,state="Cooked")
        self.assertTrue(kp.Scheduler._ready(f.observe(),2,kp.Counter([kp.SAUSAGE])))

    def test_ruined_rejects_but_warning_state_does_not(self):
        f=Fixture();f.food[2].update(composition=tree(kp.SAUSAGE,state="OverDoing"),cookingProgress=16)
        f.observe()
        f.food[2]["composition"]=tree(kp.SAUSAGE,state="Burnt")
        with self.assertRaisesRegex(kp.PlanningError,"ruined"):f.observe()

    def test_urgent_heat_preempts_electives_without_touching_active_lease(self):
        f=Fixture();p=kp.Scheduler(f.observe());p.advance(f.observe());supplier=copy.deepcopy(p.jobs[105])
        f.food[2].update(composition=tree(kp.SAUSAGE,state="Cooked"),cookingProgress=18)
        f.s["frame"]+=1;f.n["elapsed"]+=1/60
        b=p.advance(f.observe());parks=[j for j in p.jobs.values() if j.kind=="park"]
        self.assertEqual(len(parks),1);self.assertEqual(parks[0].source,2)
        self.assertEqual(p.jobs[105],supplier)

    def test_blocked_safety_never_admits_elective(self):
        f=Fixture();f.food[2].update(composition=tree(kp.SAUSAGE,state="Cooked"),cookingProgress=18)
        for i in [90,91,92,93]:f.add(200+i,"bread",parent=i,composition=tree())
        p=kp.Scheduler(f.observe());b=p.advance(f.observe())
        self.assertIsNone(b["request"]);self.assertTrue(any("electives blocked" in reason for reason in b["blocked"]))

    def test_parked_heat_excluded_and_guard_not_extended(self):
        f=Fixture();f.food[2].update(composition=tree(kp.SAUSAGE,state="Cooked"),cookingProgress=18)
        p=kp.Scheduler(f.observe());f.parent(2,90)
        self.assertEqual(p._heat(f.observe()),[])
        f.parent(2,82);f.food[2]["cookingProgress"]=23
        with self.assertRaisesRegex(kp.PlanningError,"guard"):p._heat(f.observe())

    def test_source_consumption_moves_claim_to_vessel_exact_delta(self):
        f=Fixture();f.add(200,"sausage",parent=f.roles["left-pass"],composition=tree(kp.SAUSAGE))
        p=kp.Scheduler(f.observe());p._claim(f.observe(),200,1)
        job=p._new(f.observe(),"load",1,103,200,2,kp.SAUSAGE,(82,))
        a=p._emit(f.observe(),job);f.parent(200,103);f.receipt({"request":{"actions":[a]}})
        p._reconcile(f.observe());a=p._emit(f.observe(),job);f.remove(200);f.food[2]["composition"]=tree(kp.SAUSAGE,state="Raw")
        f.receipt({"request":{"actions":[a]}});p._reconcile(f.observe())
        self.assertEqual(p.claims,{f.observe().token(2):1});self.assertFalse(p.leases)

    def test_wrong_extra_ingredient_does_not_complete_transfer(self):
        f=Fixture();f.add(200,"sausage",parent=f.roles["left-pass"],composition=tree(kp.SAUSAGE))
        p=kp.Scheduler(f.observe());job=p._new(f.observe(),"load",1,103,200,2,kp.SAUSAGE,(82,))
        a=p._emit(f.observe(),job);f.parent(200,103);f.receipt({"request":{"actions":[a]}});p._reconcile(f.observe())
        a=p._emit(f.observe(),job);f.remove(200);f.food[2]["composition"]=tree(kp.SAUSAGE,kp.SAUSAGE,state="Raw")
        f.receipt({"request":{"actions":[a]}});p._reconcile(f.observe())
        self.assertIn(103,p.jobs);self.assertEqual(p.jobs[103].phase,"put")

    def test_recipe_expiration_not_delivery_and_rewind_needs_branch(self):
        f=Fixture();p=kp.Scheduler(f.observe());f.n["orders"]=[]
        with self.assertRaisesRegex(kp.PlanningError,"vanished"):p.advance(f.observe())
        f=Fixture();p=kp.Scheduler(f.observe());f.s["frame"]=0
        with self.assertRaisesRegex(kp.PlanningError,"rewind"):p.advance(f.observe())

    def test_alternatives_do_not_mutate_parent_or_share_leases(self):
        f=Fixture(158500,228996);p=kp.Scheduler(f.observe());alternatives=p.alternatives(f.observe())
        self.assertFalse(p.jobs);self.assertFalse(p.leases)
        self.assertTrue(alternatives)
        alternatives[0][0].leases[999]="branch-only"
        self.assertNotIn(999,p.leases)

    def test_metadata_class_pose_spawn_and_missing_food_fail_closed(self):
        f=Fixture();o=f.observe();self.assertEqual(o.role("left-pass"),f.roles["left-pass"])
        f.e(f.roles["left-pass"])["position"]["x"]+=.05
        with self.assertRaises(kp.PlanningError):f.observe().role("left-pass")
        f=Fixture();eid=f.observe().crate(kp.BUN)
        next(r for r in f.s["registry"] if r["EntityId"]==eid)["SpawnNames"]=["Wrong"]
        with self.assertRaises(kp.PlanningError):f.observe().crate(kp.BUN)

    def test_actual_native_f_initial_schema_is_read_only_compatible(self):
        path=ROOT/"artifacts/framework-migration/native-f/load.json"
        if not path.exists():self.skipTest("Operator native-F artifact absent")
        doc=json.loads(path.read_text(encoding="utf-8-sig"))
        snapshot=doc["steps"][-1]["response"]
        native=next(s["response"]["bridge"]["nativeRound"] for s in reversed(doc["steps"])
                    if s["response"].get("bridge",{}).get("nativeRound",{}).get("available"))
        o=kp.Observation(snapshot,native);p=kp.Scheduler(o);batch=p.advance(o)
        self.assertEqual(batch["request"]["actions"][0]["target"],68)
        self.assertEqual(batch["request"]["actions"][0]["chef"],105)
        self.assertEqual(o.role("left-pass"),45)
        self.assertEqual(p.homes[2][0],17)
        self.assertEqual(batch["nativeLedger"]["total"],0)


if __name__=="__main__":unittest.main()
