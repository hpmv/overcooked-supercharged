"""Verify an authored legal chopped-bun landing and retrieval from an empty counter."""
import argparse,gzip,json,math
from pathlib import Path
from check_bowl_offmix import ingredients,pin,require,controller_evidence,ordinary_request

JOBS=['B00-clear-original-target-plate','B01-clear-central-left','B02-clear-central-right','B03-chop-and-land-bun','B04-confirm-native-counter-pickup']
class Checker:
    def __init__(self):
        self.initial=None;self.last=None;self.mapping={};self.raw=None;self.raw_ordinal=None;self.source=None;self.source_ordinal=None;self.composition=None
        self.work=[];self.chop_frames=0;self.held=False;self.flight=[];self.landed=None;self.retrieved=None;self.replaced=None;self.done=set();self.guards=[];self.plate_staged=False
    def resolve(self,state):
        require(state['gameplayFrame']==0 and state['scene']=='s_Day_3_4' and len(state['chefs'])==4,'Fresh four-chef Carnival frame zero required')
        def station(x,z,component):
            found=[e for e in state['entities'] if e.get('active') and component in e.get('components',[]) and abs(e['position']['x']-x)<.05 and abs(e['position']['z']-z)<.05]
            require(len(found)==1,'Native scene-property station selector not unique');return found[0]
        target=station(19.2,-15.6,'AttachStation');staging=station(19.2,-10.8,'AttachStation');board=station(15.6,-14.4,'Workstation')
        plate=next((e for e in state['entities'] if e['id']==target['attachedEntityId']),None)
        require(plate and any('Plate' in c for c in plate['components']) and not ingredients(plate.get('composition')),'Initial intended counter must have its observed original empty plate')
        require(staging['attachedEntityId']==0 and board['attachedEntityId']==0,'Plate staging and preparation surfaces must start empty')
        require(not any(c in target['components'] for c in ['IngredientCatcher','CookingStation','MixingStation']),'Receiver must be an ordinary native counter, not a processing vessel')
        self.mapping={k:e['id'] for k,e in [('target',target),('staging',staging),('board',board),('plate',plate)]}
        self.identities={e['id']:e['observedOrdinal'] for e in [target,staging,board,plate]};self.initial=state
    def state(self,state,inputs):
        if self.initial is None:self.resolve(state)
        entities={e['id']:e for e in state['entities']};m=self.mapping
        for i,o in self.identities.items():require(i in entities and entities[i].get('active') and entities[i].get('observedOrdinal')==o,'Original station or cleared plate identity changed')
        target,staging,board=[entities[m[k]] for k in ['target','staging','board']];chef=next(c for c in state['chefs'] if c['playerId']==2);central=next(c for c in state['chefs'] if c['playerId']==0);frame=state['gameplayFrame']
        if staging['attachedEntityId']==m['plate']:
            self.plate_staged=True;require(not ingredients(entities[m['plate']].get('composition')),'Staged original plate received unintended food')
        if self.plate_staged:require(staging['attachedEntityId']==m['plate'],'Original plate left its explicit staging counter')
        item=entities.get(board['attachedEntityId'])
        if item and 'WorkableItem' in item.get('components',[]):
            require(item['name']=='HotdogBun','A different raw workable occupied the bun board')
            if self.raw is None:self.raw=item['id'];self.raw_ordinal=item['observedOrdinal']
            require((item['id'],item['observedOrdinal'])==(self.raw,self.raw_ordinal),'Raw bun identity changed before preparation')
            self.work.append(item['workProgress'])
            self.chop_frames+=int(chef.get('interactingEntityId')==m['board'] and chef.get('serverInteractionId')==m['board'] and chef['heldEntityId']==0)
        elif item and ingredients(item.get('composition'))==[262914]:
            require(self.raw is not None and item['id']!=self.raw and 'ThrowableItem' in item.get('components',[]),'Chopped bun lacks native workable replacement')
            if self.source is None:self.source=item['id'];self.source_ordinal=item['observedOrdinal'];self.composition=item['composition']
        source=entities.get(self.source)
        if self.source is not None:
            require(source and source.get('active') and source.get('observedOrdinal')==self.source_ordinal and source.get('composition')==self.composition,'Exact chopped bun identity/composition changed or disappeared')
            require(not any(c['playerId'] in [1,3] and c['heldEntityId']==self.source for c in state['chefs']),'Unexpected chef caught or picked up the bun')
            if chef['heldEntityId']==self.source:self.held=True
            if source.get('throwFlying'):
                require(self.held and self.plate_staged and target['attachedEntityId']==0 and source.get('throwerEntityId')==chef['entityId'],'Flight lacks original supplier, staged plate or empty landing target')
                self.flight.append({'frame':frame,'position':source['position']})
            if target['attachedEntityId']==self.source and self.landed is None:
                require(self.flight and not source.get('throwFlying') and chef['heldEntityId']==0 and source.get('previousThrowerEntityId')==chef['entityId'],'Counter attachment lacks completed native P2 flight')
                require(self.last and self.last['targetAttachment']==0,'First landing lacks the preceding empty native target')
                self.landed={'frame':frame,'position':source['position'],'previousFrame':self.last['frame'],'previousPosition':self.last['sourcePosition'],'nativeFlightEnded':True,'targetAttachedExactSource':True}
            if central['heldEntityId']==self.source:
                require(self.landed and target['attachedEntityId']==0,'P0 retrieval precedes native counter attachment or failed to clear counter')
                if self.retrieved is None:self.retrieved=frame
            if self.retrieved is not None and central['heldEntityId']==0 and target['attachedEntityId']==self.source:self.replaced=frame
        require(state['score']==0 and state['delivered']==0,'Unexpected score/delivery in landing mechanism probe')
        self.last={'frame':frame,'targetAttachment':target['attachedEntityId'],'sourcePosition':source['position'] if source else None,'state':state,'inputs':inputs}
    def event(self,name,value):
        require(name not in ['planFailure','actionFailure','actionTimeout'],'Native probe contains a failure')
        if name=='jobComplete':require(value['id'] in JOBS and value['id'] not in self.done,'Unexpected completed job');self.done.add(value['id'])
        if name=='guardMatched':self.guards.append({'frame':self.last['frame'],'path':value['path'],'observed':value['observed']})
    def finish(self):
        require(self.done==set(JOBS),'Corrected five-job native probe incomplete')
        require(self.plate_staged and self.landed and self.retrieved is not None and self.replaced is not None,'Clear/land/retrieve/replace evidence incomplete')
        require(self.chop_frames>0 and self.work and max(self.work)-min(self.work)>.5,'Native chopping interaction/progress evidence missing')
        require(any(g['path']=='entities.37.attachedEntityId' and g['observed']==0 and g['frame']<self.flight[0]['frame'] for g in self.guards),'Prethrow empty-target guard missing')
        state=self.last['state'];require(all(c['heldEntityId']==0 and c['controlsEnabled'] for c in state['chefs']),'Final four chefs must be controlled and empty-handed')
        require(len(self.last['inputs'])==4 and all(p['x']==0 and p['y']==0 and not any(p[k] for k in ['use','pickup','dash']) for p in self.last['inputs']),'Final four native inputs must be neutral')
        return {'passed':True,'qualification':'One corrected ordinary-input counter-landing mechanism probe, not throughput or exact replay qualification',
            'mapping':self.mapping,'identities':self.identities,'rawBun':self.raw,'rawOrdinal':self.raw_ordinal,'preparedBun':self.source,'preparedOrdinal':self.source_ordinal,
            'nativeChopFrames':self.chop_frames,'nativeWorkRange':[min(self.work),max(self.work)],'flight':self.flight,'landing':self.landed,
            'originalPlateStagedAndUnchanged':True,'retrievedByP0Frame':self.retrieved,'sameBunReplacedFrame':self.replaced,'finalFrame':state['gameplayFrame'],
            'finalNativeComposition':self.composition,'guards':self.guards,'allFourControlledEmptyNeutral':True}

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('trace',type=Path);p.add_argument('route',type=Path);p.add_argument('result',type=Path);p.add_argument('output',type=Path);p.add_argument('--controller-bundle',type=Path,required=True);a=p.parse_args()
    require(a.output.resolve() not in [x.resolve() for x in [a.trace,a.route,a.result]],'Output would overwrite source');initial=a.trace.stat();checker=Checker();plugins=set();calls=0
    plan=json.loads(a.route.read_text(encoding='utf-8-sig'));require(set(j['id'] for j in plan['jobs'])==set(JOBS),'Only corrected empty-target probe is supported')
    with gzip.open(a.trace,'rt',encoding='utf-8-sig') as stream:
        for line in stream:
            row=json.loads(line)
            if row.get('kind')=='call':
                ordinary_request(row['request']);require(row['response']['ok'],'Native input request failed');calls+=1
                require(row['request']['command']!='restart' or calls==1,'Mid-probe restart')
                state=row['response'].get('state')
                if state and state.get('levelReady'):checker.state(state,row['response']['inputs']);plugins.add(state['instrumentation']['manifest']['pluginSha256'])
            elif row.get('kind')=='event':checker.event(row['name'],row.get('value') or {})
    report=checker.finish();final=a.trace.stat();require((initial.st_size,initial.st_mtime_ns)==(final.st_size,final.st_mtime_ns),'Trace changed during proof')
    result=json.loads(a.result.read_text(encoding='utf-8-sig'));require(result.get('ok'),'Native controller result failed')
    if 'state' in result:require(result['state']==checker.last['state'],'Result differs from final native state')
    else:require(result.get('gameplayFrame')==report['finalFrame'] and result.get('score')==0,'Compact result differs from final native frame/score')
    require(len(plugins)==1,'Native plugin changed');report.update(sourceTrace=pin(a.trace),route=pin(a.route),result=pin(a.result),checkerSource=pin(Path(__file__)),controllerBundleEvidence=controller_evidence(a.controller_bundle),pluginSha256=next(iter(plugins)),ordinaryInputCommandsOnly=True)
    a.output.write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps({'passed':True,'output':str(a.output),'landedFrame':report['landing']['frame'],'finalFrame':report['finalFrame']}))
