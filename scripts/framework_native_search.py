"""Evaluate short beam-search branches in the isolated game, using authoring rewind.

Start a fresh headless host and load Carnival first. Every branch restores one
observed base, then replays its recorded prefix without feedback. Actual inputs,
native food hierarchies, round ledgers and endpoint snapshots are retained.
"""
import argparse
from dataclasses import replace
import json
import math
from pathlib import Path
import struct
import time

from framework_rpc import Client, ControllerClient
from framework_search import Evaluation, SearchConfig, bounded_beam, generate_segments, identity, integer


class NativeSessionLost(BaseException):
    """Abort the entire search when its native checkpoint contract fails."""


def exact_values(a, b):
    """Exact numeric values, no tolerance; booleans are never numeric aliases."""
    if isinstance(a, bool) or isinstance(b, bool):
        return type(a) is type(b) and a == b
    if isinstance(a, (int, float)) and isinstance(b, (int, float)):
        return math.isfinite(a) and math.isfinite(b) and a == b
    if type(a) is not type(b): return False
    if isinstance(a, dict):
        return a.keys() == b.keys() and all(exact_values(a[k], b[k]) for k in a)
    if isinstance(a, list):
        return len(a) == len(b) and all(exact_values(x, y) for x, y in zip(a, b))
    return a == b


def gameplay_round(value):
    """Named projection; retain raw counters/history in every evidence receipt."""
    value = json.loads(json.dumps(value))
    rng = value.get('recipeRandom', {})
    rng.pop('authoringWarpCount', None)
    for key in ('history', 'nativeRecipes'):
        if key in rng:
            index = integer(rng['nextIndex'], 'native recipe cursor')
            if not 0 <= index <= len(rng[key]):
                raise ValueError('Native recipe history does not contain its current prefix')
            rng[key] = rng[key][:index]
    return value


def entities_by_id(state):
    entities = {integer(e['id'], 'entity id'): e for e in state['entities']}
    if len(entities) != len(state['entities']):
        raise ValueError('Duplicate observed entity IDs')
    return entities


def require_paused(state):
    if (state['state'] != 'Paused' or state['requestPending'] or state.get('errors') or
            state.get('invalidStateReason', '').startswith('AUTHORING_WARP_FAILED:')):
        raise ValueError('Expected settled paused controller: '+json.dumps(state))


def require_native_boundary(food):
    bridge = food['bridge']
    native = bridge['nativeRound']
    if (bridge['fullScreen'] or not bridge['paused'] or not bridge['loadComplete'] or
            not native['available'] or native['timerSuppressed']):
        raise ValueError('Expected windowed, paused, loaded, unsuppressed native round')
    if bridge.get('screenWidth') != 1280 or bridge.get('screenHeight') != 720:
        raise ValueError('Native window dimensions changed')
    return native


def native_clock_state(bridge):
    """Actual native private clocks plus the declared source; missing is not zero."""
    checkpoints = bridge['nativeCheckpoints']
    result = {}
    for name, size in (('nativeServerClock', 3), ('nativeClientClock', 6)):
        values = checkpoints.get(name)
        if not isinstance(values, list) or len(values) != size or any(type(v) not in (int, float) or not math.isfinite(v) for v in values):
            raise ValueError('Missing complete native clock state: ' + name)
        result[name] = values
    clock = bridge['logicalClock']
    result.update(source=bridge['logicalRealtime'], ticks=clock['eligibleTicks'], step=clock['step'])
    return result


def food_trees(food):
    entities = food['detail']['entities']
    if len({integer(e['id'], 'native food entity id') for e in entities}) != len(entities):
        raise ValueError('Duplicate native food entity IDs')
    return {e['id']: e['composition'] for e in entities if e.get('composition')}


def compare_boundary(expected_state, expected_native, expected_food, state, native, food):
    """No physics tolerance. Ignore only named authoring RNG audit fields."""
    a, b = entities_by_id(expected_state), entities_by_id(state)
    different = sorted(i for i in a.keys() | b.keys() if not exact_values(a.get(i), b.get(i)))
    return dict(frameEqual=expected_state['frame'] == state['frame'], changedEntityIds=different,
                nativeRoundEqual=exact_values(gameplay_round(expected_native), gameplay_round(native)),
                nativeFoodEqual=exact_values(expected_food, food),
                projection='Exact reconstructed entities and food trees; nativeRound removes only authoringWarpCount and recipe history beyond nextIndex')


def boundary_matches(comparison):
    return comparison['frameEqual'] and not comparison['changedEntityIds'] and comparison['nativeRoundEqual'] and comparison['nativeFoodEqual']


