"""Streaming geometric drift measurements for two complete native JSONL traces.

No tolerance qualifies a replay. Chef identity uses playerId; entity identity
uses the recorded ID, without spatial matching or initial-body renumbering.
Physical proxies are reported separately. Exact native-event differences remain
visible independently of geometric magnitudes. Units are Unity world units,
world units/second, and degrees. Only current snapshots and per-ID aggregates
are retained, not frame histories.
"""
import argparse
import hashlib
import json
import math
import time
from pathlib import Path

from analyze_repro import Samples, first_diff, transient


def vector(value, fields='xyz'):
    if not isinstance(value, dict):
        return None
    values = [value.get(f) for f in fields]
    return values if all(type(v) in (int, float) and math.isfinite(v) for v in values) else None


def distance(a, b):
    return math.sqrt(sum((x - y) ** 2 for x, y in zip(a, b)))


def rotation_angle(a, b):
    """Shortest orientation angle; q and -q represent the same rotation."""
    na, nb = math.sqrt(sum(x*x for x in a)), math.sqrt(sum(x*x for x in b))
    if na == 0 or nb == 0:
        return None
    if a == b or all(x == -y for x, y in zip(a, b)):
        return 0.0
    x, y, z, w = (v / na for v in a)
    X, Y, Z, W = (v / nb for v in b)
    # conjugate(a)*b; atan2 retains precision for very small differences.
    v = (w*X - x*W - y*Z + z*Y, w*Y + x*Z - y*W - z*X,
         w*Z - x*Y + y*X - z*W)
    scalar = w*W + x*X + y*Y + z*Z
    return math.degrees(2 * math.atan2(math.sqrt(sum(n*n for n in v)), abs(scalar)))


