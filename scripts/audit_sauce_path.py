"""Bounded, file-only action and native-motion projection for sauce-path diagnosis.

No gameplay mutations or counterfactual score claims. Events use the latest
observed gameplay frame, since action event `frame` is Unity's absolute frame.
"""
import argparse, collections, gzip, hashlib, io, json, math, re
from pathlib import Path
from analyze_planner import Prefix


def run(source, destination, start, end):
    decoder = json.JSONDecoder()
    def obj(text, marker, offset=None):
        p = text.find(marker)
        return decoder.raw_decode(text, p + (len(marker) if offset is None else offset))[0] if p >= 0 else None
    frame, samples, checks = -1, 0, 0
    actions, spans, events, states, counts = {}, [], [], [], collections.Counter()
    relevant = {'plannerJobStart', 'plannerJobComplete', 'actionCreated', 'actionStage', 'actionComplete',
                'pathPlanned', 'pathReplan', 'navigationWaiting', 'navigationBrake', 'interactionTargetVerified',
                'plateUnderAssemblyObserved', 'assemblyRecoveryPickup', 'assemblyRecovered', 'transferComplete',
                'condimentSwitchEdge', 'condimentSwitchComplete', 'transportComplete', 'transportNativeTarget',
                'cooperativeSauceLeaseAcquired', 'cooperativeSauceBarrier', 'cooperativeSauceComplete'}
    source_bytes = source.stat().st_size
    with source.open('rb', buffering=0) as raw:
        prefix = Prefix(raw, source_bytes)
        with io.BufferedReader(prefix) as limited, gzip.GzipFile(fileobj=limited) as stream:
            for line in stream:
                if line.startswith(b'{"kind":"event"'):
                    event = json.loads(line); name, value = event['name'], event.get('value') or {}
                    counts[name] += 1
                    player = value.get('player', value.get('action', {}).get('player'))
                    if name == 'actionCreated':
                        actions[player] = {'player': player, 'action': value, 'start': frame, 'stages': []}
                    elif name == 'actionStage' and player in actions:
                        actions[player]['stages'].append({'start': frame, **value})
                    elif name == 'actionComplete' and player in actions:
                        action = actions.pop(player); action.update(end=frame, elapsedFrames=frame-action['start'], completion=value)
                        if start <= action['start'] <= end or action['action'].get('type') == 'fire-cannon': spans.append(action)
                    if name in relevant and (start <= frame <= end or value.get('type') == 'fire-cannon' or
                            value.get('action', {}).get('type') == 'fire-cannon' or name.startswith('cooperativeSauce')):
                        events.append({'gameplayFrame': frame, 'name': name, 'value': value})
                    continue
                if not line.startswith(b'{"kind":"call"'): continue
                text = line.decode('utf8'); m = re.search(r'"gameplayFrame":(-?\d+)', text)
                if not m: continue
                next_frame = int(m[1])
                if next_frame == frame: continue
                frame = next_frame; samples += 1
                if not start <= frame <= end: continue
                chefs = obj(text, '"chefs":') or []
                request = obj(text, '"request":') or {}
                ids = [13, 37, 49, 72, 79, 84, 85]
                entities = {i: obj(text, '{"id":'+str(i)+',"layer":', 0) for i in ids}
                entities = {i:e for i,e in entities.items() if e is not None}
                if len(states) % 200 == 0:
                    exact = json.loads(line)['response']['state']; es = {e['id']:e for e in exact['entities']}
                    assert chefs == exact['chefs'] and frame == exact['gameplayFrame']
                    assert entities == {i:es[i] for i in ids if i in es}; checks += 1
                states.append({'gameplayFrame': frame, 'inputs': request.get('inputs'),
                    'chefs': [{k:c.get(k) for k in ['playerId','entityId','position','forward','heldEntityId','velocity',
                        'controlsEnabled','movementSuppressed','interacting','useTargetId','pickupTargetId','placementTargetId']} for c in chefs],
                    'entities': {i:{k:e.get(k) for k in ['id','position','attachedEntityId','switchIndex','composition','contents',
                        'cannonFlying','cannonLoadedEntityId','cannonState']} for i,e in entities.items()}})
    for a in spans:
        stages = a['stages']
        for i,s in enumerate(stages): s['end'] = stages[i+1]['start'] if i+1<len(stages) else a['end']; s['frames'] = s['end']-s['start']
        selected = [s for s in states if a['start'] <= s['gameplayFrame'] <= a['end']]
        path = [next(c for c in s['chefs'] if c['playerId']==a['player'])['position'] for s in selected]
        a['nativeMotionWindowComplete'] = bool(selected) and selected[0]['gameplayFrame']==a['start'] and selected[-1]['gameplayFrame']==a['end']
        a['observedXZTravel'] = sum(math.hypot(b['x']-p['x'],b['z']-p['z']) for p,b in zip(path,path[1:])) if a['nativeMotionWindowComplete'] else None
    report = {'source':str(source.resolve()), 'sourceBytesAtStart':source_bytes,'sourceSha256':prefix.digest.hexdigest(),
        'sourceBytesAtFinish':source.stat().st_size, 'frameWindow':[start,end], 'totalNativeFramesRead':samples,
        'projectionChecksAgainstFullJson':checks,'scriptSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        'eventCounts':dict(counts),'actions':spans,'events':events,'nativeSamples':states,
        'qualification':'Observed action/stage timings and native planar displacement; not a simulated alternative or scoring proof.'}
    destination.write_text(json.dumps(report,indent=2)+'\n',encoding='utf8')
    print(json.dumps({'output':str(destination),'samples':len(states),'actions':len(spans),'events':len(events),'projectionChecks':checks}))


if __name__ == '__main__':
    p=argparse.ArgumentParser();p.add_argument('source',type=Path);p.add_argument('destination',type=Path)
    p.add_argument('--start',type=int,default=1346);p.add_argument('--end',type=int,default=2155)
    a=p.parse_args();run(a.source,a.destination,a.start,a.end)
