import copy
import gzip
import json
import unittest
from pathlib import Path
from check_near_ready_fryer import Checker, cooked


class NativeFryerEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        path = Path('artifacts/near-ready-fryer-a.jsonl.gz')
        if not path.exists(): raise unittest.SkipTest('Native mechanism fixture is not installed')
        checker = Checker()
        with gzip.open(path, 'rt', encoding='utf-8-sig') as stream:
            for line in stream:
                row = json.loads(line)
                if row.get('kind') == 'event': checker.event(row['name'], row.get('value') or {})
                elif row.get('kind') == 'call':
                    s = row['response']['state']; pads = row['response']['inputs']
                    if s['gameplayFrame'] == 1718:
                        cls.before, cls.consumption, cls.pads = copy.deepcopy(checker), s, pads
                        break
                    checker.state(s, pads)

    def attempt(self, mutate):
        checker, state, pads = copy.deepcopy((self.before, self.consumption, self.pads))
        mutate(checker, state, pads)
        checker.state(state, pads)
        return checker

    def test_original_native_consumption_passes(self):
        self.assertEqual(self.attempt(lambda c,s,p: None).harvest_frame, 1718)

    def test_same_recipe_wrong_plate_identity_rejected(self):
        def mutate(c,s,p): c.entity(s,c.mapping['plate'])['observedOrdinal'] += 1
        with self.assertRaises(ValueError): self.attempt(mutate)

    def test_basket_removed_from_original_stove_rejected(self):
        def mutate(c,s,p): c.entity(s,c.mapping['fryerHome'])['attachedEntityId'] = 0
        with self.assertRaises(ValueError): self.attempt(mutate)

    def test_basket_carried_rejected(self):
        def mutate(c,s,p): s['chefs'][0]['heldEntityId'] = c.mapping['basket']
        with self.assertRaises(ValueError): self.attempt(mutate)

    def test_consumption_without_fresh_edge_rejected(self):
        def mutate(c,s,p): next(i for i in p if i['player']==3)['pickup'] = False
        with self.assertRaises(ValueError): self.attempt(mutate)

    def test_wrong_native_target_rejected(self):
        def mutate(c,s,p): next(i for i in c.last['chefs'] if i['playerId']==3)['placementTargetId'] = 123456
        with self.assertRaises(ValueError): self.attempt(mutate)

    def test_missing_native_cooked_predecessor_rejected(self):
        def mutate(c,s,p): c.cooked_frame = None
        with self.assertRaises(ValueError): self.attempt(mutate)

    def test_changed_native_duration_rejected(self):
        def mutate(c,s,p): c.entity(s,c.mapping['basket'])['cookingTime'] = 1
        with self.assertRaises(ValueError): self.attempt(mutate)

    def test_burnt_and_incorrect_recipe_shape_rejected(self):
        plate = copy.deepcopy(self.before.entity(self.consumption,self.before.mapping['plate']))
        self.assertTrue(cooked(plate))
        def change(node):
            if node.get('type') == 'CookedCompositeAssembledNode': node['state'] = 'Burnt'
            for child in node.get('children',[]): change(child)
        change(plate['composition']); self.assertFalse(cooked(plate))
        plate['composition'] = {'type':'CookedCompositeAssembledNode','state':'Cooked','cookingStepId':17160,'children':[]}
        self.assertFalse(cooked(plate))


if __name__ == '__main__': unittest.main()
