"""Captured observations test checker guards; synthetic log events are not native execution evidence."""
import copy
import gzip
import json
from pathlib import Path
import tempfile
import unittest

from check_far_pot_interception import registration
from check_stationary_transfer_comparison import TransferAudit, inspect_run, route_pair

ROOT = Path(__file__).resolve().parents[1]


class StationaryComparisonTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.before = json.loads((ROOT/'artifacts/stationary-transfer-b-gf437.json').read_text())
        cls.confirmed = json.loads((ROOT/'artifacts/stationary-transfer-b-gf438.json').read_text())
        cls.a = json.loads((ROOT/'routes/probes/stationary-transfer-baseline.json').read_text())
        cls.b = json.loads((ROOT/'routes/probes/stationary-transfer-shortcut.json').read_text())

    def pending(self):
        audit = TransferAudit(self.b, True); before = copy.deepcopy(self.before); state = before['state']
        audit.call({'command': 'restart'}, before)
        audit.event('jobStart', {'id': 'PH05-plated-native-assembly', 'player': 0})
        audit.event('actionCreated', {'type': 'take', 'station': '38', 'player': 0, 'stationaryTargetTransfer': True})
        c = next(c for c in state['chefs'] if c['playerId'] == 0)
        audit.event('stationaryTransferNeutralConfirmation', {'player': 0, 'stationId': 38, 'targetId': 38,
                    'field': 'pickupTargetId', 'frame': state['frame'], 'gameplayFrame': 437, 'heldId': 0,
                    'position': c['position'], 'actualVelocity': c['velocity'], 'cachedVelocity': c['lastVelocity']})
        return audit

    def edge(self, audit):
        before = audit.candidates[0]['response']['state']; state = audit.last['state']
        entities = {e['id']: e for e in state['entities']}
        event = {'player': 0, 'stationId': 38, 'targetId': 38, 'candidateFrame': before['frame'],
                 'frame': state['frame'], 'heldId': 0, 'expectedTakeId': 10,
                 'nativeIncarnations': [{'id': eid, 'ordinal': entities[eid]['observedOrdinal'],
                                        'registration': registration(state, eid)['sequence']} for eid in (10, 38)]}
        audit.event('stationaryTransferEdge', event)
        audit.event('interactionTargetVerified', {'player': 0, 'field': 'pickupTargetId', 'targetId': 38,
                    'resolvedStationId': 38, 'heldBefore': 0})

    def confirm(self, audit, mutation=None):
        response = copy.deepcopy(self.confirmed)
        if mutation: mutation(response)
        audit.call({'command': 'step', 'inputs': response['inputs']}, response)

    def test_captured_neutral_confirmation_and_edge_event(self):
        audit = self.pending(); self.confirm(audit); self.edge(audit)
        self.assertTrue(audit.current[0]['shortcut'])
        self.assertEqual(audit.pending_edges[0]['decisionFrame'], 438)
        self.assertEqual(len(route_pair(self.a, self.b)), 13)

    def test_captured_guard_mutations(self):
        def c(r): return next(x for x in r['state']['chefs'] if x['playerId'] == 0)
        def e(r): return next(x for x in r['state']['entities'] if x['id'] == 10)
        cases = {
            'moving rigidbody': lambda r: c(r)['velocity'].__setitem__('x', .01),
            'moving cached velocity': lambda r: c(r)['lastVelocity'].__setitem__('z', .01),
            'different native target': lambda r: c(r).__setitem__('pickupTargetId', 39),
            'reused source ordinal': lambda r: e(r).__setitem__('observedOrdinal', 1000),
            'changed source material': lambda r: e(r).__setitem__('ingredientIds', [262914]),
            'changed stationary pose': lambda r: c(r)['position'].__setitem__('x', c(r)['position']['x']+.002),
            'native input suppression': lambda r: c(r).__setitem__('inputSuppressed', True),
            'missing neutral frame': lambda r: r['state'].__setitem__('frame', r['state']['frame']+1),
        }
        for name, mutate in cases.items():
            with self.subTest(name=name), self.assertRaises(ValueError):
                audit = self.pending(); self.confirm(audit, mutate); self.edge(audit)

    def test_missing_neutral_or_native_pickup_receipt(self):
        audit = self.pending()
        with self.assertRaises(ValueError):
            self.confirm(audit, lambda r: r['inputs'][0].__setitem__('pickup', True))
        audit = self.pending(); self.confirm(audit); self.edge(audit)
        response = copy.deepcopy(self.confirmed); response['state']['gameplayFrame'] = 439
        with self.assertRaises(ValueError): audit.call({'command': 'step', 'inputs': response['inputs']}, response)

    def test_only_thirteen_route_flags(self):
        for change in [lambda b: b['jobs'][0]['actions'][0].__setitem__('station', 'Onion'),
                       lambda b: b['jobs'][0]['actions'][1].__setitem__('stationaryTargetTransfer', True),
                       lambda b: b['jobs'][0]['actions'][0].pop('stationaryTargetTransfer')]:
            b = copy.deepcopy(self.b); change(b)
            with self.assertRaises(ValueError): route_pair(self.a, b)

    def test_failed_attempt_keeps_full_neutral_diagnostic(self):
        # An explicitly synthetic invalid-start/failure trace checks the archival
        # path; it is never labeled a completed native mechanism.
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); trace = root/'failed.jsonl.gz'; plan = root/'plan.json'
            result = root/'result.json'; stderr = root/'stderr.txt'; archive = root/'failure.json'
            response = copy.deepcopy(self.confirmed)
            request = {'version': 1, 'command': 'step', 'steps': 1, 'inputs': response['inputs']}
            rows = [{'kind': 'header', 'format': 'overcooked-tas-trace', 'version': 1, 'mode': 'plan'},
                    {'kind': 'call', 'request': request, 'response': response},
                    {'kind': 'event', 'name': 'planFailure', 'value': {'error': 'synthetic captured mutation'}}]
            with gzip.open(trace, 'wt') as stream:
                for row in rows: stream.write(json.dumps(row)+'\n')
            plan.write_text(json.dumps(self.b)); result.write_text(json.dumps(response)); stderr.write_text('synthetic failure\n')
            report, _ = inspect_run(trace, plan, result, stderr, True, archive)
            self.assertFalse(report['passed']); self.assertTrue(report['exactFullResultBinding'])
            self.assertTrue(report['lastInputsNeutral']); self.assertTrue(report['lastRequestedInputsNeutral'])
            saved = json.loads(archive.read_text())
            self.assertEqual(saved['lastFullNativeResponse'], response)
            self.assertEqual(saved['firstNativeRouteFailure']['value']['error'], 'synthetic captured mutation')


if __name__ == '__main__': unittest.main()
