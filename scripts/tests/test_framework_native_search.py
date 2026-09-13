"""Offline protocol fixtures only: generated scores/positions are not native evidence."""
import copy
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
import framework_search as fs
import framework_native_search as ns
import framework_probe as fp
import framework_input_probe as ip


def segment(x=1, frames=8):
    return fs.Segment(frames, (fs.Pad(103, x=x, use=True), fs.Pad(104), fs.Pad(105), fs.Pad(106)), trailing_neutral_frames=2)


def recording(payload, pads):
    prior = {chef: dict(pickup=False, interact=False, dash=False) for chef in pads}
    rows = []
    for ordinal in range(payload + 2):
        inputs = {}
        for chef, requested in pads.items():
            p = requested if ordinal < payload else dict(x=0, y=0, pickup=False, interact=False, dash=False)
            f32 = lambda x: struct.unpack('f', struct.pack('f', x))[0]
            value = {'Pad': {'X': f32(p['x']), 'Y': f32(p['y'])}}
            for lower, upper in [('pickup', 'Pickup'), ('interact', 'Interact'), ('dash', 'Dash')]:
                value[upper] = dict(Down=p[lower], JustPressed=p[lower] and not prior[chef][lower],
                                    JustReleased=not p[lower] and prior[chef][lower])
                prior[chef][lower] = p[lower]
            inputs[chef] = value
        rows.append(dict(ordinal=ordinal, inputs=inputs))
    r = dict(version=1, kind='supercharged-logical-input-recording', payloadFrames=payload, releaseFrames=2,
             initialControllerStates={chef: 'synthetic-test-only' for chef in pads}, frames=rows)
    r['sha256'] = fs.identity(r)
    return r


class FakeProtocol:
    """Synchronous stand-in for observed RPC responses, never opens a socket."""
    def __init__(self, root):
        self.root, self.frame, self.base, self.x, self.attempt = root, 10, 10, 0.0, 0
        self.calls, self.closed = [], []
        self.raw = dict(outcome='none', active=False, error=None)
        self.recording = None
        self.fail_raw = self.drift = self.stale_restore = self.wrong_frames = self.bad_export = self.fail_pause = False

    def state(self):
        return dict(state='Paused', requestPending=False, errors=[], frame=self.frame, invalidStateReason='',
                    freshLevelLoadObserved=True, registryValidation={'layoutValid': True}, rawInput=copy.deepcopy(self.raw),
                    entities=[dict(id=i, exists=True, className='chef', chef={}, data={},
                                   position={'x': self.x if i == 103 else 0, 'y': 0, 'z': 0}) for i in range(103, 107)])

    def native(self):
        return dict(available=True, elapsed=self.frame / 60, remaining=270-self.frame/60, timerSuppressed=False,
                    ledger=dict(total=0, deliveries=0, deductions=0), orders=[dict(id=1, recipeId=296560, remaining=100)],
                    recipeRandom=dict(nextIndex=1, authoringWarpCount=self.attempt, history=[6], nativeRecipes=[{'recipeId': 296560}]))

    def bridge(self):
        return dict(paused=True, fullScreen=False, screenWidth=1280, screenHeight=720, loadComplete=True, nativeRound=self.native(),
                    applicationFocused=False, unfocusedVirtualInputChecks=4,
                    nativeCheckpoints=dict(restoreAttempts=self.attempt, lastRestore=dict(verified=True, frame=self.base, attempt=self.attempt)))

    def client(self, kind):
        protocol = self
        class Client:
            def call(self, request): return protocol.call(kind, request)
            def close(self): protocol.closed.append(kind)
        return Client()

    def execute(self, r):
        start = self.frame
        self.x += sum(row['inputs']['103']['Pad']['X'] * .1 for row in r['frames'])
        self.frame += len(r['frames']) + int(self.wrong_frames)
        self.raw = dict(outcome='complete', active=False, error=None, startFrame=start, payloadFrames=r['payloadFrames'],
                        totalFramesIncludingRelease=len(r['frames']), emittedFrames=len(r['frames']),
                        observedFrames=len(r['frames']), recordingSha256=r['sha256'])

    def call(self, kind, request):
        self.calls.append((kind, copy.deepcopy(request)))
        command = request['command']
        if kind == 'bridge':
            if command == 'pause' and self.fail_pause: raise RuntimeError('synthetic pause transport failure')
            result = {'bridge': self.bridge()}
            if command == 'food': result['detail'] = {'entities': []}
            return result
        if command == 'warp':
            self.frame, self.x = self.base, .001 if self.drift else 0
            if not self.stale_restore: self.attempt += 1
        elif command == 'raw-input':
            if self.fail_raw: raise RuntimeError('synthetic uncertain input transport failure')
            s = request['segments'][0]
            self.recording = recording(s['frames'], s['chefs'])
            self.execute(self.recording)
        elif command == 'raw-replay':
            self.execute(request['recording'])
        elif command == 'step': self.frame += request['frames']
        elif command == 'checkpoint': self.base = self.frame
        elif command == 'record-input':
            path = self.root / 'export.json'
            r = copy.deepcopy(self.recording)
            if self.bad_export: r['frames'][0]['inputs']['103']['Pad']['X'] = -99
            path.write_text(json.dumps(r))
            return {'path': str(path)}
        return self.state()


