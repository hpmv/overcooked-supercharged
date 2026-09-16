"""File-only, gated preparation for one native Carnival sausage progress probe.

No RPC, process, game or checkpoint calls. Each phase consumes a NEW jointly
sampled paused full inspection + native food receipt; its output is an unexecuted
operator request. Never concatenate the phases or infer a consumed ingredient
from the typed attachment-placement action's completion.
"""
from __future__ import annotations

import argparse
from collections import Counter
import copy
import hashlib
import json
from pathlib import Path

from framework_kitchen_planner import Observation, PlanningError, SAUSAGE, finite, region
from framework_native_search import require_native_boundary, native_clock_state, exact_values
from framework_pause_boundary import native_physics


def observation(snapshot, receipt):
    require_native_boundary(receipt)
    native_clock_state(receipt['bridge'])
    if receipt.get('detail', {}).get('source') != 'native-server-preparation-composition':
        raise PlanningError('Actual native food telemetry is required')
    return Observation(snapshot, receipt['bridge']['nativeRound'], receipt)


def fixed_at(o, kind, components, x, z):
    found = []
    for eid, e in o.entities.items():
        if e['className'] != kind or e['path'] != [eid] or eid not in o.initial:
            continue
        p, q = o.initial[eid]['Pos'], e['position']
        if (max(abs(finite(p['X'], 'initial X')-x), abs(finite(p['Z'], 'initial Z')-z),
                abs(finite(q['x'], 'current X')-x), abs(finite(q['z'], 'current Z')-z)) <= .04
                and set(components) <= set(o.registry[eid]['Components'])):
            found.append(eid)
    if len(found) != 1:
        raise PlanningError('Expected one actual fixed class/component/initial+current pose match')
    return found[0]


def paired(o, parent, item):
    actual = o.entity(parent).get('data', {}).get('attachment', {}).get('path', [])
    if o.parent(item) != parent or actual != o.entity(item)['path'] or o.on(parent) != item:
        raise PlanningError('Exact two-sided native attachment is missing')


def empty_hands(o, chefs):
    return all(o.held(i) is None and not o.entity(i).get('data', {}).get('attachment', {}).get('path') for i in chefs)


def graph(chef, resources, rows, maximum=360):
    actions = []
    for i, row in enumerate(rows):
        row = dict(row, chef=chef, resources=resources, dash=False,
                   timeoutFrames=30 if row['type'] == 'wait' else 180)
        row['id'] = f'phase-{i}'
        if i:
            row['after'] = [f'phase-{i-1}']
        actions.append(row)
    return {'command': 'actions', 'maximumFrames': maximum, 'actions': actions}


def pot_empty(o, case):
    paired(o, case['home'], case['pot'])
    f = o.foods.get(case['pot'], {})
    if (o.facts(case['pot']) is None or o.facts(case['pot'])[0] or
            f.get('cookingTime') != 12 or f.get('cookingProgress') != 0 or
            f.get('cookingState') != 'Raw' or f.get('composition', {}).get('cookingStepId') != 20068):
        raise PlanningError('Expected the actual empty original 12-second sausage pot')


