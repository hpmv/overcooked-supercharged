"""Native recorded witnesses distinguish input parity from actual pickup."""
import copy
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
ARTIFACTS = ROOT / 'artifacts'
if not (ARTIFACTS / 'framework-migration' / 'native-s').exists():
    ARTIFACTS = ROOT.parent / 'artifacts'
sys.path.insert(0, str(ROOT / 'scripts'))
from framework_input_probe import (delivery_outcome, native_physics_comparison, pickup_outcome,
                                   round_end_contact_sidecar_comparison,
                                   round_end_terminal_comparison)


def witness(run):
    rows = json.loads((ARTIFACTS / 'framework-migration' / run / 'pickup-probe/observations.json').read_text())
    def take(label):
        return next(r['response'] for r in rows if r['label'] == label)
    return [take('base'), take('base-native'), take('original'), take('original-native')]


class PickupOutcomeTest(unittest.TestCase):
    goal = {'type': 'pickup', 'chef': 103, 'item': 12, 'source': 41}

    def test_actual_s_pickup_accepted(self):
        self.assertTrue(pickup_outcome(*witness('native-s'), self.goal)['achieved'])

    def test_nested_dynamic_source_paths_are_not_required_to_equal_entity_ids(self):
        args = witness('native-s')
        before = {entity['id']: entity for entity in args[0]['entities']}
        after = {entity['id']: entity for entity in args[2]['entities']}
        before[self.goal['source']]['path'] = [34, 0]
        after[self.goal['source']]['path'] = [34, 0]
        before[self.goal['item']]['path'] = [34, 0, 0]
        after[self.goal['item']]['path'] = [34, 0, 0]
        before[self.goal['item']]['data']['attachmentParent'] = {'path': [34, 0]}
        before[self.goal['source']]['data']['attachment'] = {'path': [34, 0, 0]}
        after[self.goal['chef']]['data']['attachment'] = {'path': [34, 0, 0]}
        self.assertTrue(pickup_outcome(*args, self.goal)['achieved'])

    def test_q_and_u_identical_replays_do_not_prove_pickup(self):
        for run in ('native-q', 'native-u'):
            with self.subTest(run=run):
                proof = pickup_outcome(*witness(run), self.goal)
                self.assertFalse(proof['achieved'])
                self.assertFalse(proof['checks']['finalChefHoldsItem'])

    def test_each_attachment_side_is_required(self):
        for identity, field, replacement in ((12, 'attachmentParent', [41]),
                                             (103, 'attachment', [13]),
                                             (41, 'attachment', [12])):
            args = witness('native-s')
            e = next(e for e in args[2]['entities'] if e['id'] == identity)
            e['data'][field] = {'path': replacement}
            with self.subTest(identity=identity):
                self.assertFalse(pickup_outcome(*args, self.goal)['achieved'])

    def test_native_food_missing_or_changed_fails(self):
        for missing in (True, False):
            args = witness('native-s')
            rows = args[3]['detail']['entities']
            if missing:
                args[3]['detail']['entities'] = [e for e in rows if e['id'] != 12]
            else:
                next(e for e in rows if e['id'] == 12)['composition']['children'].append({'type': 'unexpected'})
            self.assertFalse(pickup_outcome(*args, self.goal)['achieved'])

    def test_native_proxy_source_requires_body_alias_then_item_body_retirement(self):
        args = witness('native-s')
        source = next(entity for entity in args[0]['entities'] if entity['id'] == self.goal['source'])
        args[0]['entities'].remove(source)
        args[2]['entities'] = [entity for entity in args[2]['entities'] if entity['id'] != self.goal['source']]
        args[1]['bridge']['nativePhysics']['bodies'].extend((
            {'entityId': self.goal['item'], 'bodyInstanceId': -200},
            {'entityId': self.goal['source'], 'bodyInstanceId': -200}))
        args[3]['bridge']['nativePhysics']['bodies'].append(
            {'entityId': self.goal['source'], 'bodyInstanceId': -200})
        # Registry identities are stable paths, but dynamic descendants are not
        # required to use their entity ID as their hierarchy path.
        next(entity for entity in args[0]['entities'] if entity['id'] == self.goal['item'])['path'] = [30, 0, 0]
        next(entity for entity in args[2]['entities'] if entity['id'] == self.goal['item'])['path'] = [30, 0, 0]
        next(entity for entity in args[2]['entities'] if entity['id'] == self.goal['chef'])['data']['attachment']['path'] = [30, 0, 0]
        proof = pickup_outcome(*args, self.goal)
        self.assertTrue(proof['achieved'])
        self.assertEqual(proof['sourceLifecycle'], 'native-proxy-body-survived-item-alias-retired')

        args[3]['bridge']['nativePhysics']['bodies'].append(
            {'entityId': self.goal['item'], 'bodyInstanceId': -200})
        proof = pickup_outcome(*args, self.goal)
        self.assertFalse(proof['achieved'])
        self.assertFalse(proof['checks']['finalSourceReleasedItem'])

    def test_invalid_goal_rejected(self):
        goal = copy.deepcopy(self.goal)
        goal['chef'] = True
        with self.assertRaises(ValueError):
            pickup_outcome(*witness('native-s'), goal)


