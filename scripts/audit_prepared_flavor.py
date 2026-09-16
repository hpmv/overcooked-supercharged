"""File-only production prepared-flavor audit, with exact consumed-prefix hashes."""
import argparse,gzip,hashlib,json,math,re
from pathlib import Path
from check_bowl_offmix import ingredients,nodes,require,pin,controller_evidence,ordinary_request

class BoundedHashReader:
    def __init__(self,stream,limit):self.stream,self.remaining=stream,limit;self.count=0;self.sha=hashlib.sha256()
    def read(self,n=-1):
        data=self.stream.read(self.remaining if n<0 else min(n,self.remaining))
        self.remaining-=len(data);self.count+=len(data);self.sha.update(data);return data

class Audit:
    def __init__(self):self.initial=None;self.last=None;self.active=None;self.jobs={};self.completed=[];self.plugins=set();self.manifests=set()
    def observe(self,state):
        self.last=state
        if self.initial is None:
            require(state['gameplayFrame']==0,'Initial native frame zero missing');self.initial=state
        instrument=state['instrumentation'];self.plugins.add(instrument['manifest']['pluginSha256']);self.manifests.add(instrument['manifestSha256'])
        if not self.active:return
        d=self.active;entities={e['id']:e for e in state['entities']};chef=next(c for c in state['chefs'] if c['playerId']==1)
        for identity,ordinal in d['identities'].items():
            e=entities.get(identity);require(e and e.get('active') and e.get('observedOrdinal')==ordinal,'Reserved native identity became inactive, absent or reused')
        bowl,home,board=[entities[d[k]] for k in ('bowl','home','board')]
        require(home['attachedEntityId']==bowl['id'] and not any(c['heldEntityId']==bowl['id'] for c in state['chefs']),'Assigned bowl left its exact original home')
        progress=bowl.get('mixingProgress');require(type(progress) in (int,float) and math.isfinite(progress) and 0<=progress<21 and bowl.get('mixingTime')==12,'Native mixer clock is absent, invalid or outside guard')
        require(chef.get('controlsEnabled') and chef['position']['x']>25.2 and chef['position']['z']>-16,'Supplier left its controlled upper-right region')
        actual=ingredients(bowl.get('composition'));before=[16620,18448];after=sorted(before+[d['flavor']])
        require(actual in [before,after],'Assigned bowl changed outside the exact one-flavor delta')
        item=entities.get(board['attachedEntityId'])
        if item and 'WorkableItem' in item.get('components',[]):
            # Native workable ingredients omit intrinsic composition; bind its exact
            # prefab name/identity and subsequently require the prepared native tree.
            require(item.get('name')=={22804:'Chocolate',129618:'Raspberry'}[d['flavor']],'Wrong raw workable prefab appeared on board')
            if d['raw'] is None:d['raw']=(item['id'],item['observedOrdinal'])
            require(d['raw']==(item['id'],item['observedOrdinal']),'Raw flavor observation identity changed')
            d['work'].append(item['workProgress'])
            if chef.get('interactingEntityId')==board['id'] and chef.get('serverInteractionId')==board['id'] and chef['heldEntityId']==0:d['chopFrames']+=1
        elif item and ingredients(item.get('composition'))==[d['flavor']]:
            require(d['raw'] is not None and item['id']!=d['raw'][0] and 'ThrowableItem' in item.get('components',[]),'Prepared flavor lacks native workable replacement')
            require('IngredientToContainerBehaviour' in item.get('components',[]) and item.get('name')=={22804:'ChoppedChocolate',129618:'RaspberryChopped'}[d['flavor']],'Native prepared prefab/ingredient-container evidence absent')
            if d['prepared'] is None:d['prepared']=(item['id'],item['observedOrdinal'])
        source=entities.get(d['prepared'][0]) if d['prepared'] else None
        if source:
            require(source['observedOrdinal']==d['prepared'][1] and ingredients(source.get('composition'))==[d['flavor']],'Exact chopped source identity/composition changed')
            require(not any(c['playerId']!=1 and c['heldEntityId']==source['id'] for c in state['chefs']),'Prepared flavor was intercepted')
            if chef['heldEntityId']==source['id']:d['held']=True
            if source.get('throwFlying') and source.get('throwerEntityId')==chef['entityId']:d['flightFrames']+=1
        if actual==after and d['caughtFrame'] is None:
            require(d['prepared'] and d['held'] and d['flightFrames']>0 and source is None and chef['heldEntityId']==0 and board['attachedEntityId']==0,'Native flavor addition lacks exact held source consumption, flight or cleared supplier/board')
            require(d['chopFrames']>0 and max(d['work'])-min(d['work'])>.5,'Native chopping progress/interaction evidence missing')
            d['caughtFrame']=state['gameplayFrame'];d['beforeIngredients']=before;d['afterIngredients']=after
        d['samples']+=1
    def event(self,name,value):
        if name=='plannerJobStart':
            if self.active:require(not set(value['resources']).intersection(self.active['resources']),'Another logged job claimed reserved flavor resources')
            self.jobs[value['player']]=value
        elif name=='plannerJobComplete':
            job=self.jobs.get(value['player'])
            if job and job['name']==value['name']:
                if value['player']==1 and self.completed and self.completed[-1]['jobName']==value['name']:
                    require(self.completed[-1]['completedFrame']==value['frame'],'Work completion differs from successful supply callback frame');self.completed[-1]['workCompletedFrame']=value['frame']
                del self.jobs[value['player']]
        elif name=='nativePreparedFlavorSupplyStarted':
            require(self.active is None and self.last['gameplayFrame']==value['frame'],'Duplicate or unbound supply admission')
            job=self.jobs.get(1);require(job and job['name']=='supply-prepared-flavor-throw-'+str(value['orderIndex']+1),'Admission lacks exact supplying Work')
            actions=job['actions'];require([a['type'] for a in actions]==['take','place','chop','take','navigate','throw'],'Unexpected prepared supply action sequence')
            guard=actions[-1]['nativeSupplyHome'];crate=int(actions[0]['station']);resources=[value['bowl'],value['home'],value['board'],crate]
            require(set(job['resources'])==set(resources) and len(job['resources'])==4,'Exact bowl/home/board/crate reservation missing')
            require(guard['vessel']==value['bowl'] and guard['home']==value['home'] and guard['role']=='bowl' and actions[-1]['targetEntityId']==value['bowl'],'Throw is not guarded to its exact original bowl')
            initial={e['id']:e for e in self.initial['entities']};entities={e['id']:e for e in self.last['entities']}
            home=initial[value['home']];require('MixingStation' in home['components'] and abs(home['position']['x']-24)<.05 and abs(home['position']['z']+10.8)<.05 and home['attachedEntityId']==value['bowl'],'Receiver is not the original native near bowl')
            require(ingredients(entities[value['bowl']].get('composition'))==[16620,18448] and entities[value['board']]['attachedEntityId']==0,'Admission lacks exact Flour+Egg and empty board')
            require(value['flavor'] in [22804,129618] and 0<=value['nativeMixProgressAtAdmission']<21 and 0<value['estimatedWalkingChopAndEdgesSeconds']<21-value['nativeMixProgressAtAdmission'],'Admission native deadline estimate invalid')
            self.active=dict(value,resources=resources,identities={i:entities[i]['observedOrdinal'] for i in resources},raw=None,prepared=None,work=[],chopFrames=0,flightFrames=0,held=False,caughtFrame=None,samples=0,jobName=job['name'])
            self.observe(self.last)
        elif name in ['nativePreparedFlavorCatchObserved','nativePreparedFlavorSupplyComplete']:
            d=self.active;require(d and self.last['gameplayFrame']==value['frame'] and d['caughtFrame'] is not None,'Completion/catch event lacks exact preceding native state proof')
            require(all(value[k]==d[k] for k in ('bowl','home','board','orderIndex','flavor')) and value['rawItem']==d['raw'][0] and value['preparedItem']==d['prepared'][0] and value['preparedOrdinal']==d['prepared'][1],'Reported food/job identities differ from native observations')
            require(value['accepted'] and value['nativeChopFrames']==d['chopFrames'] and value['nativeFlightFrames']==d['flightFrames'],'Reported native event counts differ')
            if name=='nativePreparedFlavorCatchObserved':require(value['frame']==d['caughtFrame'],'Catch event timing differs')
            else:
                d['completedFrame']=value['frame'];d['rawItem'],d['rawOrdinal']=d.pop('raw');d['preparedItem'],d['preparedOrdinal']=d.pop('prepared');work=d.pop('work');d['nativeWorkRange']=[min(work),max(work)]
                d['accepted']=True;d['nativeChopFrames']=d['chopFrames'];d['nativeFlightFrames']=d['flightFrames'];d['nativeMixProgressAtCompletion']=value['nativeMixProgress'];self.completed.append(d);self.active=None

