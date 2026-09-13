"""Offline runner regressions. Every process invocation is replaced by a fixture."""
import argparse
import contextlib
import dataclasses
import gzip
import io
import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import optimize_native as runner


def response(reason='requested-delivery-checkpoint', delivered=4, score=332, ended=False):
    return {'ok': True, 'state': {'scene': 's_Day_3_4', 'gameplayFrame': 1200,
        'timer': 0 if ended else 250, 'serverRoundActive': not ended,
        'clientRoundActive': not ended, 'delivered': delivered, 'score': score,
        'deductions': 0, 'gameEvents': [{'kind': 'delivery', 'recipeId': 158500}]},
        'planner': {'reason': reason, 'targetReached': True, 'predictedFreshScore': 9000}}


def call(value):
    return {'kind': 'call', 'response': value}


def initialized(command):
    def number(flag, default):
        return int(command[command.index(flag) + 1]) if flag in command else default
    return {'kind': 'event', 'name': 'plannerInitialized', 'value': {'configuration': {
        'Seed': number('--seed', 0), 'Lookahead': number('--lookahead', 8),
        'ServiceBatch': number('--service-batch', 3), 'UseDash': '--dash' in command,
        'DirectVesselThrows': '--no-direct-throws' not in command,
        'CooperativeSauces': '--cooperative-sauces' in command,
        'UseShortDash': '--short-dash' in command, 'BufferChoppedBuns': '--bun-buffer' in command,
        'EarlyOnionHeat': '--early-onion' in command,
        'PantryChopping': '--pantry-chop' in command,
        'ParallelOnionHeat': '--parallel-onion' in command,
        'StagedResourceRelease': '--staged-release' in command,
        'ConditionalFarPotThrows': '--conditional-far-pot' in command,
        'PreServiceStock': '--pre-service-stock' in command,
        'BakeryLookahead': number('--bakery-lookahead', 0),
        'CannonBoundaryPreemption': '--preempt-cannons' in command,
        'ServiceSideHead': '--service-side-head' in command,
        'SausageBufferSize': number('--sausage-buffer', 0),
        'SharedPantryChopping': '--share-pantry-chop' in command,
        'WaitForImminentHead': '--wait-ready-head' in command,
        'ReleaseFiringChefOnLaunch': '--release-firer' in command,
        'NearSauceStaging': '--near-sauce-stage' in command,
        'ShortDashVessels': '--short-dash-vessels' in command,
        'ServeBeforeSafeHeat': '--serve-before-safe-heat' in command,
        'DirectPreparedFlavorThrows': '--direct-flavor-throws' in command,
        'NearReadyPotHarvest': '--near-ready-pot' in command,
        'WasherSidePlating': '--washer-side-plating' in command,
        'DirectCleanPassAssembly': '--direct-clean-pass' in command,
        'NearReadyFryerHarvest': '--near-ready-fryer' in command,
        'NearestCentralTaskPreference': '--nearest-central-task' in command,
        'ContinuousWaypoints': '--continuous-waypoints' in command,
        'WaitForFinalSauceHead': '--wait-final-sauce' in command,
        'PredictiveBakeryReturn': '--predictive-bakery-return' in command,
        'PlatedOnionFinish': '--plated-onion-finish' in command,
        'FifoEmptyBowls': '--fifo-empty-bowls' in command,
        'PlatedHotdogBase': '--plated-hotdog-base' in command,
        'StationaryTargetTransfers': '--stationary-target-transfers' in command,
        'TargetScore': 5000, 'StopAfterDeliveries': number('--stop-after-deliveries', 0)}}}


def write_trace(path, rows, truncate=0):
    data = gzip.compress(b''.join(json.dumps(row).encode() + b'\n' for row in rows))
    path.write_bytes(data[:-truncate] if truncate else data)


