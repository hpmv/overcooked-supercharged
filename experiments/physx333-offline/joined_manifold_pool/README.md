# Offline joined large-manifold pool image (PhysX 3.3.3)

Run from the framework root after building the pinned Win32 source mirror:

```bat
cmd /c experiments\physx333-offline\joined_manifold_pool\Build-Check.cmd
```

This is a **read-only source-level observation**, not a rewind/restorer. It
loads the pinned source-built Win32 PhysX 3.3.3 DLL and uses the existing
public-API `level_graph` scene. Unity and the game are not loaded. The test
keeps its files and ignored build output in this directory.

For settled A (`12 contact / 4 trigger / 2 marker`), B (`8 / 2 / 2`), and a
public-API return to settled A, the fixture captures the complete physical
`PxsContext::mManifoldPool` allocation partition: slab addresses, element
size/count, used count, unreleased-free counter, sorted used slots, and the
**ordered linked free chain**. It also maps every native-oriented contact
actor/shape key and contact-manager slot to its `PxcNpWorkUnit` manifold
pointer and physical large-manifold pool slot. Every pointer must name a
unique used slot whose `mContactPoints` points to that object's own inline
buffer. The full `JoinedContactImage` is captured twice at each stopped
boundary and must match exactly; the manifold-pool image must do the same.

Two independent fresh scenes follow the same A→B→A public-API trace. At the
first departure, all eight surviving contacts retain their exact manifold
addresses/slots. The four missing moving-chef capsule/box contacts release
exactly their four slots to the **head** of B's free chain, leaving A's old
free tail unchanged. When the public trace returns to A, all four missing
contacts reacquire their original addresses and slots (`4/4`), and the
complete same-scene manifold-pool image equals the earlier A image. The
portable projection—pool counters, slab count/size, ordered used/free slots,
and keyed contact→slot mapping—matches between fresh scenes at A, B, and
returned A. Fresh-scene absolute addresses are intentionally not compared.

The source mechanism is explicit: `PxsContext::createContactManager` allocates
a `Gu::LargePersistentContactManifold` from `mManifoldPool` for this PCM
capsule/box pair (`PxsContext.cpp:377-409`); destruction returns it
(`PxsContext.cpp:442-466`). `Ps::PoolBase::allocate` consumes the free-list
head and `deallocate` pushes onto that head (`PsPool.h:60-93,169-177`). The
large-manifold constructor binds `mContactPoints` to its own inline array
(`GuPersistentContactManifold.h:130,335-351`), so copying a saved whole PCM
object into a newly allocated one would be wrong even if its other bytes
looked identical.

This proves the existing public trace can reproduce the required four
manifold **identities and pool order** in this synthetic fixture. It does
not prove that the source-private joined-topology bridge will allocate in
the same order, that arbitrary game allocation histories retain the same
slabs, that manifold contents are restored, or that Unity's statically
linked build exposes a compatible write path. The current joined-topology
preflight checks SIP, ActorPair, trigger, contact-manager, and island-edge
allocation heads, but not this manifold pool. A later joined restorer should
capture/check this pool alongside the contact WorkUnits before claiming
exact native rewind parity; a different physical manifold slot must remain
an explicit failure or be reconciled through a separately proved allocator
restore, not silently called exact parity.
