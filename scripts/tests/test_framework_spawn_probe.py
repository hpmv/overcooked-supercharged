"""Offline recorded-layout and adversarial dynamic identity fixtures; no sockets."""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
import framework_spawn_probe as sp
from framework_kitchen_planner import PlanningError


def baseline():
    # Q is a real fixed native layout. Clock observations added below are an
    # explicit synthetic current-schema fixture, not retrospective Q evidence.
    path = ROOT / "artifacts/framework-migration/native-q/pickup-probe/observations.json"
    rows = json.loads(path.read_text(encoding="utf-8-sig"))
    state = next(r["response"] for r in rows if r.get("label") == "base")
    receipt = next(r["response"] for r in rows if r.get("label") == "base-native")
    receipt["bridge"]["nativeCheckpoints"].update(nativeServerClock=[1., 2., 3.], nativeClientClock=[1., 2., 3., 4., 5., 6.])
    receipt["bridge"]["logicalClock"] = {"eligibleTicks": state["frame"], "step": 1. / 60}
    receipt["bridge"]["logicalRealtime"] = 1.
    return state, receipt


def egg_fixture(base, native, root=200, container=201, body_instance=999):
    s, r = copy.deepcopy(base), copy.deepcopy(native)
    s["frame"] += 20
    r["bridge"]["nativeRound"]["elapsed"] += 20 / 60
    r["bridge"]["logicalRealtime"] += 20 / 60
    r["bridge"]["logicalClock"]["eligibleTicks"] += 20
    chef = next(e for e in s["entities"] if e["id"] == 104)
    chef["data"]["attachment"] = {"path": [65, 0]}
    e = {"id": root, "path": [65, 0], "name": "Egg", "className": "egg", "prefab": "Egg", "exists": True,
         "position": {"x": 30., "y": .6, "z": -12.}, "rotation": {"x": 0., "y": 0., "z": 0., "w": 1.},
         "velocity": {"x": 0., "y": 0., "z": 0.}, "data": {"attachmentParent": {"path": [104]}}, "chef": None}
    s["entities"].append(e)
    s["registry"] += [
        {"EntityId": root, "Name": "Egg", "Pos": {"X": 30., "Y": .6, "Z": -12.},
         "Components": ["IngredientProperties", "ServerIngredientContainer", "PhysicalAttachment"], "SyncEntityTypes": [12], "SpawnNames": []},
        {"EntityId": container, "Name": "Physics Container", "Pos": {"X": 30., "Y": .6, "Z": -12.},
         "Components": ["Rigidbody", "ObjectContainer"], "SyncEntityTypes": [47], "SpawnNames": []}]
    physics = r["bridge"]["nativePhysics"]["bodies"]
    template = copy.deepcopy(next(b for b in physics if b["entityId"] == 104))
    for i in (root, container):
        row = copy.deepcopy(template); row.update(entityId=i, bodyInstanceId=body_instance)
        physics.append(row)
    r["detail"]["entities"].append({"id": root, "name": "Egg", "composition":
        {"type": "IngredientAssembledNode", "id": sp.EGG, "name": "Egg", "children": [], "optional": []}})
    s["typedActions"] = {"outcome": "complete"}
    return s, r


def actual_x_held_fixture():
    # The state, food/physics and field read are unchanged native receipts.
    # Host bracketing below is a synthetic protocol fixture, not a retrospective
    # claim that the original failed runner performed these new checks.
    directory = ROOT / "artifacts/framework-migration/native-x-v11"
    rows = json.loads((directory / "spawn-body-r3/observations.json").read_text())
    get = lambda label: next(r["response"] for r in rows if r["label"] == label)
    raw = json.loads((directory / "spawn-egg-native-container.json").read_text())
    return get("base"), get("base-native"), get("original"), get("original-native"), raw


class ActualHeldOwnershipTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.fixture = actual_x_held_fixture()

    def setUp(self):
        self.base, self.base_native, self.state, self.native, self.read = copy.deepcopy(self.fixture)
        self.calls = []

    def capture(self):
        def call(target, request, label):
            self.calls.append((target, request))
            if target == "controller": return copy.deepcopy(self.state)
            if request["command"] == "hot-call": return copy.deepcopy(self.read["records"][1]["response"])
            if request["command"] == "pause": return copy.deepcopy(self.read["records"][0]["response"])
            if request["command"] == "arm": return copy.deepcopy(self.native)
            self.fail("Unexpected protocol operation")
        return sp.collect_ownership(self.state, self.native, call, "fixture")

    def test_real_held_egg_missing_root_physics_requires_exact_native_field_receipt(self):
        bodies = self.native["bridge"]["nativePhysics"]["bodies"]
        self.assertNotIn(123, [b["entityId"] for b in bodies])
        self.assertEqual(next(b["bodyInstanceId"] for b in bodies if b["entityId"] == 124), -175550)
        with self.assertRaisesRegex(PlanningError, "unique actual"): sp.logical_projection(self.state, self.native)
        before = json.dumps(self.native, sort_keys=True)
        receipt = self.capture()
        case = sp.prepare_case(self.base, self.base_native)
        goal = sp.held_egg(case, self.state, receipt)
        self.assertEqual((goal["egg"], goal["container"], goal["path"]), (123, 124, [65, 0]))
        self.assertEqual(sp.logical_projection(self.state, receipt)[1]["physicalContainers"], {"124": 123})
        self.assertEqual(before, json.dumps(self.native, sort_keys=True))
        self.assertEqual([r[1]["command"] for r in self.calls], ["status", "pause", "hot-call", "status", "arm"])

    def test_epoch_frame_level_and_clock_replacement_rejected_before_rearming(self):
        for mutation in ("epoch", "frame", "level", "clock", "fence", "host-frame"):
            with self.subTest(mutation=mutation):
                self.setUp()
                b = self.read["records"][1]["response"]["bridge"]
                if mutation == "epoch": b["inputExchange"]["connectionEpoch"] += 1
                elif mutation == "frame": b["nativeCheckpoints"]["lastFrame"] += 1
                elif mutation == "level": b["readyUnityFrame"] += 1
                elif mutation == "clock": b["logicalClock"]["eligibleTicks"] += 1
                elif mutation == "fence": b["inputBlocked"] = False
                else: self.state["frame"] += 1
                with self.assertRaises(PlanningError): self.capture()
                self.assertNotIn("arm", [r[1]["command"] for r in self.calls])

    def test_exact_root_component_field_and_readonly_identity_required(self):
        for mutation in ("entity", "component", "object", "body", "field", "type", "mutation", "before-after"):
            with self.subTest(mutation=mutation):
                self.setUp()
                r = self.read["records"][1]["response"]["detail"]["result"]
                if mutation == "entity": r["entityId"] += 1
                elif mutation == "component": r["componentInstanceId"] += 1
                elif mutation == "object": r["objectInstanceId"] += 1
                elif mutation == "mutation": r["mutationAttempted"] = True
                elif mutation == "before-after": r["before"]["values"][0]["value"]["instanceId"] += 1
                else:
                    for phase in ("before", "after"):
                        v = r[phase]["values"][0]
                        if mutation == "body": v["value"]["instanceId"] += 1
                        elif mutation == "field": v["name"] = "m_unrelated"
                        else: v["value"]["type"] = "UnityEngine.GameObject"
                with self.assertRaises(PlanningError): self.capture()

    def test_uniquely_registered_proxy_not_numeric_adjacency_or_name(self):
        for mutation in ("unknown", "type", "component", "duplicate-proxy", "conflicting-root"):
            with self.subTest(mutation=mutation):
                self.setUp()
                bodies = self.native["bridge"]["nativePhysics"]["bodies"]
                proxy = next(b for b in bodies if b["entityId"] == 124)
                meta = next(m for m in self.state["registry"] if m["EntityId"] == 124)
                if mutation == "unknown": meta["EntityId"] = 999
                elif mutation == "type": meta["SyncEntityTypes"] = []
                elif mutation == "component": meta["Components"].remove("ObjectContainer")
                elif mutation == "duplicate-proxy":
                    other = copy.deepcopy(proxy); other["entityId"] = 999; bodies.append(other)
                    other = copy.deepcopy(meta); other["EntityId"] = 999; self.state["registry"].append(other)
                else:
                    other = copy.deepcopy(proxy); other.update(entityId=123, bodyInstanceId=-99); bodies.append(other)
                with self.assertRaises(PlanningError):
                    result = self.capture(); sp.logical_projection(self.state, result)

    def test_stored_receipt_cannot_be_reused_after_state_or_epoch_change(self):
        receipt = self.capture()
        for mutation in ("state", "epoch", "host", "request", "duplicate-read"):
            state, r = copy.deepcopy(self.state), copy.deepcopy(receipt)
            if mutation == "state": state["entities"][-1]["position"]["x"] += .000001
            elif mutation == "epoch": r["bridge"]["inputExchange"]["connectionEpoch"] += 1
            elif mutation == "host": r["physicalAttachmentOwnership"]["hostAfter"]["frame"] += 1
            elif mutation == "request": r["physicalAttachmentOwnership"]["reads"][0]["request"]["operation"] = "invoke"
            else: r["physicalAttachmentOwnership"]["reads"].append(copy.deepcopy(r["physicalAttachmentOwnership"]["reads"][0]))
            with self.subTest(mutation=mutation), self.assertRaises(PlanningError): sp.logical_projection(state, r)

    def test_same_frame_restart_output_directory_gets_distinct_create_new_paths(self):
        a, b = ROOT / "artifacts/probe-a", ROOT / "artifacts/probe-b"
        self.assertEqual(sp.artifact_name(a, "base", 31, "pb"), sp.artifact_name(a, "base", 31, "pb"))
        self.assertNotEqual(sp.artifact_name(a, "base", 31, "pb"), sp.artifact_name(b, "base", 31, "pb"))
        self.assertNotEqual(sp.artifact_name(a, "base", 31, "pb"), sp.artifact_name(a, "input", 31, "json"))


class SpawnTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.base, cls.native = baseline()

    def setUp(self):
        self.s, self.r = copy.deepcopy(self.base), copy.deepcopy(self.native)
        self.case = sp.prepare_case(self.s, self.r)

    def test_recorded_layout_resolves_actual_egg_spawn_profile_and_four_chefs(self):
        c = self.case
        self.assertEqual((c["chef"], c["crate"], c["ingredient"]), (104, 65, 16620))
        self.assertEqual(c["request"]["maximumFrames"] + 2, 300)
        self.assertEqual([a["id"] for a in c["request"]["actions"]], ["egg-approach", "egg-settle", "egg-pickup"])
        self.assertTrue(c["request"]["actions"][-1]["expectSpawn"])
        self.assertTrue(all(a["resources"] == [65] for a in c["request"]["actions"]))
        self.assertTrue(sp.compare_logical(self.s, self.r, self.s, self.r)["passed"])

    def test_session_capability_and_metadata_are_required_not_ids_alone(self):
        for field in ["scene", "dlc", "variantPlayers", "serverUsers", "clientStates", "virtualPads"]:
            s, r = copy.deepcopy(self.s), copy.deepcopy(self.r)
            r["bridge"]["session"][field] = None
            with self.subTest(field=field), self.assertRaises(PlanningError): sp.prepare_case(s, r)
        for mutation in ("capability", "spawn", "component", "local", "pose", "chef", "clock"):
            s, r = copy.deepcopy(self.s), copy.deepcopy(self.r)
            meta = next(e for e in s["registry"] if e["EntityId"] == 65)
            if mutation == "capability": s["nativeWarpCapabilities"]["Features"] = 0
            elif mutation == "spawn": meta["SpawnNames"] = ["Flour", "Egg"]
            elif mutation == "component": meta["Components"].remove("ServerPickupItemSpawner")
            elif mutation == "local": r["bridge"]["chefs"][0]["local"] = False
            elif mutation == "pose": next(e for e in s["entities"] if e["id"] == 65)["position"]["x"] += 1
            elif mutation == "chef": next(e for e in s["entities"] if e["id"] == 104)["className"] = "chef-blue"
            else: r["bridge"]["nativeCheckpoints"].pop("nativeServerClock")
            with self.subTest(mutation=mutation), self.assertRaises((PlanningError, ValueError)): sp.prepare_case(s, r)

    def test_total_frame_bound_is_explicit_and_includes_release(self):
        for bound in (59, 301, True, 300.0):
            with self.subTest(bound=bound), self.assertRaises(PlanningError): sp.prepare_case(self.s, self.r, bound)
        s, r = egg_fixture(self.s, self.r)
        s["frame"] = self.s["frame"] + 301
        with self.assertRaisesRegex(PlanningError, "frame bound"): sp.held_egg(self.case, s, r)

    def test_native_raw_egg_and_actual_physical_proxy_goal(self):
        s, r = egg_fixture(self.s, self.r)
        result = sp.held_egg(self.case, s, r)
        self.assertEqual((result["egg"], result["container"], result["path"]), (200, 201, [65, 0]))
        self.assertEqual(result["framesIncludingRelease"], 20)

    def test_goal_rejects_unrelated_food_paths_catches_and_registry(self):
        for mutation in ("flour", "path", "reverse", "chef", "fixed-parent", "extra", "ledger", "other-food"):
            s, r = egg_fixture(self.s, self.r)
            if mutation == "flour": r["detail"]["entities"][-1]["composition"]["id"] = 1
            elif mutation == "path": s["entities"][-1]["path"] = [64, 0]
            elif mutation == "reverse": next(e for e in s["entities"] if e["id"] == 104)["data"].pop("attachment")
            elif mutation == "chef": s["entities"][-1]["data"]["attachmentParent"]["path"] = [106]
            elif mutation == "fixed-parent": next(e for e in s["entities"] if e["id"] == 6)["data"]["attachmentParent"]["path"] = [17]
            elif mutation == "extra": s["registry"].append({"EntityId": 203, "Name": "unknown"})
            elif mutation == "ledger": r["bridge"]["nativeRound"]["ledger"]["unexpected"] = 1
            else: r["detail"]["entities"][0]["cookingProgress"] += 1
            with self.subTest(mutation=mutation), self.assertRaises(PlanningError): sp.held_egg(self.case, s, r)

    def test_projection_remaps_only_proven_dynamic_ids_and_keeps_raw_differences(self):
        a, ar = egg_fixture(self.s, self.r)
        b, br = egg_fixture(self.s, self.r, 208, 305, -999)
        result = sp.compare_logical(a, ar, b, br)
        self.assertTrue(result["passed"]); self.assertFalse(result["rawEqual"])
        self.assertEqual(result["actualIdentities"]["physicalContainers"], {"305": 208})
        self.assertEqual(result["expectedProjectionSha256"], result["actualProjectionSha256"])

    def test_proxy_mapping_rejects_numeric_adjacency_without_shared_native_body(self):
        s, r = egg_fixture(self.s, self.r)
        r["bridge"]["nativePhysics"]["bodies"][-1]["bodyInstanceId"] += 1
        with self.assertRaisesRegex(PlanningError, "unique actual"): sp.logical_projection(s, r)

    def test_incomplete_or_ambiguous_native_proxy_metadata_rejected(self):
        for mutation in ("component", "type", "duplicate-registry", "duplicate-path", "duplicate-food"):
            s, r = egg_fixture(self.s, self.r)
            if mutation == "component": s["registry"][-1]["Components"].remove("ObjectContainer")
            elif mutation == "type": s["registry"][-1]["SyncEntityTypes"] = []
            elif mutation == "duplicate-registry": s["registry"].append(copy.deepcopy(s["registry"][-1]))
            elif mutation == "duplicate-path": s["entities"][-1]["path"] = [65]
            else: r["detail"]["entities"].append(copy.deepcopy(r["detail"]["entities"][-1]))
            with self.subTest(mutation=mutation), self.assertRaises(PlanningError): sp.logical_projection(s, r)

    def test_physics_sleep_flags_quaternion_signed_zero_and_clock_differences_remain_exact(self):
        a, ar = egg_fixture(self.s, self.r)
        for field in ("rotation", "sleep", "velocity", "fixed-instance", "clock", "food", "fixed-meta"):
            b, br = copy.deepcopy(a), copy.deepcopy(ar)
            body = br["bridge"]["nativePhysics"]["bodies"][-1]
            if field == "rotation": body["rotation"]["w"] *= -1
            elif field == "sleep": body["sleeping"] = not body["sleeping"]
            elif field == "velocity": body["rawVelocity"]["x"] = -0.0
            elif field == "fixed-instance": br["bridge"]["nativePhysics"]["bodies"][0]["bodyInstanceId"] += 1
            elif field == "clock": br["bridge"]["nativeCheckpoints"]["nativeClientClock"][0] += .000000001
            elif field == "food": br["detail"]["entities"][-1]["composition"]["id"] += 1
            else: b["registry"][0]["Pos"]["X"] += .000000001
            with self.subTest(field=field): self.assertFalse(sp.compare_logical(a, ar, b, br)["passed"])

    def test_retired_history_remains_raw_and_live_inventory_still_checked(self):
        s, r = copy.deepcopy(self.s), copy.deepcopy(self.r)
        retired = {"id": 200, "path": [65, 0], "exists": False, "name": "Egg"}
        s["entities"].append(retired)
        result = sp.compare_logical(self.s, self.r, s, r)
        self.assertTrue(result["passed"]); self.assertFalse(result["rawEqual"])
        s["entities"][0]["exists"] = False
        with self.assertRaises(PlanningError): sp.compare_logical(self.s, self.r, s, r)

    def test_exact_both_object_deletion_receipt_and_absence_required(self):
        s, r = egg_fixture(self.s, self.r); goal = sp.held_egg(self.case, s, r)
        self.r["bridge"]["nativeCheckpoints"]["nativeDynamicWarp"] = {"verified": True, "deleted":
            [{"id": 200, "containerId": 201, "containerRemoved": True}]}
        self.assertTrue(sp.require_deletion(goal, self.s, self.r)["passed"])
        for field in ("verified", "containerId", "containerRemoved"):
            r = copy.deepcopy(self.r)
            audit = r["bridge"]["nativeCheckpoints"]["nativeDynamicWarp"]
            if field == "verified": audit[field] = False
            else: audit["deleted"][0][field] = None
            with self.subTest(field=field), self.assertRaises(PlanningError): sp.require_deletion(goal, self.s, r)
        r = copy.deepcopy(self.r)
        r["bridge"]["nativePhysics"]["bodies"].append(copy.deepcopy(egg_fixture(self.s, self.r)[1]["bridge"]["nativePhysics"]["bodies"][-1]))
        with self.assertRaisesRegex(PlanningError, "remains live"): sp.require_deletion(goal, self.s, r)

    def test_historical_registry_requires_explicit_prior_native_deletion_whitelist(self):
        spawned, receipt = egg_fixture(self.s, self.r)
        restored = copy.deepcopy(self.s)
        restored["registry"] = copy.deepcopy(spawned["registry"])
        with self.assertRaisesRegex(PlanningError, "Unexpected unmapped"): sp.logical_projection(restored, self.r)
        self.assertTrue(sp.compare_logical(self.s, self.r, restored, self.r, actual_retired={200, 201})["passed"])
        current, native = egg_fixture(restored, self.r, 208, 305, -999)
        self.assertTrue(sp.held_egg(self.case, current, native, {200, 201})["achieved"])
        with self.assertRaisesRegex(PlanningError, "unexpectedly remains live"):
            sp.logical_projection(current, native, {200, 201, 208})
        with self.assertRaisesRegex(PlanningError, "unexpectedly remains live"):
            sp.logical_projection(restored, self.r, {103})

    def test_actual_first_registration_history_and_dynamic_collider_warning_do_not_fake_layout_validity(self):
        s, r = egg_fixture(self.s, self.r)
        s["initialRegistry"] += copy.deepcopy(s["registry"][-2:])
        s["registryValidation"]["layoutValid"] = False
        s["registryValidation"]["errors"] = [{"id":200,"reason":"Unmodeled registered object has a physical collider; layout needs review."}]
        raw = json.dumps(s, sort_keys=True)
        self.assertTrue(sp.held_egg(self.case, s, r)["achieved"])
        self.assertEqual(raw, json.dumps(s, sort_keys=True))
        a, identities = sp.logical_projection(s, r)
        self.assertIn('["physical-container",65,0]', identities["nativeIdsByPath"])
        self.assertNotIn('[201]', a["registry"])
        for field, value in (("id",103),("reason","Missing required observed component: Rigidbody")):
            changed=copy.deepcopy(s);changed["registryValidation"]["errors"][0][field]=value
            with self.subTest(field=field),self.assertRaises(PlanningError): sp.logical_projection(changed,r)

    def test_transport_failure_still_requests_neutral_pause_and_closes_both_clients(self):
        commands = []; closed = []
        class FakeBridge:
            def __init__(self, port): pass
            def call(self, request):
                commands.append(request["command"])
                if request["command"] == "arm": raise RuntimeError("injected bridge failure")
                return {"ok": True}
            def close(self): closed.append("bridge")
        class FakeHost:
            def __init__(self, port): pass
            def status(self): return {"state": "Paused", "requestPending": False}
            def call(self, request): return copy.deepcopy(self_outer.s)
            def close(self): closed.append("host")
        self_outer = self
        with tempfile.TemporaryDirectory() as td:
            out = Path(td) / "probe"
            with patch.object(sp, "Client", FakeBridge), patch.object(sp, "ControllerClient", FakeHost), \
                 patch.object(sys, "argv", ["probe", "--out", str(out), "--trace", str(Path(td) / "unused")]), \
                 patch("builtins.print"):
                self.assertEqual(sp.main(), 1)
            summary = json.loads((out / "summary.json").read_text())
            self.assertFalse(summary["passed"])
            self.assertEqual(commands, ["arm", "pause"])
            self.assertEqual(closed, ["bridge", "host"])
            self.assertTrue((out / "receipt-hashes.json").exists())
            self.assertIn("injected bridge failure", summary["error"])

    def test_corrupt_restored_native_baseline_prevents_any_input_repetition(self):
        commands = []; closed = []; outer = self
        current = {"state": copy.deepcopy(self.s), "native": copy.deepcopy(self.r)}
        current["state"]["frame"] -= 30
        class FakeBridge:
            def __init__(self, port): pass
            def call(self, request):
                commands.append(("bridge", request["command"]))
                return copy.deepcopy(current["native"])
            def close(self): closed.append("bridge")
        class FakeHost:
            def __init__(self, port): pass
            def status(self): return {"state": "Paused", "requestPending": False, "frame": current["state"]["frame"]}
            def call(self, request):
                command = request["command"]; commands.append(("host", command))
                if command == "step": current.update(state=copy.deepcopy(outer.s), native=copy.deepcopy(outer.r))
                elif command == "actions":
                    s, r = egg_fixture(outer.s, outer.r); current.update(state=s, native=r)
                elif command == "warp":
                    old = current["native"]["bridge"]["nativeCheckpoints"]["restoreAttempts"]
                    current.update(state=copy.deepcopy(outer.s), native=copy.deepcopy(outer.r))
                    check = current["native"]["bridge"]["nativeCheckpoints"]
                    check.update(restoreAttempts=old+1, lastRestore={"verified": True, "frame": outer.s["frame"], "attempt": old+1},
                        nativeDynamicWarp={"verified": True, "deleted": [{"id": 200, "containerId": 201, "containerRemoved": True}]})
                    current["native"]["bridge"]["nativePhysics"]["bodies"][0]["position"]["x"] += .000001
                return copy.deepcopy(current["state"])
            def close(self): closed.append("host")
        class FakeTrace:
            def __init__(self, path): self.offset=0; self.rows=[]
            def read(self): pass
            def frames(self, start, end, chefs):
                rows=[]
                for n in range(end-start):
                    pads={}
                    for chef in chefs:
                        pads[str(chef)]={"Pad":{"X":0.,"Y":0.}, **{b:{"Down":False,"JustPressed":False,"JustReleased":False} for b in ("Pickup","Interact","Dash")}}
                    rows.append({"ordinal":n,"nextFrame":start+n+1,"inputs":pads})
                return rows
        def fake_pause(read_receipt, read_frame, **kwargs):
            assert read_frame() == kwargs["expected_frame"]
            return {"receipt":read_receipt(), "proof":{"passed":True,"classification":"synthetic protocol fixture"}}
        with tempfile.TemporaryDirectory() as td:
            out=Path(td)/"probe";trace=Path(td)/"exchange.jsonl";trace.write_bytes(b"")
            with patch.object(sp,"Client",FakeBridge), patch.object(sp,"ControllerClient",FakeHost), \
                 patch.object(sp,"AdvancingTrace",FakeTrace), patch.object(sp,"observe_settled_pause",fake_pause), \
                 patch.object(sys,"argv",["probe","--out",str(out),"--trace",str(trace)]), patch("builtins.print"):
                self.assertEqual(sp.main(),1)
            result=json.loads((out/"summary.json").read_text())
            self.assertTrue(result["first-deletion"]["deletion"]["passed"])
            self.assertFalse(result["first-deletion"]["comparison"]["passed"])
            self.assertIn("Fixed baseline differs",result["error"])
            self.assertNotIn(("host","raw-input"),commands)
            self.assertNotIn(("host","raw-replay"),commands)
            self.assertEqual(commands[-1],("bridge","pause"))
            self.assertEqual(closed,["bridge","host"])
            self.assertTrue(result["traceWindows"])


if __name__ == "__main__": unittest.main()
