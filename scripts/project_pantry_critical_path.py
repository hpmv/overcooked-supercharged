"""File-only bounded native scheduling projection; hashes exact consumed trace prefixes."""
import argparse,gzip,hashlib,json,re
from pathlib import Path

class BoundedHashReader:
    def __init__(self,stream,limit):self.stream,self.remaining=stream,limit;self.count=0;self.sha=hashlib.sha256()
    def read(self,n=-1):
        data=self.stream.read(self.remaining if n<0 else min(n,self.remaining));self.remaining-=len(data);self.count+=len(data);self.sha.update(data);return data

def project(source,output,through):
    entity_keys=('id','observedOrdinal','name','position','attachedEntityId','ingredientIds','composition','cookingProgress','mixingProgress','workProgress','plateCount','cannonLoadedEntityId','cannonState','cannonFlying','cannonReady')
    chef_keys=('playerId','entityId','position','heldEntityId','interactingEntityId','serverInteractionId','controlsEnabled','directlyControlled','inputSuppressed','dashTimer')
    pattern=re.compile(rb'"gameplayFrame"\s*:\s*(-?\d+)'); delivered_pattern=re.compile(rb'"delivered"\s*:\s*(\d+)')
    events=[];samples={};deliveries=[];last=-1;last_line=None;last_delivered=0;records=0;uncompressed=hashlib.sha256();size=0;cutoff=False
    def snapshot(line,full=False):
        row=json.loads(line);s=row['response']['state'];gf=s['gameplayFrame']
        samples[gf]={'gf':gf,'score':s.get('score'),'delivered':s.get('delivered'),'timer':s.get('timer'),
            'chefs':[{k:c.get(k) for k in chef_keys} for c in s['chefs']],
            'entities':[{k:e.get(k) for k in entity_keys} for e in s['entities'] if e.get('active') and not e.get('name','').endswith('_Rigidbody')],
            'orders':s.get('orders')}
        return s
    initial_size=source.stat().st_size
    with source.open('rb') as raw:
        reader=BoundedHashReader(raw,initial_size)
        try:
            with gzip.GzipFile(fileobj=reader,mode='rb') as stream:
                for line in stream:
                    if not line.endswith(b'\n'):break
                    prefix=line[:100].replace(b' ',b'')
                    if b'"kind":"call"' in prefix:
                        m=pattern.search(line);gf=int(m.group(1)) if m else last
                        if gf>through:cutoff=True;break
                        last=gf;last_line=line
                        dm=delivered_pattern.search(line);d=int(dm.group(1)) if dm else last_delivered
                        if gf>=0 and (gf%10==0 or d!=last_delivered):
                            s=snapshot(line)
                            if d>last_delivered:
                                native=[e for e in s.get('gameEvents',[]) if e.get('kind')=='delivery' and e.get('afterScore',{}).get('delivered',0)>last_delivered]
                                deliveries.append({'gf':gf,'delivered':d,'score':s.get('score'),'events':native})
                        last_delivered=d
                    elif b'"kind":"event"' in prefix:
                        row=json.loads(line);name=row.get('name','');v=row.get('value') or {}
                        if name.startswith(('planner','preService','imminent','cannon','nativeHeat','pantryChop','sharedPantry','nativePreparedFlavor','nearReady')) or name=='actionComplete':
                            v={k:x for k,x in v.items() if k not in ('state','snapshot','approachPath','candidateApproaches')}
                            if name=='plannerInitialized':v['preview']={'recipes':v.get('preview',{}).get('recipes',[])}
                            events.append({'gf':last,'name':name,'value':v})
                            if last_line is not None and name in ('plannerJobStart','plannerJobComplete','pantryChopDelegated','imminentHeadWaitRejected','imminentHeadWaitStarted'):
                                snapshot(last_line)
                    uncompressed.update(line);size+=len(line);records+=1
        except EOFError:pass
    data={'qualification':'Bounded native trace projection; job occupancy and observed gaps do not alone establish causal idle or recoverable time',
          'source':{'path':str(source.resolve()),'initialCompressedBytes':initial_size,'consumedCompressedBytes':reader.count,'consumedCompressedPrefixSha256':reader.sha.hexdigest(),
                    'completeJsonlPrefixBytes':size,'completeJsonlPrefixSha256':uncompressed.hexdigest(),'completeRecords':records},
          'throughFrame':last,'intentionalCutoff':cutoff,'deliveries':deliveries,'events':events,'samples':list(samples.values())}
    with gzip.open(output,'wt',encoding='utf-8') as f:json.dump(data,f,separators=(',',':'))
    print(json.dumps({'output':str(output),'throughFrame':last,'samples':len(samples),'events':len(events),'deliveries':[(d['gf'],d['delivered'],d['score']) for d in deliveries]}))

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('source',type=Path);p.add_argument('output',type=Path);p.add_argument('--through',type=int,required=True);a=p.parse_args();project(a.source,a.output,a.through)
