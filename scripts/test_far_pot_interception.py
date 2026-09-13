"""Captured negative checks for the independent native interception proof; no game I/O."""
import copy
import gzip
import json
import unittest
from pathlib import Path

from check_far_pot_interception import audit_rows, check

ROOT = Path(__file__).resolve().parents[1]


class FarInterceptionEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.trace = ROOT / 'artifacts/far-pot-interception-b.jsonl.gz'
        with gzip.open(cls.trace, 'rt', encoding='utf-8-sig') as stream:
            cls.rows = [json.loads(line) for line in stream]
        cls.plan = json.loads((ROOT / 'routes/probes/far-pot-interception-b.json').read_text(encoding='utf-8-sig'))
        cls.result = json.loads((ROOT / 'artifacts/far-pot-interception-b-result.json').read_text(encoding='utf-8-sig'))

    def test_original_closed_native_probe(self):
        report = audit_rows(self.rows, self.plan, self.result)
        mechanism = check(self.trace)
        self.assertEqual(report['ordinaryUnitSteps'], 216)
        self.assertEqual(len(report['completedActions']), 11)
        self.assertEqual(report['originalSource']['pickup']['frame'], 163)
        self.assertEqual(report['nativeFlight']['frame'], 189)
        self.assertEqual(mechanism['caught']['frame'], 196)
        self.assertEqual(mechanism['consumed']['frame'], 213)
        self.assertEqual(mechanism['consumed']['recoveryFramesFromCatch'], 17)
        self.assertTrue(report['completeResultBoundToFinalResponse'] and report['finalInputNeutral'])

    def test_result_requires_entire_final_response(self):
        for mutation in ['timer', 'paused', 'inputs', 'source']:
            with self.subTest(mutation=mutation):
                result = copy.deepcopy(self.result)
                if mutation == 'timer': result['state']['timer'] += .25
                if mutation == 'paused': result['paused'] = False
                if mutation == 'inputs': result['inputs'][2]['use'] = True
                if mutation == 'source': result['state']['entities'][0]['observedOrdinal'] += 1
                with self.assertRaises(ValueError): audit_rows(self.rows, self.plan, result)

    def test_captured_trace_mutations_fail(self):
        def frame(row, number):
            return row.get('kind') == 'call' and row['response']['state']['gameplayFrame'] == number

        mutations = ['final-neutral', 'failed-call', 'extra-command-field', 'missing-frame', 'second-restart', 'duration', 'physics-clock',
                     'input-response', 'other-player-input', 'pickup-provenance', 'pre-resolve-source', 'pickup-registration-frame', 'source-ordinal', 'source-registration', 'source-composition',
                     'missing-native-flight', 'foreign-catch', 'changed-home', 'final-food-lost', 'unmatched-action', 'native-throw-force']
        for mutation in mutations:
            with self.subTest(mutation=mutation):
                rows = list(self.rows)
                number = {'final-neutral': 216, 'missing-frame': 190, 'pickup-provenance': 162, 'pre-resolve-source': 164, 'pickup-registration-frame': 163,
                          'missing-native-flight': 189, 'foreign-catch': 197, 'final-food-lost': 216}.get(mutation, 184)
                if mutation in ['unmatched-action', 'native-throw-force']:
                    name = 'actionComplete' if mutation == 'unmatched-action' else 'throwResolved'
                    index = next(i for i, r in enumerate(rows) if r.get('kind') == 'event' and r.get('name') == name
                                 and (name != 'throwResolved' or r['value']['targetEntityId'] == 2))
                else: index = next(i for i, r in enumerate(rows) if frame(r, number))
                row = copy.deepcopy(rows[index]); rows[index] = row
                if mutation == 'missing-frame': del rows[index]
                elif mutation == 'unmatched-action': row['value']['action']['station'] = 'Onion'
                elif mutation == 'native-throw-force': row['value']['nativeThrowForce'] = 19
                else:
                    req, res = row['request'], row['response']; state = res['state']
                    chefs = {c['playerId']: c for c in state['chefs']}
                    entity = lambda identity: next(e for e in state['entities'] if e['id'] == identity)
                    if mutation == 'final-neutral': req['inputs'][2]['use'] = res['inputs'][2]['use'] = True
                    if mutation == 'failed-call': res['ok'] = False
                    if mutation == 'extra-command-field': req['setPosition'] = {'x': 0, 'z': 0}
                    if mutation == 'second-restart': req.update(command='restart', seed=0, isolateRecipeRandom=True)
                    if mutation == 'duration': state['roundDuration']['seconds'] = 300
                    if mutation == 'physics-clock': state['fixedDeltaTime'] = .01
                    if mutation == 'input-response': res['inputs'][2]['x'] = .75
                    if mutation == 'other-player-input': req['inputs'][0]['x'] = res['inputs'][0]['x'] = .5
                    if mutation == 'pickup-provenance': chefs[2]['pickupTargetId'] = 69
                    if mutation == 'source-ordinal': entity(125)['observedOrdinal'] += 1
                    if mutation == 'pre-resolve-source': entity(125)['observedOrdinal'] += 1
                    if mutation == 'pickup-registration-frame':
                        event = next(e for e in state['entityRegistration']['events'] if (e.get('entity') or {}).get('entityId') == 125)
                        event['gameplayFrame'] -= 1
                    if mutation == 'source-registration':
                        event = next(e for e in state['entityRegistration']['events'] if (e.get('entity') or {}).get('entityId') == 125)
                        event['entity']['unityInstanceId'] += 1
                    if mutation == 'source-composition': entity(125)['composition']['state'] = 'Chopped'
                    if mutation == 'missing-native-flight': entity(125)['throwFlying'] = False
                    if mutation == 'foreign-catch': chefs[0]['heldEntityId'] = 125; chefs[3]['heldEntityId'] = 0
                    if mutation == 'changed-home': entity(17)['position']['x'] += .1
                    if mutation == 'final-food-lost': entity(2)['composition'] = None
                # Keep final response synchronized for final-state mutations so
                # rejection tests the invariant, rather than only result binding.
                result = row['response'] if mutation in ['final-neutral', 'final-food-lost'] else self.result
                with self.assertRaises(ValueError): audit_rows(rows, self.plan, result)

    def test_authored_plan_is_bound(self):
        plan = copy.deepcopy(self.plan)
        plan['jobs'][3]['actions'][-1]['station'] = '17'
        with self.assertRaises(ValueError): audit_rows(self.rows, plan, self.result)


if __name__ == '__main__':
    unittest.main()
