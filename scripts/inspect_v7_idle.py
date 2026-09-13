"""Read-only native trace projection for the V7 scheduling investigation."""
import gzip,json,pathlib,sys

source=pathlib.Path(sys.argv[1])
destination=pathlib.Path(sys.argv[2])
events=[]; last_frame=-1; frames=0; previous_delivered=0; deliveries=[]
entity_keys=['id','name','position','attachedEntityId','ingredientIds','contents','composition','cookingProgress','cookingTime','mixingProgress','mixingTime','workProgress','plateCount','plateStackKind','cannonLoadedEntityId','cannonState','cannonFlying','components']
chef_keys=['playerId','position','heldEntityId','interactingEntityId','serverInteractionId','controlsEnabled']
with gzip.open(source,'rt',encoding='utf-8') as stream, gzip.open(str(destination)+'-frames.jsonl.gz','wt',encoding='utf-8') as output:
 for line in stream:
  row=json.loads(line)
  if row.get('kind')=='call':
   state=row.get('response',{}).get('state',{})
   last_frame=state.get('gameplayFrame',last_frame)
   if last_frame<0: continue
   if state.get('delivered',0)>previous_delivered:
    deliveries.extend({k:e.get(k) for k in ['index','gameplayFrame','recipe','scoreDelta','afterScore']} for e in state.get('gameEvents',[]) if e.get('kind')=='delivery' and e.get('afterScore',{}).get('delivered',0)>previous_delivered)
    previous_delivered=state.get('delivered',0)
   compact={'gf':last_frame,'timer':state.get('timer'),'score':state.get('score'),'delivered':state.get('delivered'),'inputs':row.get('request',{}).get('inputs'),
    'chefs':[{k:c.get(k) for k in chef_keys} for c in state.get('chefs',[])],
    'entities':[{k:e.get(k) for k in entity_keys} for e in state.get('entities',[]) if e.get('active') and not e.get('name','').endswith('_Rigidbody')]}
   output.write(json.dumps(compact,separators=(',',':'))+'\n'); frames+=1
  elif row.get('kind')=='event':
   name=row.get('name',''); value=row.get('value') or {}
   if name.startswith(('planner','earlyOnion','pantryChop')):
    value={k:v for k,v in value.items() if k not in ['state','snapshot']}
    if name=='plannerInitialized': value['preview']={'recipes':value.get('preview',{}).get('recipes',[])[:16]}
    events.append({'gf':last_frame,'name':name,'value':value})
pathlib.Path(str(destination)+'-events.json').write_text(json.dumps({'frames':frames,'deliveries':deliveries,'events':events},indent=2),encoding='utf-8')
print(json.dumps({'frames':frames,'deliveries':deliveries,'events':len(events),'output':str(destination)}))
