"""File-only stationary-transfer A/B audit; failures retain a pinned diagnostic.

The executing bundle is operator-selected provenance, not a native executable attestation.
Memory retains initial/current state and at most four pending neutral confirmations.
"""
import argparse
import collections
import copy
import gzip
import hashlib
import json
import math
from pathlib import Path

from check_bowl_offmix import controller_evidence, ingredients, pin, require
from check_far_pot_interception import neutral, registration
from check_plated_sauce_before_onion import Checker
from check_waypoint_comparison import diff_summary

TRANSFER_TYPES = {'take', 'place', 'combine', 'apply', 'assemble'}


def route_pair(a, b):
    stripped = copy.deepcopy(b); differences = []
    require(len(a['jobs']) == len(b['jobs']) == 5, 'Wrong A/B job count')
    for old, new in zip(a['jobs'], stripped['jobs']):
        require(len(old['actions']) == len(new['actions']), 'Different A/B action count')
        for index, (x, y) in enumerate(zip(old['actions'], new['actions'])):
            require('stationaryTargetTransfer' not in x, 'Baseline shortcut flag must be absent')
            if x['type'] in TRANSFER_TYPES:
                require(y.pop('stationaryTargetTransfer', None) is True, 'Missing candidate transfer flag')
                differences.append({'job': old['id'], 'actionIndex': index, 'type': x['type']})
            else:
                require('stationaryTargetTransfer' not in y, 'Flag added to non-transfer action')
    require(a == stripped and len(differences) == 13, 'Routes differ beyond the thirteen transfer flags')
    return differences


def vec(value):
    values = tuple(value[k] for k in ('x', 'y', 'z'))
    require(all(type(v) in (int, float) and math.isfinite(v) for v in values), 'Missing/nonfinite native vector')
    return values


def still(value):
    x, y, z = vec(value)
    return math.hypot(x, z) <= .00001 and abs(y) <= .01


def same_pose(a, b, tolerance=.0001):
    return all(abs(x-y) <= tolerance for x, y in zip(vec(a), vec(b)))


def material(entity):
    def food(value):
        if isinstance(value, dict): return {k: food(v) for k, v in value.items() if k != 'progress'}
        if isinstance(value, list): return [food(v) for v in value]
        return value
    keys = ['attachedEntityId', 'spawnPrefab', 'switchIndex', 'ingredientIds', 'workStage', 'workSubStage',
            'cookingTime', 'cookingTypeId', 'mixingTime', 'mixingTypeId', 'composition', 'contents']
    return {k: food(entity.get(k)) for k in keys}


def chef(state, player):
    return next(c for c in state['chefs'] if c['playerId'] == player)


def native_entities(state):
    return {e['id']: e for e in state['entities'] if e.get('active')}


def family(state, station):
    found = {station}
    # Native attachment relation, without assuming that the reported target is
    # always the furniture rather than its attached original vessel/source.
    changed = True
    while changed:
        old = set(found)
        for e in state['entities']:
            attached = e.get('attachedEntityId', 0)
            if attached and (e['id'] in found or attached in found): found.update((e['id'], attached))
        changed = old != found
    return found


def stationary(state, player):
    c = chef(state, player)
    require(all(c[k] is True for k in ('controlsEnabled', 'canAcceptInput', 'directlyControlled')) and
            all(c[k] is False for k in ('inputSuppressed', 'useSuppressed', 'respawning', 'aimingThrow')),
            'Shortcut native control gate is closed')
    require(math.isfinite(c['dashTimer']) and c['dashTimer'] <= 0 and math.isfinite(c['impactTimer']) and c['impactTimer'] < 0 and
            all(c[k] == 0 for k in ('interactingEntityId', 'clientPredictedInteractionId', 'serverInteractionId', 'trackedThrowableEntityId')),
            'Shortcut chef is dashing/impacted/interacting/throwing')
    require(all(still(c[k]) for k in ('velocity', 'lastVelocity', 'impactVelocity', 'surfaceVelocity', 'windVelocity')),
            'Shortcut actual/cached native motion is not stationary')
    require(same_pose(c['groundNormal'], {'x': 0, 'y': 1, 'z': 0}, .001), 'Shortcut chef is not on level ground')
    x, y, z = vec(c['forward']); vec(c['position'])
    require(.999 <= math.hypot(x, z) <= 1.001 and abs(y) <= .001, 'Shortcut forward is invalid')
    require(not any(e.get('active') and (e.get('throwFlying') or e.get('cannonFlying')) for e in state['entities']), 'Native flight during shortcut')
    require(state['framesSinceNoPhysics'] in range(6) and abs(state['unityDeltaTime']-1/60) < .00001 and
            abs(state['fixedDeltaTime']-.02) < .00001, 'Shortcut native clock phase/configuration unavailable')


