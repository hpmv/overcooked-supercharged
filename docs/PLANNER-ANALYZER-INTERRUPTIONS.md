# Planner interruption accounting

`scripts/analyze_planner.py` understands `plannerJobPaused` and `plannerJobResumed`. It retains the original job's start and resource declaration while the cannon-firing child appears as an ordinary active job. A resume emitted before the child's same-frame `plannerJobComplete` is matched through the existing bounded displaced-job mechanism; it does not restart or erase the original job.

Completed original-job `seconds` and aggregate `completedJobs` durations exclude explicit suspended intervals. Its timeline additionally reports `elapsedSeconds` and `pausedSeconds`. The child owns its own active time. Separate `completedJobSuspensions` and `recentJobSuspensionTimeline` report the suspended intervals and resources recorded at pause. `players[].pausedWorkSeconds` is an overlapping diagnostic: the same chef may actively fire a cannon or be idle while its original work remains suspended, so it must not be added to assigned time as another chef-time total.

A trace ending during interruption retains both open jobs, marks the original `suspended`, and reports its active time up to the pause, total elapsed and suspended time through the last observed frame, and `ownedResourcesAtPause`. Only one suspended work item is retained globally, matching the controller contract. Nested pauses, unmatched pause/resume/completion, invalid timing, or changed resume resources are reported as anomalies without replacing the valid suspended job. Reported pause durations use the actual event-frame interval; inconsistent duration metadata is flagged.

Twenty offline tests pass, including seven interruption fixtures and all thirteen previous streaming/accounting fixtures. The completed callback-order fixture produces an identical full segment report whether resume comes before or after the child's same-frame completion. The parent job's active time plus the child's active time equals the observed assigned chef time in that fixture.

```powershell
C:\Python312\python.exe -m unittest discover -s scripts -p test_analyze_planner.py -v
```

Evidence: `artifacts/planner-analyzer-pause-tests.txt`. No native trace, controller input, game state, or score is changed by this analyzer update.
