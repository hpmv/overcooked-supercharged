"""Operator-run plate micro-search using ordinary fresh rounds, never rewind.

Four typed candidates each get restart(seed0), loaded frame1 and30 warmup frames.
The selected exact emitted raw input is then repeated in TWO more fresh rounds.
Only plate preparation is scored. Raw state is preserved and compared separately;
absolute clocks, Unity identities and full raw parity are not normalized into a
success claim. Importing this module makes no connection.
"""
from __future__ import annotations

import argparse
import base64
import copy
import hashlib
import json
from pathlib import Path
import time

import framework_plate_search as plate
from framework_kitchen_planner import Observation, PlanningError
from framework_native_search import exact_values, require_native_boundary, native_clock_state
from framework_pause_boundary import observe_settled_pause, native_clock_fields
from framework_rpc import Client, ControllerClient
from compare_framework_frames import bits, first_difference, message_info
from framework_evidence_journal import EvidenceJournal


def require_fresh(state, receipt, expected_frame):
    native=require_native_boundary(receipt);bridge=receipt['bridge'];session=bridge['session']
    if (state.get('frame')!=expected_frame or state.get('preventInvalidState') is not False or
            state.get('invalidStateReason') or state.get('traceFailure') or state.get('needsFreshLevelBaseline')):
        raise PlanningError('Fresh round frame/control baseline is not the requested native boundary')
    if (session.get('scene')!='s_Day_3_4' or session.get('variantScene')!='s_Day_3_4' or session.get('dlc')!=8 or
            any(session.get(k)!=4 for k in ('variantPlayers','serverUsers','clientUsers','virtualPads')) or
            session.get('stage')!='kitchen_ready' or session.get('busy') or session.get('error')):
        raise PlanningError('Actual native Carnival four-player session is not ready')
    if native.get('configuredDuration')!=270 or native.get('timeLimit')!=270 or native.get('timerSuppressed'):
        raise PlanningError('Actual native unsuppressed270-second round required')
    rng=native.get('recipeRandom') or {}
    if rng.get('seed')!=0 or rng.get('generator')!='RoundData.GetNextRecipe' or rng.get('isolatedPerRound') is not True or rng.get('authoringWarpCount')!=0:
        raise PlanningError('Fresh native weighted seed0 recipe generator required')
    if not native.get('orders') or any(v!=0 for v in native['ledger'].values()):
        raise PlanningError('Fresh native orders and zero score/delivery ledger required')
    if state.get('requestedSeed') not in (None,0):
        raise PlanningError('Headless configured seed conflicts with native seed0')
    if any(row.get('actions') for row in state.get('actionGraph',{}).get('chefs',[])):
        raise PlanningError('An old action graph survived the normal fresh load')
    if state.get('typedActions',{}).get('active') or state.get('rawInput',{}).get('active'):
        raise PlanningError('Another input operation is active at fresh baseline')
    if expected_frame==1 and not 0<=native['elapsed']<.15:
        raise PlanningError('Fresh frame1 already contains unexplained native elapsed time')
    if expected_frame==31 and not .4<native['elapsed']<.7:
        raise PlanningError('Warmup31 boundary does not have the observed30-frame native duration')
    native_clock_state(bridge)
    observation=Observation(state,native,receipt)
    plate.require_native_physics(receipt,sorted(i for i,e in observation.entities.items() if e.get('chef') is not None))
    return observation


def restart_comparison_key(case, state, receipt):
    """Explicit fresh-round setup projection; no clocks or Unity instance IDs."""
    rng=receipt['bridge']['nativeRound']['recipeRandom']
    # Registration Pos is the historical pose at registration, not the actual
    # starting state after native warmup. Preserve it in raw_identity instead.
    # W's first two fresh rounds differ only in that historical chef Y, while
    # every observed frame31 pose, velocity, rotation and chef state is exact.
    metadata=[{k:v for k,v in e.items() if k!='Pos'} for e in state['initialRegistry']]
    fields=('id','path','name','className','prefab','exists','position','velocity','rotation','angularVelocity','chef')
    current={str(e['id']):{k:e[k] for k in fields} for e in state['entities']}
    return {'roles':{k:case[k] for k in ('source','target','plate','chefs')},
            'nativeRegistrationMetadata':metadata,'currentEntityGeometryAndChefs':current,
            'fixedParents':case['fixedParents'],'nativeFoodCompositions':case['nativeFoodCompositions'],
            'ledger':case['ledger'],'seed':rng['seed'],'generator':rng['generator'],
            'nativeRecipeDraws':rng['nativeRecipes'],'recipeFrequencies':rng['cumulativeFrequencies']}