class AdapterTests(unittest.TestCase):
    def runtime(self, root):
        protocol = FakeProtocol(root)
        runtime = ns.NativeEvaluator(root/'evidence', bridge=protocol.client('bridge'), host=protocol.client('host'))
        runtime.base = protocol.base
        initial = runtime.observe(protocol.state(), 0, [], 'base')
        return protocol, runtime, fs.Node(initial, fs.assess(initial.snapshot, initial.native_round))

    def test_exact_parent_then_actual_export_and_two_release_frames(self):
        with tempfile.TemporaryDirectory() as temp:
            p, r, parent = self.runtime(Path(temp))
            result = r.evaluate(parent, segment(.70710678))
            self.assertEqual(result.frames_executed, 8)
            self.assertEqual(result.snapshot['frame'], 18)
            raw = next(q for target, q in p.calls if q['command'] == 'raw-input')
            self.assertEqual(raw['segments'][0]['frames'], 6)
            self.assertEqual(result.checkpoint[0]['releaseFrames'], 2)
            self.assertEqual(ns.require_recorded_completion(result.snapshot, result.checkpoint[0], 10), 8)
            self.assertTrue(result.timings['includesTwoReleaseFrames'])
            child = fs.Node(result, fs.assess(result.snapshot, result.native_round), (segment(.70710678),))
            r.restore_prefix(result.checkpoint, result, 'child-verified')
            self.assertEqual(p.frame, 18)
            self.assertTrue(ns.boundary_matches(json.loads((r.output/'child-verified-comparison.json').read_text())))
            r.close()
            self.assertEqual(p.closed, ['bridge', 'host'])

    def test_parent_physics_drift_aborts_before_candidate_even_at_same_frame(self):
        with tempfile.TemporaryDirectory() as temp:
            p, r, parent = self.runtime(Path(temp)); p.drift = True
            with self.assertRaises(ns.NativeSessionLost): r.evaluate(parent, segment())
            self.assertFalse(any(q['command'] == 'raw-input' for _, q in p.calls))
            self.assertEqual(p.calls[-1][1]['command'], 'pause')
            self.assertEqual(json.loads((r.output/'candidate-1-parent-comparison.json').read_text())['changedEntityIds'], [103])
            calls = len(p.calls)
            with self.assertRaises(ns.NativeSessionLost): r.evaluate(parent, segment())
            self.assertEqual(len(p.calls), calls)
            r.close()

    def test_uncertain_transport_failure_cannot_be_swallowed_by_beam(self):
        with tempfile.TemporaryDirectory() as temp:
            p, r, parent = self.runtime(Path(temp)); p.fail_raw = True
            with self.assertRaises(ns.NativeSessionLost):
                fs.bounded_beam(parent.evaluation, r.evaluate, lambda *_: [segment(), segment(-1)])
            self.assertEqual(sum(q['command'] == 'raw-input' for _, q in p.calls), 1)
            failure = json.loads((r.output/'candidate-1-failed.json').read_text())
            self.assertTrue(failure['neutralPauseRequested'])
            self.assertEqual(len(failure['inputs']), 8)
            r.close()

    def test_stale_restore_wrong_frame_or_changed_export_aborts(self):
        for flag in ('stale_restore', 'wrong_frames', 'bad_export'):
            with self.subTest(flag=flag), tempfile.TemporaryDirectory() as temp:
                p, r, parent = self.runtime(Path(temp)); setattr(p, flag, True)
                with self.assertRaises(ns.NativeSessionLost): r.evaluate(parent, segment())
                self.assertTrue(r.aborted)
                self.assertEqual(p.calls[-1][1]['command'], 'pause')
                r.close()

    def test_cleanup_closes_both_clients_after_pause_failure(self):
        with tempfile.TemporaryDirectory() as temp:
            p, r, _ = self.runtime(Path(temp)); p.fail_pause = True
            with self.assertRaises(RuntimeError): r.close()
            self.assertEqual(p.closed, ['bridge', 'host'])
            self.assertTrue(r.log.closed)

    def test_parent_native_food_score_timer_and_bool_types_remain_exact(self):
        p = FakeProtocol(Path('.')); state, native, food = p.state(), p.native(), {2: {'state': 'Raw'}}
        changed = copy.deepcopy(native); changed['recipeRandom']['authoringWarpCount'] += 4
        changed['recipeRandom']['history'].append(2); changed['recipeRandom']['nativeRecipes'].append({'recipeId': 158500})
        self.assertTrue(ns.boundary_matches(ns.compare_boundary(state, native, food, state, changed, food)))
        self.assertEqual(len(changed['recipeRandom']['history']), 2)  # projection never edits evidence
        for mutate in (lambda r: r['ledger'].update(total=1), lambda r: r.update(elapsed=1), lambda r: r.update(available=1)):
            changed = copy.deepcopy(native); mutate(changed)
            self.assertFalse(ns.boundary_matches(ns.compare_boundary(state, native, food, state, changed, food)))
        self.assertFalse(ns.boundary_matches(ns.compare_boundary(state, native, food, state, native, {2: {'state': 'Cooked'}})))

    def test_actual_f_native_food_schema_and_input_recording(self):
        food_path = ROOT/'artifacts/framework-migration/native-f/native-food.json'
        if not food_path.exists(): self.skipTest('Local immutable native-f evidence unavailable')
        food = json.loads(food_path.read_text())
        self.assertEqual(ns.require_native_boundary(food)['ledger']['total'], 0)
        trees = ns.food_trees(food)
        self.assertEqual(trees[2]['type'], 'CookedCompositeAssembledNode')
        self.assertEqual(trees[3]['state'], 'Unmixed')
        rec = json.loads((ROOT/'artifacts/framework-migration/native-f/input-probe/recording.json').read_text())
        self.assertEqual(ns.recording_frames(rec), 10)
        bad = copy.deepcopy(rec); bad['frames'][-1]['inputs']['103']['Pickup']['JustReleased'] = True
        with self.assertRaises(ValueError): ns.recording_frames(bad)

    def test_release_receipt_rejects_hash_or_observation_count_or_missing_release(self):
        p = FakeProtocol(Path('.'))
        pads = {str(c): dict(x=0, y=0, pickup=False, interact=False, dash=False) for c in range(103, 107)}
        rec = recording(6, pads); p.execute(rec)
        for key, value in [('observedFrames', 6), ('recordingSha256', 'stale'), ('startFrame', 11)]:
            s = p.state(); s['rawInput'][key] = value
            with self.assertRaises(ValueError): ns.require_recorded_completion(s, rec, 10)
        for key, value in [('releaseFrames', 0), ('payloadFrames', 8)]:
            r = copy.deepcopy(rec); r[key] = value
            with self.assertRaises(ValueError): ns.recording_frames(r)

    def test_neutral_tail_is_part_of_segment_identity_budget_and_reservations(self):
        s = segment()
        self.assertEqual(len(s.input_frames()), 8)
        self.assertTrue(s.input_frames()[5]['103']['use'])
        self.assertEqual(s.input_frames()[6]['103']['x'], 0)
        self.assertFalse(s.input_frames()[7]['103']['use'])
        self.assertNotEqual(s.key, fs.Segment(8, s.pads).key)
        self.assertEqual(fs.reserve([], s, 10)[0].end, 18)
        for value in (8, -1, True):
            with self.assertRaises(ValueError): fs.Segment(8, s.pads, trailing_neutral_frames=value)

    def test_optional_native_food_branch_and_duplicate_food_entities(self):
        node = dict(type='CookedCompositeAssembledNode', state='Cooked', children=[],
                    optional=[dict(type='IngredientAssembledNode', id=fs.ONION)])
        self.assertEqual(fs.food_facts(node)[0], {fs.ONION})
        with self.assertRaises(ValueError): ns.food_trees({'detail': {'entities': [{'id': 2}, {'id': 2}]}})

    def test_zero_repeat_rejected_before_any_protocol_open(self):
        with patch.object(sys, 'argv', ['framework_probe.py', '--out', 'unused', '--repeat', '0']), patch.object(fp, 'Client') as client:
            with self.assertRaises(SystemExit): fp.main()
            client.assert_not_called()

    def test_probe_mains_count_actual_payload_release_and_step_frames(self):
        for module, options in [(fp, ['--warmup', '2', '--frames', '4']), (ip, ['--frames', '6'])]:
            with self.subTest(module=module.__name__), tempfile.TemporaryDirectory() as temp:
                root = Path(temp); p = FakeProtocol(root)
                with patch.object(sys, 'argv', [module.__name__, '--out', str(root/'out'), *options]), \
                        patch.object(module, 'Client', return_value=p.client('bridge')), \
                        patch.object(module, 'ControllerClient', return_value=p.client('host')):
                    self.assertEqual(module.main(), 0)
                summary = json.loads((root/'out/summary.json').read_text())
                self.assertTrue(summary['passed'])
                if module is fp: self.assertEqual(summary['replays'][0]['observedAdvancingFrames'], 4)
                else:
                    self.assertEqual(summary['observedFramesIncludingRelease'], 8)
                    self.assertEqual(summary['releaseFrames'], 2)
                self.assertEqual(p.closed, ['bridge', 'host'])

    def test_second_connection_failure_releases_first_connection(self):
        for module in (fp, ip):
            with self.subTest(module=module.__name__), tempfile.TemporaryDirectory() as temp:
                root = Path(temp); p = FakeProtocol(root)
                with patch.object(sys, 'argv', [module.__name__, '--out', str(root/'out')]), \
                        patch.object(module, 'Client', return_value=p.client('bridge')), \
                        patch.object(module, 'ControllerClient', side_effect=ConnectionError('synthetic host unavailable')):
                    self.assertEqual(module.main(), 1)
                self.assertEqual(p.calls[-1][1]['command'], 'pause')
                self.assertEqual(p.closed, ['bridge'])

    def test_search_main_rechecks_selected_endpoint_and_counts_all_frames(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); p = FakeProtocol(root)
            with patch.object(sys, 'argv', ['framework_native_search.py', '--out', str(root/'out'), '--warmup', '2',
                                           '--evaluations', '2', '--depth', '1', '--beam', '1']), \
                    patch.object(ns, 'Client', return_value=p.client('bridge')), \
                    patch.object(ns, 'ControllerClient', return_value=p.client('host')):
                self.assertEqual(ns.main(), 0)
            report = json.loads((root/'out/report.json').read_text())
            self.assertTrue(report['selectedReplayCompleted'])
            self.assertEqual(report['evaluations'], 2)
            self.assertTrue(ns.boundary_matches(json.loads((root/'out/selected-restored-comparison.json').read_text())))
            for outcome in report['outcomes']:
                self.assertEqual(outcome['totalFrames'], outcome['segment']['frames'])
                self.assertEqual(outcome['segment']['trailing_neutral_frames'], 2)
            self.assertEqual(p.closed, ['bridge', 'host'])


if __name__ == '__main__': unittest.main()
