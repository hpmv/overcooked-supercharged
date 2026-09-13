"""File-only trace/first-divergence tests, including the closed native M recording."""
import base64
import copy
import gzip
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'scripts'))
import compare_framework_frames as parity


def message(kind,entity,payload=0):
    # Native header is MSB-first10bits. These are opaque synthetic payloads.
    encoded=(((entity<<8)|payload)<<6).to_bytes(3,'big') if kind==43 else ((entity<<6)|payload).to_bytes(2,'big')
    return {'Type':kind,'Message':base64.b64encode(encoded).decode()}


def fixture():
    ids=range(103,107)
    physics={str(i):{'Pos':{'X':0,'Y':.05,'Z':0},'Rotation':{'X':0,'Y':0,'Z':0,'W':1},
                       'Velocity':{'X':0,'Y':0,'Z':0},'AngularVelocity':{'X':0,'Y':0,'Z':0}} for i in ids}
    chefs={str(i):{'DashTimer':0,'HighlightedForPickup':-1,'LastVelocity':{'X':0,'Y':0,'Z':0}} for i in ids}
    def row(frame,paused,next_paused,items=None,next_frame=None):
        inputs={'NextFrame':frame if next_frame is None else next_frame,'Input':None,'Warp':None,
                'ResetOrderSeed':0,'__isset':{'nextFrame':True,'resetOrderSeed':False}}
        if next_frame is not None:inputs['Input']={str(i):{'Pad':{'X':0,'Y':0},'Pickup':{'Down':False,'JustPressed':False,'JustReleased':False}} for i in ids}
        return {'kind':'exchange','output':{'FrameNumber':frame,'LastFramePaused':paused,'NextFramePaused':next_paused,
                'Items':items or {},'Chefs':copy.deepcopy(chefs),'EntityRegistry':[], 'ServerMessages':[],
                'PhysicsFramesElapsed':1,'FramesSinceLastNoPhysicsFrame':2},'input':inputs}
    first=row(10,True,False,copy.deepcopy(physics),11)
    first['output']['EntityRegistry']=[{'EntityId':i,'SyncEntityTypes':[1]} for i in ids]
    a=row(11,False,False,{'103':{'Pos':{'X':.1,'Y':.05,'Z':0}}},12)
    b=row(12,False,True,{'103':{'Pos':{'X':.2,'Y':.05,'Z':0}}})
    warp=row(12,True,True);warp['input']['Warp']={'Frame':10}
    return [first,a,b,warp,copy.deepcopy(first),copy.deepcopy(a),copy.deepcopy(b)]


def write(path,rows):
    path.write_text(''.join(json.dumps(r)+'\n' for r in rows),encoding='utf8')


def evaluate(rows):
    with tempfile.TemporaryDirectory() as directory:
        path=Path(directory)/'trace.jsonl';write(path,rows)
        epochs=parity.selected_epochs(path,{0,1},10,12)
        return parity.compare(epochs[0],epochs[1],10,12)


