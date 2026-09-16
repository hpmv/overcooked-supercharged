"""Offline fresh-round projections and native S transfer evidence; no game calls."""
import ast
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'scripts'))
import framework_plate_fresh_search as f
from framework_kitchen_planner import Observation


def captured():
    root=ROOT/'artifacts/framework-migration/native-s/plate-search'
    obs=json.loads((root/'observations.json').read_text())
    def get(label):return next(x['response'] for x in obs if x['label']==label)
    return root,json.loads((root/'case.json').read_text()),get('base'),get('base-native'),get('chef-103-walk-end'),get('chef-103-walk-native')


def frames():
    root,case,base,bnative,end,enative=captured()
    rows=json.loads((root/'achievement-trace-slice.json').read_text())
    registry={str(e['EntityId']):e for e in base['registry']}
    actual=[{'offset':x['row']['output']['FrameNumber']-31,'output':x['row']['output'],
             'messages':[f.message_info(m,registry) for m in x['row']['output']['ServerMessages']]}
            for x in rows if 31<x['row']['output']['FrameNumber']<=104 and x['row']['output']['LastFramePaused'] is False]
    candidate=case['candidates'][0];effect=f.native_effects(case,candidate,actual)
    record={'id':'chef-103-walk','eligible':True,'goal':f.plate.check_goal(case,Observation(end,enative['bridge']['nativeRound'],enative)),
            'frames':actual,'effects':effect,'raw':f.raw_identity(end,enative),
            'inputs':json.loads((root/'chef-103-walk-inputs.json').read_text()),
            'gameplay':f.gameplay_projection(case,end,enative,bnative,effect)}
    return record


