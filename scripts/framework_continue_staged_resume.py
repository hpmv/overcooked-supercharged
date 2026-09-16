"""Continue a probe that stopped after a verified warp while staged resume is pending."""
import argparse
import json
from pathlib import Path
import time

from framework_input_probe import native_physics_comparison
from framework_native_search import (exact_values, food_trees, gameplay_round,
                                     native_clock_state, require_native_boundary,
                                     require_recorded_completion)
from framework_pause_boundary import observe_settled_pause
from framework_rpc import Client, ControllerClient


def take(rows, label):
    matches = [row['response'] for row in rows if row.get('label') == label]
    if not matches:
        raise ValueError('Source probe lacks ' + label)
    return matches[-1]


def take_any(rows, *labels):
    for label in labels:
        matches = [row['response'] for row in rows if row.get('label') == label]
        if matches:
            return matches[-1]
    raise ValueError('Source probe lacks any of: ' + ', '.join(labels))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--actor-rebuild-slot', default='rigidbody-actor-rebuild')
    parser.add_argument('--commit-replay-prefix', action='store_true',
                        help=('Reject one wrong staged boundary, then commit the exact restored target before '
                              'the first resume. The supplied recording is subsequently treated as a new branch.'))
    args = parser.parse_args()
    if args.out.exists():
        parser.error('Output already exists.')

    rows = json.loads((args.source/'observations.json').read_text())
    recording = json.loads((args.source/'recording.json').read_text())
    # A warmup probe checkpoints its ``base`` boundary; a zero-warmup probe
    # reuses ``initial`` and therefore has no separate base observation.
    baseline = take_any(rows, 'base', 'initial')
    baseline_native = take(rows, 'base-native')
    expected = take(rows, 'original')
    expected_native = take(rows, 'original-native')
    checkpoint_frame = baseline['frame']
    expected_frame = checkpoint_frame + len(recording['frames'])
    fixed_entity_ids = {entity['id'] for entity in baseline['entities']}
    report = {'passed': False, 'source': str(args.source.resolve()),
              'checkpointFrame': checkpoint_frame, 'expectedFrame': expected_frame,
              'recordingSha256': recording['sha256'], 'records': []}
    bridge = host = None
    began = time.monotonic()

    def call(target, request, label):
        response = (bridge if target == 'bridge' else host).call(request)
        report['records'].append({'label': label, 'target': target, 'request': request,
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
            state = host.call({'command': 'status'})
            if state['state'] != 'Paused' or state['requestPending'] or state['errors']:
                raise RuntimeError('Native observation requires a settled pause.')
            return state['frame']
        return observe_settled_pause(lambda: call('bridge', {'command': 'food'}, label), read_frame)['receipt']

    def compare_paused(left, left_native, right, right_native, allow_reincarnation=False):
        left_entities = {entity['id']: entity for entity in left['entities']}
        right_entities = {entity['id']: entity for entity in right['entities']}
        changed = sorted(identity for identity in left_entities.keys() | right_entities.keys()
                         if not exact_values(left_entities.get(identity), right_entities.get(identity)))
        physics = native_physics_comparison(left_native['bridge']['nativePhysics'],
                                            right_native['bridge']['nativePhysics'], fixed_entity_ids,
                                            right_native if allow_reincarnation else None,
                                            checkpoint_frame if allow_reincarnation else None)
        result = {
            'frameEqual': left['frame'] == right['frame'],
            'changedEntityIds': changed,
            'nativeRoundEqual': exact_values(gameplay_round(left_native['bridge']['nativeRound']),
                                             gameplay_round(right_native['bridge']['nativeRound'])),
            'nativeFoodEqual': exact_values(food_trees(left_native), food_trees(right_native)),
            'nativePhysicsEqual': physics['equal'],
            'nativePhysicsComparison': physics,
            'rawNativePhysicsEqual': exact_values(left_native['bridge']['nativePhysics'],
                                                  right_native['bridge']['nativePhysics']),
            'nativeClocksEqual': exact_values(native_clock_state(left_native['bridge']),
                                              native_clock_state(right_native['bridge'])),
        }
        result['passed'] = all((result['frameEqual'], not changed, result['nativeRoundEqual'],
                                result['nativeFoodEqual'], result['nativePhysicsEqual'],
                                result['rawNativePhysicsEqual'] or allow_reincarnation,
                                result['nativeClocksEqual']))
        return result

    try:
        bridge, host = Client(17636), ControllerClient(17637)
        initial = settled('initial')
        if initial['frame'] != checkpoint_frame:
            raise RuntimeError('Live controller is not at the source checkpoint.')
        call('bridge', {'command': 'pause'}, 'initial-fence')
        native_status = call('bridge', {'command': 'status'}, 'initial-native-status')['bridge']['nativeCheckpoints']
        restore = native_status.get('lastRestore')
        if not restore or restore.get('verified') is not True or restore.get('frame') != checkpoint_frame:
            raise RuntimeError('Live checkpoint lacks a verified native restore.')
        animator_before = call('bridge', {'command': 'hot-call', 'slot': 'chef-animator-checkpoint',
                               'operation': 'status', 'args': {}}, 'animator-before')['detail']['result']
        if animator_before.get('pendingResumeRestore') is not True or \
                animator_before.get('resumeFrame') != checkpoint_frame or animator_before.get('failure') is not None:
            raise RuntimeError('Expected a clean staged Animator resume at the checkpoint.')
        actor_before = call('bridge', {'command': 'hot-call', 'slot': args.actor_rebuild_slot,
                            'operation': 'status', 'args': {}}, 'actor-before')['detail']['result']
        if actor_before.get('automaticRestorePending') is not True or \
                actor_before.get('contactPoolSnapshotFrame') != checkpoint_frame:
            raise RuntimeError('Expected a matching pending contact-pool restore.')

        staged_native = native_observation('staged-native-before-commit')
        require_native_boundary(staged_native)
        restored_comparison = compare_paused(baseline, baseline_native, initial, staged_native, True)
        report['restoredBaselineComparison'] = restored_comparison
        if not restored_comparison['passed']:
            raise RuntimeError('Live staged target differs from the source checkpoint baseline.')

        commit_ok = True
        if args.commit_replay_prefix:
            # Prove a nearby but wrong controller boundary is rejected without
            # consuming or damaging the staged transaction.
            wrong_request = {'command': 'hot-call', 'slot': 'chef-animator-checkpoint',
                             'operation': 'commit-replay-prefix',
                             'args': {'frame': checkpoint_frame + 1}}
            try:
                wrong_response = bridge.call(wrong_request)
            except RuntimeError as error:
                wrong_response = json.loads(str(error))
            else:
                raise RuntimeError('Wrong-frame staged Animator commit unexpectedly succeeded.')
            report['records'].append({'label': 'wrong-frame-commit', 'target': 'bridge',
                                      'request': wrong_request, 'response': wrong_response,
                                      'wallSeconds': time.monotonic()-began})
            wrong_state = settled('after-wrong-frame-commit')
            wrong_native = native_observation('after-wrong-frame-commit-native')
            wrong_no_mutation = compare_paused(initial, staged_native, wrong_state, wrong_native)
            wrong_animator = call('bridge', {'command': 'hot-call', 'slot': 'chef-animator-checkpoint',
                                   'operation': 'status', 'args': {}}, 'after-wrong-frame-animator')['detail']['result']
            wrong_rejected = (wrong_response.get('ok') is False and wrong_no_mutation['passed'] and
                              wrong_animator.get('chefRandomizeMode') == 'ReplayStaged' and
                              wrong_animator.get('pendingResumeRestore') is True and
                              wrong_animator.get('resumeFrame') == checkpoint_frame and
                              wrong_animator.get('replayReferenceFrames') == animator_before.get('replayReferenceFrames'))
            report['wrongFrameCommit'] = {'rejected': wrong_response.get('ok') is False,
                                          'error': wrong_response.get('error'),
                                          'noMutation': wrong_no_mutation,
                                          'stagedTransactionIntact': wrong_rejected}
            if not wrong_rejected:
                raise RuntimeError('Wrong-frame staged commit rejection changed the transaction.')

            committed = call('bridge', {'command': 'hot-call', 'slot': 'chef-animator-checkpoint',
                              'operation': 'commit-replay-prefix', 'args': {'frame': checkpoint_frame}},
                             'commit-replay-prefix')['detail']['result']
            committed_state = settled('after-commit')
            committed_native = native_observation('after-commit-native')
            commit_no_mutation = compare_paused(initial, staged_native, committed_state, committed_native)
            receipt = committed.get('lastReplayPrefixCommit') or {}
            receipt_exact = (receipt.get('exact') is True and receipt.get('gameStateMutation') is False and
                             receipt.get('frame') == checkpoint_frame and
                             receipt.get('priorMode') == 'ReplayStaged' and
                             receipt.get('resultMode') == 'ReplayStaged' and
                             receipt.get('verifiedPrefixFrames') == 0 and
                             receipt.get('retainedReferenceFramesBefore') == animator_before.get('replayReferenceFrames') and
                             receipt.get('discardedFutureFrames') == animator_before.get('replayReferenceFrames') and
                             receipt.get('callbackCount') == animator_before.get('replayChefRandomizeCursor') and
                             receipt.get('discardedCallbackTail') ==
                             animator_before.get('chefRandomizeReferenceCount') - animator_before.get('replayChefRandomizeCursor') and
                             receipt.get('resumePrefixAvailableAtCommit') is True and
                             receipt.get('resumePrefixPolicy') == 'existing-exact-linked-prefix')
            commit_ok = (receipt_exact and commit_no_mutation['passed'] and
                         committed.get('chefRandomizeMode') == 'ReplayStaged' and
                         committed.get('replayReferenceFrames') == 0 and
                         committed.get('pendingResumeRestore') is True and
                         committed.get('resumeRestoreStage') == 1)
            report['stagedCommit'] = {'receipt': receipt, 'receiptExact': receipt_exact,
                                      'noMutation': commit_no_mutation,
                                      'pendingResumeIntact': commit_ok}
            if not commit_ok:
                raise RuntimeError('Exact staged Animator branch-prefix commit was not safe and complete.')

        call('bridge', {'command': 'arm'}, 'replay-arm')
        call('controller', {'command': 'raw-replay', 'recording': recording}, 'replay')
        replay = settled('replayed')
        require_recorded_completion(replay, recording, checkpoint_frame)
        call('bridge', {'command': 'pause'}, 'replayed-fence')
        animator_after = call('bridge', {'command': 'hot-call', 'slot': 'chef-animator-checkpoint',
                              'operation': 'status', 'args': {}}, 'animator-after')['detail']['result']
        failure_fields = ('failure', 'resumeFailure', 'resumePrefixObserverFailure',
                          'chefRandomizeFailure', 'firstReplayDifference',
                          'firstRandomStateReplayDifference', 'firstChefRandomizeReplayDifference',
                          'firstControllerInputReplayDifference', 'firstTransitionTopologyReplayDifference',
                          'firstMixerGraphReplayDifference', 'firstOwnerGraphReplayDifference',
                          'firstResumePrefixPostDifference', 'firstResumePrefixPostOwnerGraphDifference')
        animator_failures = {name: animator_after.get(name) for name in failure_fields
                             if animator_after.get(name) is not None}
        if animator_after.get('pendingResumeRestore') or animator_failures or \
                (args.commit_replay_prefix and animator_after.get('chefRandomizeMode') != 'Record'):
            raise RuntimeError('Animator staged resume did not complete cleanly: ' + json.dumps(animator_failures))
        replay_native = native_observation('replayed-native')
        require_native_boundary(replay_native)
        actor_after = call('bridge', {'command': 'hot-call', 'slot': args.actor_rebuild_slot,
                           'operation': 'status', 'args': {}}, 'actor-after')['detail']['result']

        left = {entity['id']: entity for entity in expected['entities']}
        right = {entity['id']: entity for entity in replay['entities']}
        changed = sorted(identity for identity in left.keys() | right.keys()
                         if not exact_values(left.get(identity), right.get(identity)))
        physics = native_physics_comparison(expected_native['bridge']['nativePhysics'],
                                            replay_native['bridge']['nativePhysics'], fixed_entity_ids,
                                            replay_native, checkpoint_frame)
        comparison = {
            'frameEqual': replay['frame'] == expected_frame == expected['frame'],
            'changedEntityIds': changed,
            'nativeRoundEqual': exact_values(gameplay_round(expected_native['bridge']['nativeRound']),
                                             gameplay_round(replay_native['bridge']['nativeRound'])),
            'nativeFoodEqual': exact_values(food_trees(expected_native), food_trees(replay_native)),
            'nativePhysicsEqual': physics['equal'],
            'nativePhysicsComparison': physics,
            'nativeClocksEqual': exact_values(native_clock_state(expected_native['bridge']),
                                              native_clock_state(replay_native['bridge'])),
            'recordingEqual': replay['rawInput'].get('recordingSha256') == recording['sha256'],
        }
        comparison['passed'] = all((comparison['frameEqual'], not changed,
                                    comparison['nativeRoundEqual'], comparison['nativeFoodEqual'],
                                    comparison['nativePhysicsEqual'], comparison['nativeClocksEqual'],
                                    comparison['recordingEqual']))
        pool_ok = actor_after.get('automaticRestorePending') is False and \
            actor_after.get('contactPoolRestores') == actor_before.get('contactPoolRestores') + 1 and \
            actor_after.get('transformDispatchRestores') == actor_before.get('transformDispatchRestores') + 1
        report.update(animatorBefore={key: animator_before.get(key) for key in
                                     ('resumeFrame', 'resumeRestoreStage', 'pendingResumeRestore')},
                      animatorAfter={key: animator_after.get(key) for key in
                                    ('resumeFrame', 'resumeRestoreStage', 'pendingResumeRestore',
                                     'lastResumeCoordination', 'lastResumeCompletion',
                                     'lastOverrideClipRestore', 'lastTargetNullClipRestore',
                                     'lastPlayableTimeRestore')},
                      animatorFailures=animator_failures, endpointComparison=comparison,
                      contactPoolRestored=pool_ok, passed=comparison['passed'] and pool_ok and commit_ok)
    except Exception as error:
        report['error'] = str(error)
    finally:
        try:
            if bridge is not None:
                call('bridge', {'command': 'pause'}, 'finally-pause')
        except Exception as error:
            report['pauseError'] = str(error)
            report['passed'] = False
        for client in (bridge, host):
            try:
                if client is not None:
                    client.close()
            except Exception as error:
                report['closeError'] = str(error)
                report['passed'] = False
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(report, indent=2))
        print(json.dumps({key: value for key, value in report.items() if key != 'records'}, indent=2))
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
