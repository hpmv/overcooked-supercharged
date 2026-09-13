"""Independent, streaming proof of the ordinary-input plated hotdog ordering probe.

Registration receipts describe actual native network registration, not Unity creation.
The result must equal the final *full* recorded response, including unrelated fields.
"""
import argparse
import gzip
import hashlib
import json
from pathlib import Path

from check_bowl_offmix import controller_evidence, ingredients, nodes, ordinary_request, pin, require
from check_far_pot_interception import neutral, registration

BUN, SAUSAGE, MUSTARD, ONION = 262914, 284626, 17094, 461162
STAGES = [[], [BUN], sorted([BUN, SAUSAGE]), sorted([BUN, SAUSAGE, MUSTARD]), sorted([BUN, SAUSAGE, MUSTARD, ONION])]
JOBS = ['PH01-sausage', 'PH02-onion', 'PH03-bun', 'PH04-heat-onion', 'PH05-plated-native-assembly']
TYPES = [['take', 'navigate', 'throw'], ['take', 'place', 'chop'], ['take', 'place', 'chop'], ['take', 'place'],
         ['take', 'assemble', 'navigate', 'cook', 'combine', 'apply', 'navigate', 'cook', 'combine', 'place']]


def entity_map(state):
    result = {e['id']: e for e in state['entities'] if e.get('active')}
    require(len(result) == sum(bool(e.get('active')) for e in state['entities']), 'Duplicate active native entity ID')
    return result


def station(entities, component, x, z):
    found = [e for e in entities.values() if component in e['components'] and abs(e['position']['x']-x) < .05 and abs(e['position']['z']-z) < .05]
    require(len(found) == 1, 'Native topology selector is not unique: '+component)
    return found[0]


def cooked(node, ingredient, step):
    return any(n.get('type') == 'CookedCompositeAssembledNode' and n.get('state') == 'Cooked'
               and n.get('cookingStepId') == step and n.get('progress', -1) >= 1
               and ingredients(n) == [ingredient] for n in nodes(node))


