"""Bounded four-chef macro scheduler for the validated Carnival 3-4 layout.

No sockets, execution, physics simulation, score prediction or state corrections.
Call Scheduler.advance(Observation(...)) at settled, jointly sampled boundaries;
send the returned `request` to the V5 `actions` endpoint, then collect a fresh
full inspection + nativeRound + NativeFoodSnapshot. Keep this Scheduler per
branch; alternatives() returns independent copies for a short-horizon evaluator.

An original action's Done is an input receipt, never a food/score proof. Every
phase retains its leases until its native postcondition is observed. One phase
per chef per batch bounds unobserved work. Long processing runs independently.
The first implementation deliberately uses shared-counter handoffs, not throws.
Cannon/portal travel is reported as a concrete transport prerequisite: V5 has no
native arrival barrier, so this module does not invent a timed flight macro.
Delivery and clean-plate circulation work when the outer chef is on that island.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
import math
from collections import Counter
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Mapping

from framework_search import (BUN, SAUSAGE, ONION, FLOUR, EGG, CHOCOLATE,
                              RASPBERRY, KETCHUP, MUSTARD, RECIPES,
                              RAW_CLASSES, PREPARED_CLASSES, INGREDIENTS)


class PlanningError(ValueError):
    """A missing proof, ownership violation or expired bounded attempt."""


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False)


def finite(value, label):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
        raise PlanningError(f"{label} must be finite")
    return float(value)


def tree_facts(tree):
    """Exact multiplicities and per-leaf native preparation, not set membership."""
    counts, cooked, mixed = Counter(), Counter(), Counter()
    ruined = False

    def walk(node, hot=False, dough=False, depth=0):
        nonlocal ruined
        if not isinstance(node, dict) or depth > 24:
            raise PlanningError("Missing or overdeep native food tree")
        state = node.get("state", "").lower()
        ruined |= state in {"burnt", "burned", "overmixed", "ruined"}
        hot |= state == "cooked"
        dough |= state == "mixed"
        if node.get("type") == "IngredientAssembledNode":
            ingredient = node.get("id")
            if not isinstance(ingredient, int):
                raise PlanningError("Native ingredient ID absent")
            counts[ingredient] += 1
            if hot:
                cooked[ingredient] += 1
            if dough:
                mixed[ingredient] += 1
        for child in node.get("children", []) + node.get("optional", []):
            walk(child, hot, dough, depth + 1)
    walk(tree)
    return counts, cooked, mixed, ruined


# Coordinates identify validated fixed prop roles, never substitute native IDs.
# A role requires the live class, recorded initial pose, native components and
# current fixed pose. It is resolved anew from actual registry metadata.
ROLE_SPECS = {
    "bun-board": ("board", 15.6, -14.4, "Workstation"),
    "onion-board": ("board", 15.6, -12.0, "Workstation"),
    "flavor-board": ("board", 25.2, -14.4, "Workstation"),
    "egg-stage": ("board", 25.2, -12.0, "Workstation"),
    "left-pass": ("counter", 15.6, -13.2, "AttachStation"),
    "right-pass": ("counter", 25.2, -13.2, "AttachStation"),
    "clean-pass": ("counter", 15.6, -20.4, "AttachStation"),
    "dirty-pass": ("counter", 15.6, -19.2, "AttachStation"),
    "washer-pass": ("counter", 15.6, -18.0, "AttachStation"),
    "service-pass": ("counter", 25.2, -20.4, "AttachStation"),
    "service-pass-2": ("counter", 25.2, -19.2, "AttachStation"),
    "dispenser": ("condiment-dispenser", 20.4, -10.8, "PlacementItemSpawner"),
    "switch": ("button", 20.4, -15.6, "TriggerColourCycle"),
    "serve": ("serve", 32.4, -19.8, "PlateStation"),
    "sink": ("sink", 13.207, -21.6, "WashingStation"),
    "dryer": ("clean-plate-spawner", 12.005, -21.6, "AttachStation"),
}
CRATE_SPAWN = {BUN: "HotdogBun", SAUSAGE: "Frankfurter", ONION: "DLC08_Onion",
               FLOUR: "Flour", EGG: "Egg", CHOCOLATE: "Chocolate", RASPBERRY: "Raspberry"}
CRATE_CLASS = {value: key + "-crate" for key, value in RAW_CLASSES.items()}
STAGE_ROLE = {BUN: "bun-board", ONION: "onion-board", SAUSAGE: "left-pass",
              FLOUR: "right-pass", EGG: "egg-stage", CHOCOLATE: "flavor-board", RASPBERRY: "flavor-board"}
CHOP = {BUN, ONION, CHOCOLATE, RASPBERRY}
VESSEL_KIND = {SAUSAGE: "pot", ONION: "pan"}
GUARDS = {"pot": ("cooking", 23.0), "pan": ("cooking", 23.0),
          "frier": ("cooking", 19.0), "mixer": ("mixing", 21.0)}


def region(position):
    x, z = finite(position.get("x"), "position x"), finite(position.get("z"), "position z")
    if 16 <= x <= 24.9 and -21.2 <= z <= -11.2:
        return "center"
    if 9.3 <= x <= 14.7:
        return "UL" if -14.6 <= z <= -11.1 else "LL" if -20.8 <= z <= -17.5 else None
    if 26.1 <= x <= 31.5:
        return "UR" if -14.6 <= z <= -11.1 else "LR" if -20.8 <= z <= -17.5 else None
    return None


@dataclass
class Observation:
    snapshot: Mapping
    native_round: Mapping
    food: Mapping | list = field(default_factory=dict)
    entities: dict = field(init=False)
    registry: dict = field(init=False)
    initial: dict = field(init=False)
    foods: dict = field(init=False)
    paths: dict = field(init=False)

    def __post_init__(self):
        s, n = self.snapshot, self.native_round
        if s.get("state") != "Paused" or s.get("requestPending") or s.get("errors"):
            raise PlanningError("Scheduler requires a settled error-free paused inspection")
        validation = s.get("registryValidation", {})
        if not s.get("freshLevelLoadObserved") or not validation.get("layoutValid") or validation.get("errors") or validation.get("semanticGaps"):
            raise PlanningError("Actual fresh native fixed-layout/semantic validation is required")
        if not n.get("available") or n.get("gameState", "InLevel") != "InLevel":
            raise PlanningError("An active native round observation is required")
        if not isinstance(s.get("frame"), int) or s["frame"] < 0:
            raise PlanningError("Native observation frame is invalid")
        finite(n.get("elapsed"), "native elapsed")
        rows = [e for e in s.get("entities", []) if e.get("exists")]
        self.entities = {e["id"]: e for e in rows}
        self.paths = {tuple(e["path"]): e["id"] for e in rows}
        if len(self.entities) != len(rows) or len(self.paths) != len(rows):
            raise PlanningError("Duplicate native entity ID/path")
        self.registry = {e["EntityId"]: e for e in s.get("registry", [])}
        self.initial = {e["EntityId"]: e for e in s.get("initialRegistry", [])}
        self.foods = self._food_records(self.food)
        for eid, record in self.foods.items():
            if eid in self.entities and record.get("composition") is not None and tree_facts(record["composition"])[3]:
                raise PlanningError(f"Native ruined food observed on active entity {eid}")

    @staticmethod
    def _food_records(document):
        if isinstance(document, list):
            return {e["id"]: e for e in document}
        if "detail" in document:
            document = document["detail"]
        if "entities" in document:
            return {e["id"]: e for e in document["entities"]}
        return {int(k): (v if "composition" in v else {"id": int(k), "composition": v}) for k, v in document.items()}

    @property
    def frame(self):
        return self.snapshot["frame"]

    @property
    def elapsed(self):
        return self.native_round["elapsed"]

    def entity(self, eid):
        if eid not in self.entities or eid not in self.registry:
            raise PlanningError(f"Native entity/registration unavailable: {eid}")
        return self.entities[eid]

    def token(self, eid):
        e, r = self.entity(eid), self.registry[eid]
        # Original framework paths distinguish spawned incarnations. These are
        # observed identity tokens, not an invented Unity creation sequence.
        return canonical([eid, e["path"], e["className"], r["Name"], r.get("UnityInstanceId")])

    def parent(self, eid):
        path = self.entity(eid).get("data", {}).get("attachmentParent", {}).get("path", [])
        return self.paths.get(tuple(path)) if path else None

    def held(self, chef):
        children = [eid for eid in self.entities if self.parent(eid) == chef]
        if len(children) > 1:
            raise PlanningError(f"Chef {chef} has ambiguous held attachments")
        return children[0] if children else None

    def on(self, station):
        children = [eid for eid in self.entities if self.parent(eid) == station]
        if len(children) > 1:
            raise PlanningError(f"Station {station} has ambiguous attachments")
        return children[0] if children else None

    def facts(self, eid):
        record = self.foods.get(eid, {})
        return tree_facts(record["composition"]) if record.get("composition") is not None else None

    def ingredients(self, eid):
        facts = self.facts(eid)
        kind = self.entity(eid)["className"]
        if facts and facts[0]:
            return facts[0]
        if kind in RAW_CLASSES:
            return Counter([RAW_CLASSES[kind]])  # Raw prefab still requires native work evidence for chopping.
        return Counter()

    def prepared(self, eid, ingredient):
        e, facts = self.entity(eid), self.facts(eid)
        if not facts or facts[0][ingredient] != 1:
            return False
        if ingredient in {SAUSAGE, ONION}:
            return facts[1][ingredient] == 1
        if ingredient in CHOP:
            return e["className"] not in RAW_CLASSES
        return True

    def role(self, name):
        kind, x, z, component = ROLE_SPECS[name]
        found = []
        for eid, e in self.entities.items():
            if e["className"] != kind or eid not in self.initial or eid not in self.registry:
                continue
            p, current = self.initial[eid]["Pos"], e["position"]
            if max(abs(p["X"]-x), abs(p["Z"]-z), abs(current["x"]-x), abs(current["z"]-z)) > .04:
                continue
            if component not in self.registry[eid].get("Components", []):
                continue
            found.append(eid)
        if len(found) != 1:
            raise PlanningError(f"Fixed role {name} needs one live native class/component/pose match; found {found}")
        return found[0]

    def crate(self, ingredient):
        found = [eid for eid, e in self.entities.items() if e["className"] == CRATE_CLASS[ingredient]
                 and eid in self.initial and eid in self.registry
                 and CRATE_SPAWN[ingredient] in self.registry[eid].get("SpawnNames", [])]
        if len(found) != 1:
            raise PlanningError(f"Native {INGREDIENTS[ingredient]} crate spawn metadata is not unique")
        return found[0]

    def accessible(self, chef, target):
        """Coarse island admission only; original graph still validates its path."""
        place = region(self.entity(chef)["position"])
        p = self.entity(target)["position"]
        x, z = p["x"], p["z"]
        return bool(place == "center" and 15.55 <= x <= 25.25
                    or place == "UL" and x <= 15.65 and z >= -14.45
                    or place == "UR" and x >= 25.15 and z >= -14.45
                    or place == "LL" and x <= 15.65 and z <= -17.8
                    or place == "LR" and x >= 25.15 and z <= -17.8)


@dataclass(frozen=True)
class RecipeTask:
    key: str
    kind: str
    after: tuple[str, ...]
    ingredients: tuple[int, ...] = ()


def recipe_dag(order):
    recipe = RECIPES.get(order.get("recipeId"))
    if not recipe:
        raise PlanningError(f"Unknown actual native recipe {order.get('recipeId')}")
    prefix = f"order:{order['id']}:"
    tasks = []
    for ingredient in recipe:
        if ingredient in {MUSTARD, KETCHUP}:
            continue
        key = prefix + INGREDIENTS[ingredient]
        tasks.append(RecipeTask(key+":supply", "supply", (), (ingredient,)))
        if ingredient in CHOP:
            tasks.append(RecipeTask(key+":chop", "chop", (key+":supply",), (ingredient,)))
    if BUN in recipe:
        tasks.append(RecipeTask(prefix+"sausage:cook", "cook", (prefix+"sausage:supply",), (SAUSAGE,)))
        base = [prefix+"bun:chop", prefix+"sausage:cook"]
        if ONION in recipe:
            tasks.append(RecipeTask(prefix+"onion:cook", "cook", (prefix+"onion:chop",), (ONION,)))
            base.append(prefix+"onion:cook")
    else:
        flavor = CHOCOLATE if CHOCOLATE in recipe else RASPBERRY
        tasks.append(RecipeTask(prefix+"mix", "mix", (prefix+"flour:supply", prefix+"egg:supply", prefix+INGREDIENTS[flavor]+":chop"), tuple(recipe)))
        tasks.append(RecipeTask(prefix+"fry", "fry", (prefix+"mix",), tuple(recipe)))
        base = [prefix+"fry"]
    tasks.append(RecipeTask(prefix+"plate", "plate", tuple(base), tuple(i for i in recipe if i not in {MUSTARD, KETCHUP})))
    last = prefix+"plate"
    for condiment in (MUSTARD, KETCHUP):
        if condiment in recipe:
            key = prefix+INGREDIENTS[condiment]
            tasks.append(RecipeTask(key, "sauce", (last,), (condiment,)))
            last = key
    tasks.append(RecipeTask(prefix+"deliver", "deliver", (last,), tuple(recipe)))
    return tuple(tasks)


@dataclass
class Job:
    key: str
    kind: str
    order: int
    chef: int
    source: int
    target: int
    ingredient: int | None
    resources: tuple[int, ...]
    phase: str = "take"
    held: int | None = None
    started: int = 0
    issued: int | None = None
    action_id: str | None = None
    before: dict = field(default_factory=dict)
    bindings: dict = field(default_factory=dict)
    sequence: int = 0
    deadline: int = 600


class Scheduler:
    """One mutable scheduler per search branch; no mutable global reservations."""
    def __init__(self, observation: Observation, lookahead=2, action_timeout=180):
        if not 1 <= lookahead <= 8 or not 20 <= action_timeout <= 600:
            raise PlanningError("lookahead1..8 and action_timeout20..600 required")
        self.lookahead, self.action_timeout = lookahead, action_timeout
        self.orders, self.claims, self.jobs, self.leases, self.homes = {}, {}, {}, {}, {}
        self.events, self.completed_orders = [], set()
        self.last_frame, self.last_elapsed = observation.frame, observation.elapsed
        self.last_boundary = None
        self.serial = 0
        self.role_chefs = {"center": [], "left": [], "right": []}
        for eid, e in observation.entities.items():
            if e.get("chef") is not None:
                if "PlayerControls" not in observation.registry.get(eid, {}).get("Components", []):
                    raise PlanningError("Chef lacks actual native PlayerControls metadata")
                where = region(e["position"])
                role = "center" if where == "center" else "left" if where == "UL" else "right" if where == "UR" else None
                if not role:
                    raise PlanningError("Initialize from observed two-center/two-upper-pantry positions")
                self.role_chefs[role].append(eid)
            if e["className"] in GUARDS:
                home = observation.parent(eid)
                if home is None or observation.on(home) != eid or home not in observation.initial:
                    raise PlanningError(f"Cookware {eid} needs its observed original native home")
                self.homes[eid] = (home, observation.token(eid), observation.token(home))
        if sorted(map(len, self.role_chefs.values())) != [1, 1, 2]:
            raise PlanningError("Exactly four native chefs with two central workers required")
        for values in self.role_chefs.values():
            values.sort()
        self._orders(observation)

    def _event(self, observation, kind, **fields):
        self.events.append({"frame": observation.frame, "kind": kind, **fields})
        self.events[:] = self.events[-256:]

    def _orders(self, o):
        active = {}
        for order in o.native_round.get("orders", []):
            oid = order.get("id")
            if not isinstance(oid, int) or oid in active or finite(order.get("remaining"), "order remaining") <= 0:
                raise PlanningError("Native active order identity/deadline invalid")
            recipe_dag(order)
            active[oid] = order
            if oid in self.orders and self.orders[oid]["recipeId"] != order["recipeId"]:
                raise PlanningError("Native FIFO order identity changed recipe")
        vanished = set(self.orders) - set(active) - self.completed_orders
        if vanished:
            raise PlanningError(f"Order vanished without this scheduler's native delivery proof: {sorted(vanished)}")
        self.orders.update({i: copy.deepcopy(v) for i, v in active.items()})

    def _claim(self, o, entity, order):
        token = o.token(entity)
        old = self.claims.get(token)
        if old is not None and old != order:
            raise PlanningError("One physical material cannot belong to two recipes")
        self.claims[token] = order

    def _unclaim(self, job, source):
        token = job.bindings.get(source)
        if token:
            self.claims.pop(token, None)

    def _allocate(self, o):
        tokens = {o.token(eid): eid for eid in o.entities}
        for token in list(self.claims):
            if token not in tokens:
                raise PlanningError("An assigned material disappeared without a verified consumption transition")
        allocations = {i: [] for i in sorted(self.orders) if i not in self.completed_orders}
        remaining = {i: Counter(RECIPES[self.orders[i]["recipeId"]]) for i in allocations}
        # Complete/partial composites first; no credit for loose duplicates later.
        assets = [(eid, o.ingredients(eid)) for eid, e in o.entities.items()
                  if e["className"] in set(RAW_CLASSES) | set(PREPARED_CLASSES) | set(GUARDS) | {"plate", "hotdog"}
                  and not (e.get("plateLifecycle") or {}).get("phase")
                  and (o.parent(eid) not in self.jobs or o.token(eid) in self.claims)]
        assets = [(eid, counts) for eid, counts in assets if counts]
        assets.sort(key=lambda item: (-sum(item[1].values()), item[0]))
        for eid, counts in assets:
            token = o.token(eid)
            owner = self.claims.get(token)
            choices = [owner] if owner is not None else list(allocations)[:self.lookahead]
            for order in choices:
                if order in remaining and counts <= remaining[order]:
                    allocations[order].append(eid)
                    remaining[order].subtract(counts)
                    self._claim(o, eid, order)
                    break
            else:
                if owner in remaining:
                    raise PlanningError(f"Assigned WIP has duplicate/incompatible contents for order {owner}")
        return allocations, remaining

    def _free(self, *resources):
        return all(resource not in self.leases for resource in resources)

    def _idle(self, o, chef):
        e = o.entity(chef)
        state = e.get("chef") or {}
        return chef not in self.jobs and self._free(chef) and o.held(chef) is None and region(e["position"]) is not None \
            and not state.get("aimingThrow") and not state.get("movementInputSuppressed") \
            and state.get("dashTimer", 0) <= 0 and state.get("impactTimer", 0) <= 0

    def _worker(self, o, targets, allowed=None):
        choices = allowed or self.role_chefs["center"]
        return next((c for c in sorted(choices, key=lambda c: sum(self._distance(o,c,t) for t in targets))
                     if self._idle(o,c) and all(o.accessible(c,t) for t in targets)), None)

    @staticmethod
    def _distance(o, a, b):
        pa, pb = o.entity(a)["position"], o.entity(b)["position"]
        return math.hypot(pa["x"]-pb["x"],pa["z"]-pb["z"])

    def _empty_counter(self, o, service=False):
        if service:
            return next((o.role(r) for r in ("service-pass", "service-pass-2")
                         if self._free(o.role(r)) and o.on(o.role(r)) is None), None)
        # Ordinary central storage only; handoffs, traffic center and cookware homes excluded.
        return next((eid for eid,e in sorted(o.entities.items()) if e["className"] == "counter"
                     and 18.9 <= e["position"]["x"] <= 21.7 and e["position"]["x"] != 20.4
                     and (e["position"]["z"] < -21.0 or e["position"]["z"] > -11.0)
                     and eid in o.initial and self._free(eid) and o.on(eid) is None), None)

    def _vessel(self, o, kind):
        return next((eid for eid,(home,token,htoken) in sorted(self.homes.items())
                     if o.entity(eid)["className"] == kind and o.token(eid)==token and o.token(home)==htoken
                     and o.parent(eid)==home and o.on(home)==eid and self._free(eid,home)
                     and o.facts(eid) is not None and not o.facts(eid)[0]), None)

    def _new(self, o, kind, order, chef, source, target, ingredient=None, extra=()):
        resources = tuple(sorted(set((chef,source,target,*extra))))
        if not self._free(*resources):
            return None
        self.serial += 1
        job = Job(f"task-{self.serial}",kind,order,chef,source,target,ingredient,resources,started=o.frame,
                  bindings={r:o.token(r) for r in resources},deadline=max(600,4*self.action_timeout))
        if kind in {"chop", "switch", "wash"}:
            job.phase="use"
        self.jobs[chef]=job
        for resource in resources:
            self.leases[resource]=job.key
        self._event(o,"job-start",job=job.key,task=kind,order=order,chef=chef,resources=list(resources))
        return job

    def _finish(self,o,job):
        for resource in job.resources:
            if self.leases.get(resource) != job.key:
                raise PlanningError("A finishing job no longer owns its exact lease")
            del self.leases[resource]
        del self.jobs[job.chef]
        self._event(o,"job-complete",job=job.key,task=job.kind,order=job.order)

    def _binding(self,o,job):
        for eid,token in job.bindings.items():
            # Only a native food merge/chop replacement may consume the source.
            may_disappear = job.issued is not None and (
                eid == job.source and job.kind == "chop"
                or eid == job.target and job.kind == "merge"
                or eid == job.held and job.kind in {"load", "deliver"})
            if eid not in o.entities:
                if may_disappear:
                    continue
                raise PlanningError(f"Reserved entity {eid} disappeared during {job.key}")
            if o.token(eid) != token:
                raise PlanningError(f"Reserved entity incarnation changed during {job.key}")

    def _receipt(self,o,job):
        status=o.snapshot.get("typedActions",{})
        if status.get("outcome") in {"failed","interrupted"}:
            raise PlanningError(f"Typed input graph {status.get('outcome')}: {status.get('error')}")
        return status.get("outcome")=="complete" and any(a.get("id")==job.action_id and a.get("endFrame") is not None for a in status.get("actions",[]))

    def _reconcile(self,o):
        for job in list(self.jobs.values()):
            self._binding(o,job)
            if o.frame-job.started > job.deadline:
                raise PlanningError(f"Observable task timeout {job.key}/{job.kind}")
            if job.issued is None:
                continue
            if o.frame-job.issued > self.action_timeout:
                raise PlanningError(f"Native postcondition timeout {job.key}/{job.phase}")
            if not self._receipt(o,job):
                continue
            held=o.held(job.chef)
            good=False
            if job.phase=="take":
                if job.kind=="supply":
                    good=held is not None and held not in job.before["existing"] and o.entity(held)["className"]==next(k for k,v in RAW_CLASSES.items() if v==job.ingredient)
                    if good:
                        spawn=o.registry.get(held,{})
                        path, crate_path=o.entity(held)["path"],o.entity(job.source)["path"]
                        good=bool(spawn.get("Name")) and o.ingredients(held)==Counter([job.ingredient]) \
                            and len(path)==len(crate_path)+1 and path[:len(crate_path)]==crate_path
                else:
                    good=held==job.source
                if good:
                    job.held=held;job.bindings[held]=o.token(held)
                    if job.kind=="supply":
                        self._claim(o,held,job.order)
                        if not self._free(held):raise PlanningError("New spawned ingredient is already reserved")
                        job.resources=tuple(sorted(set((*job.resources,held))))
                        self.leases[held]=job.key
                    job.phase="put"
            elif job.phase=="recover":
                good=held==job.held
                if good: job.phase="stage"
            elif job.phase=="put":
                if job.kind in {"supply","move","park","restore"}:
                    good=held is None and o.parent(job.held)==job.target and o.on(job.target)==job.held and o.ingredients(job.held)==Counter(job.before["contents"])
                elif job.kind in {"load","merge","pour"}:
                    target=job.target
                    target_facts=o.facts(target)
                    expected=Counter(job.before["destination"])+Counter(job.before["contents"])
                    good=bool(target_facts and target_facts[0]==expected)
                    if job.kind=="load":
                        good &= job.held not in o.entities and held is None
                    elif job.kind=="pour":
                        good &= held==job.held and o.facts(job.held) is not None and not o.facts(job.held)[0]
                    else:
                        # Held plate placed onto/under exact native food. Native
                        # plate may be held or attached under it; recover separately.
                        good=bool(o.facts(job.held) and o.facts(job.held)[0]==expected)
                        if target in o.entities and o.entity(target)["className"] in GUARDS:
                            good &= o.facts(target) is not None and not o.facts(target)[0]
                        else:
                            good &= target not in o.entities
                    if good:
                        consumed=job.target if job.kind=="merge" else job.held
                        self._unclaim(job,consumed)
                        self._claim(o,job.held if job.kind=="merge" else job.target,job.order)
                        if consumed not in o.entities:
                            job.bindings.pop(consumed,None)
                            job.resources=tuple(r for r in job.resources if r!=consumed)
                            if self.leases.pop(consumed,None)!=job.key:
                                raise PlanningError("Native consumption did not match the owned resource")
                elif job.kind=="sauce":
                    good=held==job.held and o.facts(held) is not None and o.facts(held)[0]==Counter(job.before["contents"])+Counter([job.ingredient])
                elif job.kind=="deliver":
                    ledger=o.native_round.get("ledger",{})
                    plate=o.entities.get(job.held,{})
                    served=(plate.get("plateLifecycle") or {}).get("phase")==1 or job.held not in o.entities
                    good=served and held is None and job.order not in {a["id"] for a in o.native_round.get("orders",[])} \
                        and ledger.get("deliveries")==job.before["deliveries"]+1 \
                        and ledger.get("baseScore",0)-job.before["baseScore"]==self.orders[job.order]["baseValue"]
                    if good:
                        self._unclaim(job,job.held);self.completed_orders.add(job.order)
                if good and job.kind=="pour":
                    job.phase="restore"
                elif good and job.kind in {"merge","sauce"}:
                    job.phase="stage" if held==job.held else "recover"
                elif good:
                    self._finish(o,job)
            elif job.phase in {"stage","restore"}:
                destination=job.before["output"] if job.phase=="stage" else self.homes[job.held][0]
                good=held is None and o.parent(job.held)==destination and o.on(destination)==job.held \
                    and o.ingredients(job.held)==Counter(job.before["contents"])
                if good: self._finish(o,job)
            elif job.phase=="use":
                if job.kind=="chop":
                    prepared=o.on(job.target)
                    good=prepared is not None and o.entity(prepared)["className"] in PREPARED_CLASSES and o.facts(prepared) is not None and o.ingredients(prepared)==Counter([job.ingredient])
                    if good:
                        self._unclaim(job,job.source);self._claim(o,prepared,job.order);self._finish(o,job)
                    elif job.source in o.entities and o.foods.get(job.source,{}).get("workStage",0)+o.foods.get(job.source,{}).get("workSubStage",0)>job.before.get("work",0):
                        good=True  # Advance one observed native chop edge, not the recipe task.
                elif job.kind=="switch":
                    good=o.ingredients(job.target)==Counter([job.ingredient])
                    if good:self._finish(o,job)
                elif job.kind=="wash":
                    good=o.on(job.target) is not None
                    if good:self._finish(o,job)
            if good:
                job.issued=None;job.action_id=None;job.sequence+=1
                self._event(o,"native-barrier",job=job.key,nextPhase=job.phase)
            # An input receipt can precede a native animation/attachment event.
            # Keep the exact leases and original deadline; never repeat the edge.

    def _emit(self,o,job):
        if job.issued is not None:
            return None
        action={"id":f"{job.key}-{job.sequence}","chef":job.chef,"timeoutFrames":self.action_timeout,
                "resources":list(job.resources),"dash":False}
        job.before.update({"existing":list(o.entities),"contents":dict(o.ingredients(job.held or job.source))})
        if job.phase=="take":
            action.update(type="pickup",target=job.source,expectSpawn=job.kind=="supply")
        elif job.phase=="recover":
            action.update(type="pickup",target=job.held)
        elif job.phase=="put":
            action.update(type="place",target=job.target)
            job.before["destination"]=dict(o.ingredients(job.target))
            if job.kind=="merge":
                # Merge source is held plate; native recipient is target food/vessel.
                job.before["destination"],job.before["contents"]=dict(o.ingredients(job.held)),dict(o.ingredients(job.target))
            if job.kind=="deliver":
                ledger=o.native_round["ledger"]
                job.before.update(deliveries=ledger["deliveries"],baseScore=ledger["baseScore"])
        elif job.phase in {"stage","restore"}:
            action.update(type="place",target=job.before["output"] if job.phase=="stage" else self.homes[job.held][0])
        else:
            action.update(type="interact",target=job.target if job.kind=="chop" else job.source)
            record=o.foods.get(job.source,{})
            job.before["work"]=record.get("workStage",0)+record.get("workSubStage",0)
        job.issued=o.frame;job.action_id=action["id"]
        self._event(o,"action-issued",job=job.key,phase=job.phase,action=copy.deepcopy(action))
        return action

    def _heat(self,o):
        pending=[]
        for vessel,(home,token,htoken) in self.homes.items():
            o.entity(vessel)
            if o.token(vessel)!=token or o.token(home)!=htoken:
                raise PlanningError("Original cookware/home identity changed")
            record=o.foods.get(vessel,{})
            counts=o.ingredients(vessel)
            if not counts or o.parent(vessel)!=home or o.on(home)!=vessel:
                continue
            clock,guard=GUARDS[o.entity(vessel)["className"]]
            if clock+"Progress" not in record:
                raise PlanningError(f"On-heat vessel {vessel} lacks actual native progress")
            progress=finite(record[clock+"Progress"],"native processing progress")
            slack=guard-progress
            if slack<=0: raise PlanningError(f"Native heat safety guard reached for vessel {vessel}")
            if slack<=6:
                pending.append((slack,vessel,home))
        return sorted(pending)

    def _admit(self,o,allocations,remaining,choice):
        blocked=[]
        urgent=self._heat(o)
        for slack,vessel,home in urgent:
            if not self._free(vessel,home):
                blocked.append(f"heat {vessel}: active job owns exact vessel; {slack:.3f}s guard remains")
                continue
            counter=self._empty_counter(o)
            chef=self._worker(o,[vessel,counter]) if counter is not None else None
            if chef is None:
                blocked.append(f"heat {vessel}: no idle central chef + empty offheat counter; electives blocked")
                continue
            # This lower-bound screen rejects impossible routes. It is not a
            # collision/arrival proof: action and global heat guards still apply.
            if self._distance(o,chef,vessel)/6+1.0>=slack:
                raise PlanningError(f"No measured slack to detach urgent vessel {vessel}")
            self._new(o,"park",self.claims.get(o.token(vessel),-1),chef,vessel,counter,extra=(home,))
        if urgent:
            return blocked
        # Empty offheat cookware must return to its own home before replenishment.
        for vessel,(home,_,_) in self.homes.items():
            if o.parent(vessel)!=home and o.facts(vessel) is not None and not o.ingredients(vessel) and o.on(home) is None:
                chef=self._worker(o,[vessel,home])
                if chef is not None:self._new(o,"restore",-1,chef,vessel,home)
        orders=[i for i in sorted(allocations) if i not in self.completed_orders][:self.lookahead]
        if choice and len(orders)>1:orders=orders[choice%len(orders):]+orders[:choice%len(orders)]
        for oid in orders:
            recipe=Counter(RECIPES[self.orders[oid]["recipeId"]])
            assets=allocations[oid]
            # Serve only the oldest actual order, using native complete plate.
            plates=[e for e in assets if o.entity(e)["className"]=="plate"]
            if plates:
                plate=plates[0];counts=o.ingredients(plate)
                complete=counts==recipe and self._ready(o,plate,recipe)
                if complete:
                    target=o.role("serve") if oid==min(orders) else None
                    chef=self._worker(o,[plate,target],self.role_chefs["left"]) if target is not None else None
                    if chef is not None and self._free(plate,target):self._new(o,"deliver",oid,chef,plate,target)
                    elif o.parent(plate) not in {o.role("service-pass"),o.role("service-pass-2")}:
                        output=self._empty_counter(o,True);chef=self._worker(o,[plate,output]) if output else None
                        if chef is not None:self._new(o,"move",oid,chef,plate,output)
                    else:blocked.append(f"order {oid}: complete plate {plate} awaits left pantry chef on LR; native cannon arrival adapter required")
                    continue
                missing=recipe-counts
                if sum(missing.values()) and all(i in {MUSTARD,KETCHUP} for i in missing):
                    condiment=next(i for i in (MUSTARD,KETCHUP) if missing[i])
                    dispenser=o.role("dispenser");switch=o.role("switch")
                    if o.ingredients(dispenser)!=Counter([condiment]):
                        chef=self._worker(o,[switch])
                        if chef is not None:self._new(o,"switch",oid,chef,switch,dispenser,condiment)
                    else:
                        output=self._empty_counter(o,True);chef=self._worker(o,[plate,dispenser,output]) if output else None
                        if chef is not None:
                            job=self._new(o,"sauce",oid,chef,plate,dispenser,condiment,(switch,output))
                            if job:job.before["output"]=output
                    continue
            # A native complete component can be added to a clean/partial plate.
            available_plates=plates or [e for e,v in sorted(o.entities.items()) if v["className"]=="plate"
                                      and not (v.get("plateLifecycle") or {}).get("phase") and o.facts(e) is not None and not o.ingredients(e)
                                      and o.parent(e) is not None and self._free(e,o.parent(e))]
            for source in assets:
                if source in plates or not self._ready(o,source,o.ingredients(source)) or o.entity(source)["className"] in RAW_CLASSES:
                    continue
                if not available_plates:break
                plate=available_plates[0]
                if not o.ingredients(source)<=recipe-o.ingredients(plate):continue
                output=self._empty_counter(o,True)
                chef=self._worker(o,[plate,source,output]) if output else None
                if chef is not None and self._free(source,plate):
                    if oid!=min(allocations) and len(available_plates)<=1 and not any(o.entity(e)["className"]=="plate" for e in allocations[min(allocations)]):
                        blocked.append(f"order {oid}: last available plate reserved for native FIFO head")
                        continue
                    extras=[output]
                    if o.parent(plate) is not None:extras.append(o.parent(plate))
                    if source in self.homes:extras.append(self.homes[source][0])
                    job=self._new(o,"merge",oid,chef,plate,source,extra=extras)
                    if job:job.before["output"]=output;self._claim(o,plate,oid)
                    break
            # Raw/chopped input processing remains addressed to this recipe.
            for source in assets:
                kind=o.entity(source)["className"];counts=o.ingredients(source)
                if not self._free(source) or o.parent(source) is None:continue
                if kind in RAW_CLASSES and RAW_CLASSES[kind] in CHOP:
                    ingredient=RAW_CLASSES[kind];board=o.parent(source)
                    if o.entity(board)["className"]!="board":continue
                    chef=self._worker(o,[board])
                    if chef is not None and source in o.foods:self._new(o,"chop",oid,chef,source,board,ingredient)
                    continue
                if sum(counts.values())==1 and kind not in GUARDS and kind!="plate":
                    ingredient=next(iter(counts))
                    if ingredient in CHOP and kind not in PREPARED_CLASSES:continue
                    vessel=None
                    if ingredient in VESSEL_KIND:
                        # Do not put onions on heat until its bun+sausage base exists.
                        base_ready=any(o.ingredients(a)[BUN] and o.ingredients(a)[SAUSAGE] and o.prepared(a,SAUSAGE) for a in assets)
                        bun_ready=any(o.prepared(a,BUN) for a in assets)
                        if ingredient==ONION and not base_ready or ingredient==SAUSAGE and not bun_ready:continue
                        vessel=self._vessel(o,VESSEL_KIND[ingredient])
                    elif ingredient in {FLOUR,EGG,CHOCOLATE,RASPBERRY}:
                        flavor=CHOCOLATE if recipe[CHOCOLATE] else RASPBERRY
                        kit=Counter([FLOUR,EGG,flavor]);staged=sum((o.ingredients(a) for a in assets),Counter())
                        # Require all ingredients already present and flavor native-prepared.
                        if not kit<=staged or not any(o.prepared(a,flavor) for a in assets):continue
                        partial=[a for a in assets if o.entity(a)["className"]=="mixer" and o.ingredients(a)<kit
                                 and not o.ingredients(a)[ingredient] and self._free(a,self.homes[a][0])]
                        vessel=partial[0] if partial else self._vessel(o,"mixer")
                    if vessel is not None:
                        chef=self._worker(o,[source,vessel])
                        if chef is not None:self._new(o,"load",oid,chef,source,vessel,ingredient,(o.parent(source),self.homes[vessel][0]))
                if kind=="mixer" and counts==recipe and o.facts(source) and o.facts(source)[2]==counts:
                    basket=self._vessel(o,"frier");chef=self._worker(o,[source,basket]) if basket else None
                    if chef is not None:self._new(o,"pour",oid,chef,source,basket,extra=(self.homes[source][0],self.homes[basket][0]))
            for ingredient,count in sorted(remaining[oid].items(),key=lambda item:(item[0]!=BUN,item[0])):
                if count<=0 or ingredient in {MUSTARD,KETCHUP}:continue
                if any(j.order==oid and j.kind=="supply" and j.ingredient==ingredient for j in self.jobs.values()):continue
                crate,stage=o.crate(ingredient),o.role(STAGE_ROLE[ingredient])
                chefs=self.role_chefs["left"] if ingredient in {BUN,SAUSAGE,ONION} else self.role_chefs["right"]
                chef=self._worker(o,[crate,stage],chefs)
                if chef is not None and o.on(stage) is None:self._new(o,"supply",oid,chef,crate,stage,ingredient)
                elif region(o.entity(chefs[0])["position"]) not in {"UL","UR"}:
                    blocked.append(f"order {oid}: {INGREDIENTS[ingredient]} needs supplier return to upper pantry; native portal arrival adapter required")
        # Clean handoff remains disjoint from dirty relay and service outputs.
        clean=o.role("clean-pass")
        if o.on(clean) is not None:
            plate=o.on(clean)
            if o.entity(plate)["className"]=="plate" and o.facts(plate) is not None and not o.ingredients(plate):
                storage=self._empty_counter(o);chef=self._worker(o,[plate,storage]) if storage else None
                if chef is not None:self._new(o,"move",-1,chef,plate,storage,extra=(clean,))
        return blocked

    @staticmethod
    def _ready(o,entity,counts):
        facts=o.facts(entity)
        if not facts or not counts or facts[0]!=counts:return False
        if counts[FLOUR] or counts[EGG]:return facts[1]==counts and facts[2]==counts
        return all(o.prepared(entity,i) for i in counts if i not in {MUSTARD,KETCHUP})

    def advance(self,observation: Observation,alternative=0):
        o=observation
        if o.frame<self.last_frame or o.elapsed<self.last_elapsed:
            raise PlanningError("Use the saved scheduler branch for a rewind; do not rebase reservations")
        self.last_frame,self.last_elapsed=o.frame,o.elapsed
        self._reconcile(o)
        self._orders(o)
        allocations,remaining=self._allocate(o)
        self._heat(o)  # Global guard still observes vessels held by existing jobs.
        boundary=(o.frame,o.elapsed)
        blocked=[]
        if self.last_boundary!=boundary:
            blocked=self._admit(o,allocations,remaining,alternative)
        actions=[]
        for job in sorted(self.jobs.values(),key=lambda j:j.chef):
            action=self._emit(o,job)
            if action:actions.append(action)
        self.last_boundary=boundary
        if not actions and self.jobs:blocked.append("Awaiting the original typed action receipt and exact native postcondition")
        if not actions and not blocked:blocked.append("No admissible current recipe task: processing, occupied workspace, missing native food proof, or unavailable island worker")
        return {"version":1,"kind":"carnival-observable-macro-batch","frame":o.frame,
                "request":{"command":"actions","maximumFrames":self.action_timeout+2,"actions":actions} if actions else None,
                "blocked":blocked,"reservations":{str(k):v for k,v in sorted(self.leases.items())},
                "allocation":{str(k):v for k,v in allocations.items()},
                "tasks":{str(i):[vars(t) for t in recipe_dag(order)] for i,order in sorted(self.orders.items()) if i not in self.completed_orders},
                "nativeLedger":copy.deepcopy(o.native_round.get("ledger",{})),
                "qualification":"Planner proposal and observed transitions only; no native execution or score forecast"}

    def alternatives(self,observation,limit=3):
        """Return (branch scheduler,batch) pairs; never mutate the parent branch."""
        if not 1<=limit<=8:raise PlanningError("Alternative count must be1..8")
        result=[];seen=set()
        for choice in range(limit):
            branch=copy.deepcopy(self);batch=branch.advance(observation,choice)
            key=canonical(batch["request"])
            if key not in seen:result.append((branch,batch));seen.add(key)
        return result


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--snapshot",type=Path,required=True)
    parser.add_argument("--native",type=Path,required=True)
    parser.add_argument("--food",type=Path)
    parser.add_argument("--lookahead",type=int,default=2)
    args=parser.parse_args()
    load=lambda p:json.loads(p.read_text(encoding="utf-8-sig"))
    snapshot=load(args.snapshot);native=load(args.native)
    native=native.get("bridge",native).get("nativeRound",native)
    o=Observation(snapshot,native,load(args.food) if args.food else {})
    print(json.dumps(Scheduler(o,args.lookahead).advance(o),indent=2))


if __name__=="__main__":main()
