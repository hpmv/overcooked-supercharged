# Resume phase after a delayed paused RPC

The native X strict comparison remains failed. In idle epoch 0 versus epoch 2, every `FramesSinceLastNoPhysicsFrame` differs across the 60 advancing frames. `PhysicsFramesElapsed` also differs on 20 frames: the zero-physics callback moves from frames 34, 40, … to 35, 41, …. Matching physical values and native message bytes do not make this phase mismatch a pass.

The immutable prefix is `artifacts/framework-migration/native-x/offline-exact-frames/exchange-through-movement-replay-131.jsonl`, SHA-256 `65a9c78cda66b81d8a47c0f5f2e8ecfa111e45a5b847577daac684bad4945dfb`.

| Idle attempt | RequestResume source line / phase | Actual resume line / phase | First advancing frame32 line / phase |
|---|---|---|---|
| Original epoch0 | 1567 / 0 | 1568 / 3 | 1569 / 4 |
| Replay epoch1 | 1635 / 0 | 1636 / 3 | 1637 / 4 |
| Replay epoch2 | 1708 / 0 | 1709 / 2 | 1710 / 3 |

The original controller selects a resume reply using `(observedPhase + 1) % 6 == savedPhase`. The paused pump keeps Unity responsive while that reply is in flight. Extra paused callbacks mean the reply can be consumed later than the immediately following callback assumed by this formula. The trace directly establishes the phase changes above; the exact count of skipped Unity callbacks is not recoverable from these published rows alone.

## External module

`framework/modules/resume-phase/ResumePhaseModule.cs` is compiled separately against immutable X. It implements the existing `IAuthoringModule` API 1 and installs only two owned Harmony prefixes after explicit paused activation:

1. `InjectorServer.PublishReply`: bind the exact plain `RequestResume` object to its immutable originating observation, connection epoch, exchange ID, and phase. This worker-thread observer touches only managed fields and a locked bounded list.
2. `ControllerHandler.LateUpdate`: acquire through the existing nonblocking input reader, retain that exact accepted reply, and defer the original body until the **current actual** phase equals `(originPhase + 1) % 6`. Deferred callbacks preserve the existing native-message flush, pending events, and per-callback physics reset. They perform no capture, commit, new RPC, or gameplay-frame acceptance. At the matching phase, the unchanged core handler captures, resumes, and acknowledges once.

The module validates capture60/fixed50 (`Time.captureFramerate == 60`, `Time.fixedDeltaTime == 0.02f`) and the frozen private-envelope reflection contract. It accepts only plain alignment resumes with no input pad payload, warp, pause, seed, or speed change. Unknown provenance, changed connection, unsupported timing, and a wait beyond 12 distinct Unity callbacks fail through the existing neutral bridge fence plus `AUTHORING_RESUME_PHASE_FAILED`. It never writes a phase counter, time, or physics value, and never patches `Helpers.Resume`; warp's temporary native resume is untouched. Running callbacks bypass the module.

Activation/deactivation owns only this revision's Harmony hooks. Retirement of a held resume fences it first. A second active resume-phase revision is rejected. Status contains bounded execution receipts with source/target/executed phase, epoch/exchange, and held callback count.

## Build and validation

The ready module is:

- `framework-run/modules/ResumePhase-r1b/ResumePhase.r1b.dll`
- SHA-256 `3d7ff605802145c12fca06afafd9f1184bb1848a93ddbba5e6eef9587f0c4116`
- X core SHA-256 `391f6265321ae57c7924e723ecb0c3ec3f9f9c91baadd42aac2521ec624bd746`
- Entry type `SuperchargedPatch.Authoring.Modules.ResumePhaseModule`
- Suggested slot `resume-phase`; explicit `activate`, `status`, and `deactivate` operations take no arguments.

`scripts/Build-FrameworkModule.ps1` produced a CLR2 DLL against the actual frozen X core and installed managed game assemblies. No permanent core, native runtime, or V11 controller was rebuilt. `scripts/FrameworkResumePhaseCheck` passes 38 checks, covering delays of 1/2/3/6 callbacks, the exact captured phase0→phase2 failure, single input application and acknowledgement, event retention, epoch/provenance/combined-input/timing/timeout/retirement failures, warp/running bypass, and compiled module/hook signatures. It uses actual module/server/handler source with stubbed Harmony/native surfaces and opens no socket. The report is `artifacts/framework-resume-phase-tests.json`.

Actual Harmony installation and native continuation parity remain root-owned validation. Record both the original continuation and replays with the module active. Old X epoch0 already resumed at the wrong phase and must remain failed evidence; the corrected continuation should not be forced to match its old first-advance phase4.

## r1c acceptance-race correction and native r1b evidence

The initial r1b prefix returned to the original LateUpdate body after a missing reply. A reply arriving during the original body's message flush could then pass its second input poll without the phase guard. Revision r1c closes that demonstrated window: a missing prefix reply flushes the existing native messages, calls `SkipPausedCallback`, and returns false. A reply arriving during that flush remains queued for the next guarded callback. The targeted regression injects exactly that arrival and proves one guarded acceptance at the required phase.

Ready r1c DLL: `framework-run/modules/ResumePhase-r1c/ResumePhase.r1c.dll`, SHA256 `1310f5ac6c812a2e1f80e46dcc96d645df1382e30a16f60008a008bb74969ea2`, same frozen X core and entry type. The focused test report now contains42 passing checks. The r1b DLL and its original native records remain preserved.

Independent r1b strict proof is in `artifacts/framework-migration/native-x-v11/offline-exact-frames`: three60-frame idle replays and one10-frame movement replay pass all categories. Its11-frame pickup replay matches all physical/chef/phase/input/auxiliary fields but fails at165 due to one extra native WorldObject packet for plate12; no raw-event waiver is applied. These r1b results cannot be relabeled as r1c tests. Later r1c endpoint and strict results are separate parent-owned artifacts.

The later combined r1c + BodyRestore r4 + WorldSyncCache r1b native experiment has now been independently audited in `artifacts/framework-migration/native-x-v11b/offline-exact-frames-synced-r4`. All201 advancing replay frames pass: three60-frame idle intervals,10 movement frames, and11 actual pickup frames, including exact phase/PhysicsFramesElapsed and raw message bytes/order. Its immutable9,126,042-byte source prefix has SHA256 `4f36505982f1dc66a979477a3c4422bca5e22e66e6ff0ae1ff42a17ffe50606d`. The older extra-packet failure remains separate unchanged evidence.
