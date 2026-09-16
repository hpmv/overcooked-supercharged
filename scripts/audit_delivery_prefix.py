"""Bounded native first-delivery scheduling projection; no game connection."""
import argparse, collections, gzip, io, json, re
from pathlib import Path
from analyze_planner import Prefix

def run(path, output, end):
    decoder=json.JSONDecoder(); events=[]; transitions=[]; last={}; jobs=collections.defaultdict(list); spans=[]; frame=-1; checks=0
    def obj(text, marker, offset=None):
        pos=text.find(marker)
        return decoder.raw_decode(text,pos+(len(marker) if offset is None else offset))[0] if pos>=0 else None
    size=path.stat().st_size
    with path.open('rb',buffering=0) as raw:
        prefix=Prefix(raw,size)
        with io.BufferedReader(prefix) as limited,gzip.GzipFile(fileobj=limited) as stream:
            for line in stream:
                if line.startswith(b'{"kind":"event"'):
                    row=json.loads(line);name=row['name'];v=row.get('value') or {}
                    if name=='plannerJobStart':jobs[v['player'],v['name']].append(v)
                    elif name=='plannerJobComplete':
                        queue=jobs[v['player'],v['name']]
                        if queue:
                            start=queue.pop(0);spans.append({'player':v['player'],'name':v['name'],'start':start['frame'],'end':v['frame'],'frames':v['frame']-start['frame'],'resources':start['resources']})
                    if name in {'plannerJobStart','plannerJobComplete','plannerJobPaused','plannerJobResumed','earlyOnionPhase','nativeNearReadyDoughAdmitted','nativeNearReadyDoughComplete','nativeDirectEarlyOnionAdmitted','nativeDirectEarlyOnionComplete','nativeHeatSafetyDispatched','nativeHeatSafetyBlockedElectives','cooperativeSauceLeaseAcquired','cooperativeSauceComplete','cooperativeSauceBarrier','nearSauceStagingAdmitted','nativeCannonFlightLaunched','nativeCannonFlightArrived','nativeCannonFlightLeaseAcquired'}:
                        events.append({'gameplayFrame':frame,'name':name,'value':v})
                    continue
                if not line.startswith(b'{"kind":"call"'):continue
                text=line.decode('utf8');match=re.search(r'"gameplayFrame":(-?\d+)',text)
                if not match:continue
                frame=int(match[1])
                if frame>end:break
                chefs=obj(text,'"chefs":') or []
                entities={i:obj(text,'{"id":'+str(i)+',"layer":',0)for i in [3,6,10,11,12,13,47,49,84,85]}
                observation={'chefs':[(c['playerId'],c['heldEntityId'],c['controlsEnabled'])for c in chefs],
                    'entities':{i:{k:e.get(k) for k in ['ingredientIds','attachedEntityId','cannonLoadedEntityId','cannonFlying','cannonState']}for i,e in entities.items()if e}}
                if observation!=last:
                    transitions.append({'frame':frame,**observation,'chefPositions':{c['playerId']:c['position'] for c in chefs},'mixing':{i:{k:e.get(k)for k in ['mixingProgress','composition']}for i,e in entities.items()if e and i in [3,6]}});last=observation
                if frame in [0,975,1627,1872,1987,2061,2128,2188,2757,3082,4000]:
                    exact=json.loads(line)['response']['state'];es={e['id']:e for e in exact['entities']}
                    assert chefs==exact['chefs'] and all(e==es.get(i)for i,e in entities.items());checks+=1
    report={'qualification':'Actual bounded native scheduling observations; durations do not simulate alternative policies or prove causality alone.',
        'source':str(path),'sourceBytesAtStart':size,'consumedCompressedBytes':prefix.count,'consumedPrefixSha256':prefix.digest.hexdigest(),'throughFrame':end,'projectionChecks':checks,'jobs':spans,'events':events,'nativeTransitions':transitions}
    output.write_text(json.dumps(report,indent=2)+'\n');print(json.dumps({'report':str(output),'jobs':len(spans),'events':len(events),'nativeTransitions':len(transitions),'checks':checks}))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('source',type=Path);p.add_argument('output',type=Path);p.add_argument('--end',type=int,default=4000);a=p.parse_args();run(a.source,a.output,a.end)