class NativePhysicsComparisonTest(unittest.TestCase):
    @staticmethod
    def physics(entity=51, instance=-100, x=1.25):
        return {'fixedDeltaTime': .02, 'bodies': [{
            'entityId': entity, 'bodyInstanceId': instance,
            'position': {'x': x, 'y': .5, 'z': 2}, 'sleeping': False,
        }]}

    def test_dynamic_allocation_handle_is_reported_but_excluded(self):
        result = native_physics_comparison(self.physics(), self.physics(instance=-200), {1, 2, 3})
        self.assertTrue(result['equal'])
        self.assertIsNone(result['firstDifference'])
        self.assertEqual(result['changedDynamicIncarnationIds'], [51])
        self.assertEqual(result['dynamicIncarnations']['original']['51'], -100)
        self.assertEqual(result['dynamicIncarnations']['replay']['51'], -200)

    def test_fixed_allocation_handle_remains_exact(self):
        result = native_physics_comparison(self.physics(), self.physics(instance=-200), {51})
        self.assertFalse(result['equal'])
        self.assertEqual(result['firstDifference']['field'], '$nativePhysics/bodies/0/bodyInstanceId')

    @staticmethod
    def reincarnation_receipt(frame=444, owner=2, container=51, path=(2,), verified=True):
        return {'bridge': {'nativeCheckpoints': {
            'restoreAttempts': 3,
            'lastRestore': {
                'verified': verified, 'frame': frame, 'attempt': 3,
                'nativeInitialAttachmentRecreation': {
                    'ownerId': owner, 'containerId': container, 'path': list(path),
                    'attachmentIdentityRebound': True, 'bodyIdentityRebound': True,
                    'identityRebound': True, 'collidersRebound': True, 'rebound': True,
                },
            },
            'nativeDynamicWarp': {
                'verified': verified,
                'spawned': [{
                    'id': owner, 'containerId': container, 'path': list(path),
                    'latentInitialFactory': True,
                    'retiredLatentIntermediatePhysicsPair': True,
                }],
            },
        }}}

    def test_verified_fixed_reincarnation_handle_is_reported_but_excluded(self):
        receipt = self.reincarnation_receipt()
        result = native_physics_comparison(self.physics(), self.physics(instance=-200),
                                           {2, 51}, receipt, 444)
        self.assertTrue(result['equal'])
        self.assertEqual(result['changedVerifiedFixedIncarnationIds'], [51])
        self.assertEqual(result['verifiedFixedIncarnations']['original']['51'], -100)
        self.assertEqual(result['verifiedFixedIncarnations']['replay']['51'], -200)

    def test_verified_fixed_reincarnation_still_requires_exact_physics(self):
        result = native_physics_comparison(self.physics(), self.physics(instance=-200, x=1.5),
                                           {2, 51}, self.reincarnation_receipt(), 444)
        self.assertFalse(result['equal'])
        self.assertEqual(result['firstDifference']['field'], '$nativePhysics/bodies/0/position/x')

    def test_unverified_fixed_reincarnation_fails_closed(self):
        result = native_physics_comparison(self.physics(), self.physics(instance=-200),
                                           {2, 51}, self.reincarnation_receipt(verified=False), 444)
        self.assertFalse(result['equal'])
        self.assertEqual(result['firstDifference']['field'], '$nativePhysics/bodies/0/bodyInstanceId')

    def test_wrong_restore_frame_fails_closed(self):
        result = native_physics_comparison(self.physics(), self.physics(instance=-200),
                                           {2, 51}, self.reincarnation_receipt(frame=443), 444)
        self.assertFalse(result['equal'])

    def test_unrelated_fixed_body_handle_remains_exact(self):
        left = self.physics(entity=51)
        left['bodies'].append(self.physics(entity=52, instance=-300)['bodies'][0])
        right = self.physics(entity=51, instance=-200)
        right['bodies'].append(self.physics(entity=52, instance=-400)['bodies'][0])
        result = native_physics_comparison(left, right, {2, 51, 52},
                                           self.reincarnation_receipt(), 444)
        self.assertFalse(result['equal'])
        self.assertEqual(result['firstDifference']['field'], '$nativePhysics/bodies/1/bodyInstanceId')

    def test_dynamic_pose_remains_exact(self):
        result = native_physics_comparison(self.physics(), self.physics(instance=-200, x=1.5), set())
        self.assertFalse(result['equal'])
        self.assertEqual(result['firstDifference']['field'], '$nativePhysics/bodies/0/position/x')

    def test_dynamic_logical_identity_remains_exact(self):
        result = native_physics_comparison(self.physics(), self.physics(entity=53, instance=-200), set())
        self.assertFalse(result['equal'])
        self.assertEqual(result['firstDifference']['field'], '$nativePhysics/bodies/0/entityId')


