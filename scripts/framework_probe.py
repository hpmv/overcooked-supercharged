"""Measure a bounded native checkpoint/replay probe on an already loaded kitchen.

All native state restoration is explicit authoring. This is not score validation.
The evidence preserves every bridge observation and full controller endpoint.
"""
import argparse
import hashlib
import json
from pathlib import Path
import time

from framework_rpc import Client, ControllerClient
from framework_native_search import exact_values, gameplay_round, require_native_boundary, require_paused, native_clock_state
from framework_pause_boundary import observe_settled_pause, PauseBoundaryError


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--warmup', type=int, default=180)
    parser.add_argument('--frames', type=int, default=120)
    parser.add_argument('--repeat', type=int, default=1)
    parser.add_argument('--render-fps', type=int, default=60)
    args = parser.parse_args()
    if not 2 <= args.warmup <= 36000 or not 2 <= args.frames <= 36000 or not 1 <= args.repeat <= 100:
        parser.error('Use2..36000 warmup/continuation frames and1..100 replays.')
    args.out.mkdir(parents=True, exist_ok=False)
    evidence_key = hashlib.sha256(str(args.out.resolve()).encode('utf-8')).hexdigest()[:16]
    bridge = controller = None
    records, summary = [], {'classification': 'authoring checkpoint probe', 'passed': False}
    start = time.monotonic()

    def call(target, request, label):
        result = (bridge if target == 'bridge' else controller).call(request)
        records.append({'label': label, 'target': target, 'request': request,
                        'wallSeconds': time.monotonic() - start, 'response': result})
        return result

    def settled(label):
        deadline = time.monotonic() + 20
        while True:
            state = controller.call({'command': 'status'})
            if state.get('errors'):
                raise RuntimeError(json.dumps(state['errors']))
            if state.get('state') == 'Error' or state.get('invalidStateReason', '').startswith('AUTHORING_WARP_FAILED:'):
                raise RuntimeError(json.dumps(state))
            if state['state'] == 'Paused' and not state['requestPending']:
                return call('controller', {'command': 'inspect', 'full': True}, label)
            if time.monotonic() > deadline:
                raise TimeoutError(json.dumps(state))
            time.sleep(.025)

    def step(count, label):
        began = time.monotonic()
        before = settled(label + '-before')
        call('controller', {'command': 'step', 'frames': count}, label + '-request')
        state = settled(label)
        require_paused(state)
        if state['frame'] != before['frame'] + count:
            raise RuntimeError('Observed continuation frame count differs from the request.')
        observed = native_observation(label + '-native')
        require_native_boundary(observed)
        return {'state': state, 'native': observed['bridge'], 'food': observed['detail'],
                'wallSeconds': time.monotonic() - began}

    def native_observation(label):
        def read_frame():
            state = controller.call({'command': 'status'})
            require_paused(state)
            return state['frame']
        try:
            result = observe_settled_pause(lambda: call('bridge', {'command': 'food'}, label), read_frame)
            proof = result['proof']
        except PauseBoundaryError as error:
            (args.out / (label + '-pause-proof.json')).write_text(json.dumps(error.report, indent=2))
            raise
        (args.out / (label + '-pause-proof.json')).write_text(json.dumps(proof, indent=2))
        return result['receipt']

    def native_gameplay(value):
        # Authoring counters and retained future recipe audit entries are evidence,
        # not current gameplay. Keep them intact in observations.json.
        return gameplay_round(value['nativeRound'])

    try:
        bridge = Client(17636)
        controller = ControllerClient(17637)
        initial = settled('initial')
        status = call('bridge', {'command': 'status'}, 'initial-native')['bridge']
        if not status['loadComplete'] or status['fullScreen']:
            raise RuntimeError('Probe requires loaded, windowed native kitchen.')
        if not initial['freshLevelLoadObserved']:
            raise RuntimeError('Controller did not observe this level baseline.')
        call('bridge', {'command': 'render', 'fps': args.render_fps}, 'render')
        call('bridge', {'command': 'arm'}, 'arm')
        baseline = step(args.warmup, 'baseline')
        frame = baseline['state']['frame']
        call('controller', {'command': 'checkpoint', 'path': f'probe-{evidence_key}-{frame}.pb'}, 'checkpoint')
        original = step(args.frames, 'original')
        summary.update({'checkpointFrame': frame, 'endFrame': original['state']['frame'],
                        'originalWallSeconds': original['wallSeconds'],
                        'originalNativeRound': native_gameplay(original['native']), 'replays': []})
        for attempt in range(args.repeat):
            before_attempt = call('bridge', {'command': 'status'}, f'pre-warp-{attempt}-native')['bridge']['nativeCheckpoints']['restoreAttempts']
            call('controller', {'command': 'warp', 'frame': frame, 'development': True}, f'warp-{attempt}')
            restored = settled(f'restored-{attempt}')
            native = call('bridge', {'command': 'status'}, f'restored-{attempt}-native')['bridge']
            last_restore = native['nativeCheckpoints']['lastRestore']
            if (restored['frame'] != frame or not last_restore or not last_restore['verified'] or last_restore['frame'] != frame or
                    last_restore['attempt'] <= before_attempt):
                raise RuntimeError('Native checkpoint has not acknowledged a verified restore.')
            restored_receipt = native_observation(f'restored-{attempt}-settled-native')
            native = restored_receipt['bridge']
            replay = step(args.frames, f'replay-{attempt}')
            expected = {e['id']: e for e in original['state']['entities']}
            actual = {e['id']: e for e in replay['state']['entities']}
            differences = {str(i): {'before': expected.get(i), 'after': actual.get(i)}
                           for i in expected.keys() | actual.keys() if not exact_values(expected.get(i), actual.get(i))}
            report = {'attempt': attempt, 'wallSeconds': replay['wallSeconds'],
                      'requestedAdvancingFrames': args.frames, 'observedAdvancingFrames': replay['state']['frame'] - restored['frame'],
                      'baselineNativeRoundEqual': exact_values(native_gameplay(baseline['native']), native_gameplay(native)),
                      'baselineNativeClocksEqual': exact_values(native_clock_state(baseline['native']), native_clock_state(native)),
                      'finalNativeClocksEqual': exact_values(native_clock_state(original['native']), native_clock_state(replay['native'])),
                      'baselineFullEntityStateEqual': exact_values(baseline['state']['entities'], restored['entities']),
                      'baselineNativeFoodEqual': exact_values(baseline['food']['entities'], restored_receipt['detail']['entities']),
                      'baselineNativePhysicsEqual': exact_values(baseline['native'].get('nativePhysics'),native.get('nativePhysics')) if 'nativePhysics' in native else None,
                      'finalNativePhysicsEqual': exact_values(original['native'].get('nativePhysics'),replay['native'].get('nativePhysics')) if 'nativePhysics' in native else None,
                      'finalNativeRoundEqual': exact_values(native_gameplay(original['native']), native_gameplay(replay['native'])),
                      'nativeFoodEqual': exact_values(original['food']['entities'], replay['food']['entities']),
                      'fullEntityStateEqual': not differences, 'changedEntityIds': list(differences),
                      'finalNativeRound': native_gameplay(replay['native']),
                      'nativeRestore': last_restore, 'fullScreen': replay['native']['fullScreen']}
            summary['replays'].append(report)
            (args.out / f'entity-differences-{attempt}.json').write_text(json.dumps(differences, indent=2))
        summary['passed'] = all(r['baselineNativeRoundEqual'] and r['finalNativeRoundEqual'] and
                                r['baselineNativeClocksEqual'] and r['finalNativeClocksEqual'] and
                                r['baselineFullEntityStateEqual'] and r['baselineNativeFoodEqual'] and
                                r['baselineNativePhysicsEqual'] is True and r['finalNativePhysicsEqual'] is True and
                                r['fullEntityStateEqual'] and r['nativeFoodEqual'] and not r['fullScreen'] for r in summary['replays'])
        summary['pauseBoundary'] = 'Exact settled native pause after two distinct stable physics ticks; initial receipts retained separately.'
    except Exception as error:
        summary['error'] = str(error)
    finally:
        try:
            if bridge is not None: call('bridge', {'command': 'pause'}, 'finally-pause')
        except Exception as error:
            summary['pauseError'] = str(error)
            summary['passed'] = False
        for client in (bridge, controller):
            try:
                if client is not None: client.close()
            except Exception as error:
                summary['closeError'] = str(error)
                summary['passed'] = False
        (args.out / 'observations.json').write_text(json.dumps(records, indent=2))
        (args.out / 'summary.json').write_text(json.dumps(summary, indent=2))
        print(json.dumps(summary, indent=2))
    return 0 if summary['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
