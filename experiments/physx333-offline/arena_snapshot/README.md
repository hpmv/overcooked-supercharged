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

The byte comparison masks exactly three unwritten padding bytes in each of
25 embedded `PxsComputeAABBParams` task copies. Their offsets are derived
from the pinned source types and checked at build and run time. The snapshot
itself retains the raw bytes; this normalization applies only to comparison.

This is an **offline diagnostic oracle**, not a proposed game-side rewind
implementation. The Unity executable does not use this allocator, and this
fixture does not capture OS threads, mutexes, events, thread-local storage,
user callbacks, or allocations made outside the PhysX allocator. It also
keeps actor and shape lifetime fixed, uses an inline dispatcher, and covers
two box/box five-step traces rather than arbitrary PhysX states. The result shows
that source-level state capture can close this fixture's hidden allocation
history, not that unrestricted or Unity-integrated parity is complete.
