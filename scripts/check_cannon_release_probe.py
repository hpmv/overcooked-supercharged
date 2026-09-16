"""Read-only proof of the bounded native launch-completion/overlapped-walk probe."""
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
    return {'path': str(path), 'sha256': digest.hexdigest(), 'bytes': path.stat().st_size}


def distance(a, b):
    return math.hypot(a['x'] - b['x'], a['z'] - b['z'])


def neutral(item):
    return item['x'] == item['y'] == 0 and not any(item[k] for k in ('pickup', 'use', 'dash'))


def project(state):
    entities = {}
    for item in state['entities']:
        if 'Plate' in item.get('components', []) or item.get('cannonState') is not None or item['id'] in {c['entityId'] for c in state['chefs']}:
            entities[item['id']] = {key: item.get(key) for key in ('id', 'observedOrdinal', 'active', 'cannonState', 'cannonLoadedEntityId', 'cannonFlying', 'cannonReady', 'cannonTarget')}
    return {'frame': state['gameplayFrame'], 'timer': state['timer'], 'clientTime': state['clientTime'], 'score': state['score'], 'delivered': state['delivered'],
            'chefs': {c['playerId']: c for c in state['chefs']}, 'entities': entities,
            'pluginSha256': state.get('instrumentation', {}).get('manifest', {}).get('pluginSha256')}


def read_trace(path):
    rows, events = [], []
    opener = gzip.open if str(path).endswith('.gz') else open
    with opener(path, 'rt', encoding='utf-8-sig') as stream:
        for line in stream:
            entry = json.loads(line)
            if entry.get('kind') == 'event':
                if entry['name'] in ('transportLaunchConfirmed', 'jobStart', 'jobComplete', 'actionFailure', 'planFailure'):
                    events.append(entry)
            elif entry.get('kind') == 'call':
                require(entry['response']['ok'], 'A native request failed')
                rows.append({'request': entry['request'], 'state': project(entry['response']['state'])})
    return rows, events


