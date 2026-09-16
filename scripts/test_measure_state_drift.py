"""Offline fixtures for magnitude, identity and event-separation semantics."""
import gzip
import json
import math
import tempfile
import unittest
from pathlib import Path

from measure_state_drift import Metric, distance, forward_angle, measure, rotation_angle


class DriftTests(unittest.TestCase):
    def test_angles_and_vector_magnitudes(self):
        self.assertEqual(distance([0, 0, 0], [.3, .4, 0]), .5)
        self.assertEqual(rotation_angle([0, 0, 0, 1], [0, 0, 0, -1]), 0)
        q = [.312456, -.713481, .0000729, .624921]
        self.assertEqual(rotation_angle(q, q), 0)
        self.assertAlmostEqual(rotation_angle([0, 0, 0, 1], [0, math.sqrt(.5), 0, math.sqrt(.5)]), 90)
        self.assertIsNone(rotation_angle([0, 0, 0, 0], [0, 0, 0, 1]))
        self.assertEqual(forward_angle([0, 0, 1], [1, 0, 0]), 90)
        self.assertIsNone(forward_angle([0, 0, 0], [0, 0, 1]))

    def test_disappeared_body_does_not_have_a_final_sample_difference(self):
        metric = Metric()
        metric.add({'x': 0, 'y': 0, 'z': 0}, {'x': .3, 'y': .4, 'z': 0}, (0, 1, 0))
        report = metric.report((0, 2, 0))
        self.assertIsNone(report['endDifference'])
        self.assertEqual(report['lastMatchedObservation']['difference'], .5)

    def test_alignment_ids_proxies_and_exact_events(self):
        zero, rotation = {'x': 0, 'y': 0, 'z': 0}, {'x': 0, 'y': 0, 'z': 0, 'w': 1}
        def body(identifier, name):
            return {'id': identifier, 'name': name, 'position': zero, 'velocity': zero,
                    'rotation': rotation, 'hasRigidbody': False, 'components': []}
        def sample(frame, actual=False):
            s = {'gameplayFrame': frame, 'scene': 's_Day_3_4', 'levelFrameZero': 200 if actual else 100,
                'score': 68, 'delivered': 1, 'entities': [body(103, 'Player 1'), body(33, 'counter'),
                    {**body(124, 'Frankfurter_Rigidbody'), 'hasRigidbody': True,
                     'components': ['ObjectContainer', 'X.ServerPhysicsObjectSynchroniser']}],
                'chefs': [{'entityId': 103, 'playerId': 0, 'name': 'Player 1',
                    'position': {'x': .3 if actual and frame == 1 else 0, 'y': .4 if actual and frame == 1 else 0, 'z': 0},
                    'velocity': zero, 'forward': {'x': 0, 'y': 0, 'z': 1}}],
                'gameEvents': [{'kind': 'delivery', 'recipeId': 158500, 'frame': 201 if actual else 101,
                    'fixedFrame': 1, 'unityFrame': 1, 'finalizedFrame': 202 if actual else 102, 'gameplayFrame': 1}]}
            if actual and frame == 1:
                s['entities'] = [e for e in s['entities'] if e['id'] != 33]
            return {'kind': 'call', 'request': {'command': 'inspect'}, 'response': {'state': s, 'inputs': []}}
        def write(path, rows):
            path.write_bytes(gzip.compress(b''.join(json.dumps(row).encode() + b'\n' for row in rows)))
        with tempfile.TemporaryDirectory() as directory:
            a, b = Path(directory) / 'a.jsonl.gz', Path(directory) / 'b.jsonl.gz'
            write(a, [sample(f) for f in [0, 0, 1, 2]])
            write(b, [sample(f, True) for f in [0, 1, 2]])
            report = measure(a, b)
            self.assertEqual(report['alignment']['matchedSamples'], 3)
            self.assertEqual(report['alignment']['expectedOnlySamples'], 1)
            chef = report['chefs'][0]
            self.assertEqual(chef['metrics']['position']['maximum'], .5)
            self.assertEqual(chef['metrics']['position']['endDifference']['difference'], 0)
            self.assertEqual(chef['expectedChefEntityIds'], [103])
            self.assertEqual(report['physicalProxies']['observedIDs'], 1)
            self.assertEqual(report['nativeEntities']['bodiesWithDifferencesOrMissingData'][0]['missingActualSamples'], 1)
            self.assertEqual(report['exactChecks']['nativeEvents']['differentSamples'], 0)
            changed = [sample(f, True) for f in [0, 1, 2]]
            changed[-1]['response']['state']['gameEvents'][0]['recipeId'] = 99
            write(b, changed)
            report = measure(a, b)
            self.assertEqual(report['exactChecks']['nativeEvents']['differentSamples'], 1)


if __name__ == '__main__':
    unittest.main()
