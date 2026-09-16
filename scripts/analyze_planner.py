#!/usr/bin/env python3
"""Streaming diagnostics for an OC2 planner trace, including a growing gzip file.

Reads only the compressed/file prefix present at startup. Durations use logical
gameplay frames / 60, never wall time. No frame snapshots are retained. Lists of
examples are capped; totals remain uncapped. This is a partial-run diagnostic,
not a recipe validator or high-score/reproducibility certification.
"""

import argparse
import collections
import gzip
import hashlib
import io
import json
import math
import re
import sys
from pathlib import Path


HZ = 60
INGREDIENTS = {262914: "bun", 284626: "sausage", 461162: "onion", 18448: "flour",
               16620: "egg", 22804: "chocolate", 129618: "raspberry", 158482: "ketchup", 17094: "mustard"}
RAW_NAMES = {"HotdogBun": 262914, "Frankfurter": 284626, "DLC08_Onion": 461162,
             "Flour": 18448, "Egg": 16620, "Chocolate": 22804, "Raspberry": 129618}


def seconds(frames):
    return round(frames / HZ, 4)


def compact(value):
    return {k: v for k, v in value.items() if k not in ("state", "snapshot", "preview", "configuration")}


class Prefix(io.RawIOBase):
    """A read-only bounded view prevents analysis from chasing a live writer."""
    def __init__(self, raw, length):
        self.raw, self.remaining = raw, length
        self.digest = hashlib.sha256()
        self.count = 0

    def readable(self):
        return True

    def readinto(self, target):
        data = self.raw.read(min(len(target), self.remaining))
        self.remaining -= len(data)
        self.count += len(data)
        self.digest.update(data)
        target[:len(data)] = data
        return len(data)


class Moments:
    def __init__(self):
        self.count, self.total, self.low, self.high = 0, 0, None, 0

    def add(self, frames):
        self.count += 1
        self.total += frames
        self.low = frames if self.low is None else min(self.low, frames)
        self.high = max(self.high, frames)

    def report(self):
        return {"count": self.count, "totalSeconds": seconds(self.total),
                "meanSeconds": seconds(self.total / self.count) if self.count else None,
                "minSeconds": seconds(self.low) if self.low is not None else None, "maxSeconds": seconds(self.high)}


class Spans:
    def __init__(self, minimum=1):
        self.start = None
        self.stats = Moments()
        self.longest = []
        self.minimum = minimum

    def observe(self, frame, present):
        if present and self.start is None:
            self.start = frame
        elif not present and self.start is not None:
            self._close(frame, False)

    def _close(self, frame, open_at_end):
        length = max(0, frame - self.start)
        if length >= self.minimum:
            self.stats.add(length)
            self.longest.append({"startFrame": self.start, "endFrame": frame, "seconds": seconds(length), "openAtEnd": open_at_end})
            self.longest = sorted(self.longest, key=lambda s: s["seconds"], reverse=True)[:5]
        self.start = None

    def finish(self, frame):
        if self.start is not None:
            self._close(frame, True)
        return {**self.stats.report(), "longest": self.longest, "minimumFrames": self.minimum}


def role(entity):
    name = entity.get("name") or ""
    components = set(entity.get("components") or [])
    if name.endswith("_Rigidbody") or "PlayerIDProvider" in components:
        return None
    if "Plate" in components:
        return "plate"
    if "WashingStation" in components:
        return "sink"
    if "PlateReturnStation" in components:
        return "drying" if "Drying" in name else "dirty-return"
    if "CarryableItem" in components:
        return next((kind for fragment, kind in (("mixer", "bowl"), ("frying_pan", "pan"),
                    ("FrierBasket", "basket"), ("pot_", "pot")) if fragment in name), "food")
    return None


def food(entity):
    stack = [entity["composition"]] if isinstance(entity.get("composition"), dict) else list(entity.get("contents") or [])
    ids, states = [], set()
    while stack:
        node = stack.pop()
        if not isinstance(node, dict):
            continue
        if "IngredientAssembledNode" in (node.get("type") or ""):
            ids.append(node.get("id"))
        if node.get("state"):
            states.add(node["state"])
        stack.extend(node.get("children") or [])
    if not ids and "WorkableItem" in (entity.get("components") or []):
        raw_id = next((i for name, i in RAW_NAMES.items() if (entity.get("name") or "").startswith(name)), None)
        if raw_id:
            ids.append(raw_id)
    return ids, states