def require_fenced_initial_clear(state, receipt):
    """A retained graph may be removed only before native input is armed."""
    bridge=receipt['bridge']
    if (state.get('frame')!=1 or state.get('state')!='Paused' or state.get('requestPending') or
            bridge.get('inputBlocked') is not True or bridge.get('holdPause') is not True or
            bridge.get('paused') is not True):
        raise PlanningError('Fresh action cleanup requires the native paused frame1 input fence')
    # Metadata alone is insufficient: reject a retained startup action which
    # has already moved the plate or put anything in any native chef's hands.
    native=require_native_boundary(receipt)
    case=plate.prepare_case(Observation(state,native,receipt))
    return restart_comparison_key(case,state,receipt)


def round_timers(receipt):
    n=receipt['bridge']['nativeRound']
    return {'elapsed':n['elapsed'],'remaining':n['remaining']}


def authoring_counters(receipt):
    b=receipt['bridge']
    return {'restoreAttempts':b['nativeCheckpoints']['restoreAttempts'],
            'clockRestores':b['authoringClockRestores']}


def raw_identity(state, receipt):
    b=receipt['bridge']
    return {'readyUnityFrame':b['readyUnityFrame'],'unityFrame':b['unityFrame'],'unityTime':b['unityTime'],
            'fixedTime':b['fixedTime'],'absoluteNativeClocks':native_clock_state(b),
            'nativePhysics':b['nativePhysics'],'entities':state['entities'],'historicalInitialRegistrations':state['initialRegistry'],
            'nativeRound':b['nativeRound'],'nativeFood':receipt['detail'],
            'frameworkAssembly':b['frameworkAssembly'],'authoringCounters':authoring_counters(receipt),
            'historicalHeadlessWarpUsed':state.get('warpUsed')}


class RoundTrace:
    """Retain one bounded round segment, plus the existing exact input extractor."""
    def __init__(self,path):
        self.path=Path(path);self.inputs=plate.AdvancingTrace(path)
        self.offset=self.inputs.offset;self.pending=b'';self.skip_partial=self.inputs.skip_partial;self.rows=[]

    def read(self):
        if self.path.stat().st_size<self.offset:
            raise PlanningError('Trace was replaced/truncated during a candidate')
        with self.path.open('rb') as stream:
            stream.seek(self.offset)
            while chunk:=stream.read(65536):
                self.offset+=len(chunk);self.pending+=chunk
                while b'\n' in self.pending:
                    line,self.pending=self.pending.split(b'\n',1)
                    if self.skip_partial:self.skip_partial=False;continue
                    if not line.strip():continue
                    row=json.loads(line)
                    if row.get('kind')=='paused-exchanges':
                        if row.get('version')!=1 or row.get('encoding')!='brotli-jsonl':
                            raise PlanningError('Unsupported paused trace block')
                        continue  # Advancing/control transitions are never packed by TraceStore.
                    if row.get('kind')=='exchange':self.rows.append(row)
                    if len(self.rows)>40000:raise PlanningError('Bounded candidate trace limit exceeded')
                if len(self.pending)>4*1024*1024:raise PlanningError('Oversized trace record')

    def complete(self,start,end,chefs,registry):
        self.read();inputs=self.inputs.frames(start,end,chefs)
        frames={};native_registry={str(e['EntityId']):e for e in registry}
        for row in self.rows:
            control=row.get('input') or {}
            if start<control.get('NextFrame',-1)<=end and control.get('PreventInvalidState') is True:
                raise PlanningError('Candidate input enabled native-state correction')
            output=row['output'];frame=output.get('FrameNumber')
            if output.get('LastFramePaused') is not False or not isinstance(frame,int) or not start<frame<=end:continue
            if frame in frames:raise PlanningError('Duplicate advancing output during one fresh candidate')
            if set(output.get('Chefs',{}))!={str(i) for i in chefs}:
                raise PlanningError('Native candidate frame lacks all four actual chef observations')
            if output.get('InvalidStateReason'):raise PlanningError(output['InvalidStateReason'])
            for entity in output.get('EntityRegistry') or []:native_registry[str(entity['EntityId'])]=entity
            infos=[message_info(m,native_registry) for m in output.get('ServerMessages') or []]
            if any(m['type'] in {5,6,31,36,38,44} for m in infos):
                raise PlanningError('Fixed plate candidate entered a native spawn/removal/load lifecycle')
            frames[frame]={'offset':frame-start,'output':output,'messages':infos}
        if set(frames)!=set(range(start+1,end+1)):
            raise PlanningError('Trace does not yet contain every advancing native output')
        return inputs,[frames[f] for f in sorted(frames)]


