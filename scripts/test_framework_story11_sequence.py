"""Native event codec and sequential-ledger negative fixtures, no game calls."""
import base64
import copy
import struct
import unittest
from types import SimpleNamespace

from framework_story11_delivery_events import decode_delivery, delivery_events, verify_delivery
from framework_story11_sequence import choose_next, unique_request, SequenceRunner, require_completed_history
from framework_story11_search import Runner
from framework_story11_planner import Blocked
from test_framework_story11 import fixture, observation


def packet(fields):
    value=0;length=0
    for number,width in fields:
        assert 0<=number<2**width
        value=(value<<width)|number;length+=width
    pad=(-length)%8
    return base64.b64encode((value<<pad).to_bytes((length+pad)//8,"big")).decode()


def fixture_event(count=1):
    before={"baseScore":20*(count-1),"tips":sum(8*max(i,1) for i in range(count-1)),
        "multiplier":min(count-1,4),"combo":count-1,"deductions":0,"deliveries":count-1}
    before["total"]=before["baseScore"]+before["tips"]
    tip=8*max(before["multiplier"],1)
    after={**before,"baseScore":before["baseScore"]+20,"tips":before["tips"]+tip,
           "deliveries":count,"combo":count,"multiplier":min(count,4),"total":before["total"]+20+tip}
    fields=[(40,10),(2,4),(0,2),(0,2),(1,1)]
    fields += [(after[k],n) for k,n in (("baseScore",16),("tips",16),("multiplier",3),("combo",8),("deductions",16))]
    fields += [(1,1),(count,8),(33,10),(count,8),(1,1),(int.from_bytes(struct.pack(">f",.9),"big"),32),(tip,6)]
    wire=packet(fields)
    flow=decode_delivery({"type":4,"nativeComponentType":31,"entityId":40,"bytes":wire})
    station=decode_delivery({"type":4,"nativeComponentType":8,"entityId":33,"bytes":packet([(33,10),(1,4),(1,10),(1,1)])})
    flow.update(frame=200,rawEventOrdinal=2);station.update(frame=200,rawEventOrdinal=3)
    case={"plate":1,"serve":33,"head":{"id":count,"baseValue":20}}
    return case,before,after,[flow,station]


class SequenceTests(unittest.TestCase):
    def test_unfinished_history_cannot_run_during_warmup(self):
        s={"frame":180,"actionGraph":{"chefs":[{"actions":[{"predictions":{"endFrame":178}}]}]}}
        require_completed_history(s)
        for end in (None,180,181):
            s["actionGraph"]["chefs"][0]["actions"][0]["predictions"]["endFrame"]=end
            with self.assertRaises(Blocked):require_completed_history(s)

    def test_both_runners_inspect_timer_while_fenced_before_arm(self):
        for base in (Runner,SequenceRunner):
            commands=[]
            class Stub(base):
                def settled(self,label):return {"preventInvalidState":False}
                def call(self,target,request,label):commands.append(request["command"]);return {}
                def observe(self,label):raise RuntimeError("stop after warmup")
            args=SimpleNamespace(search=False,warmup=30)
            runner=Stub(args,None,None,None)
            with self.assertRaisesRegex(RuntimeError,"stop after warmup"):runner.run()
            self.assertEqual(commands,["pause","hot-call","arm","step"])

    def test_first_through_capped_combo_tip_uses_previous_multiplier(self):
        for count in range(1,7):
            c,b,a,events=fixture_event(count)
            with self.subTest(count=count):
                report=verify_delivery(c,b,a,events,40)
                self.assertTrue(report["passed"]);self.assertEqual(report["tipBoundaryValue"],8)
                self.assertEqual(a["baseScore"]-b["baseScore"],20)
                self.assertEqual(a["multiplier"],min(count,4))

    def test_plate_station_success_alone_does_not_prove_kitchen_acceptance(self):
        c,b,a,events=fixture_event()
        with self.assertRaisesRegex(Blocked,"kitchen"):verify_delivery(c,b,a,[events[1]],40)
        events[0]["success"]=False
        with self.assertRaisesRegex(Blocked,"accept"):verify_delivery(c,b,a,events,40)

    def test_wrong_order_plate_flow_score_combo_and_duplicate_reject(self):
        c,b,a,events=fixture_event(3)
        for kind in ("order","plate","station","flow","base","tip","combo","multiplier","duplicate","delay"):
            z=copy.deepcopy(events);expected=copy.deepcopy(a)
            if kind=="order":z[0]["order"]=2
            if kind=="plate":z[1]["plate"]=4
            if kind=="station":z[0]["station"]=34
            if kind=="flow":z[0]["entityId"]=39
            if kind=="base":expected["baseScore"]+=20;z[0]["ledger"]=expected
            if kind=="tip":z[0]["tip"]+=1
            if kind=="combo":z[0]["wasCombo"]=False
            if kind=="multiplier":expected["multiplier"]-=1;z[0]["ledger"]=expected
            if kind=="duplicate":z.append(copy.deepcopy(z[0]))
            if kind=="delay":z[1]["frame"]+=1
            with self.subTest(kind=kind),self.assertRaises(Blocked):verify_delivery(c,b,expected,z,40)

    def test_registry_codec_binding_and_frame_coverage(self):
        c,b,a,events=fixture_event()
        registry=[{"EntityId":40,"SyncEntityTypes":[0,0,31]},{"EntityId":33,"SyncEntityTypes":[0,8]}]
        capture={"validation":"exact-four-pad-frame-coverage","startExclusive":199,"endInclusive":200,"rawExchanges":[
            {"input":{"NextFrame":200},"output":{"FrameNumber":199,"LastFramePaused":False,"ServerMessages":[{"Type":4,"Message":e["bytes"]} for e in events]}}]}
        decoded=delivery_events([capture],registry)
        self.assertTrue(verify_delivery(c,b,a,decoded,40)["passed"])
        terminal=copy.deepcopy(capture)
        terminal["endInclusive"]=201
        terminal["rawExchanges"].append({"input":{"NextFrame":201},"output":{"FrameNumber":200,"LastFramePaused":True,"ServerMessages":[]}})
        self.assertEqual(delivery_events([terminal],registry),decoded)
        capture["endInclusive"]=201
        with self.assertRaisesRegex(Blocked,"Missing"):delivery_events([capture],registry)

    def test_initial_plate_admission_and_graph_names(self):
        s,r=fixture(); c=choose_next(observation(s,r),True)
        self.assertTrue(c["dash"]);self.assertIn(c["plate"],(1,2))
        request=unique_request(c["request"],"meal-2-")
        self.assertEqual(request["actions"][1]["after"],["meal-2-fetch"])
        self.assertEqual(c["request"]["actions"][1]["after"],["fetch"])
        for plate in (1,2):
            e=next(x for x in s["entities"] if x["id"]==plate)
            parent=e["data"]["attachmentParent"]["path"][0]
            e["path"]=[34,0,plate]
            next(x for x in s["entities"] if x["id"]==parent)["data"]["attachment"]={"path":e["path"]}
        with self.assertRaises(Blocked):choose_next(observation(s,r))


if __name__=="__main__":unittest.main()