class FreshPlateSearchTests(unittest.TestCase):
    def test_journal_appends_once_and_legacy_reader_gets_every_native_receipt(self):
        _,_,base,native,_,_=captured()
        records=[{'target':'controller','label':'base','response':base},
                 {'target':'bridge','label':'raw-0','response':native},
                 {'target':'bridge','label':'raw-1','response':native},
                 {'target':'settled-pause-observation','label':'base-native','response':native}]
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp)/'observations.jsonl';compat=Path(tmp)/'observations.json'
            journal=f.EvidenceJournal(path);prefix=b''
            for record in records:
                journal.append(record)
                current=path.read_bytes();self.assertTrue(current.startswith(prefix));prefix=current
                self.assertFalse(compat.exists())
            self.assertEqual([json.loads(line) for line in path.read_text().splitlines()],records)
            report=journal.finalize(compat);first=compat.read_bytes()
            self.assertEqual(json.loads(first),records)
            self.assertEqual(report['records'],len(records))
            self.assertEqual(journal.finalize(compat),report)
            self.assertEqual(compat.read_bytes(),first)
            # Existing label-based comparison readers retain their old schema.
            loaded=json.loads(compat.read_text())
            self.assertEqual(next(r['response'] for r in loaded if r['label']=='base-native'),native)
            self.assertEqual(journal.describe()['sha256'],f.hashlib.sha256(prefix).hexdigest())

    def test_journal_preserves_receipts_on_exception_and_closed_journal_rejects_append(self):
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp)/'observations.jsonl';compat=Path(tmp)/'observations.json';journal=f.EvidenceJournal(path)
            try:
                journal.append({'label':'native-receipt','response':{'value':'line\nquote"','held':False}})
                raise ValueError('deliberate later validation failure')
            except ValueError:
                journal.append({'label':'finally-pause','response':{'paused':True}})
            finally:journal.finalize(compat)
            self.assertEqual([r['label'] for r in json.loads(compat.read_text())],['native-receipt','finally-pause'])
            self.assertEqual(len(path.read_text().splitlines()),2)
            with self.assertRaisesRegex(ValueError,'closed'):journal.append({'late':True})

    def test_actual_w_fresh_pair_compares_current_geometry_not_historical_registration_y(self):
        rows=json.loads((ROOT/'artifacts/framework-migration/native-w/plate-fresh-search/observations.json').read_text())
        def get(label):return next(x['response'] for x in rows if x.get('label')==label)
        states=[get(f'candidate-{i}-base') for i in range(2)]
        natives=[get(f'candidate-{i}-base-native') for i in range(2)]
        cases=[f.plate.prepare_case(f.require_fresh(s,n,31)) for s,n in zip(states,natives)]
        self.assertNotEqual(states[0]['initialRegistry'],states[1]['initialRegistry'])
        keys=[f.restart_comparison_key(c,s,n) for c,s,n in zip(cases,states,natives)]
        self.assertIsNone(f.first_difference(*keys))
        for mutation in ('position','velocity','rotation','chef','metadata'):
            with self.subTest(mutation=mutation):
                state=copy.deepcopy(states[1]);chef=next(e for e in state['entities'] if e['id']==103)
                if mutation in ('position','velocity','rotation'):chef[mutation]['y']+=.0000001
                elif mutation=='chef':chef['chef']['dashTimer']=.1
                else:state['initialRegistry'][0]['Name']='different-native-prefab'
                self.assertIsNotNone(f.first_difference(keys[0],f.restart_comparison_key(cases[1],state,natives[1])))
        self.assertEqual(f.raw_identity(states[1],natives[1])['historicalInitialRegistrations'],states[1]['initialRegistry'])

    def test_actual_s_cold_baseline_has_native4p270seed0_and_all_four_candidates(self):
        _,case,s,n,_,_=captured()
        observed=f.require_fresh(s,n,31)
        actual=f.plate.prepare_case(observed)
        self.assertEqual([x['id'] for x in actual['candidates']],['chef-103-walk','chef-103-dash','chef-106-walk','chef-106-dash'])
        self.assertEqual(actual['source'],38);self.assertEqual(actual['target'],32)
        # Historical warpUsed is deliberately retained, not relabeled as a warp
        # in this fresh round; native generator count is zero for the round.
        self.assertTrue(s['warpUsed'])
        self.assertEqual(n['bridge']['nativeRound']['recipeRandom']['authoringWarpCount'],0)

    def test_configuration_old_graph_and_wrong_frame_are_rejected(self):
        for mutation in ('frame','4p','duration','seed','generator','warp','score','graph','correction','clock'):
            with self.subTest(mutation=mutation):
                _,_,s,n,_,_=captured()
                if mutation=='frame':s['frame']=32
                if mutation=='4p':n['bridge']['session']['serverUsers']=3
                if mutation=='duration':n['bridge']['nativeRound']['timeLimit']=180
                if mutation=='seed':n['bridge']['nativeRound']['recipeRandom']['seed']=1
                if mutation=='generator':n['bridge']['nativeRound']['recipeRandom']['generator']='replacement'
                if mutation=='warp':n['bridge']['nativeRound']['recipeRandom']['authoringWarpCount']=1
                if mutation=='score':n['bridge']['nativeRound']['ledger']['total']=1
                if mutation=='graph':s['actionGraph']['chefs'][0]['actions']=[{'id':1}]
                if mutation=='correction':s['preventInvalidState']=True
                if mutation=='clock':n['bridge']['nativeCheckpoints'].pop('nativeClientClock')
                with self.assertRaises(ValueError):f.require_fresh(s,n,31)

    def test_fresh_graph_cleanup_requires_fence_and_untouched_native_plate(self):
        _,_,s,n,_,_=captured()
        s['frame']=1
        n['bridge'].update(inputBlocked=True,holdPause=True)
        s['actionGraph']['chefs'][0]['actions']=[{'id':'retained-reset-node'}]
        # This admission allows only metadata cleanup behind the input fence;
        # require_fresh still independently rejects an uncleared graph afterward.
        self.assertEqual(f.require_fenced_initial_clear(s,n)['roles']['plate'],10)
        for mutation in ('inputBlocked','holdPause','paused','frame','held'):
            with self.subTest(mutation=mutation):
                state=copy.deepcopy(s);native=copy.deepcopy(n)
                if mutation in ('inputBlocked','holdPause','paused'):native['bridge'][mutation]=False
                if mutation=='frame':state['frame']=2
                if mutation=='held':
                    entity=next(e for e in state['entities'] if e['id']==10)
                    entity['data']['attachmentParent']={'path':[103]}
                with self.assertRaises(ValueError):f.require_fenced_initial_clear(state,native)

    def test_actual_s_pickup_and_placement_events_decode_to_same_native_plate(self):
        r=frames();self.assertTrue(r['effects']['nativeTransferProven'])
        self.assertEqual(r['effects']['pickupOffsets'],[24])
        self.assertEqual(r['effects']['placementOffsets'],[71])
        self.assertEqual(r['goal']['frames'],73)
        self.assertEqual(r['goal']['nativeSeconds'],1.2166655)
        case=captured()[1]
        modified=copy.deepcopy(r['frames'])
        for row in modified:row['messages']=[m for m in row['messages'] if m.get('entityId')!=32]
        self.assertFalse(f.native_effects(case,case['candidates'][0],modified)['nativeTransferProven'])

    def test_absolute_clock_and_instance_differences_are_reported_without_raw_parity_claim(self):
        expected=frames();actual=copy.deepcopy(expected)
        actual['raw']['absoluteNativeClocks']['source']+=100
        actual['raw']['nativePhysics']['bodies'][0]['bodyInstanceId']+=1000
        result=f.compare_repeat(expected,actual)
        self.assertTrue(result['passed']);self.assertFalse(result['rawNativeStateEqual'])
        self.assertFalse(result['fullRawStateParityClaimed']);self.assertFalse(result['freshProcessClaimed'])

    def test_input_timer_native_effect_and_food_projection_changes_fail_repetition(self):
        for mutation in ('input','timer','event','food','goal','transfer'):
            with self.subTest(mutation=mutation):
                expected=frames();actual=copy.deepcopy(expected)
                if mutation=='input':actual['inputs'][0]['inputs']['103']['Pad']['X']=.5
                if mutation=='timer':actual['gameplay']['nativeSeconds']+=.000001
                if mutation=='event':actual['gameplay']['nativeEvents'].pop()
                if mutation=='food':actual['gameplay']['nativeFoodCompositions']['10']={'type':'different'}
                if mutation=='goal':actual['goal']['achieved']=False
                if mutation=='transfer':actual['effects']['nativeTransferProven']=False
                self.assertFalse(f.compare_repeat(expected,actual)['passed'])

    def test_native_clock_event_does_not_shift_projected_attachment_event_order(self):
        r=frames();case=captured()[1];actual=copy.deepcopy(r['frames'])
        for frame in actual:
            if frame['messages']:frame['messages'].insert(0,{'type':3,'entityId':107,'bytes':'uninterpreted-clock'})
        projection=f.native_effects(case,case['candidates'][0],actual)
        self.assertEqual(projection['gameplayEvents'],r['effects']['gameplayEvents'])
        changed=copy.deepcopy(r);changed['frames']=actual
        result=f.compare_repeat(r,changed)
        self.assertTrue(result['passed']);self.assertFalse(result['allRawNativeMessagesEqual'])

    def test_failed_fast_candidate_is_never_selected_and_only_observed_times_rank(self):
        trials=[{'id':'failed','eligible':False,'goal':{'nativeSeconds':.1,'frames':6}},
                {'id':'baseline','eligible':True,'goal':{'nativeSeconds':2.,'frames':120}},
                {'id':'better','eligible':True,'goal':{'nativeSeconds':1.,'frames':60}}]
        self.assertEqual(f.select_trial(trials)['id'],'better')
        with self.assertRaises(ValueError):f.select_trial(trials[:1])

    def test_failed_edge_conversion_retains_observed_inputs_and_native_effects(self):
        _,case,base,bnative,end,enative=captured();record=frames()
        inputs=copy.deepcopy(record['inputs'])
        # The actual W encoding defect: JustPressed true with Down false.
        inputs[0]['inputs']['103']['Dash']['JustPressed']=True
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp)/'failed-candidate.json'
            with self.assertRaisesRegex(ValueError,'inconsistent'):
                f.persist_candidate_evidence(path,'captured-edge-mutation',base,bnative,end,enative,
                                             case,case['candidates'][0],inputs,record['frames'])
            saved=json.loads(path.read_text())
            self.assertEqual(saved['validation'],'failed')
            self.assertEqual(saved['inputs'],inputs)
            self.assertTrue(saved['effects']['nativeTransferProven'])
            self.assertTrue(saved['goal']['achieved'])
            self.assertEqual(saved['endNativeReceipt'],enative)
            self.assertNotIn('rawRequest',saved)

    def test_actual_w_malformed_dash_capture_is_preserved_without_replay_correction(self):
        root=ROOT/'artifacts/framework-migration/native-w'
        captured=json.loads((root/'plate-fresh-search-2-dash-encoding.json').read_text())
        rows=json.loads((root/'plate-fresh-search-2/observations.json').read_text())
        def get(label):return next(x['response'] for x in rows if x.get('label')==label)
        case=json.loads((root/'plate-fresh-search-2/candidate-1-case.json').read_text())
        self.assertEqual(captured['firstMalformedLine'],4249)
        self.assertEqual(captured['malformedRows'],5)
        candidate=next(c for c in case['candidates'] if c['id']=='chef-103-dash')
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp)/'actual-w-rejection.json'
            with self.assertRaisesRegex(ValueError,'inconsistent'):
                f.persist_candidate_evidence(path,candidate['id'],get('candidate-1-base'),get('candidate-1-base-native'),
                    get('chef-103-dash-end'),get('chef-103-dash-native'),case,candidate,captured['inputs'],captured['frames'])
            evidence=json.loads(path.read_text())
            self.assertTrue(evidence['goal']['achieved']);self.assertEqual(evidence['goal']['frames'],73)
            self.assertEqual(evidence['inputs'],captured['inputs'])
            self.assertEqual(evidence['validation'],'failed')

    def test_full_round_reader_keeps_terminal_output_with_no_next_input(self):
        root,case,s,_,_,_=captured();rows=json.loads((root/'achievement-trace-slice.json').read_text())
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp)/'trace.jsonl';path.write_text('')
            cursor=f.RoundTrace(path)
            with path.open('a') as stream:
                for row in rows:stream.write(json.dumps(row['row'])+'\n')
            inputs,observed=cursor.complete(31,104,case['chefs'],s['registry'])
            self.assertEqual(len(inputs),73);self.assertEqual(len(observed),73)
            self.assertEqual(observed[-1]['offset'],73)

    def test_runner_has_no_authoring_warp_checkpoint_or_replay_command(self):
        tree=ast.parse((ROOT/'scripts/framework_plate_fresh_search.py').read_text())
        commands=[]
        for node in ast.walk(tree):
            if isinstance(node,ast.Dict):
                for key,value in zip(node.keys,node.values):
                    if isinstance(key,ast.Constant) and key.value=='command' and isinstance(value,ast.Constant):commands.append(value.value)
        self.assertIn('restart',commands)
        self.assertIn('actions-clear',commands)
        self.assertFalse({'warp','checkpoint','raw-replay'} & set(commands))


if __name__=='__main__':unittest.main()
