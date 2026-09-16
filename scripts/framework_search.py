"""Deterministic, bounded authoring search over real framework evaluator callbacks.

This module never connects to a game, changes a clock/score, or simulates a score.
`bounded_beam` calls the supplied evaluator from a parent checkpoint; the evaluator
owns legal input execution, checkpoint restoration, and native receipt capture.
An Evaluation must include a separate bridge.nativeRound sample. Native score is
the primary result key; heuristic credits are dimensionless WIP, never points.

Headless schema: entities[].data.contents is a FLATTENED ingredient list. Its
cooking/mixing progress is native-message reconstruction but omits food-tree
states. Optional food_evidence[id] may contain an actual native FoodState tree
(`type`, `state`, `id`, `children`). Without that proof, loose composite cooked
state is deliberately unknown; this heuristic is not a serving validator.

Each physical food component is allocated once to remaining recipe demand.
Complete donut kits claim flour+egg+flavor together, before loose ingredients;
credits migrate from chop/mix to fry instead of being added at every stage.
Stage buckets explain work distribution. `pipeline` retains comparable progress
through those transitions and is used for Pareto diversity. Deadlines use the
verified Carnival timings, with configurable safety margin; they are penalties,
not a proof that a planned rescue can reach its vessel in time.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import random
import time
from collections import Counter
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any, Callable, Iterable, Mapping, Sequence

BUN, SAUSAGE, ONION = 262914, 284626, 461162
FLOUR, EGG, CHOCOLATE, RASPBERRY = 18448, 16620, 22804, 129618
KETCHUP, MUSTARD = 158482, 17094
INGREDIENTS = {BUN: "bun", SAUSAGE: "sausage", ONION: "onion", FLOUR: "flour", EGG: "egg",
               CHOCOLATE: "chocolate", RASPBERRY: "raspberry", KETCHUP: "ketchup", MUSTARD: "mustard"}
RECIPES = {
    158500: (BUN, SAUSAGE, MUSTARD), 224216: (BUN, SAUSAGE, KETCHUP),
    125780: (BUN, SAUSAGE, KETCHUP, MUSTARD), 472326: (BUN, SAUSAGE, ONION),
    257844: (BUN, SAUSAGE, ONION, KETCHUP), 47642: (BUN, SAUSAGE, ONION, MUSTARD),
    296560: (BUN, SAUSAGE), 228996: (FLOUR, EGG, CHOCOLATE), 130976: (FLOUR, EGG, RASPBERRY),
}
RAW_CLASSES = {"bread": BUN, "onion": ONION, "chocolate": CHOCOLATE, "berry": RASPBERRY,
               "sausage": SAUSAGE, "flour": FLOUR, "egg": EGG}
PREPARED_CLASSES = {"chopped-bread": BUN, "chopped-onion": ONION,
                    "chopped-chocolate": CHOCOLATE, "chopped-berry": RASPBERRY}
STAGES = ("sausage", "onion", "chop", "mix", "fry", "plates", "wash")
PIPELINES = ("bun", "sausage", "onion", "donut", "plates")


def number(value: Any, name: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
        raise ValueError(f"{name} must be a finite number")
    return float(value)


def integer(value: Any, name: str) -> int:
    if isinstance(value, bool) or not isinstance(value, int):
        raise ValueError(f"{name} must be an integer")
    return value


def canonical(value: Any) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False)


def identity(value: Any) -> str:
    return hashlib.sha256(canonical(value).encode()).hexdigest()


def path_id(value: Any) -> int | None:
    path = value.get("path", []) if isinstance(value, dict) else []
    return int(path[0]) if len(path) == 1 else None


def path_key(value: Any) -> tuple[int, ...] | None:
    path = value.get('path') if isinstance(value, dict) else None
    if path is None or path == []:
        return None
    if not isinstance(path, list) or any(integer(i, 'entity path component') < 0 for i in path):
        raise ValueError('Entity path must be a nonnegative integer array')
    return tuple(path)


def extract_samples(document: Mapping[str, Any]) -> tuple[dict, dict | None]:
    """Last full reconstruction and last nativeRound; not an atomicity claim.

    Integration receipts sample these sequentially. Caller/evaluator should use
    a paused boundary and retain both receipts for actual search comparisons.
    """
    snapshot = document if "entities" in document else None
    native = document.get("nativeRound") or document.get("bridge", {}).get("nativeRound")
    for step in document.get("steps", []):
        response = step.get("response", {})
        if "entities" in response:
            snapshot = response
        observed = response.get("bridge", {}).get("nativeRound")
        if observed and observed.get("available"):
            native = observed
    if snapshot is None:
        raise ValueError("A full headless inspection with entities is required")
    return dict(snapshot), native


@dataclass(frozen=True)
class Demand:
    index: int
    ingredients: tuple[int, ...]
    remaining: float | None
    source: str


def pending_demand(snapshot: Mapping, native: Mapping | None, lookahead: int) -> tuple[Demand, ...]:
    if not 1 <= lookahead <= 32:
        raise ValueError("lookahead must be1..32")
    by_index: dict[int, Demand] = {}
    for order in (native or {}).get("orders", []):
        recipe = RECIPES.get(order.get("recipeId"))
        remaining = number(order.get("remaining"), "order remaining")
        if recipe and remaining > 0:
            index = integer(order["id"], "native order id") - 1
            by_index[index] = Demand(index, recipe, remaining, "native-active-order")
    minimum = min(by_index) if by_index else int((native or {}).get("ledger", {}).get("deliveries", 0))
    for entity in snapshot["entities"]:
        flow = entity.get("data", {}).get("kitchenFlowController", {})
        for forecast in flow.get("futureOrders", []):
            index = integer(forecast.get("orderIndex", 0), "forecast orderIndex")
            ingredients = tuple(integer(i, "forecast ingredient") for i in forecast.get("ingredients", []))
            if index >= minimum and ingredients and tuple(sorted(ingredients)) in {tuple(sorted(r)) for r in RECIPES.values()}:
                by_index.setdefault(index, Demand(index, ingredients, None, "framework-forecast-unvalidated"))
    return tuple(by_index[i] for i in sorted(by_index)[:lookahead])


@dataclass(frozen=True)
class Credit:
    entity: int
    token: str
    claims: tuple[tuple[str, int], ...]
    stage: str
    pipeline: str
    value: float
    observation: str


@dataclass(frozen=True)
class Assessment:
    native_score: int | None
    native_deliveries: int | None
    native_deductions: int | None
    native_elapsed: float | None
    demand: tuple[Demand, ...]
    demand_caps: dict[str, int]
    stages: dict[str, float]
    pipeline: dict[str, float]
    credits: tuple[Credit, ...]
    useful_wip: float
    deadline_penalty: float
    resource_penalty: float
    waste_penalty: float
    free_counters: int
    deadlines: tuple[dict, ...]
    evidence_gaps: tuple[str, ...]

    @property
    def heuristic(self) -> float:
        return self.useful_wip - self.deadline_penalty - self.resource_penalty - self.waste_penalty

    @property
    def rank(self) -> tuple:
        return (self.native_score if self.native_score is not None else -math.inf,
                -(self.native_deductions or 0), self.heuristic,
                -(self.native_elapsed if self.native_elapsed is not None else math.inf))

    @property
    def pareto(self) -> tuple[float, ...]:
        return (self.native_score if self.native_score is not None else -math.inf,
                *[self.pipeline[k] for k in PIPELINES], float(self.free_counters),
                -self.deadline_penalty, -self.waste_penalty,
                -(self.native_elapsed if self.native_elapsed is not None else math.inf))


def food_facts(tree: Mapping | None) -> tuple[set[int], set[int], bool]:
    """Return cooked leaf IDs, mixed leaf IDs, ruined from a native FoodState tree."""
    cooked, mixed = set(), set()
    ruined = False

    def walk(node: Mapping, is_cooked: bool, is_mixed: bool, depth: int) -> None:
        nonlocal ruined
        if depth > 24:
            raise ValueError("food evidence exceeds24 levels")
        state = str(node.get("state", "")).lower()
        ruined |= state in {"burnt", "burned", "overmixed", "ruined"}
        is_cooked |= state == "cooked"
        is_mixed |= state == "mixed"
        leaf_id = node.get("id", node.get("ingredientId"))
        if leaf_id is not None and node.get("type") == "IngredientAssembledNode":
            if is_cooked:
                cooked.add(int(leaf_id))
            if is_mixed:
                mixed.add(int(leaf_id))
        for child in (*node.get("children", []), *node.get("optional", [])):
            walk(child, is_cooked, is_mixed, depth + 1)

    if tree:
        walk(tree, False, False, 0)
    return cooked, mixed, ruined


def assess(snapshot: Mapping, native_round: Mapping | None, *, food_evidence: Mapping[int, Mapping] | None = None,
           lookahead: int = 6, safety_margin: float = 3.0, horizon_seconds: float = 1.0) -> Assessment:
    food_evidence = food_evidence or {}
    demand = pending_demand(snapshot, native_round, lookahead)
    caps = Counter(INGREDIENTS[i] for order in demand for i in order.ingredients)
    caps["plate"] = min(4, len(demand))
    active = [e for e in snapshot["entities"] if e.get("exists", False)]
    entities = {integer(e["id"], "entity id"): e for e in active}
    if len(entities) != len(active):
        raise ValueError("Duplicate physical entity IDs in inspection")
    by_path = {}
    for eid, entity in entities.items():
        # Full headless observations carry paths; ID fallback supports older
        # fixtures with only fixed entities without guessing dynamic spawn paths.
        key = path_key(entity) if 'path' in entity else (eid,)
        if key is None or key in by_path:
            raise ValueError('Missing or duplicate reconstructed entity path')
        by_path[key] = entity
    candidates: list[Credit] = []
    deadlines, gaps = [], []
    ruined_count = 0
    food_entities: set[int] = set()

    def add(eid: int, suffix: str, claims: Mapping[str, int], stage: str, pipeline: str, value: float, proof: str) -> None:
        candidates.append(Credit(eid, f"{eid}:{suffix}", tuple(sorted(claims.items())), stage, pipeline, value, proof))

    for eid, entity in sorted(entities.items()):
        if (entity.get('plateLifecycle') or {}).get('phase') in (1, 2):
            # Native delivery animation retains the plate briefly. It remains an
            # observed entity, but its food and clean plate are already consumed.
            continue
        data = entity.get("data", {})
        kind = entity.get("className", "")
        contents = Counter(int(i) for i in data.get("contents", []))
        raw = kind in RAW_CLASSES
        if not contents and kind in RAW_CLASSES | PREPARED_CLASSES:
            contents[(RAW_CLASSES | PREPARED_CLASSES)[kind]] = 1
        cooked, mixed, ruined = food_facts(food_evidence.get(eid) or food_evidence.get(str(eid)))
        cook = max(0, number(entity.get("cookingProgress", 0), "cookingProgress"))
        mix = max(0, number(entity.get("mixingProgress", 0), "mixingProgress"))
        chop = min(1, max(0, number(entity.get("choppingProgress", 0), "choppingProgress")) / 1.4)
        parent = by_path.get(path_key(data.get("attachmentParent")), {})
        parent_kind = parent.get("className", "")
        if contents:
            ruined |= (kind in {"pot", "pan"} and cook >= 24) or (kind == "frier" and cook >= 20) or (kind == "mixer" and mix >= 24)
        # Potential on-heat clocks are penalized conservatively even when a native
        # turned-on flag is absent. Off-heat counters/held vessels incur no clock.
        if contents and parent_kind in {"heat-station", "frier-station", "mixer-station"}:
            progress, limit = (mix, 24.0) if parent_kind == "mixer-station" else (cook, 20.0 if parent_kind == "frier-station" else 24.0)
            slack = limit - progress
            deadlines.append({"entity": eid, "parent": parent["id"], "progress": progress,
                              "limit": limit, "slack": slack, "potentialOnHeat": True})
            ruined |= progress >= limit
        if contents:
            food_entities.add(eid)
        if ruined:
            ruined_count += 1
            continue
        flavor = CHOCOLATE if contents[CHOCOLATE] else RASPBERRY if contents[RASPBERRY] else None
        exact_kit = flavor is not None and contents == Counter((FLOUR, EGG, flavor))
        if contents and kind == "mixer" and not any(contents <= Counter((FLOUR, EGG, f)) for f in (CHOCOLATE, RASPBERRY)):
            gaps.append(f"entity{eid}: incompatible/duplicate bowl contents cannot complete a native three-item kit")
            continue
        if exact_kit and kind in {"mixer", "frier", "plate"}:
            claims = {"flour": 1, "egg": 1, INGREDIENTS[flavor]: 1}
            if set(contents) <= cooked:
                stage, value, proof = "fry", 1.0, "native-food-tree-Cooked"
            elif kind == "frier":
                stage, value, proof = "fry", .65 + .34 * min(1, cook / 10), "native-fryer-progress; final food state unverified"
            elif kind == "mixer":
                stage, value, proof = "mix", .65 if set(contents) <= mixed else .25 + .40 * min(1, mix / 12), "complete kit and native mixer progress"
            else:
                stage, value, proof = "mix", .25, "flat plated kit; preparation unknown"
                gaps.append(f"entity{eid}: flattened plated donut lacks Cooked proof")
            add(eid, "donut-kit", claims, stage, "donut", value, proof)
            contents.clear()  # One kit cannot also receive loose flavor/mix credits.
        for ingredient, count in sorted(contents.items()):
            family = INGREDIENTS.get(ingredient)
            if family is None:
                gaps.append(f"entity{eid}: unknown ingredient{ingredient}")
                continue
            for occurrence in range(count):
                stage, pipeline, value = "chop", family, 0.0
                proof = "native reconstructed ingredient/progress"
                if ingredient == SAUSAGE:
                    stage, pipeline = "sausage", "sausage"
                    value = 1 if ingredient in cooked else .10 + .85 * min(1, cook / 12) if kind == "pot" else .10
                elif ingredient == ONION:
                    stage, pipeline = "onion", "onion"
                    value = 1 if ingredient in cooked else .05 + .25 * chop if raw else .30
                    if kind == "pan" and ingredient not in cooked:
                        value = .30 + .65 * min(1, cook / 12)
                elif ingredient == BUN:
                    value = .05 + .95 * chop if raw else 1
                elif ingredient in {CHOCOLATE, RASPBERRY}:
                    pipeline = "donut"
                    value = .02 + .13 * chop if raw else .15
                    if kind == "mixer":
                        stage = "mix"  # Incomplete dough does not earn timed mixing progress.
                elif ingredient in {FLOUR, EGG}:
                    stage, pipeline, value = "mix", "donut", .05
                else:
                    continue  # Condiment presence does not fabricate another preparation stage.
                if value:
                    add(eid, f"{family}:{occurrence}", {family: 1}, stage, pipeline, value, proof)
        if kind == "plate":
            add(eid, "plate", {"plate": 1}, "plates", "plates", .2, "observed clean plate entity")
        if kind == "sink" and int(data.get("numPlates", 0)) > 0:
            value = .2 * min(1, max(0, number(entity.get("washingProgress", 0), "washingProgress")) / 3)
            if value:
                add(eid, "washing-one-plate", {"plate": 1}, "wash", "plates", value, "native current-plate wash progress")
    # Highest completed packages get first claim on finite demand. A complete kit
    # consumes its three ingredient slots, so spare raw ingredients cannot stack
    # credits on the same downstream order. No station/chef attachment traversal
    # duplicates the independently registered food entity.
    remaining = caps.copy()
    credits = []
    for credit in sorted(candidates, key=lambda c: (-c.value, c.token)):
        if all(remaining[k] >= n for k, n in credit.claims):
            credits.append(credit)
            remaining.subtract(dict(credit.claims))
    stages = {stage: 0.0 for stage in STAGES}
    pipeline = {name: 0.0 for name in PIPELINES}
    for credit in credits:
        stages[credit.stage] += credit.value
        pipeline[credit.pipeline] += credit.value
    occupied, free = 0, 0
    for entity in entities.values():
        position = entity.get("position", {})
        if entity.get("className") == "counter" and 16 < position.get("x", 0) < 25 and entity["id"] != 42:
            if path_key(entity.get("data", {}).get("attachment")) is None:
                free += 1
            else:
                occupied += 1
    used_entities = {c.entity for c in credits if c.stage not in {"plates", "wash"}}
    waste = .05 * len(food_entities - used_entities) + 2 * ruined_count
    deadline_penalty = sum(max(0.0, safety_margin + horizon_seconds - d["slack"]) / max(.1, safety_margin) for d in deadlines)
    for order in demand:
        if order.remaining is not None:
            deadline_penalty += max(0.0, horizon_seconds - order.remaining) / max(1.0, horizon_seconds)
    native = native_round or {}
    ledger = native.get("ledger") if native.get("available") else None
    score = deliveries = deductions = elapsed = None
    if ledger is not None:
        score = integer(ledger["total"], "native ledger total")
        deliveries = integer(ledger["deliveries"], "native ledger deliveries")
        deductions = integer(ledger["deductions"], "native ledger deductions")
        elapsed = number(native["elapsed"], "native elapsed")
    else:
        gaps.append("No authoritative native score sample; assessment cannot rank a native search result")
    if not food_evidence:
        gaps.append("Flattened framework contents omit Cooked/Mixed/ruined tree states; WIP is a heuristic")
    if snapshot.get("invalidStateReason"):
        gaps.append("Framework invalidStateReason: " + str(snapshot["invalidStateReason"]))
    return Assessment(score, deliveries, deductions, elapsed, demand, dict(caps), stages, pipeline, tuple(credits),
                      sum(stages.values()), deadline_penalty, .01 * occupied, waste, free, tuple(deadlines), tuple(sorted(set(gaps))))


@dataclass(frozen=True)
class Pad:
    chef: int
    x: float = 0.0
    y: float = 0.0
    pickup: bool = False
    use: bool = False
    dash: bool = False

    def __post_init__(self):
        if integer(self.chef, "chef entity id") < 0:
            raise ValueError("Chef entity id cannot be negative")
        if any(abs(number(v, "axis")) > 1 for v in (self.x, self.y)):
            raise ValueError("Axes must be in[-1,1]")
        if any(type(v) is not bool for v in (self.pickup, self.use, self.dash)):
            raise ValueError("Buttons must be levels, not manufactured edge claims")


@dataclass(frozen=True)
class Segment:
    frames: int
    pads: tuple[Pad, ...]
    stations: tuple[tuple[int, int], ...] = ()  # (station entity id, owning chef)
    label: str = "input-segment"
    trailing_neutral_frames: int = 0

    def __post_init__(self):
        if not 1 <= integer(self.frames, "frames") <= 600:
            raise ValueError("A segment must contain1..600 input frames")
        if not 0 <= integer(self.trailing_neutral_frames, "trailing neutral frames") < self.frames:
            raise ValueError("Trailing neutral frames must leave at least one payload frame")
        if len(self.pads) != 4 or len({p.chef for p in self.pads}) != 4:
            raise ValueError("Each segment needs exactly four distinct chef inputs")
        chefs = {p.chef for p in self.pads}
        if any(integer(station, "station entity id") < 0 for station, _ in self.stations):
            raise ValueError("Station entity id cannot be negative")
        if any(owner not in chefs for _, owner in self.stations):
            raise ValueError("Station reservation owner must be one of the four chefs")
        if len({station for station, _ in self.stations}) != len(self.stations):
            raise ValueError("Two chefs cannot reserve the same station in one segment")

    @property
    def key(self) -> str:
        return identity(asdict(self))

    def input_frames(self) -> list[dict]:
        """Explicit level frames for a native adapter; no clocks/warps or fake edges."""
        return [{str(p.chef): {"x": 0.0 if neutral else p.x, "y": 0.0 if neutral else p.y,
                              "pickup": False if neutral else p.pickup, "use": False if neutral else p.use,
                              "dash": False if neutral else p.dash}
                 for p in sorted(self.pads, key=lambda p: p.chef)}
                for neutral in [i >= self.frames - self.trailing_neutral_frames for i in range(self.frames)]]


@dataclass(frozen=True)
class Reservation:
    resource: str
    owner: int
    start: int
    end: int

    def __post_init__(self):
        if not self.resource or self.start < 0 or self.end <= self.start:
            raise ValueError("Reservations use nonempty half-open frame intervals")


def reserve(existing: Sequence[Reservation], segment: Segment, start: int) -> tuple[Reservation, ...]:
    added = [Reservation(f"chef:{pad.chef}", pad.chef, start, start + segment.frames) for pad in segment.pads]
    added += [Reservation(f"station:{station}", chef, start, start + segment.frames) for station, chef in segment.stations]
    for new in added:
        for old in existing:
            if new.resource == old.resource and max(new.start, old.start) < min(new.end, old.end):
                raise ValueError(f"Reservation conflict: {new.resource}")
    return tuple(existing) + tuple(added)


def generate_segments(chefs: Sequence[int], *, seed: int, limit: int = 32, durations: Sequence[int] = (6, 12),
                      station_targets: Mapping[int, int] | None = None) -> tuple[Segment, ...]:
    """Deterministic bounded micro-actions, including use+movement and concurrency.

    No collision rejection or speed model is used. Holding use and then releasing
    in a later segment allows native throw/contact/catch experiments. Station
    claims are explicit intent, not inferred ownership of every nearby collider.
    """
    chefs = tuple(sorted(chefs))
    if len(chefs) != 4 or len(set(chefs)) != 4 or not 1 <= limit <= 4096 or not durations:
        raise ValueError("Expected four chefs, nonempty durations, and limit1..4096")
    if any(not 1 <= integer(duration, "duration") <= 600 for duration in durations):
        raise ValueError("Durations must be1..600 frames")
    station_targets = station_targets or {}
    rng = random.Random(seed)
    axes = ((0., 0.), (1., 0.), (-1., 0.), (0., 1.), (0., -1.), (.70710678, .70710678),
            (-.70710678, -.70710678), (.70710678, -.70710678), (-.70710678, .70710678))
    result: dict[str, Segment] = {}

    def emit(pads: Sequence[Pad], duration: int, label: str) -> None:
        claims = tuple((station_targets[p.chef], p.chef) for p in pads if (p.use or p.pickup) and p.chef in station_targets)
        try:
            segment = Segment(duration, tuple(pads), claims, label)
        except ValueError:  # conflicting intended stations, never physical contacts
            return
        result.setdefault(segment.key, segment)

    emit([Pad(c) for c in chefs], durations[0], "neutral-release")
    for index in range(limit * 8):
        if len(result) >= limit:
            break
        pads = []
        for slot, chef in enumerate(chefs):
            active = slot == index % 4 or (index >= 8 and rng.random() < .5)
            x, y = rng.choice(axes) if active else (0, 0)
            button = rng.choice(("none", "use", "pickup", "dash", "use")) if active else "none"
            pads.append(Pad(chef, x, y, button == "pickup", button == "use", button == "dash"))
        emit(pads, durations[index % len(durations)], "native-contact-permitted")
    return tuple(result.values())


@dataclass
class Evaluation:
    snapshot: dict
    native_round: dict | None
    frames_executed: int
    checkpoint: Any = None  # opaque evaluator-owned authoring state handle
    food_evidence: dict = field(default_factory=dict)
    failure: str | None = None
    evidence_id: str = ""
    timings: dict = field(default_factory=dict)


@dataclass
class Node:
    evaluation: Evaluation
    assessment: Assessment
    segments: tuple[Segment, ...] = ()
    reservations: tuple[Reservation, ...] = ()

    @property
    def key(self) -> str:
        return identity([s.key for s in self.segments])


@dataclass(frozen=True)
class SearchConfig:
    seed: int = 0
    beam_width: int = 8
    max_depth: int = 3
    max_evaluations: int = 64
    proposals_per_node: int = 16
    max_total_frames: int = 120
    lookahead: int = 6

    def __post_init__(self):
        for name in ("beam_width", "max_depth", "max_evaluations", "proposals_per_node", "max_total_frames", "lookahead"):
            if integer(getattr(self, name), name) < 1:
                raise ValueError(name + " must be positive")
        if self.max_depth > 64 or self.max_evaluations > 100000 or self.lookahead > 32:
            raise ValueError("Search bounds exceed the offline core limits")


def dominates(a: Assessment, b: Assessment) -> bool:
    return all(x >= y for x, y in zip(a.pareto, b.pareto)) and any(x > y for x, y in zip(a.pareto, b.pareto))


def select_beam(nodes: Sequence[Node], width: int) -> list[Node]:
    ordered = sorted(nodes, key=lambda n: (n.assessment.rank, n.key), reverse=True)
    frontier = [node for node in ordered if not any(other is not node and dominates(other.assessment, node.assessment) for other in ordered)]
    if len(frontier) <= width:
        return frontier
    # Always retain the highest native-score result. Additional axis champions
    # preserve food/plate/resource balance instead of filling the beam with one
    # saturated ingredient. This does not change the final score-first ranking.
    selected = [frontier[0]]
    selected_keys = {frontier[0].key}
    for axis in range(1, len(frontier[0].assessment.pareto)):
        champion = max(frontier, key=lambda n: (n.assessment.pareto[axis], n.assessment.rank, n.key))
        if champion.key not in selected_keys:
            selected.append(champion)
            selected_keys.add(champion.key)
        if len(selected) == width:
            return selected
    return selected + [n for n in frontier if n.key not in selected_keys][:width - len(selected)]


def bounded_beam(initial: Evaluation, evaluator: Callable[[Node, Segment], Evaluation],
                 proposals: Callable[[Node, int, int], Iterable[Segment]], *, config: SearchConfig = SearchConfig(),
                 reservations: Sequence[Reservation] = ()) -> dict:
    """Sequential real-evaluator beam with immutable total work limits.

    `proposals(parent, deterministic_seed, limit)` and `evaluator(parent, segment)`
    must be deterministic for reproducibility; wall timings are recorded but do
    not break ties. A callback failure is recorded and never qualifies as a node.
    There is no implicit fallback simulator or automatic world-state mutation.
    """
    first = assess(initial.snapshot, initial.native_round, food_evidence=initial.food_evidence, lookahead=config.lookahead)
    if first.native_score is None or initial.failure:
        raise ValueError("Initial evaluator state requires an authoritative native score and no failure")
    root = Node(initial, first, reservations=tuple(reservations))
    beam, all_nodes, outcomes = [root], [root], []
    calls = 0
    for depth in range(config.max_depth):
        candidates = []
        for parent in beam:
            node_seed = int(identity([config.seed, depth, parent.key])[:16], 16)
            seen = set()
            for ordinal, segment in enumerate(proposals(parent, node_seed, config.proposals_per_node)):
                if ordinal >= config.proposals_per_node or calls >= config.max_evaluations:
                    break
                if segment.key in seen:
                    continue
                seen.add(segment.key)
                total_frames = sum(s.frames for s in parent.segments) + segment.frames
                receipt = {"parent": parent.key, "segment": asdict(segment), "candidate": identity([parent.key, segment.key]),
                           "depth": depth + 1, "seed": node_seed, "totalFrames": total_frames}
                if total_frames > config.max_total_frames:
                    outcomes.append(dict(receipt, failure="total frame budget", evaluated=False))
                    continue
                try:
                    start = integer(parent.evaluation.snapshot["frame"], "parent frame")
                    owned = reserve(parent.reservations, segment, start)
                except ValueError as error:
                    outcomes.append(dict(receipt, failure=str(error), evaluated=False))
                    continue
                calls += 1
                began = time.perf_counter()
                observed = None
                try:
                    observed = evaluator(parent, segment)
                    if observed.failure:
                        raise ValueError(observed.failure)
                    if observed.frames_executed != segment.frames or observed.snapshot.get("frame") != start + observed.frames_executed:
                        raise ValueError("Evaluator did not attest the requested advancing frame interval")
                    assessment = assess(observed.snapshot, observed.native_round, food_evidence=observed.food_evidence,
                                        lookahead=config.lookahead, horizon_seconds=segment.frames / 60)
                    if assessment.native_score is None:
                        raise ValueError("Evaluator omitted authoritative native score")
                    if assessment.native_elapsed < parent.assessment.native_elapsed or assessment.native_deliveries < parent.assessment.native_deliveries:
                        raise ValueError("Native round clock/delivery ledger regressed during candidate execution")
                    node = Node(observed, assessment, parent.segments + (segment,), owned)
                    candidates.append(node)
                    outcomes.append(dict(receipt, evaluated=True, failure=None, objective=asdict(assessment),
                                         rank=list(assessment.rank), evidenceId=observed.evidence_id,
                                         timings=observed.timings, evaluationWallSeconds=time.perf_counter() - began))
                except Exception as error:
                    outcomes.append(dict(receipt, evaluated=True, failure=f"{type(error).__name__}: {error}",
                                         evidenceId=observed.evidence_id if observed else "",
                                         nativeRound=observed.native_round if observed else None,
                                         timings=observed.timings if observed else {}, evaluationWallSeconds=time.perf_counter() - began))
        if not candidates:
            break
        all_nodes.extend(candidates)
        beam = select_beam(candidates, config.beam_width)
        if calls >= config.max_evaluations:
            break
    best = max(all_nodes, key=lambda n: (n.assessment.rank, n.key))
    return {"version": 1, "qualification": "Authoring search results from supplied evaluator; no full-round or fresh-replay qualification",
            "config": asdict(config), "evaluations": calls, "outcomes": outcomes,
            "best": {"key": best.key, "segments": [asdict(s) for s in best.segments], "objective": asdict(best.assessment),
                     "evidenceId": best.evaluation.evidence_id}, "frontier": [n.key for n in beam]}


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("assess", "generate"))
    parser.add_argument("--snapshot", type=Path, required=True)
    parser.add_argument("--native-round", type=Path)
    parser.add_argument("--lookahead", type=int, default=6)
    parser.add_argument("--seed", type=int, default=0)
    parser.add_argument("--limit", type=int, default=32)
    parser.add_argument("--out", type=Path)
    args = parser.parse_args(argv)
    raw = args.snapshot.read_bytes()
    snapshot, native = extract_samples(json.loads(raw))
    if args.native_round:
        native = json.loads(args.native_round.read_bytes())
        native = native.get("bridge", native).get("nativeRound", native)
    if args.command == "assess":
        result = asdict(assess(snapshot, native, lookahead=args.lookahead))
    else:
        chefs = [e["id"] for e in snapshot["entities"] if e.get("exists") and e.get("chef") is not None]
        result = {"segments": [asdict(s) for s in generate_segments(chefs, seed=args.seed, limit=args.limit)]}
    result["source"] = str(args.snapshot.resolve())
    result["sourceSha256"] = hashlib.sha256(raw).hexdigest()
    result["qualification"] = "Offline reconstruction assessment/input proposals only; no native evaluation executed"
    text = json.dumps(result, indent=2, sort_keys=True, allow_nan=False)
    if args.out:
        with args.out.open("x", encoding="utf-8") as output:
            output.write(text + "\n")
    else:
        print(text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