def recording_frames(recording):
    if recording['version'] != 1 or recording['kind'] != 'supercharged-logical-input-recording':
        raise ValueError('Unsupported native logical recording')
    payload = integer(recording['payloadFrames'], 'recorded payload frames')
    frames = recording['frames']
    if payload < 1 or recording['releaseFrames'] != 2 or len(frames) != payload + 2:
        raise ValueError('Recording must contain payload plus exactly two counted release frames')
    chef_ids = set(recording['initialControllerStates'])
    if len(chef_ids) != 4:
        raise ValueError('Recording requires four explicit chefs')
    for ordinal, row in enumerate(frames):
        if row['ordinal'] != ordinal or set(row['inputs']) != chef_ids:
            raise ValueError('Recording frame/chef order changed')
        for pad in row['inputs'].values():
            if ordinal >= payload and (pad['Pad']['X'] != 0 or pad['Pad']['Y'] != 0 or
                    any(pad[button]['Down'] or pad[button]['JustPressed'] for button in ('Pickup', 'Interact', 'Dash'))):
                raise ValueError('Recording release tail is not neutral')
            if ordinal == payload + 1 and any(pad[button]['JustReleased'] for button in ('Pickup', 'Interact', 'Dash')):
                raise ValueError('Final release barrier contains repeated release edges')
    terminal = recording.get('expectedTerminalGameState')
    has_terminal_counts = any(key in recording for key in ('terminalObservedFrames', 'terminalEmittedFrames'))
    if terminal is None:
        if has_terminal_counts:
            raise ValueError('Non-terminal recording contains a terminal receipt')
    else:
        if terminal != 'RunLevelOutro':
            raise ValueError('Unsupported recorded terminal game state')
        observed = integer(recording.get('terminalObservedFrames'), 'terminal observed frames')
        emitted = integer(recording.get('terminalEmittedFrames'), 'terminal emitted frames')
        if not 1 <= observed < len(frames) or emitted != observed or emitted > len(frames):
            raise ValueError('Terminal receipt must contain only the exactly consumed prefix inside the planned stream')
    return len(frames)


def require_recorded_completion(state, recording, start):
    require_paused(state)
    count = recording_frames(recording)
    raw = state['rawInput']
    if (raw['outcome'] != 'complete' or raw.get('active') or raw.get('error') or
            raw['recordingSha256'] != recording['sha256'] or raw['startFrame'] != start or
            raw['payloadFrames'] != recording['payloadFrames'] or raw['totalFramesIncludingRelease'] != count or
            raw['emittedFrames'] != count or raw['observedFrames'] != count or state['frame'] != start + count):
        raise ValueError('Native recording completion/frame/release receipt mismatch')
    return count


def require_recorded_terminal(state, recording, start):
    """Require the explicit pristine InLevel->RunLevelOutro latch receipt."""
    require_paused(state)
    count = recording_frames(recording)
    if recording.get('expectedTerminalGameState') != 'RunLevelOutro':
        raise ValueError('Recording does not declare the supported terminal')
    observed = integer(recording['terminalObservedFrames'], 'terminal observed frames')
    emitted = integer(recording['terminalEmittedFrames'], 'terminal emitted frames')
    raw = state['rawInput']
    if (raw['outcome'] != 'terminal' or raw.get('active') or raw.get('error') or
            raw['recordingSha256'] != recording['sha256'] or raw['startFrame'] != start or
            raw['payloadFrames'] != recording['payloadFrames'] or
            raw['totalFramesIncludingRelease'] != count or raw['emittedFrames'] != emitted or
            raw['observedFrames'] != observed or raw.get('expectedTerminalGameState') != 'RunLevelOutro' or
            raw.get('observedTerminalGameState') != 'RunLevelOutro' or
            raw.get('terminalFrame') != start + observed or
            raw.get('terminalObservedFrames') != observed or raw.get('terminalEmittedFrames') != emitted or
            state['frame'] != start + observed):
        raise ValueError('Native pristine terminal recording/frame/consumed-prefix receipt mismatch')
    return observed


def require_segment_recording(segment, recording):
    if recording_frames(recording) != segment.frames or recording['payloadFrames'] != segment.frames - 2:
        raise ValueError('Exported candidate duration differs from the proposed segment')
    def f32(value):
        return struct.unpack('f', struct.pack('f', value))[0]
    for expected, observed in zip(segment.input_frames(), recording['frames']):
        if set(expected) != set(observed['inputs']):
            raise ValueError('Exported candidate chefs differ from proposed inputs')
        for chef, pad in expected.items():
            wire = observed['inputs'][chef]
            if (wire['Pad']['X'] != f32(pad['x']) or wire['Pad']['Y'] != f32(pad['y']) or
                    wire['Pickup']['Down'] != pad['pickup'] or wire['Interact']['Down'] != pad['use'] or wire['Dash']['Down'] != pad['dash']):
                raise ValueError('Exported candidate input levels differ from the proposed segment')


