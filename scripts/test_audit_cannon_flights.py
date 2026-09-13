import copy
import gzip
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("audit_cannon_flights", Path(__file__).with_name("audit_cannon_flights.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def fixture():
    def call(frame, flying, landed=False):
        chefs = [{"playerId": i, "entityId": 100 + i, "heldEntityId": 10 if i == 2 else 0, "position": {"x": 27 if landed and i == 2 else 20, "y": .05, "z": -20},
                  "controlsEnabled": i != 2 or landed, "directlyControlled": True, "canAcceptInput": i != 2 or landed, "inputSuppressed": False, "respawning": False} for i in range(4)]
        entities = [{"id": i, "observedOrdinal": i - 1, "cannonFlying": flying if i == 84 else False, "cannonState": "Launched" if flying or landed else "Load"}
                    for i in [84, 78, 100, 101, 102, 103, 10]]
        inputs = [{"player": i, "x": 0, "y": 0, "pickup": False, "use": False, "dash": False} for i in range(4)]
        return {"kind": "call", "response": {"inputs": inputs, "state": {"levelReady": True, "gameplayFrame": frame, "clientTime": frame / 60,
                "chefs": chefs, "entities": entities, "instrumentation": {"manifest": {"pluginSha256": "fixture-only"}}}}}
    flight = {"cannon": 84, "button": 78, "firingPlayer": 0, "passengerPlayer": 2, "passengerEntity": 102, "heldEntity": 10,
              "destinationRegion": "lower-right", "landingTarget": {"x": 27, "z": -20}, "landingRadius": 1,
              "ownedResources": [84, 102, 10], "frame": 0, "stableArrivalSamples": 0}
    def event(name, frame, **changes):
        return {"kind": "event", "name": name, "value": dict(copy.deepcopy(flight), frame=frame, **changes)}
    return [call(0, False), event("cannonArrivalReserved", 0), call(1, True), call(2, True),
            event("cannonFiringChefReleased", 2, launchReceipt={"nativeFlying": True, "passenger": 102, "held": 10, "launchFrame": 1}),
            call(3, True), call(4, False, True), call(5, False, True), event("cannonPassengerArrivalConfirmed", 5, stableArrivalSamples=2)]


class FlightAuditTests(unittest.TestCase):
    def run_fixture(self, data):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "fixture.jsonl.gz"
            with gzip.open(path, "wt", encoding="utf-8") as stream:
                for row in data:
                    stream.write(json.dumps(row) + "\n")
            return module.audit(path)

    def test_valid_native_shape(self):
        result = self.run_fixture(fixture())
        self.assertTrue(result["ok"])
        self.assertEqual(result["scope"], "closed trace")
        self.assertEqual(result["completedFlights"][0]["earlyReleaseFrames"], 3)
        self.assertEqual(len(result["traceSha256"]), 64)

    def test_changed_passenger_item(self):
        rows = fixture(); rows[5]["response"]["state"]["chefs"][2]["heldEntityId"] = 11
        self.assertFalse(self.run_fixture(rows)["ok"])

    def test_changed_native_identity(self):
        rows = fixture(); rows[5]["response"]["state"]["entities"][-1]["observedOrdinal"] = 400
        self.assertFalse(self.run_fixture(rows)["ok"])

    def test_passenger_non_neutral(self):
        rows = fixture(); rows[5]["response"]["inputs"][2]["x"] = 1
        self.assertFalse(self.run_fixture(rows)["ok"])

    def test_no_native_launch(self):
        rows = fixture(); rows[2]["response"]["state"]["entities"][0]["cannonFlying"] = False
        self.assertFalse(self.run_fixture(rows)["ok"])

    def test_missing_second_arrival_sample(self):
        rows = fixture(); rows[6]["response"]["state"]["chefs"][2]["controlsEnabled"] = False
        self.assertFalse(self.run_fixture(rows)["ok"])

    def test_release_without_neutral(self):
        rows = fixture(); rows[3]["response"]["inputs"][0]["use"] = True
        self.assertFalse(self.run_fixture(rows)["ok"])

    def test_pending_is_reported(self):
        result = self.run_fixture(fixture()[:-1])
        self.assertEqual(len(result["completedFlights"]), 0)
        self.assertEqual(len(result["pendingFlights"]), 1)


if __name__ == "__main__":
    unittest.main()