class TransferAudit:
    def __init__(self, plan, enabled):
        self.plan = plan; self.enabled = enabled; self.last = self.initial = None
        self.jobs = {}; self.current = {}; self.counts = collections.Counter(); self.events = collections.Counter()
        self.actions = []; self.job_times = {}; self.candidates = {}; self.pending_edges = {}; self.receipts = []
        self.shortcut_receipts = []; self.confirmations = []; self.refusals = []

    def event(self, name, v):
        require(self.last is not None, 'Event before native observation')
        state = self.last['state']; gf = state['gameplayFrame']; self.events[name] += 1
        if name == 'jobStart':
            self.jobs[v['player']] = v['id']; self.job_times[v['id']] = {'start': gf}
        elif name == 'jobComplete':
            self.job_times[v['id']]['end'] = gf
        elif name == 'actionCreated':
            p = v['player']; require(p not in self.current, 'Action replaced before completion')
            job = self.jobs[p]; index = self.counts[job]
            self.current[p] = {'job': job, 'actionIndex': index, 'action': v, 'start': gf, 'edges': []}
        elif name == 'actionComplete':
            p = v['action']['player']; action = self.current.pop(p)
            require(action['action'] == v['action'], 'Created/completed action mismatch')
            require(p not in self.candidates and p not in self.pending_edges, 'Action completed with pending confirmation/input')
            action.update(end=gf, elapsedFrames=gf-action['start'], reportedFrames=v['frames'])
            self.actions.append(action); self.counts[action['job']] += 1
        elif name == 'stationaryTransferNeutralConfirmation':
            p = v['player']; action = self.current[p]
            require(self.enabled and action['action'].get('stationaryTargetTransfer') is True and p not in self.candidates,
                    'Unauthored/duplicate stationary confirmation')
            stationary(state, p); c = chef(state, p)
            require(v['frame'] == state['frame'] and v['gameplayFrame'] == gf and c[v['field']] == v['targetId'] != 0 and
                    c['heldEntityId'] == v['heldId'] and c['position'] == v['position'] and c['velocity'] == v['actualVelocity'] and
                    c['lastVelocity'] == v['cachedVelocity'], 'Confirmation log differs from native observation')
            item = {'event': v, 'response': self.last, 'confirmed': False, 'actionKey': [action['job'], action['actionIndex']]}
            self.candidates[p] = item
            self.confirmations.append({'player': p, 'gameplayFrame': gf, 'frame': state['frame'], 'target': v['targetId'], 'actionKey': item['actionKey']})
        elif name == 'stationaryTransferRejected':
            p = v['player']; require(p in self.candidates and self.enabled, 'Rejection has no prior admitted confirmation')
            self.candidates.pop(p); self.refusals.append({'player': p, 'gameplayFrame': gf, 'reason': v['reason']})
        elif name == 'stationaryTransferEdge':
            p = v['player']; pending = self.candidates.pop(p); before = pending['response']['state']; original = pending['event']
            require(pending['confirmed'] and state['frame'] == before['frame']+1 == v['frame'] and
                    v['candidateFrame'] == before['frame'] and gf == before['gameplayFrame']+1 and
                    abs(state['clientTime']-before['clientTime']-1/60) <= .00001, 'Shortcut lacks one contiguous neutral native frame')
            stationary(state, p); c = chef(state, p); old = chef(before, p)
            require(v['stationId'] == original['stationId'] and v['targetId'] == original['targetId'] == c[original['field']] and
                    v['heldId'] == old['heldEntityId'] == c['heldEntityId'] and same_pose(c['position'], old['position']) and
                    same_pose(c['forward'], old['forward']), 'Shortcut confirmed target/source/pose changed')
            a, b = native_entities(before), native_entities(state)
            identities = v['nativeIncarnations']; require(len({i['id'] for i in identities}) == len(identities), 'Duplicate shortcut identity')
            ids = {i['id'] for i in identities}; required = family(before, v['stationId']) | ({v['heldId']} if v['heldId'] else set())
            require(ids == required and family(state, v['stationId']) == family(before, v['stationId']), 'Missing/changed station attachment or held identity')
            for identity in identities:
                eid = identity['id']; require(eid in a and eid in b, 'Shortcut native entity disappeared')
                require(a[eid]['observedOrdinal'] == b[eid]['observedOrdinal'] == identity['ordinal'] and
                        registration(before, eid)['sequence'] == registration(state, eid)['sequence'] == identity['registration'] and
                        material(a[eid]) == material(b[eid]), 'Shortcut source incarnation/material changed during neutral confirmation')
                if eid != v['heldId']: require(same_pose(a[eid]['position'], b[eid]['position']), 'Shortcut station/source moved during confirmation')
            self.current[p]['shortcut'] = {'candidateFrame': before['gameplayFrame'], 'decisionFrame': gf, 'identities': identities}
        elif name == 'interactionTargetVerified':
            p = v['player']; require(p in self.current and p not in self.pending_edges, 'Duplicate/unowned transfer edge event')
            c = chef(state, p)
            require(c[v['field']] == v['targetId'] != 0 and c['heldEntityId'] == v['heldBefore'], 'Edge event differs from native referral')
            receipt = {'player': p, 'decisionFrame': gf, 'target': v['targetId'], 'station': v['resolvedStationId'],
                       'heldBefore': c['heldEntityId'], 'actionKey': [self.current[p]['job'], self.current[p]['actionIndex']],
                       'shortcut': self.current[p].get('shortcut', {}).get('decisionFrame') == gf}
            self.pending_edges[p] = receipt

    def call(self, request, response):
        state = response['state']
        if self.initial is None: self.initial = response
        if request['command'] == 'step':
            pads = {p['player']: p for p in request['inputs']}
            observed = {p['player']: p for p in response['inputs']}
            for p, candidate in self.candidates.items():
                if not candidate['confirmed']:
                    require(state['frame'] == candidate['event']['frame']+1 and neutral(pads[p]) and neutral(observed[p]),
                            'Missing ordinary requested/observed neutral confirmation frame')
                    candidate['confirmed'] = True
            for p, receipt in self.pending_edges.items():
                oldpad = next(x for x in self.last['inputs'] if x['player'] == p)
                require(state['gameplayFrame'] == receipt['decisionFrame']+1 and pads[p]['pickup'] and observed[p]['pickup'] and
                        not oldpad['pickup'] and not any(pads[p][k] for k in ('use', 'dash')) and pads[p]['x'] == pads[p]['y'] == 0,
                        'Transfer event lacks following fresh ordinary stationary pickup edge')
                c = chef(state, p); es = native_entities(state)
                receipt.update(nativeResponseFrame=state['gameplayFrame'], heldAfter=c['heldEntityId'],
                               stationAttachmentAfter=es.get(receipt['station'], {}).get('attachedEntityId'),
                               heldIngredientIdsAfter=ingredients(es.get(c['heldEntityId'], {}).get('composition')))
                self.current[p]['edges'].append(receipt); self.receipts.append(receipt)
                if receipt['shortcut']: self.shortcut_receipts.append(receipt)
            self.pending_edges.clear()
        self.last = response

    def finish(self):
        require(not self.current and not self.candidates and not self.pending_edges and len(self.actions) == 21, 'Incomplete action/shortcut audit')
        if not self.enabled: require(not self.confirmations and not self.refusals and not self.shortcut_receipts, 'Baseline emitted shortcut events')
        flagged = [a for a in self.actions if a['action'].get('stationaryTargetTransfer')]
        return {'eventCounts': dict(self.events), 'confirmations': self.confirmations, 'loggedRejectedConfirmations': self.refusals,
                'issuedWithNativeInputReceipts': len(self.shortcut_receipts), 'nativeInputReceipts': self.receipts,
                'flaggedActionsUsingOrdinaryFallback': sum(not a.get('shortcut') for a in flagged),
                'unloggedInitialIneligibility': 'Initial ineligible observations do not emit a refusal event; no count is inferred.',
                'actionTimings': self.actions, 'jobTimings': self.job_times}