class RoundEndTerminalComparisonTest(unittest.TestCase):
    @staticmethod
    def state(frame=8999):
        return {
            'frame': frame,
            'entities': [{'id': 44, 'position': {'x': 1, 'y': .05, 'z': 2}}],
            'registry': [{'Id': 44, 'Components': ['Rigidbody']}],
            'rawInput': {
                'outcome': 'terminal', 'active': False, 'error': None, 'startFrame': 8492,
                'payloadFrames': 700, 'totalFramesIncludingRelease': 702,
                'emittedFrames': 507, 'observedFrames': 507,
                'recordingSha256': 'abc', 'expectedTerminalGameState': 'RunLevelOutro',
                'observedTerminalGameState': 'RunLevelOutro', 'terminalFrame': frame,
                'terminalObservedFrames': 507, 'terminalEmittedFrames': 507,
            },
        }

    @staticmethod
    def receipt(nonce=3, unity_frame=100):
        return {
            'nonce': nonce, 'frame': 8999, 'phase': 'held', 'serverIteratorPc': -1,
            'clientIteratorPc': 1, 'dormantOutroPc': 0, 'deferredServerPc': -1,
            'clientTimerZeroCalls': 0,
            'lifecycle': {'serverState': 'RunLevelOutro', 'clientFinished': False},
            'nativeRound': {'available': True, 'elapsed': 150, 'remaining': 0,
                            'recipeRandom': {'nextIndex': 0, 'history': [], 'nativeRecipes': []}},
            'food': {'source': 'native-server-preparation-composition', 'unityFrame': unity_frame,
                     'entities': [{'id': 2, 'composition': None}]},
            'physics': {'fixedDeltaTime': .02, 'bodies': [
                {'entityId': 44, 'bodyInstanceId': -44, 'position': {'x': 1, 'y': .05, 'z': 2}}]},
            'clocks': {'nativeServerClock': [1., 2., 3.], 'nativeClientClock': [1., 2., 3., 4., 5., 6.],
                       'source': 100., 'ticks': 6000, 'step': 1/60,
                       'serverTimer': {'elapsed': 150., 'timeLeft': 0, 'limit': 150., 'suppressed': False},
                       'clientTimer': {'elapsed': 150., 'timeLeft': 0, 'limit': 150., 'suppressed': False}},
        }

    def compare(self, mutate=None):
        original, replay = self.state(), self.state()
        first, second = self.receipt(), self.receipt(nonce=4, unity_frame=999)
        if mutate:
            mutate(replay, second)
        return round_end_terminal_comparison(original, replay, first, second, {44}, {}, 8492)

    def test_exact_terminal_and_food_unity_frame_only_difference_pass(self):
        result = self.compare()
        self.assertTrue(result['equal'])
        self.assertTrue(all(result['checks'].values()))

    def test_lifecycle_mutation_fails(self):
        result = self.compare(lambda state, receipt: receipt['lifecycle'].__setitem__('clientFinished', True))
        self.assertFalse(result['equal'])
        self.assertFalse(result['checks']['lifecycle'])

    def test_fixed_physics_mutation_fails(self):
        result = self.compare(lambda state, receipt: receipt['physics']['bodies'][0]['position'].__setitem__('y', 0))
        self.assertFalse(result['equal'])
        self.assertFalse(result['checks']['nativePhysics'])

    def test_nonce_must_advance_exactly_once(self):
        result = self.compare(lambda state, receipt: receipt.__setitem__('nonce', 5))
        self.assertFalse(result['equal'])
        self.assertFalse(result['checks']['nonceProgression'])

    def test_clock_mutation_fails(self):
        result = self.compare(lambda state, receipt: receipt['clocks'].__setitem__('ticks', 6001))
        self.assertFalse(result['equal'])
        self.assertFalse(result['checks']['nativeClocks'])


