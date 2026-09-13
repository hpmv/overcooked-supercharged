The mapped Story 1-1 host is `artifacts/framework-headless-host-v12c-story11/Headless.dll` (SHA256 `00191fcc6c365f5820d58808211ee2437bbd82e7bb17d9ca3f4766a0d1133f3b`). Select `--level story11`. The prior discovery and mapped builds remain unchanged.

The setup uses all 50 first native registrations from `artifacts/framework-migration/story11-discovery-b/load.json`, step 6 (file SHA256 `21aca07f4379e9560b29918cc06a49b7f43f73a11fd2b7bc1527eaed0bca72bc`). Actual names, component multiplicities, positions and available spawn names must match before actions. Story-specific mapped component flags are also compared with the observed native components. The four corner objects 18, 20, 22 and 23 have no AttachStation; the initial mapped build's incorrect annotation was rejected by native warp preflight and is corrected in v12b.

| Role | Native IDs |
| --- | --- |
| Chefs | 43–46 |
| Fish / prawn crates | 30 / 29 |
| Chopping boards | 27, 28, 31, 32 |
| Initial plates | 1–4 |
| Delivery / clean plate return | 33 / 34 |
| Campaign flow | 40 |

Native crate metadata names `SushiFish` and `SushiPrawn`. Matching source assets identify prepared ingredient UIDs 23600 and 21875, respectively. These differ from the native order recipe IDs 22294 and 32748. Raw WorkableItem assets have eight stages; native `HasFinished` tests progress seven. The framework's 1.4-second maximum is its existing estimator. Only native source replacement and food composition prove preparation completion.

Navigation polygons come from the supplied scene's matching counter anchors, with 0.4-metre chef clearance. The observed registered BoxColliders are interaction triggers. These observations do not verify complete native solid geometry or collision paths; original native target, progress and action timeout checks remain active. Initial attachments are reconstructed from native messages rather than inferred from positions.

`prepare-primary` exposes the original native-target preparation action without issuing an interaction or claiming a spawn. The caller must recheck the observed target before a subsequent raw edge and prove assembly/delivery separately. `actions-clear all:true` clears all graph metadata only at a settled paused, fixed-only baseline without active input/action plans or live spawn claims. It preserves current entity state and does not reset native state.

Full inspection includes `graphMappingValidation`, recomputed from the same live mapping validator used for action admission. It binds the current frame, exact observed spawned paths, physical-container associations and removed-ID receipts. A failed audit returns `ok:false` with its error. The original registry layout report remains unchanged, including warnings for extra dynamic colliders; consumers may accept only the specific extra-collider warning when this current identity proof covers that exact ID.

The user-authorized level-session module starts Story 1-1's native 150-second timer immediately. This is separate from the host and from Carnival's duration. ScriptedRoundData rewind support is also a separate native module. Mapping and offline checks do not qualify a native rewind, delivery or full-round score.

The v12b build passed 65 focused checks covering actual registry data, invalid mappings, serialization, read-only mapping proof, discovery gating and modeled approaches for every chef. The full headless regression is recorded separately in `artifacts/framework-story11-regression-v12b.json`.

v12c handles a framework retirement observation for an unmapped physical proxy only when a prior native SpawnPhysicalAttachment packet associated that exact proxy with an already-retired logical owner, and its actual registry metadata still identifies a Rigidbody/ObjectContainer/PhysicsObject. The original message remains unchanged in trace. `observedProxyRetirements` records why logical-entity lookup was skipped. Unknown or unassociated IDs remain fatal; current registry absence is still established separately by the observer module.

The integration fixture replays all69,399 exchanges and eight controls from the unchanged native mapped-b trace prefix (SHA256 `9ec5a1c0b051bd576b6252cdbd10ff5ec0cc09d90a16bb9bec17b4a59ba0780a`) with zero recorded input differences, reaching actual raw51 removal and prepared53 at frame305. Because v12b threw before persisting the failed retirement callback, the fixture appends an explicitly generated retirement52 test packet using the existing codec. It proves the proxy-only fix preserves logical entities/physics/frame. Additional suffix tests cover owner53 destruction followed by proxy54 retirement in one callback, live-owner rejection, unknown999 rejection and a fresh native load. These18 checks are in `artifacts/framework-proxy-retirement-v12c.json`; they are offline reconstruction evidence, not a native replay claim.

At a newly observed native level load, v12c discards nodes authored by that headless CLI session before the original framework rebuilds the graph. It preserves supplied setup graph definitions and action-ID monotonicity. Old spawned claims become non-live through the original frame-zero reset. The `nativeReloadDiscard` receipt retains discarded action IDs and previous runtime errors; storage failures still require a fresh host/trace. Explicit post-load fixed-baseline clearing remains available.
