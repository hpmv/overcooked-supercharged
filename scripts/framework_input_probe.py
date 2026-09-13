"""Bounded native four-pad input recording and replay probe; authoring only."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import re
import time
from compare_framework_frames import first_difference
from framework_rpc import Client, ControllerClient
from framework_pause_boundary import observe_settled_pause, PauseBoundaryError
from framework_native_search import (boundary_matches, compare_boundary, exact_values, food_trees, gameplay_round,
                                    require_native_boundary, require_recorded_completion, native_clock_state)
from framework_story11_registry import RegistryEvidence, world_proof


def load_prefix_requests(path):
    """Load raw-input prefixes, expanding request or exact-capture entries in order."""
    values = json.loads(path.read_text(encoding='utf-8-sig'))
    if not isinstance(values, list) or not values:
        raise ValueError('--prefix-inputs must contain a nonempty JSON array.')
    # A prior probe's observations are also an exact, hashable description of
    # its setup branch.  Reuse only the numbered raw-input prefix requests;
    # every other observation remains evidence and is deliberately ignored.
    observed = []
    for value in values:
        match = re.fullmatch(r'prefix-(\d+)-input', value.get('label', '')) if isinstance(value, dict) else None
        if match:
            observed.append((int(match.group(1)), value.get('request')))
    if observed:
        observed.sort(key=lambda item: item[0])
        if [index for index, _ in observed] != list(range(len(observed))):
            raise ValueError('Prefix observations must be one contiguous zero-based sequence.')
        values = [request for _, request in observed]
    expanded = []
    for index, value in enumerate(values):
        if isinstance(value, dict) and set(value) == {'capture'}:
            capture = value['capture']
            if not isinstance(capture, str) or not capture:
                raise ValueError(f'Prefix capture {index} must name a JSON file.')
            capture_path = (path.parent / capture).resolve()
            captured = json.loads(capture_path.read_text(encoding='utf-8-sig'))
            rows = captured.get('inputs', []) if isinstance(captured, dict) else []
            if captured.get('validation') != 'exact-four-pad-frame-coverage' or len(rows) < 3 or any(
                    row.get('nextFrame') != rows[0].get('nextFrame') + row_index
                    for row_index, row in enumerate(rows)):
                raise ValueError(f'Prefix capture {index} is not an exact contiguous four-pad recording.')
            def neutral(row):
                return all(input_state['Pad']['X'] == 0 and input_state['Pad']['Y'] == 0 and
                           not any(input_state[button]['Down'] for button in ('Pickup', 'Interact', 'Dash'))
                           for input_state in row['inputs'].values())
            if not neutral(rows[-2]) or not neutral(rows[-1]):
                raise ValueError(f'Prefix capture {index} lacks its two terminal neutral release frames.')
            payload = rows[:-2]
            expanded.append({'command': 'raw-input', 'segments': [
                {'frames': 1, 'chefs': {
                    chef: {
                        'x': input_state['Pad']['X'], 'y': input_state['Pad']['Y'],
                        'pickup': input_state['Pickup']['Down'],
                        'interact': input_state['Interact']['Down'],
                        'dash': input_state['Dash']['Down'],
                    } for chef, input_state in row['inputs'].items()
                }} for row in payload
            ]})
            continue
        if isinstance(value, dict) and set(value) == {'include'}:
            include = value['include']
            if not isinstance(include, str) or not include:
                raise ValueError(f'Prefix include {index} must name a JSON file.')
            include_path = (path.parent / include).resolve()
            included = json.loads(include_path.read_text(encoding='utf-8-sig'))
            if isinstance(included, dict):
                included = [included]
            if not isinstance(included, list) or not included:
                raise ValueError(f'Prefix include {index} must contain a raw-input object or nonempty array.')
            if any(isinstance(entry, dict) and 'include' in entry for entry in included):
                raise ValueError(f'Nested prefix includes are not supported: {include_path}')
            expanded.extend(included)
        else:
            expanded.append(value)
    return expanded


def pickup_outcome(base, base_native, end, end_native, expected):
    """Verify the requested fixed-item transfer, independently of replay equality."""
    if expected.get('type') != 'pickup' or any(type(expected.get(k)) is not int for k in ('chef', 'item', 'source')):
        raise ValueError('Expected outcome requires pickup with integer chef, item and source.')
    chef, item, source = (expected[k] for k in ('chef', 'item', 'source'))
    if len({chef, item, source}) != 3:
        raise ValueError('Pickup outcome identities must be distinct.')
    before = {e['id']: e for e in base['entities']}
    after = {e['id']: e for e in end['entities']}
    def path(entities, identity, field):
        entity = entities.get(identity)
        return None if entity is None else entity.get('data', {}).get(field, {}).get('path')
    def food(receipt):
        return {e['id']: e for e in receipt['detail']['entities']}
    def physics(receipt):
        return {body['entityId']: body for body in receipt['bridge']['nativePhysics']['bodies']}
    old_food, new_food = food(base_native), food(end_native)
    old_physics, new_physics = physics(base_native), physics(end_native)
    source_survived = source in after
    controller_source_witness = source in before and \
        path(before, item, 'attachmentParent') == before[source].get('path') and \
        path(before, source, 'attachment') == before[item].get('path')
    native_proxy_witness = source not in before and item in old_physics and source in old_physics and \
        old_physics[item].get('bodyInstanceId') == old_physics[source].get('bodyInstanceId')
    final_controller_source = controller_source_witness and source_survived and not path(after, source, 'attachment')
    final_retired_controller_source = controller_source_witness and not source_survived and source not in new_physics
    final_native_proxy = native_proxy_witness and item not in new_physics and source in new_physics
    checks = {
        'samePersistentIdentities': all(i in before and i in after and before[i]['path'] == after[i]['path']
                                        for i in (chef, item)),
        'initialSourceWitness': controller_source_witness or native_proxy_witness,
        'initialChefEmpty': not path(before, chef, 'attachment'),
        'finalItemOnChef': path(after, item, 'attachmentParent') == before[chef]['path'],
        'finalChefHoldsItem': path(after, chef, 'attachment') == before[item]['path'],
        'finalSourceReleasedItem': final_controller_source or final_retired_controller_source or final_native_proxy,
        'nativeItemCompositionPresentAndEqual': item in old_food and item in new_food
            and old_food[item].get('composition') is not None
            and exact_values(old_food[item]['composition'], new_food[item].get('composition')),
    }
    if controller_source_witness:
        source_lifecycle = 'controller-source-survived-empty' if source_survived else 'controller-source-retired'
    elif native_proxy_witness:
        source_lifecycle = 'native-proxy-body-survived-item-alias-retired'
    else:
        source_lifecycle = 'unproved'
    return {'expected': expected, 'achieved': all(checks.values()), 'checks': checks,
            'sourceLifecycle': source_lifecycle,
            'scope': 'Observed a controller attachment source or an exact native item/proxy Rigidbody alias, then the item attached two-sided to the chef while that source released the item, with native composition preserved; replay equality is tested separately.'}


def delivery_outcome(base_native, end_native):
    """Prove one ordinary native delivery/score transition, independently of replay equality."""
    before = base_native['bridge']['nativeRound']
    after = end_native['bridge']['nativeRound']
    old_ledger, new_ledger = before['ledger'], after['ledger']
    old_orders, new_orders = before['orders'], after['orders']
    if not old_orders:
        raise ValueError('Delivery outcome requires a native head order at the checkpoint.')
    head = old_orders[0]
    old_ids = [order['id'] for order in old_orders]
    new_ids = [order['id'] for order in new_orders]
    foods = base_native['detail']['entities']
    plated = [entity for entity in foods
              if (entity.get('composition') or {}).get('children')]
    checks = {
        'initialLedgerUndelivered': old_ledger['deliveries'] >= 0,
        'initialPlatedCompositionPresent': len(plated) == 1,
        'exactlyOneDeliveryAdded': new_ledger['deliveries'] == old_ledger['deliveries'] + 1,
        'positiveScoreAdded': new_ledger['total'] > old_ledger['total']
            and new_ledger['baseScore'] >= old_ledger['baseScore'] + head['baseValue'],
        'deductionsUnchanged': new_ledger['deductions'] == old_ledger['deductions'],
        'headOrderConsumed': head['id'] in old_ids and head['id'] not in new_ids,
        'otherExistingOrdersPreservedInOrder': [identity for identity in old_ids if identity != head['id']]
            == [identity for identity in new_ids if identity in old_ids],
    }
    return {
        'achieved': all(checks.values()),
        'checks': checks,
        'headOrder': head,
        'platedEntity': plated[0] if len(plated) == 1 else None,
        'ledgerBefore': old_ledger,
        'ledgerAfter': new_ledger,
        'orderIdsBefore': old_ids,
        'orderIdsAfter': new_ids,
        'scope': ('Observed native order/ledger/food state only: one head order was consumed, exactly one '
                  'delivery and positive score were added, deductions were unchanged, and replay equality '
                  'is tested separately.'),
    }


def verified_initial_fixed_body_reincarnations(original, replay, fixed_entity_ids,
                                               native_observation, checkpoint_frame):
    """Derive the one fixed body reincarnation mutually proved by native restore receipts."""
    fixed = set(fixed_entity_ids)
    if type(checkpoint_frame) is not int or not isinstance(native_observation, dict):
        return set()
    bridge = native_observation.get('bridge')
    checkpoints = bridge.get('nativeCheckpoints') if isinstance(bridge, dict) else None
    if not isinstance(checkpoints, dict):
        return set()
    last_restore = checkpoints.get('lastRestore')
    transaction = checkpoints.get('nativeDynamicWarp')
    restore_attempts = checkpoints.get('restoreAttempts')
    if not isinstance(last_restore, dict) or not isinstance(transaction, dict) or \
            last_restore.get('verified') is not True or transaction.get('verified') is not True or \
            last_restore.get('frame') != checkpoint_frame or \
            type(last_restore.get('attempt')) is not int or \
            last_restore.get('attempt') != restore_attempts:
        return set()
    recreation = last_restore.get('nativeInitialAttachmentRecreation')
    if not isinstance(recreation, dict) or any(recreation.get(field) is not True for field in (
            'attachmentIdentityRebound', 'bodyIdentityRebound', 'identityRebound',
            'collidersRebound', 'rebound')):
        return set()
    owner, container, path = (recreation.get('ownerId'), recreation.get('containerId'),
                              recreation.get('path'))
    if type(owner) is not int or type(container) is not int or owner == container or \
            owner not in fixed or container not in fixed or not isinstance(path, list) or \
            not path or any(type(value) is not int for value in path):
        return set()
    spawned = transaction.get('spawned')
    if not isinstance(spawned, list):
        return set()
    matches = []
    for row in spawned:
        if not isinstance(row, dict) or type(row.get('id')) is not int or \
                type(row.get('containerId')) is not int:
            return set()
        exact_match = (row.get('id') == owner and row.get('containerId') == container and
                       row.get('path') == path and row.get('latentInitialFactory') is True and
                       row.get('retiredLatentIntermediatePhysicsPair') is True)
        if exact_match:
            matches.append(row)
        elif row.get('id') in fixed or row.get('containerId') in fixed:
            # No other fixed checkpoint entity may hide behind the transaction.
            return set()
    if len(matches) != 1:
        return set()
    try:
        old_bodies = [body for body in original['bodies'] if body.get('entityId') == container]
        new_bodies = [body for body in replay['bodies'] if body.get('entityId') == container]
    except (KeyError, TypeError):
        return set()
    if len(old_bodies) != 1 or len(new_bodies) != 1:
        return set()
    old_instance = old_bodies[0].get('bodyInstanceId')
    new_instance = new_bodies[0].get('bodyInstanceId')
    if type(old_instance) is not int or type(new_instance) is not int or \
            old_instance == 0 or new_instance == 0 or old_instance == new_instance:
        return set()
    return {container}


def native_physics_comparison(original, replay, fixed_entity_ids,
                              native_observation=None, checkpoint_frame=None):
    """Compare exact physics while excluding only proved reincarnation allocation handles."""
    fixed = set(fixed_entity_ids)
    if any(type(identity) is not int for identity in fixed):
        raise ValueError('Fixed physics entity IDs must be integers.')
    reincarnated_fixed = verified_initial_fixed_body_reincarnations(
        original, replay, fixed, native_observation, checkpoint_frame) \
        if native_observation is not None or checkpoint_frame is not None else set()
    left, right = copy.deepcopy(original), copy.deepcopy(replay)
    if not isinstance(left, dict) or not isinstance(right, dict):
        raise ValueError('Native physics observations must be objects.')
    incarnations = {'original': {}, 'replay': {}}
    fixed_incarnations = {'original': {}, 'replay': {}}
    for label, physics in (('original', left), ('replay', right)):
        bodies = physics.get('bodies')
        if not isinstance(bodies, list):
            raise ValueError('Native physics observation lacks a body list.')
        seen = set()
        for body in bodies:
            if not isinstance(body, dict) or type(body.get('entityId')) is not int:
                raise ValueError('Native physics body lacks an integer entity ID.')
            identity = body['entityId']
            if identity in seen:
                raise ValueError('Native physics observation contains duplicate entity IDs.')
            seen.add(identity)
            instance = body.get('bodyInstanceId')
            if type(instance) is not int or instance == 0:
                raise ValueError('Native physics body lacks a nonzero integer instance ID.')
            if identity not in fixed:
                incarnations[label][str(identity)] = instance
                body['bodyInstanceId'] = '<dynamic-recreated>'
            elif identity in reincarnated_fixed:
                fixed_incarnations[label][str(identity)] = instance
                body['bodyInstanceId'] = '<verified-fixed-reincarnation>'
    difference = first_difference(left, right, '$nativePhysics')
    changed = sorted(identity for identity in set(map(int, incarnations['original'])) &
                     set(map(int, incarnations['replay']))
                     if incarnations['original'][str(identity)] != incarnations['replay'][str(identity)])
    changed_fixed = sorted(identity for identity in set(map(int, fixed_incarnations['original'])) &
                           set(map(int, fixed_incarnations['replay']))
                           if fixed_incarnations['original'][str(identity)] !=
                           fixed_incarnations['replay'][str(identity)])
    return {
        'equal': difference is None,
        'firstDifference': difference,
        'dynamicIncarnations': incarnations,
        'changedDynamicIncarnationIds': changed,
        'verifiedFixedIncarnations': fixed_incarnations,
        'changedVerifiedFixedIncarnationIds': changed_fixed,
        'scope': ('Exact native physics comparison, including body order and logical entity IDs. '
                  'bodyInstanceId remains exact for checkpoint-fixed entities except the Rigidbody '
                  'mutually proved by the current restore and latent-initial-factory receipts; allocation '
                  'handles are also excluded for entities created after the checkpoint.'),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--warmup', type=int, default=30,
                        help='Neutral frames before the checkpoint; use 0 when already at the intended boundary.')
    parser.add_argument('--settle-timeout', type=float, default=20,
                        help=('Maximum seconds to wait for each fixed-input or replay request to reach its paused '
                              'release boundary. Increase this for long probes while the game is minimized.'))
    parser.add_argument('--frames', type=int, default=8)
    parser.add_argument('--x', type=float, default=-1)
    parser.add_argument('--y', type=float, default=0)
    parser.add_argument('--chef', type=int, default=103)
    parser.add_argument('--segments', type=Path, help='Explicit raw-input request JSON; all four pads per segment.')
    parser.add_argument('--prefix-inputs', type=Path,
                        help=('Optional JSON array of complete raw-input requests executed on this same persistent '
                              'bridge/controller connection before the checkpoint observation.'))
    parser.add_argument('--frame-capture', type=Path,
                        help='Exact four-pad capture; its two terminal neutral frames are replaced by raw-input release frames.')
    parser.add_argument('--frame-capture-payload', type=int,
                        help='Use only this many leading payload frames from --frame-capture.')
    parser.add_argument('--frame-capture-neutral-tail', type=int, default=0,
                        help='Append this many neutral payload frames after a frame capture and before its release frames.')
    parser.add_argument('--expect-pickup', type=int, nargs=2, metavar=('ITEM', 'SOURCE'),
                        help='Require the selected chef to transfer ITEM from SOURCE.')
    parser.add_argument('--expect-delivery', action='store_true',
                        help='Require one native head-order delivery with positive score and no deduction.')
    parser.add_argument('--restore-contact-manager-free-stack', action='store_true')
    parser.add_argument('--reuse-contact-manager-checkpoint', action='store_true',
                        help=('Start from a just-restored checkpoint whose matching contact/Transform sidecar is '
                              'already scheduled; do not replace either checkpoint before the original branch.'))
    parser.add_argument('--restore-transform-dispatch', action='store_true',
                        help=('Also restore Unity TransformChangeDispatch queue order and pending masks at the '
                              'checkpoint; requires --restore-contact-manager-free-stack.'))
    parser.add_argument('--actor-rebuild-slot', default='rigidbody-actor-rebuild')
    parser.add_argument('--inspect-animator-history', action='store_true',
                        help=('Capture the retained Chef Animator descriptor at the checkpoint, original endpoint, '
                              'and replay endpoint through the read-only inspector module.'))
    parser.add_argument('--animator-inspector-slot', default='animator-checkpoint-inspector')
    parser.add_argument('--arm-phase-trace-before-warp', action='store_true',
                        help='Arm the read-only physics-capture trace for this probe\'s first warp.')
    parser.add_argument('--mark-native-trace', action='store_true',
                        help='Write phase markers to the active native-physics-trace module.')
    parser.add_argument('--split-native-trace-around-warp', action='store_true',
                        help=('Export and deactivate the native trace before warp, then reactivate it after warp. '
                              'This permits pose-setter hooks whose exact entry bytes are verified by BodyRestore.'))
    parser.add_argument('--save-managed-phase-traces', action='store_true',
                        help=('Clear and label the active chef-managed-mutation-tracer at each branch boundary, '
                              'then save its original and replay receipts outside observations.json.'))
    parser.add_argument('--reconcile-dynamic-registry', action='store_true',
                        help=('Publish observation-only dynamic spawn metadata and current proxy-absence receipts '
                              'through the active registry-observer module. Native world state is compared before '
                              'and after every publication.'))
    parser.add_argument('--inspect-world-sync-cache', action='store_true',
                        help='Save the active world-sync cache status at the checkpoint boundary.')
    args = parser.parse_args()
    if args.segments and args.frame_capture:
        parser.error('Use either --segments or --frame-capture, not both.')
    if args.frame_capture_payload is not None and not args.frame_capture:
        parser.error('--frame-capture-payload requires --frame-capture.')
    if args.frame_capture_neutral_tail and not args.frame_capture:
        parser.error('--frame-capture-neutral-tail requires --frame-capture.')
    if args.split_native_trace_around_warp and not args.mark_native_trace:
        parser.error('--split-native-trace-around-warp requires --mark-native-trace.')
    if args.restore_transform_dispatch and not args.restore_contact_manager_free_stack:
        parser.error('--restore-transform-dispatch requires --restore-contact-manager-free-stack.')
    if args.reuse_contact_manager_checkpoint and not args.restore_contact_manager_free_stack:
        parser.error('--reuse-contact-manager-checkpoint requires --restore-contact-manager-free-stack.')
    if not 0 <= args.frame_capture_neutral_tail <= 36000:
        parser.error('Use 0..36000 appended neutral frames.')
    if not 1 <= args.frames <= 36000:
        parser.error('Use1..36000 payload frames; two release frames are additional.')
    if not 0 <= args.warmup <= 36000:
        parser.error('Use0..36000 neutral warmup frames.')
    if not 1 <= args.settle_timeout <= 600:
        parser.error('Use a settle timeout from 1 to 600 seconds.')
    args.out.mkdir(parents=True, exist_ok=False)
    evidence_key = hashlib.sha256(str(args.out.resolve()).encode('utf-8')).hexdigest()[:16]
    bridge = host = None
    evidence, summary = [], {'passed': False, 'classification': 'authoring input replay probe',
                             'contactManagerFreeStackRestored': args.restore_contact_manager_free_stack,
                             'transformDispatchRestored': args.restore_transform_dispatch}
    began = time.monotonic()
    contact_pool_start = None
    contact_pool_cleanup_needed = False
    split_trace_config = None
    registry_evidence = RegistryEvidence() if args.reconcile_dynamic_registry else None

    def call(target, request, label):
        result = (bridge if target == 'bridge' else host).call(request)
        evidence.append(dict(label=label, target=target, request=request, response=result, wallSeconds=time.monotonic()-began))
        return result

    def settled(label):
        deadline = time.monotonic() + args.settle_timeout
        while True:
            s = host.call({'command': 'status'})
            if s['errors'] or s['state'] == 'Error':
                raise RuntimeError(json.dumps(s))
            if s['state'] == 'Paused' and not s['requestPending']:
                return call('controller', {'command': 'inspect', 'full': True}, label)
            if time.monotonic() > deadline:
                raise TimeoutError(json.dumps(s))
            time.sleep(.025)

    def native_observation(label):
        def read_frame():
            state = host.call({'command': 'status'})
            if state['state'] != 'Paused' or state['requestPending'] or state['errors']:
                raise RuntimeError('Native pause observation requires a settled controller.')
            return state['frame']
        try:
            result = observe_settled_pause(lambda: call('bridge', {'command': 'food'}, label), read_frame)
        except PauseBoundaryError as error:
            (args.out/(label+'-pause-proof.json')).write_text(json.dumps(error.report,indent=2))
            raise
        (args.out/(label+'-pause-proof.json')).write_text(json.dumps(result['proof'],indent=2))
        return result['receipt']

    def mark_native_trace(code, frame, label):
        if args.mark_native_trace:
            call('bridge', {'command': 'pause'}, label + '-fence')
            call('bridge', {'command': 'hot-call', 'slot': 'native-physics-trace',
                            'operation': 'mark', 'args': {'code': code, 'value': frame}}, label)

    def inspect_animator_history(frame, label):
        if args.inspect_animator_history:
            call('bridge', {'command': 'pause'}, label + '-fence')
            call('bridge', {'command': 'hot-call', 'slot': args.animator_inspector_slot,
                            'operation': 'dump-history-frame', 'args': {'frame': frame}}, label)

    def save_split_trace(name):
        # Keep the large event array out of observations.json; that file is for
        # the probe transaction. The independently saved receipt still pins the
        # module/native hashes, mask, markers and every native event.
        receipt = bridge.call({'command': 'hot-call', 'slot': 'native-physics-trace',
                               'operation': 'read', 'args': {'afterSequence': 0, 'max': 32768}})
        (args.out/name).write_text(json.dumps(receipt['detail'], indent=2))

    def managed_trace(operation, label=None, output=None):
        bridge.call({'command': 'pause'})
        request = {'command': 'hot-call', 'slot': 'chef-managed-mutation-tracer',
                   'operation': operation, 'args': {} if label is None else {'label': label}}
        receipt = bridge.call(request)
        if output is not None:
            (args.out/output).write_text(json.dumps(receipt['detail'], indent=2))
        return receipt

    def reconcile_registry(state, native, label):
        if registry_evidence is None:
            return state, native
        requests = registry_evidence.requests(state, native)
        publications = []
        if requests:
            before = world_proof(state, native)
            call('bridge', {'command': 'pause'}, label + '-registry-fence')
            for number, request in enumerate(requests):
                response = call('bridge', {'command': 'hot-call', 'slot': 'registry-observer',
                                'operation': request['operation'], 'args': request['args']},
                                label + '-registry-' + str(number))
                publication = registry_evidence.validate_publication(request, response)
                publications.append((request, publication))
            deadline = time.monotonic() + 3
            while True:
                state = settled(label + '-registry-received')
                if all(registry_evidence.received(request, publication, state)
                       for request, publication in publications):
                    break
                if time.monotonic() > deadline:
                    raise TimeoutError('Published registry observation did not reach the paused controller.')
                time.sleep(.01)
            native = call('bridge', {'command': 'food'}, label + '-registry-native-after')
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
                raise RuntimeError('Registry observation changed native world state: ' + json.dumps(difference))
        captured = registry_evidence.capture(state, native)
        if captured:
            summary.setdefault('registryPhysicalContainers', []).extend(captured)
        return state, native

    try:
        bridge = Client(17636)
        host = ControllerClient(17637)
        if args.prefix_inputs:
            prefix_requests = load_prefix_requests(args.prefix_inputs)
            settled('prefix-start')
            for index, prefix_request in enumerate(prefix_requests):
                if not isinstance(prefix_request, dict) or prefix_request.get('command') != 'raw-input' or \
                        not isinstance(prefix_request.get('segments'), list) or not prefix_request['segments']:
                    raise ValueError(f'Prefix request {index} is not a complete raw-input request.')
                prefix_frames = sum(segment.get('frames', 0) for segment in prefix_request['segments']
                                    if isinstance(segment, dict))
                if not 1 <= prefix_frames <= 36000 or any(
                        not isinstance(segment, dict) or type(segment.get('frames')) is not int or
                        segment['frames'] < 1 or not isinstance(segment.get('chefs'), dict)
                        for segment in prefix_request['segments']):
                    raise ValueError(f'Prefix request {index} has invalid segments.')
                call('bridge', {'command': 'arm'}, f'prefix-{index}-arm')
                call('controller', prefix_request, f'prefix-{index}-input')
                prefix_state = settled(f'prefix-{index}-settled')
                if registry_evidence is not None:
                    prefix_native = native_observation(f'prefix-{index}-native')
                    require_native_boundary(prefix_native)
                    reconcile_registry(prefix_state, prefix_native, f'prefix-{index}')
        initial = settled('initial')
        mark_native_trace(200, initial['frame'], 'native-trace-before-warmup')
        if args.warmup:
            call('bridge', {'command': 'arm'}, 'arm')
            call('controller', {'command': 'step', 'frames': args.warmup}, 'warmup')
            base = settled('base')
        else:
            base = initial
        if base['frame'] != initial['frame'] + args.warmup:
            raise RuntimeError('Warmup did not observe exactly the requested native frames.')
        base_native = native_observation('base-native')
        require_native_boundary(base_native)
        base, base_native = reconcile_registry(base, base_native, 'base')
        if args.inspect_world_sync_cache:
            call('bridge', {'command': 'pause'}, 'world-sync-base-status-fence')
            world_sync = call('bridge', {'command': 'hot-call', 'slot': 'world-sync-cache',
                              'operation': 'status', 'args': {}}, 'world-sync-base-status')['detail']['result']
            (args.out/'world-sync-base.json').write_text(json.dumps(world_sync, indent=2))
            latest = world_sync.get('latest') or {}
            summary['worldSyncBase'] = {
                'name': world_sync.get('name'),
                'active': world_sync.get('active'),
                'lastError': world_sync.get('lastError'),
                'frame': latest.get('frame'),
                'unsupported': latest.get('unsupported'),
                'itemCount': len(latest.get('items') or []),
                'dynamicItems': [item for item in latest.get('items') or [] if item.get('dynamic')],
            }
        fixed_entity_ids = {entity['id'] for entity in base['entities']}
        frame = base['frame']
        inspect_animator_history(frame, 'animator-checkpoint-history')
        mark_native_trace(210, frame, 'native-trace-after-warmup')
        if not args.reuse_contact_manager_checkpoint:
            call('controller', {'command': 'checkpoint', 'path': f'input-probe-{evidence_key}-{frame}.pb'}, 'checkpoint')
        if args.restore_contact_manager_free_stack:
            call('bridge', {'command': 'pause'}, 'contact-pool-capture-fence')
            pool = call('bridge', {'command': 'hot-call', 'slot': args.actor_rebuild_slot,
                                   'operation': 'status', 'args': {}}, 'contact-pool-status-before')['detail']['result']
            expected_pending=args.reuse_contact_manager_checkpoint
            if pool.get('active') is not True or pool.get('automaticContactPoolRestore') is not True or \
                    not pool.get('contactManagerContext') or pool.get('pendingContactPoolAction') != 'none' or \
                    pool.get('automaticRestorePending') is not expected_pending:
                raise RuntimeError('Automatic contact-manager free-stack restoration is not ready')
            if args.restore_transform_dispatch and pool.get('automaticTransformDispatchRestore') is not True:
                raise RuntimeError('Automatic transform-dispatch restoration is not ready')
            contact_pool_start = {'captures': pool.get('contactPoolCaptures', 0),
                                  'restores': pool.get('contactPoolRestores', 0),
                                  'transformCaptures': pool.get('transformDispatchCaptures', 0),
                                  'transformRestores': pool.get('transformDispatchRestores', 0)}
            if args.reuse_contact_manager_checkpoint:
                checkpoint_sidecar=call('bridge', {'command': 'hot-call', 'slot': args.actor_rebuild_slot,
                                        'operation': 'checkpoint-status', 'args': {'frame': frame}},
                                        'contact-pool-reused-checkpoint')['detail']['result'].get('result', {})
                if checkpoint_sidecar.get('captured') is not True or \
                        checkpoint_sidecar.get('coreSnapshotMatches') is not True or \
                        (args.restore_transform_dispatch and checkpoint_sidecar.get('transformDispatchCaptured') is not True):
                    raise RuntimeError('Matching existing contact/Transform checkpoint sidecar is unavailable')
                summary['contactPoolCapture'] = checkpoint_sidecar
            else:
                armed = call('bridge', {'command': 'hot-call', 'slot': args.actor_rebuild_slot,
                                        'operation': 'capture-contact-pool-next', 'args': {}},
                             'contact-pool-arm-capture')['detail']['result']
                if armed.get('pendingContactPoolAction') != 'capture':
                    raise RuntimeError('Contact-manager free-stack capture did not arm')
            contact_pool_cleanup_needed = True
        chefs = sorted(e['id'] for e in base['entities'] if e['chef'] is not None)
        if len(chefs) != 4 or args.chef not in chefs:
            raise RuntimeError('Expected four observed chefs and the requested mover.')
        pads = {str(i): dict(x=args.x if i == args.chef else 0, y=args.y if i == args.chef else 0,
                            pickup=False, interact=False, dash=False) for i in chefs}
        if args.segments:
            request = json.loads(args.segments.read_text(encoding='utf-8-sig'))
        elif args.frame_capture:
            captured_input = json.loads(args.frame_capture.read_text(encoding='utf-8-sig'))
            rows = captured_input.get('inputs', [])
            if captured_input.get('validation') != 'exact-four-pad-frame-coverage' or len(rows) < 3 or \
                    any(row.get('nextFrame') != rows[0].get('nextFrame') + index for index, row in enumerate(rows)):
                raise RuntimeError('Frame capture is not an exact contiguous four-pad recording')
            def neutral(row):
                return all(value['Pad']['X'] == 0 and value['Pad']['Y'] == 0 and
                           not any(value[button]['Down'] for button in ('Pickup', 'Interact', 'Dash'))
                           for value in row['inputs'].values())
            if not neutral(rows[-2]) or not neutral(rows[-1]):
                raise RuntimeError('Frame capture lacks the two terminal neutral release frames')
            payload_count = len(rows) - 2 if args.frame_capture_payload is None else args.frame_capture_payload
            if not 1 <= payload_count <= len(rows) - 2:
                raise RuntimeError('Requested frame-capture payload is outside the recorded payload')
            payload = rows[:payload_count]
            if any(set(row.get('inputs', {})) != {str(chef) for chef in chefs} for row in payload):
                raise RuntimeError('Frame capture does not contain exactly the current four chef IDs')
            segments = [
                {'frames': 1, 'chefs': {
                    chef: {
                        'x': value['Pad']['X'], 'y': value['Pad']['Y'],
                        'pickup': value['Pickup']['Down'], 'interact': value['Interact']['Down'],
                        'dash': value['Dash']['Down'],
                    } for chef, value in row['inputs'].items()
                }} for row in payload]
            if args.frame_capture_neutral_tail:
                segments.append({'frames': args.frame_capture_neutral_tail, 'chefs': {
                    str(chef): {'x': 0, 'y': 0, 'pickup': False, 'interact': False, 'dash': False}
                    for chef in chefs
                }})
            if sum(segment['frames'] for segment in segments) > 36000:
                raise RuntimeError('Captured payload plus neutral tail exceeds 36000 frames')
            request = {'command': 'raw-input', 'segments': segments}
            summary['frameCapture'] = {
                'path': str(args.frame_capture.resolve()),
                'sha256': hashlib.sha256(args.frame_capture.read_bytes()).hexdigest(),
                'sourceFramesIncludingRelease': len(rows),
                'capturedPayloadFrames': len(payload),
                'appendedNeutralFrames': args.frame_capture_neutral_tail,
                'payloadFrames': sum(segment['frames'] for segment in segments),
            }
        else:
            request = {'command': 'raw-input', 'segments': [{'frames': args.frames, 'chefs': pads}]}
        expected_outcome = request.pop('expectedOutcome', None)
        if args.expect_pickup:
            if expected_outcome is not None:
                raise RuntimeError('Pickup outcome was specified twice')
            expected_outcome = {'type': 'pickup', 'chef': args.chef,
                                'item': args.expect_pickup[0], 'source': args.expect_pickup[1]}
        request['command'] = 'raw-input'
        if args.save_managed_phase_traces:
            managed_trace('clear')
            managed_trace('mark', 'original')
        if args.split_native_trace_around_warp:
            call('bridge', {'command': 'hot-call', 'slot': 'native-physics-trace',
                            'operation': 'clear', 'args': {}}, 'native-trace-clear-before-original')
        mark_native_trace(220, frame, 'native-trace-before-original')
        call('bridge', {'command': 'arm'}, 'input-arm')
        call('controller', request, 'input')
        original = settled('original')
        if original['rawInput']['outcome'] != 'complete':
            raise RuntimeError(json.dumps(original['rawInput']))
        inspect_animator_history(original['frame'], 'animator-original-endpoint-history')
        original_native = native_observation('original-native')
        require_native_boundary(original_native)
        original, original_native = reconcile_registry(original, original_native, 'original')
        if args.save_managed_phase_traces:
            managed_trace('status', output='managed-trace-original.json')
        mark_native_trace(230, original['frame'], 'native-trace-after-original')
        if args.restore_contact_manager_free_stack:
            contact_pool_cleanup_needed = False
            call('bridge', {'command': 'pause'}, 'contact-pool-captured-fence')
            captured = call('bridge', {'command': 'hot-call', 'slot': args.actor_rebuild_slot,
                                       'operation': 'status', 'args': {}},
                            'contact-pool-status-captured')['detail']['result']
            expected_captures=contact_pool_start['captures']+(0 if args.reuse_contact_manager_checkpoint else 1)
            if captured.get('pendingContactPoolAction') != 'none' or \
                    captured.get('contactPoolSnapshotCaptured') is not True or \
                    captured.get('contactPoolSnapshotFrame') != frame or \
                    captured.get('contactPoolCaptures') != expected_captures:
                raise RuntimeError('Contact-manager free-stack snapshot was not captured for the input checkpoint')
            if not args.reuse_contact_manager_checkpoint:
                summary['contactPoolCapture'] = captured.get('contactPoolReceipts', [])[-1]
            if args.restore_transform_dispatch:
                if captured.get('transformDispatchSnapshotCaptured') is not True or \
                        captured.get('transformDispatchSnapshotFrame') != frame or \
                        captured.get('transformDispatchCaptures') != contact_pool_start['transformCaptures'] + (0 if args.reuse_contact_manager_checkpoint else 1):
                    raise RuntimeError('Transform-dispatch snapshot was not captured for the input checkpoint')
                if not args.reuse_contact_manager_checkpoint:
                    summary['transformDispatchCapture'] = captured.get('transformDispatchReceipts', [])[-1]
            if args.reuse_contact_manager_checkpoint:
                if captured.get('automaticRestorePending') is not False or \
                        captured.get('contactPoolRestores') != contact_pool_start['restores'] + 1 or \
                        (args.restore_transform_dispatch and
                         captured.get('transformDispatchRestores') != contact_pool_start['transformRestores'] + 1):
                    raise RuntimeError('Existing contact/Transform checkpoint was not consumed by the original branch')
                contact_pool_start['restores']=captured.get('contactPoolRestores')
                contact_pool_start['transformRestores']=captured.get('transformDispatchRestores')
        exported = call('controller', {'command': 'record-input', 'path': f'input-probe-{evidence_key}-{frame}.json'}, 'export')
        recording = json.loads(Path(exported['path']).read_text())
        count = require_recorded_completion(original, recording, frame)
        if recording['payloadFrames'] != sum(s['frames'] for s in request['segments']):
            raise RuntimeError('Exported payload length differs from requested input.')
        (args.out/'recording.json').write_text(json.dumps(recording, indent=2))
        if expected_outcome is not None:
            summary['originalRequestedOutcome'] = pickup_outcome(base, base_native, original, original_native, expected_outcome)
            if not summary['originalRequestedOutcome']['achieved']:
                raise RuntimeError('Recorded inputs did not achieve the requested native pickup outcome.')
        if args.expect_delivery:
            summary['originalDeliveryOutcome'] = delivery_outcome(base_native, original_native)
            if not summary['originalDeliveryOutcome']['achieved']:
                raise RuntimeError('Recorded inputs did not achieve one native delivery outcome.')
        if args.arm_phase_trace_before_warp:
            armed = call('bridge', {'command': 'hot-call', 'slot': 'physics-capture-trace',
                                    'operation': 'arm-warp', 'args': {}},
                         'physics-capture-trace-arm-warp')['detail']['result']
            if armed.get('active') is not True or armed.get('armed') is not True:
                raise RuntimeError('Read-only physics capture trace did not arm before warp.')
        mark_native_trace(240, frame, 'native-trace-before-warp')
        if args.split_native_trace_around_warp:
            trace_status = call('bridge', {'command': 'hot-call', 'slot': 'native-physics-trace',
                                           'operation': 'status', 'args': {}},
                                'native-trace-split-status')['detail']['result']
            if trace_status.get('active') is not True or not trace_status.get('nativePath') or \
                    not trace_status.get('nativeSha256') or not trace_status.get('installedMask'):
                raise RuntimeError('Active native trace configuration is unavailable for split capture')
            split_trace_config = {'nativePath': trace_status['nativePath'],
                                  'sha256': trace_status['nativeSha256'],
                                  'mask': trace_status['installedMask']}
            save_split_trace('native-trace-original.json')
            call('bridge', {'command': 'hot-call', 'slot': 'native-physics-trace',
                            'operation': 'deactivate', 'args': {}}, 'native-trace-split-deactivate')
        call('bridge', {'command': 'arm'}, 'warp-arm')
        call('controller', {'command': 'warp', 'frame': frame, 'development': True}, 'warp')
        restored = settled('restored')
        restore = call('bridge', {'command': 'status'}, 'restore-native')['bridge']['nativeCheckpoints']['lastRestore']
        if (restored['frame'] != frame or not restore or not restore['verified'] or restore['frame'] != frame or
                restore['attempt'] <= original_native['bridge']['nativeCheckpoints']['restoreAttempts']):
            raise RuntimeError('No verified native checkpoint restoration.')
        restored_native = native_observation('restored-food')
        require_native_boundary(restored_native)
        if args.split_native_trace_around_warp:
            call('bridge', {'command': 'pause'}, 'native-trace-split-reactivate-fence')
            call('bridge', {'command': 'hot-call', 'slot': 'native-physics-trace',
                            'operation': 'activate', 'args': split_trace_config},
                 'native-trace-split-reactivate')
            call('bridge', {'command': 'hot-call', 'slot': 'native-physics-trace',
                            'operation': 'clear', 'args': {}}, 'native-trace-split-clear')
        mark_native_trace(250, restored['frame'], 'native-trace-after-warp')
        compared = compare_boundary(base, base_native['bridge']['nativeRound'], food_trees(base_native),
                                    restored, restored_native['bridge']['nativeRound'], food_trees(restored_native))
        summary['restoredBaselineComparison'] = compared
        baseline_physics = native_physics_comparison(base_native['bridge']['nativePhysics'],
                                                     restored_native['bridge']['nativePhysics'],
                                                     fixed_entity_ids, restored_native, frame)
        compared['nativePhysicsEqual'] = baseline_physics['equal']
        compared['nativePhysicsComparison'] = baseline_physics
        compared['nativeClocksEqual'] = exact_values(native_clock_state(base_native['bridge']), native_clock_state(restored_native['bridge']))
        if not boundary_matches(compared) or compared['nativePhysicsEqual'] is not True or not compared['nativeClocksEqual']:
            raise RuntimeError('Native input probe restored a different initial boundary.')
        if registry_evidence is not None:
            summary['registryEvidenceRebranch'] = registry_evidence.rebranch_after_verified_warp(
                restored, restored_native)
        if args.restore_contact_manager_free_stack:
            call('bridge', {'command': 'pause'}, 'contact-pool-restore-fence')
            scheduled = call('bridge', {'command': 'hot-call', 'slot': args.actor_rebuild_slot,
                                        'operation': 'status', 'args': {}},
                             'contact-pool-status-auto-scheduled')['detail']['result']
            if scheduled.get('automaticRestorePending') is not True or \
                    scheduled.get('contactPoolSnapshotFrame') != frame:
                raise RuntimeError('Successful input checkpoint warp did not schedule the matching free-stack restore')
            contact_pool_cleanup_needed = True
        mark_native_trace(260, frame, 'native-trace-before-replay')
        if args.save_managed_phase_traces:
            managed_trace('clear')
            managed_trace('mark', 'replay')
        call('bridge', {'command': 'arm'}, 'replay-arm')
        call('controller', {'command': 'raw-replay', 'recording': recording}, 'replay')
        replay = settled('replayed')
        require_recorded_completion(replay, recording, frame)
        if args.save_managed_phase_traces:
            managed_trace('status', output='managed-trace-replay.json')
        inspect_animator_history(replay['frame'], 'animator-replay-endpoint-history')
        if args.inspect_animator_history:
            animator_status = call('bridge', {'command': 'hot-call', 'slot': 'chef-animator-checkpoint',
                                              'operation': 'status', 'args': {}},
                                   'animator-post-replay-status')['detail']['result']
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
            animator_failures = {name: animator_status.get(name) for name in animator_failure_fields
                                 if animator_status.get(name) is not None}
            summary['animatorSemanticParity'] = {
                'passed': not animator_failures,
                'failures': animator_failures,
                'lastResumeCompletion': animator_status.get('lastResumeCompletion'),
                'lastResumeCoordination': animator_status.get('lastResumeCoordination'),
            }
            if animator_failures:
                raise RuntimeError('Animator semantic replay parity failed: ' + json.dumps(animator_failures))
        replay_native = native_observation('replayed-native')
        require_native_boundary(replay_native)
        replay, replay_native = reconcile_registry(replay, replay_native, 'replayed')
        mark_native_trace(270, replay['frame'], 'native-trace-after-replay')
        if args.split_native_trace_around_warp:
            save_split_trace('native-trace-replay.json')
        if args.restore_contact_manager_free_stack:
            contact_pool_cleanup_needed = False
            call('bridge', {'command': 'pause'}, 'contact-pool-restored-fence')
            restored_pool = call('bridge', {'command': 'hot-call', 'slot': args.actor_rebuild_slot,
                                            'operation': 'status', 'args': {}},
                                 'contact-pool-status-restored')['detail']['result']
            if restored_pool.get('pendingContactPoolAction') != 'none' or \
                    restored_pool.get('automaticRestorePending') is not False or \
                    restored_pool.get('contactPoolRestores') != contact_pool_start['restores'] + 1:
                raise RuntimeError('Contact-manager free-stack restore was not applied for input replay')
            summary['contactPoolRestore'] = restored_pool.get('contactPoolReceipts', [])[-1]
            if args.restore_transform_dispatch:
                if restored_pool.get('transformDispatchRestores') != contact_pool_start['transformRestores'] + 1:
                    raise RuntimeError('Transform-dispatch restore was not applied for input replay')
                summary['transformDispatchRestore'] = restored_pool.get('transformDispatchReceipts', [])[-1]
        if expected_outcome is not None:
            summary['replayedRequestedOutcome'] = pickup_outcome(base, base_native, replay, replay_native, expected_outcome)
            if not summary['replayedRequestedOutcome']['achieved']:
                raise RuntimeError('Replayed inputs did not achieve the requested native pickup outcome.')
        if args.expect_delivery:
            summary['replayedDeliveryOutcome'] = delivery_outcome(base_native, replay_native)
            if not summary['replayedDeliveryOutcome']['achieved']:
                raise RuntimeError('Replayed inputs did not achieve one native delivery outcome.')
        entities = lambda s: {e['id']: e for e in s['entities']}
        a, b, c = entities(base), entities(original), entities(replay)
        changed = {str(i): {'before': b.get(i), 'after': c.get(i)} for i in b.keys() | c.keys() if not exact_values(b.get(i), c.get(i))}
        def gameplay(value):
            return gameplay_round(value['bridge']['nativeRound'])
        round_equal = exact_values(gameplay(original_native), gameplay(replay_native))
        food_equal = exact_values(original_native['detail']['entities'], replay_native['detail']['entities'])
        physics_comparison = native_physics_comparison(original_native['bridge']['nativePhysics'],
                                                       replay_native['bridge']['nativePhysics'],
                                                       fixed_entity_ids, replay_native, frame)
        physics_equal = physics_comparison['equal']
        clocks_equal = exact_values(native_clock_state(original_native['bridge']), native_clock_state(replay_native['bridge']))
        (args.out/'entity-differences.json').write_text(json.dumps(changed, indent=2))
        summary.update(dict(startFrame=frame, originalEndFrame=original['frame'], replayEndFrame=replay['frame'],
                            payloadFrames=recording['payloadFrames'], observedFramesIncludingRelease=count, releaseFrames=2,
                            originalInput=original['rawInput'], replayInput=replay['rawInput'],
                            initialPosition=a[args.chef]['position'], originalPosition=b[args.chef]['position'],
                            replayPosition=c[args.chef]['position'], changedEntityIds=list(changed),
                            recordingSha256=recording['sha256'], nativeRestore=restore,
                            nativeRoundEqual=round_equal, nativeFoodEqual=food_equal,
                            nativePhysicsEqual=physics_equal, nativePhysicsComparison=physics_comparison,
                            nativeClocksEqual=clocks_equal,
                            actualFocusAtEndpoints=[original_native['bridge']['applicationFocused'], replay_native['bridge']['applicationFocused']],
                            unfocusedVirtualChecks=replay_native['bridge']['unfocusedVirtualInputChecks'],
                            fullScreen=replay_native['bridge']['fullScreen']))
        summary['passed'] = (replay['rawInput']['outcome'] == 'complete' and not changed and round_equal and food_equal and physics_equal is True and clocks_equal and
                             any(a.get(i) != entity for i, entity in b.items()) and
                             original['rawInput']['recordingSha256'] == replay['rawInput']['recordingSha256'])
    except Exception as error:
        summary['error'] = str(error)
    finally:
        try:
            if bridge is not None:
                call('bridge', {'command': 'pause'}, 'finally-pause')
                if contact_pool_cleanup_needed:
                    call('bridge', {'command': 'hot-call', 'slot': args.actor_rebuild_slot,
                                    'operation': 'cancel-contact-pool-next', 'args': {}},
                         'finally-contact-pool-cancel')
                    contact_pool_cleanup_needed = False
        except Exception as error:
            summary['pauseError'] = str(error)
            summary['passed'] = False
        for client in (bridge, host):
            try:
                if client is not None: client.close()
            except Exception as error:
                summary['closeError'] = str(error)
                summary['passed'] = False
        (args.out/'observations.json').write_text(json.dumps(evidence, indent=2))
        (args.out/'summary.json').write_text(json.dumps(summary, indent=2))
        print(json.dumps(summary, indent=2))
    return 0 if summary['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
