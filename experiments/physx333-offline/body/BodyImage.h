#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace physx { class PxScene; }

namespace oc2 { namespace offline {

// A same-scene image of settled rigid actor and dynamic body state. Addresses
// are intentional: this component does not recreate actors, simulation bodies,
// or SimStateData pool entries. Other scene components must restore the
// interaction, active-body, broadphase, contact, and island structures before
// another simulation step is allowed.
struct BodyImage
{
    struct Actor
    {
        std::uintptr_t userId = 0;
        std::uintptr_t pxActor = 0;
        std::uintptr_t scbActor = 0;
        std::uintptr_t core = 0;
        std::uintptr_t sim = 0;
        std::uintptr_t lowLevelBody = 0;
        std::uintptr_t simStateData = 0;
        std::uint32_t type = 0;
        std::uint32_t rigidId = 0;
        std::uint32_t simStateType = 0;
        std::uint32_t active = 0;
        std::vector<std::uintptr_t> shapes;
    };

    struct Field
    {
        std::string name;
        std::uintptr_t address = 0;
        std::vector<unsigned char> bytes;
        // A topology field must still match the live scene before any write.
        // It is observed but never published by this component.
        bool topology = false;
    };

    std::uintptr_t scene = 0;
    std::vector<Actor> actors;
    std::vector<Field> fields;

    bool equals(const BodyImage& other, std::string& firstDifference) const;
};

// Both calls require a scene stopped after fetchResults. The image refuses
// pending Scb buffers, actor lifetime changes, unsupported CCD/constraint
// pointer payloads, and changed SimStateData allocation/type. Restore checks
// every target and saves a rollback image before its first write.
bool CaptureBodies(physx::PxScene& scene, BodyImage& image,
                   std::string& error);
bool RestoreBodies(physx::PxScene& scene, const BodyImage& image,
                   std::string& error);

}} // namespace oc2::offline
