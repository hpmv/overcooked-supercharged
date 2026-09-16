"""Synthetic acceptance/codec mutations only; no native dash success is claimed."""
import base64
import copy
import json
from pathlib import Path
import sys
import unittest

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'scripts'))
import check_framework_dash_outcome as d


def fixture():
    route=json.loads((ROOT/'routes/probes/framework-four-chef-dash.json').read_text())
    chefs,inputs=d.expected_inputs(route);start=31
    epoch={'epoch':0,'boundary':{'chefs':{c:{'DashTimer':-2.0} for c in chefs}},'inputs':{},'frames':{}}
    for i,pads in enumerate(inputs,start+1):
        epoch['inputs'][i]={'value':{'NextFrame':i,'Input':pads,'PreventInvalidState':False}}
        epoch['frames'][i]={'location':{'line':i},'chefs':{c:{'DashTimer':.4-(i-start-1)/60} for c in chefs},'nativeMessages':[]}
    return epoch,route,start


def dash_event(chef,subtype=0):
    # Actual native wire: entity10, component4, InputEventType10, argumentEntity10.
    value=(int(chef)<<30)|(5<<26)|(subtype<<16)
    return {'type':4,'entityId':int(chef),'componentIndex':5,'nativeComponentType':30,
            'bytes':base64.b64encode(value.to_bytes(5,'big')).decode()}


class DashOutcomeTests(unittest.TestCase):
    def test_explicit_route_and_all_four_positive_timer_transitions(self):
        e,r,s=fixture();out=d.audit(e,r,s)
        self.assertTrue(out['passed']);self.assertEqual(out['payloadFrames'],26)
        self.assertEqual(out['automaticNeutralReleaseFrames'],2)
        self.assertTrue(all(x['positiveTimerTransitions'][0]['frame']==32 for x in out['chefs'].values()))

    def test_exact_native_startdash_survives_immediate_collision_timer_reset(self):
        e,r,s=fixture()
        for f in e['frames'].values():
            for chef in f['chefs'].values():chef['DashTimer']=-3.4028234663852886e38
        e['frames'][32]['nativeMessages']=[dash_event(c) for c in e['boundary']['chefs']]
        out=d.audit(e,r,s);self.assertTrue(out['passed'])
        self.assertTrue(all(x['evidenceKind']=='native-StartDash-event' for x in out['chefs'].values()))

    def test_moving_endpoints_and_correct_edges_without_native_dash_are_failure(self):
        e,r,s=fixture()
        for n,f in e['frames'].items():
            for chef in f['chefs'].values():chef.update(DashTimer=-2.0,Pos={'X':n*5.0})
        out=d.audit(e,r,s);self.assertFalse(out['passed'])
        self.assertEqual(len(out['missingDashAcceptance']),4)

    def test_one_refused_chef_fails_entire_four_chef_outcome(self):
        e,r,s=fixture()
        for f in e['frames'].values():f['chefs']['105']['DashTimer']=-2
        out=d.audit(e,r,s);self.assertFalse(out['passed']);self.assertEqual(out['missingDashAcceptance'],['105'])

    def test_missing_native_timers_input_mutations_directives_and_incomplete_ranges_reject(self):
        for mutation in ('timer','nan','edge','pad','warp','seed','correction','frames','chef'):
            with self.subTest(mutation=mutation):
                e,r,s=fixture()
                if mutation=='timer':e['frames'][32]['chefs']['103'].pop('DashTimer')
                if mutation=='nan':e['frames'][32]['chefs']['103']['DashTimer']=float('nan')
                if mutation=='edge':e['inputs'][32]['value']['Input']['103']['Dash']['JustPressed']=False
                if mutation=='pad':e['inputs'][32]['value']['Input']['103']['Pad']['X']=1
                if mutation=='warp':e['inputs'][32]['value']['Warp']={}
                if mutation=='seed':e['inputs'][32]['value']['ResetOrderSeed']=0
                if mutation=='correction':e['inputs'][32]['value']['PreventInvalidState']=True
                if mutation=='frames':e['frames'].pop(59)
                if mutation=='chef':e['inputs'][32]['value']['Input'].pop('105')
                with self.assertRaises(ValueError):d.audit(e,r,s)

    def test_dashcollision_or_wrong_native_component_is_not_startdash(self):
        for mutation in ('collision','component','release-only'):
            with self.subTest(mutation=mutation):
                e,r,s=fixture()
                for f in e['frames'].values():
                    for chef in f['chefs'].values():chef['DashTimer']=-2
                events=[dash_event(c,1 if mutation=='collision' else 0) for c in e['boundary']['chefs']]
                if mutation=='component':
                    for ev in events:ev['nativeComponentType']=29
                e['frames'][58 if mutation=='release-only' else 32]['nativeMessages']=events
                self.assertFalse(d.audit(e,r,s)['passed'])

    def test_preexisting_dash_and_malformed_payload_are_rejected(self):
        e,r,s=fixture();e['boundary']['chefs']['103']['DashTimer']=.1
        with self.assertRaises(ValueError):d.audit(e,r,s)
        e,r,s=fixture();event=dash_event('103');event['bytes']=base64.b64encode(base64.b64decode(event['bytes'])+b'\0').decode()
        e['frames'][32]['nativeMessages']=[event]
        with self.assertRaises(ValueError):d.audit(e,r,s)


if __name__=='__main__':unittest.main()