def geometry(entities):
    """Same measured region bounds as KitchenModel; geometric inference only."""
    corners = [e["position"] for e in entities.values() if "countertop_01_standard_circus_corner" in (e.get("name") or "")]
    xs, zs = sorted({round(p["x"], 2) for p in corners}), sorted({round(p["z"], 2) for p in corners})
    counters = sorted({round(e["position"]["x"], 2) for e in entities.values() if "countertop_01_" in (e.get("name") or "")})
    pitch = collections.Counter(round(b - a, 2) for a, b in zip(counters, counters[1:]) if .2 < b - a < 2)
    cannons = [e["position"]["z"] for e in entities.values() if "Cannon" in (e.get("components") or [])]
    portals = [e["position"]["z"] for e in entities.values() if "TeleportalPlayerSender" in (e.get("components") or []) and e["position"].get("y", 0) < 1]
    if len(xs) != 4 or len(zs) != 2 or len(cannons) != 2 or len(portals) != 2 or not pitch:
        return []
    tile = sorted(pitch, key=lambda v: (-pitch[v], v))[0]
    top = sum(cannons) / 2 - tile / 2
    bottom = sum(portals) / 2 + tile / 2
    return [("upper-left", xs[0], xs[1], top, zs[1]), ("lower-left", xs[0], xs[1], zs[0], bottom),
            ("center", xs[1], xs[2], zs[0], zs[1]), ("upper-right", xs[2], xs[3], top, zs[1]),
            ("lower-right", xs[2], xs[3], zs[0], bottom)]


def region(position, bounds):
    x, z = position.get("x", 0), position.get("z", 0)
    return next((name for name, x0, x1, z0, z1 in bounds if x0 <= x <= x1 and z0 <= z <= z1), "unknown")


