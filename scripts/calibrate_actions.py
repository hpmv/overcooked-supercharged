"""Extract observed adaptive action/stage timings without loading frame snapshots."""
import argparse, collections, gzip, hashlib, json, statistics
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument('trace',type=Path)
parser.add_argument('--out',type=Path,required=True)
args=parser.parse_args()
if args.trace.resolve()==args.out.resolve():raise SystemExit('Output must not overwrite trace')
phase=None
stages={}
actions=[]
guards=[]
counts=collections.Counter()
opener=gzip.open if args.trace.suffix=='.gz' else open
with opener(args.trace,'rb') as source:
    for raw in source:
        # The trace writer emits compact JSON with kind first. Frame-call
        # telemetry dominates the stream; avoid parsing those large objects.
        if not raw.startswith(b'{"kind":"event"'):continue
        event=json.loads(raw)
        name=event['name'];value=event.get('value');counts[name]+=1
        if name=='phaseStart':phase=value
        if name=='actionStage':
            key=(value.get('player'),value.get('type'))
            if value.get('actionFrames')==0:stages[key]=[]
            stages.setdefault(key,[]).append((value['stage'],value['actionFrames']))
        if name=='actionComplete':
            spec=value['action'];key=(spec.get('player'),spec.get('type'));end=value['frames']
            marks=stages.pop(key,[]);parts=[]
            for index,(stage,start) in enumerate(marks):
                stop=marks[index+1][1] if index+1<len(marks) else end
                parts.append({'stage':stage,'frames':max(0,stop-start),'seconds':max(0,stop-start)/60})
            actions.append({'phase':phase,'type':spec.get('type'),'player':spec.get('player'),'station':spec.get('station'),
                            'cannon':spec.get('cannon'),'frames':end,'seconds':end/60,'specification':spec,'stages':parts})
        if 'Suppression' in name or 'Reacquire' in name or name.endswith('Retry') or 'Failure' in name:
            if isinstance(value,dict):value={k:v for k,v in value.items() if k not in ('state','snapshot')}
            guards.append({'name':name,'value':value})
groups=collections.defaultdict(list)
for action in actions:groups[action['type']].append(action['seconds'])
summary={kind:{'count':len(values),'min':min(values),'median':statistics.median(values),'max':max(values),'sum':sum(values)} for kind,values in sorted(groups.items())}
digest=hashlib.file_digest(args.trace.open('rb'),'sha256').hexdigest()
result={'source':str(args.trace.resolve()),'sourceSha256':digest,'classification':'observed_action_durations_not_independent_cost_terms',
        'notes':['All durations are logical frames divided by60, as recorded by actionComplete.',
                 'Navigate/face/transfer stages overlap different chefs and must not be summed as round duration.',
                 'A single combined probe has route/setup overhead and insufficient repeated trips to calibrate all batching costs.',
                 'Cook/mix passive wait duration is observation wait remaining, not native processing duration.'],
        'summary':summary,'actions':actions,'guards':guards,'eventCounts':dict(counts)}
args.out.parent.mkdir(parents=True,exist_ok=True)
args.out.write_text(json.dumps(result,indent=2),encoding='utf-8')
print(json.dumps({'summary':summary,'guards':guards,'actionCount':len(actions),'output':str(args.out)},indent=2))
