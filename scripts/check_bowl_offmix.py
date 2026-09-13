"""Read-only native evidence checker for the authored bowl-offmix probe."""
import argparse,copy,gzip,hashlib,json,math,pathlib

EXPECTED=[16620,18448,22804]
JOBS=["M01-supply-flour","M02-supply-egg","M03-chop-chocolate","M04-mix-and-park","M05-observe-offmix-thirteen-seconds","M06-fry-and-restore"]
def require(condition,message):
 if not condition:raise ValueError(message)
def components(entity):return entity.get('components',[])
def ingredients(node):
 if not node:return []
 return sorted(([node['id']] if node.get('type')=='IngredientAssembledNode' else [])+sum((ingredients(c) for c in node.get('children',[])),[]))
def nodes(node):
 if node:
  yield node
  for child in node.get('children',[]):yield from nodes(child)
def mixed(entity):return ingredients(entity.get('composition'))==EXPECTED and any(n.get('type')=='MixedCompositeAssembledNode' and n.get('state')=='Mixed' for n in nodes(entity.get('composition'))) and not any(n.get('state') in ['OverMixed','Burnt','Ruined'] for n in nodes(entity.get('composition')))
def canon(value):return json.dumps(value,sort_keys=True,separators=(',',':'))
def pin(path):
 with path.open('rb') as stream:sha=hashlib.file_digest(stream,'sha256').hexdigest()
 return {'path':str(path.resolve()),'sha256':sha,'bytes':path.stat().st_size}

def controller_evidence(bundle):
 bundle=bundle.resolve();manifest_path=bundle/'manifest.json';manifest=json.loads(manifest_path.read_text(encoding='utf-8-sig'))
 controller=pin(bundle/'OvercookedTAS.Controller.dll')
 require(controller['sha256']==manifest['controllerSha256'],'Selected frozen controller does not match its manifest')
 for source in manifest['sourceFiles']:
  require(pin(bundle/'source'/source['path'])['sha256']==source['sha256'],'Selected frozen source file hash differs')
 tree=''.join(s['path']+'\0'+s['sha256'].lower()+'\n' for s in sorted(manifest['sourceFiles'],key=lambda s:s['path']))
 require(hashlib.sha256(tree.encode()).hexdigest()==manifest['controllerSourceTreeSha256'],'Selected frozen source tree hash differs')
 return {'bundle':str(bundle),'controller':controller,'manifest':pin(manifest_path),'sourceTreeSha256':manifest['controllerSourceTreeSha256'],
         'sourceFilesVerified':len(manifest['sourceFiles']),
         'provenance':'Operator-selected executing controller bundle; native trace records controller version only. This Python checker uses its own independently hashed food-schema checks, not the controller classifier.'}

def ordinary_request(request):
 require(request.get('version')==1 and request.get('command') in ['restart','inspect','step'],'Unexpected native mutation command/version')
 if request['command']=='step':
  require(request.get('steps')==1,'Non-unit input step')
  pads=request.get('inputs',[]);require(sorted(p.get('player') for p in pads)==[0,1,2,3],'Four unique native player slots required')
  for pad in pads:
   require(all(type(pad.get(k)) in [int,float] and math.isfinite(pad[k]) and abs(pad[k])<=1 for k in ['x','y']),'Invalid native movement axis')
   require(all(type(pad.get(k)) is bool for k in ['pickup','use','dash']),'Invalid native logical button')

