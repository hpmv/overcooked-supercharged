"""File-only proof of one native fixed-plate candidate, not search completion."""
import argparse
import base64
import hashlib
import json
from pathlib import Path

from compare_framework_frames import bits, first_difference, message_info, loads
from framework_plate_search import digest, normalize_inputs, to_raw_request, input_report


def require(condition,message):
    if not condition:raise ValueError(message)


def sha(path):
    with Path(path).open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()


def verify(folder:Path,candidate='chef-103-walk'):
    evidence=json.loads((folder/'observations.json').read_text())
    summary=json.loads((folder/'summary.json').read_text())
    case=json.loads((folder/'case.json').read_text())
    saved=json.loads((folder/f'{candidate}-inputs.json').read_text())
    sliced=json.loads((folder/'achievement-trace-slice.json').read_text())
    def observed(label):
        matches=[e['response']for e in evidence if e['label']==label]
        require(len(matches)==1,'Observation label is not unique: '+label)
        return matches[0]
    base,end=observed('base'),observed(candidate+'-end')
    before,after=observed('base-native'),observed(candidate+'-native')
    first,last=base['frame'],end['frame']
    source,target,plate=case['source'],case['target'],case['plate']
    actor=next(c['chef']for c in case['candidates']if c['id']==candidate)
    chefs=case['chefs'];start_entities={e['id']:e for e in base['entities']};end_entities={e['id']:e for e in end['entities']}
    require(first==case['frame'] and last>first and len(saved)==last-first,'Candidate frame count mismatch')
    require(start_entities.keys()==end_entities.keys(),'Native inventory changed')
    for entity in start_entities:
        a,b=start_entities[entity],end_entities[entity]
        require(a['path']==b['path'] and a['className']==b['className'] and a['exists'] and b['exists'],'Fixed native identity changed')
    def parent(entities,e):return entities[e].get('data',{}).get('attachmentParent',{}).get('path')
    def held(entities,e):return entities[e].get('data',{}).get('attachment',{}).get('path')
    require(parent(start_entities,plate)==[source] and held(start_entities,source)==[plate] and held(start_entities,target)is None,'Initial exact source/plate pair missing')
    require(parent(end_entities,plate)==[target] and held(end_entities,target)==[plate] and held(end_entities,source)is None,'Final exact target/plate pair missing')
    for entities,station in ((start_entities,source),(end_entities,target)):
        require(all(entities[plate]['position'][axis]==entities[station]['position'][axis]for axis in ('x','z')),'Plate transform is not at its observed attachment station')
    require(all(held(end_entities,c)is None for c in chefs),'A chef remains holding an item')
    for entity in start_entities.keys()-{plate}:
        require(parent(start_entities,entity)==parent(end_entities,entity),'Another native attachment changed')
    def foods(receipt):return {e['id']:e['composition']for e in receipt['detail']['entities']}
    require(first_difference(foods(before),foods(after))is None,'Native food composition changed')
    require(foods(after)[plate]=={'type':'CompositeAssembledNode','children':[],'optional':[]},'Exact plate is not clean')
    nr0,nr1=before['bridge']['nativeRound'],after['bridge']['nativeRound']
    require(nr0['available'] and nr1['available'] and nr0['configuredDuration']==nr1['configuredDuration']==270,'Native round configuration changed')
    require(nr0['ledger']==nr1['ledger'] and nr1['ledger']['deliveries']==0,'Native ledger changed or a delivery was claimed')
    require(nr0['recipeRandom']==nr1['recipeRandom'],'Native recipe generator changed during this short candidate')
    require(after['bridge']['session']['variantPlayers']==4,'Not the actual four-player variant')
    duration=nr1['elapsed']-nr0['elapsed'];require(duration>0,'Native duration did not advance')
    for label in ('base-native',candidate+'-native'):
        proof=json.loads((folder/f'{label}-pause-boundary.json').read_text())
        require(proof['passed'] and proof['observations'][-1]['stableDistinctPhysicsTicks']>=2,'Settled pause proof failed')
        require(first_difference(proof['observations'][-1]['receipt'],observed(label))is None,'Endpoint is not bound to its actual final pause receipt')
    plan=end['typedActions']
    require(plan['outcome']=='complete' and not plan['active'] and plan['error']is None,'Typed graph did not complete')
    require(all(a['startFrame']is not None and a['endFrame']is not None for a in plan['actions']),'A typed action is incomplete')
    registry={str(r['EntityId']):r for r in base['registry']}
    native_events=[];wire_inputs=[];advancing=[]
    for entry in sliced:
        row=entry['row'];require(row.get('kind')=='exchange','Non-exchange in candidate slice')
        o,i=row['output'],row.get('input')or{}
        require(i.get('Warp')is None and not i.get('PreventInvalidState',False) and not i.get('__isset',{}).get('resetOrderSeed',False),'Authoring/correction directive inside candidate')
        frame=i.get('NextFrame')
        if type(frame)is int and first<frame<=last and i.get('Input')is not None:
            wire_inputs.append({'ordinal':frame-first-1,'nextFrame':frame,'inputs':normalize_inputs(i['Input'],chefs)})
        if o.get('LastFramePaused')is False and first<o['FrameNumber']<=last:advancing.append(o['FrameNumber'])
        for m in o.get('ServerMessages')or[]:
            require(m.get('Type')not in {5,6,31,36,38,44},'Native entity spawn/removal inside the fixed-incarnation candidate')
            info=message_info(m,registry)
            if info.get('entityId')not in {plate,source,target,actor} or info.get('nativeComponentType')not in {11,15,29}:continue
            data=base64.b64decode(info['bytes']);kind=info['nativeComponentType']
            decoded={'parent':bits(data,14,10)}if kind==11 else {'item':bits(data,15,10)if bits(data,14,1)else 0}if kind==15 else {'item':bits(data,14,10),'attachTarget':bits(data,24,2)}
            native_events.append(dict(info,frame=o['FrameNumber'],location=entry['location'],decoded=decoded))
    require(advancing==list(range(first+1,last+1)),'Actual advancing observations are not contiguous')
    require(first_difference(wire_inputs,saved)is None,'Saved input rows differ from actual wire')
    require([r['nextFrame']for r in saved]==list(range(first+1,last+1)),'Input tags are not contiguous')
    raw=to_raw_request(saved,chefs)
    require(sum(s['frames']for s in raw['segments'])+2==len(saved),'Payload plus native release tail is not exact')
    for r in saved:
        for chef,pad in r['inputs'].items():
            require(not pad['Interact']['Down'] and not pad['Dash']['Down'],'Unexpected use/dash in walking plate candidate')
            if int(chef)!=actor:require(all(v==0 for v in pad['Pad'].values()) and not pad['Pickup']['Down'],'Another chef was controlled')
    def event(entity,kind,field,value):return [e for e in native_events if e['entityId']==entity and e['nativeComponentType']==kind and e['decoded'].get(field)==value]
    pickup=event(plate,11,'parent',actor);placement=event(plate,11,'parent',target)
    require(len(pickup)==len(placement)==1 and pickup[0]['frame']<placement[0]['frame'],'Native plate parent sequence missing or duplicated')
    pf,qf=pickup[0]['frame'],placement[0]['frame']
    require(any(e['frame']==pf for e in event(actor,29,'item',plate)) and any(e['frame']==pf for e in event(source,15,'item',0)),'Native pickup lacks matching chef/source messages')
    require(any(e['frame']==qf for e in event(actor,29,'item',0)) and any(e['frame']==qf for e in event(target,15,'item',plate)),'Native placement lacks matching chef/target messages')
    presses=[r['nextFrame']for r in saved if r['inputs'][str(actor)]['Pickup']['JustPressed']]
    require(presses==[pf-1,qf-1],'Native transfer events do not follow the captured input edges')
    # Bind the decoded slice to an immutable byte prefix of the original trace.
    # The session may append more paused receipts; its later tail is irrelevant.
    start_line=sliced[0]['location']['line'];end_line=sliced[-1]['location']['line']
    require([e['location']for e in sliced]==[{'line':n}for n in range(start_line,end_line+1)],'Candidate slice is not contiguous original direct trace rows')
    hasher=hashlib.sha256();length=0;count=0
    with (folder.parent/'exchange.jsonl').open('rb')as stream:
        for line,rawline in enumerate(stream,1):
            require(rawline.endswith(b'\n'),'Incomplete source trace prefix')
            hasher.update(rawline);length+=len(rawline)
            if start_line<=line<=end_line:
                require(first_difference(loads(rawline),sliced[count]['row'])is None,'Decoded candidate slice differs from actual native trace line');count+=1
            if line==end_line:break
    require(count==len(sliced),'Source trace prefix is missing')
    repo=Path(__file__).resolve().parents[1]
    game=json.loads((folder.parent/'game-process.json').read_text());host=json.loads((folder.parent/'host-process.json').read_text())
    plugin=repo/'artifacts/framework-plugin-native-s/SuperchargedPatch.dll';headless=repo/'artifacts/framework-headless-host-v9/Headless.dll'
    require(sha(plugin)==game['pluginHash'].lower() and sha(headless)==host['controllerHash'].lower(),'Frozen native/host binaries differ from execution receipts')
    rollback=summary.get('failedRestoreComparison')
    require(summary.get('passed')is False and len(summary['trials'])==1 and rollback and rollback['changedEntityIds']==[plate],'This checker is scoped to S one-candidate success followed by failed rollback')
    return {'candidateAchieved':True,'searchCompleted':False,'optimizedWinnerEstablished':False,'exactReplayVerified':False,
            'classification':'One native fixed-plate preparation candidate; authoring search stopped on strict rollback mismatch',
            'candidate':candidate,'startFrame':first,'endFrame':last,'frames':len(saved),'nativeSeconds':duration,
            'plate':plate,'source':source,'target':target,'chef':actor,'nativePickupFrame':pf,'nativePlacementFrame':qf,
            'input':input_report(saved),'nativeLedger':nr1['ledger'],'exactCleanPlateAndFixedInventoryPreserved':True,
            'nativeEvents':native_events,'actionTimings':plan['actions'],'failedRestoreComparison':rollback,
            'tracePrefix':{'path':str((folder.parent/'exchange.jsonl').resolve()),'throughLine':end_line,'bytes':length,'sha256':hasher.hexdigest(),'candidateStartLine':start_line},
            'pluginSha256':sha(plugin),'headlessSha256':sha(headless),'gamePid':game['pid'],'hostPid':host['pid'],
            'files':{name:sha(folder/name)for name in ['observations.json','summary.json','case.json',f'{candidate}-inputs.json','achievement-trace-slice.json','base-native-pause-boundary.json',f'{candidate}-native-pause-boundary.json']},
            'preparedRawRequest':{'executed':False,'payloadFrames':len(saved)-2,'automaticReleaseFrames':2,'totalFrames':len(saved),
                                  'canonicalRequestSha256':digest(raw),'prerequisite':'Operator must establish and verify the intended native frame31 baseline in the target session; S rollback is not accepted.'}},raw


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--folder',type=Path,required=True);args=p.parse_args()
    proof,raw=verify(args.folder)
    request_path=args.folder/'chef-103-walk-prepared-raw-request.json'
    request_path.write_text(json.dumps(raw,indent=2)+'\n')
    proof['preparedRawRequest'].update(path=str(request_path.resolve()),fileSha256=sha(request_path))
    (args.folder/'single-candidate-achievement.json').write_text(json.dumps(proof,indent=2)+'\n')
    print(json.dumps({k:proof[k]for k in ['candidateAchieved','searchCompleted','frames','nativeSeconds','nativePickupFrame','nativePlacementFrame','exactReplayVerified']},indent=2))


if __name__=='__main__':main()
