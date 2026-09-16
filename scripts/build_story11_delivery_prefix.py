"""Build the no-search fixed-input prefix for the proven Story 1-1 first delivery."""
import argparse
import json
from pathlib import Path

from framework_plate_search import to_raw_request


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--preparation', type=Path, required=True)
    parser.add_argument('--delivery-dir', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    if args.out.exists():
        parser.error('Output already exists.')

    chefs = (43, 44, 45, 46)
    neutral = {str(chef): {'x': 0, 'y': 0, 'pickup': False,
                           'interact': False, 'dash': False} for chef in chefs}
    requests = [{'command': 'raw-input', 'segments': [{'frames': 28, 'chefs': neutral}]}]
    preparation = json.loads(args.preparation.read_text(encoding='utf-8-sig'))
    if preparation.get('command') != 'raw-input' or \
            sum(segment['frames'] for segment in preparation.get('segments', [])) != 42:
        raise ValueError('Expected the retained 42-payload-frame preparation recording.')
    requests.append(preparation)

    expected_boundaries = ((75, 138), (138, 200), (200, 280),
                           (280, 283), (283, 315), (315, 444))
    for number, (start, end) in enumerate(expected_boundaries):
        path = args.delivery_dir / f'delivery-75-{number:02d}-raw-capture.json'
        capture = json.loads(path.read_text(encoding='utf-8-sig'))
        if capture.get('validation') != 'exact-four-pad-frame-coverage' or \
                capture.get('startExclusive') != start or capture.get('endInclusive') != end or \
                len(capture.get('inputs', [])) != end-start:
            raise ValueError(f'Delivery input capture {number} has a different boundary.')
        requests.append(to_raw_request(capture['inputs'], chefs))

    observed = [sum(segment['frames'] for segment in request['segments']) + 2 for request in requests]
    if observed != [30, 44, 63, 62, 80, 3, 32, 129] or sum(observed) != 443:
        raise ValueError('Fixed-input prefix no longer reaches output frame 444 from fresh frame 1.')
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(requests, indent=2), encoding='utf-8')
    print(json.dumps({'out': str(args.out), 'requests': len(requests),
                      'observedFrames': observed, 'finalFrameFromFreshOne': 444}, indent=2))


if __name__ == '__main__':
    main()
