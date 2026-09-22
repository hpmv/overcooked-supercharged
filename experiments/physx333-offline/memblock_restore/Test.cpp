// Reuse the offline six-contact fixture within this test translation unit.
// The fixture's normal main is renamed so no game code or scene is loaded.
#define main physx333_original_fixture_main
#include "../harness/main.cpp"
#undef main

#include "MemBlockRestore.h"

// The included harness main is renamed and never run in this executable.
// These fail-stop definitions satisfy only that dead function's references
// while the independent interaction component is being developed.
namespace physx333_offline {
bool CaptureInteractionImage(PxScene&, InteractionImage&, std::string&)
{ std::abort(); }
bool RestoreInteractionOrder(PxScene&, const InteractionImage&, std::string&)
{ std::abort(); }
bool RestoreInteractionMetadata(PxScene&, const InteractionImage&, std::string&)
{ std::abort(); }
bool InteractionImage::sameSlotsAndOrder(const InteractionImage&,
                                          std::string&) const
{ std::abort(); }
}

namespace {

using namespace physx333_offline;

void expectPool(PxScene& scene, MemBlockIdentityRegistry& registry,
                const MemBlockRestoreImage& expected,
                const char* where)
{
    MemBlockRestoreImage current;
    std::string error;
    if (!CaptureMemBlockRestore(scene, registry, current, error))
        die(std::string(where) + " capture: " + error);
    std::string difference;
    if (!current.pool.equals(expected.pool, difference))
        die(std::string(where) + " image: " + difference);
    if (current.contactBindings != expected.contactBindings)
        die(std::string(where) + " contact bindings changed");
}

} // namespace

int main()
{
    Runtime runtime;
    World world(runtime);
    world.step(false);
    world.step(true);
    world.step(false);

    // Public source API deliberately adds two unused heap blocks. This gives
    // the test a free LIFO tail to perturb without changing live contacts.
    NpScene& np = static_cast<NpScene&>(*world.scene);
    PxsContext* context = np.getScene().getScScene()
                              .getInteractionScene().getLowLevelContext();
    if (!context) die("missing low-level context");
    world.scene->setNbContactDataBlocks(
        world.scene->getNbContactDataBlocksUsed() + 2);

    MemBlockIdentityRegistry registry;
    MemBlockRestoreImage checkpoint;
    std::string error;
    if (!CaptureMemBlockRestore(*world.scene, registry, checkpoint, error))
        die("checkpoint capture: " + error);
    if (!checkpoint.pool.unsupported.empty())
        die("checkpoint pool has unsupported ownership");
    const std::size_t unusedIndex = checkpoint.pool.arrays.size() - 1;
    if (checkpoint.pool.arrays[unusedIndex].size < 2)
        die("fixture has fewer than two unused blocks");

    MemBlockRestoreImage perturbed = checkpoint;
    std::size_t unusedOffset = 0;
    for (std::size_t i = 0; i < unusedIndex; ++i)
        unusedOffset += perturbed.pool.arrays[i].size;
    std::swap(perturbed.pool.blocks[unusedOffset],
              perturbed.pool.blocks[unusedOffset + 1]);
    std::swap(perturbed.pool.arrays[unusedIndex].entries[0],
              perturbed.pool.arrays[unusedIndex].entries[1]);
    perturbed.pool.blocks[unusedOffset].bytes[0] ^= 0x5au;
    perturbed.pool.maxUsedBlocks += 1;

    for (unsigned iteration = 0; iteration < 100; ++iteration)
    {
        if (!RestoreMemBlockPool(*world.scene, registry, perturbed, error))
            die("restore changed unused order: " + error);
        expectPool(*world.scene, registry, perturbed, "perturbed");
        if (!RestoreMemBlockPool(*world.scene, registry, checkpoint, error))
            die("restore original pool: " + error);
        expectPool(*world.scene, registry, checkpoint, "checkpoint");
    }
    std::cout << "PASS memblock payload/LIFO/metadata A-B round-trip x100\n";

    MemBlockRestoreImage invalid = checkpoint;
    ++invalid.pool.arrays[unusedIndex].capacity;
    if (RestoreMemBlockPool(*world.scene, registry, invalid, error))
        die("corrupt array capacity accepted");
    expectPool(*world.scene, registry, checkpoint, "rejected capacity");
    std::cout << "PASS corrupt memblock image rejected atomically\n";

    world.step(true);
    MemBlockRestoreImage deleted;
    if (!CaptureMemBlockRestore(*world.scene, registry, deleted, error))
        die("deleted-state capture: " + error);
    if (RestoreMemBlockPool(*world.scene, registry, checkpoint, error))
        die("pool-only restore accepted changed contact topology");
    expectPool(*world.scene, registry, deleted, "rejected topology");
    std::cout << "PASS changed contact topology rejected atomically\n";
    return 0;
}
