"""Preserve exact response fixtures and a compact event/chef projection from a closed trace."""
import argparse
import gzip
import hashlib
import json
from pathlib import Path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('trace', type=Path)
p.add_argument('out', type=Path)
p.add_argument('--start', type=int, required=True)
p.add_argument('--frames', default='')
a = p.parse_args()
a.out.mkdir(exist_ok=False)
wanted = set(map(int, filter(None, a.frames.split(','))))
events, changes, prior = [], [], None
last = None
with gzip.open(a.trace, 'rt', encoding='utf-8') as source:
    for line in source:
        row = json.loads(line)
        if row.get('kind') == 'event':
            if last and last['state'].get('gameplayFrame', -1) >= a.start:
                events.append(row)
            continue
        response = row.get('response')
        if not isinstance(response, dict) or not isinstance(response.get('state'), dict):
            continue
        state = response['state']
        last = response
        frame = state.get('gameplayFrame', -1)
        if frame in wanted:
            (a.out / f'gf{frame}.json').write_text(json.dumps(response, indent=2), encoding='utf-8')
            wanted.remove(frame)
        if frame < a.start:
            continue
        projection = [{'player': c.get('playerId'), 'held': c.get('heldEntityId'),
                       'pickup': c.get('pickupTargetId'), 'placement': c.get('placementTargetId')}
                      for c in state.get('chefs', [])]
        if projection != prior:
            changes.append({'frame': frame, 'chefs': projection})
            prior = projection
if wanted:
    raise ValueError(f'Missing frames: {wanted}')
with a.trace.open('rb') as f:
    sha = hashlib.file_digest(f, 'sha256').hexdigest()
(a.out / 'final.json').write_text(json.dumps(last, indent=2), encoding='utf-8')
(a.out / 'projection.json').write_text(json.dumps({'trace': str(a.trace.resolve()),
    'traceSha256': sha, 'start': a.start, 'events': events, 'chefChanges': changes}, indent=2), encoding='utf-8')
print(json.dumps({'out': str(a.out), 'events': len(events), 'chefChanges': len(changes),
                  'finalFrame': last['state']['gameplayFrame']}))
