"""Synthetic search fixtures plus read-only native-d schema regression.

Synthetic evaluator scores below test ranking only; they are not native evidence.
"""
import copy
import importlib.util
import json
import sys
import unittest
from dataclasses import replace
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("framework_search", ROOT / "scripts/framework_search.py")
fs = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = fs
SPEC.loader.exec_module(fs)


def native(*recipes, score=0, elapsed=0):
    return {"available": True, "elapsed": elapsed, "ledger": {"total": score, "deliveries": 0, "deductions": 0},
            "orders": [{"id": i + 1, "recipeId": recipe, "remaining": 100} for i, recipe in enumerate(recipes)]}


def entity(eid, kind, contents=(), cook=0, mix=0, chop=0, **extra):
    result = {"id": eid, "exists": True, "className": kind, "position": {"x": 20, "y": 0, "z": -15},
              "data": {"contents": list(contents)}, "cookingProgress": cook, "mixingProgress": mix, "choppingProgress": chop}
    result.update(extra)
    return result


def snapshot(*entities, frame=0):
    return {"frame": frame, "entities": list(entities)}


def tree(state, *ingredients):
    return {"type": "CookedCompositeAssembledNode", "state": state,
            "children": [{"type": "IngredientAssembledNode", "id": i} for i in ingredients]}


def segment(frames=6, x=0, use=False, label="synthetic", claims=()):
    return fs.Segment(frames, (fs.Pad(103, x, 0, use=use), fs.Pad(104), fs.Pad(105), fs.Pad(106)), claims, label)


