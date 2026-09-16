"""Bounded read-only projection of recorded pot/supply timing; no game requests."""
import gzip, io, json, pathlib, re, sys
from analyze_planner import Prefix, food

source, destination = map(pathlib.Path, sys.argv[1:3])
limit = source.stat().st_size
decoder = json.JSONDecoder()
ids = [2, 7, 23, 32, 33, 38, 39, 40, 41, 42, 43, 45, 47, 49, 51, 52, 56, 63]
events, transitions, summaries = [], [], []
previous, intervals, starts = {}, [], {}
frame = -1
samples = 0
projection_checks = 0
def scalar(text, name):
    match = re.search('"' + name + '":(-?[0-9.Ee+-]+)', text)
    return json.loads(match.group(1)) if match else None
def object_at(text, marker, offset=0):
    pos = text.find(marker)
    return decoder.raw_decode(text, pos + offset)[0] if pos >= 0 else None

with source.open('rb', buffering=0) as raw:
    prefix = Prefix(raw, limit)
    with io.BufferedReader(prefix) as limited, gzip.GzipFile(fileobj=limited) as stream:
        for rawline in stream:
            if rawline.startswith(b'{"kind":"event"'):
                row = json.loads(rawline); value = row.get('value') or {}; name = row.get('name')
                if name in ('plannerJobStart', 'plannerJobComplete', 'plannerResourceReleased', 'preServiceStockAdmitted', 'plannerFailure'):
                    if name == 'plannerFailure': value = {'error': value.get('error')}
                    events.append({'gf': frame, 'event': name, 'value': value})
                continue
            if not rawline.startswith(b'{"kind":"call"'): continue
            text = rawline.decode('utf8'); next_frame = scalar(text, 'gameplayFrame')
            if next_frame is None or next_frame == frame: continue
            frame = next_frame
            chefs = object_at(text, '"chefs":', len('"chefs":')) or []
            entities = {n: object_at(text, '{"id":' + str(n) + ',"layer":') for n in ids}
            entities = {n: e for n, e in entities.items() if e is not None}
            if samples % 1000 == 0:
                exact = json.loads(rawline)['response']['state']
                exact_entities = {e['id']: e for e in exact['entities']}
                assert exact['gameplayFrame'] == frame and exact['chefs'] == chefs
                assert entities == {n: exact_entities[n] for n in ids if n in exact_entities}
                projection_checks += 1
            samples += 1
            changed = []
            for n in (2, 7):
                e = entities[n]; ingredients, states = food(e)
                kind = 'empty' if not ingredients else 'cooked' if 'Cooked' in states else 'cooking'
                if previous.get(n) != kind:
                    if n in starts:
                        start, old = starts[n]; intervals.append({'pot': n, 'kind': old, 'start': start, 'end': frame, 'frames': frame-start})
                    starts[n] = (frame, kind); previous[n] = kind
                    changed.append({'pot': n, 'kind': kind, 'progress': e.get('cookingProgress'), 'ingredients': ingredients})
            if changed:
                transitions.append({'gf': frame, 'timer': scalar(text, 'timer'), 'score': scalar(text, 'score'), 'changes': changed,
                    'chefs': [{k:c.get(k) for k in ('playerId','position','heldEntityId','controlsEnabled')} for c in chefs],
                    'stations': {n: {'attached': e.get('attachedEntityId'), 'food': food(e)[0]} for n,e in entities.items()}})
            summaries.append((frame, scalar(text,'timer'),scalar(text,'score'))) if samples == 1 else None
for n,(start,kind) in starts.items(): intervals.append({'pot':n,'kind':kind,'start':start,'end':frame,'frames':frame-start,'openAtEnd':True})
output = {'source':str(source.resolve()),'sourceBytesAtStart':limit,'consumedPrefixSha256':prefix.digest.hexdigest(),
    'samples':samples,'fullJsonProjectionChecks':projection_checks,'lastFrame':frame,'events':events,'potIntervals':intervals,'transitions':transitions}
destination.write_text(json.dumps(output,indent=2)+'\n',encoding='utf8')
print(json.dumps({'samples':samples,'lastFrame':frame,'events':len(events),'transitions':len(transitions),'projectionChecks':projection_checks,'output':str(destination)}))
