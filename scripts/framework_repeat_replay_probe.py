"""Repeat a completed input probe's live checkpoint without reloading the level."""
import argparse
import json
from pathlib import Path
import re
import sys
import time

from compare_framework_frames import first_difference
from framework_input_probe import native_physics_comparison, pickup_outcome
from framework_native_search import (boundary_matches, compare_boundary, exact_values, food_trees,
                                     gameplay_round, native_clock_state, require_native_boundary,
                                     require_recorded_completion)
from framework_pause_boundary import PauseBoundaryError, observe_settled_pause
from framework_rpc import Client, ControllerClient
from framework_story11_registry import RegistryEvidence, world_proof


def take(rows, label):
    matches = [row['response'] for row in rows if row.get('label') == label]
    if not matches:
        raise ValueError('Source probe does not contain a ' + label + ' observation.')
    # Settled native observations deliberately sample the same label repeatedly;
    # the final sample is the receipt returned by observe_settled_pause.
    return matches[-1]


def take_any(rows, *labels):
    for label in labels:
        matches = [row['response'] for row in rows if row.get('label') == label]
        if matches:
            return matches[-1]
    raise ValueError('Source probe does not contain any of these observations: ' + ', '.join(labels))


def endpoint_comparison(expected, expected_native, actual, actual_native, fixed_entity_ids, recording,
                        checkpoint_frame):
    left = {entity['id']: entity for entity in expected['entities']}
    right = {entity['id']: entity for entity in actual['entities']}
    changed = sorted(identity for identity in left.keys() | right.keys()
                     if not exact_values(left.get(identity), right.get(identity)))
    physics = native_physics_comparison(expected_native['bridge']['nativePhysics'],
                                        actual_native['bridge']['nativePhysics'], fixed_entity_ids,
                                        actual_native, checkpoint_frame)
    result = {
        'frameEqual': actual['frame'] == expected['frame'],
        'changedEntityIds': changed,
        'nativeRoundEqual': exact_values(gameplay_round(expected_native['bridge']['nativeRound']),
                                         gameplay_round(actual_native['bridge']['nativeRound'])),
        'nativeFoodEqual': exact_values(expected_native['detail']['entities'], actual_native['detail']['entities']),
        'nativePhysicsEqual': physics['equal'],
        'nativePhysicsComparison': physics,
        'nativeClocksEqual': exact_values(native_clock_state(expected_native['bridge']),
                                          native_clock_state(actual_native['bridge'])),
        'recordingEqual': actual['rawInput'].get('recordingSha256') == recording['sha256'],
    }
    result['passed'] = all((result['frameEqual'], not changed, result['nativeRoundEqual'],
                            result['nativeFoodEqual'], result['nativePhysicsEqual'],
                            result['nativeClocksEqual'], result['recordingEqual']))
    return result