def forward_angle(a, b):
    if not any(a) or not any(b):
        return None
    cross = (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
    return math.degrees(math.atan2(math.sqrt(sum(v*v for v in cross)), sum(x*y for x, y in zip(a, b))))


def point(key):
    return dict(zip(('segment', 'gameplayFrame', 'sameFrameOccurrence'), key))


class Metric:
    def __init__(self):
        self.compared = self.invalid = self.different = 0
        self.maximum = self.maximum_at = self.last = None

    def add(self, left, right, key, kind='vector'):
        a, b = vector(left, 'xyzw' if kind == 'rotation' else 'xyz'), vector(right, 'xyzw' if kind == 'rotation' else 'xyz')
        if a is None or b is None:
            self.invalid += 1
            return
        value = rotation_angle(a, b) if kind == 'rotation' else forward_angle(a, b) if kind == 'forward' else distance(a, b)
        if value is None:
            self.invalid += 1
            return
        self.compared += 1
        self.different += a != b
        observation = {'at': point(key), 'difference': value, 'expected': a, 'actual': b}
        self.last = observation
        if self.maximum is None or value > self.maximum:
            self.maximum, self.maximum_at = value, observation

    def report(self, end_key):
        end = self.last if self.last and self.last['at'] == point(end_key) else None
        return {'comparedSamples': self.compared, 'invalidOrMissingSamples': self.invalid,
                'recordedVectorDifferentSamples': self.different, 'maximum': self.maximum,
                'maximumObservation': self.maximum_at, 'endDifference': end,
                'lastMatchedObservation': self.last if end is None else None}


class Body:
    def __init__(self, identity):
        self.identity, self.names = identity, set()
        self.expected_entity_ids, self.actual_entity_ids = set(), set()
        self.compared = self.missing_expected = self.missing_actual = self.identity_mismatch = 0
        self.first_missing = None
        self.hash_compared = self.hash_different = 0
        self.metrics = {k: Metric() for k in ('position', 'velocity', 'rotationDegrees')}

    def add(self, left, right, key, chef=False):
        if chef:
            if left is not None:
                self.expected_entity_ids.add(left.get('entityId'))
            if right is not None:
                self.actual_entity_ids.add(right.get('entityId'))
        for obj in (left, right):
            if obj is not None:
                self.names.add(obj.get('name', ''))
        if left is None or right is None:
            self.missing_expected += left is None
            self.missing_actual += right is None
            self.first_missing = self.first_missing or {'at': point(key), 'missingFrom': 'expected' if left is None else 'actual'}
            return
        if not chef and left.get('name') != right.get('name'):
            self.identity_mismatch += 1
            self.first_missing = self.first_missing or {'at': point(key), 'reason': 'same ID, different names',
                'expectedName': left.get('name'), 'actualName': right.get('name')}
            return
        self.compared += 1
        for field, metric in self.metrics.items():
            source = 'rotation' if field == 'rotationDegrees' else 'forward' if field == 'forwardDegrees' else field
            kind = 'rotation' if field == 'rotationDegrees' else 'forward' if field == 'forwardDegrees' else 'vector'
            metric.add(left.get(source), right.get(source), key, kind)
        if isinstance(left.get('physicsHash'), str) and isinstance(right.get('physicsHash'), str):
            self.hash_compared += 1
            self.hash_different += left['physicsHash'] != right['physicsHash']

    def report(self, end_key):
        return {'id': self.identity, 'names': sorted(self.names), 'comparedSamples': self.compared,
                'expectedChefEntityIds': sorted(self.expected_entity_ids, key=str),
                'actualChefEntityIds': sorted(self.actual_entity_ids, key=str),
                'missingExpectedSamples': self.missing_expected, 'missingActualSamples': self.missing_actual,
                'identityMismatchSamples': self.identity_mismatch, 'firstIdentityGap': self.first_missing,
                'physicsHashComparedSamples': self.hash_compared, 'physicsHashDifferentSamples': self.hash_different,
                'metrics': {name: metric.report(end_key) for name, metric in self.metrics.items()}}


def native_events(state):
    events = []
    for event in state.get('gameEvents', []):
        value = {k: v for k, v in event.items() if k not in {'frame', 'fixedFrame', 'unityFrame', 'finalizedFrame'}}
        if event.get('finalizedFrame', -1) >= 0 and state.get('levelFrameZero', -1) >= 0:
            value['finalizedGameplayFrame'] = event['finalizedFrame'] - state['levelFrameZero']
        events.append(value)
    return events


def identity_map(values, field, issues, label, key):
    result = {}
    for value in values:
        identifier = value.get(field)
        if type(identifier) is not int or identifier in result:
            issues['count'] += 1
            if len(issues['examples']) < 10:
                issues['examples'].append({'at': point(key), 'side': label, 'field': field, 'id': identifier})
            continue
        result[identifier] = value
    return result


def file_info(path):
    status = path.stat()
    with path.open('rb') as source:
        sha = hashlib.file_digest(source, 'sha256').hexdigest()
    return {'path': str(path.resolve()), 'bytes': status.st_size, 'sha256': sha}, (status.st_size, status.st_mtime_ns)


def measure(expected_path, actual_path):
    started = time.monotonic()
    paths = [Path(expected_path), Path(actual_path)]
    provenance = [file_info(path) for path in paths]
    samples = [Samples(path) for path in paths]
    streams = [iter(sample) for sample in samples]
    errors = [None, None]
    def advance(side):
        try:
            return next(streams[side])
        except StopIteration:
            return None
        except (OSError, EOFError, ValueError) as error:
            errors[side] = type(error).__name__ + ': ' + str(error)
            return None
    a, b = advance(0), advance(1)
    alignment = {'matchedSamples': 0, 'expectedOnlySamples': 0, 'actualOnlySamples': 0, 'firstUnmatched': None}
    exact = {k: {'differentSamples': 0, 'firstDifference': None} for k in ('requests', 'inputs', 'nativeEvents', 'scoreLedger')}
    ledger_fields = ('score', 'baseScore', 'tips', 'combo', 'multiplier', 'delivered', 'deductions')
    chefs, entities, proxies = {}, {}, {}
    identities = {'count': 0, 'examples': []}
    end_key = (-1, -1, -1)
    while a is not None or b is not None:
        if b is None or (a is not None and a['key'] < b['key']):
            alignment['expectedOnlySamples'] += 1
            alignment['firstUnmatched'] = alignment['firstUnmatched'] or {'side': 'expected', 'at': point(a['key'])}
            a = advance(0)
            continue
        if a is None or b['key'] < a['key']:
            alignment['actualOnlySamples'] += 1
            alignment['firstUnmatched'] = alignment['firstUnmatched'] or {'side': 'actual', 'at': point(b['key'])}
            b = advance(1)
            continue
        key, left, right = a['key'], a['state'], b['state']
        end_key = key
        alignment['matchedSamples'] += 1
        compared = {'requests': (a['request'], b['request']), 'inputs': (a['inputs'], b['inputs']),
            'nativeEvents': (native_events(left), native_events(right)),
            'scoreLedger': ({k: left[k] for k in ledger_fields if k in left}, {k: right[k] for k in ledger_fields if k in right})}
        for field, (x, y) in compared.items():
            delta = first_diff(x, y)
            if delta:
                exact[field]['differentSamples'] += 1
                exact[field]['firstDifference'] = exact[field]['firstDifference'] or {'at': point(key), **delta}
        le = identity_map(left.get('entities', []), 'id', identities, 'expected', key)
        re = identity_map(right.get('entities', []), 'id', identities, 'actual', key)
        lc = identity_map(left.get('chefs', []), 'playerId', identities, 'expected', key)
        rc = identity_map(right.get('chefs', []), 'playerId', identities, 'actual', key)
        chef_ids = {c.get('entityId') for c in list(lc.values()) + list(rc.values())}
        for identifier in lc.keys() | rc.keys():
            if identifier not in chefs:
                chefs[identifier] = Body(identifier)
                chefs[identifier].metrics['forwardDegrees'] = Metric()
            body = chefs[identifier]
            def with_rotation(c, table):
                if c is None:
                    return None
                physical = table.get(c.get('entityId'), {})
                return {**c, 'rotation': physical.get('rotation'), 'physicsHash': physical.get('physicsHash')}
            body.add(with_rotation(lc.get(identifier), le), with_rotation(rc.get(identifier), re), key, chef=True)
        for identifier in le.keys() | re.keys():
            if identifier in chef_ids:
                continue
            x, y = le.get(identifier), re.get(identifier)
            target = proxies if identifier in proxies or any(transient(obj) for obj in (x, y) if obj is not None) else entities
            if identifier not in target:
                target[identifier] = Body(identifier)
            target[identifier].add(x, y, key)
        a, b = advance(0), advance(1)
    sources = []
    for i, path in enumerate(paths):
        status = path.stat()
        sources.append({**provenance[i][0], 'unchangedDuringRead': provenance[i][1] == (status.st_size, status.st_mtime_ns),
            'readError': errors[i], 'samples': samples[i].stats['samples'], 'segments': len(samples[i].stats['segments']),
            'legacyAbsoluteFrameFallback': samples[i].stats.get('legacyAbsoluteFrameFallback', False),
            'failedResponses': len(samples[i].stats['failedResponses']),
            'preGameplaySnapshots': samples[i].stats['preGameplaySnapshots']})
    def group_report(bodies):
        reports = [body.report(end_key) for _, body in sorted(bodies.items())]
        maxima = {}
        for metric in ('position', 'velocity', 'rotationDegrees'):
            valid = [r for r in reports if r['metrics'][metric]['maximum'] is not None]
            highest = max(valid, key=lambda r: r['metrics'][metric]['maximum'], default=None)
            maxima[metric] = {'id': highest['id'], **highest['metrics'][metric]['maximumObservation']} if highest else None
        changed = [r for r in reports if r['missingExpectedSamples'] or r['missingActualSamples'] or r['identityMismatchSamples']
                   or r['physicsHashDifferentSamples'] or any(m['recordedVectorDifferentSamples'] or m['invalidOrMissingSamples']
                                                             for m in r['metrics'].values())]
        return {'observedIDs': len(reports), 'IDsWithDifferencesOrMissingData': len(changed), 'maxima': maxima,
                'bodiesWithDifferencesOrMissingData': changed}
    return {'format': 'oc2-state-drift', 'version': 1, 'sources': sources,
        'policy': {'alignment': 'segment, gameplayFrame, same-frame occurrence; no interpolation',
            'identity': 'chefs by playerId; other objects by recorded native ID; names must match; no ID canonicalization',
            'rotation': 'shortest normalized quaternion angle; signs q/-q are physically equivalent; raw vector/hash differences retained',
            'events': 'Exact native event payloads and gameplay finalization frames; only absolute frame/fixedFrame/unityFrame/finalizedFrame replaced or excluded',
            'qualification': 'Magnitude report only, no tolerances and no reproducibility/high-score qualification; not a complete discrete gameplay-state comparison',
            'units': {'position': 'world units', 'velocity': 'world units/second', 'rotationDegrees': 'degrees'},
            'end': 'Last aligned sample only; disappeared entities retain separately labeled last matched observations'},
        'alignment': alignment, 'identityIssues': identities, 'exactChecks': exact,
        'lastAlignedSample': point(end_key) if alignment['matchedSamples'] else None,
        'chefs': [body.report(end_key) for _, body in sorted(chefs.items())],
        'nativeEntities': group_report(entities), 'physicalProxies': group_report(proxies),
        'wallSeconds': round(time.monotonic() - started, 3)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('expected', type=Path)
    parser.add_argument('actual', type=Path)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    if args.out.exists():
        parser.error('Output already exists; use a new report path')
    report = measure(args.expected, args.actual)
    with args.out.open('x', encoding='utf-8') as output:
        json.dump(report, output, indent=2, allow_nan=False)
    print(json.dumps({'out': str(args.out), 'alignment': report['alignment'], 'wallSeconds': report['wallSeconds']}))


if __name__ == '__main__':
    main()
