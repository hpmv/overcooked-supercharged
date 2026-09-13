# Prepared-bun counter landing: bounded integration proposal

The corrected C probe proves that P2 can throw one native chopped bun onto the ordinary counter at(19.2,−15.6), observed entity38. It does not establish equivalent trajectories to other counters, from other release poses, or through moving chefs. Keep any integration off by default and outside the current V16 correctness freeze.

## What the native recording establishes

`artifacts/chopped-bun-counter-landing-c-proof.json` identifies raw bun123/ordinal122, its native chopped replacement125/ordinal124, source board23/ordinal22, and target38/ordinal37. Native chopping was observed for84 frames. Original plate10 initially occupied38; C legally moved it to32 before the throw. Production should simply decline the option when38 is occupied rather than create an unsolicited plate-relocation job.

The authored walking target is(12.8,−12.4), but the actual stationary release pose is(12.8440742,0.0499992371,−12.4410744). Heading is approximately(0.895497262,0,−0.4450674). At the release decision, native `framesSinceNoPhysics` is5; the next recorded frame397 begins flight. The same bun finishes flight and attaches to38 at438. It is stable enough for the throw action to complete at444, is retrieved by P0 at499 and replaced on38 at507/509.

The proof trace SHA256 is `1b871798e8419324d32d04aa7324d3c61be6f7d059c1ad76f4e70eecdbc8f97d`; the route SHA256 is `c786e47f78286361ecf4cb9bd00e993f3b41a638ff7fdfdb717529e4ec3050a7`. It ran the frozen V15 controller `bc623154f7cdfd4b8ca09ab2652fd5d9ead97838268215ff2f54d78a09730f77` and native plugin `7d5fa9fb67a17cfc974d80e2416620bfa4f6ab2cc213ac8a33b890aa7b6fafbd`.

Native `ServerAttachStation.CanHandleCatch` requires a stopped throwable for attachment to an empty ordinary counter. This is landing followed by native attachment, not a processing vessel consuming an airborne ingredient. The production completion barrier must require the exact bun attached to the exact counter; a stationary bun merely inside a rectangular landing area is insufficient.

## Measured workload tradeoff

In C, chopping completes343; P2 takes the prepared bun by351, walks to staging by379, releases396, and finishes the throw job445. The post-chop supplier cost is102 frames, or1.70 seconds. This excludes the probe's deliberate center-clearing and initial plate-relocation jobs.

V15's eight completed ordinary center bun-buffer jobs take792 total frames, or13.20 chef-seconds, with a1.65-second mean. Only two use38:68 frames at1966–2034 and111 frames at3383–3494. This optional throw mainly moves work from a center to P2. It has no demonstrated total-work or score advantage, and P2 also supplies hotdogs and serves dishes. It should lose admission to a ready service wave or an immediate pot/bun prerequisite.

## Exact optional transaction

If later implemented, use a separate `PreparedBunCounterThrows=false` / `--prepared-bun-counter-throws` option, requiring the existing bun-buffer and pantry-chopping options. Do not widen generic `Supply` or generic counter throwing.

1. At P2's completed native chopping boundary, require the exact pure chopped-bun replacement on board23, normal current recipe demand for at least two buns, no existing/in-flight bun buffer, native empty hands/control, and free target38. Resolve board and counter by scene properties, then retain their observed identities and positions. Preserve the current stock cap and protected FIFO workspace rules.
2. Reserve board23, bun, target38 and the validated nearby supply lane before take/staging. At the native release in C, board23 and handoff45 are empty. Their centers lie only0.528 and0.547 units from the recorded horizontal trajectory. Occupancy on45 therefore cannot be ignored. Keep both surfaces empty and leased through flight; compare relevant physical-collider geometry against the reference lane and reject unknown loose objects or parked vessels near it.
3. Use only the C walking staging/aiming procedure. Inspect actual pose, stationary velocity, heading, original source composition/incarnation and native physics phase before arming and again at release. Bounds or phase waiting would be experimental admission guards, not proof that nearby conditions are ballistic-equivalent. Do not change native throw force, drag, timing or physics.
4. If admission becomes invalid before arming, leave the bun on its source board or legally return the still-held exact bun to that reserved board, verify the placement, and release only this transaction. The ordinary center buffer remains the fallback. Once armed, unexpected invalidation fails the candidate with explicit armed-state diagnostics; neutral cleanup can release an armed ingredient, so it cannot be labeled safe retention or successful recovery.
5. Require the exact source ordinal/registration and P2 provenance during flight. Success needs P2 empty-handed, observed native flight, `previousThrowerEntityId` matching P2, flight ended, no chef holder, exact unchanged pure-bun composition, and target38 attached to that source for the normal stable interval. A floor landing, another counter, ingredient consumption or chef interception is a failure. The existing pot-only intercepted-source recovery does not apply.
6. Only after that barrier publish `bufferedBun`/`bunBufferCounter`, or keep the in-flight reservation unavailable through the existing free-resource predicate. Normal `BuildUnplatedHotdog` can then consume this buffer and reuse38 for the resulting unplated meal. A native replacement raw→chopped ID must never be treated as mutation of the raw entity.

## Remaining traffic requirement

C explicitly moves both center chefs away and keeps them neutral. At release, P0 is(20.3387,−19.1586) and P3 is(23.7992,−19.1968). Production's station leases currently do not constrain either chef's future movement through this arc.

Native automatic chef catching is wider than physical capsule collision: `ServerAttachmentCatcher` requires empty hands, a different thrower, distance at most the observed1.8 and an incoming angle within120 degrees; `ServerCatchableItem` permits chef catching after0.1 seconds of flight. A physical trajectory sweep alone is insufficient. Merely observing safe current positions is also insufficient for the next42 flight frames.

A bounded first experiment could retain already idle, stationary, clear chefs in explicit neutral slots until attachment, without interrupting active work or walking them elsewhere. It must reject imminent heat work and preserve existing participant leases. This closely matches C but can consume roughly another second of availability for each held chef, potentially erasing the benefit. A useful concurrent version instead needs a reviewed movement/catch-envelope reservation over the whole flight, honored by ordinary navigation and new job dispatch, with no silent reset of active actions. That safeguard is not currently provided by the bun-buffer resource lease.

Recommendation: retain this as a later optional experiment. The mechanism is legal and verified, but a useful concurrent integration needs the traffic reservation above; simply replacing center take/place with the C throw would be an unsafe extrapolation. No controller source was changed by this design review, and no throughput/replay/high-score qualification is claimed.
