"""Nine captured negative cases for the native preparation-order proof.

Fixture projection bounds test memory; the CLI checks complete, unprojected responses.
"""
import copy
import gzip
import json
from pathlib import Path
import unittest

from check_plated_sauce_before_onion import check

ROOT = Path(__file__).resolve().parents[1]
STATE_KEYS = ['frame', 'fixedFrame', 'gameplayFrame', 'physicsStepsThisFrame', 'scene', 'roundDuration', 'captureFramerate',
              'fixedDeltaTime', 'timerSuppressed', 'serverRoundActive', 'clientRoundActive', 'score', 'baseScore', 'tips',
              'deductions', 'delivered', 'instrumentation', 'gameEventsInstalled', 'gameEventsDropped', 'gameEventsError',
              'timer', 'clientTime', 'clientDeltaTime', 'logicalTime', 'entityRegistration', 'chefs']
ENTITY_KEYS = ['id', 'active', 'observedOrdinal', 'components', 'position', 'attachedEntityId', 'composition',
               'cookingTime', 'cookingTypeId', 'cookingProgress', 'workProgress', 'spawnPrefab', 'name', 'throwFlying', 'throwerEntityId', 'switchIndex']


def project_response(response):
    result = {k: response[k] for k in ['ok', 'paused', 'session', 'inputs']}
    state = response['state']; result['state'] = {k: state[k] for k in STATE_KEYS}
    result['state']['entities'] = [{k: e[k] for k in ENTITY_KEYS} for e in state['entities']]
    return result


class PlatedSauceBeforeOnionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.plan = json.loads((ROOT/'routes/probes/plated-hotdog-sauce-before-onion-b.json').read_text(encoding='utf-8-sig'))
        cls.rows = []
        with gzip.open(ROOT/'artifacts/plated-hotdog-sauce-before-onion-b.jsonl.gz', 'rt') as stream:
            for line in stream:
                row = json.loads(line)
                if row.get('kind') == 'call': row = dict(row, response=project_response(row['response']))
                cls.rows.append(row)
        cls.result = project_response(json.loads((ROOT/'artifacts/plated-hotdog-sauce-before-onion-b-result.json').read_text(encoding='utf-8-sig')))

    def test_captured_native_pass(self):
        proof = check(iter(self.rows), self.plan, self.result)
        self.assertEqual([v['frame'] for v in proof['transitions']], [471, 792, 833, 1082])
        self.assertEqual(proof['samePlateRecoveryFrame'], 474)
        self.assertEqual(proof['samePlateFinalOutputFrame'], 1163)
        self.assertEqual(proof['finalGameplayFrame'], 1165)
        self.assertEqual(proof['vessels']['pot']['cooked']['frame'], 784)
        self.assertEqual(proof['vessels']['pan']['cooked']['frame'], 1074)
        self.assertEqual(len(proof['completedActions']), 21)

    def mutated(self, frame, mutate):
        for row in self.rows:
            if row.get('kind') == 'call' and row['response']['state']['gameplayFrame'] == frame:
                row = copy.deepcopy(row); mutate(row)
            yield row

    def test_nine_rejected_evidence_mutations(self):
        def entity(row, identity): return next(e for e in row['response']['state']['entities'] if e['id'] == identity)
        def registration_removed(row):
            audit = row['response']['state']['entityRegistration']
            audit['events'] = [e for e in audit['events'] if (e.get('entity') or {}).get('entityId') != 123]
        def missing_edge(row):
            for pads in [row['request']['inputs'], row['response']['inputs']]:
                next(p for p in pads if p['player'] == 0)['pickup'] = False
        def sauce_replaced(row):
            entity(row, 10)['composition']['children'][-1]['id'] = 461162
        cases = [
            ('plate incarnation', self.mutated(833, lambda r: entity(r, 10).__setitem__('observedOrdinal', 999))),
            ('native crate registration', self.mutated(30, registration_removed)),
            ('original pot moved', self.mutated(600, lambda r: entity(r, 7)['position'].__setitem__('x', 17))),
            ('native cook duration', self.mutated(900, lambda r: entity(r, 9).__setitem__('cookingTime', 6))),
            ('missing gameplay frame', (r for r in self.rows if not (r.get('kind') == 'call' and r['response']['state']['gameplayFrame'] == 600))),
            ('mustard ordering', self.mutated(833, sauce_replaced)),
            ('fresh native sauce edge', self.mutated(833, missing_edge)),
            ('missing final action completion', (r for r in self.rows if not (r.get('kind') == 'event' and r.get('name') == 'actionComplete'
                 and r['value']['action'].get('station') == 'counter:dlc08_countertop_01_standard_circus@25.20,-20.40'))),
        ]
        for name, rows in cases:
            with self.subTest(name=name), self.assertRaises(ValueError): check(rows, self.plan, self.result)
        mismatched_result = dict(self.result, unrelatedResultField='must also bind the complete result')
        with self.subTest(name='complete result mismatch'), self.assertRaises(ValueError): check(iter(self.rows), self.plan, mismatched_result)


if __name__ == '__main__': unittest.main()
