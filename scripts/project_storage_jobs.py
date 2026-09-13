"""Read closed native traces for bun/clean-plate job costs; never calls the game."""
import argparse,gzip,hashlib,json,re
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('trace',type=Path);p.add_argument('output',type=Path);p.add_argument('--snapshots',type=Path);args=p.parse_args()
selected={'buffer-native-chopped-bun','clear-clean-plate-handoff'}
names={'plannerJobStart','plannerJobComplete','actionComplete','transferComplete','plannerBunBuffered','plannerBunBufferHarvested',
       'cooperativeSauceLeaseAcquired','cooperativeSauceComplete','nativeHeatSafetyDispatched','nativeHeatSafetyBlockedElectives','plannerFailure'}
pattern=re.compile(r'"gameplayFrame"\s*:\s*(-?\d+)');last=-1;last_call=None;events=[];captures=[]
if args.snapshots:args.snapshots.mkdir(parents=True,exist_ok=True)
with gzip.open(args.trace,'rt',encoding='utf-8-sig') as stream:
 for line in stream:
  prefix=line[:100].replace(' ','')
  if '"kind":"call"' in prefix:
   m=pattern.search(line)
   if m:last=int(m.group(1));last_call=line
  elif '"kind":"event"' in prefix:
   row=json.loads(line);name=row.get('name');value=row.get('value') or {}
   if name in names:
    events.append({'gf':last,'name':name,'value':{k:v for k,v in value.items() if k not in ['state','snapshot','planner','approachPath','candidateApproaches']}})
   if args.snapshots and name=='plannerJobStart' and value.get('name') in selected:
    target=args.snapshots/(value['name']+'-gf'+str(last)+'.json')
    target.write_text(json.dumps(json.loads(last_call)['response'],separators=(',',':')),encoding='utf-8');captures.append(str(target))
active={};jobs=[]
for e in events:
 v=e['value']
 if e['name']=='plannerJobStart':active[(v['player'],v['name'])]=(e['gf'],v)
 elif e['name']=='plannerJobComplete':
  old=active.pop((v['player'],v['name']),None)
  if old:jobs.append({'player':v['player'],'name':v['name'],'start':old[0],'end':e['gf'],'frames':e['gf']-old[0],
                      'resources':old[1]['resources'],'actions':old[1]['actions']})
costs=[]
for name in sorted(selected):
 rows=[j for j in jobs if j['name']==name]
 costs.append({'name':name,'jobs':len(rows),'frames':sum(j['frames'] for j in rows),'chefSeconds':sum(j['frames'] for j in rows)/60})
with args.trace.open('rb') as stream:sha=hashlib.file_digest(stream,'sha256').hexdigest()
args.output.write_text(json.dumps({'source':{'path':str(args.trace.resolve()),'sha256':sha},'throughFrame':last,
 'qualification':'Observed job occupancy totals; not automatic wall-time or score savings','costs':costs,'captures':captures,'jobs':jobs,'events':events},indent=2),encoding='utf-8')
print(json.dumps({'throughFrame':last,'costs':costs,'captures':len(captures),'output':str(args.output)}))