class NativeEvaluator:
    def __init__(self, output, *, bridge=None, host=None):
        self.output = output
        output.mkdir(parents=True, exist_ok=False)
        self.bridge = self.host = None
        self.log = (output/'calls.jsonl').open('w')
        self.started = time.monotonic()
        self.counter = 0
        self.recordings = {identity([]): []}
        self.base = None
        self.initial = None
        self.aborted = False
        try:
            self.bridge = bridge if bridge is not None else Client(17636)
            self.host = host if host is not None else ControllerClient(17637)
        except BaseException:
            self.close()
            raise

    def call(self, target, request):
        result = (self.bridge if target == 'bridge' else self.host).call(request)
        self.log.write(json.dumps(dict(target=target, request=request, response=result,
                                       wallSeconds=time.monotonic()-self.started))+'\n')
        self.log.flush()
        return result

    def settled(self):
        deadline = time.monotonic()+30
        while True:
            result = self.host.call({'command': 'inspect'})
            if result['errors'] or result['state'] == 'Error':
                raise NativeSessionLost(json.dumps(result))
            if result['state'] == 'Paused' and not result['requestPending']:
                return self.call('controller', {'command': 'inspect', 'full': True})
            if time.monotonic() > deadline:
                raise NativeSessionLost('Native frame transition deadline exceeded: '+json.dumps(result))
            time.sleep(.025)

    def observe(self, state, count, recordings, evidence_id):
        require_paused(state)
        food = self.call('bridge', {'command': 'food'})
        native = require_native_boundary(food)
        tree = food_trees(food)
        result = Evaluation(state, native, count, checkpoint=recordings,
                            food_evidence=tree, evidence_id=evidence_id)
        (self.output/(evidence_id+'.json')).write_text(json.dumps(dict(state=state, native=food), indent=2))
        return result

    def initialize(self, warmup, render_fps):
        if not 2 <= integer(warmup, 'warmup') <= 36000:
            raise ValueError('Warmup must request2..36000 native frames')
        state = self.settled()
        start_frame = state['frame']
        if not state['freshLevelLoadObserved'] or not state['registryValidation']['layoutValid']:
            raise NativeSessionLost('A validated observed level baseline is required.')
        self.call('bridge', {'command': 'render', 'fps': render_fps})
        self.call('bridge', {'command': 'arm'})
        self.call('controller', {'command': 'step', 'frames': warmup})
        state = self.settled()
        if state['frame'] != start_frame + warmup:
            raise NativeSessionLost('Native warmup did not observe the requested frame interval')
        self.base = state['frame']
        self.call('controller', {'command': 'checkpoint', 'path': f'search-base-{self.base}.pb'})
        self.initial = self.observe(state, 0, [], 'base')
        return self.initial

    def restore_prefix(self, recordings, expected=None, evidence_id='restored-parent'):
        before_attempt = self.call('bridge', {'command': 'status'})['bridge']['nativeCheckpoints']['restoreAttempts']
        self.call('controller', {'command': 'warp', 'frame': self.base, 'development': True})
        state = self.settled()
        native = self.call('bridge', {'command': 'status'})['bridge']
        restored = native['nativeCheckpoints']['lastRestore']
        if (state['frame'] != self.base or not restored or not restored['verified'] or restored['frame'] != self.base or
                restored['attempt'] <= before_attempt):
            raise NativeSessionLost('Native checkpoint did not attest restoration of the requested base.')
        for recording in recordings:
            start = state['frame']
            self.call('controller', {'command': 'raw-replay', 'recording': recording})
            state = self.settled()
            require_recorded_completion(state, recording, start)
        if expected is not None:
            observed = self.observe(state, state['frame'] - self.base, recordings, evidence_id)
            compared = compare_boundary(expected.snapshot, expected.native_round, expected.food_evidence,
                                        observed.snapshot, observed.native_round, observed.food_evidence)
            (self.output/(evidence_id+'-comparison.json')).write_text(json.dumps(compared, indent=2))
            if not boundary_matches(compared):
                raise NativeSessionLost('Replayed parent differs from its recorded boundary: '+json.dumps(compared))
        return state

    def evaluate(self, parent, segment):
        if self.aborted:
            raise NativeSessionLost('Native evaluator was already aborted')
        try:
            return self._evaluate(parent, segment)
        except BaseException as error:
            self.aborted = True
            receipt = dict(candidate=self.counter, parent=parent.key, segment=segment.key,
                           inputs=segment.input_frames(), error=str(error))
            try:
                self.call('bridge', {'command': 'pause'})
                receipt['neutralPauseRequested'] = True
            except Exception as pause_error:
                receipt['neutralPauseError'] = str(pause_error)
            (self.output/f'candidate-{self.counter}-failed.json').write_text(json.dumps(receipt, indent=2))
            raise NativeSessionLost(str(error)) from error

    def _evaluate(self, parent, segment):
        if segment.trailing_neutral_frames != 2:
            raise ValueError('Native adapter requires two explicit, counted release frames.')
        self.counter += 1
        prefix = parent.evaluation.checkpoint
        before = self.restore_prefix(prefix, parent.evaluation, f'candidate-{self.counter}-parent')
        if before['frame'] != parent.evaluation.snapshot['frame']:
            raise NativeSessionLost('Recorded parent prefix reached a different frame.')
        pads = {str(p.chef): dict(x=p.x, y=p.y, pickup=p.pickup, interact=p.use, dash=p.dash) for p in segment.pads}
        began = time.monotonic()
        self.call('controller', {'command': 'raw-input', 'segments': [dict(frames=segment.frames-2, chefs=pads)]})
        state = self.settled()
        if state['rawInput']['outcome'] != 'complete':
            raise NativeSessionLost('Candidate input stream interrupted: '+json.dumps(state['rawInput']))
        exported = self.call('controller', {'command': 'record-input',
                                          'path': f'search-{self.base}/candidate-{self.counter}.json'})
        recording = json.loads(Path(exported['path']).read_text())
        require_recorded_completion(state, recording, before['frame'])
        require_segment_recording(segment, recording)
        records = [*prefix, recording]
        key = identity([s.key for s in (*parent.segments, segment)])
        self.recordings[key] = records
        (self.output/f'candidate-{self.counter}-input.json').write_text(json.dumps(recording, indent=2))
        result = self.observe(state, state['frame']-before['frame'], records, f'candidate-{self.counter}')
        result.timings = {'candidateWallSeconds': time.monotonic()-began, 'includesTwoReleaseFrames': True}
        return result

    def close(self):
        try:
            if self.bridge is not None:
                self.call('bridge', {'command': 'pause'})
        finally:
            try:
                if self.bridge is not None: self.bridge.close()
            finally:
                try:
                    if self.host is not None: self.host.close()
                finally: self.log.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--warmup', type=int, default=30)
    parser.add_argument('--seed', type=int, default=0)
    parser.add_argument('--evaluations', type=int, default=16)
    parser.add_argument('--depth', type=int, default=2)
    parser.add_argument('--beam', type=int, default=4)
    parser.add_argument('--render-fps', type=int, default=60)
    args = parser.parse_args()
    runtime = NativeEvaluator(args.out)
    report = {'qualification': 'Incomplete authoring experiment; not a fresh-start score proof.'}
    try:
        initial = runtime.initialize(args.warmup, args.render_fps)
        def proposals(parent, seed, limit):
            chefs = sorted(e['id'] for e in parent.evaluation.snapshot['entities'] if e['chef'] is not None)
            return [replace(segment, frames=segment.frames+2, trailing_neutral_frames=2)
                    for segment in generate_segments(chefs, seed=seed, limit=limit)]
        report = bounded_beam(initial, runtime.evaluate, proposals, config=SearchConfig(
            seed=args.seed, max_evaluations=args.evaluations, max_depth=args.depth, beam_width=args.beam))
        records = runtime.recordings[report['best']['key']]
        (args.out/'selected-inputs.json').write_text(json.dumps(records, indent=2))
        # Re-emit the selected exact prefix once; preserve its observed result.
        # Compare the selected replay to its own retained endpoint, not merely
        # an input hash or frame number. Empty selection compares the base.
        best = json.loads((args.out/(report['best']['evidenceId']+'.json')).read_text())
        expected = Evaluation(best['state'], best['native']['bridge']['nativeRound'], 0,
                              food_evidence=food_trees(best['native']))
        state = runtime.restore_prefix(records, expected, 'selected-restored')
        runtime.observe(state, state['frame']-runtime.base, records, 'selected-replay')
        report['selectedReplayCompleted'] = True
    except (Exception, NativeSessionLost) as error:
        report['error'] = str(error)
    finally:
        try: runtime.close()
        except Exception as error:
            report['closeError'] = str(error)
            report['selectedReplayCompleted'] = False
        (args.out/'report.json').write_text(json.dumps(report, indent=2))
        print(json.dumps({k: report[k] for k in ('qualification', 'evaluations', 'selectedReplayCompleted', 'error') if k in report}, indent=2))
    return 0 if report.get('selectedReplayCompleted') else 1


if __name__ == '__main__':
    raise SystemExit(main())
