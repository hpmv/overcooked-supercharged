"""Read-only fixtures for a possible plate-first, no-onion hotdog transaction."""
import gzip
import hashlib
import json
import re
from pathlib import Path

analysis = Path('artifacts/v16-central-work-analysis.json')
report = json.loads(analysis.read_text())
trace = Path(report['source'])
with trace.open('rb') as source:
    source_hash = hashlib.file_digest(source, 'sha256').hexdigest()
assert source_hash == report['sourceSha256']
out = Path('artifacts/v16-plate-first-opportunities')
out.mkdir(exist_ok=False)
selected = []
for assembly in report['jobs']:
    if assembly['family'] != 'assemble-meal' or not assembly['name'].endswith(('Hotdog_Mustard', 'Hotdog_Ketchup', 'Hotdog_Plain')):
        continue
    index = int(assembly['name'].split('-')[2])
    base = next((j for j in report['jobs'] if j['name'] == f'unplated-hotdog-{index}'), None)
    if base:
        selected.append({'mealNumber': index, 'base': base, 'assembly': assembly})
wanted = {job['base']['start'] for job in selected}
pattern = re.compile(rb'"gameplayFrame"\s*:\s*(-?\d+)')
fixtures = []
with gzip.open(trace, 'rb') as source:
    for line in source:
        if b'"kind":"call"' not in line[:100].replace(b' ', b''):
            continue
        match = pattern.search(line)
        if match is None or int(match.group(1)) not in wanted:
            continue
        frame = int(match.group(1))
        response = json.loads(line)['response']
        data = json.dumps(response, separators=(',', ':')).encode()
        path = out / f'gf{frame}.json'
        path.write_bytes(data)
        fixtures.append({'frame': frame, 'path': str(path), 'sha256': hashlib.sha256(data).hexdigest(),
                         'originalCallLineSha256': hashlib.sha256(line).hexdigest()})
        wanted.remove(frame)
        if not wanted:
            break
assert not wanted, wanted
manifest = {'qualification': 'Unmodified native observations and recorded old jobs. No new input, admission, timing saving, score projection or game-state restoration.',
            'trace': str(trace), 'traceSha256': source_hash,
            'analysis': str(analysis), 'analysisSha256': hashlib.sha256(analysis.read_bytes()).hexdigest(),
            'fixtures': fixtures, 'jobs': selected}
(out / 'manifest.json').write_text(json.dumps(manifest, indent=2))
print(json.dumps({'fixtures': len(fixtures), 'jobs': len(selected), 'out': str(out)}))
