# Story11 scripted recipe checkpoints

Frozen X only recognizes its sealed `WarpableRoundData` type. Story1-1 instead returns native `ScriptedRoundData`: six manual entries first, followed by native weighted selection. The manual branch increments `RecipeCount` without updating weighted frequencies or consuming RNG. Applying the weighted-only invariant to those draws would reject legitimate native behavior.

External module [ScriptedRound-r2](../framework-run/modules/ScriptedRound-r2/manifest.json), DLL SHA256 `76B123665E0BB20D1B97AB322C3D64E04DF66C5BB6D385E68400BEDE425F92FE`, provides the bounded adapter against frozen X `391F6265…BD746`. No permanent core file changes are required.

The module constructs the existing wrapper shell and binds its private `original` reference to the **actual native ScriptedRoundData before initialization**. The temporary base source is never initialized or drawn. The recipe-list object, its array and native entry objects are retained by reference. A Harmony prefix handles only the manual portion by calling that actual native generator and checking the exact returned manual entry, one cursor increment, unchanged frequencies and unchanged per-round RNG. It appends the ordinary frozen checkpoint/history. Subsequent draws execute the existing frozen wrapper, which calls the original ScriptedRoundData and its native weighted fallback. Native initialization, weighting, timer data and ambient RNG preservation remain on their existing paths.

Manual entries are distinct from weighted entries. For the existing diagnostic-history schema, each must map uniquely to a weighted entry with the **same native order-definition reference and base score**. That mapped index is a diagnostic identity, not evidence that the manual entry was randomly selected. Module status preserves the original manual sequence and declares this mapping. Missing/ambiguous identities, array replacement, modified recipe data, wrong instance owners or unexpected draw changes reject.

Native [load/activation](../artifacts/story11/scripted-round-r2-load.json) and [validate-current](../artifacts/story11/scripted-round-r2-validate.json) succeeded in the existing process. Validation receipt SHA256 `CA9605869DF82FDF8456DA9D1DEC364D3496E773B94D9B262D17AA8D5C23BB93` confirms original type `ScriptedRoundData`, unchanged recipe list/array, and manual Fish22294, Fish, Prawn32748, Fish, Prawn, Fish, all base20 and distinct from weighted entry objects. **Zero manual calls occurred during validation.** It neither adopts nor rewrites the already-running unwrapped round. Native initialization, preview and rewind must be tested after reload.

Load into slot `scripted-round`, entry `SuperchargedPatch.Authoring.Modules.ScriptedRoundModule`, then use these hot-call operations with empty args:

1. `activate` installs the scoped getter/manual-draw hooks.
2. `validate-current` checks the actual current raw Story11 generator without drawing or replacing it.
3. Reload through `level-session/load-main-1-1` with the mapped controller ready. The new native round receives the wrapper during normal setup.
4. `status` reports binding identity and manual calls, including scratch preview calls.

`deactivate` and disposal reject while any live native order controller owns a bound wrapper, even after all six manual entries have passed: previews and rewinds can revisit them. Leave/reload to an unwrapped level before removing the module. Do not hot-replace it in the middle of an owned round. Existing native snapshots remain bound to their original round identity.

The [62-check offline report](../artifacts/framework-scripted-round-tests.json) (SHA256 `04D0FE32D57484D493B2D5C10386B7F69C6DE1A0D8F61B4843FFD01DCA895ADE`) checks the native scripted/weighted source bodies against the production adapter and frozen wrapper body, including rewind at0/5/6/9, forward seek, preview across the transition, exception recovery, identity negatives and hook-removal rejection. The CPU harness uses controlled Unity RNG and an explicitly recorded prefix-dispatch shim, not a live Unity/Harmony runtime. Installed native IL also confirms the scripted method's cursor-only manual branch and native-base fallback. These checks do not establish live RNG equivalence, physical rewind or score qualification.
