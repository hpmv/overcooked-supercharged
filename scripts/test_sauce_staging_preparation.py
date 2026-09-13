"""File-only preparation regressions; does not open a game connection."""
import gzip, hashlib, json, subprocess, sys
from pathlib import Path
from extract_inputs import encode

root = Path(__file__).resolve().parents[1]
receipt = json.loads((root / 'artifacts/sauce-staging-probe-preparation.json').read_text())
checks = []
def require(value, name):
    if not value: raise AssertionError(name)
    checks.append(name)
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
for path, expected in receipt['files'].items():
    p = Path(path); require(sha(p) == expected['sha256'] and p.stat().st_size == expected['bytes'], 'exact artifact: ' + p.name)
setup = json.loads((root / 'routes/probes/sauce-staging-common-setup.json').read_text())
waits = [a for a in setup['jobs'][0]['actions'] if a['type'] == 'wait']
require([a.get('durationFrames', 1) for a in waits] == [18, 2] and all('frames' not in a for a in waits), 'effective native route wait durations 18 and 2')
with gzip.open(root / 'routes/probes/sauce-staging-prefix-gf1346-neutral.jsonl.gz', 'rt') as f: requests = [json.loads(line) for line in f]
require(len(requests) == 1350 and sum(r.get('steps', 1) for r in requests if r['command'] == 'step') == 1347, 'preserved prefix counts')
require(hashlib.sha256(b''.join(encode(r) for r in requests[:-1])).hexdigest() == receipt['originalCanonicalRequestsSha256'], 'original 1349 canonical request hash')
require(requests[-1] == receipt['appendedCommands'][0], 'one explicitly new neutral request')
a, b = [json.loads((root / f'routes/probes/sauce-staging-{name}.json').read_text())['jobs'][0]['actions'] for name in ['source37', 'near40']]
diffs = [i for i, (x, y) in enumerate(zip(a, b)) if x != y]
require(diffs == [4, 6] and all({**b[i], 'station': '37'} == a[i] for i in diffs), 'branch actions differ only temporary place and take')
before = {Path(p): sha(Path(p)) for p in receipt['files']}
run = subprocess.run([sys.executable, str(root / 'scripts/prepare_sauce_staging_probe.py')], cwd=root, capture_output=True, text=True)
require(run.returncode != 0 and 'FileExistsError' in run.stderr and all(sha(p) == h for p, h in before.items()), 'no-overwrite refuses existing outputs and preserves bytes')
require(receipt['corrections'][0]['oldSetup']['sha256'] == '5a49be95fc8d5e49ec8575dd9468dde0d7a2c2a79d3fe0777d0a8b53d575e92a', 'wait correction explicitly preserves old evidence hash')
result = {'ok': True, 'checks': checks, 'count': len(checks), 'qualification': 'Offline preparation checks only; no native experiment.', 'scriptSha256': sha(Path(__file__))}
(root / 'artifacts/sauce-staging-probe-preparation-tests.json').write_text(json.dumps(result, indent=2) + '\n')
print(json.dumps(result, indent=2))