class CandidateTests(unittest.TestCase):
    def test_predictive_bakery_dependency_preserves_bounded_native_route(self):
        with self.assertRaises(ValueError):
            runner.Candidate(0, predictive_bakery_return=True)
        base = runner.Candidate(0, direct_throws=True, pantry_chop=True, direct_flavor_throws=True)
        enabled = dataclasses.replace(base, predictive_bakery_return=True)
        self.assertTrue(all(c.direct_throws and c.pantry_chop and c.direct_flavor_throws
                            for c in runner.neighbors(enabled) if c.predictive_bakery_return))
        self.assertTrue(any(c.predictive_bakery_return for c in runner.neighbors(base)))
        self.assertFalse(any(c.predictive_bakery_return for c in runner.neighbors(runner.Candidate(0))))

    def test_final_sauce_dependency_is_preserved_by_all_neighbors(self):
        with self.assertRaises(ValueError):
            runner.Candidate(0, wait_final_sauce=True)
        enabled = runner.Candidate(0, wait_ready_head=True, wait_final_sauce=True)
        self.assertTrue(all(not c.wait_final_sauce or c.wait_ready_head for c in runner.neighbors(enabled)))
        self.assertFalse(any(c.wait_final_sauce for c in runner.neighbors(runner.Candidate(0))))
        self.assertTrue(any(c.wait_final_sauce for c in runner.neighbors(runner.Candidate(0, wait_ready_head=True))))

    def test_order_is_independent_of_json_key_insertion_order(self):
        def row(seed, reverse=False):
            c = dataclasses.asdict(runner.Candidate(seed))
            if reverse:
                c = dict(reversed(list(c.items())))
            return {'candidate': c, 'completedRequestedRun': True,
                    'score': 68, 'delivered': 1, 'gameplayFrame': 1175}
        self.assertEqual(runner.rank(row(8)), runner.rank(row(8, True)))
        self.assertEqual([r['candidate']['seed'] for r in sorted([row(8, True), row(-2)], key=runner.rank)], [-2, 8])

    def test_ranking_prefers_completion_then_native_deliveries_score_and_frame(self):
        base = {'candidate': dataclasses.asdict(runner.Candidate(0)),
                'completedRequestedRun': True, 'score': 332, 'delivered': 4, 'gameplayFrame': 1200}
        rows = [dict(base, completedRequestedRun=False, score=9000, delivered=90),
                dict(base, gameplayFrame=1300), dict(base, score=331), dict(base, delivered=3), base]
        self.assertIs(sorted(rows, key=runner.rank)[0], base)
        self.assertIs(sorted(rows, key=runner.rank)[-1], rows[0])
        runner.rank(dict(base, score=None, delivered=None, gameplayFrame=None))

    def test_neighbors_change_exactly_one_parameter_and_respect_bounds(self):
        for c, count in [(runner.Candidate(7), 34), (runner.Candidate(7, 2, 1), 32),
                         (runner.Candidate(7, 16, 8), 32)]:
            adjacent = list(runner.neighbors(c))
            self.assertEqual(len(set(adjacent)), count)
            self.assertEqual(adjacent, list(runner.neighbors(c)))
            for n in adjacent:
                self.assertEqual(n.seed, c.seed)
                self.assertEqual(sum(getattr(n, f.name) != getattr(c, f.name)
                                     for f in dataclasses.fields(c)), 1)
                self.assertTrue(2 <= n.lookahead <= 16 and 1 <= n.service_batch <= 8)

    def test_full_round_score_goal_outranks_more_lower_value_deliveries(self):
        base = {'candidate': dataclasses.asdict(runner.Candidate(0)),
                'completedRequestedRun': True, 'gameplayFrame': 16200}
        high = dict(base, score=5100, delivered=46, qualifyingNativeHighScoreRound=True)
        low = dict(base, score=4800, delivered=52, qualifyingNativeHighScoreRound=False)
        self.assertIs(sorted([low, high], key=runner.rank)[0], high)

    def test_buffer_and_shared_chop_bounds_preserve_admissible_neighbors(self):
        for size in (-1, 3, True):
            with self.assertRaises(ValueError):
                runner.Candidate(0, sausage_buffer=size)
        with self.assertRaises(ValueError):
            runner.Candidate(0, share_pantry_chop=True)
        candidate = runner.Candidate(0, pantry_chop=True, share_pantry_chop=True, sausage_buffer=2)
        adjacent = list(runner.neighbors(candidate))
        self.assertTrue(adjacent)
        self.assertTrue(all(0 <= item.sausage_buffer <= 2 for item in adjacent))
        self.assertTrue(all(not item.share_pantry_chop or item.pantry_chop for item in adjacent))
        self.assertTrue(all(sum(getattr(item, f.name) != getattr(candidate, f.name)
                                for f in dataclasses.fields(candidate)) == 1 for item in adjacent))