class ProgressTests(unittest.TestCase):
    def test_dynamic_attachment_path_occupies_counter(self):
        counter = entity(32, 'counter', data={'attachment': {'path': [65, 0]}})
        raw = entity(130, 'egg', [fs.EGG], path=[65, 0])
        assessed = fs.assess(snapshot(counter, raw), native(228996))
        self.assertEqual(assessed.free_counters, 0)
        self.assertEqual(assessed.resource_penalty, .01)
        # Even an unmapped attachment is occupied, never silently free.
        self.assertEqual(fs.assess(snapshot(counter), native(228996)).free_counters, 0)

    def test_dynamic_parent_path_and_duplicate_mapping(self):
        pot = entity(2, 'pot', [fs.SAUSAGE], cook=12, data={'contents': [fs.SAUSAGE], 'attachmentParent': {'path': [65, 0]}})
        parent = entity(131, 'heat-station', path=[65, 0])
        self.assertEqual(fs.assess(snapshot(pot, parent), native(296560)).deadlines[0]['parent'], 131)
        with self.assertRaises(ValueError):
            fs.assess(snapshot(parent, entity(132, 'counter', path=[65, 0])), native(296560))

    def test_served_native_plate_remains_observable_without_reusable_wip(self):
        plate = entity(10, 'plate', [fs.BUN, fs.SAUSAGE], plateLifecycle={'phase': 1})
        n = native(296560, score=68)
        assessed = fs.assess(snapshot(plate), n, food_evidence={10: tree('Cooked', fs.SAUSAGE, fs.BUN)})
        self.assertEqual(assessed.native_score, 68)
        self.assertEqual(assessed.useful_wip, 0)
        self.assertEqual(assessed.credits, ())
        self.assertTrue(plate['exists'])
    def test_actual_native_d_schema_baseline(self):
        path = ROOT / "artifacts/framework-migration/native-d/load.json"
        if not path.exists():
            self.skipTest("native-d operator evidence not present in checkout")
        snap, round_state = fs.extract_samples(json.loads(path.read_bytes()))
        assessed = fs.assess(snap, round_state)
        self.assertEqual(assessed.native_score, 0)
        self.assertEqual(assessed.native_deliveries, 0)
        self.assertEqual(assessed.demand_caps["sausage"], 5)
        self.assertEqual([c.entity for c in assessed.credits], [10, 11, 12, 13])
        self.assertAlmostEqual(assessed.stages["plates"], .8)
        self.assertEqual(assessed.free_counters, 4)
        self.assertEqual(assessed.demand[0].source, "native-active-order")
        self.assertEqual(assessed.demand[2].source, "framework-forecast-unvalidated")

    def test_useless_saturated_sausages_cannot_increase_heuristic(self):
        n = native(296560)
        base = snapshot(entity(2, "pot", [fs.SAUSAGE], cook=12))
        crowded = copy.deepcopy(base)
        crowded["entities"] += [entity(i, "sausage") for i in range(130, 150)]
        a, b = fs.assess(base, n), fs.assess(crowded, n)
        self.assertEqual(a.stages["sausage"], b.stages["sausage"])
        self.assertLess(b.heuristic, a.heuristic)

    def test_unused_flavor_not_preferred(self):
        n = native(296560)
        a = fs.assess(snapshot(), n)
        b = fs.assess(snapshot(entity(120, "chopped-berry")), n)
        self.assertEqual(b.useful_wip, 0)
        self.assertLess(b.heuristic, a.heuristic)

    def test_sausage_bottleneck_progress_monotonic(self):
        values = [fs.assess(snapshot(entity(2, "pot", [fs.SAUSAGE], cook=p)), native(296560)).heuristic for p in (0, 3, 6, 9, 12)]
        self.assertTrue(all(b > a for a, b in zip(values, values[1:])))

    def test_onion_chop_then_pan_does_not_double_count(self):
        n = native(472326)
        a = fs.assess(snapshot(entity(120, "onion", chop=1.4)), n)
        b = fs.assess(snapshot(entity(4, "pan", [fs.ONION], cook=0)), n)
        c = fs.assess(snapshot(entity(4, "pan", [fs.ONION], cook=6)), n)
        self.assertAlmostEqual(a.pipeline["onion"], b.pipeline["onion"])
        self.assertGreater(c.pipeline["onion"], b.pipeline["onion"])
        self.assertEqual(c.stages["chop"], 0)
        self.assertEqual(len(c.credits), 1)

    def test_full_kit_consumes_loose_demand_once(self):
        entities = [entity(6, "mixer", [fs.FLOUR, fs.EGG, fs.CHOCOLATE], mix=6),
                    entity(121, "flour"), entity(122, "egg"), entity(123, "chopped-chocolate")]
        a = fs.assess(snapshot(*entities), native(228996))
        self.assertEqual(len(a.credits), 1)
        self.assertEqual(a.credits[0].entity, 6)
        self.assertAlmostEqual(a.useful_wip, .45)
        self.assertGreater(a.waste_penalty, 0)

    def test_mix_to_fry_pipeline_monotonic_disjoint_buckets(self):
        n = native(228996)
        kit = [fs.FLOUR, fs.EGG, fs.CHOCOLATE]
        mixed = fs.assess(snapshot(entity(6, "mixer", kit, mix=12)), n)
        fryer = fs.assess(snapshot(entity(5, "frier", kit)), n)
        cooked = fs.assess(snapshot(entity(10, "plate", kit)), n, food_evidence={10: tree("Cooked", *kit)})
        self.assertAlmostEqual(mixed.pipeline["donut"], fryer.pipeline["donut"])
        self.assertGreater(cooked.pipeline["donut"], fryer.pipeline["donut"])
        self.assertEqual(fryer.stages["mix"], 0)
        self.assertEqual(cooked.stages["mix"], 0)
        self.assertEqual(cooked.native_score, 0)

    def test_partial_mixed_kit_no_timer_credit(self):
        n = native(228996)
        a = fs.assess(snapshot(entity(6, "mixer", [fs.FLOUR, fs.EGG], mix=1)), n)
        b = fs.assess(snapshot(entity(6, "mixer", [fs.FLOUR, fs.EGG], mix=12)), n)
        self.assertEqual(a.useful_wip, b.useful_wip)
        self.assertAlmostEqual(b.useful_wip, .1)

    def test_duplicate_bowl_ingredients_not_useful(self):
        a = fs.assess(snapshot(entity(6, "mixer", [fs.FLOUR, fs.FLOUR, fs.EGG], mix=10)), native(228996, 228996))
        self.assertEqual(a.useful_wip, 0)
        self.assertTrue(any("duplicate" in gap for gap in a.evidence_gaps))

    def test_native_cooked_component_preserved_in_hotdog(self):
        n = native(296560)
        pot = fs.assess(snapshot(entity(2, "pot", [fs.SAUSAGE], cook=12)), n, food_evidence={2: tree("Cooked", fs.SAUSAGE)})
        meal = fs.assess(snapshot(entity(120, "hotdog", [fs.SAUSAGE, fs.BUN])), n,
                         food_evidence={120: {"children": [tree("Cooked", fs.SAUSAGE), tree("Raw", fs.BUN)]}})
        self.assertEqual(pot.pipeline["sausage"], meal.pipeline["sausage"])
        self.assertEqual(meal.pipeline["bun"], 1)
        self.assertEqual(meal.native_score, 0)

    def test_unknown_composite_does_not_claim_cooked_proof(self):
        a = fs.assess(snapshot(entity(120, "hotdog", [fs.SAUSAGE, fs.BUN])), native(296560))
        self.assertAlmostEqual(a.pipeline["sausage"], .1)
        self.assertTrue(a.evidence_gaps)

    def test_parent_attachment_does_not_duplicate_food(self):
        item = entity(2, "pot", [fs.SAUSAGE], cook=6)
        parent = entity(17, "heat-station", data={"attachment": {"path": [2]}})
        chef = entity(103, "chef-blue", data={"attachment": {"path": [2]}}, chef={})
        a = fs.assess(snapshot(item, parent, chef), native(296560))
        self.assertEqual(len(a.credits), 1)

    def test_duplicate_entity_ids_refused(self):
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            fs.assess(snapshot(entity(2, "pot"), entity(2, "pot")), native(296560))

    def test_ruined_offheat_food_gives_no_credit(self):
        a = fs.assess(snapshot(entity(2, "pot", [fs.SAUSAGE], cook=24)), native(296560))
        self.assertEqual(a.useful_wip, 0)
        self.assertGreaterEqual(a.waste_penalty, 2)

    def test_heat_deadline_penalizes_same_credit_near_burn(self):
        def state(progress):
            food = entity(2, "pot", [fs.SAUSAGE], cook=progress)
            food["data"]["attachmentParent"] = {"path": [17]}
            return snapshot(food, entity(17, "heat-station"))
        a, b = [fs.assess(state(p), native(296560)) for p in (12, 23)]
        self.assertEqual(a.useful_wip, b.useful_wip)
        self.assertGreater(b.deadline_penalty, a.deadline_penalty)

    def test_offheat_has_no_deadline_countdown(self):
        food = entity(2, "pot", [fs.SAUSAGE], cook=23)
        food["data"]["attachmentParent"] = {"path": [32]}
        a = fs.assess(snapshot(food, entity(32, "counter")), native(296560))
        self.assertEqual(a.deadlines, ())

    def test_wash_to_clean_plate_monotonic_not_cumulative(self):
        n = native(296560)
        a = fs.assess(snapshot(entity(75, "sink", data={"numPlates": 3}, washingProgress=3)), n)
        b = fs.assess(snapshot(entity(10, "plate")), n)
        self.assertAlmostEqual(a.useful_wip, b.useful_wip)
        self.assertEqual(b.stages["wash"], 0)

    def test_no_native_score_remains_missing(self):
        a = fs.assess(snapshot(entity(107, "", data={"kitchenFlowController": {"teamScore": "AAAA"}})), None)
        self.assertIsNone(a.native_score)

    def test_nonfinite_or_boolean_metrics_refused(self):
        for bad in (True, float("nan"), float("inf")):
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                fs.assess(snapshot(entity(2, "pot", cook=bad)), native(296560))
        with self.assertRaises(ValueError):
            fs.assess(snapshot(), native(296560, score=True))


