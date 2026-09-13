"""File-only closed V16 native pot occupancy and selected full snapshot extraction.

Job leases are reconstructed from controller notifications and explicitly kept
separate from native attachment/contents observations. No gameplay connection.
"""
import gzip
import hashlib
import json
from pathlib import Path
from archive_closed_gate_logs import locked_read

ROOT = Path(__file__).resolve().parents[1]
TRACE = ROOT / 'artifacts/native-round-v16/trial001.jsonl.gz'
OUT = ROOT / 'artifacts/v16-native-pot-occupancy.jsonl'
SNAPS = ROOT / 'artifacts/v16-supply-snapshots'
FRAMES = {11069, 11445, 12373, 14401, 15625}
EXPECTED = 'd3802824e17cf667c954b822c5479c3d36ac30ed45b5699caa601c683be1f5e2'
assert not OUT.exists()
SNAPS.mkdir(exist_ok=True)
assert not any((SNAPS / f'gf{f}.json').exists() for f in FRAMES)
jobs = []
gaps = []
active_gaps = {}
snapshots = []
frame = -1
samples = 0
last_status = {}


def ingredients(n):
    if not n:
        return []
    return ([n['id']] if n.get('type') == 'IngredientAssembledNode' else []) + [i for child in n.get('children', []) for i in ingredients(child)]


def region(chef):
    pos = chef['position']
    if not chef['controlsEnabled'] or pos['y'] > .9:
        return 'transit'
    if pos['x'] < 15.6:
        return 'upper-left' if pos['z'] > -16.8 else 'lower-left'
    if pos['x'] > 25.2:
        return 'upper-right' if pos['z'] > -16.8 else 'lower-right'
    return 'center-or-boundary'


with locked_read(TRACE) as raw:
    h = hashlib.file_digest(raw, 'sha256').hexdigest()
    assert h == EXPECTED
    raw.seek(0)
    with gzip.GzipFile(fileobj=raw) as stream, OUT.open('x', encoding='utf-8') as out:
        for line in stream:
            row = json.loads(line)
            if row['kind'] == 'event':
                name, value = row['name'], row['value']
                if name == 'plannerJobStart':
                    jobs.append({'player': value['player'], 'name': value['name'], 'start': value['frame'], 'resources': set(value['resources'])})
                elif name == 'plannerJobComplete':
                    matching = [j for j in jobs if j['player'] == value['player'] and j['name'] == value['name']]
                    if matching:
                        jobs.remove(matching[0])
                elif name == 'plannerResourceReleased':
                    matching = [j for j in jobs if j['player'] == value['player'] and j['name'] == value['name']]
                    assert len(matching) == 1
                    matching[0]['resources'].discard(value['resource'])
                elif name == 'plannerStatus':
                    last_status = value
                continue
            if row['kind'] != 'call':
                continue
            s = row['response']['state']
            frame = s['gameplayFrame']
            if frame in FRAMES and frame not in {x['frame'] for x in snapshots}:
                destination = SNAPS / f'gf{frame}.json'
                destination.write_text(json.dumps(row['response'], separators=(',', ':')) + '\n', encoding='utf-8')
                snapshots.append({'frame': frame, 'path': str(destination), 'sha256': hashlib.sha256(destination.read_bytes()).hexdigest()})
            entities = {e['id']: e for e in s['entities']}
            chef2 = next(c for c in s['chefs'] if c['playerId'] == 2)
            where = region(chef2)
            pots = []
            for pot, home in [(2, 17), (7, 19)]:
                e = entities[pot]
                content = ingredients(e['composition'])
                home_attached = entities[home]['attachedEntityId'] == pot
                holders = [c['playerId'] for c in s['chefs'] if c['heldEntityId'] == pot]
                holders_work = [{k: j[k] for k in ['player', 'name', 'start']} for j in jobs if {pot, home} & j['resources']]
                value = {'pot': pot, 'home': home, 'ordinal': e['observedOrdinal'], 'ingredients': content,
                         'progress': e['cookingProgress'], 'nativeAtOriginalHome': home_attached,
                         'holderPlayers': holders, 'workLeaseNotifications': holders_work}
                pots.append(value)
                empty_home = not content and home_attached and not holders
                if empty_home:
                    gap = active_gaps.setdefault(pot, {'pot': pot, 'home': home, 'start': frame, 'end': frame,
                        'samples': 0, 'p2RegionSamples': {}, 'withoutPotOrHomeWorkLeaseSamples': 0})
                    gap['end'] = frame
                    gap['samples'] += 1
                    gap['p2RegionSamples'][where] = gap['p2RegionSamples'].get(where, 0) + 1
                    gap['withoutPotOrHomeWorkLeaseSamples'] += not holders_work
                elif pot in active_gaps:
                    gap = active_gaps.pop(pot)
                    gap['nextObservedOccupiedOrDetachedFrame'] = frame
                    gap['nextNativeContents'] = content
                    gaps.append(gap)
            samples += 1
            center_ids = [32, 33, 37, 38, 40, 41, 43, 61]
            counters = [{'id': i, 'attached': entities[i]['attachedEntityId'],
                         'workLeasePlayers': [j['player'] for j in jobs if i in j['resources']]} for i in center_ids]
            out.write(json.dumps({'frame': frame, 'timer': s['timer'], 'delivered': s['delivered'],
                'p2Region': where, 'chefs': [{k: c[k] for k in ['playerId', 'position', 'heldEntityId', 'controlsEnabled']} for c in s['chefs']],
                'pots': pots, 'centerCounters': counters,
                'statusFrame': last_status.get('gameplayFrame'),
                'persistentStatus': {k: last_status.get(k) for k in ['potRescues', 'earlyOnions', 'fryerRescues', 'bakeryLeases', 'sausageBuffers']}
            }, separators=(',', ':')) + '\n')
for gap in active_gaps.values():
    gap['openAtRoundEnd'] = True
    gaps.append(gap)
report = {'format': 'v16-native-supply-screen', 'trace': str(TRACE), 'traceSha256': h,
          'scope': 'Exact native empty/original-home intervals. Chef regions use explicit position boundaries; native flight is separate. Work-lease notifications are reconstructed, while persistent lease status is sampled and may lag; this is not a proof of scheduler admission or buffer throughput.',
          'sourceSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
          'projection': str(OUT), 'projectionSha256': hashlib.sha256(OUT.read_bytes()).hexdigest(),
          'samples': samples, 'lastGameplayFrame': frame, 'snapshots': snapshots, 'gaps': gaps}
dest = ROOT / 'artifacts/v16-native-pot-gap-screen.json'
with dest.open('x', encoding='utf-8') as f:
    json.dump(report, f, indent=2)
    f.write('\n')
print(json.dumps({'report': str(dest), 'samples': samples, 'gaps': len(gaps), 'snapshots': snapshots}))
