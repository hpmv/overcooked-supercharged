"""Attribute nested completion/start events without rereading native call telemetry."""
import collections,gzip,json,re
from pathlib import Path

def family(name):
    for prefix in ('assemble-meal-','unplated-hotdog-','direct-early-onion-harvest-','early-onion-','finish-early-onion-','rescue-cooked-pot-','restore-pot-','rescue-cooked-fryer-','restore-fryer-','fire-','interrupt-fire-','recover-washer-plated-meal-','stage-unplated-for-washer-'):
        if name.startswith(prefix):return prefix.rstrip('-')
    return name

def rebuild(events):
    jobs=[];active={}
    for e in events:
        n=e['name'];v=e['value'];gf=e['frame'];p=v.get('player')
        if n=='plannerJobStart':
            j={'number':len(jobs),'player':p,'name':v['name'],'family':family(v['name']),'start':v.get('frame',gf),'end':None,'pausedFrames':0,'actions':v.get('actions',[]),'resources':v.get('resources',[]),'paths':[],'completedActions':[],'actionIndex':0}
            jobs.append(j);active[p]=j
        elif n=='plannerJobComplete':
            pending=[j for j in jobs if j['player']==p and j['name']==v['name'] and j['end'] is None]
            if not pending:continue
            j=pending[-1];j['end']=v.get('frame',gf)
            if active.get(p) is j:active.pop(p)
        elif n=='plannerJobPaused':
            j=active.get(p)
            if j and j['name']==v['name']:j['pausedAt']=v.get('frame',gf)
        elif n=='plannerJobResumed':
            pending=[j for j in jobs if j['player']==p and j['name']==v['name'] and j['end'] is None]
            if pending:
                j=pending[-1];j['pausedFrames']+=v.get('pausedFrames',0);active[p]=j
        elif n=='pathPlanned':
            j=active.get(p)
            if j:j['paths'].append({'frame':gf,'actionIndex':j['actionIndex'],'firstForAction':not any(path['actionIndex']==j['actionIndex'] for path in j['paths']),**v})
        elif n=='actionComplete':
            p=v.get('action',{}).get('player');j=active.get(p)
            if j:j['completedActions'].append({'frame':gf,**v});j['actionIndex']+=1
    return jobs

for version in (16,17):
    source=Path(f'artifacts/v{version}-central-work-projection.json.gz');d=json.load(gzip.open(source,'rt'));jobs=rebuild(d['events']);groups={}
    for j in jobs:
        if j['player'] not in (0,3) or j['end'] is None:continue
        j['nativeFrames']=j['end']-j['start']-j['pausedFrames'];j['firstPlanDistance']=sum(p['path']['Length'] for p in j['paths'] if p['firstForAction'] and p['path']['Success'])
        j['allPlanDistance']=sum(p['path']['Length'] for p in j['paths'] if p['path']['Success'])
        g=groups.setdefault(j['family'],{'jobs':0,'nativeFrames':0,'firstPlanDistance':0,'actionFrames':collections.Counter(),'players':collections.Counter()})
        g['jobs']+=1;g['nativeFrames']+=j['nativeFrames'];g['firstPlanDistance']+=j['firstPlanDistance'];g['players'][j['player']]+=j['nativeFrames']
        for a in j['completedActions']:g['actionFrames'][a['action']['type']]+=a['frames']
    output={'source':d['source'],'sourceSha256':d['compressedSha256'],'lastFrame':d['lastFrame'],'qualification':'Planner job/action occupancy, subtracting explicitly paused nested firing time; planned path lengths are not actual displacement or saved time.',
            'groups':dict(sorted(groups.items(),key=lambda kv:-kv[1]['nativeFrames'])),'jobs':jobs}
    Path(f'artifacts/v{version}-central-work-analysis.json').write_text(json.dumps(output,indent=2),encoding='utf-8')
    print(version,[(k,v['jobs'],round(v['nativeFrames']/60,2),round(v['firstPlanDistance'],2)) for k,v in output['groups'].items()]);print('open',[(j['player'],j['name'],j['start']) for j in jobs if j['end'] is None])
