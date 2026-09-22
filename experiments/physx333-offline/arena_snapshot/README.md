# Source-built PhysX arena snapshot diagnostic

Run from the framework root after building the pinned PhysX 3.3.3 Win32
source mirror:

```bat
cmd /c experiments\physx333-offline\arena_snapshot\Build-Check.cmd
```

This executable creates two 12-pair box/box scenes using a diagnostic
`PxAllocatorCallback`. The callback reserves one fixed-address arena,
quarantines freed blocks, and records its allocation ledger. At a settled
12-pair boundary it captures the arena bytes and ledger. It then follows a
five-step suffix alternating between eight and twelve pairs, restores the
old bytes and ledger, and repeats the whole suffix 100 times. Every replay
step must reproduce the pair count, ordered callbacks, allocation ledger,
and initialized arena bytes of the original continuation.
The first scene has twelve touching pairs. The second has ten touching pairs
and two overlapping but non-touching pairs, matching the mixed report
ownership pattern seen in the level trace. The build script runs both.
Foreign-owner and malformed-block images are rejected without changing the
live arena.

The separate `--lifetime` mode starts from the settled all-touch scene. It
records a five-step reference suffix, then repeatedly releases one static
actor and its attached shape, recreates them at a distant position, and steps
the successor. At each rewind it repairs the fixture's own actor pointer,
checks the exact checkpoint bytes and allocation ledger, confirms actor/shape
identity and static ray queries, and replays the suffix against its reference
callbacks, contact counts, queries, and initialized arena bytes. It passes
100 cycles in the source-built, inline-dispatcher fixture. The build script
runs this mode after the two original tests. No deletion listener is
registered; the mode checks that source-built PhysX has none before starting.

The byte comparison masks exactly three unwritten padding bytes in each of
25 embedded `PxsComputeAABBParams` task copies. Their offsets are derived
from the pinned source types and checked at build and run time. The snapshot
itself retains the raw bytes; this normalization applies only to comparison.

This is an **offline diagnostic oracle**, not a proposed game-side rewind
implementation. The Unity executable does not use this allocator, and this
fixture does not capture OS threads, mutexes, events, thread-local storage,
user callbacks, or allocations made outside the PhysX allocator. It also
uses an inline dispatcher and covers three box/box five-step traces rather
than arbitrary PhysX states. Only the third trace crosses one static
actor/shape lifetime. The result shows that source-level state capture can
close these fixture-specific hidden allocation histories, not that
unrestricted or Unity-integrated parity is complete. In particular, raw arena
restore cannot undo external deletion-listener effects or recreate destroyed
OS synchronization objects.
