import copy
import unittest
from check_flavor_throw import Checker, JOBS


def leaf(identity):
    return {"type": "IngredientAssembledNode", "id": identity, "children": []}


def run_fixture(flavor="Chocolate", change=lambda frame, state: None):
    flavor_id = 22804 if flavor == "Chocolate" else 129618
    def entity(identity, components, x, z, attachment=0, food=None):
        return {"id": identity, "observedOrdinal": identity + 10, "name": "fixture", "active": True, "components": components,
                "position": {"x": x, "y": .5, "z": z}, "attachedEntityId": attachment, "composition": food}
    home = entity(100, ["MixingStation"], 24, -10.8, 101)
    bowl = entity(101, ["MixableContainer", "IngredientCatcher"], 23.992, -10.995, food={"type": "MixedCompositeAssembledNode", "state": "Unmixed", "children": []})
    bowl["mixingTime"] = 12
    far_home = entity(102, ["MixingStation"], 22.8, -10.8, 103)
    other = entity(103, ["MixableContainer"], 22.792, -10.995)
    basket = entity(104, ["CookableContainer"], 22.8, -21.6, food={"type": "CookedCompositeAssembledNode", "state": "Raw", "cookingStepId": 17160, "children": []})
    basket["cookingTime"] = 10
    board = entity(105, ["Workstation"], 25.2, -14.4)
    state = {"gameplayFrame": 0, "frame": 100, "scene": "s_Day_3_4", "score": 0, "delivered": 0, "deductions": 0,
             "entities": [home, bowl, far_home, other, basket, board],
             "chefs": [{"playerId": i, "entityId": 1000 + i, "heldEntityId": 0, "controlsEnabled": True,
                        "position": {"x": 27.2, "y": .05, "z": -12.2}, "throwForce": 18, "throwInclination": 12} for i in range(4)]}
    inputs = [{"player": i, "x": 0, "y": 0, "pickup": False, "use": False, "dash": False} for i in range(4)]
    checker = Checker(flavor)
    def sample(frame):
        state["gameplayFrame"], state["frame"] = frame, 100 + frame
        value = copy.deepcopy(state)
        change(frame, value)
        checker.state(value, inputs)
    sample(0)
    for job in JOBS[:2]:
        checker.event("jobComplete", {"id": job})
    bowl["composition"]["children"] = [leaf(16620), leaf(18448)]
    raw = entity(200, ["WorkableItem"], 25.2, -14.4)
    raw.update(name=flavor, workProgress=0)
    state["entities"].append(raw); board["attachedEntityId"] = 200
    state["chefs"][1].update(interactingEntityId=105, serverInteractionId=105)
    sample(1); raw["workProgress"] = .9; sample(2)
    state["entities"].remove(raw)
    prepared = entity(201, ["ThrowableItem"], 25.2, -14.4, food=leaf(flavor_id))
    prepared.update(throwFlying=False, throwerEntityId=0)
    state["entities"].append(prepared); board["attachedEntityId"] = 201
    sample(3); board["attachedEntityId"] = 0; state["chefs"][1]["heldEntityId"] = 201; sample(4)
    checker.event("throwReleased", {"player": 1, "itemId": 201, "frame": 104})
    state["chefs"][1]["heldEntityId"] = 0; prepared.update(throwFlying=True, throwerEntityId=1001); sample(5)
    state["entities"].remove(prepared); bowl["composition"]["children"].append(leaf(flavor_id)); sample(6)
    checker.event("throwComplete", {"player": 1, "itemId": 201, "targetEntityId": 101})
    checker.event("jobComplete", {"id": JOBS[2]})
    bowl["composition"]["state"] = "Mixed"; sample(7)
    home["attachedEntityId"] = 0; state["chefs"][3]["heldEntityId"] = 101; sample(8)
    basket["composition"]["children"] = [copy.deepcopy(bowl["composition"])]
    bowl["composition"]["children"] = []; sample(9)
    home["attachedEntityId"] = 101; state["chefs"][3]["heldEntityId"] = 0; basket["composition"]["state"] = "Cooked"; sample(10)
    checker.event("jobComplete", {"id": JOBS[3]})
    return checker.finish()


class FlavorThrowTests(unittest.TestCase):
    def test_chocolate(self):
        self.assertEqual(run_fixture()["finalRecipeId"], 228996)

    def test_raspberry(self):
        self.assertEqual(run_fixture("Raspberry")["finalRecipeId"], 130976)

    def test_source_identity_change(self):
        def change(frame, state):
            if frame == 5:
                next(e for e in state["entities"] if e["id"] == 201)["observedOrdinal"] += 1
        with self.assertRaisesRegex(ValueError, "throwable changed"):
            run_fixture(change=change)

    def test_no_native_flight(self):
        def change(frame, state):
            if frame == 5:
                next(e for e in state["entities"] if e["id"] == 201)["throwFlying"] = False
        with self.assertRaisesRegex(ValueError, "without observed native P1 flight"):
            run_fixture(change=change)

    def test_receiving_home_moved(self):
        def change(frame, state):
            if frame == 5:
                next(e for e in state["entities"] if e["id"] == 100)["attachedEntityId"] = 0
        with self.assertRaisesRegex(ValueError, "original mixer"):
            run_fixture(change=change)

    def test_wrong_other_bowl(self):
        def change(frame, state):
            if frame == 5:
                next(e for e in state["entities"] if e["id"] == 103)["composition"] = leaf(22804)
        with self.assertRaisesRegex(ValueError, "other original bowl"):
            run_fixture(change=change)

    def test_missing_chop_work(self):
        def change(frame, state):
            if frame in [1, 2]:
                state["chefs"][1]["serverInteractionId"] = 0
        with self.assertRaisesRegex(ValueError, "chopping"):
            run_fixture(change=change)

    def test_wrong_final_cooking(self):
        def change(frame, state):
            if frame == 10:
                next(e for e in state["entities"] if e["id"] == 104)["composition"]["cookingStepId"] = 20068
        with self.assertRaisesRegex(ValueError, "not correctly cooked"):
            run_fixture(change=change)


if __name__ == "__main__":
    unittest.main()
