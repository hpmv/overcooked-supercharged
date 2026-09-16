import copy,json,pathlib,unittest
from check_bowl_offmix import Checker,JOBS

ROOT=pathlib.Path(__file__).resolve().parents[1]
NATIVE=json.loads((ROOT/'artifacts/v7-initial-gameplay.json').read_text())['state']
def initial():
 return {'scene':NATIVE['scene'],'gameplayFrame':0,'timer':270.0,'score':0,'delivered':0,
  'entities':[copy.deepcopy(e) for e in NATIVE['entities'] if e['id'] in [6,18,33,5]],
  'chefs':[{'playerId':i,'heldEntityId':0,'controlsEnabled':True} for i in range(4)]}
def dough(mixed):
 return {'type':'MixedCompositeAssembledNode','id':0,'state':'Mixed' if mixed else 'Unmixed','progress':1.01 if mixed else .5,
  'children':[{'type':'IngredientAssembledNode','id':i,'children':[]} for i in [16620,18448,22804]]}
def exercise(mutate=None,park_frames=780):
 c=Checker();s=initial();c.state(copy.deepcopy(s))
 def step(frame):
  s['gameplayFrame']=frame;s['timer']=270-frame/60
  if mutate:mutate(frame,s)
  c.state(copy.deepcopy(s))
 es={e['id']:e for e in s['entities']};bowl=es[6];basket=es[5]
 for j in JOBS[:3]:c.event('jobComplete',{'id':j})
 bowl['composition']=dough(False);bowl['mixingProgress']=3;step(1)
 bowl['mixingProgress']=11;step(2)
 bowl['composition']=dough(True);bowl['mixingProgress']=12.1;step(3)
 es[18]['attachedEntityId']=0;s['chefs'][3]['heldEntityId']=6;step(4)
 s['chefs'][3]['heldEntityId']=0;es[33]['attachedEntityId']=6;step(5)
 c.event('jobComplete',{'id':JOBS[3]});c.event('jobStart',{'id':JOBS[4]})
 for f in range(6,6+park_frames):step(f)
 c.event('jobComplete',{'id':JOBS[4]});f=6+park_frames
 es[33]['attachedEntityId']=0;s['chefs'][3]['heldEntityId']=6;step(f)
 bowl['composition']={'type':'MixedCompositeAssembledNode','id':0,'state':'Unmixed','children':[]};bowl['mixingProgress']=0
 basket['composition']={'type':'CookedCompositeAssembledNode','state':'Raw','cookingStepId':17160,'children':[dough(True)]};step(f+1)
 es[18]['attachedEntityId']=6;s['chefs'][3]['heldEntityId']=0;basket['composition']['state']='Cooked';step(f+2)
 c.event('jobComplete',{'id':JOBS[5]});return c.finish()

class Tests(unittest.TestCase):
 def test_native_shape_lifecycle_passes(self):
  r=exercise();self.assertTrue(r['passed']);self.assertEqual(r['offmix']['consecutiveSamples'],781);self.assertAlmostEqual(r['offmix']['elapsedNativeSeconds'],13)
 def test_progress_change_rejected(self):
  def change(f,s):
   if f==30:next(e for e in s['entities'] if e['id']==6)['mixingProgress']+=.01
  with self.assertRaisesRegex(ValueError,'mixing progress'):exercise(change)
 def test_identity_reuse_rejected(self):
  def change(f,s):
   if f==30:next(e for e in s['entities'] if e['id']==6)['observedOrdinal']+=1
  with self.assertRaisesRegex(ValueError,'identity'):exercise(change)
 def test_bowl_back_on_mixer_rejected(self):
  def change(f,s):
   if f==30:next(e for e in s['entities'] if e['id']==18)['attachedEntityId']=6
  with self.assertRaisesRegex(ValueError,'off the original mixer'):exercise(change)
 def test_short_wait_rejected(self):
  with self.assertRaisesRegex(ValueError,'shorter than thirteen'):exercise(park_frames=720)
 def test_frozen_timer_rejected(self):
  def change(f,s):
   if f==30:s['timer']=270-29/60
  with self.assertRaisesRegex(ValueError,'timer did not advance'):exercise(change)
 def test_incorrect_native_fry_step_rejected(self):
  def change(f,s):
   if f==788:next(e for e in s['entities'] if e['id']==5)['composition']['cookingStepId']=20068
  with self.assertRaisesRegex(ValueError,'chocolate dough proof'):exercise(change)

if __name__=='__main__':unittest.main()
