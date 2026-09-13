"""Read-only evidence check for the captured V9 legal dirty-stack continuation."""
import argparse
import gzip
import hashlib
import json
import math
from pathlib import Path


def require(value, message):
    if not value:
        raise ValueError(message)


def pin(path):
    path = Path(path).resolve()
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return {'path': str(path), 'bytes': path.stat().st_size, 'sha256': digest.hexdigest()}


def entity(state, identity):
    return next(e for e in state['entities'] if e['id'] == identity and e['active'])


def chef(state, player):
    return next(c for c in state['chefs'] if c['playerId'] == player)


def analyze(rows, result, before, plan):
    calls = [row for row in rows if row.get('kind') == 'call']
    states = [row['response']['state'] for row in calls]
    require(len(calls) >= 3, 'Probe has insufficient native calls')
    first, final = states[0], states[-1]
    before = before.get('state', before)
    require(calls[0]['request']['command'] == 'inspect', 'Continuation must begin with read-only inspection')
    require(first['gameplayFrame'] == before['gameplayFrame'] == 9038, 'Wrong paused native continuation frame')
    require(first['frame'] == before['frame'] and first['timer'] == before['timer'], 'Initial native clocks differ from captured failure')
    station, stack = 46, chef(first, 0)['heldEntityId']
    require(stack == 314 and entity(first, stack)['plateStackKind'] == 'dirty', 'Expected original held dirty stack is absent')
    require(entity(first, stack)['plateCount'] == 2 and entity(first, station)['attachedEntityId'] == 0, 'Initial stack count or empty target differs')
    ordinal = entity(first, stack)['observedOrdinal']
    target = plan['jobs'][0]['actions'][0]['target']
    button_samples, native_transfers = [], []
    previous = first
    for index, (row, state) in enumerate(zip(calls, states)):
        request = row['request']
        require(row['response']['ok'], 'Native request failed')
        require(state['score'] == first['score'] and state['delivered'] == first['delivered'], 'Unrelated delivery/score change occurred')
        require(len(state['chefs']) == 4 and {c['playerId'] for c in state['chefs']} == set(range(4)), 'Four native chef slots are not present')
        require(all(c['controlsEnabled'] and not c['respawning'] for c in state['chefs']), 'Native chef control gate changed during the probe')
        require(entity(state, stack)['observedOrdinal'] == ordinal and entity(state, stack)['plateCount'] == 2, 'Original native stack identity/count changed')
        if index:
            require(request['command'] == 'step' and request['steps'] == 1, 'Only one-frame ordinary inputs may advance this continuation')
            require(state['gameplayFrame'] == previous['gameplayFrame'] + 1 and state['frame'] == previous['frame'] + 1, 'Native frame progression is discontinuous')
            require(state['timer'] < previous['timer'], 'Elapsed native gameplay time was not preserved')
            inputs = request.get('inputs', [])
            require(len(inputs) == 4 and {i['player'] for i in inputs} == set(range(4)), 'Incomplete logical input frame')
            for item in inputs:
                player = item['player']
                require(all(math.isfinite(item[a]) and abs(item[a]) <= 1 for a in ('x', 'y')), 'Illegal movement axes')
                require(not item['use'] and not item['dash'], 'This bounded probe permits walking and one pickup edge only')
                if player in (1, 2):
                    require(not item['pickup'] and item['x'] == item['y'] == 0, 'Unrelated chef received input')
                if player == 3:
                    require(not item['pickup'], 'Yielding chef must remain empty-handed without pickup input')
                if item['pickup']:
                    require(player == 0, 'Unexpected pickup input owner')
                    require(chef(previous, 0)['placementTargetId'] == station, 'Pickup edge lacked the preceding native placement target')
                    button_samples.append(state['gameplayFrame'])
            old_held, new_held = chef(previous, 0)['heldEntityId'], chef(state, 0)['heldEntityId']
            if old_held == stack and new_held == 0:
                require(entity(state, station)['attachedEntityId'] == stack, 'Hands emptied without the original stack attaching to the intended counter')
                native_transfers.append(state['gameplayFrame'])
        require(chef(state, 3)['heldEntityId'] == 0, 'Yielding chef unexpectedly acquired an item')
        previous = state
    require(len(button_samples) == 1 and len(native_transfers) == 1, 'Expected exactly one pickup edge and one authoritative attachment transition')
    require(chef(final, 0)['heldEntityId'] == 0 and entity(final, station)['attachedEntityId'] == stack, 'Original placement did not complete')
    endpoint = chef(final, 3)['position']
    require(math.hypot(endpoint['x'] - target['x'], endpoint['z'] - target['z']) <= .101, 'Helper did not reach the measured yield target')
    require(result['ok'] and result['state']['gameplayFrame'] == final['gameplayFrame'] and result['state']['score'] == final['score'], 'Result does not identify the same final native state')
    require(entity(result['state'], station)['attachedEntityId'] == stack and entity(result['state'], stack)['observedOrdinal'] == ordinal, 'Result lost final native stack attachment evidence')
    require(all(i['x'] == i['y'] == 0 and not i['pickup'] and not i['use'] and not i['dash'] for i in result['inputs']), 'Final input release is absent')
    return {'passed': True, 'classification': 'native diagnostic continuation; fresh full planner validation remains separate',
            'initialGameplayFrame': first['gameplayFrame'], 'finalGameplayFrame': final['gameplayFrame'],
            'advancingFrames': final['gameplayFrame'] - first['gameplayFrame'], 'nativeSamples': len(states),
            'elapsedNativeSeconds': first['timer'] - final['timer'], 'scoreUnchanged': final['score'], 'deliveredUnchanged': final['delivered'],
            'yieldPlayer': 3, 'yieldTarget': target, 'observedYieldPosition': endpoint,
            'placementPlayer': 0, 'stationId': station, 'sameStackId': stack, 'sameObservedOrdinal': ordinal,
            'sameDirtyPlateCount': 2, 'pickupInputFrames': button_samples, 'nativeAttachmentTransitionFrames': native_transfers,
            'requests': 'initial inspect, then complete four-chef one-frame logical inputs only'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--trace', default='artifacts/v9-idle-traffic-continuation-a.jsonl.gz')
    parser.add_argument('--result', default='artifacts/v9-idle-traffic-continuation-a-result.json')
    parser.add_argument('--before', default='artifacts/native-round-v9-failure-state.json')
    parser.add_argument('--plan', default='routes/probes/v9-idle-traffic-continuation.json')
    parser.add_argument('--controller', default='artifacts/planner-candidate-v9/OvercookedTAS.Controller.dll')
    parser.add_argument('--output', default='artifacts/v9-idle-traffic-continuation-a-proof.json')
    args = parser.parse_args()
    with gzip.open(args.trace, 'rt', encoding='utf-8') as stream:
        rows = [json.loads(line) for line in stream]
    load = lambda path: json.loads(Path(path).read_text(encoding='utf-8-sig'))
    proof = analyze(rows, load(args.result), load(args.before), load(args.plan))
    proof['artifacts'] = {key: pin(getattr(args, key)) for key in ('trace', 'result', 'before', 'plan', 'controller')}
    Path(args.output).write_text(json.dumps(proof, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({key: proof[key] for key in ('passed', 'advancingFrames', 'nativeSamples', 'sameStackId', 'sameDirtyPlateCount', 'nativeAttachmentTransitionFrames')}))
