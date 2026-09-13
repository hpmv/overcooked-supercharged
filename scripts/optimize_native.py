"""Evaluate bounded planner candidates through ordinary inputs in the local game.

The game must already be loaded through the workspace plugin. Each evaluation
restarts the native kitchen; there are no savestates, position writes or timing
changes. Prefix scores are development observations, never full-round claims.
"""
import argparse
import dataclasses
import hashlib
import gzip
import json
import math
import subprocess
import time
import zlib
from pathlib import Path


@dataclasses.dataclass(frozen=True, order=True)
class Candidate:
    seed: int
    lookahead: int = 8
    service_batch: int = 3
    dash: bool = False
    direct_throws: bool = True
    cooperative_sauces: bool = False
    short_dash: bool = False
    bun_buffer: bool = False
    early_onion: bool = False
    pantry_chop: bool = False
    parallel_onion: bool = False
    staged_release: bool = False
    conditional_far_pot: bool = False
    pre_service_stock: bool = False
    bakery_lookahead: int = 0
    preempt_cannons: bool = False
    service_side_head: bool = False
    sausage_buffer: int = 0
    share_pantry_chop: bool = False
    wait_ready_head: bool = False
    release_firer: bool = False
    near_sauce_stage: bool = False
    short_dash_vessels: bool = False
    serve_before_safe_heat: bool = False
    direct_flavor_throws: bool = False
    near_ready_pot: bool = False
    washer_side_plating: bool = False
    direct_clean_pass: bool = False
    near_ready_fryer: bool = False
    nearest_central_task: bool = False
    continuous_waypoints: bool = False
    wait_final_sauce: bool = False
    predictive_bakery_return: bool = False
    plated_onion_finish: bool = False
    fifo_empty_bowls: bool = False
    plated_hotdog_base: bool = False
    stationary_target_transfers: bool = False

    def __post_init__(self):
        if self.short_dash and not self.dash:
            raise ValueError('Short dash requires dash navigation')
        if self.bakery_lookahead != 0 and not 2 <= self.bakery_lookahead <= 32:
            raise ValueError('Bakery lookahead must be 0 or 2..32')
        if type(self.sausage_buffer) is not int or not 0 <= self.sausage_buffer <= 2:
            raise ValueError('Sausage buffer must be 0..2')
        if self.share_pantry_chop and not self.pantry_chop:
            raise ValueError('Shared pantry chopping requires pantry chopping')
        if self.wait_final_sauce and not self.wait_ready_head:
            raise ValueError('Final-sauce wait requires imminent-head wait')
        if self.predictive_bakery_return and not (self.direct_throws and self.pantry_chop and self.direct_flavor_throws):
            raise ValueError('Predictive bakery return requires direct throws, pantry chopping and direct flavor throws')


