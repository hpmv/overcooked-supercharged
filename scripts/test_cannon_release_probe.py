import copy
import unittest

from check_cannon_release_probe import check


def fixture():
    rows = []
    receipt = {'nativeFlying': True, 'destinationRegion': 'lower-right', 'firingPlayer': 0, 'passengerPlayer': 2,
               'cannon': 84, 'cannonOrdinal': 83, 'button': 78, 'buttonOrdinal': 77, 'passenger': 105, 'passengerOrdinal': 104,
               'held': 10, 'heldOrdinal': 9, 'fireEdgeFrame': 0, 'launchFrame': 1, 'landingTarget': {'x': 26.7, 'z': -20.3}}
    neutral = lambda p: dict(player=p, x=0, y=0, pickup=False, use=False, dash=False)
    for frame in range(6):
        chefs = {p: dict(playerId=p, entityId=103+p, heldEntityId=10 if p == 2 else 0,
                         controlsEnabled=p != 2 or frame >= 4, directlyControlled=True, canAcceptInput=True,
                         respawning=False, inputSuppressed=False, useTargetId=78 if p == 0 else 0,
                         position={'x': .1 if p == 0 and frame >= 3 else 0, 'y': .3, 'z': 0}) for p in range(4)}
        if frame >= 4:
            chefs[2]['position'] = dict(x=26.7, y=.3, z=-20.3)
        entities = {84: dict(observedOrdinal=83, cannonState='Load' if frame == 0 else 'Launched', cannonReady=True,
                             cannonFlying=0 < frame < 4, cannonLoadedEntityId=105),
                    105: dict(observedOrdinal=104, active=True), 10: dict(observedOrdinal=9, active=True)}
        inputs = [neutral(p) for p in range(4)]
        if frame == 1:
            inputs[0]['use'] = True
        if frame == 3:
            inputs[0]['x'] = 1
        rows.append({'request': dict(version=1, command='restart' if frame == 0 else 'step', steps=1, inputs=inputs),
                     'state': dict(frame=frame, timer=270-frame/60, clientTime=frame/60, score=0, delivered=0,
                                   chefs=chefs, entities=entities, pluginSha256='offline-fixture')})
    events = [{'name': 'transportLaunchConfirmed', 'value': receipt},
              {'name': 'jobComplete', 'value': {'id': 'fire-until-native-launch', 'frame': 2}}]
    result = {'ok': True, 'state': {'gameplayFrame': 5, 'score': 0}, 'inputs': [neutral(p) for p in range(4)]}
    return rows, events, result


class CannonProbeTests(unittest.TestCase):
    def test_bounded_observed_overlap(self):
        report = check(*fixture())
        self.assertEqual(report['arrivalSamples'], [4, 5])
        self.assertEqual(report['firingChefMovementDuringFlightFrames'], [3])

    def test_rejects_claimed_overlap_without_movement(self):
        rows, events, result = fixture()
        rows[3]['state']['chefs'][0]['position']['x'] = 0
        with self.assertRaisesRegex(ValueError, 'movement overlapped'):
            check(rows, events, result)

    def test_rejects_early_passenger_input(self):
        rows, events, result = fixture()
        rows[2]['request']['inputs'][2]['pickup'] = True
        with self.assertRaisesRegex(ValueError, 'Passenger received input'):
            check(rows, events, result)

    def test_rejects_reused_plate_id(self):
        rows, events, result = fixture()
        rows[3]['state']['entities'][10]['observedOrdinal'] = 999
        with self.assertRaisesRegex(ValueError, 'identity changed'):
            check(rows, events, result)

    def test_rejects_stale_launched_state(self):
        rows, events, result = fixture()
        rows[1]['state']['entities'][84]['cannonFlying'] = False
        with self.assertRaisesRegex(ValueError, 'authoritative launch'):
            check(rows, events, result)

    def test_rejects_unreleased_fire(self):
        rows, events, result = fixture()
        rows[2]['request']['inputs'][0]['use'] = True
        with self.assertRaises(ValueError):
            check(rows, events, result)

    def test_rejects_single_arrival_sample(self):
        rows, events, result = fixture()
        rows[4]['state']['chefs'][2]['controlsEnabled'] = False
        with self.assertRaisesRegex(ValueError, 'two bounded'):
            check(rows, events, result)


if __name__ == '__main__':
    unittest.main()
