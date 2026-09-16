"""Validate the bounded native empty-lane far-pot mechanism probe offline."""
import gzip,hashlib,json,pathlib,sys

root=pathlib.Path(__file__).resolve().parents[1]
trace=root/'artifacts/empty-lane-far-pot-a.jsonl.gz'
result=root/'artifacts/empty-lane-far-pot-a-result.json'
route=root/'routes/probes/empty-lane-far-pot.json'
states={}; resolved=[]; releases=[]; completions=[]
with gzip.open(trace,'rt',encoding='utf-8') as stream:
 for line in stream:
  row=json.loads(line)
  if row.get('kind')=='call':
   state=row.get('response',{}).get('state',{})
   if state.get('gameplayFrame',-1)>=0:states[state['frame']]=state
  elif row.get('kind')=='event':
   if row.get('name')=='throwResolved':resolved.append(row['value'])
   if row.get('name')=='throwReleased':releases.append(row['value'])
   if row.get('name')=='throwComplete':completions.append(row['value'])
assert len(resolved)==len(releases)==len(completions)==2
def entities(s):return {e['id']:e for e in s['entities'] if e.get('active')}
def ingredients(e):
 def visit(n):return ([n['id']] if n.get('type')=='IngredientAssembledNode' else [])+sum((visit(c) for c in n.get('children',[])),[])
 return sorted(visit(e.get('composition') or {}))
initial=states[min(states)]; final=states[max(states)]; origin=initial['frame']; checks=[]
initial_entities=entities(initial)
near,far=sorted((e for e in initial_entities.values() if 'CookableContainer' in e.get('components',[]) and e['name']=='DLC08_utensil_pot_01'),key=lambda e:e['position']['x'])
assert abs(near['position']['x']-16.8)<1e-4 and abs(far['position']['x']-18)<1e-4
boards=[e for e in initial_entities.values() if 'Workstation' in e.get('components',[]) and abs(e['position']['x']-15.6)<1e-4]
board=max(boards,key=lambda e:e['position']['z'])
assert abs(board['position']['z']+12)<1e-4
for index,(r,release,complete,target) in enumerate(zip(resolved,releases,completions,[near,far])):
 item=r['itemId'];begin=release['frame'];end=complete['frame'];target_id=target['id']
 assert r['targetEntityId']==complete['targetEntityId']==target_id
 assert release['itemId']==complete['itemId']==item
 start_state=states[begin];start_entities=entities(start_state);end_entities=entities(states[end])
 chef=next(c for c in start_state['chefs'] if c['playerId']==2)
 assert chef['heldEntityId']==item
 original=ingredients(start_entities[item]);before=ingredients(start_entities[target_id]);after=ingredients(end_entities[target_id])
 assert original==[284626] and before==[] and after==original
 assert item not in end_entities
 flight=[s['gameplayFrame'] for f,s in states.items() if begin<f<=end and (e:=entities(s).get(item)) and e.get('throwFlying') and e.get('throwerEntityId')==chef['entityId']]
 assert flight and complete['flightObserved']
 checks.append({'targetEntityId':target_id,'targetPosition':target['position'],'sourceEntityId':item,'sourceOriginalIngredients':original,'beforeTargetIngredients':before,'afterTargetIngredients':after,'releaseFrame':begin,'releaseGameplayFrame':begin-origin,'completionFrame':end,'completionGameplayFrame':end-origin,'nativeFlightGameplayFrames':flight,'sameNativeChefThrower':chef['entityId'],'sourceAbsentAtNativeCompletion':True})
far_start=releases[1]['frame'];far_end=completions[1]['frame']
far_window=[s for f,s in states.items() if far_start<=f<=far_end]
assert all(entities(s)[board['id']]['attachedEntityId']==0 for s in far_window)
assert all(ingredients(entities(s)[near['id']])==[284626] for s in far_window)
assert all(entities(s)[near['id']]['observedOrdinal']==near['observedOrdinal'] for s in far_window)
assert all(entities(s)[far['id']]['observedOrdinal']==far['observedOrdinal'] for s in far_window)
result_state=json.loads(result.read_text(encoding='utf-8-sig'))['state']
assert result_state['frame']==final['frame'] and result_state['gameplayFrame']==122
assert final['timer']<initial['timer'] and final['score']==0 and final['delivered']==0
assert next(c for c in final['chefs'] if c['playerId']==2)['heldEntityId']==0
def pin(path):return {'path':str(path),'sha256':hashlib.file_digest(path.open('rb'),'sha256').hexdigest(),'bytes':path.stat().st_size}
proof={'format':'oc2-native-empty-lane-far-pot-proof','version':1,'passed':True,'scope':'One actual legal-input mechanism probe; no general guarantee for occupied or moving throw lanes.','sourceTrace':pin(trace),'result':pin(result),'route':pin(route),'initialFrame':origin,'finalFrame':final['frame'],'finalGameplayFrame':final['gameplayFrame'],'sampleCount':len(states),'throws':checks,'farThrowLane':{'boardEntityId':board['id'],'boardPosition':board['position'],'firstFrame':far_start,'lastFrame':far_end,'sampleCount':len(far_window),'boardEmptyEverySample':True,'nearPotEntityId':near['id'],'nearPotIngredientsEverySample':[284626],'nearPotObservationIdentityUnchanged':True,'nearPotCookingContinuedNatively':{'before':entities(states[far_start])[near['id']]['cookingProgress'],'after':entities(states[far_end])[near['id']]['cookingProgress']},'farPotObservationIdentityUnchanged':True},'fourLocalChefs':len(final['chefs'])==4,'throwerEmptyAtEnd':True,'nativeTimeAdvanced':True,'score':0}
output=root/'artifacts/empty-lane-far-pot-a-proof.json'
output.write_text(json.dumps(proof,indent=2),encoding='utf-8')
print(json.dumps({'passed':True,'output':str(output),'nearLoadedGameplayFrame':checks[0]['completionGameplayFrame'],'farLoadedGameplayFrame':checks[1]['completionGameplayFrame'],'finalGameplayFrame':122}))