def initial_case(snapshot, receipt):
    o = observation(snapshot, receipt)
    if any(e['path'] != [eid] for eid, e in o.entities.items()):
        raise PlanningError('Initial fixture requires the validated fixed native inventory')
    chefs = sorted(i for i, e in o.entities.items() if e.get('chef') is not None)
    if len(chefs) != 4 or not empty_hands(o, chefs):
        raise PlanningError('Four empty-handed observed chefs required')
    supplier = [i for i in chefs if region(o.entity(i)['position']) == 'UL']
    if len(supplier) != 1:
        raise PlanningError('Expected one native upper-left supplier')
    crate = fixed_at(o, 'sausage-crate', ['PickupItemSpawner', 'ServerPickupItemSpawner'], 14.4, -10.8)
    if o.crate(SAUSAGE) != crate:
        raise PlanningError('The selected native crate does not spawn Frankfurter')
    handoff = o.role('left-pass')
    if o.on(handoff) is not None or o.entity(handoff)['data'].get('attachment'):
        raise PlanningError('Shared handoff must be natively empty')
    home = fixed_at(o, 'heat-station', ['AttachStation', 'ServerAttachStation', 'CookingStation', 'ServerCookingStation'], 16.8, -10.8)
    pot = o.on(home)
    if pot is None or o.entity(pot)['className'] != 'pot' or not {
            'ServerCookableContainer', 'ServerCookingHandler', 'ServerIngredientContainer'} <= set(o.registry[pot]['Components']):
        raise PlanningError('Original home needs its actual native sausage pot')
    centers = [i for i in chefs if region(o.entity(i)['position']) == 'center']
    if len(centers) != 2:
        raise PlanningError('Expected two central chefs')
    center = min(centers, key=lambda i: ((o.entity(i)['position']['x']-16.8)**2 +
                                       (o.entity(i)['position']['z']+13.2)**2, i))
    case = {'version': 1, 'kind': 'single-sausage-cooking-progress', 'phase': 'stage',
            'classification': 'unexecuted native mechanism fixture; no achievement or replay claim',
            'initialFrame': o.frame, 'crate': crate, 'handoff': handoff, 'pot': pot, 'home': home,
            'supplier': supplier[0], 'center': center, 'chefs': chefs,
            'fixedTokens': {str(i): o.token(i) for i in o.entities},
            'fixedParents': {str(i): o.parent(i) for i in o.entities},
            'foods': {str(i): f.get('composition') for i, f in o.foods.items()},
            'ledger': copy.deepcopy(o.native_round['ledger'])}
    pot_empty(o, case)
    # This fixture begins cold. No unrelated food deadline is permitted during setup.
    for i, f in o.foods.items():
        if any(f.get(k, 0) != 0 for k in ('cookingProgress', 'mixingProgress', 'workStage', 'workSubStage')):
            raise PlanningError('Initial fixture has unrelated processing work')
        if o.facts(i) and o.facts(i)[0] and o.entity(i)['className'] != 'condiment-dispenser':
            raise PlanningError('Initial fixture has unrelated food')
    case['request'] = graph(supplier[0], [crate, handoff, pot, home], [
        {'type': 'goto', 'x': 14.4, 'z': -12.0}, {'type': 'wait', 'frames': 6},
        {'type': 'pickup', 'target': crate, 'expectSpawn': True},
        {'type': 'goto', 'x': 14.4, 'z': -13.2}, {'type': 'wait', 'frames': 6},
        {'type': 'place', 'target': handoff}])
    return case


def unchanged_fixed(case, o, permit_pot_food=False):
    for key, token in case['fixedTokens'].items():
        if o.token(int(key)) != token or o.parent(int(key)) != case['fixedParents'][key]:
            raise PlanningError('An original fixed identity or attachment changed')
    if not exact_values(o.native_round['ledger'], case['ledger']):
        raise PlanningError('Preparation fixture changed native score/delivery ledger')
    for key, food in case['foods'].items():
        if permit_pot_food and int(key) == case['pot']:
            continue
        if not exact_values(o.foods.get(int(key), {}).get('composition'), food):
            raise PlanningError('Unrelated original food composition changed')


def staged_case(case, snapshot, receipt):
    if case['phase'] != 'stage':
        raise PlanningError('Expected completed staging phase')
    o = observation(snapshot, receipt)
    unchanged_fixed(case, o)
    pot_empty(o, case)
    if snapshot.get('typedActions', {}).get('outcome') != 'complete':
        raise PlanningError('Staging graph has not completed')
    source = o.on(case['handoff'])
    if source is None or o.entity(source)['className'] != 'sausage' or o.entity(source)['path'][:-1] != [case['crate']]:
        raise PlanningError('One actual raw Frankfurter from the exact crate must occupy the handoff')
    paired(o, case['handoff'], source)
    if o.facts(source) is None or o.facts(source)[0] != Counter({SAUSAGE: 1}) or not empty_hands(o, case['chefs']):
        raise PlanningError('Exact native raw sausage or empty hands proof missing')
    if any(i not in {int(k) for k in case['foods']} | {source} for i in o.foods):
        raise PlanningError('Unexpected additional prepared/raw food')
    result = copy.deepcopy(case)
    result.update(phase='load-edge', source=source, sourceToken=o.token(source), stagedFrame=o.frame)
    result['request'] = graph(case['center'], [source, case['handoff'], case['pot'], case['home']], [
        {'type': 'goto', 'x': 16.8, 'z': -13.2}, {'type': 'wait', 'frames': 6},
        {'type': 'pickup', 'target': source},
        {'type': 'goto', 'x': 16.8, 'z': -12.0}, {'type': 'wait', 'frames': 6}])
    return result


def neutral_request(chefs, frames, pickup_chef=None):
    return {'command': 'raw-input', 'segments': [{'frames': frames, 'chefs': {
        str(i): {'x': 0, 'y': 0, 'pickup': i == pickup_chef, 'interact': False, 'dash': False}
        for i in chefs}}]}


