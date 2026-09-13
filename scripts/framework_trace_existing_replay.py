"""Trace one existing checkpoint warp and fixed-input replay without a level reload."""
import argparse
import json
from pathlib import Path
import sys
import time

from framework_rpc import Client, ControllerClient


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True,
                        help='Input-probe directory containing summary.json and recording.json.')
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--trace-slot', default='native-physics-trace')
    args = parser.parse_args()
    if args.out.exists():
        parser.error('Output already exists.')
    summary = json.loads((args.source/'summary.json').read_text(encoding='utf-8-sig'))
    rows = json.loads((args.source/'observations.json').read_text(encoding='utf-8-sig'))
    recording = json.loads((args.source/'recording.json').read_text(encoding='utf-8-sig'))
    baseline = next(row['response'] for row in rows if row.get('label') in ('base', 'initial'))
    checkpoint_frame = baseline['frame']
    expected_frame = checkpoint_frame + len(recording['frames'])
    report = {
        'passed': False,
        'classification': 'read-only native trace around existing checkpoint warp and fixed-input replay',
        'source': str(args.source.resolve()),
        'checkpointFrame': checkpoint_frame,
        'expectedFrame': expected_frame,
        'recordingSha256': recording['sha256'],
        'records': [],
    }
    bridge = host = None
    began = time.monotonic()

    def call(target, request, label, retain=True):
        response = (bridge if target == 'bridge' else host).call(request)
        if retain:
            report['records'].append({
                'label': label, 'target': target, 'request': request,
                'response': response, 'wallSeconds': time.monotonic()-began,
            })
        return response

    def settled(label, expected=None):
        deadline = time.monotonic() + 45
        while True:
            value = host.call({'command': 'status'})
            if value.get('errors') or value.get('state') == 'Error':
                raise RuntimeError(label + ': ' + json.dumps(value))
            if value.get('state') == 'Paused' and not value.get('requestPending'):
                if expected is not None and value.get('frame') != expected:
                    raise RuntimeError(label + ' paused at an unexpected frame: ' + json.dumps(value))
                return call('host', {'command': 'inspect', 'full': True}, label)
            if time.monotonic() >= deadline:
                raise TimeoutError(label + ' did not settle.')
            time.sleep(.025)

    def trace(operation, label, payload=None, retain=True):
        return call('bridge', {'command': 'hot-call', 'slot': args.trace_slot,
                               'operation': operation, 'args': payload or {}}, label, retain)

    try:
        bridge, host = Client(17636), ControllerClient(17637)
        initial = settled('initial')
        call('bridge', {'command': 'pause'}, 'initial-fence')
        before = call('bridge', {'command': 'status'}, 'before-status')['bridge']['nativeCheckpoints']
        trace('clear', 'trace-clear')
        trace('mark', 'trace-before-warp', {'code': 240, 'value': checkpoint_frame})
        call('bridge', {'command': 'arm'}, 'warp-arm')
        call('host', {'command': 'warp', 'frame': checkpoint_frame, 'development': True}, 'warp')
        restored = settled('restored', checkpoint_frame)
        call('bridge', {'command': 'pause'}, 'restored-fence')
        after = call('bridge', {'command': 'status'}, 'restored-status')['bridge']['nativeCheckpoints']
        restore = after.get('lastRestore')
        if not restore or restore.get('verified') is not True or restore.get('frame') != checkpoint_frame or \
                after.get('restoreAttempts', 0) <= before.get('restoreAttempts', 0):
            raise RuntimeError('Warp lacks a new verified native restore.')
        trace('mark', 'trace-after-warp', {'code': 250, 'value': checkpoint_frame})
        trace('mark', 'trace-before-replay', {'code': 260, 'value': checkpoint_frame})
        call('bridge', {'command': 'arm'}, 'replay-arm')
        call('host', {'command': 'raw-replay', 'recording': recording}, 'replay')
        replayed = settled('replayed', expected_frame)
        call('bridge', {'command': 'pause'}, 'replayed-fence')
        trace('mark', 'trace-after-replay', {'code': 270, 'value': expected_frame})
        trace_receipt = trace('read', 'trace-read', {'afterSequence': 0, 'max': 32768}, False)
        report.update(passed=True, initialFrame=initial['frame'], restored=restored,
                      replayed=replayed, nativeRestore=restore, nativeTrace=trace_receipt)
    except Exception as error:
        report['error'] = str(error)
    finally:
        if bridge is not None:
            try:
                call('bridge', {'command': 'pause'}, 'finally-pause')
                trace('deactivate', 'trace-deactivate')
            except Exception as error:
                report['cleanupError'] = str(error)
                report['passed'] = False
        for client in (bridge, host):
            try:
                if client is not None:
                    client.close()
            except Exception:
                pass
        report['wallSeconds'] = time.monotonic()-began
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({key: value for key, value in report.items()
                      if key not in ('records', 'restored', 'replayed', 'nativeTrace')}, indent=2))
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
