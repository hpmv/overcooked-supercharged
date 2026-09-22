# ShapeSim transform-cache bindings

`PxsTransformCache` stores the transform array, reference counts, and free-ID
stack, but each `Sc::ShapeSim` separately stores the ID that it will release
when its contact manager is destroyed. Restoring only the cache array and ID
pool leaves those per-shape IDs from the recreated NPhase lifecycle in place.

The joined six-contact replay exposed this hidden dependency: public body
state, callbacks, and the 39-section oracle all matched after the first
replayed step, while the transform-cache free-ID stack contained paired swaps
(`10,8,9,6,7,...` instead of `10,9,8,7,...`). The source path is
`Sc::ShapeInstancePairLL::destroyManager` →
`Sc::ShapeSim::destroyTransformCache` → `PxsTransformCache::releaseID`.

`ShapeCacheBindings` captures the stopped scene's active rigid-overlap
ShapeSim addresses, shape IDs, and transform-cache IDs. After the source
lifecycle and cache-array restore, its guarded writer restores the ShapeSim
bindings, verifies the result, and rolls back on verification failure. It
rejects changed shape identity, invalid or duplicate target IDs, and IDs
without a live cache reference before writing. The offline joined probe
checks malformed-image atomic rejection and then confirms the next-step
free-ID stack, six other component images, 100 rewinds, and a five-step
contact suffix match.

This is an exact-allocation component for active rigid overlaps, not a
general actor/shape lifetime serializer. Contactless shapes, triggers,
reallocation, and a Unity-shipped binary are outside its current claim.
