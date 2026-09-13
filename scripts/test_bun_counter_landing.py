"""Captured native positive and mutated-observation regressions; no game I/O."""
import copy,gzip,json,unittest
from pathlib import Path
from check_bun_counter_landing import Checker

class Tests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.rows=[]
        with gzip.open(Path('artifacts/chopped-bun-counter-landing-c.jsonl.gz'),'rt',encoding='utf-8-sig') as f:
            for line in f:
                row=json.loads(line)
                if row.get('kind')=='call':
                    s=row['response'].get('state')
                    if not s or not s.get('levelReady'):continue
                    s={k:s[k] for k in ['gameplayFrame','scene','score','delivered','chefs','entities']}
                    s['entities']=[e for e in s['entities'] if e['id'] in [10,23,32,38,123,125]]
                    cls.rows.append(('state',s,row['response']['inputs']))
                elif row.get('kind')=='event' and row['name'] in ['guardMatched','jobComplete','actionFailure','planFailure']:
                    cls.rows.append(('event',row['name'],row.get('value') or {}))
    def replay(self,mutate=None):
        c=Checker()
        for kind,value,extra in self.rows:
            if kind=='state':
                s=copy.deepcopy(value)
                if mutate:mutate(s)
                c.state(s,extra)
            else:c.event(value,extra)
        return c.finish()
    def test_native_positive(self):self.assertEqual(self.replay()['landing']['frame'],438)
    def change(self,s,field,value,when=lambda s:True,identity=125):
        e=next((e for e in s['entities'] if e['id']==identity),None)
        if e and when(s):e[field]=value
    def test_wrong_thrower(self):
        with self.assertRaises(ValueError):self.replay(lambda s:self.change(s,'previousThrowerEntityId',999,lambda s:s['gameplayFrame']==438))
    def test_no_flight(self):
        with self.assertRaises(ValueError):self.replay(lambda s:self.change(s,'throwFlying',False))
    def test_target_occupied_during_flight(self):
        with self.assertRaises(ValueError):self.replay(lambda s:self.change(s,'attachedEntityId',10,lambda s:400<=s['gameplayFrame']<430,38))
    def test_plate_identity_changed(self):
        with self.assertRaises(ValueError):self.replay(lambda s:self.change(s,'observedOrdinal',999,lambda s:s['gameplayFrame']>100,10))
    def test_bun_composition_changed(self):
        with self.assertRaises(ValueError):self.replay(lambda s:self.change(s,'composition',None,lambda s:s['gameplayFrame']==439))
    def test_source_reused(self):
        with self.assertRaises(ValueError):self.replay(lambda s:self.change(s,'observedOrdinal',999,lambda s:s['gameplayFrame']==439))

if __name__=='__main__':unittest.main()