class Checker:
 def __init__(self):
  self.last=None;self.initial=None;self.mapping=None;self.completed=[];self.offmix=False;self.park=None;self.park_samples=0;self.park_last=None
  self.raw_progress=[];self.mixed_seen=False;self.mixed_carried=False;self.empty_carried=False;self.transfer=None;self.restored=False
 def entity(self,state,entity_id):
  return next((e for e in state['entities'] if e['id']==entity_id and e.get('active')),None)
 def resolve(self,state):
  require(state['scene']=='s_Day_3_4' and len(state['chefs'])==4,'Wrong scene or chef count')
  def station(required,x,z):
   found=[e for e in state['entities'] if e.get('active') and required in components(e) and abs(e['position']['x']-x)<.05 and abs(e['position']['z']-z)<.05]
   require(len(found)==1,'Native station selector is not unique: '+required);return found[0]
  home=station('MixingStation',24,-10.8);bowl=self.entity(state,home['attachedEntityId']);require(bowl is not None,'Original bowl absent')
  counter=station('AttachStation',21.6,-10.8)
  require(not any(c in components(counter) for c in ['MixingStation','CookingStation','ServerMixingStation','ServerCookingStation']),'Parking counter actively processes contents')
  basket=station('CookableContainer',22.8,-21.6)
  require('MixableContainer' in components(bowl) and bowl['mixingTime']==12,'Unexpected native bowl/mixing duration')
  require(not ingredients(bowl.get('composition')) and not ingredients(basket.get('composition')) and counter['attachedEntityId']==0,'Probe must start with empty bowl/fryer/counter')
  self.mapping={'bowl':bowl['id'],'bowlOrdinal':bowl['observedOrdinal'],'home':home['id'],'counter':counter['id'],'basket':basket['id'],'basketOrdinal':basket['observedOrdinal']}
 def state(self,state):
  if state.get('gameplayFrame',-1)<0:return
  if self.initial is None:self.initial=state;self.resolve(state)
  self.last=state;m=self.mapping;bowl=self.entity(state,m['bowl']);basket=self.entity(state,m['basket'])
  require(bowl and bowl['observedOrdinal']==m['bowlOrdinal'],'Original bowl observation identity changed')
  require(basket and basket['observedOrdinal']==m['basketOrdinal'],'Original fryer basket identity changed')
  chef=next(c for c in state['chefs'] if c['playerId']==3)
  if ingredients(bowl.get('composition'))==EXPECTED:
   if not mixed(bowl):self.raw_progress.append(bowl['mixingProgress'])
   else:
    self.mixed_seen=True
    if chef['heldEntityId']==m['bowl'] and self.entity(state,m['home'])['attachedEntityId']==0:self.mixed_carried=True
  if self.offmix:self.parked(state)
  if self.park and not ingredients(bowl.get('composition')) and ingredients(basket.get('composition'))==EXPECTED:
   if self.transfer is None:
    require(chef['heldEntityId']==m['bowl'],'Native transfer did not retain the original bowl')
    require(mixed(basket),'Fryer did not receive exactly the native Mixed dough')
    self.transfer={'gameplayFrame':state['gameplayFrame'],'bowlIngredients':[],'basketIngredients':EXPECTED,'heldOriginalBowl':True,'basketNativeComposition':basket['composition']}
   self.empty_carried|=chef['heldEntityId']==m['bowl']
   self.restored|=self.entity(state,m['home'])['attachedEntityId']==m['bowl'] and chef['heldEntityId']==0
 def parked(self,state):
  m=self.mapping;bowl=self.entity(state,m['bowl']);home=self.entity(state,m['home']);counter=self.entity(state,m['counter'])
  require(counter['attachedEntityId']==m['bowl'] and home['attachedEntityId']==0,'Bowl is not on its reserved ordinary counter off the original mixer')
  require(not any(c['heldEntityId']==m['bowl'] for c in state['chefs']),'Bowl carried during stationary offmix observation')
  require(mixed(bowl),'Parked bowl no longer has exact unruined native Mixed dough')
  if self.park is None:
   self.park={'firstGameplayFrame':state['gameplayFrame'],'firstTimer':state['timer'],'mixingProgress':bowl['mixingProgress'],'nativeComposition':bowl['composition']}
  require(bowl['mixingProgress']==self.park['mixingProgress'] and canon(bowl['composition'])==canon(self.park['nativeComposition']),'Parked native mixing progress or food changed')
  if self.park_last is None or state['gameplayFrame']!=self.park_last['gameplayFrame']:
   if self.park_last:
    require(state['gameplayFrame']==self.park_last['gameplayFrame']+1,'Offmix proof is missing an intervening native frame')
    require(state['timer']<self.park_last['timer'],'Native timer did not advance during offmix observation')
   self.park_samples+=1;self.park_last={'gameplayFrame':state['gameplayFrame'],'timer':state['timer']}
 def event(self,name,value):
  require(name not in ['actionFailure','planFailure'],'Native probe recorded an action/plan failure')
  if name=='jobStart' and value.get('id')==JOBS[4]:self.offmix=True;self.parked(self.last)
  if name=='jobComplete':
   job=value.get('id');require(job in JOBS and job not in self.completed,'Unexpected or duplicate completed job');self.completed.append(job)
   if job==JOBS[4]:
    self.parked(self.last);self.offmix=False
    require(self.park_last['gameplayFrame']-self.park['firstGameplayFrame']>=780 and self.park_samples>=781,'Offmix interval is shorter than thirteen native seconds')
    require(self.park['firstTimer']-self.park_last['timer']>=12.99,'Native elapsed offmix time is too short')
 def finish(self):
  require(self.completed==JOBS,'Authored probe did not finish all six jobs in order')
  require(self.raw_progress and max(self.raw_progress)-min(self.raw_progress)>1 and self.mixed_seen and self.mixed_carried,'Native dough mixing and removal were not observed')
  require(self.park and self.transfer and self.empty_carried and self.restored,'Offmix/pour/empty-bowl restoration evidence incomplete')
  final=self.last;m=self.mapping;bowl=self.entity(final,m['bowl']);basket=self.entity(final,m['basket'])
  require(self.entity(final,m['home'])['attachedEntityId']==m['bowl'] and self.entity(final,m['counter'])['attachedEntityId']==0 and not ingredients(bowl.get('composition')),'Final original empty bowl is not restored and parking released')
  require(mixed(basket) and basket['composition'].get('type')=='CookedCompositeAssembledNode' and basket['composition'].get('state')=='Cooked' and basket['composition'].get('cookingStepId')==17160,'Final fryer food lacks native cooked chocolate dough proof')
  require(all(c['heldEntityId']==0 and c['controlsEnabled'] for c in final['chefs']),'Final chefs must be controlled and empty-handed')
  require(final['score']==0 and final['delivered']==0 and final['timer']<self.initial['timer'],'Unexpected scoring or clock result')
  return {'format':'oc2-native-bowl-offmix-proof','version':1,'passed':True,'qualification':'One legal-input native mechanism probe; no score/replay qualification','mapping':m,'firstGameplayFrame':self.initial['gameplayFrame'],'lastGameplayFrame':final['gameplayFrame'],'offmix':dict(self.park,lastGameplayFrame=self.park_last['gameplayFrame'],lastTimer=self.park_last['timer'],consecutiveSamples=self.park_samples,elapsedNativeSeconds=self.park['firstTimer']-self.park_last['timer']),'transfer':self.transfer,'nativeMixedObservedBeforeRemoval':True,'sameMixedBowlCarriedOffMixer':True,'sameEmptyBowlRestored':True,'finalNativeFryerComposition':basket['composition'],'allFourControlledEmptyChefs':True,'score':final['score']}