def audit(path,through):
    initial=path.stat();checker=Audit();complete_hash=hashlib.sha256();complete_bytes=records=0;last_line=None;cutoff=False;truncated=False;gf=-1
    with path.open('rb') as raw:
        reader=BoundedHashReader(raw,initial.st_size)
        try:
            with gzip.GzipFile(fileobj=reader,mode='rb') as stream:
                for line in stream:
                    if not line.endswith(b'\n'):truncated=True;break
                    prefix=line[:100].replace(b' ',b'')
                    if b'"kind":"call"' in prefix:
                        match=re.search(rb'"gameplayFrame"\s*:\s*(-?\d+)',line)
                        candidate=int(match.group(1)) if match else gf
                        if candidate>through:cutoff=True;break
                        gf=candidate;last_line=line
                        if checker.active or checker.initial is None:
                            row=json.loads(line);state=row.get('response',{}).get('state')
                            if state and state.get('levelReady'):
                                if checker.active:ordinary_request(row['request']);require(row['response']['ok'],'Native call failed during flavor Work')
                                checker.observe(state)
                    else:
                        row=json.loads(line)
                        if row.get('kind')=='event':
                            name=row['name']
                            if name=='nativePreparedFlavorSupplyStarted' and last_line:checker.last=json.loads(last_line)['response']['state']
                            checker.event(name,row.get('value') or {})
                    complete_hash.update(line);complete_bytes+=len(line);records+=1
        except EOFError:truncated=True
    require(checker.completed,'No completed native prepared-flavor supplies in consumed prefix')
    require(all(d.get('workCompletedFrame')==d['completedFrame'] for d in checker.completed),'Successful native supply lacks its corresponding completed Work event')
    require(len(checker.plugins)==1 and len(checker.manifests)==1,'Native instrumentation identity changed')
    final=path.stat()
    return dict(passed=True,qualification='Native production admission/ownership/event evidence only; no performance attribution or high-score qualification',
        source={'path':str(path.resolve()),'initialCompressedBytes':initial.st_size,'finalCompressedBytes':final.st_size,'changedWhileReading':(initial.st_size,initial.st_mtime_ns)!=(final.st_size,final.st_mtime_ns),
                'compressedConsumedPrefixBytes':reader.count,'compressedConsumedPrefixSha256':reader.sha.hexdigest(),'completeJsonlPrefixBytes':complete_bytes,'completeJsonlPrefixSha256':complete_hash.hexdigest(),
                'completeRecords':records,'throughGameplayFrame':gf,'intentionalFrameCutoff':cutoff,'incompleteGzipTailObserved':truncated},
        completedSupplies=checker.completed,pendingSupply=checker.active,pluginSha256=next(iter(checker.plugins)),instrumentationManifestSha256=next(iter(checker.manifests)),
        reservationEvidence='Exact logged Work resources plus no other job claim during the lifetime; native station/source identity and input observations checked independently')

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('trace',type=Path);p.add_argument('output',type=Path);p.add_argument('--through-frame',type=int,required=True);p.add_argument('--controller-bundle',type=Path,required=True);a=p.parse_args()
    require(a.trace.resolve()!=a.output.resolve(),'Cannot overwrite trace');report=audit(a.trace,a.through_frame);report['controllerBundleEvidence']=controller_evidence(a.controller_bundle);report['checkerSource']=pin(Path(__file__))
    a.output.write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps({'passed':True,'throughFrame':report['source']['throughGameplayFrame'],'completed':len(report['completedSupplies']),'output':str(a.output)}))