class Segment:
    def __init__(self, state, example_limit):
        self.limit = example_limit
        self.start = self.end = state["gameplayFrame"]
        self.level_zero = state.get("levelFrameZero")
        self.initial = self.final = self.score(state)
        self.samples, self.duplicates, self.largest_gap = 0, 0, 0
        self.previous = None
        self.jobs, self.actions, self.players = {}, {}, collections.defaultdict(collections.Counter)
        # Work.Complete callbacks can start a successor before the predecessor's
        # plannerJobComplete is emitted. Keep bounded pending starts until the
        # named completion resolves that same-frame handoff; never erase the
        # successor merely because the completion names the older job.
        self.displaced_jobs = collections.defaultdict(lambda: collections.deque(maxlen=16))
        # Native boundary interruptions allow one suspended Work globally.
        # Preserve its original start and ownership while its child is active.
        self.paused_jobs = {}
        self.pause_stats = collections.defaultdict(Moments)
        self.pause_timeline = collections.deque(maxlen=example_limit)
        self.job_stats, self.action_stats, self.stage_stats = (collections.defaultdict(Moments) for _ in range(3))
        self.job_timeline = collections.deque(maxlen=example_limit)
        self.spans = collections.defaultdict(Spans)
        self.inventory_total = collections.Counter()
        self.inventory_last, self.inventory_max = {}, collections.Counter()
        self.vessels, self.event_counts, self.guard_counts = {}, collections.Counter(), collections.Counter()
        self.bounds = []
        self.transitions, self.deliveries, self.native_events, self.burns, self.waves, self.guards = ([] for _ in range(6))
        self.last_regions, self.native_seen, self.burn_seen = {}, set(), set()
        self.wave = None
        self.delivery_intervals = Moments()
        self.last_delivery = None
        self.native_delivery_count, self.transition_count = 0, 0
        self.first_failure = self.finished = None
        self.initialized = None
        self.anomalies = collections.Counter()

    @staticmethod
    def score(state):
        fields = ("gameplayFrame", "timer", "score", "baseScore", "tips", "delivered", "deductions", "multiplier", "scene",
                  "serverRoundActive", "clientRoundActive", "levelStartPhysicsPhase", "gameEventsDropped")
        return {k: state.get(k) for k in fields}

    def keep(self, target, value):
        if len(target) < self.limit:
            target.append(value)

    def activate_job(self, player, job, frame):
        if player in self.jobs:
            self.anomalies["jobStartBeforePreviousCompletion"] += 1
            pending = self.displaced_jobs[player]
            if len(pending) == pending.maxlen:
                self.anomalies["jobPendingCompletionLimitExceeded"] += 1
            pending.append((self.jobs[player], frame))
        self.jobs[player] = job

    @staticmethod
    def job_observation(player, job, frame):
        elapsed = frame - job["frame"]
        paused = job.get("_paused_frames", 0)
        result = {"player": player, "name": job["name"], "startFrame": job["frame"],
                  "observedSeconds": seconds(elapsed - paused), "resources": job.get("resources", [])}
        if "_paused_frames" in job:
            result.update(elapsedSeconds=seconds(elapsed), pausedSeconds=seconds(paused))
        return result

    def event(self, name, value):
        self.event_counts[name] += 1
        if not isinstance(value, dict):
            return
        if name == "plannerInitialized":
            self.initialized = {k: value.get(k) for k in ("seed", "target", "predictedFreshDeliveries", "predictedFreshScore", "qualification")}
        elif name == "plannerFailure" and self.first_failure is None:
            self.first_failure = {"observedFrame": self.end, **compact(value)}
        elif name == "plannerFinished":
            self.finished = compact(value)
        elif name == "plannerJobStart":
            player = value["player"]
            self.activate_job(player, value, value["frame"])
        elif name == "plannerJobPaused":
            player = value["player"]
            if self.paused_jobs:
                self.anomalies["jobPauseWhileAnotherSuspended"] += 1
            elif self.jobs.get(player, {}).get("name") != value["name"]:
                self.anomalies["jobPauseWithoutMatchingActiveJob"] += 1
            elif value["frame"] < self.jobs[player]["frame"]:
                self.anomalies["jobPauseBeforeStart"] += 1
            else:
                self.paused_jobs[player] = (self.jobs.pop(player), value)
        elif name == "plannerJobResumed":
            player = value["player"]
            paused = self.paused_jobs.get(player)
            if paused is None or paused[0]["name"] != value["name"]:
                self.anomalies["jobResumeWithoutMatchingPause"] += 1
            elif value["frame"] < paused[1]["frame"]:
                self.anomalies["jobResumeBeforePause"] += 1
            else:
                job, pause = self.paused_jobs.pop(player)
                duration = value["frame"] - pause["frame"]
                if value.get("pausedAtFrame", pause["frame"]) != pause["frame"] or value.get("pausedFrames", duration) != duration:
                    self.anomalies["jobResumeTimingMetadataMismatch"] += 1
                if sorted(value.get("resources", [])) != sorted(pause.get("resources", [])):
                    self.anomalies["jobResumeResourceMismatch"] += 1
                job = {**job, "_paused_frames": job.get("_paused_frames", 0) + duration}
                kind = re.sub(r"(?<=-)\d+(?=-|$)", "#", job["name"])
                self.pause_stats[(player, kind)].add(duration)
                self.pause_timeline.append({"player": player, "job": job["name"], "startFrame": pause["frame"],
                    "endFrame": value["frame"], "seconds": seconds(duration), "resources": pause.get("resources", [])})
                # Resume is emitted inside the firing child's completion
                # callback. Its same-frame completion must resolve the child,
                # without erasing or restarting this original Work.
                self.activate_job(player, job, value["frame"])
        elif name in ("plannerJobComplete", "pantryChopDelegated"):
            delegated = name == "pantryChopDelegated"
            delegation = value if delegated else None
            if delegated:
                value = {"player": value["supplier"], "name": value["originalJob"], "frame": value["frame"]}
            player = value["player"]
            previous = None
            pending = self.displaced_jobs.get(player, ())
            match = next((i for i, (job, _) in enumerate(pending) if job["name"] == value["name"]), None)
            if match is not None:
                previous, replacement_frame = pending[match]
                del pending[match]
                if not pending:
                    del self.displaced_jobs[player]
                if value["frame"] == replacement_frame:
                    self.anomalies["jobStartBeforePreviousCompletion"] -= 1
                    if not self.anomalies["jobStartBeforePreviousCompletion"]:
                        del self.anomalies["jobStartBeforePreviousCompletion"]
            elif self.jobs.get(player, {}).get("name") == value["name"]:
                previous = self.jobs.pop(player)
            if previous:
                kind = re.sub(r"(?<=-)\d+(?=-|$)", "#", value["name"])
                elapsed = value["frame"] - previous["frame"]
                paused = previous.get("_paused_frames", 0)
                self.job_stats[(player, kind)].add(elapsed - paused)
                timing = {"player": player, "job": value["name"], "startFrame": previous["frame"],
                    "endFrame": value["frame"], "seconds": seconds(elapsed - paused), "resources": previous.get("resources", [])}
                if "_paused_frames" in previous:
                    timing.update(elapsedSeconds=seconds(elapsed), pausedSeconds=seconds(paused))
                if delegated:
                    timing.update(completionKind="raw-placement-delegated", helper=delegation["helper"],
                                  board=delegation.get("board"), rawEntity=delegation.get("rawEntity"),
                                  preparedFoodCallbackCompleted=False)
                self.job_timeline.append(timing)
            else:
                self.anomalies["jobCompletionWithoutMatchingStart"] += 1
        elif name == "actionCreated":
            self.actions[value.get("player", 0)] = {"spec": value, "start": self.end, "stage": None, "stageFrame": 0}
        elif name == "actionStage":
            player = value.get("player", 0)
            action = self.actions.get(player)
            if action:
                self.close_stage(player, action, value["actionFrames"])
                action["stage"], action["stageFrame"] = value["stage"], value["actionFrames"]
        elif name == "actionComplete":
            spec, duration = value["action"], value["frames"]
            player, kind = spec.get("player", 0), spec.get("type", "unknown")
            self.action_stats[(player, kind)].add(duration)
            action = self.actions.pop(player, None)
            if action:
                self.close_stage(player, action, duration)
        if name == "navigationWaiting" or "Suppression" in name or "Reacquire" in name or name.endswith("Retry") or "Failure" in name:
            reason = str(value.get("reason", value.get("error", "")))[:300]
            self.guard_counts[(name, reason)] += 1
            self.keep(self.guards, {"observedFrame": self.end, "event": name, "value": compact(value)})

    def close_stage(self, player, action, frame):
        if action["stage"] is not None:
            self.stage_stats[(player, action["spec"].get("type", "unknown"), action["stage"])].add(max(0, frame - action["stageFrame"]))

    def snapshot(self, state, request):
        frame = state["gameplayFrame"]
        self.samples += 1
        if self.previous is not None and frame == self.end:
            self.duplicates += 1
            self.final = self.score(state)
            return
        entities = {e["id"]: e for e in state.get("entities", []) if e.get("active") is not False}
        chefs = {c["playerId"]: c for c in state.get("chefs", [])}
        if not self.bounds:
            self.bounds = geometry(entities)
        step = frame - self.end if self.previous is not None else 0
        self.largest_gap = max(self.largest_gap, step)
        inputs = {i["player"]: i for i in request.get("inputs", [])}
        for player, chef in chefs.items():
            previous_chef = self.previous["chefs"].get(player, chef) if self.previous else chef
            assigned = player in self.jobs
            if step:
                self.players[player]["assignedFrames" if assigned else "idleNoJobFrames"] += step
                if player in self.paused_jobs:
                    self.players[player]["pausedWorkFrames"] += step
                if assigned and previous_chef.get("controlsEnabled") is False:
                    self.players[player]["assignedControlsDisabledFrames"] += step
                if not assigned and previous_chef.get("controlsEnabled") is False:
                    self.players[player]["idleControlsDisabledFrames"] += step
                if not assigned and self.inventory_last.get("potReady", 0):
                    self.players[player]["idleWithReadyPotFrames"] += step
                if not assigned and self.inventory_last.get("bowlReady", 0):
                    self.players[player]["idleWithReadyBowlFrames"] += step
                if not assigned and self.inventory_last.get("centerEmptyPlates", 0) == 0:
                    self.players[player]["idleWithNoCenterEmptyPlateFrames"] += step
                if assigned and (previous_chef.get("inputSuppressed") or previous_chef.get("useSuppressed")):
                    self.players[player]["assignedNativeSuppressionFrames"] += step
            self.spans[f"chef{player}.idleNoJob"].observe(self.end, not assigned)
            movement = inputs.get(player, {})
            previous_pos, current_pos = previous_chef.get("position", {}), chef.get("position", {})
            distance = math.hypot(current_pos.get("x", 0) - previous_pos.get("x", 0), current_pos.get("z", 0) - previous_pos.get("z", 0))
            stalled = step == 1 and assigned and previous_chef.get("controlsEnabled") and chef.get("controlsEnabled") and (
                math.hypot(movement.get("x", 0), movement.get("y", 0)) >= .2 and distance < .003)
            key = f"chef{player}.inferredMovementStall"
            if key not in self.spans:
                self.spans[key] = Spans(6)
            self.spans[key].observe(self.end, stalled)
            current_region = "in-transit-or-disabled" if chef.get("controlsEnabled") is False or chef.get("respawning") else region(current_pos, self.bounds)
            if step:
                self.players[player]["region:" + self.last_regions.get(player, current_region)] += step
            previous_region = self.last_regions.get(player)
            if previous_region is not None and previous_region != current_region:
                self.transition_count += 1
                self.keep(self.transitions, {"player": player, "frame": frame, "from": previous_region, "to": current_region})
            self.last_regions[player] = current_region
            if player == 2:
                if current_region == "lower-right" and self.wave is None:
                    self.wave = {"arrivalFrame": frame, "deliveries": 0, "startDelivered": state.get("delivered", 0)}
                elif self.wave is not None and current_region not in ("lower-right", "in-transit-or-disabled", "unknown"):
                    self.keep(self.waves, {**self.wave, "endFrame": frame, "seconds": seconds(frame - self.wave["arrivalFrame"]), "openAtEnd": False})
                    self.wave = None
        inventory = collections.Counter()
        held = {c.get("heldEntityId") for c in chefs.values()}
        parents = {e.get("attachedEntityId"): e for e in entities.values() if e.get("attachedEntityId")}
        for entity_id, entity in entities.items():
            kind = role(entity)
            if kind is None:
                continue
            ids, states = food(entity)
            ruined = bool(states & {"Burnt", "Overmixed"})
            if kind in ("pot", "pan", "basket", "bowl"):
                key = f"{kind}:{entity_id}"
                self.vessels[key] = {"role": kind, "entityId": entity_id, "name": entity.get("name"), "position": entity.get("position")}
                empty = len(ids) == 0
                ready = "Mixed" in states if kind == "bowl" else "Cooked" in states
                inventory[kind + "Empty"] += empty
                inventory[kind + "Occupied"] += not empty
                inventory[kind + "Ready"] += ready and not ruined
                self.spans[key + ".empty"].observe(frame, empty)
                self.spans[key + ".readyWaiting"].observe(frame, ready and not ruined)
                if kind != "bowl":
                    duration, progress = entity.get("cookingTime", -1), entity.get("cookingProgress", -1)
                    overdoing = not empty and duration > 0 and progress > 1.3 * duration
                    parent = parents.get(entity_id, {})
                    self.vessels[key].update({"cookingTime": duration, "cookingProgress": progress,
                        "onHeat": "CookingStation" in (parent.get("components") or []),
                        "overdoingWarning": overdoing,
                        "secondsToNativeBurnThresholdIfHeating": round(max(0, 2 * duration - progress), 4) if not empty and duration > 0 else None})
                    self.spans[key + ".overdoingWarning"].observe(frame, overdoing)
                    inventory[kind + "OverdoingWarning"] += overdoing
            if ruined and entity_id not in self.burn_seen:
                self.burn_seen.add(entity_id)
                self.keep(self.burns, {"firstObservedFrame": frame, "entityId": entity_id, "role": kind,
                                       "name": entity.get("name"), "nativeFoodStates": sorted(states), "ingredientIds": ids})
            if kind == "plate":
                inventory["singleCleanPlates" if not ids else "filledPlates"] += 1
                if not ids and entity_id not in held:
                    parent = parents.get(entity_id, entity)
                    # Counter boundary points are shared surfaces: RegionAt
                    # selects an outer region first, but the center cook can
                    # still access a plate on that exact inner boundary.
                    center = next((bounds for bounds in self.bounds if bounds[0] == "center"), None)
                    pos = parent.get("position", {})
                    if center and center[1] <= pos.get("x", 0) <= center[2] and center[3] <= pos.get("z", 0) <= center[4]:
                        inventory["centerEmptyPlates"] += 1
            if kind in ("drying", "sink", "dirty-return"):
                inventory[kind + "Count"] += max(0, entity.get("plateCount", 0))
            elif "dirty" in (entity.get("plateStackKind") or "").lower():
                inventory["looseDirtyCount"] += max(0, entity.get("plateCount", 0))
            if kind == "food" and ids:
                inventory["looseOrUnplatedItems"] += 1
                if len(ids) == 1:
                    inventory["loose:" + INGREDIENTS.get(ids[0], str(ids[0]))] += 1
        for key, value in self.inventory_last.items():
            self.inventory_total[key] += value * step
        for key, value in inventory.items():
            self.inventory_max[key] = max(self.inventory_max[key], value)
        self.inventory_last = inventory
        self.spans["noCenterEmptyPlate"].observe(frame, inventory["centerEmptyPlates"] == 0)
        self.spans["noSingleCleanPlateAnywhere"].observe(frame, inventory["singleCleanPlates"] == 0)
        current_delivery = state.get("delivered", 0)
        prior_delivery = self.final.get("delivered") or 0
        if current_delivery > prior_delivery:
            delta = current_delivery - prior_delivery
            if self.last_delivery is not None:
                self.delivery_intervals.add(frame - self.last_delivery)
            self.last_delivery = frame
            self.keep(self.deliveries, {"firstObservedFrame": frame, "delivered": current_delivery, "delta": delta,
                                       "score": state.get("score"), "scoreDelta": state.get("score", 0) - (self.final.get("score") or 0),
                                       "serviceRegion": self.last_regions.get(2)})
            if self.wave:
                self.wave["deliveries"] += delta
        for event in state.get("gameEvents", []):
            if event.get("kind") == "delivery" and not event.get("scoreApplied"):
                continue  # A pending record can finalize under the same native index.
            index = event.get("index")
            if index in self.native_seen:
                continue
            self.native_seen.add(index)
            if event.get("kind") == "delivery" and event.get("nativeMatch") and event.get("scoreApplied"):
                self.native_delivery_count += max(0, event.get("deliveryDelta", 1))
            self.keep(self.native_events, {k: event.get(k) for k in ("index", "kind", "gameplayFrame", "recipeId", "recipe", "nativeMatch",
                "scoreApplied", "scoreDelta", "tipDelta", "deliveryDelta", "remainingFraction", "reason")})
        self.previous = {"chefs": chefs}
        self.end, self.final = frame, self.score(state)

    def report(self):
        elapsed = max(0, self.end - self.start)
        players = []
        for player, counters in sorted(self.players.items()):
            players.append({"player": player, **{k.replace("Frames", "Seconds"): seconds(v) for k, v in counters.items() if not k.startswith("region:")},
                "regionSeconds": {k[7:]: seconds(v) for k, v in counters.items() if k.startswith("region:")}})
        def groups(stats, names):
            return [{**dict(zip(names, key)), **value.report()} for key, value in sorted(stats.items(), key=lambda p: (-p[1].total, p[0]))]
        outstanding = [self.job_observation(player, job, self.end) for player, job in self.jobs.items()]
        outstanding.extend({**self.job_observation(player, job, self.end), "completionPendingAfterReplacement": True}
                           for player, pending in self.displaced_jobs.items() for job, _ in pending)
        outstanding.extend({**self.job_observation(player, job, pause["frame"]), "suspended": True,
                            "pausedAtFrame": pause["frame"], "elapsedSeconds": seconds(self.end - job["frame"]),
                            "pausedSeconds": seconds(job.get("_paused_frames", 0) + self.end - pause["frame"]),
                            "ownedResourcesAtPause": pause.get("resources", [])}
                           for player, (job, pause) in self.paused_jobs.items())
        open_actions = [{"player": player, "type": action["spec"].get("type"), "stage": action["stage"],
                         "observedSeconds": seconds(self.end - action["start"]), "specification": action["spec"]} for player, action in self.actions.items()]
        waves = list(self.waves)
        if self.wave:
            waves.append({**self.wave, "endFrame": self.end, "seconds": seconds(self.end - self.wave["arrivalFrame"]), "openAtEnd": True})
        native_complete = self.final.get("timer") is not None and self.final["timer"] <= 0 and self.final.get("serverRoundActive") is False and self.final.get("clientRoundActive") is False
        return {"classification": "native-round-ended-observed" if native_complete else "partial-native-run",
            "start": self.initial, "end": self.final, "elapsedSeconds": seconds(elapsed), "samples": self.samples,
            "duplicateFrameSamples": self.duplicates, "largestSampleGapFrames": self.largest_gap, "initialization": self.initialized,
            "players": players, "completedJobs": groups(self.job_stats, ("player", "job")),
            "recentCompletedJobTimeline": list(self.job_timeline),
            "completedJobSuspensions": groups(self.pause_stats, ("player", "job")), "recentJobSuspensionTimeline": list(self.pause_timeline),
            "completedActions": groups(self.action_stats, ("player", "type")), "completedStages": groups(self.stage_stats, ("player", "type", "stage")),
            "openJobs": outstanding, "openActions": open_actions,
            "averageInventory": {k: round(v / elapsed, 4) if elapsed else None for k, v in sorted(self.inventory_total.items())},
            "endInventory": dict(self.inventory_last), "maximumInventory": dict(self.inventory_max), "vessels": self.vessels,
            "intervals": {key: value.finish(self.end) for key, value in sorted(self.spans.items())},
            "deliveriesObserved": (self.final.get("delivered") or 0) - (self.initial.get("delivered") or 0),
            "nativeConfirmedDeliveries": self.native_delivery_count, "deliveryIntervals": self.delivery_intervals.report(),
            "deliveries": self.deliveries, "nativeEvents": self.native_events, "serviceWaves": waves,
            "regionTransitions": self.transitions, "regionTransitionCount": self.transition_count,
            "burnOrOvermixEntityCount": len(self.burn_seen), "burnOrOvermixObservations": self.burns,
            "guardCounts": [{"event": name, "reason": reason, "count": count} for (name, reason), count in self.guard_counts.items()],
            "guardExamples": self.guards, "firstPlannerFailure": self.first_failure, "plannerFinished": self.finished,
            "eventCounts": dict(self.event_counts), "anomalies": dict(self.anomalies)}