def check(rows, events, result):
    require(rows, 'No native call observations')
    require(not any(e['name'] in ('actionFailure', 'planFailure') for e in events), 'A route/action failed')
    launches = [e['value'] for e in events if e['name'] == 'transportLaunchConfirmed']
    require(len(launches) == 1, 'Expected exactly one launch-completion receipt')
    receipt = launches[0]
    require(receipt['nativeFlying'] and receipt['destinationRegion'] == 'lower-right', 'Receipt lacks native flight or expected measured platform')
    firer, passenger = receipt['firingPlayer'], receipt['passengerPlayer']
    require((firer, passenger) == (0, 2) and receipt['held'] != 0, 'Probe did not retain its intended chef slots and plate')
    cannon, passenger_id, held = receipt['cannon'], receipt['passenger'], receipt['held']
    first = rows[0]['state']
    require(first['frame'] == 0 and first['score'] == first['delivered'] == 0, 'Probe did not begin from a fresh unscored native round')
    require(first['entities'][held]['observedOrdinal'] == receipt['heldOrdinal'], 'Carried plate was not the same initial native object')
    require(first['pluginSha256'], 'Initial actual plugin identity is absent')
    by_frame = {}
    previous = None
    fire_edges, overlap = [], []
    for row in rows:
        request, state = row['request'], row['state']
        require(request['command'] in ('restart', 'inspect', 'step') and request.get('version') == 1, 'Unexpected command in bounded ordinary-input probe')
        require(state['score'] == state['delivered'] == 0, 'Unexpected scoring or delivery in clean-plate probe')
        require(state['pluginSha256'] == first['pluginSha256'], 'Actual loaded plugin identity changed')
        require(set(state['chefs']) == set(range(4)), 'Native four-chef configuration changed')
        if request['command'] == 'step':
            require(previous is not None and request['steps'] == 1 and state['frame'] == previous['frame'] + 1, 'Native one-frame progression is discontinuous')
            require(state['clientTime'] > previous['clientTime'] and state['timer'] < previous['timer'], 'Native elapsed gameplay time did not advance')
            inputs = {i['player']: i for i in request['inputs']}
            require(set(inputs) == set(range(4)), 'Incomplete four-chef logical inputs')
            require(all(all(math.isfinite(i[a]) and abs(i[a]) <= 1 for a in ('x', 'y')) for i in inputs.values()), 'Illegal movement axes')
            require(all(not i['dash'] for i in inputs.values()) and neutral(inputs[1]) and neutral(inputs[3]), 'Unexpected dash or unrelated chef input')
            if inputs[firer]['use']:
                require(previous['chefs'][firer]['useTargetId'] == receipt['button'], 'Fire edge lacked previous native button target')
                require(previous['entities'][cannon]['cannonState'] == 'Load' and previous['entities'][cannon]['cannonReady'] and
                        not previous['chefs'][passenger]['controlsEnabled'], 'Fire edge was not directed at the observed ready loaded cannon')
                fire_edges.append(previous['frame'])
            if previous['frame'] >= receipt['launchFrame']:
                require(neutral(inputs[passenger]), 'Passenger received input after confirmed launch')
                if (previous['entities'][cannon]['cannonFlying'] and state['entities'][cannon]['cannonFlying'] and
                        (inputs[firer]['x'] != 0 or inputs[firer]['y'] != 0) and
                        distance(previous['chefs'][firer]['position'], state['chefs'][firer]['position']) > .005):
                    overlap.append(state['frame'])
            if previous['frame'] == receipt['launchFrame']:
                require(neutral(inputs[firer]), 'Launch-completing frame did not release firing input')
        if state['frame'] >= receipt['fireEdgeFrame']:
            require(state['chefs'][passenger]['entityId'] == passenger_id and state['chefs'][passenger]['heldEntityId'] == held,
                    'Exact passenger or held logical plate changed during flight/arrival')
            for entity_id, ordinal_key in ((cannon, 'cannonOrdinal'), (passenger_id, 'passengerOrdinal'), (held, 'heldOrdinal')):
                require(state['entities'][entity_id]['observedOrdinal'] == receipt[ordinal_key], 'Observed native identity changed during transport')
            require(not state['chefs'][passenger]['respawning'] and state['entities'][held]['active'], 'Passenger respawned or original plate deactivated')
        by_frame[state['frame']] = state
        previous = state
    require(fire_edges == [receipt['fireEdgeFrame']], 'Expected exactly the one recorded native fire edge')
    launch = by_frame[receipt['launchFrame']]
    require(launch['entities'][cannon]['cannonState'] == 'Launched' and launch['entities'][cannon]['cannonFlying'] and
            launch['entities'][cannon]['cannonLoadedEntityId'] == passenger_id, 'Receipt lacks corresponding authoritative launch snapshot')
    arrivals = []
    for frame in sorted(k for k in by_frame if k >= receipt['launchFrame']):
        state = by_frame[frame]
        chef = state['chefs'][passenger]
        if (not state['entities'][cannon]['cannonFlying'] and all(chef[k] for k in ('controlsEnabled', 'directlyControlled', 'canAcceptInput')) and
                not chef['inputSuppressed'] and chef['position']['y'] <= .9 and distance(chef['position'], receipt['landingTarget']) <= 1):
            arrivals.append(frame)
            if len(arrivals) >= 2 and arrivals[-1] == arrivals[-2] + 1:
                break
        else:
            arrivals = []
    require(len(arrivals) >= 2 and arrivals[-1] - receipt['launchFrame'] <= 120, 'Exact passenger did not produce two bounded native arrival samples')
    complete = [e['value'] for e in events if e['name'] == 'jobComplete' and e['value']['id'] == 'fire-until-native-launch']
    require(len(complete) == 1 and complete[0]['frame'] < arrivals[0], 'Firing job did not finish before passenger arrival')
    require(overlap and min(overlap) < arrivals[0], 'No observed firing-chef movement overlapped actual native flight')
    final = rows[-1]['state']
    require(result['ok'] and result['state']['gameplayFrame'] == final['frame'] and result['state']['score'] == 0, 'Result differs from final trace state')
    require(all(neutral(i) for i in result['inputs']), 'Final four-chef input release is absent')
    require(all(c['controlsEnabled'] and not c['respawning'] for c in final['chefs'].values()) and
            final['chefs'][passenger]['heldEntityId'] == held, 'Final native chef/held-plate state is inconsistent')
    return {'ok': True, 'classification': 'Native launch-completion and overlapping ordinary movement mechanism; bot arrival lease and high-score qualification are separate',
            'launchReceipt': receipt, 'launchFrame': receipt['launchFrame'], 'fireJobCompleteFrame': complete[0]['frame'],
            'arrivalSamples': arrivals, 'firingChefMovementDuringFlightFrames': overlap,
            'observedEarlyReleaseFrames': arrivals[1] - complete[0]['frame'], 'nativeFinalFrame': final['frame'], 'samples': len(rows),
            'sameInitialPlateRetained': True, 'passengerInputsNeutralAfterLaunch': True, 'score': 0,
            'pluginSha256': first['pluginSha256']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--trace', required=True, type=Path)
    parser.add_argument('--result', required=True, type=Path)
    parser.add_argument('--plan', default=Path('routes/probes/cannon-fire-release.json'), type=Path)
    parser.add_argument('--out', required=True, type=Path)
    args = parser.parse_args()
    rows, events = read_trace(args.trace)
    report = check(rows, events, json.loads(args.result.read_text(encoding='utf-8-sig')))
    report['artifacts'] = {name: pin(path) for name, path in (('trace', args.trace), ('result', args.result), ('plan', args.plan), ('checker', Path(__file__)))}
    args.out.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({k: report[k] for k in ('ok', 'launchFrame', 'arrivalSamples', 'observedEarlyReleaseFrames', 'nativeFinalFrame')}))


if __name__ == '__main__':
    main()
