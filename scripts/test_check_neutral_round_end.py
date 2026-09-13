import copy
import gzip
import json
import tempfile
import unittest
from pathlib import Path

import check_neutral_round_end as check


class NeutralRoundEndTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.samples = []
        with gzip.open('artifacts/native-round-end-b.jsonl.gz', 'rt', encoding='utf-8') as source:
            for line in source:
                row = json.loads(line)
                if row.get('kind') == 'call':
                    state = row['response']['state']
                    cls.samples.append({k: state[k] for k in check.STATE_FIELDS})
        cls.lifecycle = state['lifecycle']
        cls.events = state['gameEvents']
        cls.packets = row['response']['inputs']

    def test_actual_neutral_packets(self):
        check.neutral(self.packets)

    def test_non_neutral_and_duplicate_slots_rejected(self):
        for key, value in (('x', .01), ('pickup', True), ('player', 1)):
            packets = copy.deepcopy(self.packets); packets[0][key] = value
            with self.assertRaises(ValueError):
                check.neutral(packets)

    def test_actual_native_ledger(self):
        ledger = check.validate_ledger(self.events, self.samples[0], self.samples[-1])
        self.assertEqual(len(ledger), 5)
        self.assertEqual(sum(e['deductionDelta'] for e in ledger), 150)

    def test_event_delta_and_missing_history_rejected(self):
        wrong = copy.deepcopy(self.events); wrong[1]['deductionDelta'] = 0
        for events in (wrong, self.events[1:]):
            with self.assertRaises(ValueError):
                check.validate_ledger(events, self.samples[0], self.samples[-1])

    def test_final_score_without_complete_ledger_rejected(self):
        final = dict(self.samples[-1]); final['score'] = 5000
        with self.assertRaises(ValueError):
            check.validate_ledger(self.events, self.samples[0], final)

    def test_true_stop_requires_two_distinct_observed_frames(self):
        result = check.transitions(self.samples, self.lifecycle)
        self.assertEqual(result['observations']['serverInactive']['firstObservedGameplayFrame'], 16201)
        self.assertEqual(result['observations']['clientInactive']['firstObservedGameplayFrame'], 16202)
        self.assertTrue(result['observations']['bothInactive']['exactPerFrameObservation'])

    def test_missing_fine_observation_not_reported_as_exact(self):
        samples = [s for s in self.samples if s['gameplayFrame'] != 16201]
        result = check.transitions(samples, self.lifecycle)
        self.assertFalse(result['observations']['bothInactive']['exactPerFrameObservation'])

    def test_false_early_timer_zero_rejected_by_marker_interval(self):
        samples = copy.deepcopy(self.samples)
        # An inactive claim before the real native callback cannot be treated
        # as proof merely because the final snapshot is a valid outro.
        index = next(i for i, s in enumerate(samples) if s['gameplayFrame'] == 16200)
        samples[index]['serverRoundActive'] = False
        with self.assertRaises(ValueError):
            check.transitions(samples, self.lifecycle)

    def test_missing_native_callback_rejected(self):
        lifecycle = [e for e in self.lifecycle if not (e['source'] == 'ClientRound' and e['state'] == 'Inactive')]
        with self.assertRaises(ValueError):
            check.transitions(self.samples, lifecycle)

    def test_changed_route_request_rejected(self):
        requests = Path('routes/probes/native-round-end-fine.jsonl').read_text().splitlines()
        request = json.loads(requests[1]); request['steps'] = 599; requests[1] = json.dumps(request)
        with tempfile.TemporaryDirectory() as directory:
            route = Path(directory) / 'mutated.jsonl'; route.write_text('\n'.join(requests))
            with self.assertRaisesRegex(ValueError, 'Recorded requests differ'):
                check.validate_trace(Path('artifacts/native-round-end-b.jsonl.gz'), route,
                                     Path('artifacts/native-round-end-b-result.json'),
                                     check.json_file('artifacts/native-round-end-a-process-identity.json'))


if __name__ == '__main__':
    unittest.main()
