#!/usr/bin/env python3
"""Create a NEW input movie from a closed trial and an explicitly authored neutral tail.

This preserves failed-trial evidence. It does not promote that trial to success.
Only a same-frame, identical-state, neutral-input join is accepted; added neutral
disconnect observer records are recorded as an explicit transport difference.
"""
import argparse
import gzip
import hashlib
import json
import shutil
from collections import Counter
from pathlib import Path

from extract_inputs import encode, entries, request_from, sha256_file


def neutral(pads):
    return (isinstance(pads, list) and len(pads) == 4
            and {p.get('player') for p in pads} == {0, 1, 2, 3}
            and all(set(p) == {'player', 'x', 'y', 'pickup', 'use', 'dash'}
                    and p['x'] == p['y'] == 0
                    and all(p[k] is False for k in ('pickup', 'use', 'dash')) for p in pads))


def boundary(previous, following):
    if not neutral(previous.get('inputs')) or not neutral(following.get('inputs')):
        raise ValueError('Both connection-boundary input snapshots must be explicitly neutral')
    a, b = previous['state'], following['state']
    if not isinstance(a.get('gameplayFrame'), int) or a['gameplayFrame'] < 0:
        raise ValueError('Join must be in the same native gameplay segment')
    difference = [k for k in a.keys() | b.keys() if a.get(k) != b.get(k)]
    if any(k != 'gameEvents' for k in difference):
        raise ValueError('Join state changed: ' + ', '.join(difference))
    before, after = a.get('gameEvents', []), b.get('gameEvents', [])
    if after[:len(before)] != before:
        raise ValueError('Existing native event history changed at join')
    additions = after[len(before):]
    for event in additions:
        if (event.get('kind') != 'input_release'
                or event.get('reason') != 'controller-disconnected'
                or event.get('inputsNeutral') is not True
                or event.get('gameplayFrame') != a['gameplayFrame']
                or event.get('scoreApplied') is not False
                or any(event.get(k) != 0 for k in ('scoreDelta', 'baseScoreDelta', 'tipDelta', 'deductionDelta', 'deliveryDelta'))):
            raise ValueError('Join adds an event other than an inert neutral disconnect observation')
    return {'gameplayFrame': a['gameplayFrame'], 'allOtherStateFieldsIdentical': True,
            'bothInputSnapshotsNeutral': True, 'addedDisconnectObservations': additions}


