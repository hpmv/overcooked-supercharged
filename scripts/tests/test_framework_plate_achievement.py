"""Captured S success and nine proof mutations; no game or source-file edits."""
import copy
import json
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'scripts'))
from check_framework_plate_achievement import verify

FOLDER=ROOT/'artifacts/framework-migration/native-s/plate-search'


class AchievementTests(unittest.TestCase):
    def test_actual_native_candidate_and_unexecuted_recording(self):
        proof,raw=verify(FOLDER)
        self.assertTrue(proof['candidateAchieved']);self.assertFalse(proof['searchCompleted'])
        self.assertFalse(proof['optimizedWinnerEstablished']);self.assertFalse(proof['exactReplayVerified'])
        self.assertEqual((proof['frames'],proof['nativePickupFrame'],proof['nativePlacementFrame']),(73,55,102))
        self.assertEqual(sum(s['frames']for s in raw['segments']),71)
        self.assertFalse(proof['preparedRawRequest']['executed'])

    def test_nine_captured_mutations_rejected(self):
        original_read=Path.read_text
        for mutation in ('held','station-pair','position','food','ledger','input','native-event','action','qualification'):
            filename='observations.json'
            if mutation=='input':filename='chef-103-walk-inputs.json'
            elif mutation=='native-event':filename='achievement-trace-slice.json'
            elif mutation=='qualification':filename='summary.json'
            document=json.loads(original_read(FOLDER/filename))
            if filename=='observations.json':
                end=next(r['response']for r in document if r['label']=='chef-103-walk-end')
                food=next(r['response']for r in document if r['label']=='chef-103-walk-native')
                entities={e['id']:e for e in end['entities']}
                if mutation=='held':entities[103]['data']['attachment']={'path':[10]}
                elif mutation=='station-pair':entities[32]['data']['attachment']={'path':[11]}
                elif mutation=='position':entities[10]['position']['z']=-15.6
                elif mutation=='food':next(e for e in food['detail']['entities']if e['id']==10)['composition']['children']=[{'type':'IngredientAssembledNode','id':1}]
                elif mutation=='ledger':food['bridge']['nativeRound']['ledger']['total']=1
                else:end['typedActions']['outcome']='failed'
            elif mutation=='input':document[0]['inputs']['103']['Pad']['X']=.123
            elif mutation=='native-event':
                row=next(r['row']for r in document if r['row']['output']['FrameNumber']==55)
                row['output']['ServerMessages']=[m for m in row['output']['ServerMessages']if m['Message']!='AoRn']
            else:document['passed']=True
            changed=json.dumps(document)
            def reader(path,*args,**kwargs):
                return changed if path==FOLDER/filename else original_read(path,*args,**kwargs)
            with self.subTest(mutation=mutation),patch.object(Path,'read_text',reader),self.assertRaises(ValueError):
                verify(FOLDER)


if __name__=='__main__':unittest.main()
