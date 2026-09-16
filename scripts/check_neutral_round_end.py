"""File-only audit of neutral native round-end diagnostics; never calls a game.

Sparse requests are retained as requests, not expanded into an invented
per-frame movie. Transition sample resolution and native lifecycle callbacks
are reported separately. This checker cannot qualify a high-score TAS.
"""
import argparse
import gzip
import hashlib
import json
import math
from pathlib import Path


SCORE_FIELDS = ('score', 'baseScore', 'tips', 'deductions', 'delivered')
STATE_FIELDS = ('frame', 'gameplayFrame', 'gameplayFixedFrame', 'levelFrameZero',
                'levelClientTimeZero', 'timer', 'logicalTime', 'clientTime',
                'gameState', 'inLevel', 'serverRoundActive', 'clientRoundActive') + SCORE_FIELDS


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(path):
    with Path(path).open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


def json_file(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def neutral(packets):
    require(isinstance(packets, list) and len(packets) == 4, 'Expected four explicit controller packets')
    require(sorted(p.get('player') for p in packets) == [0, 1, 2, 3], 'Four distinct native player slots required')
    for p in packets:
        require(set(p) == {'player', 'x', 'y', 'pickup', 'use', 'dash'}, 'Unexpected/missing input field')
        require(type(p['player']) is int and p['x'] == 0 and p['y'] == 0 and
                all(p[k] is False for k in ('pickup', 'use', 'dash')), 'Non-neutral input packet')


def validate_ledger(events, initial, final):
    require([e.get('index') for e in events] == list(range(len(events))), 'Native event ledger has gaps or reordered indices')
    require(len(events) == 5, 'Expected five actually recorded order expirations')
    previous = {k: initial[k] for k in SCORE_FIELDS}
    compact = []
    for e in events:
        require(e.get('kind') == 'timeout' and e.get('scoreApplied') is True, 'Unexpected native score event')
        require(e.get('orderRemaining', 1) <= 0 and e.get('orderLifetime', 0) > 0, 'Expiration lacks native expired-order evidence')
        before, after = e.get('beforeScore', {}), e.get('afterScore', {})
        require({k: before.get(k) for k in SCORE_FIELDS} == previous, 'Native score event chain is broken')
        deltas = {'score': 'scoreDelta', 'baseScore': 'baseScoreDelta', 'tips': 'tipDelta',
                  'deductions': 'deductionDelta', 'delivered': 'deliveryDelta'}
        for field, delta in deltas.items():
            require(after[field] - before[field] == e.get(delta), 'Native event delta does not reconcile: ' + field)
        require(e['scoreDelta'] == -30 and e['deductionDelta'] == 30 and
                e['baseScoreDelta'] == e['tipDelta'] == e['deliveryDelta'] == 0, 'Unexpected neutral-run score award/deduction')
        previous = {k: after[k] for k in SCORE_FIELDS}
        compact.append({k: e[k] for k in ('index', 'gameplayFrame', 'frame', 'orderId', 'recipeId', 'recipe',
                                        'baseValue', 'orderRemaining', 'orderLifetime', 'roundElapsed',
                                        'scoreDelta', 'deductionDelta')})
    require(previous == {k: final[k] for k in SCORE_FIELDS}, 'Final native totals differ from complete score ledger')
    require(previous == {'score': -150, 'baseScore': 0, 'tips': 0, 'deductions': 150, 'delivered': 0}, 'Unexpected final totals')
    return compact


def transitions(samples, lifecycle):
    predicates = {
        'timerZero': lambda s: s['timer'] == 0,
        'serverInactive': lambda s: s['serverRoundActive'] is False,
        'clientInactive': lambda s: s['clientRoundActive'] is False,
        'nativeOutro': lambda s: s['gameState'] == 'RunLevelOutro',
        'bothInactive': lambda s: s['serverRoundActive'] is False and s['clientRoundActive'] is False,
    }
    answer = {}
    for name, predicate in predicates.items():
        hit = next((i for i, s in enumerate(samples) if predicate(s)), None)
        require(hit is not None and hit > 0, 'Missing native transition observation: ' + name)
        prior, current = samples[hit - 1], samples[hit]
        require(all(predicate(s) for s in samples[hit:]), 'Native transition unexpectedly reverses: ' + name)
        gap = current['gameplayFrame'] - prior['gameplayFrame']
        answer[name] = {'firstObservedGameplayFrame': current['gameplayFrame'],
                        'previousObservedGameplayFrame': prior['gameplayFrame'],
                        'observationGapFrames': gap, 'exactPerFrameObservation': gap == 1,
                        'nativeClientElapsedSeconds': current['clientTime'] - samples[0]['clientTime']}
    zero = samples[0]['levelFrameZero']
    markers = {}
    for key, source, state in (('serverInactive', 'ServerRound', 'Inactive'),
                               ('clientInactive', 'ClientRound', 'Inactive'),
                               ('serverOutro', 'ServerFlowControllerBase', 'RunLevelOutro')):
        matches = [e for e in lifecycle if e.get('source') == source and e.get('state') == state]
        require(len(matches) == 1, 'Missing/duplicate native lifecycle callback: ' + key)
        e = matches[0]
        markers[key] = {**e, 'gameplayFrame': e['frame'] - zero,
                        'nativeClientElapsedSeconds': e['clientTime'] - samples[0]['clientTime']}
        corresponding = answer['nativeOutro' if key == 'serverOutro' else key]
        require(corresponding['previousObservedGameplayFrame'] < e['frame'] - zero <= corresponding['firstObservedGameplayFrame'],
                'Native lifecycle callback falls outside its observed transition interval')
    require(markers['serverInactive']['gameplayFrame'] == markers['serverOutro']['gameplayFrame'], 'Server stop and outro callbacks disagree')
    require(markers['clientInactive']['gameplayFrame'] >= markers['serverInactive']['gameplayFrame'], 'Client stop precedes server stop')
    return {'observations': answer, 'nativeLifecycleCallbacks': markers}


def validate_trace(trace, route, result, receipt):
    requests = [json.loads(line) for line in Path(route).read_text(encoding='utf-8-sig').splitlines() if line.strip()]
    require(requests and requests[0].get('command') in ('load', 'restart'), 'Route requires explicit native setup')
    require(requests[0] == {'version': 1, 'command': requests[0]['command'], 'seed': 0, 'isolateRecipeRandom': True}, 'Unexpected native setup configuration')
    before_hash = digest(trace)
    samples, events, step_sizes = [], {}, []
    call_count, expected_frame, last_response = 0, 0, None
    command_counts, manifest_hashes = {}, set()
    with gzip.open(trace, 'rt', encoding='utf-8') as source:
        for line in source:
            row = json.loads(line)
            if row.get('kind') == 'header':
                continue
            require(row.get('kind') == 'call', 'Unexpected non-call record in neutral diagnostic')
            require(call_count < len(requests) and row.get('request') == requests[call_count], 'Recorded requests differ from pinned route')
            request = row['request']; command = request['command']; response = row['response']; s = response['state']
            require(response.get('ok') is True and not response.get('error'), 'A native protocol call failed')
            require(command in ('load', 'restart', 'render', 'step', 'inspect'), 'Unexpected diagnostic command')
            require(command not in ('load', 'restart') or call_count == 0, 'Round reset inside the diagnostic body')
            if command == 'step':
                require(set(request) == {'version', 'command', 'steps', 'inputs'}, 'Unexpected step field')
                neutral(request['inputs']); steps = request['steps']
                require(type(steps) is int and 1 <= steps <= 600, 'Invalid native step count')
                expected_frame += steps; step_sizes.append(steps)
            elif command == 'render':
                require(expected_frame == 0 and request == {'version': 1, 'command': 'render', 'width': 1280, 'height': 720, 'renderRate': 0}, 'Render configuration changed after gameplay advanced')
            neutral(response['inputs'])
            require(s.get('gameplayFrame') == expected_frame and s['frame'] - s['levelFrameZero'] == expected_frame, 'Native frame advancement differs from requested steps')
            require(s['scene'] == 's_Day_3_4', 'Wrong native scene')
            session = json.loads(response['session']) if isinstance(response['session'], str) else response['session']
            require(session.get('stage') == 'kitchen_ready' and not session.get('busy') and not session.get('error'), 'Native session is not ready')
            require(all(session.get(k) == v for k, v in {'dlc': 8, 'variantPlayers': 4, 'serverUsers': 4, 'clientUsers': 4, 'virtualPads': 4}.items()), 'Native session is not four-player DLC8')
            require(sorted(c.get('playerId') for c in s['chefs']) == [0, 1, 2, 3] and len({c.get('entityId') for c in s['chefs']}) == 4, 'Missing distinct native chef identities')
            duration = s['roundDuration']
            require(all(duration.get(k) is True for k in ('available', 'loadedRoundDataAvailable', 'timerAvailable', 'valuesAgree')) and
                    all(duration.get(k) == 270 for k in ('seconds', 'loadedRoundDataSeconds', 'timerLimitSeconds')) and
                    duration.get('levelConfigName') == 'Day_3_4_4P' and not duration.get('error'), 'Loaded native round/timer duration is not verified270seconds')
            instrumentation = s['instrumentation']; manifest = instrumentation['manifest']
            require(not instrumentation.get('error') and instrumentation.get('logicalClockActive') is True and
                    instrumentation.get('nativePhysicsAutoSimulation') is True, 'Native clock/physics instrumentation unavailable')
            require(manifest['pluginSha256'].lower() == receipt['pluginSha256'].lower() and
                    manifest['executableSha256'].lower() == receipt['imageSha256'].lower(), 'Observed native binary hashes differ from process receipt')
            require(s['captureFramerate'] == 60 and abs(s['fixedDeltaTime'] - .02) < 1e-8 and not s['timerSuppressed'], 'Clock cadence or native timer suppression changed')
            manifest_hashes.add(instrumentation['manifestSha256'])
            require(s.get('gameEventsInstalled') is True and s.get('gameEventsDropped') == 0 and not s.get('gameEventsError'), 'Native score ledger is incomplete')
            for e in s['gameEvents']:
                index = e['index']; require(index not in events or events[index] == e, 'Previously recorded native event changed')
                events[index] = e
            sample = {k: s[k] for k in STATE_FIELDS}
            require(all(isinstance(sample[k], (int, float)) and math.isfinite(sample[k]) for k in ('timer', 'clientTime', 'logicalTime')), 'Invalid native clock value')
            require(s['baseScore'] == s['tips'] == s['delivered'] == 0 and s['score'] == -s['deductions'], 'Unexpected neutral score or delivery')
            if samples:
                previous = samples[-1]
                require(s['timer'] <= previous['timer'] and s['clientTime'] >= previous['clientTime'], 'Native timer/clock moved backwards')
                require(s['levelFrameZero'] == samples[0]['levelFrameZero'], 'Level frame origin changed')
                require(abs((s['clientTime'] - samples[0]['clientTime']) - expected_frame / 60) < .001, 'Native clock elapsed time does not track requested logical frames')
            samples.append(sample); last_response = response; call_count += 1
            command_counts[command] = command_counts.get(command, 0) + 1
    require(call_count == len(requests), 'Trace does not contain every pinned request')
    require(digest(trace) == before_hash, 'Trace changed while being checked')
    require(last_response == json_file(result), 'Result artifact differs from final recorded response')
    initial, final = samples[0], samples[-1]
    require(initial['gameplayFrame'] == 0 and initial['gameState'] == 'InLevel' and
            initial['serverRoundActive'] is True and initial['clientRoundActive'] is True and 269.9 < initial['timer'] <= 270,
            'No actual running-round initial state')
    require(final['timer'] == 0 and final['serverRoundActive'] is False and final['clientRoundActive'] is False and final['gameState'] == 'RunLevelOutro',
            'No actual native round termination/outro observed')
    require(len(manifest_hashes) == 1, 'Instrumentation manifest changed during the diagnostic')
    ledger = validate_ledger([events[i] for i in sorted(events)], initial, final)
    transition = transitions(samples, last_response['state']['lifecycle'])
    at_16200 = next(s for s in samples if s['gameplayFrame'] == 16200)
    require(at_16200['timer'] > 0 and at_16200['serverRoundActive'] is True and at_16200['clientRoundActive'] is True,
            'Expected pre-stop16200 observation is absent')
    return {'trace': str(trace), 'traceSha256': before_hash, 'route': str(route), 'routeSha256': digest(route),
            'result': str(result), 'resultSha256': digest(result), 'callCount': call_count, 'commands': command_counts,
            'logicalFramesRequested': sum(step_sizes), 'largestStep': max(step_sizes),
            'stepHistogram': {str(n): step_sizes.count(n) for n in sorted(set(step_sizes))},
            'initial': initial, 'at16200': at_16200, 'final': final, 'transitions': transition,
            'nativeClockElapsedToFinal': final['clientTime'] - initial['clientTime'],
            'nativeLedger': ledger, 'manifestSha256': next(iter(manifest_hashes)),
            'neutralInputEvidence': 'Every pinned step request and every recorded response contains four explicit zero-axis/released-button packets. Sparse native frames are not individually observed.',
            'timeoutInputsNeutralField': 'Not applicable: this observer field is populated only for input_release records; timeout false values are unset defaults.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=Path('artifacts/native-round-end-proof.json'))
    args = parser.parse_args()
    receipt_path = Path('artifacts/native-round-end-a-process-identity.json'); receipt = json_file(receipt_path)
    runs = [validate_trace(Path(f'artifacts/native-round-end-{letter}.jsonl.gz'),
                           Path('routes/probes/' + route), Path(f'artifacts/native-round-end-{letter}-result.json'), receipt)
            for letter, route in (('a', 'native-round-end.jsonl'), ('b', 'native-round-end-fine.jsonl'))]
    exact = runs[1]['transitions']['observations']
    require(all(v['exactPerFrameObservation'] for v in exact.values()), 'Fine trace does not isolate every requested transition')
    require({k: v['gameplayFrame'] for k, v in runs[0]['transitions']['nativeLifecycleCallbacks'].items()} ==
            {k: v['gameplayFrame'] for k, v in runs[1]['transitions']['nativeLifecycleCallbacks'].items()}, 'Two native lifecycle transition frames differ')
    budget = 16320; stop = exact['bothInactive']['firstObservedGameplayFrame']
    require(stop < budget, 'Observed complete native stop exceeds the configured bot budget')
    report = {'ok': True, 'classification': 'Neutral native270-second round-end diagnostics with sparse prefixes and a per-frame transition tail; not a high-score TAS or probe20 qualification',
              'processReceipt': receipt, 'processReceiptSha256': digest(receipt_path),
              'receiptScope': 'The supplied OS identity receipt identifies the primary process; both trace manifests match its game/plugin hashes. This audit does not independently establish a fresh process for runB.',
              'runs': runs, 'botFrameBudget': budget, 'observedStopFrame': stop, 'remainingBudgetFrames': budget - stop,
              'budgetScope': 'Covers the observed native timer/server/client stop and entry into RunLevelOutro. It does not prove the whole outro animation or results UI has finished by this frame.',
              'pinnedFiles': {p: digest(p) for p in ('scripts/check_neutral_round_end.py', 'plugin/GameEvents.cs',
                    'artifacts/planner-candidate-v12-b/OvercookedTAS.Controller.dll', 'artifacts/native-round-end-a.png', 'artifacts/native-round-end-a-screenshot.json')}}
    require(report['pinnedFiles']['artifacts/planner-candidate-v12-b/OvercookedTAS.Controller.dll'] == receipt['controllerSha256'].lower(), 'Frozen controller hash differs from receipt')
    args.output.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'ok': True, 'report': str(args.output), 'serverStop': exact['serverInactive']['firstObservedGameplayFrame'],
                      'clientStop': stop, 'score': runs[1]['final']['score'], 'timeoutCount': len(runs[1]['nativeLedger']),
                      'frameBudgetMargin': budget - stop}))


if __name__ == '__main__':
    main()