def native_effects(case, candidate, frames):
    """Decode native pickup/place groups and preserve every selected event byte."""
    projected=[];transfers=[]
    for frame in frames:
        projected_order=0
        for m in frame['messages']:
            if m.get('type')!=4 or m.get('nativeComponentType') not in {11,15,29,30}:continue
            projected.append(dict(m,frameOffset=frame['offset'],eventOrder=projected_order));projected_order+=1
            payload=base64.b64decode(m['bytes'],validate=True);kind=m['nativeComponentType']
            if kind==11: value=bits(payload,14,10)
            elif kind==15:value=bits(payload,15,10) if bits(payload,14,1) else 0
            elif kind==29:value=bits(payload,14,10)
            else:continue
            transfers.append((frame['offset'],m['entityId'],kind,value))
    chef=candidate['chef'];plate_id=case['plate'];source=case['source'];target=case['target']
    pickup=[];placed=[]
    for frame in sorted({r[0] for r in transfers}):
        observed={r[1:] for r in transfers if r[0]==frame}
        if {(plate_id,11,chef),(chef,29,plate_id),(source,15,0)}<=observed:pickup.append(frame)
        if {(plate_id,11,target),(chef,29,0),(target,15,plate_id)}<=observed:placed.append(frame)
    proof=bool(pickup and placed and min(pickup)<max(placed))
    return {'nativeTransferProven':proof,'pickupOffsets':pickup,'placementOffsets':placed,
            'gameplayEvents':projected,'decodedAttachmentEvents':transfers,
            'projectionDefinition':'All native PhysicalAttach(11), AttachStation(15), ChefCarry(29), and InputEvent(30) entity events, exact bytes and relative frame. Absolute clock/cosmetic/world events are preserved separately.'}


def gameplay_projection(case,state,receipt,base_receipt,effects):
    n=receipt['bridge']['nativeRound'];before=base_receipt['bridge']['nativeRound']
    return {'frame':state['frame'],'nativeSeconds':n['elapsed']-before['elapsed'],
            'remainingTimerDelta':n['remaining']-before['remaining'],
            'plate':case['plate'],'source':case['source'],'target':case['target'],
            'attachmentParents':{str(e['id']):(e.get('data',{}).get('attachmentParent') or {}).get('path',[]) for e in state['entities']},
            'attachmentChildren':{str(e['id']):(e.get('data',{}).get('attachment') or {}).get('path',[]) for e in state['entities']},
            'nativeFoodCompositions':{str(e['id']):e.get('composition') for e in receipt['detail']['entities']},
            'ledger':n['ledger'],'recipeDraws':n['recipeRandom']['nativeRecipes'],
            'nativeEvents':effects['gameplayEvents']}


def compare_repeat(expected, actual):
    inputs=first_difference(expected['inputs'],actual['inputs'],'$ordinaryInputs')
    gameplay=first_difference(expected['gameplay'],actual['gameplay'],'$freshRoundGameplayProjection')
    raw=first_difference(expected['raw'],actual['raw'],'$rawNativeState')
    messages=first_difference([f['messages'] for f in expected['frames']],[f['messages'] for f in actual['frames']],'$allRawNativeMessages')
    return {'passed':inputs is None and gameplay is None and actual['goal']['achieved'] and actual['effects']['nativeTransferProven'],
            'logicalInputsEqual':inputs is None,'inputFirstDifference':inputs,
            'gameplayProjectionEqual':gameplay is None,'gameplayFirstDifference':gameplay,
            'rawNativeStateEqual':raw is None,'rawNativeStateFirstDifference':raw,
            'allRawNativeMessagesEqual':messages is None,'allRawNativeMessagesFirstDifference':messages,
            'fullRawStateParityClaimed':False,'freshProcessClaimed':False,
            'classification':'Same-process fresh-round fixed-input gameplay repetition; raw differences retained without waiver or full-parity claim'}


