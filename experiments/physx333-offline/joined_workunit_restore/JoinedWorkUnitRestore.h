#pragma once

#include "../joined_contact_image/JoinedContactImage.h"

#include <string>

namespace physx { class PxScene; }

namespace physx333_offline {

// Same-scene, stopped-boundary stage for the source-built 12-contact graph.
// The caller must already have restored the 12/4/2 topology and report/touch
// ownership. A rejected preflight makes no writes. A post-write failure is
// fail-stop: discard the scene rather than simulate or attempt another stage.
//
// This restores all twelve low-level contact WorkUnits, including survivors,
// plus each used PCM manifold payload. It deliberately requires the native
// lifecycle to have reacquired the checkpoint's exact manifold addresses.
// It does not allocate or rebase PCM objects, and does not restore the backing
// NpMemBlockPool or any other scene component.
bool InstallJoinedWorkUnitBindings(physx::PxScene& scene,
                                   const JoinedContactImage& checkpoint,
                                   std::string& error);

// Call only after RestoreMemBlockPoolForJoin has installed checkpoint block
// bytes. In addition to the first-stage checks, this requires every saved
// compressed-contact and pair-cache stream to match its backing bytes and
// verifies every full JoinedContactRow exactly after writing.
bool RestoreJoinedWorkUnitPayload(physx::PxScene& scene,
                                  const JoinedContactImage& checkpoint,
                                  std::string& error);

} // namespace physx333_offline