def digest(path):
    with open(path, 'rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


def last_response(path):
    """Recover a complete observed state, including from an unclosed gzip stream."""
    decoder, tail, clipped = zlib.decompressobj(31), b'', False
    limit = 4 * 1024 * 1024
    try:
        with open(path, 'rb') as source:
            while chunk := source.read(1024 * 1024):
                tail += decoder.decompress(chunk)
                if len(tail) > limit:
                    tail, clipped = tail[-limit:], True
    except (OSError, zlib.error):
        # Earlier complete records remain observations; they never establish
        # successful completion of the requested run.
        pass
    lines = tail.split(b'\n')[:-1]
    if clipped:
        lines = lines[1:]
    for line in reversed(lines):
        try:
            row = json.loads(line)
        except ValueError:
            continue
        if (isinstance(row, dict) and row.get('kind') == 'call'
                and isinstance(row.get('response'), dict)
                and isinstance(row['response'].get('state'), dict)):
            return row['response']
    return {}


def load_final(result_path, trace):
    try:
        result = json.loads(result_path.read_text(encoding='utf-8'))
        if isinstance(result, dict) and isinstance(result.get('state'), dict):
            return result, 'controller-result'
    except (OSError, ValueError):
        pass
    result = last_response(trace)
    return result, 'trace-fallback' if result else 'unavailable'


def observed_configuration(trace):
    """Read the controller's actual initialization settings, not its CLI echo."""
    try:
        opened = gzip.open if str(trace).endswith('.gz') else open
        with opened(trace, 'rt', encoding='utf-8') as source:
            consumed = 0
            for line in source:
                consumed += len(line)
                if consumed > 64 * 1024 * 1024:
                    break
                row = json.loads(line)
                if row.get('kind') == 'event' and row.get('name') == 'plannerInitialized':
                    value = row.get('value', {}).get('configuration')
                    return value if isinstance(value, dict) else None
    except (OSError, EOFError, ValueError, zlib.error):
        pass
    return None


def configuration_evidence(candidate, args, trace):
    observed = observed_configuration(trace)
    expected = {'Seed': candidate.seed, 'Lookahead': candidate.lookahead,
                'ServiceBatch': candidate.service_batch, 'UseDash': candidate.dash,
                'DirectVesselThrows': candidate.direct_throws,
                'CooperativeSauces': candidate.cooperative_sauces,
                'UseShortDash': candidate.short_dash, 'BufferChoppedBuns': candidate.bun_buffer,
                'EarlyOnionHeat': candidate.early_onion,
                'PantryChopping': candidate.pantry_chop,
                'ParallelOnionHeat': candidate.parallel_onion,
                'StagedResourceRelease': candidate.staged_release,
                'ConditionalFarPotThrows': candidate.conditional_far_pot,
                'PreServiceStock': candidate.pre_service_stock,
                'BakeryLookahead': candidate.bakery_lookahead,
                'CannonBoundaryPreemption': candidate.preempt_cannons,
                'ServiceSideHead': candidate.service_side_head,
                'SausageBufferSize': candidate.sausage_buffer,
                'SharedPantryChopping': candidate.share_pantry_chop,
                'WaitForImminentHead': candidate.wait_ready_head,
                'ReleaseFiringChefOnLaunch': candidate.release_firer,
                'NearSauceStaging': candidate.near_sauce_stage,
                'ShortDashVessels': candidate.short_dash_vessels,
                'ServeBeforeSafeHeat': candidate.serve_before_safe_heat,
                'DirectPreparedFlavorThrows': candidate.direct_flavor_throws,
                'NearReadyPotHarvest': candidate.near_ready_pot,
                'WasherSidePlating': candidate.washer_side_plating,
                'DirectCleanPassAssembly': candidate.direct_clean_pass,
                'NearReadyFryerHarvest': candidate.near_ready_fryer,
                'NearestCentralTaskPreference': candidate.nearest_central_task,
                'ContinuousWaypoints': candidate.continuous_waypoints,
                'WaitForFinalSauceHead': candidate.wait_final_sauce,
                'PredictiveBakeryReturn': candidate.predictive_bakery_return,
                'PlatedOnionFinish': candidate.plated_onion_finish,
                'FifoEmptyBowls': candidate.fifo_empty_bowls,
                'PlatedHotdogBase': candidate.plated_hotdog_base,
                'StationaryTargetTransfers': candidate.stationary_target_transfers,
                'TargetScore': 5000, 'StopAfterDeliveries': 0 if args.full_round else args.deliveries}
    missing_disabled = []
    mismatches = []
    disabled_baselines = {field: False for field in ('BufferChoppedBuns', 'EarlyOnionHeat', 'PantryChopping',
        'ParallelOnionHeat', 'StagedResourceRelease', 'ConditionalFarPotThrows', 'PreServiceStock',
        'CannonBoundaryPreemption', 'ServiceSideHead', 'SharedPantryChopping', 'WaitForImminentHead',
        'ReleaseFiringChefOnLaunch', 'NearSauceStaging', 'ShortDashVessels',
        'ServeBeforeSafeHeat', 'DirectPreparedFlavorThrows', 'NearReadyPotHarvest', 'WasherSidePlating',
        'DirectCleanPassAssembly', 'NearReadyFryerHarvest', 'NearestCentralTaskPreference', 'ContinuousWaypoints',
        'WaitForFinalSauceHead', 'PredictiveBakeryReturn', 'PlatedOnionFinish', 'FifoEmptyBowls', 'PlatedHotdogBase', 'StationaryTargetTransfers')}
    disabled_baselines['BakeryLookahead'] = 0
    disabled_baselines['SausageBufferSize'] = 0
    for field, value in expected.items():
        if (observed is not None and field not in observed and field in disabled_baselines
                and type(value) is type(disabled_baselines[field]) and value == disabled_baselines[field]):
            # Pre-buffer bundles expose no such policy. They remain usable as
            # the disabled baseline, never as evidence for an enabled feature.
            missing_disabled.append(field)
            continue
        actual = None if observed is None else observed.get(field)
        if type(actual) is not type(value) or actual != value:
            mismatches.append({'field': field, 'expected': value, 'observed': actual})
    return {'matched': not mismatches, 'observed': observed, 'mismatches': mismatches,
            'unsupportedDisabledBaselineFields': missing_disabled}


def finite_number(value):
    return type(value) in (int, float) and math.isfinite(value)


def qualification(final, exit_code, full_round, deliveries, evidence_valid=True):
    """Only native terminal observations qualify; target/projection fields do not."""
    state, planner = final.get('state') or {}, final.get('planner') or {}
    if not isinstance(state, dict) or not isinstance(planner, dict):
        return False, False
    reason = planner.get('reason')
    succeeded = (evidence_valid and exit_code == 0 and final.get('ok') is True
                 and state.get('scene') == 's_Day_3_4')
    ended = (succeeded and reason == 'native-round-ended'
             and finite_number(state.get('timer')) and state['timer'] <= 0
             and state.get('serverRoundActive') is False
             and state.get('clientRoundActive') is False)
    checkpoint = (succeeded and reason in ('requested-delivery-checkpoint', 'native-round-ended')
                  and finite_number(state.get('delivered')) and state['delivered'] >= deliveries
                  and (reason != 'native-round-ended' or ended))
    return bool(ended if full_round else checkpoint), bool(ended)


def neighbors(candidate):
    for field, values in (
        ('lookahead', (candidate.lookahead - 2, candidate.lookahead + 2)),
        ('service_batch', (candidate.service_batch - 1, candidate.service_batch + 1)),
        ('dash', (not candidate.dash,)),
        ('direct_throws', (not candidate.direct_throws,)),
        ('cooperative_sauces', (not candidate.cooperative_sauces,)),
        ('bun_buffer', (not candidate.bun_buffer,)),
        ('early_onion', (not candidate.early_onion,)),
        ('pantry_chop', (not candidate.pantry_chop,)),
        ('parallel_onion', (not candidate.parallel_onion,)),
        ('staged_release', (not candidate.staged_release,)),
        ('conditional_far_pot', (not candidate.conditional_far_pot,)),
        ('pre_service_stock', (not candidate.pre_service_stock,)),
        ('preempt_cannons', (not candidate.preempt_cannons,)),
        ('service_side_head', (not candidate.service_side_head,)),
        ('wait_ready_head', (not candidate.wait_ready_head,)),
        ('release_firer', (not candidate.release_firer,)),
        ('near_sauce_stage', (not candidate.near_sauce_stage,)),
        ('short_dash_vessels', (not candidate.short_dash_vessels,)),
        ('serve_before_safe_heat', (not candidate.serve_before_safe_heat,)),
        ('direct_flavor_throws', (not candidate.direct_flavor_throws,)),
        ('near_ready_pot', (not candidate.near_ready_pot,)),
        ('washer_side_plating', (not candidate.washer_side_plating,)),
        ('direct_clean_pass', (not candidate.direct_clean_pass,)),
        ('near_ready_fryer', (not candidate.near_ready_fryer,)),
        ('nearest_central_task', (not candidate.nearest_central_task,)),
        ('continuous_waypoints', (not candidate.continuous_waypoints,)),
        ('wait_final_sauce', (not candidate.wait_final_sauce,) if candidate.wait_ready_head else ()),
        ('predictive_bakery_return', (not candidate.predictive_bakery_return,)
         if candidate.direct_throws and candidate.pantry_chop and candidate.direct_flavor_throws else ()),
        ('plated_onion_finish', (not candidate.plated_onion_finish,)),
        ('fifo_empty_bowls', (not candidate.fifo_empty_bowls,)),
        ('plated_hotdog_base', (not candidate.plated_hotdog_base,)),
        ('stationary_target_transfers', (not candidate.stationary_target_transfers,)),
        ('sausage_buffer', (candidate.sausage_buffer - 1, candidate.sausage_buffer + 1)),
        ('share_pantry_chop', (not candidate.share_pantry_chop,) if candidate.pantry_chop else ()),
        ('bakery_lookahead', (max(12, candidate.lookahead),) if candidate.bakery_lookahead == 0
         else (0, min(32, candidate.bakery_lookahead + 4))),
        ('short_dash', (not candidate.short_dash,) if candidate.dash else ()),
    ):
        for value in values:
            if field == 'dash' and not value and candidate.short_dash:
                continue
            if field == 'sausage_buffer' and not 0 <= value <= 2:
                continue
            if field == 'pantry_chop' and not value and candidate.share_pantry_chop:
                continue
            if field == 'wait_ready_head' and not value and candidate.wait_final_sauce:
                continue
            if field in ('direct_throws', 'pantry_chop', 'direct_flavor_throws') and not value and candidate.predictive_bakery_return:
                continue
            result = dataclasses.replace(candidate, **{field: value})
            if result != candidate and 2 <= result.lookahead <= 16 and 1 <= result.service_batch <= 8:
                yield result


def rank(row):
    # Native observations only. Success precedes partial executions; ties use
    # the deterministic parameter tuple, never wall time or random selection.
    def observed(field, default):
        return row[field] if finite_number(row.get(field)) else default
    return (-int(row.get('qualifyingNativeHighScoreRound', False)),
            -int(row['completedRequestedRun']), -observed('score', -math.inf),
            -observed('delivered', -math.inf), observed('gameplayFrame', math.inf),
            tuple(row['candidate'][field.name] for field in dataclasses.fields(Candidate)))


def evaluate(candidate, generation, trial, args, controller, controller_hash, output):
    """One native evaluation; subprocess.run is replaced by fixtures in offline tests."""
    if digest(controller) != controller_hash:
        raise RuntimeError('Controller changed during native search')
    prefix = output / f'trial{trial:03}'
    trace, result_path, stderr_path = (prefix.with_suffix(suffix)
        for suffix in ('.jsonl.gz', '.result.json', '.stderr.txt'))
    for path in (trace, result_path, stderr_path):
        if path.exists():
            raise FileExistsError('Native trial output already exists: ' + str(path))
    command = ['dotnet', str(controller), 'bot', '--restart', '--seed', str(candidate.seed),
               '--port', str(args.port), '--lookahead', str(candidate.lookahead),
               '--service-batch', str(candidate.service_batch), '--timeout', str(args.timeout),
               '--out', str(trace), '--compact']
    if not args.full_round:
        command += ['--stop-after-deliveries', str(args.deliveries)]
    if candidate.dash:
        command.append('--dash')
    if candidate.short_dash:
        command.append('--short-dash')
    if candidate.cooperative_sauces:
        command.append('--cooperative-sauces')
    if candidate.bun_buffer:
        command.append('--bun-buffer')
    if candidate.early_onion:
        command.append('--early-onion')
    if candidate.pantry_chop:
        command.append('--pantry-chop')
    if candidate.parallel_onion:
        command.append('--parallel-onion')
    if candidate.staged_release:
        command.append('--staged-release')
    if candidate.conditional_far_pot:
        command.append('--conditional-far-pot')
    if candidate.pre_service_stock:
        command.append('--pre-service-stock')
    if candidate.bakery_lookahead:
        command += ['--bakery-lookahead', str(candidate.bakery_lookahead)]
    if candidate.preempt_cannons:
        command.append('--preempt-cannons')
    if candidate.service_side_head:
        command.append('--service-side-head')
    if candidate.sausage_buffer:
        command += ['--sausage-buffer', str(candidate.sausage_buffer)]
    if candidate.share_pantry_chop:
        command.append('--share-pantry-chop')
    if candidate.wait_ready_head:
        command.append('--wait-ready-head')
    if candidate.release_firer:
        command.append('--release-firer')
    if candidate.near_sauce_stage:
        command.append('--near-sauce-stage')
    if candidate.short_dash_vessels:
        command.append('--short-dash-vessels')
    if candidate.serve_before_safe_heat:
        command.append('--serve-before-safe-heat')
    if candidate.direct_flavor_throws:
        command.append('--direct-flavor-throws')
    if candidate.near_ready_pot:
        command.append('--near-ready-pot')
    if candidate.washer_side_plating:
        command.append('--washer-side-plating')
    if candidate.direct_clean_pass:
        command.append('--direct-clean-pass')
    if candidate.near_ready_fryer:
        command.append('--near-ready-fryer')
    if candidate.nearest_central_task:
        command.append('--nearest-central-task')
    if candidate.continuous_waypoints:
        command.append('--continuous-waypoints')
    if candidate.wait_final_sauce:
        command.append('--wait-final-sauce')
    if candidate.predictive_bakery_return:
        command.append('--predictive-bakery-return')
    if candidate.plated_onion_finish:
        command.append('--plated-onion-finish')
    if candidate.fifo_empty_bowls:
        command.append('--fifo-empty-bowls')
    if candidate.plated_hotdog_base:
        command.append('--plated-hotdog-base')
    if candidate.stationary_target_transfers:
        command.append('--stationary-target-transfers')
    if not candidate.direct_throws:
        command.append('--no-direct-throws')
    started, exit_code, failure = time.monotonic(), None, None
    # The controller's own timeout closes its socket and releases input. The
    # outer timeout also records a failed trial instead of losing its trace.
    with result_path.open('x', encoding='utf-8') as stdout, stderr_path.open('x', encoding='utf-8') as stderr:
        try:
            exit_code = subprocess.run(command, stdout=stdout, stderr=stderr,
                                       timeout=args.timeout + 60).returncode
        except (subprocess.TimeoutExpired, OSError) as error:
            failure = type(error).__name__ + ': ' + str(error)
            stderr.write(failure + '\n')
    final, source = load_final(result_path, trace)
    if source != 'controller-result' and failure is None:
        failure = 'Controller exited without a complete result; the final state is only a trace fallback.'
    state = final.get('state') or {}
    trace_hash = digest(trace) if trace.is_file() else None
    observed_controller_hash = digest(controller) if controller.is_file() else None
    controller_unchanged = observed_controller_hash == controller_hash
    configuration = configuration_evidence(candidate, args, trace)
    completed, full_completed = qualification(final, exit_code, args.full_round, args.deliveries,
        evidence_valid=controller_unchanged and trace_hash is not None and source == 'controller-result' and configuration['matched'])
    score = state.get('score')
    return {'trial': trial, 'generation': generation, 'candidate': dataclasses.asdict(candidate),
            'command': command, 'exitCode': exit_code, 'failure': failure,
            'wallSeconds': round(time.monotonic() - started, 3), 'observationSource': source,
            'completedRequestedRun': completed, 'completedFullRound': full_completed,
            'nativeScoreAtLeast5000': finite_number(score) and score >= 5000,
            'qualifyingNativeHighScoreRound': full_completed and finite_number(score) and score >= 5000,
            'controllerUnchanged': controller_unchanged, 'observedControllerSha256': observed_controller_hash,
            'configurationEvidence': configuration,
            'gameplayFrame': state.get('gameplayFrame'), 'timer': state.get('timer'),
            'score': score, 'delivered': state.get('delivered'), 'deductions': state.get('deductions'),
            'trace': str(trace), 'traceSha256': trace_hash,
            'deliveryHistory': [e for e in state.get('gameEvents', [])
                                if isinstance(e, dict) and e.get('kind') == 'delivery']}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--controller', required=True, type=Path)
    parser.add_argument('--out', required=True, type=Path, help='New evidence directory; must not exist')
    parser.add_argument('--port', type=int, default=17634)
    parser.add_argument('--seeds', default='0', help='Comma-separated native Unity seeds')
    parser.add_argument('--lookahead', type=int, default=8)
    parser.add_argument('--service-batch', type=int, default=3)
    parser.add_argument('--dash', action='store_true')
    parser.add_argument('--short-dash', action='store_true')
    parser.add_argument('--cooperative-sauces', action='store_true')
    parser.add_argument('--bun-buffer', action='store_true')
    parser.add_argument('--early-onion', action='store_true')
    parser.add_argument('--pantry-chop', action='store_true')
    parser.add_argument('--parallel-onion', action='store_true')
    parser.add_argument('--staged-release', action='store_true')
    parser.add_argument('--conditional-far-pot', action='store_true')
    parser.add_argument('--pre-service-stock', action='store_true')
    parser.add_argument('--bakery-lookahead', type=int, default=0)
    parser.add_argument('--preempt-cannons', action='store_true')
    parser.add_argument('--service-side-head', action='store_true')
    parser.add_argument('--sausage-buffer', type=int, choices=(0, 1, 2), default=0)
    parser.add_argument('--share-pantry-chop', action='store_true')
    parser.add_argument('--wait-ready-head', action='store_true')
    parser.add_argument('--release-firer', action='store_true')
    parser.add_argument('--near-sauce-stage', action='store_true')
    parser.add_argument('--short-dash-vessels', action='store_true')
    parser.add_argument('--serve-before-safe-heat', action='store_true')
    parser.add_argument('--direct-flavor-throws', action='store_true')
    parser.add_argument('--near-ready-pot', action='store_true')
    parser.add_argument('--washer-side-plating', action='store_true')
    parser.add_argument('--direct-clean-pass', action='store_true')
    parser.add_argument('--near-ready-fryer', action='store_true')
    parser.add_argument('--nearest-central-task', action='store_true')
    parser.add_argument('--continuous-waypoints', action='store_true')
    parser.add_argument('--wait-final-sauce', action='store_true')
    parser.add_argument('--predictive-bakery-return', action='store_true')
    parser.add_argument('--plated-onion-finish', action='store_true')
    parser.add_argument('--fifo-empty-bowls', action='store_true')
    parser.add_argument('--plated-hotdog-base', action='store_true')
    parser.add_argument('--stationary-target-transfers', action='store_true')
    parser.add_argument('--beam', type=int, default=2)
    parser.add_argument('--rounds', type=int, default=1)
    parser.add_argument('--budget', type=int, default=6, help='Maximum native trials')
    parser.add_argument('--deliveries', type=int, default=4, help='Stop at this native delivery count')
    parser.add_argument('--full-round', action='store_true')
    parser.add_argument('--timeout', type=int, default=1800)
    parser.add_argument('--plan-only', action='store_true')
    args = parser.parse_args(argv)
    try:
        seeds = sorted(set(int(x) for x in args.seeds.split(',')))
    except ValueError:
        parser.error('Seeds must be comma-separated signed Int32 values')
    if not seeds or any(not -(2**31) <= x < 2**31 for x in seeds):
        parser.error('Seeds must be signed Int32 values')
    if args.beam < 1 or args.rounds < 0 or args.budget < len(seeds) or args.deliveries < 1 or args.timeout < 10:
        parser.error('Invalid beam, round, trial, delivery or timeout bound')
    if not 2 <= args.lookahead <= 16 or not 1 <= args.service_batch <= 8:
        parser.error('Lookahead must be 2..16 and service batch 1..8')
    if args.bakery_lookahead != 0 and not 2 <= args.bakery_lookahead <= 32:
        parser.error('Bakery lookahead must be 0 or 2..32')
    if args.share_pantry_chop and not args.pantry_chop:
        parser.error('Shared pantry chopping requires --pantry-chop')
    if args.wait_final_sauce and not args.wait_ready_head:
        parser.error('Final-sauce wait requires --wait-ready-head')
    if args.predictive_bakery_return and not (args.pantry_chop and args.direct_flavor_throws):
        parser.error('Predictive bakery return requires --pantry-chop and --direct-flavor-throws')
    controller = args.controller.resolve(strict=True)
    controller_hash = digest(controller)
    initial = [Candidate(seed, args.lookahead, args.service_batch, args.dash or args.short_dash,
                         True, args.cooperative_sauces, args.short_dash, args.bun_buffer, args.early_onion,
                         args.pantry_chop, args.parallel_onion, args.staged_release, args.conditional_far_pot,
                         args.pre_service_stock, args.bakery_lookahead, args.preempt_cannons,
                         args.service_side_head, args.sausage_buffer, args.share_pantry_chop,
                         args.wait_ready_head, args.release_firer, args.near_sauce_stage,
                         args.short_dash_vessels, args.serve_before_safe_heat, args.direct_flavor_throws,
                         args.near_ready_pot, args.washer_side_plating, args.direct_clean_pass, args.near_ready_fryer,
                         args.nearest_central_task, args.continuous_waypoints,
                         args.wait_final_sauce, args.predictive_bakery_return,
                         args.plated_onion_finish, args.fifo_empty_bowls, args.plated_hotdog_base,
                         args.stationary_target_transfers) for seed in seeds]
    if args.plan_only:
        print(json.dumps({'executed': False, 'controllerSha256': controller_hash,
                          'initialCandidates': [dataclasses.asdict(c) for c in initial],
                          'maximumNativeTrials': args.budget, 'rounds': args.rounds,
                          'mode': 'native-round' if args.full_round else 'native-prefix'}, indent=2))
        return
    output = args.out.resolve()
    output.mkdir(parents=True, exist_ok=False)
    base = ['dotnet', str(controller)]
    inspected = subprocess.run(base + ['inspect', '--port', str(args.port), '--compact'],
                               text=True, capture_output=True, timeout=30)
    (output / 'initial-inspection.json').write_text(inspected.stdout, encoding='utf-8')
    if inspected.returncode:
        raise RuntimeError('Workspace game inspection failed: ' + inspected.stderr)
    response = json.loads(inspected.stdout)
    session = response.get('session', {})
    if isinstance(session, str):
        session = json.loads(session)
    if (not response.get('ok') or session.get('dlc') != 8 or session.get('variantPlayers') != 4
            or session.get('stage') != 'kitchen_ready' or session.get('variantScene') != 's_Day_3_4'):
        raise RuntimeError('Load the native four-chef Carnival kitchen before optimizing')
    results, seen = [], set()
    summary = {'format': 'oc2-native-candidate-search', 'version': 2,
               'controller': str(controller), 'controllerSha256': controller_hash,
               'port': args.port, 'mode': 'native-round' if args.full_round else 'native-prefix',
               'requestedDeliveries': 0 if args.full_round else args.deliveries,
               'qualification': 'Actual candidate trials; five fresh-process high-score verification is separate',
               'scoreSource': 'Observed native state.score only; prefix scores are not completed-round scores',
               'initialSnapshot': 'initial-inspection.json', 'results': results}

    def save():
        summary['rankedTrialIndices'] = [row['trial'] for row in sorted(results, key=rank)]
        (output / 'summary.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')

    frontier = initial
    for generation in range(args.rounds + 1):
        for candidate in sorted(set(frontier)):
            if candidate in seen or len(results) >= args.budget:
                continue
            seen.add(candidate)
            trial = len(results) + 1
            row = evaluate(candidate, generation, trial, args, controller, controller_hash, output)
            results.append(row)
            save()
            if not row['controllerUnchanged']:
                raise RuntimeError('Controller changed during native trial; partial evidence saved, search stopped')
            config = row.get('configurationEvidence', {})
            if config.get('observed') is not None and config.get('matched') is False:
                raise RuntimeError('Controller did not apply the requested planner settings; partial evidence saved, search stopped')
            print(json.dumps({key: row[key] for key in ('trial', 'candidate', 'completedRequestedRun', 'gameplayFrame', 'score', 'delivered')}), flush=True)
        if len(results) >= args.budget:
            break
        beam = [Candidate(**row['candidate']) for row in sorted(results, key=rank)[:args.beam]]
        frontier = [n for candidate in beam for n in neighbors(candidate) if n not in seen]
    save()


if __name__ == '__main__':
    main()
