"""Captured native metadata/absence predicates; these tests make no game calls."""
import copy
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest

from framework_story11_registry import RegistryEvidence, world_proof
from framework_story11_search import Runner, resume_case
from framework_story11_planner import Blocked, DeliveryPlanner, Observation


REPOSITORY=Path(__file__).resolve().parents[1]
CAPTURE_FIXTURE=Path('artifacts/framework-migration/story11-first-delivery-b')
ROOT=REPOSITORY if (REPOSITORY/CAPTURE_FIXTURE).exists() else REPOSITORY.parent


def captured(name):
    folder=ROOT/'artifacts/framework-migration'/name
    rows=json.loads((folder/'observations.json').read_text())
    state=next(x['response'] for x in reversed(rows) if x['target']=='controller' and x.get('request',{}).get('command')=='inspect')
    receipt=next(x['response'] for x in reversed(rows) if x['target']=='settled-pause-observation')
    return state,receipt


class RegistryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.raw,cls.raw_receipt=captured('story11-first-delivery-b')
        cls.chopped,cls.chopped_receipt=captured('story11-first-delivery-c')

    def test_actual_raw_refresh_then_exact_dynamic_absence(self):
        tracker=RegistryEvidence()
        request=tracker.requests(self.raw,self.raw_receipt)
        self.assertEqual([(r['operation'],r['args']) for r in request],[('refresh',{'entityId':51})])
        self.assertEqual(tracker.capture(self.raw,self.raw_receipt)[0]['bodyInstanceId'],-435166)
        absent=tracker.requests(self.chopped,self.chopped_receipt)
        self.assertEqual([(r['operation'],r['args']) for r in absent],
            [('observe-absent',{'entityId':52,'sceneMetadataRefreshes':28,'priorBodyInstanceId':-435166})])
        self.assertEqual(absent[0]['prior']['logicalPath'],[30,0])
        self.assertEqual(absent[0]['prior']['ownerId'],51)

    def test_new_scene_connection_or_ready_epoch_never_reuses_proxy(self):
        for key in ('sceneMetadataRefreshes','readyUnityFrame','connectionEpoch'):
            tracker=RegistryEvidence();tracker.capture(self.raw,self.raw_receipt)
            receipt=copy.deepcopy(self.chopped_receipt)
            if key=='connectionEpoch':receipt['bridge']['inputExchange'][key]+=1
            else:receipt['bridge'][key]+=1
            self.assertEqual(tracker.requests(self.chopped,receipt),[])

    def test_verified_warp_rebranches_proxy_incarnations_within_native_epoch(self):
        tracker=RegistryEvidence();tracker.capture(self.raw,self.raw_receipt)
        restored,receipt=copy.deepcopy(self.raw),copy.deepcopy(self.raw_receipt)
        restored['registryWarpRebranch']={'active':True,'frame':restored['frame'],'nativeStateChanged':False}
        proxy=restored['graphMappingValidation']['observedPhysicalContainers'][0]['nativeId']
        body=next(b for b in receipt['bridge']['nativePhysics']['bodies'] if b['entityId']==proxy)
        body['bodyInstanceId']-=100000
        proof=tracker.rebranch_after_verified_warp(restored,receipt)
        self.assertEqual(proof['previousProxyIds'],[proxy])
        self.assertEqual(proof['currentProxyIds'],[proxy])
        self.assertEqual(tracker.proxies[proxy]['bodyInstanceId'],body['bodyInstanceId'])
        bad=copy.deepcopy(receipt)
        next(b for b in bad['bridge']['nativePhysics']['bodies'] if b['entityId']==proxy)['bodyInstanceId']-=1
        with self.assertRaises(Blocked):tracker.capture(restored,bad)

        for key,value in [('active',False),('frame',restored['frame']+1),('nativeStateChanged',True)]:
            invalid=copy.deepcopy(restored);invalid['registryWarpRebranch'][key]=value
            with self.subTest(key=key),self.assertRaises(Blocked):
                tracker.rebranch_after_verified_warp(invalid,receipt)

    def test_live_owner_reused_body_stale_frame_and_missing_proxy_mapping_reject(self):
        for mutation in ('owner','body','frame','mapping'):
            tracker=RegistryEvidence();state=copy.deepcopy(self.raw)
            if mutation=='mapping':
                state['graphMappingValidation']['observedPhysicalContainers'][0]['logicalPath']=[29,0]
                with self.assertRaises(Blocked):tracker.capture(state,self.raw_receipt)
                continue
            tracker.capture(state,self.raw_receipt)
            state,receipt=copy.deepcopy(self.chopped),copy.deepcopy(self.chopped_receipt)
            if mutation=='owner':state['entities'].append(next(e for e in self.raw['entities'] if e['id']==51))
            if mutation=='body':receipt['bridge']['nativePhysics']['bodies'][0]['bodyInstanceId']=-435166
            if mutation=='frame':state['frame']=179
            with self.subTest(mutation=mutation),self.assertRaises(Blocked):tracker.requests(state,receipt)

    def test_publication_exact_source_and_nonmutation_receipts(self):
        tracker=RegistryEvidence();tracker.capture(self.raw,self.raw_receipt)
        request=tracker.requests(self.chopped,self.chopped_receipt)[0]
        result={'ok':True,**request['args'],'source':'observed-native-registry-absence',
                'historicalRemovalEventObserved':False,'nativeStateChanged':False,'initialSetsChanged':False,
                'frameworkOnlyRetirementPublished':True,'currentRegisteredIds':[1,53,54],
                'currentRegisteredBodyInstances':[-999]}
        self.assertEqual(tracker.validate_publication(request,{'detail':{'result':result}}),result)
        for key,value in [('entityId',54),('priorBodyInstanceId',-9),('sceneMetadataRefreshes',29),
                          ('historicalRemovalEventObserved',True),('nativeStateChanged',True),
                          ('frameworkOnlyRetirementPublished',False),('currentRegisteredIds',[52]),
                          ('currentRegisteredBodyInstances',[-435166])]:
            altered=copy.deepcopy(result);altered[key]=value
            with self.subTest(key=key),self.assertRaises(Blocked):tracker.validate_publication(request,{'detail':{'result':altered}})
        hidden=copy.deepcopy(self.chopped)
        hidden['graphMappingValidation']={'ok':False,'frame':hidden['frame'],'error':'unrelated current mapping error'}
        hidden['observedProxyRetirements']=[{'nativeId':52,'frame':hidden['frame']}]
        self.assertTrue(tracker.received(request,result,hidden))
        self.assertEqual(tracker.requests(hidden,self.chopped_receipt),[])

    def test_actual_observer_publications_and_completed_raw_resume(self):
        tracker=RegistryEvidence()
        refresh=tracker.requests(self.raw,self.raw_receipt)[0]
        tracker.capture(self.raw,self.raw_receipt)
        absent=tracker.requests(self.chopped,self.chopped_receipt)[0]
        for name,request in [('raw51-refresh',refresh),('proxy52-observed-absent',absent)]:
            saved=json.loads((ROOT/'artifacts/story11'/f'{name}.json').read_text())
            response=next(r['response'] for r in saved['records'] if r['request']['command']=='hot-call')
            self.assertTrue(tracker.validate_publication(request,response)['ok'])
        state=copy.deepcopy(self.raw)
        state['typedActions']={'outcome':'cleared','active':False}
        state['rawInput']={'outcome':'complete','active':False}
        _,proof=resume_case(ROOT/'artifacts/framework-migration/story11-first-delivery-b/cases.json',0,
                            Observation(state,self.raw_receipt,'s_sushi_1_1'))
        self.assertEqual(proof['resumePhase'],'staged')
        state['rawInput']['outcome']='interrupted'
        with self.assertRaises(Blocked):
            resume_case(ROOT/'artifacts/framework-migration/story11-first-delivery-b/cases.json',0,
                        Observation(state,self.raw_receipt,'s_sushi_1_1'))

    def test_world_check_ignores_only_observation_timestamp(self):
        receipt=copy.deepcopy(self.raw_receipt);receipt['detail']['unityFrame']+=100
        self.assertEqual(world_proof(self.raw,receipt),world_proof(self.raw,self.raw_receipt))
        receipt['bridge']['nativePhysics']['bodies'][0]['position']['x']+=1e-12
        self.assertNotEqual(world_proof(self.raw,receipt),world_proof(self.raw,self.raw_receipt))

    def test_automatic_refresh_fence_publication_receipt_and_rearm(self):
        state,receipt=copy.deepcopy(self.raw),copy.deepcopy(self.raw_receipt)
        class Journal:
            def append(self,row):pass
            def describe(self):return {}
        class Fake(Runner):
            def call(inner,target,request,label):
                inner.calls.append(request)
                if request['command']=='pause':return {'bridge':{'inputBlocked':True,'holdPause':True}}
                if request['command']=='hot-call':
                    next(r for r in state['registry'] if r['EntityId']==51)['SpawnNames']=['ChoppedSushiFish']
                    return {'detail':{'result':{'ok':True,'entityId':51,'published':1,'nativeStateChanged':False,
                        'initialSetsChanged':False,'entries':[{'id':51,'hasSpawnCollection':True,'spawnNames':['ChoppedSushiFish']}]}}}
                if request['command']=='food':return receipt
                return {}
            def settled(inner,label):return state
        with tempfile.TemporaryDirectory() as out:
            runner=Fake(SimpleNamespace(out=Path(out),search=False),None,None,Journal());runner.calls=[]
            repaired,_=runner.reconcile_registry(state,receipt,'captured')
            self.assertEqual([r['command'] for r in runner.calls],['pause','hot-call','food','arm','food'])
            self.assertEqual(next(r for r in repaired['registry'] if r['EntityId']==51)['SpawnNames'],['ChoppedSushiFish'])

    def test_actual_prepared_resume_and_occupied_board_clearance(self):
        # Only the missing headless observation is supplied for this offline
        # planner test; the original c failure and native receipts stay intact.
        state=copy.deepcopy(self.chopped)
        state['graphMappingValidation']={'ok':True,'frame':305,'fixedMappingsValidated':True,
            'spawnedMappings':[{'id':53,'path':[30,0,0],'prefab':'ChoppedSushiFish'}],
            'observedPhysicalContainers':[{'nativeId':54,'logicalPath':[30,0,0]}],'removedNativeIds':[51,52]}
        observed=Observation(state,self.chopped_receipt,'s_sushi_1_1')
        case,proof=resume_case(ROOT/'artifacts/framework-migration/story11-first-delivery-b/cases.json',0,observed,True)
        self.assertEqual(proof['resumePhase'],'prepare-assembly')
        raw=proof['stagedNativeProof'];self.assertEqual(raw['rawId'],51);self.assertEqual(raw['preparedId'],53)
        planner=DeliveryPlanner(case,phase='prepare-assembly',source=53,prepared_composition=[(23600,'SushiFish')])
        actions=planner.advance(observed)['request']['actions']
        self.assertEqual(actions[0]['type'],'prepare-primary');self.assertEqual(actions[0]['chef'],45)
        self.assertEqual(actions[0]['target'],case['crate'])
        self.assertEqual(actions[0]['resources'],[case['board'],case['crate']])
        self.assertEqual(actions[1]['after'],['clear-chopper'])
        self.assertEqual(actions[1]['target'],31)


if __name__=='__main__':unittest.main()