class Checker:
    def __init__(self, plan):
        require([j['id'] for j in plan['jobs']] == JOBS and [j['player'] for j in plan['jobs']] == [2, 2, 2, 0, 0]
                and [[a['type'] for a in j['actions']] for j in plan['jobs']] == TYPES
                and [j['dependencies'] for j in plan['jobs']] == [[], [JOBS[0]], [JOBS[1]], [JOBS[1]], [JOBS[2], JOBS[3]]]
                and 0 < plan['timeoutFrames'] <= 2400, 'Wrong bounded authored mechanism plan')
        self.plan = plan; self.jobs = {j['id']: j for j in plan['jobs']}
        self.active = {}; self.counts = {}; self.completed = []; self.actions = []
        self.initial = self.last = self.last_response = self.last_request = None
        self.es = {}; self.pads = {}; self.original = {}; self.sources = {}; self.chopped = {}
        self.cooking = {}; self.transitions = []; self.pickups = []; self.plate_stage = 0
        self.plate_pickup = self.plate_under = self.recovered = self.staged = None
        self.calls = self.steps = self.headers = 0; self.input_hash = hashlib.sha256()

    def identity(self, state, entity):
        return {'entityId': entity['id'], 'observedOrdinal': entity['observedOrdinal'], 'registration': registration(state, entity['id'])}

    def initialise(self, state, entities):
        self.initial = state
        plate_counter = station(entities, 'AttachStation', 19.2, -15.6)
        plate = entities.get(plate_counter['attachedEntityId'])
        require(plate and 'Plate' in plate['components'] and not ingredients(plate['composition']), 'Initial native plate is not empty on its original counter')
        mapping = {'plate': plate, 'plateCounter': plate_counter,
                   'bunBoard': station(entities, 'Workstation', 15.6, -14.4),
                   'onionBoard': station(entities, 'Workstation', 15.6, -12),
                   'output': station(entities, 'AttachStation', 25.2, -20.4),
                   'potHome': station(entities, 'CookingStation', 16.8, -10.8),
                   'panHome': station(entities, 'CookingStation', 16.8, -21.6),
                   'dispenser': station(entities, 'ServerPlacementItemSpawner', 20.4, -10.8)}
        for vessel, home, ingredient, step in [('pot', 'potHome', SAUSAGE, 20068), ('pan', 'panHome', ONION, 20294)]:
            item = entities.get(mapping[home]['attachedEntityId'])
            require(item and item['cookingTime'] == 12 and item['cookingTypeId'] == step and not ingredients(item['composition']), 'Original vessel is not empty with the native twelve-second step')
            mapping[vessel] = item
            self.cooking[vessel] = {'ingredient': ingredient, 'step': step, 'loaded': None, 'cooked': None, 'consumed': None, 'maxProgress': 0, 'samples': 0}
        require(not mapping['output']['attachedEntityId'] and not mapping['bunBoard']['attachedEntityId'] and not mapping['onionBoard']['attachedEntityId'], 'Initial destination/boards are occupied')
        self.mapping = {k: e['id'] for k, e in mapping.items()}
        self.original = {k: dict(self.identity(state, e), position=e['position']) for k, e in mapping.items()}
        self.plugin = (state['instrumentation']['manifest']['pluginSha256'], state['instrumentation']['manifestSha256'])

    def edge(self, previous, current, player, targets):
        before = next(c for c in previous['chefs'] if c['playerId'] == player)
        require(current[player]['pickup'] and not self.pads[player]['pickup']
                and (before['placementTargetId'] in targets or before['pickupTargetId'] in targets), 'Native transfer lacks fresh pickup edge and original target referral')

    def event(self, row):
        name, value = row['name'], row.get('value') or {}
        require(self.last is not None and name not in ['actionFailure', 'actionFailed', 'planFailure', 'plannerFailure', 'jobFailed'], 'Failed probe/event before native frame zero')
        if name == 'jobStart':
            job = self.jobs.get(value['id'])
            require(job and value['player'] == job['player'] and value['resources'] == job['resources']
                    and value['id'] not in self.counts and job['player'] not in self.active
                    and all(d in self.completed for d in job['dependencies']), 'Job does not match authored dependencies/ownership')
            self.active[job['player']] = job['id']; self.counts[job['id']] = 0
        elif name == 'actionComplete':
            action = value['action']; player = action['player']; job_id = self.active.get(player)
            require(job_id is not None and self.counts[job_id] < len(self.jobs[job_id]['actions']), 'Unauthored action completion')
            expected = dict(self.jobs[job_id]['actions'][self.counts[job_id]]); expected['player'] = player; expected.setdefault('timeoutFrames', 600)
            require(action == expected, 'Completed action differs from authored ordinary action')
            self.counts[job_id] += 1; self.actions.append({'job': job_id, 'gameplayFrame': self.last['gameplayFrame'], 'action': action})
        elif name == 'jobComplete':
            job = self.jobs.get(value['id'])
            require(job and self.active.get(job['player']) == job['id'] and self.counts[job['id']] == len(job['actions']), 'Job completed without every authored action')
            self.completed.append(job['id']); del self.active[job['player']]

    def row(self, row):
        if row.get('kind') == 'header':
            require(self.headers == self.calls == 0 and row.get('format') == 'overcooked-tas-trace' and row.get('version') == 1 and row.get('mode') == 'plan', 'Unexpected header')
            self.headers += 1; return
        if row.get('kind') == 'event': self.event(row); return
        require(row.get('kind') == 'call', 'Unknown trace record kind')
        request, response = row['request'], row['response']; ordinary_request(request); self.calls += 1
        allowed = {'restart': {'version', 'command', 'seed', 'isolateRecipeRandom'}, 'inspect': {'version', 'command'}, 'step': {'version', 'command', 'steps', 'inputs'}}
        require(set(request) <= allowed[request['command']] and response.get('ok') is True and response.get('paused') is True, 'Unexpected request fields or failed/unpaused response')
        require(request['command'] != 'restart' or self.calls == 1 and request.get('seed') == 0 and request.get('isolateRecipeRandom') is True, 'Unexpected restart or RNG configuration')
        state = response['state']; frame = state['gameplayFrame']; session = json.loads(response['session'])
        require(session['variantPlayers'] == session['virtualPads'] == session['serverUsers'] == session['clientUsers'] == 4 and session['dlc'] == 8 and session['stage'] == 'kitchen_ready', 'Wrong native four-player DLC8 session')
        require(state['scene'] == 's_Day_3_4' and state['roundDuration']['valuesAgree'] and state['roundDuration']['seconds'] == 270 and state['roundDuration']['levelConfigName'] == 'Day_3_4_4P', 'Wrong native scene or duration')
        require(state['captureFramerate'] == 60 and state['fixedDeltaTime'] == .02 and not state['timerSuppressed'] and state['serverRoundActive'] and state['clientRoundActive'], 'Native timing configuration changed')
        require(all(state[k] == 0 for k in ['score', 'baseScore', 'tips', 'deductions', 'delivered']), 'Unexpected native score ledger')
        require(not state['instrumentation']['error'] and state['gameEventsInstalled'] and not state['gameEventsDropped'] and not state['gameEventsError'], 'Native observation error/drop')
        chefs = {c['playerId']: c for c in state['chefs']}; pads = {p['player']: p for p in response['inputs']}
        require(len(state['chefs']) == len(response['inputs']) == 4 and set(chefs) == set(pads) == {0, 1, 2, 3}, 'Incomplete native player slots')
        require(all(neutral(pads[p]) for p in [1, 3]) and not any(p['dash'] for p in pads.values()), 'Unauthored chef/dash input')
        if request['command'] == 'step':
            self.steps += 1; require(self.steps <= self.plan['timeoutFrames'] + 1, 'Probe exceeded its authored bound')
            for pad in request['inputs']:
                require(set(pad) == {'player', 'x', 'y', 'pickup', 'use', 'dash'} and all(pad[k] == pads[pad['player']][k] for k in ['pickup', 'use', 'dash'])
                        and all(abs(pad[k]-pads[pad['player']][k]) <= 1e-6 for k in ['x', 'y']), 'Observed native inputs differ from ordinary request')
            self.input_hash.update((json.dumps(request, sort_keys=True, separators=(',', ':'), allow_nan=False)+'\n').encode())
        entities = entity_map(state)
        if self.initial is None:
            require(self.headers == 1 and request['command'] == 'restart' and frame == 0 and all(neutral(p) for p in pads.values()), 'Missing neutral fresh frame zero')
            self.initialise(state, entities)
        elif request['command'] == 'step':
            require(frame == self.last['gameplayFrame'] + 1 and state['frame'] == self.last['frame'] + 1
                    and state['timer'] < self.last['timer'] and state['clientTime'] > self.last['clientTime'] and state['logicalTime'] > self.last['logicalTime']
                    and state['fixedFrame']-self.last['fixedFrame'] == state['physicsStepsThisFrame'] in [0, 1], 'Native per-frame clocks are not contiguous/monotonic')
        else:
            require(frame == self.last['gameplayFrame'] and state['timer'] == self.last['timer'], 'Inspect advanced gameplay')
        require((state['instrumentation']['manifest']['pluginSha256'], state['instrumentation']['manifestSha256']) == self.plugin, 'Native instrumentation identity changed')
        for role, original in self.original.items():
            e = entities.get(original['entityId'])
            require(e and self.identity(state, e) == {k: original[k] for k in ['entityId', 'observedOrdinal', 'registration']}, 'Original '+role+' native incarnation changed')
            if role != 'plate': require(e['position'] == original['position'], 'Original '+role+' moved')
        mapping = self.mapping; plate = entities[mapping['plate']]
        require(not any(n.get('state') in ['Burnt', 'Ruined', 'OverMixed'] for e in entities.values() for n in nodes(e.get('composition'))), 'Active native food was ruined')
        for vessel, home in [('pot', 'potHome'), ('pan', 'panHome')]:
            v = entities[mapping[vessel]]; h = entities[mapping[home]]; cooking = self.cooking[vessel]
            require(h['attachedEntityId'] == v['id'] and sum(e.get('attachedEntityId') == v['id'] for e in entities.values()) == 1
                    and not any(c['heldEntityId'] == v['id'] for c in chefs.values()) and v['cookingTime'] == 12 and v['cookingTypeId'] == cooking['step'], 'Original native vessel left its unchanged twelve-second home')
            food = ingredients(v['composition']); require(food in [[], [cooking['ingredient']]], 'Vessel contains unrelated or duplicate food')
            if food:
                require(cooking['consumed'] is None and 0 <= v['cookingProgress'] < 24, 'Native vessel reloaded/ruined')
                cooking['maxProgress'] = max(cooking['maxProgress'], v['cookingProgress']); cooking['samples'] += 1
                if cooking['loaded'] is None:
                    require(self.last is not None, 'Vessel food appeared in initial setup')
                    if vessel == 'pot':
                        source = self.sources.get('Frankfurter'); require(source and source.get('flight'), 'Sausage lacks original native throw provenance')
                        require(source['entityId'] not in entities, 'Original thrown sausage remained active after vessel consumption')
                    else:
                        source = self.chopped.get('onion'); oldchef = next(c for c in self.last['chefs'] if c['playerId'] == 0)
                        require(source and oldchef['heldEntityId'] == source['entityId'] and chefs[0]['heldEntityId'] == 0 and source['entityId'] not in entities, 'Pan loading lacks same chopped source transfer')
                        self.edge(self.last, pads, 0, [v['id'], h['id']])
                    cooking['loaded'] = {'frame': frame, 'clientTime': state['clientTime'], 'progress': v['cookingProgress'], 'source': source}
                if self.last and ingredients(self.es[v['id']]['composition']):
                    delta = v['cookingProgress']-self.es[v['id']]['cookingProgress']
                    require(-1e-6 <= delta <= .018 and abs(delta-state['clientDeltaTime']) < .0001, 'Native cooking progress did not follow its original clock')
                if cooked(v['composition'], cooking['ingredient'], cooking['step']) and cooking['cooked'] is None:
                    elapsed = state['clientTime']-cooking['loaded']['clientTime']
                    require(v['cookingProgress'] >= 12 and 11.98 <= elapsed <= 12.04, 'Cooked state lacks a native twelve-second interval')
                    cooking['cooked'] = {'frame': frame, 'clientTime': state['clientTime'], 'progress': v['cookingProgress'], 'elapsedFromInsertion': elapsed}
            elif cooking['loaded'] and cooking['consumed'] is None:
                require(cooking['cooked'] and self.last and cooked(self.es[v['id']]['composition'], cooking['ingredient'], cooking['step'])
                        and chefs[0]['heldEntityId'] == plate['id'] and cooking['ingredient'] in ingredients(plate['composition']), 'Vessel emptied without native cooked transfer to original held plate')
                self.edge(self.last, pads, 0, [v['id'], h['id']]); cooking['consumed'] = {'frame': frame, 'previousProgress': self.es[v['id']]['cookingProgress']}

        if self.last:
            oldchefs = {c['playerId']: c for c in self.last['chefs']}
            if self.plate_pickup is None and chefs[0]['heldEntityId'] == plate['id']:
                require(oldchefs[0]['heldEntityId'] == 0 and self.es[mapping['plateCounter']]['attachedEntityId'] == plate['id']
                        and entities[mapping['plateCounter']]['attachedEntityId'] == 0 and not ingredients(plate['composition']), 'Initial plate pickup lacks exact original empty-plate/counter transition')
                self.edge(self.last, pads, 0, [mapping['plateCounter'], plate['id']]); self.plate_pickup = frame
            held = entities.get(chefs[2]['heldEntityId']); crate = self.es.get(oldchefs[2]['pickupTargetId'])
            if not oldchefs[2]['heldEntityId'] and held and crate and crate.get('spawnPrefab') in ['Frankfurter', 'DLC08_Onion', 'HotdogBun']:
                name = crate['spawnPrefab']; require(name not in self.sources, 'Duplicate authored crate source')
                self.edge(self.last, pads, 2, [crate['id']])
                identity = self.identity(state, held); require(identity['registration']['gameplayFrame'] == frame, 'Source lacks same-frame native crate registration')
                require(held['name'] == name, 'Registered raw ingredient differs from its native crate prefab')
                self.sources[name] = dict(identity, pickupFrame=frame, crateId=crate['id'], name=held['name'], composition=held['composition'])
                self.pickups.append({'frame': frame, 'player': 2, 'source': identity, 'crateId': crate['id']})
            for name, source in self.sources.items():
                e = entities.get(source['entityId'])
                if e:
                    require(self.identity(state, e) == {k: source[k] for k in ['entityId', 'observedOrdinal', 'registration']}, 'Raw source native incarnation changed')
                    require(e['composition'] == source['composition'], 'Raw source composition changed before native preparation/consumption')
                    if name == 'Frankfurter' and e.get('throwFlying') and not source.get('flight'):
                        require(not pads[2]['use'] and self.pads[2]['use'] and e['throwerEntityId'] == chefs[2]['entityId'], 'Native sausage flight lacks ordinary release edge/thrower')
                        source['flight'] = {'frame': frame, 'throwerEntityId': e['throwerEntityId']}
            for kind, raw_name, ingredient in [('onion', 'DLC08_Onion', ONION), ('bun', 'HotdogBun', BUN)]:
                board = entities[mapping[kind+'Board']]; oldboard = self.es[board['id']]; raw = self.sources.get(raw_name)
                if raw and oldboard['attachedEntityId'] == raw['entityId'] and board['attachedEntityId'] != raw['entityId']:
                    replacement = entities.get(board['attachedEntityId'])
                    require(kind not in self.chopped and replacement and ingredients(replacement['composition']) == [ingredient]
                            and raw['entityId'] not in entities and pads[2]['use'] and self.es[raw['entityId']]['workProgress'] > .8, 'Chop lacks exact raw-to-prepared native replacement')
                    identity = self.identity(state, replacement); require(identity['registration']['gameplayFrame'] == frame, 'Chopped source lacks same-frame native registration')
                    self.chopped[kind] = dict(identity, frame=frame, rawSource=raw['entityId'], ingredientId=ingredient, boardId=board['id'], composition=replacement['composition'])
            for source in list(self.sources.values()) + list(self.chopped.values()):
                e = entities.get(source['entityId'])
                if e:
                    require(self.identity(state, e) == {k: source[k] for k in ['entityId', 'observedOrdinal', 'registration']}
                            and e['composition'] == source['composition'], 'Prepared/raw source incarnation or composition changed before native merge')
                elif source.get('inactiveFrame') is None:
                    source['inactiveFrame'] = frame
                removed = [event for event in state['entityRegistration']['events'] if event['kind'] == 'remove'
                           and (event.get('entity') or {}).get('entityId') == source['entityId']
                           and event['entity']['observedRegistrationSequence'] == source['registration']['sequence']]
                if removed and source.get('removal') is None:
                    require(source.get('inactiveFrame') is not None and removed[0]['gameplayFrame'] >= source['inactiveFrame'], 'Native source removal precedes its observed preparation/consumption')
                    source['removal'] = removed[0]
            oldplate = self.es[plate['id']]; before = ingredients(oldplate['composition']); after = ingredients(plate['composition'])
            if after != before:
                require(self.plate_stage < 4 and before == STAGES[self.plate_stage] and after == STAGES[self.plate_stage+1], 'Native plate ingredient sequence/quantity changed')
                self.plate_stage += 1
                require(oldchefs[0]['heldEntityId'] == plate['id'], 'Preparation did not use the original held plate')
                if self.plate_stage == 1:
                    bun = self.chopped.get('bun'); board = entities[mapping['bunBoard']]
                    require(bun and self.es[board['id']]['attachedEntityId'] == bun['entityId'] and bun['entityId'] not in entities
                            and board['attachedEntityId'] == plate['id'] and chefs[0]['heldEntityId'] == 0, 'Native plate-under-bun identity/attachment proof is missing')
                    self.edge(self.last, pads, 0, [board['id']]); self.plate_under = frame
                elif self.plate_stage == 3:
                    require(chefs[0]['heldEntityId'] == plate['id'] and entities[mapping['dispenser']]['switchIndex'] == 0, 'Mustard lacks native correct dispenser/held plate')
                    self.edge(self.last, pads, 0, [mapping['dispenser']])
                else:
                    require(chefs[0]['heldEntityId'] == plate['id'], 'Cooked transfer did not retain the original plate')
                self.transitions.append({'frame': frame, 'ingredients': after, 'nativeComposition': plate['composition']})
            else:
                require(plate['composition'] == oldplate['composition'], 'Prepared plate composition drifted without ingredient transfer')
            if self.plate_under and self.recovered is None and chefs[0]['heldEntityId'] == plate['id']:
                require(self.es[mapping['bunBoard']]['attachedEntityId'] == plate['id'] and not entities[mapping['bunBoard']]['attachedEntityId'], 'Original plate recovery lacks native board transition')
                self.edge(self.last, pads, 0, [mapping['bunBoard'], plate['id']]); self.recovered = frame
            if self.recovered and self.staged is None and chefs[0]['heldEntityId'] != plate['id']:
                require(self.plate_stage == 4 and chefs[0]['heldEntityId'] == 0 and entities[mapping['output']]['attachedEntityId'] == plate['id'], 'Plate was lost or put down before exact final meal')
                self.edge(self.last, pads, 0, [mapping['output']]); self.staged = frame
        require(ingredients(plate['composition']) == STAGES[self.plate_stage], 'Unexpected plate ingredients')
        if self.plate_stage >= 2: require(cooked(plate['composition'], SAUSAGE, 20068), 'Plated sausage lacks native Boiled/Cooked preparation')
        if self.plate_stage >= 4: require(cooked(plate['composition'], ONION, 20294), 'Plated onion lacks native Fried/Cooked preparation')
        if self.staged:
            require(entities[mapping['output']]['attachedEntityId'] == plate['id'] and not any(c['heldEntityId'] == plate['id'] for c in chefs.values()), 'Final original meal left its verified output')
        self.last, self.es, self.pads, self.last_response, self.last_request = state, entities, pads, response, request

    def finish(self, result):
        require(result == self.last_response, 'Completed result differs from final full native trace response')
        require(set(self.completed) == set(JOBS) and len(self.completed) == 5 and not self.active and len(self.actions) == 21, 'Not every authored job/action completed')
        require(self.plate_stage == 4 and self.plate_pickup < self.plate_under < self.recovered < self.transitions[1]['frame'] < self.transitions[2]['frame'] < self.transitions[3]['frame'] < self.staged < self.last['gameplayFrame'], 'Native preparation order/completion is incomplete')
        require(all(neutral(p) for p in self.pads.values()) and all(neutral(p) for p in self.last_request['inputs'])
                and all(c['heldEntityId'] == 0 and c['controlsEnabled'] for c in self.last['chefs']), 'Final native requested/observed inputs and chefs are not neutral/controlled/empty')
        require(all(c['loaded'] and c['cooked'] and c['consumed'] for c in self.cooking.values()), 'Native heating/consumption evidence incomplete')
        require(len(self.sources) == 3 and len(self.chopped) == 2 and all(s.get('removal') for s in list(self.sources.values())+list(self.chopped.values())), 'Original ingredient registration-to-native-removal chain is incomplete')
        require(self.steps == self.last['gameplayFrame'] and self.calls == self.steps+2, 'Unexpected missing/extra gameplay calls')
        return {'format': 'oc2-native-plated-sauce-before-onion-proof', 'version': 1, 'passed': True,
                'qualification': 'One native ordinary-input preparation-order mechanism; no delivery, score, full-round or replay qualification.',
                'mapping': self.mapping, 'originalIdentities': self.original, 'rawSourceRegistrations': self.sources, 'choppedReplacements': self.chopped,
                'originalEmptyPlatePickupFrame': self.plate_pickup, 'plateUnderBunFrame': self.plate_under, 'samePlateRecoveryFrame': self.recovered, 'transitions': self.transitions,
                'vessels': self.cooking, 'samePlateFinalOutputFrame': self.staged, 'finalGameplayFrame': self.last['gameplayFrame'],
                'calls': self.calls, 'ordinaryUnitSteps': self.steps, 'canonicalStepRequestsSha256': self.input_hash.hexdigest(),
                'clientElapsedSeconds': self.last['clientTime']-self.initial['clientTime'], 'timerElapsedSeconds': self.initial['timer']-self.last['timer'],
                'originalVesselsNeverMovedOrHeld': True, 'completedJobs': self.completed, 'completedActions': self.actions,
                'nativePluginSha256': self.plugin[0], 'nativeInstrumentationManifestSha256': self.plugin[1],
                'exactFullResultBinding': True, 'finalNeutralInputs': True, 'score': 0, 'deliveries': 0}