class SearchTests(unittest.TestCase):
    def test_deterministic_bounded_segments(self):
        a = fs.generate_segments([106, 104, 103, 105], seed=7, limit=32)
        b = fs.generate_segments([103, 104, 105, 106], seed=7, limit=32)
        self.assertEqual(a, b)
        self.assertLessEqual(len(a), 32)
        self.assertNotEqual(a, fs.generate_segments([103, 104, 105, 106], seed=8, limit=32))
        self.assertTrue(any(p.use and (p.x or p.y) for s in a for p in s.pads))
        self.assertTrue(any(sum(bool(p.x or p.y or p.use or p.pickup or p.dash) for p in s.pads) > 1 for s in a))
        self.assertTrue(all(len(s.input_frames()) == s.frames for s in a))
        self.assertTrue(all(len(frame) == 4 for s in a for frame in s.input_frames()))

    def test_input_validation(self):
        with self.assertRaises(ValueError):
            fs.Pad(103, 2)
        with self.assertRaises(ValueError):
            fs.Pad(103, use=1)
        with self.assertRaises(ValueError):
            fs.Segment(3, (fs.Pad(103),) * 4)
        with self.assertRaises(ValueError):
            segment(claims=((23, 103), (23, 106)))

    def test_reservation_overlap_and_half_open_boundary(self):
        claim = fs.Reservation("station:23", 106, 0, 6)
        with self.assertRaisesRegex(ValueError, "station:23"):
            fs.reserve([claim], segment(claims=((23, 103),)), 0)
        self.assertTrue(fs.reserve([claim], segment(claims=((23, 103),)), 6))
        with self.assertRaisesRegex(ValueError, "chef:103"):
            fs.reserve([fs.Reservation("chef:103", 103, 0, 6)], segment(), 0)

    def test_pareto_retains_food_resource_balance(self):
        n = native(296560)
        food = fs.assess(snapshot(entity(2, "pot", [fs.SAUSAGE], cook=12)), n)
        plates = fs.assess(snapshot(entity(10, "plate"), entity(32, "counter")), n)
        self.assertFalse(fs.dominates(food, plates))
        self.assertFalse(fs.dominates(plates, food))
        nodes = [fs.Node(fs.Evaluation(snapshot(), n, 0), assessment, (segment(label=str(i)),)) for i, assessment in enumerate((food, plates))]
        self.assertEqual(len(fs.select_beam(nodes, 2)), 2)

    def test_authoritative_score_primary_over_huge_wip(self):
        earned = fs.assess(snapshot(), native(296560, score=1))
        preparation = fs.assess(snapshot(*[entity(i, "pot", [fs.SAUSAGE], cook=12) for i in range(2, 20)]), native(*([296560] * 12)))
        self.assertGreater(earned.rank, preparation.rank)

    def test_search_bounds_failures_records_and_determinism(self):
        initial = fs.Evaluation(snapshot(frame=0), native(296560), 0, checkpoint="initial")
        def proposals(parent, seed, limit):
            return [segment(x=1, label="move-use", use=True), segment(x=-1, label="failure"), segment(frames=600, label="over-budget")]
        def evaluator(parent, action):
            if action.pads[0].x < 0:
                return fs.Evaluation(parent.evaluation.snapshot, parent.evaluation.native_round, 0, failure="observed blocked action", evidence_id="failed-native-receipt")
            frame = parent.evaluation.snapshot["frame"] + action.frames
            return fs.Evaluation(snapshot(entity(2, "pot", [fs.SAUSAGE], cook=frame / 60), frame=frame),
                                 native(296560, elapsed=frame / 60), action.frames, evidence_id=f"synthetic-test-only:{frame}")
        config = fs.SearchConfig(max_depth=10, max_evaluations=5, proposals_per_node=3, max_total_frames=18)
        a = fs.bounded_beam(initial, evaluator, proposals, config=config)
        b = fs.bounded_beam(initial, evaluator, proposals, config=config)
        self.assertEqual(a["best"], b["best"])
        self.assertEqual(a["evaluations"], 5)
        self.assertLessEqual(sum(s["frames"] for s in a["best"]["segments"]), 18)
        self.assertTrue(any(o["failure"] and "blocked" in o["failure"] and o["evidenceId"] for o in a["outcomes"]))
        self.assertTrue(any(o["failure"] == "total frame budget" and not o["evaluated"] for o in a["outcomes"]))
        self.assertTrue(all("segment" in o for o in a["outcomes"]))
        self.assertEqual(a["best"]["objective"]["native_score"], 0)

    def test_missing_score_failed_interval_and_callback_exception(self):
        initial = fs.Evaluation(snapshot(), native(296560), 0)
        cases = [lambda p, s: fs.Evaluation(snapshot(frame=6), None, 6),
                 lambda p, s: fs.Evaluation(snapshot(frame=5), native(296560), 5)]
        for evaluator in cases:
            result = fs.bounded_beam(initial, evaluator, lambda p, seed, limit: [segment()])
            self.assertIsNotNone(result["outcomes"][0]["failure"])
            self.assertEqual(result["best"]["segments"], [])
        def raises(parent, action):
            raise RuntimeError("transport failed")
        result = fs.bounded_beam(initial, raises, lambda p, seed, limit: [segment()])
        self.assertIn("transport failed", result["outcomes"][0]["failure"])

    def test_conflicting_resource_does_not_call_evaluator(self):
        def should_not_run(parent, action):
            self.fail("Conflicting action reached the native evaluator")
        result = fs.bounded_beam(fs.Evaluation(snapshot(), native(296560), 0), should_not_run,
                                 lambda p, seed, limit: [segment(claims=((23, 103),))],
                                 reservations=[fs.Reservation("station:23", 106, 0, 10)])
        self.assertEqual(result["evaluations"], 0)
        self.assertFalse(result["outcomes"][0]["evaluated"])

    def test_initial_unknown_native_ledger_rejected(self):
        with self.assertRaises(ValueError):
            fs.bounded_beam(fs.Evaluation(snapshot(), None, 0), None, None)


if __name__ == "__main__":
    unittest.main()