def load_edge(case, snapshot, receipt):
    if case['phase'] != 'load-edge':
        raise PlanningError('Expected verified relay before pot load edge')
    o = observation(snapshot, receipt)
    unchanged_fixed(case, o)
    pot_empty(o, case)
    source, chef = case['source'], case['center']
    if snapshot.get('typedActions', {}).get('outcome') != 'complete' or o.token(source) != case['sourceToken']:
        raise PlanningError('Relay completion or source incarnation changed')
    paired(o, chef, source)
    if o.facts(source) is None or o.facts(source)[0] != Counter({SAUSAGE: 1}):
        raise PlanningError('Held native ingredient changed')
    if not empty_hands(o, [i for i in case['chefs'] if i != chef]) or o.on(case['handoff']) is not None:
        raise PlanningError('Unexpected held item or occupied handoff')
    entity, state = o.entity(chef), o.entity(chef)['chef']
    if state.get('highlightedForPlacement', {}).get('path') != [case['home']]:
        raise PlanningError('Exact current native placement target is not the original home')
    if state.get('aimingThrow') or state.get('movementInputSuppressed') or state.get('currentlyInteracting', {}).get('path'):
        raise PlanningError('Chef is not in ordinary unoccupied control')
    if state.get('dashTimer', 1) > 0 or state.get('impactTimer', 1) > 0:
        raise PlanningError('Dash/impact state is active or absent')
    vectors = [entity['velocity'], state.get('lastVelocity', {}), state.get('lastMoveInputDirection', {})]
    bodies = [b for b in native_physics(receipt, case['chefs'])['bodies'] if b['entityId'] == chef]
    if len(bodies) != 1:
        raise PlanningError('Native chef body is missing or ambiguous')
    vectors += [bodies[0]['rawVelocity'], bodies[0]['resumeVelocity']]
    if any(finite(v.get(k, 0), 'horizontal motion') != 0 for v in vectors for k in ('x', 'z')):
        raise PlanningError('Chef actual/cached/resumable horizontal motion must be zero')
    result = copy.deepcopy(case)
    result.update(phase='loaded', edgeFrame=o.frame,
                  request=neutral_request(case['chefs'], 1, chef),
                  automaticReleaseFrames=2, edgeMayBeRefusedByNativeRules=True)
    return result


def loaded_case(case, snapshot, receipt):
    if case['phase'] != 'loaded':
        raise PlanningError('Expected observed native load result')
    o = observation(snapshot, receipt)
    unchanged_fixed(case, o, permit_pot_food=True)
    paired(o, case['home'], case['pot'])
    fixed = {int(i) for i in case['fixedTokens']}
    # Headless registry is a receipt history: actual retired IDs remain there.
    # The current mapped inventory, actual native food and actual body collector
    # must lose the source/container. Do not confuse history with live objects.
    if set(o.entities) != fixed or any(b['entityId'] not in fixed for b in native_physics(receipt, case['chefs'])['bodies']):
        raise PlanningError('Spawned source/container remains live in reconstructed or native physical state')
    if case['source'] in o.foods or not empty_hands(o, case['chefs']):
        raise PlanningError('Native source consumption/empty hands is not observed')
    f = o.foods[case['pot']]
    if (o.facts(case['pot'])[0] != Counter({SAUSAGE: 1}) or f.get('cookingTime') != 12 or
            f.get('composition', {}).get('cookingStepId') != 20068 or f.get('cookingState') != 'Raw' or
            not 0 < finite(f.get('cookingProgress'), 'native cooking progress') < 2):
        raise PlanningError('Expected one exact newly cooking sausage in the unchanged 12-second pot')
    result = copy.deepcopy(case)
    result.update(phase='progress-ready', loadedFrame=o.frame, loadedProgress=f['cookingProgress'],
                  request=neutral_request(case['chefs'], 60), automaticReleaseFrames=2,
                  expectedObservedFrames=62, existingProbeWarmupFrames=30,
                  nativeReplayVerified=False,
                  remainingProof='Trace must bind exact source spawn, ingredient consumption and both source/container removal; then existing input probe must verify restore and replay including progress, clocks, physics and events.')
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--phase', choices=['initial', 'staged', 'edge', 'loaded'], required=True)
    parser.add_argument('--snapshot', type=Path, required=True)
    parser.add_argument('--native', type=Path, required=True)
    parser.add_argument('--case', type=Path)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    snapshot, receipt = json.loads(args.snapshot.read_text()), json.loads(args.native.read_text())
    if args.phase == 'initial':
        result = initial_case(snapshot, receipt)
    else:
        if args.case is None:
            parser.error('--case is required after initial phase')
        case = json.loads(args.case.read_text())
        result = {'staged': staged_case, 'edge': load_edge, 'loaded': loaded_case}[args.phase](case, snapshot, receipt)
    result['inputFiles'] = {str(p.resolve()): hashlib.sha256(p.read_bytes()).hexdigest()
                           for p in [args.snapshot, args.native] + ([args.case] if args.case else [])}
    args.out.write_text(json.dumps(result, indent=2, allow_nan=False))
    print(json.dumps({'phase': result['phase'], 'out': str(args.out), 'unexecuted': True}))


if __name__ == '__main__':
    main()
