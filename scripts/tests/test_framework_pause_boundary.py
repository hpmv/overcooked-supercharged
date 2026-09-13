"""Pure read-only pause observation policy fixtures; no native connection."""
import copy
from pathlib import Path
import sys
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from test_framework_plate_search import setup,physics_receipt
from framework_pause_boundary import observe_settled_pause,PauseBoundaryError


def receipt(fixed,sleeping=False):
    result=physics_receipt(setup());b=result["bridge"]
    result["ok"]=True
    b.update(paused=True,loadComplete=True,fixedTime=fixed,fixedDeltaTime=.02,logicalRealtime=27.95,
             logicalClock={"policy":"native-float-capture-step-with-authoring-pause-suspension",
                           "authoringPausedThisFrame":True,"authoringPauseRequested":True,
                           "value":27.95,"eligibleTicks":1677,"step":.0166666675})
    for body in b["nativePhysics"]["bodies"]:body["sleeping"]=sleeping
    return result


class Source:
    def __init__(self,receipts):self.receipts=receipts;self.index=0;self.wall=0;self.frame=100
    def food(self):
        r=self.receipts[min(self.index,len(self.receipts)-1)];self.index+=1
        return r
    def read_frame(self):return self.frame
    def now(self):return self.wall
    def sleep(self,delay):self.wall+=delay
    def run(self,**kwargs):
        return observe_settled_pause(self.food,self.read_frame,expected_frame=100,chef_ids=[103,104,105,106],
                                    monotonic=self.now,sleep=self.sleep,**kwargs)


