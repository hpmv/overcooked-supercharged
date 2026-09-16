"""Offline black-box negative tests on compact projections of the actual trace.

The complete initial/final native states are retained. Other frames retain all
fields but only checker-relevant entities plus the board's current native item.
Each altered fixture is explicitly diagnostic, never a replay or native proof.
"""
import copy
import gzip
import json
import pathlib
import subprocess

root = pathlib.Path(__file__).resolve().parent.parent
out = root / 'artifacts/pot-offheat-check-tests'
out.mkdir(exist_ok=True)
source = root / 'artifacts/pot-offheat-a.jsonl.gz'
result = root / 'artifacts/pot-offheat-a-result.json'
plan = root / 'routes/probes/pot-offheat.json'
checker = root / 'scripts/PotOffheatProbeCheck/bin/Release/net10.0/PotOffheatProbeCheck.dll'
last_frame = json.loads(result.read_text())['state']['gameplayFrame']
rows = []
with gzip.open(source, 'rt', encoding='utf8') as f:
    for line in f:
        row = json.loads(line)
        state = row.get('response', {}).get('state')
        if state and state['gameplayFrame'] not in (0, last_frame):
            board = next(e for e in state['entities'] if e['id'] == 23)
            keep = {7, 19, 32, 37, 23, 127, board['attachedEntityId']}
            state['entities'] = [e for e in state['entities'] if e['id'] in keep]
        rows.append(row)

def write_and_check(name, mutation=None, expected=None, target_frame=1000):
    trace = out / (name + '.jsonl.gz')
    report = out / (name + '.proof.json')
    changed = False
    with gzip.open(trace, 'wt', encoding='utf8', compresslevel=3) as f:
        for original in rows:
            row = original
            frame = original.get('response', {}).get('state', {}).get('gameplayFrame')
            match = target_frame[0] <= frame <= target_frame[1] if isinstance(target_frame, tuple) and frame is not None else frame == target_frame
            if mutation is not None and match and (not changed or isinstance(target_frame, tuple)):
                row = copy.deepcopy(original)
                mutation(row)
                changed = True
            f.write(json.dumps(row, separators=(',', ':')) + '\n')
    run = subprocess.run(['dotnet', str(checker), str(trace), str(plan), str(result), str(report)], cwd=root, capture_output=True, text=True)
    proof = json.loads(report.read_text())
    if mutation is None:
        assert run.returncode == 0 and proof['ok'] and proof['dwellSamples'] == 782
    else:
        assert changed and run.returncode != 0 and not proof['ok'], (name, proof)
        assert expected in proof['error'], (name, proof['error'])
    return {'fixture': name, 'expectedOutcomeObserved': True, 'error': proof.get('error'), 'report': str(report.relative_to(root))}

def entity(row, identity):
    return next(e for e in row['response']['state']['entities'] if e['id'] == identity)

checks = [write_and_check('compact-native-baseline')]
checks.append(write_and_check('progress-drift', lambda r: entity(r, 7).__setitem__('cookingProgress', 13.1), 'progress changed off heat'))
checks.append(write_and_check('contents-raw', lambda r: entity(r, 7)['composition'].__setitem__('state', 'Raw'), 'fully cooked'))
checks.append(write_and_check('reused-pot-id', lambda r: entity(r, 7).__setitem__('observedOrdinal', 9999), 'identity or cooking duration changed'))
checks.append(write_and_check('reused-bun-id', lambda r: entity(r, 127).__setitem__('observedOrdinal', 9999), 'chopped bun identity was lost'))
checks.append(write_and_check('left-storage', lambda r: entity(r, 32).__setitem__('attachedEntityId', 0), 'ordinary offheat counter'))
checks.append(write_and_check('forbidden-command', lambda r: r['request'].__setitem__('command', 'resume'), 'Non-input native mutation command'))
checks.append(write_and_check('extra-score', lambda r: r['response']['state'].__setitem__('score', 68), 'delivery, score or timeout changed'))
checks.append(write_and_check('clock-reversed', lambda r: r['response']['state'].__setitem__('clientTime', 0), 'clock/timer did not advance monotonically'))
first_time = next(r['response']['state']['clientTime'] for r in rows if r.get('response', {}).get('state', {}).get('gameplayFrame') == 887)
checks.append(write_and_check('clock-under-thirteen', lambda r: r['response']['state'].__setitem__('clientTime', first_time + .5 * (r['response']['state']['clientTime'] - first_time)), 'Less than thirteen observed native seconds', target_frame=(887,1668)))
report = {'ok': True, 'qualification': 'Black-box checker regression fixtures; compact/mutated files are not native execution traces.', 'checks': checks}
(out / 'summary.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf8')
print(json.dumps({'ok': True, 'actualBlackBoxCases': len(checks), 'output': str(out / 'summary.json')}))
