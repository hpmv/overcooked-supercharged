# Joined contact WorkUnit stage (offline PhysX 3.3.3)

`Build-Check.cmd` builds the private Win32 source bridge in
`../joined_topology/work/PhysXSDK`, then runs cold and warm synthetic
12-contact/4-trigger/2-marker scenarios in both baseline and shipped-like
SAP deletion order. It does not launch Unity or the game.
The pinned source is the external `PhysX-3.3.3` checkout; nothing in that
vendor checkout is changed.

At checkpoint A the fixture saves an oriented-key `JoinedContactImage` and a
`MemBlockRestoreImage`. Public PhysX simulation makes four contacts and two
triggers disappear at B. The existing joined topology/report bridge restores
the missing interactions and creates two report objects, bringing the total
back to ten. This fixture then calls the
two functions in `JoinedWorkUnitRestore.h`:

1. `InstallJoinedWorkUnitBindings` captures and preflights every one of the 12
   keyed contact owners, WorkUnits, stream pointers, and PCM manifold pool
   slots before the first write. It copies the exact A WorkUnit bytes and used
   PCM fields into the native lifecycle-owned objects. Eight surviving
   contacts are included; restoring only the four recreated contacts leaves a
   surviving manager's friction-patch count wrong.
2. The existing `RestoreMemBlockPoolForJoin` restores exact A block contents
   and LIFO ownership, requiring the just-installed manager bindings to match.
3. `RestoreJoinedWorkUnitPayload` repeats the full preflight, requires saved
   contact/cache backing bytes to be present, and verifies every full contact
   row including raw WorkUnit bytes, streams, and PCM used payload. A second
   invocation proves this stopped component image remains stable.

The stage requires exact same-scene physical PCM addresses. It never copies a
whole manifold object because that would overwrite `mContactPoints`, a
self-pointer into its own inline buffer. The saved pointer must equal the
pointer returned by source PhysX's recreation path and must own a unique used
large-manifold pool slot. Unknown geometry, changed endpoint/core/material
binding, changed report metadata, unexpected stream allocation, CCD contacts,
or a changed manifold address is rejected before writing. Six malformed
checkpoint controls check that rejection leaves the full current contact
image unchanged. A failure after the first write is fail-stop: discard the
offline scene rather than simulate it.

All four scenarios pass. This proves the contact WorkUnit/PCM and
memory-block component of the synthetic joined checkpoint, not full PhysX
rewind parity. Island, broadphase, caches, body, clock, context, and query
state have not been restored in this fixture. Consequently it intentionally
does not simulate B after the partial A restore or claim A→B→A→B replay.
The matched PhysX version makes these source semantics relevant but does not
turn this source-built DLL into a drop-in replacement for Unity's statically
linked PhysX ABI.
