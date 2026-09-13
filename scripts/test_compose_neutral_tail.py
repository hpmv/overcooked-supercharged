import copy
import gzip
import json
import tempfile
import unittest
from pathlib import Path
from compose_neutral_tail import boundary, compose, neutral
from extract_inputs import entries


class JoinTests(unittest.TestCase):
    def setUp(self):
        self.a = {'inputs': [dict(player=p, x=0, y=0, pickup=False, use=False, dash=False) for p in range(4)],
                  'state': {'gameplayFrame': 100, 'timer': 250., 'gameEvents': [], 'randomState': 'abc', 'entities': [{'x': 1.}]}}
        self.b = copy.deepcopy(self.a)

    def test_identical_join(self):
        self.assertTrue(boundary(self.a, self.b)['allOtherStateFieldsIdentical'])

    def test_unknown_state_change_rejected(self):
        self.b['state']['newField'] = True
        with self.assertRaises(ValueError): boundary(self.a, self.b)

    def test_clock_rng_and_small_physics_change_rejected(self):
        for key, value in [('timer', 249.99), ('randomState', 'abd'), ('entities', [{'x': 1.000001}])]:
            b = copy.deepcopy(self.b); b['state'][key] = value
            with self.assertRaises(ValueError): boundary(self.a, b)

    def test_neutral_required(self):
        self.b['inputs'][3]['pickup'] = True
        with self.assertRaises(ValueError): boundary(self.a, self.b)

    def test_four_distinct_players(self):
        self.b['inputs'][3]['player'] = 2
        self.assertFalse(neutral(self.b['inputs']))

    def test_only_inert_disconnect_addition(self):
        event = dict(kind='input_release', reason='controller-disconnected', inputsNeutral=True, gameplayFrame=100,
                     scoreApplied=False, scoreDelta=0, baseScoreDelta=0, tipDelta=0, deductionDelta=0, deliveryDelta=0)
        self.b['state']['gameEvents'] = [event]
        self.assertEqual(len(boundary(self.a, self.b)['addedDisconnectObservations']), 1)
        for key, value in [('scoreDelta', 1), ('kind', 'delivery'), ('inputsNeutral', False), ('gameplayFrame', 101)]:
            b = copy.deepcopy(self.b); b['state']['gameEvents'][0][key] = value
            with self.assertRaises(ValueError): boundary(self.a, b)

    def test_existing_history_retained(self):
        self.a['state']['gameEvents'] = [{'kind': 'delivery'}]
        with self.assertRaises(ValueError): boundary(self.a, self.b)


class CompositionTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        self.paths = [root / name for name in ('prefix.jsonl.gz', 'tail.jsonl.gz', 'movie.jsonl.gz', 'combined.jsonl.gz', 'manifest.json')]
        self.pads = [dict(player=p, x=0, y=0, pickup=False, use=False, dash=False) for p in range(4)]
        def call(command, frame, ended=False):
            request = dict(version=1, command=command)
            if command == 'step': request.update(steps=1, inputs=copy.deepcopy(self.pads))
            return dict(kind='call', request=request, response=dict(ok=True, inputs=copy.deepcopy(self.pads),
                state=dict(gameplayFrame=frame, timer=0 if ended else 3,
                    serverRoundActive=not ended, clientRoundActive=not ended, gameEvents=[])))
        self.prefix = [call('restart', 0), call('step', 1)]
        self.tail = [call('inspect', 1), call('step', 2, True)]

    def run_compose(self):
        for path, rows in zip(self.paths, (self.prefix, self.tail)):
            path.write_bytes(gzip.compress(('\n'.join(json.dumps(r) for r in rows) + '\n').encode()))
        return compose(*self.paths)

    def test_all_requests_and_original_observation_bytes_retained(self):
        report = self.run_compose()
        self.assertEqual([r for _, r in entries(self.paths[2])], [r['request'] for r in self.prefix + self.tail])
        self.assertEqual(self.paths[3].read_bytes(), self.paths[0].read_bytes() + self.paths[1].read_bytes())
        self.assertEqual((report['requests'], report['steppedFrames']), (4, 2))

    def test_non_neutral_tail_leaves_originals_and_no_outputs(self):
        self.tail[1]['request']['inputs'][1]['use'] = True
        with self.assertRaises(ValueError): self.run_compose()
        self.assertTrue(all(p.exists() for p in self.paths[:2]))
        self.assertFalse(any(p.exists() for p in self.paths[2:]))

    def test_missing_native_frame_rejected(self):
        self.tail[1]['response']['state']['gameplayFrame'] = 3
        with self.assertRaises(ValueError): self.run_compose()

    def test_unfinished_round_not_promoted(self):
        self.tail[1]['response']['state']['clientRoundActive'] = True
        with self.assertRaises(ValueError): self.run_compose()

    def test_output_collision_retains_existing_file(self):
        self.paths[2].write_bytes(b'existing')
        with self.assertRaises(ValueError): self.run_compose()
        self.assertEqual(self.paths[2].read_bytes(), b'existing')


if __name__ == '__main__':
    unittest.main()
