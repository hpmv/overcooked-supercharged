"""Read-only proof of native interception followed by exact original-pot insertion."""
import argparse
import gzip
import hashlib
import json
from pathlib import Path
from check_bowl_offmix import ingredients, ordinary_request, pin, require, controller_evidence


def neutral(pad):
    return pad['x'] == pad['y'] == 0 and not any(pad[k] for k in ['pickup', 'use', 'dash'])


def registration(state, source):
    audit = state['entityRegistration']
    require(audit.get('installed') and audit.get('addHookInstalled') and audit.get('removeHookInstalled')
            and audit.get('errorCount') == audit.get('roundDropped') == 0, 'Native registration observation is incomplete')
    events = [e for e in audit['events'] if (e.get('entity') or {}).get('entityId') == source]
    require(events, 'Source lacks actual native registration provenance')
    event = max(events, key=lambda e: e['sequence'])
    require(event['kind'] == 'register' and event['entity']['active']
            and event['entity']['observedRegistrationSequence'] == event['sequence'], 'Source is not its active registered incarnation')
    return {k: event[k] for k in ['sequence', 'gameplayFrame', 'method', 'entity']}


def audit_rows(rows, plan, result):
    """Additional independent action, clock, input, source and complete-result binding."""
    names = ['FI01-near-pot', 'FI02-catch-position', 'FI03-far-throw', 'FI04-native-catch-placement']
    jobs = {j['id']: j for j in plan['jobs']}
    require(list(jobs) == names and len(plan['jobs']) == 4 and 0 < plan['timeoutFrames'] <= 1200, 'Wrong bounded authored probe')
    require([j['player'] for j in plan['jobs']] == [2, 3, 2, 3]
            and [[a['type'] for a in j['actions']] for j in plan['jobs']] ==
            [['take', 'navigate', 'throw'], ['navigate', 'face'], ['take', 'navigate', 'throw'], ['wait', 'wait', 'place']]
            and [j['dependencies'] for j in plan['jobs']] == [[], [names[0]], [names[1]], [names[1]]], 'Unexpected authored actions/dependencies')
    initial = last = last_response = last_request = previous_pads = None
    plugin = manifest = source_receipt = native_flight = release = None
    caught_source = False
    original_vessels = {}
    active, counts, pickups, completed, actions = {}, {}, {}, [], []
    calls = steps = headers = 0; input_hash = hashlib.sha256()
    for row in rows:
        if row.get('kind') == 'header':
            require(headers == calls == 0 and row.get('format') == 'overcooked-tas-trace' and row.get('version') == 1 and row.get('mode') == 'plan', 'Unexpected trace header')
            headers += 1; continue
        if row.get('kind') == 'event':
            name, value = row['name'], row.get('value') or {}
            require(last is not None and name not in ['actionFailure', 'actionFailed', 'planFailure', 'plannerFailure', 'jobFailed'], 'Failed probe or event before frame zero')
            if name == 'jobStart':
                job = jobs.get(value['id'])
                require(job and value['player'] == job['player'] and value['resources'] == job['resources']
                        and job['player'] not in active and job['id'] not in counts and all(d in completed for d in job['dependencies']), 'Job ownership/dependencies differ from authored plan')
                active[job['player']] = job['id']; counts[job['id']] = 0
            elif name == 'actionComplete':
                action = value['action']; player = action['player']; job_id = active.get(player)
                require(job_id is not None and counts[job_id] < len(jobs[job_id]['actions']), 'Unauthored completed action')
                expected = dict(jobs[job_id]['actions'][counts[job_id]]); expected['player'] = player; expected.setdefault('timeoutFrames', 600)
                require(action == expected, 'Completed action differs from authored action')
                counts[job_id] += 1; actions.append({'job': job_id, 'frame': last['gameplayFrame'], 'action': action})
            elif name == 'jobComplete':
                job = jobs.get(value['id'])
                require(job and active.get(job['player']) == job['id'] and counts[job['id']] == len(job['actions']), 'Job completed without exact actions')
                completed.append(job['id']); del active[job['player']]
            elif name == 'throwResolved' and active.get(value.get('player')) == names[2]:
                require(source_receipt is None and value['recipientPlayer'] == -1, 'Duplicate or redirected far throw')
                e = next(x for x in last['entities'] if x['id'] == value['itemId'] and x.get('active'))
                chef = next(c for c in last['chefs'] if c['playerId'] == 2)
                require(chef['heldEntityId'] == e['id'] and e['id'] in pickups and e['composition']['type'] == 'IngredientAssembledNode'
                        and ingredients(e['composition']) == [284626], 'Far throw is not the original raw source picked up from its native crate')
                source_receipt = {'id': e['id'], 'ordinal': e['observedOrdinal'], 'composition': e['composition'],
                                  'registration': registration(last, e['id']), 'pickup': pickups[e['id']], 'targetEntityId': value['targetEntityId'], 'target': value['target']}
                target_home = original_vessels.get(value['targetEntityId'])
                require(target_home and abs(target_home['homePosition']['x'] - 18) < .05, 'Throw target is not the original far vessel')
                require(abs(value['target']['x'] - 18) < .05 and abs(value['target']['z'] + 10.8) < .05
                        and value['nativeThrowForce'] == chef['throwForce'] == 18 and value['nativeThrowInclination'] == chef['throwInclination'] == 12, 'Far native aim or throw force changed')
            elif name == 'throwReleased' and source_receipt and value['itemId'] == source_receipt['id']:
                require(release is None and value['player'] == 2 and value['target'] == source_receipt['target'], 'Duplicate/incorrect far release'); release = value
            continue
        require(row.get('kind') == 'call', 'Unknown trace record kind')
        req, res = row['request'], row['response']; ordinary_request(req); calls += 1
        allowed = {'restart': {'version', 'command', 'seed', 'isolateRecipeRandom'}, 'inspect': {'version', 'command'}, 'step': {'version', 'command', 'steps', 'inputs'}}[req['command']]
        require(set(req) <= allowed and res.get('ok') is True and res.get('paused') is True, 'Unexpected fields, failed call or unpaused response')
        require(req['command'] != 'restart' or calls == 1 and req.get('seed') == 0 and req.get('isolateRecipeRandom') is True, 'Unexpected restart/seed')
        s = res['state']; session = json.loads(res['session']); f = s['gameplayFrame']
        require(session['variantPlayers'] == session['virtualPads'] == session['serverUsers'] == session['clientUsers'] == 4
                and session['dlc'] == 8 and session['stage'] == 'kitchen_ready', 'Wrong native local four-player session')
        require(s['scene'] == 's_Day_3_4' and s['roundDuration']['valuesAgree'] and s['roundDuration']['seconds'] == 270
                and s['roundDuration']['levelConfigName'] == 'Day_3_4_4P', 'Wrong native scene/duration')
        require(s['captureFramerate'] == 60 and s['fixedDeltaTime'] == .02 and not s['timerSuppressed']
                and s['serverRoundActive'] and s['clientRoundActive'], 'Native timing/round state changed')
        require(all(s[k] == 0 for k in ['score', 'baseScore', 'tips', 'delivered', 'deductions']), 'Unexpected native score ledger')
        identity = (s['instrumentation']['manifest']['pluginSha256'], s['instrumentation']['manifestSha256'])
        if plugin is None: plugin, manifest = identity
        require(identity == (plugin, manifest) and not s['instrumentation']['error'], 'Native instrumentation changed or failed')
        chefs = {c['playerId']: c for c in s['chefs']}; pads = {p['player']: p for p in res['inputs']}
        require(len(s['chefs']) == len(res['inputs']) == 4 and set(chefs) == set(pads) == {0, 1, 2, 3}, 'Native player slots are incomplete')
        require(all(neutral(pads[p]) for p in [0, 1]) and not any(p['dash'] for p in pads.values()), 'Unauthored chef/dash input')
        if req['command'] == 'step':
            steps += 1; require(steps <= plan['timeoutFrames'] + 1, 'Probe exceeded bounded authored duration')
            for p in req['inputs']:
                require(set(p) == {'player', 'x', 'y', 'pickup', 'use', 'dash'} and all(p[k] == pads[p['player']][k] for k in ['pickup', 'use', 'dash'])
                        and all(abs(p[k] - pads[p['player']][k]) <= 1e-6 for k in ['x', 'y']), 'Native inputs differ from ordinary request')
            input_hash.update((json.dumps(req, sort_keys=True, separators=(',', ':'), allow_nan=False) + '\n').encode())
        if initial is None:
            require(req['command'] == 'restart' and f == 0 and all(neutral(p) for p in pads.values()), 'Missing neutral fresh frame zero')
            initial = s
            for home in s['entities']:
                if home.get('active') and 'CookingStation' in home['components'] and any(abs(home['position']['x'] - x) < .05 for x in [16.8, 18]) and abs(home['position']['z'] + 10.8) < .05:
                    pot = next(e for e in s['entities'] if e['id'] == home['attachedEntityId'] and e.get('active'))
                    original_vessels[pot['id']] = {'home': home['id'], 'ordinal': pot['observedOrdinal'], 'homeOrdinal': home['observedOrdinal'],
                                                       'position': pot['position'], 'homePosition': home['position']}
            require(len(original_vessels) == 2, 'Missing original two-pot topology')
        elif req['command'] == 'step':
            require(f == last['gameplayFrame'] + 1 and s['frame'] == last['frame'] + 1 and s['timer'] < last['timer']
                    and s['logicalTime'] > last['logicalTime'] and s['clientTime'] > last['clientTime']
                    and s['fixedFrame'] - last['fixedFrame'] == s['physicsStepsThisFrame'] in [0, 1], 'Native per-frame clock progression is incomplete')
        else: require(f == last['gameplayFrame'] and s['timer'] == last['timer'], 'Inspect advanced native gameplay')
        entities = {e['id']: e for e in s['entities'] if e.get('active')}
        require(len(entities) == sum(bool(e.get('active')) for e in s['entities']), 'Duplicate active native entity IDs')
        for vessel, original in original_vessels.items():
            pot, home = entities.get(vessel), entities.get(original['home'])
            require(pot and home and pot['observedOrdinal'] == original['ordinal'] and home['observedOrdinal'] == original['homeOrdinal']
                    and pot['position'] == original['position'] and home['position'] == original['homePosition']
                    and home['attachedEntityId'] == vessel and sum(e.get('attachedEntityId') == vessel for e in entities.values()) == 1
                    and pot['cookingTime'] == 12 and pot['cookingTypeId'] == 20068, 'Original receiving pot/home geometry or native configuration changed')
        if last:
            oldchef = next(c for c in last['chefs'] if c['playerId'] == 2)
            held = entities.get(chefs[2]['heldEntityId'])
            if oldchef['heldEntityId'] == 0 and held and ingredients(held['composition']) == [284626]:
                crate = next((e for e in last['entities'] if e['id'] == oldchef['pickupTargetId'] and e.get('active')), None)
                require(crate and crate.get('spawnPrefab') == 'Frankfurter' and pads[2]['pickup'] and not previous_pads[2]['pickup'], 'Source lacks targeted fresh native crate pickup')
                picked_registration = registration(s, held['id'])
                require(picked_registration['gameplayFrame'] == f, 'Picked source was not registered by this native crate pickup')
                pickups[held['id']] = {'frame': f, 'crate': crate['id'], 'crateOrdinal': crate['observedOrdinal'],
                                      'ordinal': held['observedOrdinal'], 'composition': held['composition'], 'registration': picked_registration}
        for identity, picked in pickups.items():
            e = entities.get(identity)
            if e:
                require(e['observedOrdinal'] == picked['ordinal'] and e['composition'] == picked['composition']
                        and registration(s, identity) == picked['registration'], 'Picked source changed before its native consumption')
        if source_receipt:
            source = entities.get(source_receipt['id'])
            if source:
                require(source['observedOrdinal'] == source_receipt['ordinal'] and source['composition'] == source_receipt['composition']
                        and registration(s, source['id']) == source_receipt['registration'], 'Thrown source incarnation, registration or exact raw composition changed')
                holders = [p for p, c in chefs.items() if c['heldEntityId'] == source['id']]
                if source['throwFlying']:
                    require(release and not holders and source['throwerEntityId'] == source['previousThrowerEntityId'] == chefs[2]['entityId'], 'Native flight lacks exact source/thrower provenance')
                    if native_flight is None:
                        require(previous_pads[2]['use'] and not pads[2]['use'] and oldchef['heldEntityId'] == source['id'], 'Flight lacks original ordinary use-release edge')
                        native_flight = {'frame': f, 'thrower': chefs[2]['entityId']}
                elif holders == [3]: require(native_flight and source['previousThrowerEntityId'] == chefs[2]['entityId'], 'Catch precedes proved native flight')
                if holders == [3]: caught_source = True
                if caught_source: require(holders == [3] and not source['throwFlying'], 'Caught source left its original catcher before native insertion')
            elif caught_source:
                pot = entities[source_receipt['targetEntityId']]
                require(not any(c['heldEntityId'] == source_receipt['id'] for c in chefs.values()) and ingredients(pot['composition']) == [284626]
                        and pot['composition']['type'] == 'CookedCompositeAssembledNode' and pot['composition']['cookingStepId'] == 20068,
                        'Consumed source no longer persists as exact native original-pot contents')
        last, last_response, last_request, previous_pads = s, res, req, pads
    require(headers == 1 and source_receipt and native_flight and len(actions) == 11 and set(completed) == set(names) and len(completed) == 4 and not active, 'Incomplete source/action evidence')
    require(last_request['command'] == 'step' and all(neutral(p) for p in last_request['inputs']) and all(neutral(p) for p in last_response['inputs']), 'Final ordinary input is not neutral')
    require(result == last_response and result.get('ok') is True and result.get('paused') is True, 'Closed result differs from entire final trace response')
    return {'format': 'oc2-native-far-pot-interception-proof', 'version': 2, 'originalSource': source_receipt, 'nativeFlight': native_flight,
            'completedActions': actions, 'calls': calls, 'ordinaryUnitSteps': steps, 'ordinaryInputSha256': input_hash.hexdigest(),
            'completeResultBoundToFinalResponse': True, 'finalInputNeutral': True,
            'nativeClientElapsedSeconds': last['clientTime'] - initial['clientTime'], 'nativeTimerElapsedSeconds': initial['timer'] - last['timer'],
            'pluginTelemetrySha256': plugin, 'instrumentationManifestSha256': manifest}


