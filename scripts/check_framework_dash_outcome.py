"""File-only acceptance audit for the bounded four-chef native dash probe.

Distance and changed endpoints are not dash evidence. Each requested chef needs
an actual post-edge positive native DashTimer transition or a decoded native
ServerPlayerControlsImpl_Default.StartDash/InputEvent receipt during payload.
Rewind parity is checked independently; repeat this outcome audit for each epoch.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import math
from pathlib import Path

from compare_framework_frames import TraceError, bits, selected_epochs


def expected_inputs(route):
    if route.get('command') != 'raw-input' or len(route.get('segments', [])) != 2:
        raise TraceError('Expected the explicit two-segment four-chef dash fixture')
    first, rest = route['segments']
    if first.get('frames') != 1 or rest.get('frames') != 25:
        raise TraceError('Dash fixture must contain one edge frame then25 movement frames')
    chefs = sorted(first.get('chefs', {}))
    if len(chefs) != 4 or set(rest.get('chefs', {})) != set(chefs):
        raise TraceError('Exactly four matching explicit chef pads required')
    directions = {}
    for chef in chefs:
        a, b = first['chefs'][chef], rest['chefs'][chef]
        if (set(a) != {'x','y','pickup','interact','dash'} or set(b) != set(a) or
                type(a['x']) not in (int,float) or a['x'] not in (-1,1) or a['y'] != 0 or
                a['pickup'] is not False or a['interact'] is not False or a['dash'] is not True or
                b != dict(a, dash=False)):
            raise TraceError('Only cardinal walking plus one ordinary dash edge is allowed')
        directions[chef] = a['x']
    previous = {chef: {'Pickup':False,'Interact':False,'Dash':False} for chef in chefs}
    frames=[]
    for frame in range(28):
        pads={}
        for chef in chefs:
            down={'Pickup':False,'Interact':False,'Dash':frame==0}
            pads[chef]={'Pad':{'X':directions[chef] if frame<26 else 0,'Y':0}}
            for button,value in down.items():
                pads[chef][button]={'Down':value,'JustPressed':value and not previous[chef][button],
                                    'JustReleased':previous[chef][button] and not value}
            previous[chef]=down
        frames.append(pads)
    return chefs, frames


def native_dash_events(frame):
    result=[]
    for index, message in enumerate(frame['nativeMessages']):
        if message.get('type') != 4 or message.get('nativeComponentType') != 30:
            continue
        payload=base64.b64decode(message['bytes'],validate=True)
        subtype=bits(payload,14,10)
        if subtype != 0:  # DashCollision(1), Catch(2), etc. are not StartDash.
            continue
        if len(payload)!=5 or bits(payload,24,10)!=0 or bits(payload,34,6)!=0:
            raise TraceError('Malformed native Dash InputEvent payload')
        result.append({'chef':str(message['entityId']),'nativeEventIndex':index,
                       'componentIndex':message['componentIndex'],'bytes':message['bytes']})
    return result


def timer(state):
    value=state.get('DashTimer')
    if type(value) not in (int,float) or not math.isfinite(value):
        raise TraceError('Missing/nonfinite actual native chef DashTimer')
    return value


def audit(epoch, route, start):
    chefs, expected=expected_inputs(route)
    if set(epoch['boundary']['chefs']) != set(chefs):
        raise TraceError('Route chef IDs do not match four actual native observed chefs')
    payload_end,end=start+26,start+28
    if set(epoch['frames'])!=set(range(start+1,end+1)) or set(epoch['inputs'])!=set(epoch['frames']):
        raise TraceError('All26 payload and2release advancing inputs/outputs are required')
    previous={c:timer(epoch['boundary']['chefs'][c]) for c in chefs}
    if any(v>0 for v in previous.values()):
        raise TraceError('A chef was already dashing at the selected boundary')
    report={c:{'edgeFrame':start+1,'baselineDashTimer':previous[c],
               'positiveTimerTransitions':[],'nativeStartDashEvents':[],'timerTimeline':[]} for c in chefs}
    for offset,number in enumerate(range(start+1,end+1)):
        frame,control=epoch['frames'][number],epoch['inputs'][number]['value']
        if (control.get('NextFrame')!=number or control.get('Warp') is not None or
                control.get('ResetOrderSeed') is not None or control.get('PreventInvalidState') is True):
            raise TraceError('Advancing input has a changed frame or non-input state directive')
        actual=control.get('Input') or {}
        if set(actual)!=set(chefs):
            raise TraceError('Advancing request lacks the exact four native pads')
        for chef in chefs:
            for field,value in expected[offset][chef].items():
                observed=actual[chef].get(field)
                # Thrift __isset is transport metadata, not a native button bit.
                if isinstance(observed,dict):observed={k:v for k,v in observed.items() if k!='__isset'}
                if not isinstance(observed,dict) or (field!='Pad' and any(type(v) is not bool for v in observed.values())):
                    raise TraceError('Explicit native pad/button receipt types are required')
                if field=='Pad' and any(type(v) not in (int,float) or not math.isfinite(v) for v in observed.values()):
                    raise TraceError('Finite native input axes are required')
                if observed!=value:
                    raise TraceError(f'Input mismatch at native frame{number}, chef{chef}, {field}: {observed!r}')
            current=timer(frame['chefs'][chef])
            report[chef]['timerTimeline'].append({'frame':number,'value':current,'location':frame['location']})
            if number<=payload_end and current>0 and current>previous[chef]:
                report[chef]['positiveTimerTransitions'].append({'frame':number,'before':previous[chef],
                    'after':current,'framesAfterEdge':number-(start+1),'location':frame['location']})
            previous[chef]=current
        for event in native_dash_events(frame):
            if event['chef'] not in report:
                raise TraceError('Unexpected native chef dash event')
            if number<=payload_end:
                report[event['chef']]['nativeStartDashEvents'].append(dict(event,frame=number,
                    framesAfterEdge=number-(start+1),location=frame['location']))
    for chef, value in report.items():
        value['accepted']=bool(value['positiveTimerTransitions'] or value['nativeStartDashEvents'])
        value['evidenceKind']=('timer-and-native-event' if value['positiveTimerTransitions'] and value['nativeStartDashEvents'] else
                               'positive-native-timer' if value['positiveTimerTransitions'] else
                               'native-StartDash-event' if value['nativeStartDashEvents'] else 'no-native-dash-acceptance')
        value['endpointDashTimer']=previous[chef]
    return {'passed':all(v['accepted'] for v in report.values()),
            'classification':'native dash acceptance only; not replay or distance proof',
            'epoch':epoch['epoch'],'startExclusive':start,'payloadEndInclusive':payload_end,'endInclusive':end,
            'payloadFrames':26,'automaticNeutralReleaseFrames':2,'allInputsMatchFixture':True,
            'chefs':report,'missingDashAcceptance':[c for c,v in report.items() if not v['accepted']],
            'scope':'A collision may reset DashTimer before a snapshot; its exact native StartDash event still proves acceptance. No distance threshold or endpoint-only change substitutes for either receipt.'}


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--trace',type=Path,required=True)
    p.add_argument('--route',type=Path,default=Path('routes/probes/framework-four-chef-dash.json'))
    p.add_argument('--epoch',type=int,required=True)
    p.add_argument('--start',type=int,required=True)
    p.add_argument('--out',type=Path,required=True)
    args=p.parse_args();result={'passed':False}
    try:
        before=args.trace.stat()
        route=json.loads(args.route.read_text(encoding='utf-8-sig'));expected_inputs(route)
        epoch=selected_epochs(args.trace,{args.epoch},args.start,args.start+28)[args.epoch]
        result=audit(epoch,route,args.start)
        with args.trace.open('rb') as stream: trace_hash=hashlib.file_digest(stream,'sha256').hexdigest()
        after=args.trace.stat()
        if (before.st_size,before.st_mtime_ns)!=(after.st_size,after.st_mtime_ns):
            raise TraceError('Trace changed during audit; close it before checking')
        result['inputFiles']={str(args.trace.resolve()):trace_hash,
                              str(args.route.resolve()):hashlib.sha256(args.route.read_bytes()).hexdigest()}
    except Exception as error:
        result.update(passed=False,error=str(error))
    args.out.parent.mkdir(parents=True,exist_ok=True)
    args.out.write_text(json.dumps(result,indent=2,allow_nan=False)+'\n')
    print(json.dumps({'passed':result['passed'],'error':result.get('error'),
                      'missingDashAcceptance':result.get('missingDashAcceptance'),'out':str(args.out)}))
    return 0 if result['passed'] else 1


if __name__=='__main__':raise SystemExit(main())