class PauseBoundaryTests(unittest.TestCase):
    def test_optional_native_clock_pair_must_be_complete_and_stable(self):
        a=receipt(1)
        a["bridge"]["nativeCheckpoints"]={"nativeServerClock":[30.0,27.95,27.95],
                                          "nativeClientClock":[.016,27.95,27.95,27.0,0.0,0.0]}
        rows=[copy.deepcopy(a) for _ in range(3)]
        for index,row in enumerate(rows):row["bridge"]["fixedTime"]=1+index*.02
        self.assertTrue(Source(rows).run()["proof"]["passed"])
        for mutation in ("server-only","client-only","short","null","changed","newly-missing"):
            bad=copy.deepcopy(rows);cp=bad[1]["bridge"]["nativeCheckpoints"]
            if mutation=="server-only":cp.pop("nativeClientClock")
            elif mutation=="client-only":cp.pop("nativeServerClock")
            elif mutation=="short":cp["nativeClientClock"].pop()
            elif mutation=="null":cp["nativeServerClock"]=None
            elif mutation=="changed":cp["nativeServerClock"][0]+=.0000001
            else:bad[1]["bridge"].pop("nativeCheckpoints")
            with self.subTest(mutation=mutation),self.assertRaises(PauseBoundaryError):Source(bad).run()

    def test_two_distinct_native_ticks_and_all_raw_receipts_required(self):
        s=Source([receipt(1),receipt(1),receipt(1.02),receipt(1.02),receipt(1.04)])
        result=s.run();p=result["proof"]
        self.assertTrue(p["passed"]);self.assertEqual(s.index,5)
        self.assertEqual([r["stableDistinctPhysicsTicks"] for r in p["observations"]],[0,0,1,1,2])
        self.assertEqual(len(p["observations"]),5)
        self.assertTrue(p["rawAcknowledgementPhysicsEqual"])

    def test_initial_sleep_transition_is_preserved_not_declared_exact_ack(self):
        rows=[receipt(1),receipt(1.02,True),receipt(1.04,True),receipt(1.06,True)]
        before=copy.deepcopy(rows);result=Source(rows).run();p=result["proof"]
        self.assertEqual(rows,before)
        self.assertFalse(p["rawAcknowledgementPhysicsEqual"])
        self.assertNotEqual(p["firstNativePhysicsSha256"],p["settledNativePhysicsSha256"])
        self.assertEqual(p["physicsChanges"][0]["firstDifference"]["field"],"$nativePhysics/bodies/0/sleeping")
        self.assertEqual([r["stableDistinctPhysicsTicks"] for r in p["observations"]],[0,0,1,2])
        self.assertFalse(p["observations"][0]["receipt"]["bridge"]["nativePhysics"]["bodies"][0]["sleeping"])

    def test_even_exact_tiny_velocity_and_quaternion_sign_changes_reset_streak(self):
        for field in ("velocity","rotation","signed-zero"):
            a=receipt(1);b=receipt(1.02);body=b["bridge"]["nativePhysics"]["bodies"][0]
            if field=="velocity":body["resumeVelocity"]["x"]=1e-12
            elif field=="rotation":body["rotation"]["w"]=-1
            else:body["rawVelocity"]["x"]=-0.0
            c=copy.deepcopy(b);c["bridge"]["fixedTime"]=1.04
            d=copy.deepcopy(c);d["bridge"]["fixedTime"]=1.06
            p=Source([a,b,c,d]).run()["proof"]
            self.assertFalse(p["rawAcknowledgementPhysicsEqual"])
            self.assertEqual(len(p["physicsChanges"]),1)

    def test_missing_physics_old_clock_and_unpaused_receipts_fail_with_evidence(self):
        for mutation in ("physics","old-clock","ticks","source","pause","missing-chef","nonfinite"):
            r=receipt(1);b=r["bridge"]
            if mutation=="physics":b.pop("nativePhysics")
            elif mutation=="old-clock":b.pop("logicalClock")
            elif mutation=="ticks":b["logicalClock"].pop("eligibleTicks")
            elif mutation=="source":b["logicalRealtime"]+=.01
            elif mutation=="pause":b["paused"]=False
            elif mutation=="missing-chef":b["nativePhysics"]["bodies"].pop()
            else:b["nativePhysics"]["bodies"][0]["rawVelocity"]["x"]=float("inf")
            with self.subTest(mutation=mutation),self.assertRaises(PauseBoundaryError) as caught:Source([r]).run()
            self.assertEqual(len(caught.exception.report["observations"]),1)

    def test_frame_timer_order_and_clock_change_fail_instead_of_resetting(self):
        for mutation in ("elapsed","remaining","clock","ticks","order","fixed-backward"):
            a=receipt(1);b=receipt(1.02);v=b["bridge"]
            if mutation=="elapsed":v["nativeRound"]["elapsed"]+=.01
            elif mutation=="remaining":v["nativeRound"]["remaining"]=123
            elif mutation=="clock":v["logicalRealtime"]+=.01;v["logicalClock"]["value"]+=.01
            elif mutation=="ticks":v["logicalClock"]["eligibleTicks"]+=1
            elif mutation=="order":v["nativeRound"]["orders"]=[{"id":999}]
            else:v["fixedTime"]=.98
            with self.subTest(mutation=mutation),self.assertRaises(PauseBoundaryError) as caught:Source([a,b]).run()
            self.assertFalse(caught.exception.report["passed"])
        s=Source([receipt(1)])
        def food():
            r=s.food();s.frame=101;return r
        with self.assertRaisesRegex(PauseBoundaryError,"Gameplay frame") as caught:
            observe_settled_pause(food,s.read_frame,expected_frame=100,monotonic=s.now,sleep=s.sleep)
        self.assertEqual(caught.exception.report["observations"][0]["frameAfter"],101)

    def test_duplicate_fixed_time_and_changing_physics_cannot_wait_forever(self):
        with self.assertRaises(PauseBoundaryError) as caught:
            Source([receipt(1)]).run(timeout_seconds=.05)
        self.assertFalse(caught.exception.report["passed"])
        self.assertLessEqual(len(caught.exception.report["observations"]),7)
        rows=[receipt(1+i*.02,bool(i%2)) for i in range(12)]
        with self.assertRaises(PauseBoundaryError):Source(rows).run(timeout_seconds=.05)


if __name__=="__main__":unittest.main()