def main():
 parser=argparse.ArgumentParser();parser.add_argument('trace',type=pathlib.Path);parser.add_argument('plan',type=pathlib.Path);parser.add_argument('output',type=pathlib.Path)
 parser.add_argument('--result',type=pathlib.Path);parser.add_argument('--controller-bundle',type=pathlib.Path);args=parser.parse_args()
 plan=json.loads(args.plan.read_text(encoding='utf-8-sig'));require([j['id'] for j in plan['jobs']]==JOBS,'Wrong authored probe')
 evidence=controller_evidence(args.controller_bundle) if args.controller_bundle else None
 checker=Checker();plugin_hashes=set();instrumentation_hashes=set();dash_requests=0;held_dash_frames=0;calls=0
 initial_stat=args.trace.stat()
 with gzip.open(args.trace,'rt',encoding='utf-8') as stream:
  for line in stream:
   row=json.loads(line)
   if row.get('kind')=='call':
    calls+=1;ordinary_request(row['request'])
    require(row['request']['command']!='restart' or calls==1,'Restart appeared after native probe began')
    dash_requests+=sum(p.get('dash') is True for p in row['request'].get('inputs',[]))
    require(row['response'].get('ok'),'Recorded game call failed')
    if 'state' in row['response']:
     native=row['response']['state'];checker.state(native)
     if native.get('gameplayFrame',-1)>=0:
      instrument=native.get('instrumentation',{});plugin_hashes.add(instrument.get('manifest',{}).get('pluginSha256'));instrumentation_hashes.add(instrument.get('manifestSha256'))
      chef=next(c for c in native['chefs'] if c['playerId']==3)
      held_dash_frames+=int(chef['heldEntityId']==checker.mapping['bowl'] and chef.get('dashTimer',0)>0)
   elif row.get('kind')=='event':checker.event(row.get('name'),row.get('value') or {})
 proof=checker.finish();final_stat=args.trace.stat()
 require((initial_stat.st_size,initial_stat.st_mtime_ns)==(final_stat.st_size,final_stat.st_mtime_ns),'Trace changed during proof')
 proof['sourceTrace']=pin(args.trace);proof['route']=pin(args.plan);proof['checkerSource']=pin(pathlib.Path(__file__))
 proof['dashButtonRequests']=dash_requests;proof['heldVesselNativeDashFrames']=held_dash_frames;proof['ordinaryInputCommandsOnly']=True
 if args.result:
  result=json.loads(args.result.read_text(encoding='utf-8-sig'));require(result.get('ok') and result.get('paused') and result.get('state')==checker.last,'Closed result differs from final recorded native state')
  proof['result']=pin(args.result)
 if evidence:
  require(len(plugin_hashes)==1 and None not in plugin_hashes and len(instrumentation_hashes)==1 and None not in instrumentation_hashes,'Plugin/instrumentation identity changed or is missing')
  proof['controllerBundleEvidence']=evidence;proof['pluginTelemetrySha256']=next(iter(plugin_hashes));proof['instrumentationManifestSha256']=next(iter(instrumentation_hashes))
 args.output.write_text(json.dumps(proof,indent=2),encoding='utf-8');print(json.dumps({'passed':True,'output':str(args.output),'parkedNativeSeconds':proof['offmix']['elapsedNativeSeconds']}))
if __name__=='__main__':main()
