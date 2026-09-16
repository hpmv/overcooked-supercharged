"""Read a closed native trace once; retain central action paths and job-boundary observations."""
import argparse,gzip,hashlib,json,re
from pathlib import Path

def project(source:Path,output:Path):
    before=source.stat();compressed=hashlib.sha256();plain=hashlib.sha256();records=0;last=-1;lastline=None
    frame=re.compile(rb'"gameplayFrame"\s*:\s*(-?\d+)')
    events=[];boundaries={};active={};jobs=[];actionseq=[0]*4;planned=set()
    class Reader:
        def __init__(self,stream):self.stream=stream
        def read(self,n=-1):
            data=self.stream.read(n);compressed.update(data);return data
    def boundary():
        if last in boundaries or lastline is None:return
        row=json.loads(lastline);s=row['response']['state']
        boundaries[last]={'frame':last,'nativeFrame':s.get('frame'),'timer':s.get('timer'),'score':s.get('score'),'delivered':s.get('delivered'),
            'chefs':[{k:c.get(k) for k in ('playerId','entityId','position','lastVelocity','heldEntityId','placementTargetId','controlsEnabled','runSpeed','surfaceSpeedMultiplier')} for c in s['chefs']],
            'entities':[{k:e.get(k) for k in ('id','observedOrdinal','name','position','attachedEntityId','composition','cookingProgress','mixingProgress','plateCount')} for e in s['entities'] if e.get('active') and not e.get('name','').endswith('_Rigidbody')]}
    with source.open('rb') as raw:
        with gzip.GzipFile(fileobj=Reader(raw),mode='rb') as stream:
            for line in stream:
                plain.update(line);records+=1
                if b'"kind":"call"' in line[:90].replace(b' ',b''):
                    m=frame.search(line)
                    if m:last=int(m.group(1));lastline=line
                elif b'"kind":"event"' in line[:90].replace(b' ',b''):
                    row=json.loads(line);name=row.get('name','');v=row.get('value') or {}
                    if name in ('plannerJobStart','plannerJobComplete','plannerJobPaused','plannerJobResumed','pathPlanned','actionComplete','plannerStatus','plannerResourceReleased') or name.startswith(('nativeNearReady','nativeHeat','potRescue','fryerRescue','earlyOnion','bakeryLease','cannonArrival','pantryChop','imminent')):
                        item={'frame':last,'name':name,'value':v};events.append(item)
                        if name=='plannerJobStart':
                            p=v['player'];boundary()
                            job={'number':len(jobs),'player':p,'name':v['name'],'start':v.get('frame',last),'end':None,'resources':v.get('resources',[]),'actions':v.get('actions',[]),'paths':[],'completedActions':[]}
                            jobs.append(job);active[p]=job;actionseq[p]=0
                        elif name=='plannerJobComplete':
                            p=v['player'];boundary();job=active.get(p)
                            if job and job['name']==v['name']:job['end']=v.get('frame',last);active.pop(p)
                        elif name=='actionComplete':
                            p=v.get('action',{}).get('player');job=active.get(p)
                            if p is not None:
                                if job:job['completedActions'].append({'frame':last,**v})
                                actionseq[p]+=1
                        elif name=='pathPlanned':
                            p=v.get('player');job=active.get(p)
                            if job:
                                key=(job['number'],actionseq[p]);first=key not in planned
                                planned.add(key);job['paths'].append({'frame':last,'actionIndex':actionseq[p],'firstForAction':first,**v})
    after=source.stat()
    if before.st_size!=after.st_size or before.st_mtime_ns!=after.st_mtime_ns:raise RuntimeError('Trace changed during closed-file review')
    data={'format':'closed-native-central-work-projection-v1','source':str(source.resolve()),'compressedBytes':before.st_size,
          'compressedSha256':compressed.hexdigest(),'uncompressedSha256':plain.hexdigest(),'records':records,'lastFrame':last,
          'qualification':'Exact events and job-boundary native observations. First planned paths are estimates, not measured displacement; replans retained separately. Job duration includes waiting and blocked interactions. Paused/nested jobs require individual attribution.',
          'jobs':jobs,'events':events,'boundaries':list(boundaries.values())}
    with gzip.open(output,'wt',encoding='utf-8') as f:json.dump(data,f,separators=(',',':'))
    print(json.dumps({'output':str(output),'jobs':len(jobs),'events':len(events),'boundaries':len(boundaries),'lastFrame':last,'compressedSha256':compressed.hexdigest()}))
if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('source',type=Path);p.add_argument('output',type=Path);a=p.parse_args();project(a.source,a.output)