class QualificationTests(unittest.TestCase):
    def test_prefix_is_not_full_round_even_above_target(self):
        value = response(score=6000)
        self.assertEqual(runner.qualification(value, 0, False, 4), (True, False))
        self.assertEqual(runner.qualification(value, 0, True, 4), (False, False))

    def test_early_round_end_does_not_satisfy_delivery_checkpoint(self):
        value = response('native-round-ended', delivered=3, ended=True)
        self.assertEqual(runner.qualification(value, 0, False, 4), (False, True))
        self.assertEqual(runner.qualification(value, 0, True, 4), (True, True))

    def test_completed_native_round_and_met_prefix(self):
        value = response('native-round-ended', delivered=46, score=5100, ended=True)
        self.assertEqual(runner.qualification(value, 0, False, 4), (True, True))
        self.assertEqual(runner.qualification(value, 0, True, 4), (True, True))

    def test_terminal_reason_needs_native_end_state(self):
        for patch in ({'timer': 1}, {'timer': None}, {'timer': float('nan')},
                      {'serverRoundActive': True}, {'clientRoundActive': True},
                      {'serverRoundActive': None}, {'scene': 'another-level'}):
            with self.subTest(patch=patch):
                value = response('native-round-ended', ended=True)
                value['state'].update(patch)
                self.assertEqual(runner.qualification(value, 0, True, 4), (False, False))

    def test_failures_missing_counts_and_projections_never_qualify(self):
        value = response(delivered=0)
        self.assertEqual(runner.qualification(value, 0, False, 4), (False, False))
        for exit_code in [1, None]:
            self.assertEqual(runner.qualification(response(), exit_code, False, 4), (False, False))
        self.assertEqual(runner.qualification(response(), 0, False, 4, False), (False, False))
        for delivered in [None, True, float('nan')]:
            self.assertEqual(runner.qualification(response(delivered=delivered), 0, False, 1), (False, False))


class TrialTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.controller = self.root / 'controller.dll'
        self.controller.write_bytes(b'offline fixture, never executable')
        self.controller_hash = runner.digest(self.controller)
        self.output = self.root / 'evidence'
        self.output.mkdir()
        self.args = argparse.Namespace(port=17634, timeout=10, full_round=False, deliveries=4)

    def evaluate(self, process, candidate=None):
        with mock.patch.object(runner.subprocess, 'run', side_effect=process) as invoked:
            result = runner.evaluate(candidate or runner.Candidate(8), 0, 1, self.args,
                                     self.controller, self.controller_hash, self.output)
        return result, invoked

    def process(self, value, exit_code=0, stdout=None, mutate=False):
        def run(command, **kwargs):
            write_trace(Path(command[command.index('--out') + 1]), [initialized(command), call(value)])
            kwargs['stdout'].write(json.dumps(value) if stdout is None else stdout)
            if mutate:
                self.controller.write_bytes(b'changed during trial')
            return subprocess.CompletedProcess(command, exit_code)
        return run

    def test_prefix_flags_native_score_and_trace_hash(self):
        row, invoked = self.evaluate(self.process(response(score=6000)), runner.Candidate(8, 6, 2, True, False))
        command = invoked.call_args.args[0]
        self.assertIn('--stop-after-deliveries', command)
        self.assertIn('--dash', command)
        self.assertIn('--no-direct-throws', command)
        self.assertTrue(row['completedRequestedRun'])
        self.assertTrue(row['nativeScoreAtLeast5000'])
        self.assertFalse(row['qualifyingNativeHighScoreRound'])
        self.assertEqual(row['traceSha256'], runner.digest(Path(row['trace'])))
        self.assertEqual(row['score'], 6000)

    def test_full_round_flags_and_qualification(self):
        self.args.full_round = True
        row, invoked = self.evaluate(self.process(response('native-round-ended', 46, 5100, True)))
        self.assertNotIn('--stop-after-deliveries', invoked.call_args.args[0])
        self.assertTrue(row['qualifyingNativeHighScoreRound'])

    def test_buffer_candidate_is_forwarded_and_recorded_without_score_projection(self):
        row, invoked = self.evaluate(self.process(response(score=356)), runner.Candidate(0, bun_buffer=True))
        self.assertIn('--bun-buffer', invoked.call_args.args[0])
        self.assertTrue(row['candidate']['bun_buffer'])
        self.assertEqual(row['score'], 356)
        self.assertFalse(row['qualifyingNativeHighScoreRound'])

    def test_ignored_enabled_flag_does_not_qualify_a_native_candidate(self):
        def process(command, **kwargs):
            event = initialized(command)
            event['value']['configuration'].pop('BufferChoppedBuns')
            value = response()
            write_trace(Path(command[command.index('--out') + 1]), [event, call(value)])
            kwargs['stdout'].write(json.dumps(value))
            return subprocess.CompletedProcess(command, 0)
        row, _ = self.evaluate(process, runner.Candidate(8, bun_buffer=True))
        self.assertEqual(row['score'], 332)
        self.assertFalse(row['completedRequestedRun'])
        self.assertFalse(row['configurationEvidence']['matched'])
        self.assertEqual(row['configurationEvidence']['mismatches'][0]['field'], 'BufferChoppedBuns')

    def test_parallel_pantry_policies_are_forwarded_and_native_configuration_verified(self):
        candidate = runner.Candidate(0, pantry_chop=True, parallel_onion=True, staged_release=True,
                                     conditional_far_pot=True, pre_service_stock=True, bakery_lookahead=12,
                                     preempt_cannons=True, service_side_head=True, sausage_buffer=2,
                                     share_pantry_chop=True, wait_ready_head=True,
                                     release_firer=True, near_sauce_stage=True, short_dash_vessels=True,
                                     serve_before_safe_heat=True, direct_flavor_throws=True,
                                     near_ready_pot=True, washer_side_plating=True, direct_clean_pass=True, near_ready_fryer=True,
                                     nearest_central_task=True, continuous_waypoints=True,
                                     wait_final_sauce=True, predictive_bakery_return=True,
                                     plated_onion_finish=True, fifo_empty_bowls=True, plated_hotdog_base=True,
                                     stationary_target_transfers=True)
        row, invoked = self.evaluate(self.process(response()), candidate)
        self.assertIn('--pantry-chop', invoked.call_args.args[0])
        self.assertIn('--parallel-onion', invoked.call_args.args[0])
        self.assertIn('--staged-release', invoked.call_args.args[0])
        self.assertIn('--conditional-far-pot', invoked.call_args.args[0])
        self.assertIn('--pre-service-stock', invoked.call_args.args[0])
        self.assertIn('--preempt-cannons', invoked.call_args.args[0])
        self.assertIn('--service-side-head', invoked.call_args.args[0])
        self.assertIn('--share-pantry-chop', invoked.call_args.args[0])
        self.assertIn('--wait-ready-head', invoked.call_args.args[0])
        self.assertIn('--release-firer', invoked.call_args.args[0])
        self.assertIn('--near-sauce-stage', invoked.call_args.args[0])
        self.assertIn('--short-dash-vessels', invoked.call_args.args[0])
        self.assertIn('--serve-before-safe-heat', invoked.call_args.args[0])
        self.assertIn('--direct-flavor-throws', invoked.call_args.args[0])
        self.assertIn('--near-ready-pot', invoked.call_args.args[0])
        self.assertIn('--washer-side-plating', invoked.call_args.args[0])
        self.assertIn('--direct-clean-pass', invoked.call_args.args[0])
        self.assertIn('--near-ready-fryer', invoked.call_args.args[0])
        self.assertIn('--nearest-central-task', invoked.call_args.args[0])
        self.assertIn('--continuous-waypoints', invoked.call_args.args[0])
        self.assertIn('--wait-final-sauce', invoked.call_args.args[0])
        self.assertIn('--predictive-bakery-return', invoked.call_args.args[0])
        self.assertIn('--plated-onion-finish', invoked.call_args.args[0])
        self.assertIn('--fifo-empty-bowls', invoked.call_args.args[0])
        self.assertIn('--plated-hotdog-base', invoked.call_args.args[0])
        self.assertIn('--stationary-target-transfers', invoked.call_args.args[0])
        command = invoked.call_args.args[0]
        self.assertEqual(command[command.index('--bakery-lookahead') + 1], '12')
        self.assertEqual(command[command.index('--sausage-buffer') + 1], '2')
        self.assertTrue(row['configurationEvidence']['matched'])
        path = self.output / 'configuration-only.jsonl.gz'
        for field in ('PantryChopping', 'ParallelOnionHeat', 'StagedResourceRelease', 'ConditionalFarPotThrows',
                      'PreServiceStock', 'BakeryLookahead', 'CannonBoundaryPreemption', 'ServiceSideHead',
                      'SausageBufferSize', 'SharedPantryChopping', 'WaitForImminentHead',
                      'ReleaseFiringChefOnLaunch', 'NearSauceStaging', 'ShortDashVessels',
                      'ServeBeforeSafeHeat', 'DirectPreparedFlavorThrows', 'NearReadyPotHarvest', 'WasherSidePlating',
                      'DirectCleanPassAssembly', 'NearReadyFryerHarvest', 'NearestCentralTaskPreference', 'ContinuousWaypoints',
                      'WaitForFinalSauceHead', 'PredictiveBakeryReturn', 'PlatedOnionFinish', 'FifoEmptyBowls'):
            event = initialized(invoked.call_args.args[0])
            event['value']['configuration'].pop(field)
            write_trace(path, [event])
            evidence = runner.configuration_evidence(candidate, self.args, path)
            self.assertFalse(evidence['matched'])
            self.assertEqual(evidence['mismatches'][0]['field'], field)

    def test_planner_projection_does_not_replace_observed_score(self):
        row, _ = self.evaluate(self.process(response(score=68)))
        self.assertEqual(row['score'], 68)
        self.assertFalse(row['nativeScoreAtLeast5000'])

    def test_malformed_stdout_recovers_failure_trace_without_qualification(self):
        row, _ = self.evaluate(self.process(response(score=68), exit_code=1, stdout='{partial'))
        self.assertEqual((row['score'], row['observationSource']), (68, 'trace-fallback'))
        self.assertIn('without a complete result', row['failure'])
        self.assertFalse(row['completedRequestedRun'])

    def test_outer_timeout_preserves_last_observed_state(self):
        def timeout(command, **kwargs):
            write_trace(Path(command[command.index('--out') + 1]), [call(response(score=68))], truncate=8)
            raise subprocess.TimeoutExpired(command, 70)
        row, _ = self.evaluate(timeout)
        self.assertEqual(row['score'], 68)
        self.assertIn('TimeoutExpired', row['failure'])
        self.assertIsNone(row['exitCode'])
        self.assertFalse(row['completedRequestedRun'])

    def test_failure_before_trace_has_unavailable_score(self):
        row, _ = self.evaluate(lambda command, **kwargs: subprocess.CompletedProcess(command, 1))
        self.assertEqual(row['observationSource'], 'unavailable')
        self.assertIsNone(row['score'])
        self.assertIsNone(row['traceSha256'])
        self.assertFalse(row['completedRequestedRun'])

    def test_existing_trial_outputs_are_untouched_and_process_not_started(self):
        for suffix in ['.jsonl.gz', '.result.json', '.stderr.txt']:
            with self.subTest(suffix=suffix):
                path = self.output / ('trial001' + suffix)
                path.write_bytes(b'original')
                with mock.patch.object(runner.subprocess, 'run') as invoked:
                    with self.assertRaises(FileExistsError):
                        runner.evaluate(runner.Candidate(8), 0, 1, self.args, self.controller,
                                        self.controller_hash, self.output)
                    invoked.assert_not_called()
                self.assertEqual(path.read_bytes(), b'original')
                path.unlink()

    def test_pre_trial_hash_change_stops_before_process_and_outputs(self):
        self.controller.write_bytes(b'changed before trial')
        with mock.patch.object(runner.subprocess, 'run') as invoked:
            with self.assertRaisesRegex(RuntimeError, 'Controller changed'):
                runner.evaluate(runner.Candidate(8), 0, 1, self.args, self.controller,
                                self.controller_hash, self.output)
            invoked.assert_not_called()
        self.assertEqual(list(self.output.iterdir()), [])

    def test_during_trial_hash_change_retains_observation_but_disqualifies(self):
        row, _ = self.evaluate(self.process(response(score=6000), mutate=True))
        self.assertEqual(row['score'], 6000)
        self.assertFalse(row['controllerUnchanged'])
        self.assertFalse(row['completedRequestedRun'])
        self.assertFalse(row['qualifyingNativeHighScoreRound'])