def baseline_comparison(expected, expected_native, actual, actual_native, fixed_entity_ids,
                        checkpoint_frame):
    result = compare_boundary(expected, expected_native['bridge']['nativeRound'], food_trees(expected_native),
                              actual, actual_native['bridge']['nativeRound'], food_trees(actual_native))
    physics = native_physics_comparison(expected_native['bridge']['nativePhysics'],
                                        actual_native['bridge']['nativePhysics'], fixed_entity_ids,
                                        actual_native, checkpoint_frame)
    result['nativePhysicsEqual'] = physics['equal']
    result['nativePhysicsComparison'] = physics
    result['nativeClocksEqual'] = exact_values(native_clock_state(expected_native['bridge']),
                                              native_clock_state(actual_native['bridge']))
    result['passed'] = boundary_matches(result) and physics['equal'] and result['nativeClocksEqual']
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--initial-source', type=Path,
                        help='Optional passing probe whose replay endpoint must match the current live state.')
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--repeats', type=int, default=2)
    parser.add_argument('--actor-rebuild-slot', default='rigidbody-actor-rebuild')
    parser.add_argument('--reconcile-dynamic-registry', action='store_true',
                        help='Publish observation-only retirements for dynamic physical-container proxies.')
    parser.add_argument('--registry-history-source', type=Path,
                        help='Optional interrupted repeat probe whose last restored boundary seeds proxy identity.')
    args = parser.parse_args()
    if not 1 <= args.repeats <= 20:
        parser.error('Use 1..20 additional replays.')
    if args.registry_history_source and not args.reconcile_dynamic_registry:
        parser.error('--registry-history-source requires --reconcile-dynamic-registry.')
    args.out.mkdir(parents=True, exist_ok=False)
    source_summary = json.loads((args.source/'summary.json').read_text())
    rows = json.loads((args.source/'observations.json').read_text())
    recording = json.loads((args.source/'recording.json').read_text())
    if source_summary.get('passed') is not True or source_summary.get('contactManagerFreeStackRestored') is not True:
        raise ValueError('Source must be a passing contact-pool input probe.')
    # A zero-warmup input probe reuses its ``initial`` observation as the
    # checkpoint baseline instead of emitting a duplicate ``base`` row.
    base, base_native = take_any(rows, 'base', 'initial'), take(rows, 'base-native')
    expected, expected_native = take(rows, 'original'), take(rows, 'original-native')
    source_replay, source_replay_native = take(rows, 'replayed'), take(rows, 'replayed-native')
    fixed_entity_ids = {entity['id'] for entity in base['entities']}
    initial_replay, initial_replay_native = source_replay, source_replay_native
    initial_recording = recording
    initial_fixed_entity_ids = fixed_entity_ids
    if args.initial_source:
        initial_summary = json.loads((args.initial_source/'summary.json').read_text())
        initial_rows = json.loads((args.initial_source/'observations.json').read_text())
        initial_recording = json.loads((args.initial_source/'recording.json').read_text())
        if initial_summary.get('passed') is not True:
            raise ValueError('--initial-source must be a passing input probe.')
        initial_base = take_any(initial_rows, 'base', 'initial')
        initial_fixed_entity_ids = {entity['id'] for entity in initial_base['entities']}
        initial_replay = take(initial_rows, 'replayed')
        initial_replay_native = take(initial_rows, 'replayed-native')
    checkpoint_frame = source_summary['startFrame']
    classification = ('non-adjacent checkpoint rewind plus fixed-input replay'
                      if args.initial_source else 'additional same-checkpoint fixed-input replays')
    evidence, summary = [], {'passed': False, 'classification': classification,
                             'source': str(args.source.resolve()), 'checkpointFrame': checkpoint_frame,
                             'requestedAdditionalReplays': args.repeats,
                             'dynamicRegistryReconciled': args.reconcile_dynamic_registry}
    if args.initial_source:
        summary['initialSource'] = str(args.initial_source.resolve())
    if args.registry_history_source:
        summary['registryHistorySource'] = str(args.registry_history_source.resolve())
    animator_failure_fields = (
        'failure', 'resumeFailure', 'resumePrefixObserverFailure', 'chefRandomizeFailure',
        'firstReplayDifference', 'firstRandomStateReplayDifference',
        'firstChefRandomizeReplayDifference',
        'firstControllerInputReplayDifference',
        'firstTransitionTopologyReplayDifference', 'firstMixerGraphReplayDifference',
        'firstOwnerGraphReplayDifference', 'firstResumePrefixPostDifference',
        'firstResumePrefixPostRandomStateDifference',
        'firstResumePrefixPostControllerInputDifference',
        'firstResumePrefixPostTransitionTopologyDifference',
        'firstResumePrefixPostMixerGraphDifference',
        'firstResumePrefixPostOwnerGraphDifference')
    bridge = host = None
    began = time.monotonic()
    registry_evidence = RegistryEvidence() if args.reconcile_dynamic_registry else None

    def call(target, request, label):
        response = (bridge if target == 'bridge' else host).call(request)
        evidence.append({'label': label, 'target': target, 'request': request,
                         'response': response, 'wallSeconds': time.monotonic()-began})
        return response

    def settled(label):
        deadline = time.monotonic() + 30
        while True:
            status = host.call({'command': 'status'})
            if status['errors'] or status['state'] == 'Error':
                raise RuntimeError(json.dumps(status))
            if status['state'] == 'Paused' and not status['requestPending']:
                return call('controller', {'command': 'inspect', 'full': True}, label)
            if time.monotonic() >= deadline:
                raise TimeoutError(json.dumps(status))
            time.sleep(.025)

    def native_observation(label):
        def read_frame():
            status = host.call({'command': 'status'})
            if status['state'] != 'Paused' or status['requestPending'] or status['errors']:
                raise RuntimeError('Native observation requires a settled pause.')
            return status['frame']
        try:
            result = observe_settled_pause(lambda: call('bridge', {'command': 'food'}, label), read_frame)
        except PauseBoundaryError as error:
            (args.out/(label+'-pause-proof.json')).write_text(json.dumps(error.report, indent=2))
            raise
        (args.out/(label+'-pause-proof.json')).write_text(json.dumps(result['proof'], indent=2))
        return result['receipt']

    def reconcile_registry(state, native, label):
        if registry_evidence is None:
            return state, native
        requests = registry_evidence.requests(state, native)
        publications = []
        if requests:
            before = world_proof(state, native)
            call('bridge', {'command': 'pause'}, label+'-registry-fence')
            for number, request in enumerate(requests):
                response = call('bridge', {'command': 'hot-call', 'slot': 'registry-observer',
                                'operation': request['operation'], 'args': request['args']},
                                label+'-registry-'+str(number))
                publication = registry_evidence.validate_publication(request, response)
                publications.append((request, publication))
            deadline = time.monotonic() + 3
            while True:
                state = settled(label+'-registry-received')
                if all(registry_evidence.received(request, publication, state)
                       for request, publication in publications):
                    break
                if time.monotonic() > deadline:
                    raise TimeoutError('Published registry observation did not reach the paused controller.')
                time.sleep(.01)
            native = call('bridge', {'command': 'food'}, label+'-registry-native-after')
            difference = first_difference(before, world_proof(state, native))
            proof = {
                'requests': requests,
                'publications': [publication for _, publication in publications],
                'nativeWorldFirstDifference': difference,
                'frame': state['frame'],
                'scope': ('Observed metadata/current absence only; historical native removal events are not '
                          'inferred.'),
            }
            summary.setdefault('registryObservations', []).append(proof)
            if difference is not None:
                raise RuntimeError('Registry observation changed native world state: '+json.dumps(difference))
        captured = registry_evidence.capture(state, native)
        if captured:
            summary.setdefault('registryPhysicalContainers', []).extend(captured)
        return state, native

    def seed_registry_history():
        if registry_evidence is None or args.registry_history_source is None:
            return
        history_rows = json.loads((args.registry_history_source/'observations.json').read_text())
        candidates = []
        for row in history_rows:
            match = re.fullmatch(r'repeat-(\d+)-restored', row.get('label', ''))
            if match:
                candidates.append((int(match.group(1)), row['response']))
        if not candidates:
            raise ValueError('Registry history source has no completed restored boundary.')
        number, history_state = max(candidates, key=lambda pair: pair[0])
        history_native = take(history_rows, 'repeat-'+str(number)+'-restored-native')
        require_native_boundary(history_native)
        captured = registry_evidence.capture(history_state, history_native)
        if not captured:
            raise ValueError('Registry history restored boundary contained no dynamic proxy identities.')
        summary['registryHistorySeed'] = {'repeat': number, 'frame': history_state['frame'],
                                          'captured': captured, 'nativeStateChanged': False}

    try:
        bridge, host = Client(17636), ControllerClient(17637)
        initial = settled('initial')
        initial_native = native_observation('initial-native')
        require_native_boundary(initial_native)
        seed_registry_history()
        initial, initial_native = reconcile_registry(initial, initial_native, 'initial')
        current = endpoint_comparison(initial_replay, initial_replay_native, initial, initial_native,
                                      initial_fixed_entity_ids, initial_recording, checkpoint_frame)
        current['rawNativePhysicsEqual'] = exact_values(initial_replay_native['bridge']['nativePhysics'],
                                                        initial_native['bridge']['nativePhysics'])
        summary['initialEndpointComparison'] = current
        if not current['passed'] or not current['rawNativePhysicsEqual']:
            raise RuntimeError('Live state is not the exact source probe endpoint.')
        call('bridge', {'command': 'pause'}, 'initial-fence')
        actor = call('bridge', {'command': 'hot-call', 'slot': args.actor_rebuild_slot,
                                'operation': 'status', 'args': {}}, 'initial-contact-pool')['detail']['result']
        checkpoint_sidecar = call('bridge', {'command': 'hot-call', 'slot': args.actor_rebuild_slot,
                                             'operation': 'checkpoint-status',
                                             'args': {'frame': checkpoint_frame}},
                                  'initial-checkpoint-sidecar')['detail']['result'].get('result', {})
        if actor.get('automaticContactPoolRestore') is not True or \
                checkpoint_sidecar.get('captured') is not True or \
                checkpoint_sidecar.get('coreSnapshotMatches') is not True:
            raise RuntimeError('Matching persistent contact-pool checkpoint is unavailable.')
        if source_summary.get('transformDispatchRestored') is True and \
                checkpoint_sidecar.get('transformDispatchCaptured') is not True:
            raise RuntimeError('Matching persistent Transform-dispatch checkpoint is unavailable.')
        summary['initialCheckpointSidecar'] = checkpoint_sidecar
        initial_animator = call('bridge', {'command': 'hot-call', 'slot': 'chef-animator-checkpoint',
                                           'operation': 'status', 'args': {}},
                                'initial-animator')['detail']['result']
        if initial_animator.get('chefRandomizeMode') != 'Record':
            raise RuntimeError('Initial Animator callback lifecycle is not quiescent Record mode.')
        initial_rebuilds = actor.get('rebuilds')
        initial_restores = actor.get('contactPoolRestores')
        previous_restore_attempt = call('bridge', {'command': 'status'}, 'initial-bridge-status')['bridge']['nativeCheckpoints']['restoreAttempts']
        repeats = []
        expected_outcome = source_summary.get('originalRequestedOutcome', {}).get('expected')
        for number in range(1, args.repeats + 1):
            prefix = 'repeat-' + str(number)
            call('bridge', {'command': 'arm'}, prefix+'-warp-arm')
            call('controller', {'command': 'warp', 'frame': checkpoint_frame, 'development': True}, prefix+'-warp')
            restored = settled(prefix+'-restored')
            native_status = call('bridge', {'command': 'status'}, prefix+'-restore-status')['bridge']['nativeCheckpoints']
            restore = native_status.get('lastRestore')
            if not restore or restore.get('verified') is not True or restore.get('frame') != checkpoint_frame or \
                    native_status['restoreAttempts'] <= previous_restore_attempt:
                raise RuntimeError(prefix + ' lacks a new verified native restore.')
            previous_restore_attempt = native_status['restoreAttempts']
            restored_native = native_observation(prefix+'-restored-native')
            require_native_boundary(restored_native)
            baseline = baseline_comparison(base, base_native, restored, restored_native,
                                           fixed_entity_ids, checkpoint_frame)
            if not baseline['passed']:
                raise RuntimeError(prefix + ' restored a different checkpoint boundary.')
            if registry_evidence is not None:
                summary.setdefault('registryEvidenceRebranches', []).append(
                    registry_evidence.rebranch_after_verified_warp(restored, restored_native))
            call('bridge', {'command': 'pause'}, prefix+'-pool-scheduled-fence')
            scheduled = call('bridge', {'command': 'hot-call', 'slot': args.actor_rebuild_slot,
                                        'operation': 'status', 'args': {}}, prefix+'-pool-scheduled')['detail']['result']
            if scheduled.get('automaticRestorePending') is not True:
                raise RuntimeError(prefix + ' did not schedule the contact-pool restore.')
            call('bridge', {'command': 'arm'}, prefix+'-replay-arm')
            call('controller', {'command': 'raw-replay', 'recording': recording}, prefix+'-replay')
            replay = settled(prefix+'-replayed')
            require_recorded_completion(replay, recording, checkpoint_frame)
            call('bridge', {'command': 'pause'}, prefix+'-animator-status-fence')
            animator = call('bridge', {'command': 'hot-call', 'slot': 'chef-animator-checkpoint',
                                       'operation': 'status', 'args': {}},
                            prefix+'-animator-post-replay')['detail']['result']
            animator_failures = {name: animator.get(name) for name in animator_failure_fields
                                 if animator.get(name) is not None}
            animator_parity = {
                'passed': not animator_failures,
                'failures': animator_failures,
                'modeBeforeBranchCommit': animator.get('chefRandomizeMode'),
                'lastResumeCompletion': animator.get('lastResumeCompletion'),
                'lastResumeCoordination': animator.get('lastResumeCoordination'),
            }
            if animator_failures:
                raise RuntimeError(prefix+' Animator semantic replay parity failed: '+json.dumps(animator_failures))
            replay_native = native_observation(prefix+'-replayed-native')
            require_native_boundary(replay_native)
            replay, replay_native = reconcile_registry(replay, replay_native, prefix+'-replayed')
            call('bridge', {'command': 'pause'}, prefix+'-pool-restored-fence')
            pool = call('bridge', {'command': 'hot-call', 'slot': args.actor_rebuild_slot,
                                   'operation': 'status', 'args': {}}, prefix+'-pool-restored')['detail']['result']
            endpoint = endpoint_comparison(expected, expected_native, replay, replay_native,
                                           fixed_entity_ids, recording, checkpoint_frame)
            outcome = pickup_outcome(base, base_native, replay, replay_native, expected_outcome) \
                if expected_outcome else None
            endpoint_ready = endpoint['passed'] and (outcome is None or outcome['achieved'])
            if not endpoint_ready:
                raise RuntimeError(prefix + ' endpoint differs before Animator branch commit.')
            if animator.get('chefRandomizeMode') == 'ReplayActive':
                commit = call('bridge', {'command': 'hot-call', 'slot': 'chef-animator-checkpoint',
                                         'operation': 'commit-replay-prefix',
                                         'args': {'frame': replay['frame']}},
                              prefix+'-animator-branch-commit')['detail']['result']
                committed = settled(prefix+'-branch-committed')
                committed_native = native_observation(prefix+'-branch-committed-native')
                require_native_boundary(committed_native)
                no_mutation = endpoint_comparison(replay, replay_native, committed, committed_native,
                                                  fixed_entity_ids, recording, checkpoint_frame)
                no_mutation['rawNativePhysicsEqual'] = exact_values(
                    replay_native['bridge']['nativePhysics'], committed_native['bridge']['nativePhysics'])
                animator_parity['branchCommit'] = commit.get('lastReplayPrefixCommit')
                animator_parity['branchCommitNoMutation'] = no_mutation
                animator_parity['modeAfterBranchCommit'] = commit.get('chefRandomizeMode')
                receipt = commit.get('lastReplayPrefixCommit') or {}
                receipt_exact = (receipt.get('exact') is True and
                                 receipt.get('gameStateMutation') is False and
                                 receipt.get('frame') == replay['frame'] and
                                 receipt.get('priorMode') == 'ReplayActive' and
                                 receipt.get('resultMode') == 'Record' and
                                 receipt.get('verifiedPrefixFrames') == animator.get('replayFrameComparisons') and
                                 receipt.get('retainedReferenceFramesBefore') == animator.get('replayReferenceFrames') and
                                 receipt.get('discardedFutureFrames') ==
                                 animator.get('replayReferenceFrames') - animator.get('replayFrameComparisons') and
                                 receipt.get('callbackCount') == animator.get('replayChefRandomizeCursor') and
                                 receipt.get('discardedCallbackTail') ==
                                 animator.get('replayChefRandomizeLimit') - animator.get('replayChefRandomizeCursor') and
                                 commit.get('chefRandomizeReferenceCount') == commit.get('replayChefRandomizeCursor') ==
                                 commit.get('replayChefRandomizeLimit'))
                animator_parity['branchCommitReceiptExact'] = receipt_exact
                if commit.get('chefRandomizeMode') != 'Record' or \
                        commit.get('replayReferenceFrames') != 0 or \
                        not receipt_exact or not no_mutation['passed'] or not no_mutation['rawNativePhysicsEqual']:
                    raise RuntimeError(prefix+' Animator branch-prefix commit was not exact and quiescent.')
            elif animator.get('chefRandomizeMode') == 'Record':
                animator_parity['branchCommit'] = None
                animator_parity['branchCommitNoMutation'] = None
                animator_parity['modeAfterBranchCommit'] = 'Record'
            else:
                raise RuntimeError(prefix+' Animator callback lifecycle did not reach a committable state.')
            repeat = {'number': number, 'nativeRestore': restore, 'baselineComparison': baseline,
                      'endpointComparison': endpoint, 'requestedOutcome': outcome,
                      'animatorSemanticParity': animator_parity,
                      'contactPoolRestores': pool.get('contactPoolRestores'),
                      'actorRebuilds': pool.get('rebuilds')}
            repeat['passed'] = (endpoint_ready and animator_parity['modeAfterBranchCommit'] == 'Record' and
                                pool.get('automaticRestorePending') is False and
                                pool.get('pendingContactPoolAction') == 'none' and
                                pool.get('contactPoolRestores') == initial_restores + number and
                                pool.get('rebuilds') == initial_rebuilds)
            repeats.append(repeat)
            if not repeat['passed']:
                raise RuntimeError(prefix + ' endpoint or pool accounting differs.')
        summary.update(additionalReplays=repeats, initialActorRebuilds=initial_rebuilds,
                       finalActorRebuilds=repeats[-1]['actorRebuilds'], passed=True)
    except Exception as error:
        summary['error'] = str(error)
    finally:
        try:
            if bridge is not None:
                call('bridge', {'command': 'pause'}, 'finally-pause')
        except Exception as error:
            summary['pauseError'] = str(error)
            summary['passed'] = False
        for client in (bridge, host):
            try:
                if client is not None:
                    client.close()
            except Exception as error:
                summary['closeError'] = str(error)
                summary['passed'] = False
        (args.out/'observations.json').write_text(json.dumps(evidence, indent=2))
        (args.out/'summary.json').write_text(json.dumps(summary, indent=2))
        print(json.dumps(summary, indent=2))
    return 0 if summary['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