class FrameParityTests(unittest.TestCase):
    def test_sparse_observation_reconstruction_and_input_output_frame_causality(self):
        result=evaluate(fixture())
        self.assertTrue(result['passed']);self.assertEqual(result['advancingFrames'],2)
        self.assertTrue(result['boundaryEqual'])

    def test_first_intermediate_drift_is_caught_even_when_endpoint_matches(self):
        rows=fixture();rows[5]['output']['Items']['103']['Pos']['X']=.10000001
        result=evaluate(rows)
        self.assertFalse(result['passed']);self.assertEqual(result['firstDivergence']['frame'],11)
        self.assertEqual(result['firstDivergence']['field'],'$frame/physics/103/Pos/X')
        self.assertTrue(result['checks']['inputs']['equal'])

    def test_quaternion_sign_and_signed_zero_are_not_normalized(self):
        rows=fixture();rows[5]['output']['Items']['103']['Rotation']={'X':0,'Y':0,'Z':0,'W':-1}
        self.assertFalse(evaluate(rows)['checks']['physics']['equal'])
        self.assertIsNotNone(parity.first_difference(0,-0.0))
        self.assertEqual(parity.loads('{"v":-0}')['v'],-0.0)
        self.assertIsNotNone(parity.first_difference(parity.loads('{"v":-0}'),{'v':0}))

    def test_raw_input_presence_flags_and_native_press_are_compared(self):
        rows=fixture();rows[4]['input']['__isset']['resetOrderSeed']=True
        self.assertFalse(evaluate(rows)['checks']['inputs']['equal'])
        rows=fixture();rows[4]['input']['Input']['103']['Pickup']['Down']=True
        self.assertFalse(evaluate(rows)['checks']['inputs']['equal'])

    def test_native_message_order_and_auxiliary_only_difference_remain_distinct(self):
        rows=fixture();events=[message(4,103),message(4,104)]
        rows[1]['output']['ServerMessages']=events;rows[5]['output']['ServerMessages']=events[::-1]
        self.assertFalse(evaluate(rows)['checks']['nativeMessages']['equal'])
        rows=fixture();rows[1]['output']['ServerMessages']=[message(43,103,3)]
        rows[5]['output']['ServerMessages']=[message(43,103,4)]
        result=evaluate(rows)
        self.assertTrue(result['checks']['nativeMessages']['equal']);self.assertFalse(result['checks']['auxiliaryMessages']['equal'])
        self.assertFalse(result['passed'])

    def test_missing_duplicate_and_incomplete_boundary_are_fail_closed(self):
        for mutate in [lambda r:r.pop(5),lambda r:r.insert(2,copy.deepcopy(r[1])),
                       lambda r:r[4]['output']['Items']['103'].pop('Velocity'),
                       lambda r:r[5]['output']['Chefs'].pop('104')]:
            rows=fixture();mutate(rows)
            with self.assertRaises(parity.TraceError):evaluate(rows)

    def test_truncated_registry_prefix_requires_explicit_opt_out(self):
        rows=fixture()
        prefix=copy.deepcopy(rows[0]);prefix['output']['FrameNumber']=9
        prefix['input']['NextFrame']=9;prefix['input']['Input']=None
        rows[0]['output']['EntityRegistry']=[]
        rows[4]['output']['EntityRegistry']=[]
        rows.insert(0,prefix)
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'trace.jsonl';write(path,rows)
            full=parity.selected_epochs(path,{0,1},10,12)
            self.assertTrue(parity.compare(full[0],full[1],10,12)['passed'])
            with self.assertRaisesRegex(parity.TraceError,'actual live registry'):
                parity.selected_epochs(path,{0,1},10,12,after_line=2)
            epochs=parity.selected_epochs(path,{0,1},10,12,after_line=2,require_registry_membership=False)
            self.assertTrue(parity.compare(epochs[0],epochs[1],10,12)['passed'])

    def test_truncated_registry_cli_requires_positive_crop_boundary(self):
        rows=fixture()
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'trace.jsonl';out=Path(directory)/'report.json';write(path,rows)
            argv=['compare_framework_frames.py','--trace',str(path),'--start','10','--end','12',
                  '--allow-truncated-registry-prefix','--out',str(out)]
            with mock.patch.object(sys,'argv',argv):
                self.assertEqual(parity.main(),1)
            report=json.loads(out.read_text(encoding='utf8'))
            self.assertFalse(report['passed'])
            self.assertIn('positive --after-line',report['error'])

    def test_paused_brotli_and_gzip_preserve_exact_receipts(self):
        import brotli
        paused=copy.deepcopy(fixture()[3]);paused['input']['Warp']=None
        raw=(json.dumps(paused)+'\n').encode()
        block={'kind':'paused-exchanges','version':1,'encoding':'brotli-jsonl','count':1,'uncompressedBytes':len(raw),
               'sha256':hashlib.sha256(raw).hexdigest(),'data':base64.b64encode(brotli.compress(raw)).decode()}
        rows=fixture();rows.insert(3,block)
        self.assertTrue(evaluate(rows)['passed'])
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'trace.jsonl.gz'
            with gzip.open(path,'wt',encoding='utf8') as stream:stream.write(''.join(json.dumps(r)+'\n' for r in rows))
            epochs=parity.selected_epochs(path,{0,1},10,12)
            self.assertTrue(parity.compare(epochs[0],epochs[1],10,12)['passed'])
        for key,value in [('sha256','0'*64),('uncompressedBytes',len(raw)+1),('count',2),('version',2)]:
            bad=copy.deepcopy(rows);bad[3][key]=value
            with self.assertRaises(parity.TraceError):evaluate(bad)

    def test_partial_file_and_nonfinite_values_are_rejected(self):
        for text in ['NaN','Infinity','1e999']:
            with self.assertRaises(parity.TraceError):parity.loads('{"value":'+text+'}')
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'unfinished.jsonl';path.write_text('{"kind":"exchange"}')
            with self.assertRaises(parity.TraceError):list(parity.records(path))

    def test_actual_native_m_frames_find_earlier_pose_and_event_divergence(self):
        path=ROOT/'artifacts/framework-migration/native-m/exchange.jsonl'
        if not path.exists():path=ROOT.parent/'artifacts/framework-migration/native-m/exchange.jsonl'
        if not path.exists():self.skipTest('closed native M trace fixture is not present')
        epochs=parity.selected_epochs(path,{0,1},181,301)
        result=parity.compare(epochs[0],epochs[1],181,301)
        self.assertFalse(result['passed']);self.assertTrue(result['boundaryEqual'])
        self.assertTrue(result['checks']['inputs']['equal']);self.assertTrue(result['checks']['chefs']['equal'])
        self.assertTrue(result['checks']['phase']['equal'])
        self.assertEqual(result['firstDivergence']['frame'],182)
        self.assertEqual(result['checks']['physics']['firstDivergence']['field'],'$frame/physics/81/Pos/Z')
        self.assertFalse(result['checks']['nativeMessages']['equal']);self.assertFalse(result['checks']['auxiliaryMessages']['equal'])


if __name__=='__main__':unittest.main()