def check(trace):
    first = last = previous = None
    ids, fixed, jobs = {}, {}, []
    resolved = released = caught = consumed = None
    previous_pads = None
    for line in gzip.open(trace, 'rt', encoding='utf-8'):
        row = json.loads(line)
        if row.get('kind') == 'event':
            name, value = row['name'], row.get('value') or {}
            require(name not in ['actionFailure', 'planFailure'], 'Native action/plan failed')
            if name == 'jobComplete': jobs.append(value['id'])
            if name == 'throwResolved' and value.get('targetEntityId') == ids.get('far'):
                require(resolved is None, 'Duplicate far throw')
                resolved = dict(value)
            if name == 'throwReleased' and resolved and value.get('itemId') == resolved['itemId']:
                released = dict(value)
            continue
        if row.get('kind') != 'call': continue
        request = row['request']; ordinary_request(request)
        response = row.get('response', {}); state = response.get('state')
        if not state or state.get('gameplayFrame', -1) < 0: continue
        def entity(id): return next((e for e in state['entities'] if e['id'] == id and e.get('active')), None)
        if first is None:
            first = state
            require(state['scene'] == 's_Day_3_4' and sorted(c['playerId'] for c in state['chefs']) == [0, 1, 2, 3], 'Wrong kitchen/chef slots')
            for label, x in [('near', 16.8), ('far', 18)]:
                homes = [e for e in state['entities'] if e.get('active') and 'CookingStation' in e.get('components', [])
                         and abs(e['position']['x'] - x) < .05 and abs(e['position']['z'] + 10.8) < .05]
                require(len(homes) == 1, 'Nonunique original native pot home')
                home = homes[0]; pot = entity(home['attachedEntityId'])
                require(pot and 'CookableContainer' in pot['components'] and pot['cookingTime'] == 12
                        and not ingredients(pot.get('composition')), 'Original native pot not empty with 12-second duration')
                ids[label], ids[label + 'Home'] = pot['id'], home['id']
                fixed.update({pot['id']: pot['observedOrdinal'], home['id']: home['observedOrdinal']})
        for id, ordinal in fixed.items():
            require(entity(id) and entity(id)['observedOrdinal'] == ordinal, 'Original pot/home incarnation changed')
        for label in ['near', 'far']:
            require(entity(ids[label + 'Home'])['attachedEntityId'] == ids[label]
                    and not any(c['heldEntityId'] == ids[label] for c in state['chefs']), 'Original pot moved')
            require(entity(ids[label])['cookingTime'] == 12, 'Native cooking duration changed')
        pads = request.get('inputs')
        if request['command'] == 'step' and last:
            require(state['gameplayFrame'] == last['gameplayFrame'] + 1 and state['timer'] < last['timer'], 'Missing unit native clock progression')
        if resolved:
            source = entity(resolved['itemId'])
            holders = [c for c in state['chefs'] if c['heldEntityId'] == resolved['itemId']]
            if holders and holders[0]['playerId'] == 3 and caught is None:
                require(released and len(holders) == 1 and source and not source['throwFlying']
                        and ingredients(source.get('composition')) == [284626]
                        and source['previousThrowerEntityId'] == state['chefs'][2]['entityId'], 'Native catcher lacks exact thrown source provenance')
                require(not ingredients(entity(ids['far']).get('composition')), 'Far pot was filled before catch')
                caught = {'frame': state['gameplayFrame'], 'source': source['id'], 'ordinal': source['observedOrdinal'], 'catcher': 3}
            if caught and source:
                require(source['observedOrdinal'] == caught['ordinal'], 'Caught source incarnation changed')
            if caught and not source and consumed is None:
                require(previous and pads and previous_pads and ingredients(entity(ids['far']).get('composition')) == [284626]
                        and not holders, 'Source disappeared without exact native far-pot contents')
                prior_chef = next(c for c in previous['chefs'] if c['playerId'] == 3)
                pad = next(p for p in pads if p['player'] == 3)
                oldpad = next(p for p in previous_pads if p['player'] == 3)
                require(prior_chef['heldEntityId'] == caught['source'] and prior_chef['placementTargetId'] in [ids['far'], ids['farHome']]
                        and pad['pickup'] and not oldpad['pickup'], 'Insertion lacks targeted native fresh pickup edge')
                consumed = {'frame': state['gameplayFrame'], 'source': caught['source'], 'pot': ids['far'], 'home': ids['farHome'],
                            'recoveryFramesFromCatch': state['gameplayFrame'] - caught['frame']}
        previous, previous_pads, last = state, pads, state
    require(caught and consumed and consumed['recoveryFramesFromCatch'] <= 120, 'Bounded native catch/insertion incomplete')
    require(set(jobs) == {'FI01-near-pot', 'FI02-catch-position', 'FI03-far-throw', 'FI04-native-catch-placement'}
            and len(jobs) == 4, 'Authored probe did not complete its four exact jobs')
    require(all(c['heldEntityId'] == 0 and c['controlsEnabled'] for c in last['chefs'])
            and last['score'] == 0 and last['delivered'] == 0, 'Unexpected final native chef/score state')
    return {'passed': True, 'classification': 'One native catch/placement mechanism probe; adaptive policy integration and high score are separate',
            'resolvedSceneProperties': ids, 'caught': caught, 'consumed': consumed, 'jobs': jobs,
            'finalFrame': last['gameplayFrame'], 'nativeFarPotComposition': entity(ids['far'])['composition']}


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('trace', type=Path); p.add_argument('plan', type=Path); p.add_argument('result', type=Path)
    p.add_argument('output', type=Path); p.add_argument('--controller-bundle', required=True, type=Path)
    a = p.parse_args(); before = a.trace.stat(); report = check(a.trace)
    result = json.loads(a.result.read_text(encoding='utf-8-sig'))
    plan = json.loads(a.plan.read_text(encoding='utf-8-sig'))
    with gzip.open(a.trace, 'rt', encoding='utf-8-sig') as stream:
        report.update(audit_rows((json.loads(line) for line in stream), plan, result))
    after = a.trace.stat()
    require((before.st_size, before.st_mtime_ns) == (after.st_size, after.st_mtime_ns), 'Trace changed during proof')
    report.update(controller=controller_evidence(a.controller_bundle), artifacts=[pin(x) for x in [a.trace, a.plan, a.result, Path(__file__), Path(__file__).with_name('check_bowl_offmix.py'), Path(__file__).with_name('test_far_pot_interception.py')]])
    a.output.write_text(json.dumps(report, indent=2), encoding='utf-8'); print(json.dumps(report, indent=2))
