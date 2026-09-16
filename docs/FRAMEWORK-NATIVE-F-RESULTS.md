# Native framework migration run F

This is authoring/replay development evidence, not a high-score qualification. No migrated full round has completed. Legacy V21 remains the best completed result at2,720points/25deliveries; the requested five fresh5,000+ runs remain outstanding.

Frozen game plugin: `artifacts/framework-plugin-native-f/SuperchargedPatch.dll`, SHA256 `4cc8e7e9ae86b857ca3917a1dc8ead7f759f4ba3fccb76960484c3ac628fc6f0`.
Frozen .NET10 host: `artifacts/framework-headless-host-v4/Headless.dll`, SHA256 `0203b7144607d229c34a0243a5d3916100576a6a5b313842f077106fb5c859a9`.
Evidence: `artifacts/framework-migration/native-f/`.

Native load succeeded with four users, `s_Day_3_4`, DLC8, four-player variant,270-second native timer. Windowed1280×720 persisted through loading and all probes. The window was focused at recorded endpoints, so background movement remains unverified. A narrow virtual-pad focus exception is implemented and reported in native status; native menu/direct-control gates remain active.

| Probe | Observation | Classification |
|---|---|---|
| Initial idle: checkpoint181,120frame continuation,2replays | Native timer/order deadlines/RNG/score/food identical. Entity1/63 attachment reconstruction differed; chef height differed by0.000012873m; entity82 rotation and extinguisher quaternion differed. | Full state fails; native kitchen substate passes |
| Movement: checkpoint331,8left-input frames+2neutral, one replay | Blue chef moved x18.146301→17.306295. Same input hash and every reconstructed entity field identical at341. | One successful short authoring input replay |
| Pickup: checkpoint371,8right-input frames+1pickup+2neutral | Original picked plate12 offstation41. Same recorded inputs after rewind did not pick it up. Timer/order/food state equal. | Gameplay replay fails |
| Order generation: checkpoint562/native9.383362s,720frames,2replays | Native timer/order/RNG/score/food and all reconstructed entity fields identical at1282, including additional native orders. | Two successful12second authoring continuations |

The movement recording SHA256 is `b2ca8753035c82c7ad10356f6fba3d478dcf51bb9d69a36ebe01c3b3f563426f`; the failed pickup recording is `4f618e0cff7319e0b488db5028cf82d895fd421cb59fe0e6731614a51804b6f2`. Recordings and full observations are in their probe directories.

Measured speed:120frames took2.094wall seconds with a60FPS render target.720frames/12simulation seconds took10.81–10.84wall seconds with an unlimited render target. These idle measurements do not establish full-kitchen performance. Native logical60Hz and physics50Hz remain unchanged.

The pickup failure has a concrete omitted native field: `ClientPlayerControlsImpl_Default.Update_Carry` compares `ClientTime.Time()` against `m_lastPickupTimestamp`. The clock rewound while this cooldown retained its later value. Exact capture/restoration of that field is being added, along with native use-suppression state; the proposed fix still needs the same native probe.

Future G/V5 source work is separate from this frozen evidence: preserve intro packets at frame0, restore native cannon components/codecs and native synchronization/lifecycle behavior, retain native plate retirement delay, and permit only settled cannon authoring checkpoints. None of those changes should be credited to run F.

Search tooling now consists of `scripts/framework_search.py` (demand-capped multidimensional WIP, deterministic bounded beam/Pareto selection and reservations;28offline checks) and `scripts/framework_native_search.py` (actual bridge/headless evaluator, recorded-prefix restoration, exact input export and native food observations). The native search adapter has not yet run; it waits for the demonstrated pickup rewind failure to be fixed. Its explicit two neutral release frames count against the search frame budget. Typed action primitives are being exposed through the headless API to give the optimizer useful larger actions before fine input refinement.