def analyze(path, limit=100):
    initial_length = path.stat().st_size
    report = {"source": str(path.resolve()), "sourceBytesAtStart": initial_length, "streamStatus": "complete",
              "notes": ["Reads only the file prefix present when analysis starts; a live writer may have produced later frames.",
                "Durations are logical gameplay frames / 60. Parallel chef/job times must not be added as round duration.",
                "Inventory and vessel intervals use observed samples, held constant between samples. Check largestSampleGapFrames.",
                "Idle means no emitted planner job. Disabled controls/suppression are native observations, not proof of obstruction.",
                "Completed job duration excludes explicit plannerJobPaused/Resumed intervals; suspended time is reported separately. The firing child owns its active time, so it is not also charged to the suspended job. pausedWorkSeconds can overlap assigned child work or idle time.",
                "pantryChopDelegated closes only the supplier's completed raw-placement span. The timeline labels this delegation explicitly; prepared food is verified later by the separate helper job.",
                "Idle-with-inventory categories overlap and describe concurrent observations, not established causes of idleness.",
                "Inferred movement stall means at least six consecutive sampled one-frame steps with movement input >=0.2, enabled controls and <0.003 world-unit movement.",
                "Region and service-wave boundaries use the planner's measured station geometry. In-flight/disabled control is reported separately.",
                "Center empty plates are observable inventory and can include planner-reserved plates; absence indicates starvation, presence does not prove allocatability.",
                "Burn/overmix means a native food-state label. No fire-state hook is available; no claim of detecting every fire.",
                "Native ServerCookingHandler warns above 1.3*cookingTime and burns above 2*cookingTime; remaining heating seconds are observational arithmetic, not a safety guarantee or wall-time countdown.",
                f"Example lists are capped at {limit}; aggregates include the entire captured prefix. No snapshots are retained."]}
    segments, current, lines = [], None, 0
    with path.open("rb", buffering=0) as raw:
        prefix = Prefix(raw, initial_length)
        with io.BufferedReader(prefix) as limited:
            source = gzip.GzipFile(fileobj=limited) if path.suffix.lower() == ".gz" else limited
            try:
                for line in source:
                    if not line.endswith(b"\n"):
                        report["streamStatus"] = "partial-last-line-excluded"
                        break
                    lines += 1
                    if not line.strip():
                        continue
                    entry = json.loads(line)
                    if entry.get("kind") == "event":
                        if current:
                            current.event(entry.get("name", "unknown"), entry.get("value"))
                        continue
                    state = (entry.get("response") or {}).get("state")
                    if not isinstance(state, dict) or state.get("scene") != "s_Day_3_4" or not isinstance(state.get("gameplayFrame"), (int, float)) or state["gameplayFrame"] < 0:
                        continue
                    if current is None or state["gameplayFrame"] < current.end or state.get("levelFrameZero") != current.level_zero:
                        if current:
                            segments.append(current.report())
                        if len(segments) >= 32:
                            report["streamStatus"] = "segment-limit-reached"
                            break
                        current = Segment(state, limit)
                    current.snapshot(state, entry.get("request") or {})
            except EOFError as error:
                report["streamStatus"], report["streamDetail"] = "partial-gzip-prefix", str(error)
            except (OSError, json.JSONDecodeError) as error:
                report["streamStatus"], report["streamDetail"] = "invalid-stream", f"After {lines} complete lines: {error}"
            finally:
                if source is not limited:
                    source.close()
            report["consumedCompressedOrFileBytes"] = prefix.count
            report["consumedPrefixSha256"] = prefix.digest.hexdigest()
    if current:
        segments.append(current.report())
    report["sourceBytesAtFinish"], report["linesRead"], report["segments"] = path.stat().st_size, lines, segments
    report["grewDuringRead"] = report["sourceBytesAtFinish"] > initial_length
    return report


