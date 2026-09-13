"""Read a growing gzip trace once and write a compact, explicitly partial status.

This file-only observer never connects to or advances the game. A closed gzip
stream means only that recording closed, not that a round or score qualified.
"""
import argparse
import collections
import datetime
import json
import os
import time
import zlib
from pathlib import Path


def vessel_observations(state):
    """Observed processing state only; attachment does not prove heat is enabled."""
    entities = [e for e in state.get('entities', []) if e.get('active') is True]
    parents = collections.defaultdict(list)
    for entity in entities:
        attached = entity.get('attachedEntityId', 0)
        if attached:
            parents[attached].append(entity)

    def food_states(node):
        if not isinstance(node, dict):
            return set()
        result = {node['state']} if node.get('state') else set()
        for child in node.get('children', []):
            result.update(food_states(child))
        return result

    rows = []
    for entity in entities:
        components = set(entity.get('components', []))
        if not components.intersection(('CookableContainer', 'MixableContainer')):
            continue
        processing = []
        for mode, component in (('cooking', 'CookingStation'), ('mixing', 'MixingStation')):
            progress, duration = entity.get(mode + 'Progress'), entity.get(mode + 'Time')
            if not isinstance(duration, (int, float)) or duration <= 0:
                continue
            homes = [p['id'] for p in parents[entity['id']] if component in p.get('components', [])]
            processing.append({'mode': mode, 'progress': progress, 'nativeDuration': duration,
                               'nativeProcessingParents': sorted(homes)})
        rows.append({'entityId': entity['id'], 'observedOrdinal': entity.get('observedOrdinal'),
                     'name': entity.get('name'), 'parents': sorted(p['id'] for p in parents[entity['id']]),
                     'ingredientIds': entity.get('ingredientIds', []),
                     'foodStates': sorted(food_states(entity.get('composition'))), 'processing': processing})
    return sorted(rows, key=lambda row: row['entityId'])


class Tail:
    def __init__(self):
        self.decoder = zlib.decompressobj(31)
        self.pending = b''
        self.last_call = None
        self.events = collections.deque(maxlen=12)
        self.latest_planner_status = None
        self.milestones = collections.deque(maxlen=12)
        self.compressed_bytes = 0
        self.records = 0

    def feed(self, chunk):
        self.compressed_bytes += len(chunk)
        compressed = chunk
        while compressed:
            raw = self.decoder.decompress(compressed, 1024 * 1024)
            compressed = self.decoder.unconsumed_tail
            lines = (self.pending + raw).split(b'\n')
            self.pending = lines.pop()
            for line in lines:
                self.records += 1
                prefix = line[:100].replace(b' ', b'')
                if b'"kind":"call"' in prefix:
                    self.last_call = line
                elif b'"kind":"event"' in prefix:
                    event = json.loads(line)
                    value = event.get('value')
                    if isinstance(value, dict):
                        value = {k: v for k, v in value.items()
                                 if k not in ('state', 'snapshot', 'preview', 'configuration')}
                    self.events.append({'name': event.get('name'), 'value': value})
                    if event.get('name') == 'plannerStatus':
                        self.latest_planner_status = value
                    if any(word in (event.get('name') or '').lower()
                           for word in ('failure', 'leaseacquired', 'leasereleased', 'plannerfinished', 'yield',
                                        'sausagebuffer', 'pantrychopdelegated', 'fryerrescue', 'fryeroffheat',
                                        'imminenthead', 'potrescue', 'potoffheat', 'heatadmission', 'heatobligation', 'nativeheat')):
                        self.milestones.append({'name': event.get('name'), 'value': value})

    def status(self):
        row = json.loads(self.last_call) if self.last_call else {}
        response = row.get('response') or {}
        state = response.get('state') or {}
        result = {k: state.get(k) for k in ('gameplayFrame', 'gameplayFixedFrame', 'timer',
                  'score', 'delivered', 'deductions', 'serverRoundActive', 'clientRoundActive')}
        return {'qualification': 'Partial file observation, not run verification',
                'updatedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
                'compressedBytesRead': self.compressed_bytes, 'completeRecords': self.records,
                'closedGzip': self.decoder.eof, 'incompleteRecordBytes': len(self.pending),
                'callIndex': row.get('index'), 'state': result,
                'vessels': vessel_observations(state),
                'deliveryHistory': [e for e in state.get('gameEvents', []) if e.get('kind') == 'delivery'],
                'chefs': [{k: c.get(k) for k in ('playerId', 'position', 'heldEntityId', 'controlsEnabled')}
                          for c in state.get('chefs', [])], 'recentEvents': list(self.events),
                'latestPlannerStatus': self.latest_planner_status, 'milestones': list(self.milestones)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('trace', type=Path)
    parser.add_argument('--out', required=True, type=Path)
    parser.add_argument('--interval', type=float, default=10)
    parser.add_argument('--idle-timeout', type=float, default=120)
    args = parser.parse_args()
    if args.interval <= 0 or args.idle_timeout <= 0:
        parser.error('Timing limits must be positive')
    if args.out.exists() or args.trace.resolve() == args.out.resolve():
        parser.error('Status output must be a new file distinct from the trace')
    tail = Tail()
    last_emit = 0
    last_change = time.monotonic()
    previous_summary = None
    with args.trace.open('rb') as source:
        while True:
            data = source.read(1024 * 1024)
            if data:
                tail.feed(data)
                last_change = time.monotonic()
            now = time.monotonic()
            stopped = tail.decoder.eof or now - last_change >= args.idle_timeout
            if now - last_emit >= args.interval or stopped:
                status = tail.status()
                status['stoppedForIdle'] = stopped and not tail.decoder.eof
                temporary = args.out.with_suffix(args.out.suffix + '.tmp')
                temporary.write_text(json.dumps(status, indent=2), encoding='utf-8')
                os.replace(temporary, args.out)
                summary = (status['state']['delivered'], status['state']['score'], stopped)
                if summary != previous_summary:
                    print(json.dumps({'frame': status['state']['gameplayFrame'],
                                      'delivered': summary[0], 'score': summary[1], 'stopped': stopped}), flush=True)
                    previous_summary = summary
                last_emit = now
            if stopped:
                break
            if not data:
                time.sleep(min(args.interval, 1))


if __name__ == '__main__':
    main()