class TraceRecoveryTests(unittest.TestCase):
    def test_single_first_line_and_unclosed_footer(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'trace.gz'
            for truncate in [0, 8]:
                write_trace(path, [call(response(score=68))], truncate)
                self.assertEqual(runner.last_response(path)['state']['score'], 68)

    def test_truncated_last_record_and_invalid_rows_keep_last_complete_state(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'trace.gz'
            data = json.dumps(call(response(score=68))).encode() + b'\n42\n{bad}\n{"kind":"call"}\n{"partial"'
            path.write_bytes(gzip.compress(data))
            self.assertEqual(runner.last_response(path)['state']['score'], 68)

    def test_clipped_history_does_not_discard_a_later_complete_call(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'trace.gz'
            write_trace(path, [{'kind': 'event', 'padding': 'x' * (5 * 1024 * 1024)}, call(response(score=68))])
            self.assertEqual(runner.last_response(path)['state']['score'], 68)


class MainTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.controller = self.root / 'controller.dll'
        self.controller.write_bytes(b'offline fixture')
        self.output = self.root / 'search'
        self.argv = ['--controller', str(self.controller), '--out', str(self.output),
                     '--seeds', '8,0,8', '--rounds', '1', '--beam', '1', '--budget', '4']

    def inspection(self):
        return subprocess.CompletedProcess([], 0, json.dumps({'ok': True, 'session': {
            'dlc': 8, 'variantPlayers': 4, 'stage': 'kitchen_ready', 'variantScene': 's_Day_3_4'}}), '')

    def test_plan_only_sorts_deduplicates_and_performs_no_process_or_output_writes(self):
        output = io.StringIO()
        with mock.patch.object(runner.subprocess, 'run') as invoked, contextlib.redirect_stdout(output):
            runner.main(self.argv + ['--plan-only'])
            invoked.assert_not_called()
        plan = json.loads(output.getvalue())
        self.assertFalse(plan['executed'])
        self.assertEqual([c['seed'] for c in plan['initialCandidates']], [0, 8])
        self.assertFalse(self.output.exists())

    def test_existing_evidence_directory_stops_before_inspection(self):
        self.output.mkdir()
        marker = self.output / 'summary.json'
        marker.write_text('original')
        with mock.patch.object(runner.subprocess, 'run') as invoked:
            with self.assertRaises(FileExistsError):
                runner.main(self.argv)
            invoked.assert_not_called()
        self.assertEqual(marker.read_text(), 'original')

    def test_beam_order_budget_and_summary_use_only_native_rows(self):
        evaluated = []
        def evaluation(candidate, generation, trial, *args):
            evaluated.append(candidate)
            return {'trial': trial, 'candidate': dataclasses.asdict(candidate),
                'completedRequestedRun': True, 'controllerUnchanged': True,
                'score': 68, 'delivered': 1, 'gameplayFrame': 1175}
        with mock.patch.object(runner.subprocess, 'run', return_value=self.inspection()) as invoked, \
                mock.patch.object(runner, 'evaluate', side_effect=evaluation), contextlib.redirect_stdout(io.StringIO()):
            runner.main(self.argv)
            self.assertEqual(invoked.call_count, 1)
        self.assertEqual(evaluated, [runner.Candidate(0), runner.Candidate(8),
                                    runner.Candidate(0, 6), runner.Candidate(0, 8, 2)])
        summary = json.loads((self.output / 'summary.json').read_text())
        self.assertEqual(summary['mode'], 'native-prefix')
        self.assertEqual(len(summary['results']), 4)
        self.assertNotIn('projectedScore', summary)

    def test_hash_change_is_saved_then_stops_without_another_trial(self):
        def evaluation(candidate, generation, trial, *args):
            return {'trial': trial, 'candidate': dataclasses.asdict(candidate),
                'completedRequestedRun': False, 'controllerUnchanged': False,
                'score': 68, 'delivered': 1, 'gameplayFrame': 1175}
        with mock.patch.object(runner.subprocess, 'run', return_value=self.inspection()), \
                mock.patch.object(runner, 'evaluate', side_effect=evaluation) as evaluated:
            with self.assertRaisesRegex(RuntimeError, 'partial evidence saved'):
                runner.main(self.argv)
            self.assertEqual(evaluated.call_count, 1)
        self.assertEqual(len(json.loads((self.output / 'summary.json').read_text())['results']), 1)


if __name__ == '__main__':
    unittest.main()