def select_trial(trials):
    eligible=[t for t in trials if t['eligible']]
    if not eligible:raise PlanningError('No fresh-round candidate achieved the observed native plate goal')
    return min(eligible,key=lambda t:(t['goal']['nativeSeconds'],t['goal']['frames'],t['id']))


def persist_candidate_evidence(path,label,base,base_native,end,receipt,case,candidate,inputs,frames,raw=False):
    """Save observed facts before any goal/edge/replay-conversion rejection."""
    path=Path(path)
    result={'id':label,'validation':'pending','inputs':inputs,'frames':frames,
            'baseSnapshot':base,'baseNativeReceipt':base_native,'endSnapshot':end,'endNativeReceipt':receipt}
    def save():path.write_text(json.dumps(result,indent=2))
    save()
    try:
        result['effects']=native_effects(case,candidate,frames)
        result['input']=plate.input_report(inputs)
        result['raw']=raw_identity(end,receipt);result['initialRaw']=raw_identity(base,base_native)
        result['timers']={'base':round_timers(base_native),'end':round_timers(receipt)}
        o=Observation(end,receipt['bridge']['nativeRound'],receipt)
        if not raw:decision=plate.classify_candidate(case,candidate,o)
        else:
            goal=plate.check_goal(case,o)
            if end.get('rawInput',{}).get('outcome')!='complete':raise PlanningError('Selected raw input did not complete')
            decision={'goal':goal,'eligible':goal['achieved'],'outcome':'complete'}
        result.update(decision)
        if result['eligible'] and not result['effects']['nativeTransferProven']:
            raise PlanningError('Native endpoint lacks matching pickup/place event groups')
        result['gameplay']=gameplay_projection(case,end,receipt,base_native,result['effects'])
        save()  # The emitted rows and effect receipts survive conversion failure.
        result['rawRequest']=plate.to_raw_request(inputs,case['chefs']) if result['eligible'] else None
        result['validation']='passed'
    except Exception as error:
        result.update(validation='failed',validationError=str(error));raise
    finally:save()
    return result


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--out',type=Path,required=True);p.add_argument('--trace',type=Path,required=True)
    p.add_argument('--bridge-port',type=int,default=17636);p.add_argument('--controller-port',type=int,default=17637)
    p.add_argument('--maximum-frames',type=int,default=900)
    args=p.parse_args()
    if not 120<=args.maximum_frames<=1800:p.error('Use maximum-frames120..1800 for this bounded native search')
    args.out.mkdir(parents=True,exist_ok=False)
    bridge=host=None;records=EvidenceJournal(args.out/'observations.jsonl');began=time.monotonic();summary={'passed':False,'trials':[],'repeats':[],
        'classification':'Same-process fresh-round plate preparation micro-search',
        'nativeScoreAchievement':0,'freshProcessClaimed':False,'fullRewindParityClaimed':False,'fullRawStateParityClaimed':False}

    def save():
        summary['observationEvidence']=records.describe()
        (args.out/'summary.json').write_text(json.dumps(summary,indent=2))

    def call(target,request,label):
        response=(bridge if target=='bridge' else host).call(request)
        records.append({'target':target,'label':label,'request':request,'response':response,'wallSeconds':time.monotonic()-began})
        return response

    def settled(label,frame=None,timeout=100):
        deadline=time.monotonic()+timeout
        while True:
            s=host.status()
            if s.get('errors') or s.get('state')=='Error' or s.get('traceFailure'):raise PlanningError(json.dumps(s))
            if s.get('state')=='Paused' and not s.get('requestPending') and (frame is None or s.get('frame')==frame):
                return call('controller',{'command':'inspect','full':True},label)
            if time.monotonic()>deadline:raise PlanningError('Controller did not reach requested fresh/operation boundary: '+json.dumps(s))
            time.sleep(.05)

    def native(label):
        index=0
        def read_frame():
            s=host.status()
            if s.get('state')!='Paused' or s.get('requestPending') or s.get('errors') or s.get('traceFailure'):
                raise PlanningError('Native boundary lost its error-free pause')
            return s['frame']
        def read():
            nonlocal index
            value=call('bridge',{'command':'food'},f'{label}-raw-{index:03d}');index+=1;return value
        try:result=observe_settled_pause(read,read_frame)
        except Exception as error:
            (args.out/(label+'-pause-proof.json')).write_text(json.dumps(getattr(error,'report',None),indent=2));raise
        if native_clock_fields(result['receipt']) is None:raise PlanningError('Native private clocks are missing')
        (args.out/(label+'-pause-proof.json')).write_text(json.dumps(result['proof'],indent=2))
        records.append({'target':'settled-pause-observation','label':label,'response':result['receipt']})
        return result['receipt']

    reference=None;counter_baseline=None;previous_ready=None;expected_assembly=None
    def restart(label):
        nonlocal reference,counter_baseline,previous_ready,expected_assembly
        before=call('bridge',{'command':'status'},label+'-before')['bridge']
        if counter_baseline is None:counter_baseline=authoring_counters({'bridge':before})
        settled(label+'-before-clear')
        # Completed nodes intentionally survive this API at the old boundary.
        # Remove pending nodes now, then remove reset nodes behind the fresh
        # load's input fence before arm. Never pretend this first call empties it.
        call('controller',{'command':'actions-clear'},label+'-pre-restart-clear')
        call('bridge',{'command':'restart','seed':0},label+'-restart')
        deadline=time.monotonic()+160
        while True:
            b=call('bridge',{'command':'status'},label+'-load')['bridge']
            if b.get('lastError') or b.get('session',{}).get('error'):raise PlanningError(json.dumps(b))
            if b.get('loadComplete') and not b.get('loading') and b.get('paused') and b.get('readyUnityFrame',-1)>before.get('readyUnityFrame',-1):break
            if time.monotonic()>deadline:raise PlanningError('Normal native restart did not finish')
            time.sleep(.1)
        initial=settled(label+'-initial',1);initial_native=native(label+'-initial-native')
        initial_key=require_fenced_initial_clear(initial,initial_native)
        call('controller',{'command':'actions-clear'},label+'-fenced-initial-clear')
        cleared=settled(label+'-initial-cleared',1);cleared_native=native(label+'-initial-cleared-native')
        if initial_key!=require_fenced_initial_clear(cleared,cleared_native):
            raise PlanningError('Native initial layout changed during fenced action cleanup')
        for name,left,right in (('entities',initial['entities'],cleared['entities']),
                                ('nativeRound',initial_native['bridge']['nativeRound'],cleared_native['bridge']['nativeRound']),
                                ('nativeFood',initial_native['detail']['entities'],cleared_native['detail']['entities'])):
            if not exact_values(left,right):raise PlanningError('Native '+name+' changed during fenced action cleanup')
        initial,initial_native=cleared,cleared_native
        require_fresh(initial,initial_native,1)
        ready=initial_native['bridge']['readyUnityFrame']
        if previous_ready is not None and ready<=previous_ready:raise PlanningError('A new normal same-process round was not observed')
        previous_ready=ready
        call('bridge',{'command':'arm'},label+'-arm');call('controller',{'command':'step','frames':30},label+'-warmup')
        base=settled(label+'-base',31);receipt=native(label+'-base-native')
        o=require_fresh(base,receipt,31);case=plate.prepare_case(o,args.maximum_frames)
        key=restart_comparison_key(case,base,receipt)
        if reference is None:reference=key
        elif not exact_values(reference,key):raise PlanningError('Fresh native role/initial-layout/recipe baseline differs')
        if authoring_counters(receipt)!=counter_baseline:raise PlanningError('An authoring restore occurred during fresh-round search')
        assembly=Path(receipt['bridge']['frameworkAssembly']);identity={'path':str(assembly),'sha256':hashlib.sha256(assembly.read_bytes()).hexdigest()}
        if expected_assembly is None:expected_assembly=identity
        elif expected_assembly!=identity:raise PlanningError('Framework assembly file changed between fresh rounds')
        (args.out/(label+'-case.json')).write_text(json.dumps(case,indent=2))
        return base,receipt,case

    def perform(label,base,base_native,case,candidate,raw=None):
        cursor=RoundTrace(args.trace)
        call('controller',raw or candidate['request'],label+'-request')
        end=settled(label+'-end');receipt=native(label+'-native')
        cursor.read();cursor.inputs.read()
        # Retain even malformed/unaccepted emitted rows before trace validation.
        capture={'request':raw or candidate['request'],'baseSnapshot':base,'baseNativeReceipt':base_native,
                 'endSnapshot':end,'endNativeReceipt':receipt,'exchanges':cursor.rows,'inputExchanges':cursor.inputs.rows}
        (args.out/(label+'-raw-capture.json')).write_text(json.dumps(capture,indent=2))
        if authoring_counters(receipt)!=counter_baseline:raise PlanningError('An authoring restore occurred during the candidate')
        deadline=time.monotonic()+3
        while True:
            try:inputs,frames=cursor.complete(base['frame'],end['frame'],case['chefs'],base['registry']);break
            except PlanningError as error:
                if 'does not yet contain' not in str(error) or time.monotonic()>deadline:raise
                time.sleep(.025)
        return persist_candidate_evidence(args.out/(label+'-evidence.json'),label,base,base_native,end,receipt,
                                          case,candidate,inputs,frames,raw is not None)

    try:
        bridge=Client(args.bridge_port);host=ControllerClient(args.controller_port)
        call('bridge',{'command':'render','fps':60},'render')
        trials=[];candidate_ids=None
        for index in range(4):
            label=f'candidate-{index}'
            base,receipt,case=restart(label)
            if candidate_ids is None:candidate_ids=[c['id'] for c in case['candidates']]
            candidate=next(c for c in case['candidates'] if c['id']==candidate_ids[index])
            trial=perform(candidate['id'],base,receipt,case,candidate)
            trial['candidateId']=candidate['id'];trials.append(trial)
            summary['trials'].append({k:trial[k] for k in ('id','eligible','goal','outcome','input','timers')});save()
        selected=select_trial(trials);baseline=trials[0]
        summary.update(selected=selected['id'],baseline=baseline['id'],baselineAchieved=baseline['eligible'],
                       measuredNativeSecondsSaved=baseline['goal']['nativeSeconds']-selected['goal']['nativeSeconds'] if baseline['eligible'] else None,
                       measuredFramesSaved=baseline['goal']['frames']-selected['goal']['frames'] if baseline['eligible'] else None,
                       selectedNativeGoal=selected['goal'],frameworkAssembly=expected_assembly)
        (args.out/'selected-raw-request.json').write_text(json.dumps(selected['rawRequest'],indent=2))
        for repeat in range(2):
            label=f'selected-fresh-repeat-{repeat}'
            base,receipt,case=restart(label)
            candidate=next(c for c in case['candidates'] if c['id']==selected['candidateId'])
            actual=perform(label,base,receipt,case,candidate,selected['rawRequest'])
            compared=compare_repeat(selected,actual);summary['repeats'].append(compared);save()
        summary['passed']=all(r['passed'] for r in summary['repeats']) and len(summary['repeats'])==2
        summary['measuredImprovementEstablished']=bool(summary['passed'] and baseline['eligible'] and summary['measuredNativeSecondsSaved']>0)
        summary['scope']='Six normal rounds in one process: four actual candidates and two exact selected raw-input repeats. Native score remains0. No warp command, fresh-process or full raw-state parity claim.'
    except Exception as error:summary['error']=str(error)
    finally:
        try:
            if bridge is not None:call('bridge',{'command':'pause'},'finally-pause')
        except Exception as error:summary.update(passed=False,pauseError=str(error))
        for client in (bridge,host):
            try:
                if client is not None:client.close()
            except Exception as error:summary.update(passed=False,closeError=str(error))
        try:records.finalize(args.out/'observations.json')
        except Exception as error:summary.update(passed=False,evidenceFinalizationError=str(error))
        save();print(json.dumps(summary,indent=2))
    return 0 if summary['passed'] else 1


if __name__=='__main__':raise SystemExit(main())