def console_summary(report, top):
    results = []
    for segment in report["segments"]:
        important = {k: v for k, v in segment["intervals"].items() if k.startswith("pot:") or k.startswith("no") or "Stall" in k}
        results.append({"classification": segment["classification"], "end": segment["end"], "elapsedSeconds": segment["elapsedSeconds"],
            "players": segment["players"], "deliveriesObserved": segment["deliveriesObserved"], "nativeConfirmedDeliveries": segment["nativeConfirmedDeliveries"],
            "deliveryIntervals": segment["deliveryIntervals"], "serviceWaves": segment["serviceWaves"],
            "averageInventory": segment["averageInventory"], "intervals": {k: {f: v[f] for f in ("count", "totalSeconds", "maxSeconds")} for k, v in important.items()},
            "endCookingWarnings": [v for v in segment["vessels"].values() if v.get("overdoingWarning")],
            "largestSampleGapFrames": segment["largestSampleGapFrames"], "topCompletedJobs": segment["completedJobs"][:top],
            "openJobs": segment["openJobs"], "openActions": segment["openActions"], "burnOrOvermixEntityCount": segment["burnOrOvermixEntityCount"],
            "firstPlannerFailure": segment["firstPlannerFailure"], "plannerFinished": segment["plannerFinished"], "anomalies": segment["anomalies"]})
    return {"source": report["source"], "streamStatus": report["streamStatus"], "grewDuringRead": report["grewDuringRead"], "segments": results}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("trace", type=Path)
    parser.add_argument("--out", type=Path)
    parser.add_argument("--top", type=int, default=5)
    parser.add_argument("--example-limit", type=int, default=100)
    args = parser.parse_args()
    if args.out and args.out.resolve() == args.trace.resolve():
        parser.error("Report output must not overwrite the input trace")
    if not 1 <= args.example_limit <= 1000 or not 0 <= args.top <= 50:
        parser.error("example-limit must be 1..1000 and top must be 0..50")
    report = analyze(args.trace, args.example_limit)
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(report, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    summary = console_summary(report, args.top)
    if args.out:
        summary["report"] = str(args.out.resolve())
    print(json.dumps(summary, indent=2, allow_nan=False))
    return 2 if report["streamStatus"] == "invalid-stream" or not report["segments"] else 0


if __name__ == "__main__":
    sys.exit(main())