class RoundEndContactSidecarComparisonTest(unittest.TestCase):
    baseline = {'captures': 2, 'restores': 1, 'transformCaptures': 2, 'transformRestores': 1}

    @staticmethod
    def status(restored=False):
        return {
            'pendingContactPoolAction': 'none', 'automaticRestorePending': not restored,
            'contactPoolSnapshotCaptured': True, 'contactPoolSnapshotFrame': 8492,
            'contactPoolCaptures': 3, 'contactPoolRestores': 2 if restored else 1,
            'transformDispatchSnapshotCaptured': True, 'transformDispatchSnapshotFrame': 8492,
            'transformDispatchCaptures': 3, 'transformDispatchRestores': 2 if restored else 1,
        }

    def test_capture_is_pending_for_replay(self):
        result = round_end_contact_sidecar_comparison(
            self.status(), self.baseline, 8492, 1, 0, True, True)
        self.assertTrue(result['equal'])

    def test_replay_consumes_exactly_one_restore(self):
        result = round_end_contact_sidecar_comparison(
            self.status(True), self.baseline, 8492, 1, 1, False, True)
        self.assertTrue(result['equal'])

    def test_missing_transform_restore_fails(self):
        status = self.status(True)
        status['transformDispatchRestores'] = 1
        result = round_end_contact_sidecar_comparison(
            status, self.baseline, 8492, 1, 1, False, True)
        self.assertFalse(result['equal'])
        self.assertFalse(result['checks']['transformRestoreCount'])

    def test_reused_checkpoint_consumes_pending_then_replay_restore(self):
        baseline = {'captures': 2, 'restores': 2,
                    'transformCaptures': 2, 'transformRestores': 2}
        restored = self.status(True)
        restored.update(contactPoolCaptures=2, contactPoolRestores=4,
                        transformDispatchCaptures=2, transformDispatchRestores=4)
        result = round_end_contact_sidecar_comparison(
            restored, baseline, 8492, 0, 2, False, True)
        self.assertTrue(result['equal'])

    def test_reused_checkpoint_must_not_add_a_capture(self):
        baseline = {'captures': 2, 'restores': 2,
                    'transformCaptures': 2, 'transformRestores': 2}
        pending = self.status()
        pending.update(contactPoolCaptures=3, contactPoolRestores=3,
                       transformDispatchCaptures=2, transformDispatchRestores=3)
        result = round_end_contact_sidecar_comparison(
            pending, baseline, 8492, 0, 1, True, True)
        self.assertFalse(result['equal'])
        self.assertFalse(result['checks']['captureCount'])


class DeliveryOutcomeTest(unittest.TestCase):
    @staticmethod
    def receipt(deliveries=0, total=0, base_score=0, deductions=0, orders=(1, 2, 3), plated=True):
        return {
            'bridge': {'nativeRound': {
                'ledger': {'total': total, 'baseScore': base_score, 'tips': total-base_score,
                           'multiplier': deliveries, 'combo': deliveries,
                           'deliveries': deliveries, 'deductions': deductions},
                'orders': [{'id': identity, 'baseValue': 20, 'recipe': 'Sushi_PlainFish'}
                           for identity in orders],
            }},
            'detail': {'entities': [{
                'id': 2,
                'composition': {'type': 'CompositeAssembledNode',
                                'children': ([{'type': 'IngredientAssembledNode', 'id': 23600}]
                                             if plated else []), 'optional': []},
            }]},
        }

    def test_single_native_delivery_accepted(self):
        proof = delivery_outcome(self.receipt(), self.receipt(1, 28, 20, orders=(2, 3)))
        self.assertTrue(proof['achieved'])
        self.assertTrue(all(proof['checks'].values()))

    def test_delivery_requires_score_order_and_clean_ledger_transition(self):
        mutations = (
            self.receipt(0, 28, 20, orders=(2, 3)),
            self.receipt(1, 0, 0, orders=(2, 3)),
            self.receipt(1, 28, 20, deductions=1, orders=(2, 3)),
            self.receipt(1, 28, 20, orders=(1, 2, 3)),
            self.receipt(1, 28, 20, orders=(3, 2)),
        )
        for end in mutations:
            with self.subTest(end=end['bridge']['nativeRound']):
                self.assertFalse(delivery_outcome(self.receipt(), end)['achieved'])

    def test_delivery_requires_one_initial_plated_composition(self):
        self.assertFalse(delivery_outcome(self.receipt(plated=False),
                                          self.receipt(1, 28, 20, orders=(2, 3)))['achieved'])


if __name__ == '__main__':
    unittest.main()
