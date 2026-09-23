# Offline joined contact image (PhysX 3.3.3)

Run from the framework root after building the pinned Win32 source mirror:

```bat
cmd /c experiments\physx333-offline\joined_contact_image\Build-Check.cmd
```

This is a read-only source-level observation of the synthetic level-shaped
scene, not a restorer. It uses the existing public-API `level_graph` fixture:
four chef capsules, one moving chef auxiliary shape, an isolated active body,
and eight shared static boxes. At settled A there are 12 contacts, four
triggers, and two markers; at B there are eight contacts, two triggers, and
two markers. The source-built DLL is loaded; Unity and the game are not.

Each contact row is keyed by the two **native-oriented** actor/shape IDs,
not by an index on a fictitious single mover. The image records ordered SIP
and ActorPair identities/physical slots; SIP touch/report metadata; ActorPair
touch/ref counts and report-data slot, IDs, reset stamp, and exact
`ContactStreamManager` bytes; contact-manager slot/flags, all 14 portable
Oracle manager words, exact same-scene `PxcNpWorkUnit` bytes, compressed
contact and local-cache bytes, and the single capsule/box PCM manifold's
active contacts, relative transform, warm-start counts/indices. It also
records ordered persistent/threshold event identities, manager free order and
bitmaps, and the scene-owned report buffer. The existing complete Oracle and
ActorPair ownership/pool images are embedded for cross-checks.

Two captures at each stopped A/B boundary must match exactly, including
addresses and raw WorkUnit bytes. A second fresh scene must match the
portable semantic projection at A and B: complete Oracle and ActorPair
images, keyed contact fields, stream contents, initialized PCM payload,
ordered events, bitmaps, report buffer, and pool order. Raw WorkUnit bytes,
bitmap addresses, and unused PCM warm-start index bytes are deliberately
**not** claimed portable: they can contain pointers or uninitialized storage.
The fixture checks ten report owners at A and eight at B; the two lost
no-touch contacts at A correctly have no report data.

The first `joined_topology` full-Oracle difference,
`contact.managers[48]`, is A contact row 3's
`PxcNpWorkUnit::frictionPatchCount`: native pair chef `9:0` to static
`1:0`, manager slot 3. This contact **survives** into B; the baseline
observes two friction patches at A and one at B. A later restorer must
therefore restore payload for all twelve contact managers, not merely the
four newly recreated ones.

This image is a prerequisite for adapting the old single-mover
`InteractionImage` contact restore to the mixed multi-actor graph. No deleted
contact is reconstructed here, no report object is allocated, no island
change queue is restored, and no successor is replayed from a rewind.