def check(rows, plan, result):
    checker = Checker(plan)
    for row in rows: checker.row(row)
    return checker.finish(result)


def main():
    parser = argparse.ArgumentParser(); parser.add_argument('trace', type=Path); parser.add_argument('plan', type=Path)
    parser.add_argument('result', type=Path); parser.add_argument('output', type=Path); parser.add_argument('--controller-bundle', required=True, type=Path)
    parser.add_argument('--stderr', type=Path)
    args = parser.parse_args(); initial_stat = args.trace.stat()
    with gzip.open(args.trace, 'rt', encoding='utf-8') as stream:
        proof = check((json.loads(line) for line in stream), json.loads(args.plan.read_text(encoding='utf-8-sig')), json.loads(args.result.read_text(encoding='utf-8-sig')))
    require((initial_stat.st_size, initial_stat.st_mtime_ns) == (args.trace.stat().st_size, args.trace.stat().st_mtime_ns), 'Trace changed during proof')
    proof['files'] = {k: pin(v) for k, v in {'trace': args.trace, 'plan': args.plan, 'result': args.result, 'checker': Path(__file__),
                                           'tests': Path(__file__).with_name('test_plated_sauce_before_onion.py'),
                                           'schemaHelper': Path(__file__).with_name('check_bowl_offmix.py'),
                                           'registrationHelper': Path(__file__).with_name('check_far_pot_interception.py')}.items()}
    proof['controller'] = controller_evidence(args.controller_bundle)
    if args.stderr: proof['files']['stderr'] = pin(args.stderr)
    args.output.write_text(json.dumps(proof, indent=2)+'\n', encoding='utf-8')
    print(json.dumps({'passed': True, 'output': str(args.output), 'steps': proof['ordinaryUnitSteps'], 'plateTransitions': [v['frame'] for v in proof['transitions']], 'score': 0}))


if __name__ == '__main__': main()