def compose(prefix, tail, movie, combined, manifest):
    paths = [Path(p).resolve() for p in (prefix, tail, movie, combined, manifest)]
    prefix, tail, movie, combined, manifest = paths
    if len(set(paths)) != 5 or any(p.exists() for p in paths[2:]):
        raise ValueError('Use distinct new output paths; source recordings are immutable')
    if not all(str(p).endswith('.jsonl.gz') for p in (prefix, tail, movie, combined)):
        raise ValueError('Sources, movie and byte-concatenated observation trace must be .jsonl.gz')
    sources = []
    for path in (prefix, tail):
        stat = path.stat()
        sources.append({'path': str(path), 'sha256': sha256_file(path), 'size': stat.st_size, 'mtimeNs': stat.st_mtime_ns})
    for path in paths[2:]:
        path.parent.mkdir(parents=True, exist_ok=True)
    digest, total, frames = hashlib.sha256(), 0, 0
    previous, join = None, None
    try:
        with movie.open('xb') as raw, gzip.GzipFile(filename='', mode='wb', fileobj=raw, mtime=0) as writer:
            for source_index, path in enumerate((prefix, tail)):
                count, stepped, commands, last_frame = 0, 0, Counter(), None
                for line, entry in entries(path):
                    request = request_from(entry, line)
                    if request is None:
                        continue
                    response = entry.get('response')
                    if not isinstance(response, dict) or response.get('ok') is not True or not isinstance(response.get('state'), dict):
                        raise ValueError('Every source request needs its successful recorded native response')
                    command = request['command']
                    frame = response['state'].get('gameplayFrame')
                    if type(frame) is not int:
                        raise ValueError('Missing gameplay frame')
                    if count == 0:
                        if source_index == 0 and (command != 'restart' or frame != 0):
                            raise ValueError('Prefix must begin with a recorded native restart at frame zero')
                        if source_index == 1:
                            if command != 'inspect':
                                raise ValueError('Tail must begin with a non-advancing inspection')
                            join = boundary(previous, response)
                    elif command in ('load', 'restart'):
                        raise ValueError('Only the initial restart is allowed')
                    if command not in ({'restart', 'inspect', 'preview', 'step'} if source_index == 0 else {'inspect', 'step'}):
                        raise ValueError('Unexpected protocol command: ' + command)
                    delta = request.get('steps', 1) if command == 'step' else 0
                    if last_frame is not None and frame != last_frame + delta:
                        raise ValueError('Recorded native frame discontinuity')
                    if source_index == 1 and command == 'step' and (not neutral(request.get('inputs')) or not neutral(response.get('inputs'))):
                        raise ValueError('Authored tail must contain only explicit four-chef neutral inputs')
                    data = encode(request)
                    writer.write(data)
                    digest.update(data)
                    count += 1
                    commands[command] += 1
                    stepped += delta
                    last_frame, previous = frame, response
                if count == 0 or stepped == 0:
                    raise ValueError('Each source must contain recorded native steps')
                sources[source_index].update(requests=count, steppedFrames=stepped, commands=dict(commands))
                total += count
                frames += stepped
        end = previous['state']
        if end.get('timer') != 0 or end.get('serverRoundActive') is not False or end.get('clientRoundActive') is not False:
            raise ValueError('Neutral continuation must reach the native round end')
        for source in sources:
            path = Path(source['path'])
            stat = path.stat()
            if (stat.st_size, stat.st_mtime_ns) != (source['size'], source['mtimeNs']) or sha256_file(path) != source['sha256']:
                raise ValueError('A source changed while being composed')
        with combined.open('xb') as output:
            for path in (prefix, tail):
                with path.open('rb') as source:
                    shutil.copyfileobj(source, output, 1024 * 1024)
        report = {'format': 'oc2-authored-neutral-continuation', 'version': 1,
                  'sources': sources, 'join': join, 'requests': total, 'steppedFrames': frames,
                  'movie': str(movie), 'movieSha256': sha256_file(movie),
                  'canonicalRequestsSha256': digest.hexdigest(),
                  'combinedObservationTrace': str(combined), 'combinedTraceSha256': sha256_file(combined),
                  'combinedTraceEncoding': 'Exact gzip members concatenated in source order; all original bytes, headers and failures retained',
                  'final': {k: end.get(k) for k in ('gameplayFrame', 'timer', 'score', 'baseScore', 'tips', 'deductions', 'delivered', 'serverRoundActive', 'clientRoundActive', 'gameState')},
                  'qualification': 'New authored complete input movie; original bot trial remains failed. Fresh-process replay and target score are separate gates.',
                  'transportLimitation': 'One-connection playback omits the explicitly listed same-frame neutral disconnect observation history.',
                  'toolSha256': sha256_file(Path(__file__))}
        with manifest.open('x', encoding='utf-8', newline='\n') as output:
            json.dump(report, output, indent=2)
            output.write('\n')
        return report
    except Exception:
        for path in paths[2:]:
            path.unlink(missing_ok=True)
        raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('prefix', 'tail', 'movie', 'combined', 'manifest'):
        parser.add_argument('--' + name, required=True)
    args = parser.parse_args()
    report = compose(**vars(args))
    print(json.dumps({k: report[k] for k in ('movie', 'movieSha256', 'requests', 'steppedFrames', 'final')}))


if __name__ == '__main__':
    main()
