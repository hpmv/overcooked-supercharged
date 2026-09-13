import copy
import gzip
import json
import pathlib
import unittest

from check_idle_traffic_probe import analyze


class IdleTrafficProofTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        with gzip.open('artifacts/v9-idle-traffic-continuation-a.jsonl.gz', 'rt', encoding='utf-8') as stream:
            cls.rows = [json.loads(line) for line in stream]
        load = lambda p: json.loads(pathlib.Path(p).read_text(encoding='utf-8-sig'))
        cls.result = load('artifacts/v9-idle-traffic-continuation-a-result.json')
        cls.before = load('artifacts/native-round-v9-failure-state.json')
        cls.plan = load('routes/probes/v9-idle-traffic-continuation.json')
        cls.calls = [r for r in cls.rows if r.get('kind') == 'call']

    def run_proof(self):
        return analyze(self.rows, self.result, self.before, self.plan)

    def test_native_trace_passes(self):
        self.assertEqual(self.run_proof()['advancingFrames'], 63)

    def test_non_input_command_rejected(self):
        request = self.calls[1]['request']
        old = request['command']
        try:
            request['command'] = 'restart'
            with self.assertRaises(ValueError):
                self.run_proof()
        finally:
            request['command'] = old

    def test_unrelated_chef_input_rejected(self):
        control = next(i for i in self.calls[1]['request']['inputs'] if i['player'] == 1)
        try:
            control['x'] = 1
            with self.assertRaises(ValueError):
                self.run_proof()
        finally:
            control['x'] = 0

    def test_stack_identity_reuse_rejected(self):
        stack = next(e for e in self.calls[-1]['response']['state']['entities'] if e['id'] == 314)
        old = stack['observedOrdinal']
        try:
            stack['observedOrdinal'] = old + 1
            with self.assertRaises(ValueError):
                self.run_proof()
        finally:
            stack['observedOrdinal'] = old

    def test_clock_freeze_rejected(self):
        state = self.calls[1]['response']['state']
        old = state['timer']
        try:
            state['timer'] = self.calls[0]['response']['state']['timer']
            with self.assertRaises(ValueError):
                self.run_proof()
        finally:
            state['timer'] = old


if __name__ == '__main__':
    unittest.main()
