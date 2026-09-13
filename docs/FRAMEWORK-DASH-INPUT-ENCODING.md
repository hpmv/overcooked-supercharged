# Dash input encoding correction

The second W fresh-round plate search completed its walking candidate and its dash-enabled candidate in 73 frames each. The dash candidate's exact-input conversion then failed. Its emitted rows contained five dash requests with `JustPressed=true` and `Down=false`, beginning at input frame 59, trace line 4249. The native TAS button intentionally reads `Down` and derives its own edges, held duration, and claims. An upstream synthetic edge without a held state cannot start a native dash.

The unmodified 73 input/output frames are retained in `artifacts/framework-migration/native-w/plate-fresh-search-2-dash-encoding.json`, bound to the source trace prefix length and SHA256. The original failed output directory is unchanged. These malformed inputs are not corrected into a replay movie, and the equal 73-frame result is not evidence of a native dash.

`ControllerState.ApplyInputAndAdvanceFrame` now treats desired dash as a held logical button. It emits `Down=current`, `JustPressed=current && !previous`, and `JustReleased=!current && previous`, then retains that held state. The new protobuf field 11, `dash_button_down`, preserves it through saved controller history; older protobuf records default to released. `ActualControllerInput` already persists all three output fields. Native plugin gating, cooldowns, edge claims, and movement are unchanged.

The raw-input adapter continues emitting the same pads and two counted neutral tail frames. It also keeps the new controller state field synchronized, so a raw-held button followed by typed input cannot invent a second press. Existing primary/secondary behavior is unchanged.

The isolated `scripts/FrameworkControllerInputCheck` harness links the production controller transformation, button/history types, actual protobuf schema, and the shared test suite. Its 45 checks cover press/hold/release, repeated neutral input, existing pickup behavior, old-schema defaults, protobuf round trips, and continuation after branching from held state. It makes no game calls. The normal headless selftest additionally checks raw-to-typed state continuity.

```powershell
dotnet restore scripts/FrameworkControllerInputCheck/FrameworkControllerInputCheck.csproj --packages framework/.packages --source framework/.packages
dotnet run --project scripts/FrameworkControllerInputCheck/FrameworkControllerInputCheck.csproj -c Release --no-restore
python -m unittest discover -s scripts/tests -p test_framework_plate_fresh_search.py
```

The fresh search runner now saves raw exchanges, exact input rows, native endpoint receipts, and decoded attachment effects before goal or replay conversion can reject a candidate. Failed validation remains failed and includes its reason. Thirteen Python checks include the actual W malformed-dash case and verify that the native plate achievement survives as evidence while replay conversion still refuses the inconsistent inputs.

The next native check must observe a positive native dash timer or the native `StartDash` event following the corrected ordinary input edge. Source tests and a successful plate placement alone do not establish a native dash or a time saving.
