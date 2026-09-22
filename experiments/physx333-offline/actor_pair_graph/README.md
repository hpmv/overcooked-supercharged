# ActorPair graph image (source-built PhysX 3.3.3)

`ActorPairGraphImage` is a read-only observer for the pinned Windows x86
PhysX 3.3.3 source build. It runs at a completed `fetchResults` boundary and
uses unique, nonzero 32-bit public actor `userData` values as stable semantic
IDs. It does not load Unity or the game.

The image records every registered contact SIP in scene order, with oriented
shape endpoints, a canonical actor-pair key, the SIP and ActorPair pool slots,
and touch/report booleans. Each distinct ActorPair is recorded once, keyed by
the canonical actor IDs while retaining native actor A/B orientation. Its row
contains the physical pool slot, reference and touch counts, internal flags,
report-set membership, and report-data identity, clients, stream counters, and
physical report-data pool slot. Public pointers and PhysX's internal actor IDs
are checked against the live actors and normalized in the image. The ordered
contact-report ActorPair set is captured separately. Both pools include the
used slot occupancy and exact free-list order, along with slab and count
metadata. The pool does not retain a separate chronological order for live
allocations; `usedSlots` is in ascending physical slot order.

Capture rejects duplicate shape-pair keys or ActorPairs for one canonical
actor pair. It checks every ActorPair's reference count against its contact SIP
owners plus one reference if it remains in the report set, and checks touch
count against touching SIPs. It also rejects unknown report-set entries,
unowned live ActorPairs or report-data objects, and malformed pool free lists.
Those checks describe the settled rigid-contact fixture. Scenes with other
ActorPair owner types, removed public actors awaiting reports, or unsupported
actor identities require a broader observer.

Run `Build-Check.cmd` from Windows. It compiles against the locally built,
pinned SDK mirror and exercises two shape contacts sharing one ActorPair. It
then detaches one shape, steps, detaches the other, and steps again. The
expected SIP/ActorPair ownership is `2/1 -> 1/1 -> 0/0`, with touch and report
data counts checked at each boundary. A second independent scene repeats the
same history; all three images compare equal, including physical pool slots,
free-list order, report-set order, and initialized report fields.

This milestone is capture and validation only. It does not restore ActorPairs,
preserve pending report-buffer bytes, model non-contact interactions, or prove
Unity's compiled PhysX layout/ABI. It is intended to expose shared ActorPair
ownership to a later graph-aware rewind implementation.
