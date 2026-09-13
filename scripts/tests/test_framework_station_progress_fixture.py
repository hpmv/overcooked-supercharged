"""Captured fresh-layout checks; later phase mutations are explicit synthetic input."""
import copy
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT/'scripts'))
import framework_station_progress_fixture as f


def fresh():
    rows = json.loads((ROOT/'artifacts/framework-migration/native-s/plate-search/observations.json').read_text())
    return (next(x['response'] for x in rows if x['label'] == 'base'),
            next(x['response'] for x in rows if x['label'] == 'base-native-raw-000'))


def entity(s, i):
    return next(e for e in s['entities'] if e['id'] == i)


def staged():
    s, n = fresh(); case = f.initial_case(s, n)
    raw = copy.deepcopy(entity(s, 7))
    raw.update(id=123, path=[70, 0], name='Frankfurter', className='sausage', prefab='Sausage',
               position=copy.deepcopy(entity(s, 45)['position']), data={'attachmentParent': {'path': [45]}})
    s['entities'].append(raw)
    s['registry'].append({'EntityId': 123, 'Name': 'Frankfurter', 'Components': ['IngredientPropertiesComponent'], 'Pos': {}, 'SpawnNames': []})
    entity(s, 45)['data']['attachment'] = {'path': [70, 0]}
    n['detail']['entities'].append({'id': 123, 'name': 'Frankfurter', 'composition': {
        'type': 'IngredientAssembledNode', 'id': f.SAUSAGE, 'name': 'Frankfurter'}})
    s['typedActions'] = {'outcome': 'complete'}
    return case, s, n


def edge():
    case, s, n = staged(); case = f.staged_case(case, s, n)
    entity(s, 45)['data'].pop('attachment')
    entity(s, 123)['data']['attachmentParent'] = {'path': [103]}
    entity(s, 103)['data']['attachment'] = {'path': [70, 0]}
    entity(s, 103)['chef']['highlightedForPlacement'] = {'path': [19]}
    return case, s, n