def load_json(path): return json.loads(path.read_text(encoding='utf-8-sig'))


def inspect_run(trace, plan_path, result_path, stderr, enabled, archive):
    plan = load_json(plan_path); mechanism = Checker(plan); audit = TransferAudit(plan, enabled)
    errors = {}; first_failure = None; last = initial = last_request = None; rows = 0
    start = trace.stat(); closed = False
    try:
        with gzip.open(trace, 'rt', encoding='utf-8') as stream:
            for line in stream:
                row = json.loads(line); rows += 1
                if row.get('kind') == 'call':
                    last = row['response']; last_request = row['request']; initial = initial or last
                elif row.get('kind') == 'event' and row.get('name') in ('planFailure', 'actionFailure', 'actionFailed', 'plannerFailure', 'jobFailed') and first_failure is None:
                    first_failure = {'name': row['name'], 'gameplayFrame': last['state']['gameplayFrame'] if last else None,
                                     'value': row.get('value')}
                if 'mechanism' not in errors:
                    try: mechanism.row(row)
                    except (ValueError, KeyError, TypeError, StopIteration) as error: errors['mechanism'] = str(error)
                if 'shortcutAudit' not in errors:
                    try:
                        if row.get('kind') == 'event': audit.event(row['name'], row.get('value') or {})
                        elif row.get('kind') == 'call': audit.call(row['request'], row['response'])
                    except (ValueError, KeyError, TypeError, StopIteration) as error: errors['shortcutAudit'] = str(error)
        closed = True
    except (OSError, EOFError, json.JSONDecodeError) as error: errors['traceRead'] = str(error)
    require((start.st_size, start.st_mtime_ns) == (trace.stat().st_size, trace.stat().st_mtime_ns), 'Trace changed during audit; wait for closed run')
    result = None
    try: result = load_json(result_path)
    except (OSError, ValueError) as error: errors['resultRead'] = str(error)
    mechanism_proof = audit_proof = None
    if 'mechanism' not in errors:
        try: mechanism_proof = mechanism.finish(result)
        except (ValueError, KeyError, TypeError) as error: errors['mechanism'] = str(error)
    if 'shortcutAudit' not in errors:
        try: audit_proof = audit.finish()
        except (ValueError, KeyError, TypeError) as error: errors['shortcutAudit'] = str(error)
    report = {'passed': not errors and first_failure is None and closed, 'errors': errors, 'firstNativeRouteFailure': first_failure,
              'closedGzip': closed, 'rows': rows, 'mechanism': mechanism_proof, 'transfers': audit_proof,
              'lastGameplayFrame': last['state']['gameplayFrame'] if last else None, 'exactFullResultBinding': last is not None and result == last,
              'lastInputsNeutral': last is not None and all(neutral(p) for p in last['inputs']),
              'lastRequestedInputsNeutral': last_request is not None and last_request.get('command') == 'step' and all(neutral(p) for p in last_request['inputs']),
              'files': {k: pin(p) for k, p in {'trace': trace, 'plan': plan_path, 'result': result_path, 'stderr': stderr}.items() if p.exists()}}
    if mechanism_proof:
        plate = native_entities(last['state'])[mechanism_proof['mapping']['plate']]
        report['finalNativePlateComposition'] = plate['composition']
        report['finalIngredientIds'] = ingredients(plate['composition'])
    if not report['passed']:
        archive.write_text(json.dumps({'classification': 'Failed/unqualified stationary transfer attempt; original trace retained',
                                      'lastFullNativeResponse': last, 'lastRequest': last_request, 'errors': errors,
                                      'firstNativeRouteFailure': first_failure}, indent=2)+'\n', encoding='utf-8')
        report['failureArchive'] = pin(archive)
    return report, initial


