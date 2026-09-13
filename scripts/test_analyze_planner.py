"""Small offline fixtures for analyzer accounting and partial-stream behavior."""
import gzip
import io
import json
import tempfile
import unittest
from pathlib import Path

from analyze_planner import Prefix, analyze


def state(frame, occupied=False, delivery=None):
    return {"scene": "s_Day_3_4", "gameplayFrame": frame, "levelFrameZero": 400,
            "timer": 270 - frame / 60, "score": 68 if delivery else 0, "delivered": 1 if delivery else 0,
            "serverRoundActive": True, "clientRoundActive": True,
            "chefs": [{"playerId": 2, "position": {"x": 0, "z": 0}, "controlsEnabled": True}],
            "entities": [{"id": 2, "name": "utensil_pot_01", "active": True,
                          "components": ["CarryableItem"], "position": {"x": 18, "z": -10.8},
                          "contents": [{"type": "IngredientAssembledNode", "id": 284626}] if occupied else []}],
            "gameEvents": [delivery] if delivery else []}


def call(frame, **kwargs):
    return {"kind": "call", "request": {"command": "step", "steps": 1}, "response": {"state": state(frame, **kwargs)}}


def event(event_name, **kwargs):
    return {"kind": "event", "name": event_name, "value": kwargs}


class PlannerAnalysisTests(unittest.TestCase):
    def run_trace(self, entries, truncate=0, invalid=False):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "trace.jsonl.gz"
            raw = b"".join(json.dumps(entry).encode() + b"\n" for entry in entries)
            if invalid:
                raw += b"{invalid}\n"
            data = gzip.compress(raw)
            path.write_bytes(data[:-truncate] if truncate else data)
            return analyze(path)

    def test_job_idle_stage_and_inventory_accounting(self):
        result = self.run_trace([call(0),
            event("plannerJobStart", player=2, name="supply-Frankfurter", frame=0),
            event("actionCreated", player=2, type="take"),
            event("actionStage", player=2, type="take", stage="navigate", actionFrames=0),
            call(3), event("actionStage", player=2, type="take", stage="transfer", actionFrames=3),
            call(6, occupied=True), event("actionComplete", action={"player": 2, "type": "take"}, frames=6),
            event("plannerJobComplete", player=2, name="supply-Frankfurter", frame=6), call(12, occupied=True)])
        segment = result["segments"][0]
        self.assertEqual(segment["classification"], "partial-native-run")
        self.assertEqual(segment["players"][0]["assignedSeconds"], .1)
        self.assertEqual(segment["players"][0]["idleNoJobSeconds"], .1)
        self.assertEqual(segment["completedJobs"][0]["totalSeconds"], .1)
        self.assertEqual([stage["totalSeconds"] for stage in segment["completedStages"]], [.05, .05])
        self.assertEqual(segment["averageInventory"]["potEmpty"], .5)
        self.assertEqual(segment["intervals"]["pot:2.empty"]["totalSeconds"], .1)
        self.assertEqual(segment["largestSampleGapFrames"], 6)

    def test_pending_delivery_finalizes_under_same_index(self):
        pending = {"index": 0, "kind": "delivery", "nativeMatch": True, "scoreApplied": False}
        final = {**pending, "scoreApplied": True, "deliveryDelta": 1}
        segment = self.run_trace([call(0), call(1, delivery=pending), call(2, delivery=final), call(3, delivery=final)])["segments"][0]
        self.assertEqual(segment["nativeConfirmedDeliveries"], 1)
        self.assertEqual(len(segment["nativeEvents"]), 1)

    def test_shared_chop_closes_raw_supplier_span_without_prepared_claim(self):
        def both(frame):
            row = call(frame)
            row['response']['state']['chefs'].append(
                {'playerId': 0, 'position': {'x': 1, 'z': 0}, 'controlsEnabled': True})
            return row
        segment = self.run_trace([both(0),
            event('plannerJobStart', player=2, name='supply-HotdogBun', frame=0, resources=[68, 23]),
            both(6), event('pantryChopDelegated', supplier=2, helper=0, originalJob='supply-HotdogBun',
                           frame=6, board=23, rawEntity=147),
            event('plannerJobStart', player=0, name='shared-pantry-chop-HotdogBun', frame=6, resources=[23, 147]),
            event('plannerJobStart', player=2, name='supply-Frankfurter', frame=6, resources=[70, 7]),
            both(9), event('plannerJobComplete', player=2, name='supply-Frankfurter', frame=9),
            both(12), event('plannerJobComplete', player=0, name='shared-pantry-chop-HotdogBun', frame=12),
            both(18)])["segments"][0]
        timeline = segment['recentCompletedJobTimeline']
        delegated = next(j for j in timeline if j['job'] == 'supply-HotdogBun')
        self.assertEqual(delegated['seconds'], .1)
        self.assertEqual(delegated['completionKind'], 'raw-placement-delegated')
        self.assertFalse(delegated['preparedFoodCallbackCompleted'])
        self.assertEqual(delegated['helper'], 0)
        self.assertFalse(segment['anomalies'])
        players = {p['player']: p for p in segment['players']}
        self.assertEqual(players[2]['assignedSeconds'], .15)
        self.assertEqual(players[0]['assignedSeconds'], .1)

    def test_early_pan_restore_callback_preserves_both_jobs_and_assigned_time(self):
        # Exact production names/order: Work.Complete begins the restore child
        # before CompleteWork logs completion of the onion-combine predecessor.
        def central_call(frame):
            entry = call(frame)
            entry["response"]["state"]["chefs"][0]["playerId"] = 0
            return entry
        entries = [central_call(0),
            event("plannerJobStart", player=0, name="finish-early-onion-5", frame=0, resources=[177, 38]),
            central_call(12), event("plannerJobStart", player=0, name="early-onion-restore-empty-pan-5", frame=12, resources=[]),
            event("plannerJobComplete", player=0, name="finish-early-onion-5", frame=12),
            central_call(18), event("plannerJobComplete", player=0, name="early-onion-restore-empty-pan-5", frame=18),
            central_call(24)]
        segment = self.run_trace(entries)["segments"][0]
        self.assertEqual(segment["anomalies"], {})
        self.assertEqual(segment["openJobs"], [])
        self.assertEqual(segment["players"][0]["assignedSeconds"], .3)
        self.assertEqual(segment["players"][0]["idleNoJobSeconds"], .1)
        timeline = segment["recentCompletedJobTimeline"]
        self.assertEqual([(j["job"], j["startFrame"], j["endFrame"], j["resources"]) for j in timeline], [
            ("finish-early-onion-5", 0, 12, [177, 38]), ("early-onion-restore-empty-pan-5", 12, 18, [])])
        self.assertEqual([j["totalSeconds"] for j in segment["completedJobs"]], [.2, .1])
        entries[3], entries[4] = entries[4], entries[3]
        self.assertEqual(segment, self.run_trace(entries)["segments"][0],
                         "same-frame callback order must have the exact ordinary completion/start report")

    def test_same_name_callback_matches_oldest_pending_start(self):
        segment = self.run_trace([call(0), event("plannerJobStart", player=2, name="chop-native-item", frame=0),
            call(6), event("plannerJobStart", player=2, name="chop-native-item", frame=6),
            event("plannerJobComplete", player=2, name="chop-native-item", frame=6), call(9),
            event("plannerJobComplete", player=2, name="chop-native-item", frame=9)])["segments"][0]
        self.assertEqual(segment["anomalies"], {})
        self.assertEqual(segment["openJobs"], [])
        self.assertEqual(segment["players"][0]["assignedSeconds"], .15)
        self.assertEqual([(j["startFrame"], j["endFrame"]) for j in segment["recentCompletedJobTimeline"]], [(0, 6), (6, 9)])

    def test_cannon_resume_callback_excludes_child_from_original_active_time(self):
        entries = [call(0), event("plannerJobStart", player=2, name="unplated-hotdog-2", frame=0, resources=[23, 153, 2, 33]),
            call(6), event("plannerJobPaused", player=2, name="unplated-hotdog-2", frame=6, resources=[153, 2, 33]),
            event("plannerJobStart", player=2, name="interrupt-fire-left", frame=6, resources=[84, 78]), call(10),
            event("plannerJobResumed", player=2, name="unplated-hotdog-2", frame=10, pausedAtFrame=6, pausedFrames=4, resources=[153, 2, 33]),
            event("plannerJobComplete", player=2, name="interrupt-fire-left", frame=10), call(18),
            event("plannerJobComplete", player=2, name="unplated-hotdog-2", frame=18), call(24)]
        segment = self.run_trace(entries)["segments"][0]
        self.assertEqual(segment["anomalies"], {})
        self.assertEqual(segment["openJobs"], [])
        self.assertEqual(segment["players"][0]["assignedSeconds"], .3)
        self.assertEqual(segment["players"][0]["idleNoJobSeconds"], .1)
        self.assertEqual(segment["players"][0]["pausedWorkSeconds"], .0667)
        parent = next(j for j in segment["recentCompletedJobTimeline"] if j["job"] == "unplated-hotdog-2")
        self.assertEqual((parent["startFrame"], parent["endFrame"], parent["seconds"], parent["elapsedSeconds"], parent["pausedSeconds"]),
                         (0, 18, .2333, .3, .0667))
        self.assertEqual(segment["recentJobSuspensionTimeline"], [dict(player=2, job="unplated-hotdog-2", startFrame=6, endFrame=10,
                                                                    seconds=.0667, resources=[153, 2, 33])])
        self.assertEqual(segment["completedJobSuspensions"][0]["totalSeconds"], .0667)
        self.assertEqual(sum(j["seconds"] for j in segment["recentCompletedJobTimeline"]), .3)
        entries[6], entries[7] = entries[7], entries[6]
        self.assertEqual(segment, self.run_trace(entries)["segments"][0], "resume-before-child-complete must equal ordinary same-frame ordering")

    def test_partial_cannon_interrupt_keeps_original_ownership_and_paused_time(self):
        segment = self.run_trace([call(0), event("plannerJobStart", player=2, name="original", frame=0, resources=[2, 23]),
            call(6), event("plannerJobPaused", player=2, name="original", frame=6, resources=[2]),
            event("plannerJobStart", player=2, name="interrupt-fire-left", frame=6), call(12)])["segments"][0]
        self.assertEqual(segment["completedJobs"], [])
        self.assertEqual(segment["completedJobSuspensions"], [])
        original = next(j for j in segment["openJobs"] if j["name"] == "original")
        self.assertTrue(original["suspended"])
        self.assertEqual((original["observedSeconds"], original["elapsedSeconds"], original["pausedSeconds"]), (.1, .2, .1))
        self.assertEqual(original["ownedResourcesAtPause"], [2])
        self.assertEqual(segment["players"][0]["assignedSeconds"], .2)

    def test_repeated_non_nested_interrupts_accumulate_only_actual_suspensions(self):
        segment = self.run_trace([call(0), event("plannerJobStart", player=2, name="original", frame=0), call(3),
            event("plannerJobPaused", player=2, name="original", frame=3), event("plannerJobStart", player=2, name="fire", frame=3),
            call(6), event("plannerJobResumed", player=2, name="original", frame=6), event("plannerJobComplete", player=2, name="fire", frame=6),
            call(9), event("plannerJobPaused", player=2, name="original", frame=9), event("plannerJobStart", player=2, name="fire", frame=9),
            call(15), event("plannerJobResumed", player=2, name="original", frame=15), event("plannerJobComplete", player=2, name="fire", frame=15),
            call(18), event("plannerJobComplete", player=2, name="original", frame=18)])["segments"][0]
        self.assertEqual(segment["anomalies"], {})
        parent = next(j for j in segment["recentCompletedJobTimeline"] if j["job"] == "original")
        self.assertEqual((parent["seconds"], parent["pausedSeconds"]), (.15, .15))
        self.assertEqual(segment["completedJobSuspensions"][0]["count"], 2)
        self.assertEqual(segment["completedJobSuspensions"][0]["totalSeconds"], .15)

    def test_nested_pause_is_reported_without_overwriting_suspended_work(self):
        segment = self.run_trace([call(0), event("plannerJobStart", player=2, name="original", frame=0), call(3),
            event("plannerJobPaused", player=2, name="original", frame=3), event("plannerJobStart", player=2, name="fire", frame=3),
            call(6), event("plannerJobPaused", player=2, name="fire", frame=6), call(9)])["segments"][0]
        self.assertEqual(segment["anomalies"], {"jobPauseWhileAnotherSuspended": 1})
        self.assertEqual({j["name"] for j in segment["openJobs"]}, {"original", "fire"})
        self.assertEqual(next(j for j in segment["openJobs"] if j.get("suspended"))["name"], "original")

    def test_unmatched_pause_resume_leave_active_work_intact(self):
        segment = self.run_trace([call(0), event("plannerJobStart", player=2, name="original", frame=0), call(3),
            event("plannerJobPaused", player=2, name="wrong", frame=3),
            event("plannerJobResumed", player=2, name="wrong", frame=3), call(6),
            event("plannerJobComplete", player=2, name="original", frame=6)])["segments"][0]
        self.assertEqual(segment["anomalies"], {"jobPauseWithoutMatchingActiveJob": 1, "jobResumeWithoutMatchingPause": 1})
        self.assertEqual(segment["completedJobs"][0]["totalSeconds"], .1)

    def test_resume_metadata_mismatch_uses_observed_event_interval_and_reports_loss(self):
        segment = self.run_trace([call(0), event("plannerJobStart", player=2, name="original", frame=0), call(3),
            event("plannerJobPaused", player=2, name="original", frame=3, resources=[2]),
            event("plannerJobStart", player=2, name="fire", frame=3), call(9),
            event("plannerJobResumed", player=2, name="original", frame=9, pausedFrames=600, pausedAtFrame=2, resources=[99]),
            event("plannerJobComplete", player=2, name="fire", frame=9), call(12),
            event("plannerJobComplete", player=2, name="original", frame=12)])["segments"][0]
        self.assertEqual(segment["anomalies"], {"jobResumeTimingMetadataMismatch": 1, "jobResumeResourceMismatch": 1})
        self.assertEqual(segment["completedJobSuspensions"][0]["totalSeconds"], .1)
        self.assertEqual(next(j for j in segment["recentCompletedJobTimeline"] if j["job"] == "original")["seconds"], .1)

    def test_paused_job_cannot_complete_without_a_matching_resume(self):
        segment = self.run_trace([call(0), event("plannerJobStart", player=2, name="original", frame=0), call(3),
            event("plannerJobPaused", player=2, name="original", frame=3), call(6),
            event("plannerJobComplete", player=2, name="original", frame=6)])["segments"][0]
        self.assertEqual(segment["anomalies"], {"jobCompletionWithoutMatchingStart": 1})
        self.assertTrue(segment["openJobs"][0]["suspended"])
        self.assertEqual(segment["players"][0]["idleNoJobSeconds"], .05)
        self.assertEqual(segment["players"][0]["pausedWorkSeconds"], .05)

    def test_unmatched_completion_does_not_erase_current_job(self):
        segment = self.run_trace([call(0), event("plannerJobStart", player=2, name="supply-Frankfurter", frame=0),
            call(3), event("plannerJobComplete", player=2, name="missing-job", frame=3), call(6),
            event("plannerJobComplete", player=2, name="supply-Frankfurter", frame=6)])["segments"][0]
        self.assertEqual(segment["anomalies"], {"jobCompletionWithoutMatchingStart": 1})
        self.assertEqual(segment["players"][0]["assignedSeconds"], .1)
        self.assertEqual(segment["completedJobs"][0]["totalSeconds"], .1)

    def test_unresolved_replacement_retains_missing_completion_evidence(self):
        segment = self.run_trace([call(0), event("plannerJobStart", player=2, name="older", frame=0),
            call(6), event("plannerJobStart", player=2, name="successor", frame=6), call(12)])["segments"][0]
        self.assertEqual(segment["anomalies"], {"jobStartBeforePreviousCompletion": 1})
        self.assertEqual({j["name"] for j in segment["openJobs"]}, {"older", "successor"})
        self.assertTrue(next(j for j in segment["openJobs"] if j["name"] == "older")["completionPendingAfterReplacement"])
        self.assertEqual(segment["completedJobs"], [])

    def test_overlap_across_native_frames_is_not_excused_as_callback_order(self):
        segment = self.run_trace([call(0), event("plannerJobStart", player=2, name="older", frame=0),
            call(6), event("plannerJobStart", player=2, name="successor", frame=6), call(7),
            event("plannerJobComplete", player=2, name="older", frame=7), call(12),
            event("plannerJobComplete", player=2, name="successor", frame=12)])["segments"][0]
        self.assertEqual(segment["anomalies"], {"jobStartBeforePreviousCompletion": 1})
        self.assertEqual(segment["openJobs"], [])
        self.assertEqual([(j["startFrame"], j["endFrame"]) for j in segment["recentCompletedJobTimeline"]], [(0, 7), (6, 12)])

    def test_missing_completion_memory_is_bounded_and_loss_is_reported(self):
        entries = [call(0)] + [event("plannerJobStart", player=2, name=f"job-{i}", frame=0) for i in range(18)] + [call(1)]
        segment = self.run_trace(entries)["segments"][0]
        self.assertEqual(len(segment["openJobs"]), 17)
        self.assertEqual(segment["anomalies"], {"jobStartBeforePreviousCompletion": 17, "jobPendingCompletionLimitExceeded": 1})
        self.assertEqual(segment["completedJobs"], [])

    def test_incomplete_gzip_footer_is_a_partial_observation(self):
        report = self.run_trace([call(0), call(1)], truncate=8)
        self.assertEqual(report["streamStatus"], "partial-gzip-prefix")
        self.assertEqual(report["segments"][0]["end"]["gameplayFrame"], 1)

    def test_invalid_complete_line_is_not_accepted_as_live_tail(self):
        report = self.run_trace([call(0)], invalid=True)
        self.assertEqual(report["streamStatus"], "invalid-stream")

    def test_frame_reset_creates_separate_segment(self):
        report = self.run_trace([call(0), call(10), call(0), call(1)])
        self.assertEqual(len(report["segments"]), 2)
        self.assertEqual([s["end"]["gameplayFrame"] for s in report["segments"]], [10, 1])

    def test_prefix_cannot_follow_new_writer_bytes(self):
        prefix = Prefix(io.BytesIO(b"oldnew"), 3)
        self.assertEqual(io.BufferedReader(prefix).read(), b"old")

    def test_native_warning_is_not_a_burn_claim(self):
        first, last = call(0, occupied=True), call(12, occupied=True)
        for entry in (first, last):
            entry["response"]["state"]["entities"][0].update(cookingTime=12, cookingProgress=23.7)
        segment = self.run_trace([first, last])["segments"][0]
        self.assertTrue(segment["vessels"]["pot:2"]["overdoingWarning"])
        self.assertEqual(segment["vessels"]["pot:2"]["secondsToNativeBurnThresholdIfHeating"], .3)
        self.assertEqual(segment["burnOrOvermixEntityCount"], 0)
        self.assertEqual(segment["intervals"]["pot:2.overdoingWarning"]["totalSeconds"], .2)


if __name__ == "__main__":
    unittest.main()