class StationProgressFixtureTests(unittest.TestCase):
    def test_actual_fresh_roles_and_bounded_unexecuted_requests(self):
        s, n = fresh(); c = f.initial_case(s, n)
        self.assertEqual([c[k] for k in ('crate', 'handoff', 'pot', 'home', 'supplier', 'center')], [70,45,7,19,105,103])
        self.assertEqual(c['request']['maximumFrames'], 360)
        self.assertTrue(all(a['dash'] is False for a in c['request']['actions']))
        self.assertTrue(next(a for a in c['request']['actions'] if a['type']=='pickup')['expectSpawn'])

    def test_missing_or_wrong_native_roles_never_emit_a_request(self):
        for mutation in ('spawn', 'component', 'pose', 'home', 'food', 'clock', 'busy'):
            with self.subTest(mutation=mutation):
                s, n = fresh()
                if mutation == 'spawn': next(r for r in s['registry'] if r['EntityId']==70)['SpawnNames']=['Flour']
                if mutation == 'component': next(r for r in s['registry'] if r['EntityId']==19)['Components']=[]
                if mutation == 'pose': entity(s,70)['position']['x']+=1
                if mutation == 'home': entity(s,7)['data']['attachmentParent']={'path':[17]}
                if mutation == 'food': next(r for r in n['detail']['entities'] if r['id']==7)['cookingProgress']=1
                if mutation == 'clock': n['bridge']['nativeCheckpoints'].pop('nativeClientClock')
                if mutation == 'busy': entity(s,103)['data']['attachment']={'path':[10]}
                with self.assertRaises((ValueError, KeyError)): f.initial_case(s,n)

    def test_native_source_lineage_and_exact_staging_pair_required(self):
        c,s,n=staged(); result=f.staged_case(c,s,n)
        self.assertEqual(result['source'],123)
        self.assertFalse(any(a['type']=='place' for a in result['request']['actions']))
        for mutation in ('lineage','wrong-pair','duplicate-food','unfinished'):
            with self.subTest(mutation=mutation):
                c,s,n=staged()
                if mutation=='lineage': entity(s,123)['path']=[68,0]
                if mutation=='wrong-pair': entity(s,45)['data']['attachment']={'path':[68,0]}
                if mutation=='duplicate-food': n['detail']['entities'].append({'id':124,'composition':{'type':'IngredientAssembledNode','id':f.SAUSAGE}})
                if mutation=='unfinished': s['typedActions']['outcome']='failed'
                with self.assertRaises(ValueError): f.staged_case(c,s,n)

    def test_only_stationary_exact_home_edge_is_prepared(self):
        c,s,n=edge(); result=f.load_edge(c,s,n)
        pads=result['request']['segments'][0]['chefs']
        self.assertEqual(result['request']['segments'][0]['frames'],1)
        self.assertEqual([int(i) for i,p in pads.items() if p['pickup']],[103])
        self.assertEqual(result['automaticReleaseFrames'],2)
        for mutation in ('target','source','cached','resume','actual','throw','dash','impact'):
            with self.subTest(mutation=mutation):
                c,s,n=edge(); chef=entity(s,103)
                if mutation=='target': chef['chef']['highlightedForPlacement']={'path':[17]}
                if mutation=='source': entity(s,123)['path']=[70,1]
                if mutation=='cached': chef['chef']['lastVelocity']['x']=.000001
                if mutation=='actual': chef['velocity']['z']=.000001
                if mutation=='resume': next(b for b in n['bridge']['nativePhysics']['bodies'] if b['entityId']==103)['resumeVelocity']['z']=.000001
                if mutation=='throw': chef['chef']['aimingThrow']=True
                if mutation=='dash': chef['chef']['dashTimer']=.01
                if mutation=='impact': chef['chef']['impactTimer']=.01
                with self.assertRaises(ValueError): f.load_edge(c,s,n)

    def test_loaded_native_boundary_requires_consumption_and_young_progress(self):
        c,s,n=edge(); c=f.load_edge(c,s,n)
        s['entities']=[e for e in s['entities'] if e['id']!=123]
        # Production headless registry keeps historical registration receipts.
        entity(s,103)['data'].pop('attachment')
        raw=next(e for e in n['detail']['entities'] if e['id']==123)['composition']
        n['detail']['entities']=[e for e in n['detail']['entities'] if e['id']!=123]
        pot=next(e for e in n['detail']['entities'] if e['id']==7)
        pot['composition']['children']=[raw];pot['cookingProgress']=.05
        result=f.loaded_case(c,s,n)
        self.assertEqual(result['expectedObservedFrames'],62)
        self.assertFalse(result['nativeReplayVerified'])
        for mutation in ('missing-consumption','wrong-food','home','old','zero','time','stale-carry'):
            with self.subTest(mutation=mutation):
                ss,nn=copy.deepcopy(s),copy.deepcopy(n);pp=next(e for e in nn['detail']['entities'] if e['id']==7)
                if mutation=='missing-consumption':
                    extra=copy.deepcopy(nn['bridge']['nativePhysics']['bodies'][0]);extra['entityId']=123
                    nn['bridge']['nativePhysics']['bodies'].append(extra)
                if mutation=='wrong-food': pp['composition']['children'][0]['id']=f.SAUSAGE+1
                if mutation=='home': entity(ss,7)['data']['attachmentParent']={'path':[17]}
                if mutation=='old': pp['cookingProgress']=2
                if mutation=='zero': pp['cookingProgress']=0
                if mutation=='time': pp['cookingTime']=10
                if mutation=='stale-carry': entity(ss,103)['data']['attachment']={'path':[70,0]}
                with self.assertRaises(ValueError): f.loaded_case(c,ss,nn)


if __name__ == '__main__': unittest.main()