def compare_timings(a, b):
    left = {(x['job'], x['actionIndex']): x for x in a['transfers']['actionTimings']}
    right = {(x['job'], x['actionIndex']): x for x in b['transfers']['actionTimings']}
    require(left.keys() == right.keys(), 'Different completed A/B actions')
    return [{'job': key[0], 'index': key[1], 'type': x['action']['type'], 'baselineStart': x['start'], 'baselineEnd': x['end'],
             'shortcutStart': right[key]['start'], 'shortcutEnd': right[key]['end'], 'baselineDuration': x['elapsedFrames'],
             'shortcutDuration': right[key]['elapsedFrames']} for key, x in left.items()]


def main():
    parser = argparse.ArgumentParser()
    for side in ('baseline', 'shortcut'):
        parser.add_argument('--'+side, required=True, type=Path)
        parser.add_argument('--'+side+'-result', required=True, type=Path)
        parser.add_argument('--'+side+'-stderr', required=True, type=Path)
        parser.add_argument('--'+side+'-plan', type=Path, default=Path('routes/probes/stationary-transfer-'+side+'.json'))
    parser.add_argument('--controller-bundle', required=True, type=Path); parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args(); differences = route_pair(load_json(args.baseline_plan), load_json(args.shortcut_plan))
    bundle = controller_evidence(args.controller_bundle)
    a, initial_a = inspect_run(args.baseline, args.baseline_plan, args.baseline_result, args.baseline_stderr, False, args.output.with_suffix('.baseline-failure.json'))
    b, initial_b = inspect_run(args.shortcut, args.shortcut_plan, args.shortcut_result, args.shortcut_stderr, True, args.output.with_suffix('.shortcut-failure.json'))
    report = {'format': 'oc2-native-stationary-transfer-comparison', 'version': 1, 'passed': a['passed'] and b['passed'],
              'qualification': 'Bounded native mechanism A/B; no full-round, high-score, or exact replay qualification.',
              'routeFlagDifferences': differences, 'controller': bundle, 'baseline': a, 'shortcut': b,
              'rawInitialResponseDifferences': diff_summary(initial_a, initial_b),
              'checkerFiles': [pin(Path(__file__)), pin(Path(__file__).with_name('check_plated_sauce_before_onion.py')),
                               pin(Path(__file__).with_name('test_stationary_transfer_comparison.py')),
                               pin(Path(__file__).with_name('check_bowl_offmix.py')), pin(Path(__file__).with_name('check_far_pot_interception.py')),
                               pin(Path(__file__).with_name('check_waypoint_comparison.py'))]}
    if report['passed']:
        require(a['mechanism']['nativePluginSha256'] == b['mechanism']['nativePluginSha256'] and
                a['mechanism']['nativeInstrumentationManifestSha256'] == b['mechanism']['nativeInstrumentationManifestSha256'], 'A/B native instrumentation differs')
        report['actionComparison'] = compare_timings(a, b)
        report['shortcutExercised'] = b['transfers']['issuedWithNativeInputReceipts'] > 0
        require(a['finalIngredientIds'] == b['finalIngredientIds'], 'A/B final native ingredient multiset differs')
        report['outputRecipeEvidence'] = {'sameIngredientMultiset': a['finalIngredientIds'],
                                         'nativePreparationStepsChecked': {'sausage': 20068, 'onion': 20294},
                                         'sameOriginalPlateInEachRun': True,
                                         'qualification': 'Both native food trees satisfy the preparation-order checker. No serving/matching score event is authored in this probe.'}
        report['wholeEndpoint'] = {'baselineFrames': a['lastGameplayFrame'], 'shortcutFrames': b['lastGameplayFrame'],
                                  'differenceFramesShortcutMinusBaseline': b['lastGameplayFrame']-a['lastGameplayFrame'],
                                  'baselineClientElapsed': a['mechanism']['clientElapsedSeconds'], 'shortcutClientElapsed': b['mechanism']['clientElapsedSeconds'],
                                  'interpretation': 'Actual whole endpoint only. Concurrent local action differences are not summed into elapsed savings.'}
    args.output.write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
    print(json.dumps({'passed': report['passed'], 'output': str(args.output), 'wholeEndpoint': report.get('wholeEndpoint'),
                      'baselineErrors': a['errors'], 'shortcutErrors': b['errors']}))
    return 0 if report['passed'] else 1


if __name__ == '__main__': raise SystemExit(main())
