import gzip,hashlib,json
from pathlib import Path
from check_bowl_offmix import pin
p=Path('artifacts/chopped-bun-counter-landing-a.jsonl.gz');rows=[];events=[];first=None
with gzip.open(p,'rt',encoding='utf-8-sig') as stream:
 for line in stream:
  row=json.loads(line)
  if row.get('kind')=='call':
   s=row['response'].get('state')
   if not s or not s.get('levelReady'):continue
   if first is None:first=s
   e=next((e for e in s['entities'] if e['id']==125),None)
   if e:
    rows.append({'frame':s['gameplayFrame'],'position':e['position'],'velocity':e['velocity'],'ordinal':e['observedOrdinal'],'flying':e['throwFlying'],
     'thrower':e['throwerEntityId'],'previousThrower':e['previousThrowerEntityId'],'composition':e.get('composition'),
     'attachedTo':[{'id':st['id'],'name':st['name'],'position':st['position']} for st in s['entities'] if st.get('attachedEntityId')==125],
     'chef':next(c for c in s['chefs'] if c['playerId']==2),'inputs':row['request'].get('inputs')})
  elif row.get('kind')=='event' and row['name'] in ['throwRelease','throwComplete','actionFailure','jobComplete']:
   events.append(row)
out=Path('artifacts/chopped-bun-counter-landing-a-trajectory.json');out.write_text(json.dumps({'source':pin(p),'rows':rows,'events':events},indent=2),encoding='utf-8')
print(json.dumps({'output':str(out),'flight':[r for r in rows if r['flying']],'attached':[r for r in rows if r['attachedTo']][:3],'events':events},separators=(',',':')))
