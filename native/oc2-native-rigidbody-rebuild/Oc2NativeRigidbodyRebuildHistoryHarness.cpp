#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#if !defined(_M_IX86)
#error This harness requires Win32/x86.
#endif

#pragma pack(push, 8)
struct ContactPoolReceipt {
    uint32_t apiVersion, structSize, result, lastError;
    uintptr_t context, freeArray;
    uint32_t freeCount, orderHashBefore, orderHashAfter;
    uintptr_t top[16];
};

struct ManifoldPoolReceipt {
    uint32_t apiVersion, structSize, result, lastError;
    uintptr_t unityBase, context, pool;
    uint32_t poolKind;
    uintptr_t freeHeadBefore, freeHeadAfter;
    uint32_t elementSize, elementsPerSlab, used, unreleased, slabSize;
    uint32_t traversedCount, orderHashBefore, orderHashAfter;
    uintptr_t topBefore[16], topAfter[16];
};

struct DirtyInteractionKey {
    uintptr_t elementLow, elementHigh, primaryVtable;
    uint32_t interactionType;
};

struct DirtyInteractionOrderReceipt {
    uint32_t apiVersion, structSize, result, lastError;
    uintptr_t unityBase, nphaseCore, set, entries, entriesNext, hash;
    uint32_t entriesCapacity, hashSize, count, action, captures, restores;
    uint32_t orderHashBefore, orderHashAfter, installed, armed;
    uint32_t restoreMode, matchedCount, capturedOnlyCount, liveOnlyCount;
};
#pragma pack(pop)

static_assert(sizeof(ManifoldPoolReceipt) == 200,
    "Unexpected Win32 manifold-pool receipt ABI");
static_assert(sizeof(DirtyInteractionKey) == 16,
    "Unexpected Win32 dirty-interaction key ABI");
static_assert(sizeof(DirtyInteractionOrderReceipt) == 96,
    "Unexpected Win32 dirty-interaction receipt ABI");

typedef uint32_t (__cdecl *ApiVersion)();
typedef int (__cdecl *CaptureSnapshot)(uintptr_t, uintptr_t*, uint32_t,
    ContactPoolReceipt*);
typedef int (__cdecl *RestoreSnapshot)(uintptr_t, uintptr_t,
    const uintptr_t*, uint32_t, ContactPoolReceipt*);
typedef int (__cdecl *CaptureManifoldSnapshot)(uintptr_t, uintptr_t, uint32_t,
    uintptr_t*, uint32_t, ManifoldPoolReceipt*);
typedef int (__cdecl *RestoreManifoldSnapshot)(uintptr_t, uintptr_t, uint32_t,
    uintptr_t, const uintptr_t*, uint32_t, ManifoldPoolReceipt*);
typedef int (__cdecl *DirtyAction)(uintptr_t, DirtyInteractionOrderReceipt*);
typedef int (__cdecl *DirtyCaptureCopy)(uintptr_t, DirtyInteractionKey*,
    uint32_t, DirtyInteractionOrderReceipt*);
typedef int (__cdecl *DirtyRestoreArm)(uintptr_t, uintptr_t, uintptr_t,
    uintptr_t, uintptr_t, uint32_t, uint32_t,
    const DirtyInteractionKey*, uint32_t, uint32_t,
    DirtyInteractionOrderReceipt*);
typedef void (__thiscall *DirtyUpdate)(void*);

static int failures = 0;

static void Check(bool value, const char* message) {
    if (value) return;
    ++failures;
    printf("FAIL: %s\n", message);
}

static bool Same(const uintptr_t* left, const uintptr_t* right,
    uint32_t count) {
    for (uint32_t i = 0; i < count; ++i)
        if (left[i] != right[i]) return false;
    return true;
}

static void CopyBytes(uint8_t* destination, const uint8_t* source,
    uint32_t count) {
    for (uint32_t i = 0; i < count; ++i) destination[i] = source[i];
}

static const uint32_t kLargePoolAllocatorRva = 0xA69A90;
static const uint32_t kSpherePoolAllocatorRva = 0xA69AC0;
static const uint32_t kLargePoolSlabRva = 0xA69BEA;
static const uint32_t kSpherePoolSlabRva = 0xA69CCA;
static const uint32_t kLargePoolCallsiteRva = 0xA69F15;
static const uint32_t kSpherePoolCallsiteRva = 0xA69F32;
static const uint32_t kDirtyUpdateRva = 0xA540F0;
static const uint8_t kDirtyUpdateFunctionBytes[] = {
    0x55,0x8B,0xEC,0x83,0xEC,0x34,0x8B,0xE5,0x5D,0xC3
};
static const uint8_t kLargePoolAllocatorBytes[] = {
    0x56,0x8B,0xF1,0x83,0xBE,0x24,0x01,0x00,0x00,0x00,0x75,0x05,
    0xE8,0xAF,0x00,0x00,0x00,0x8B,0x86,0x24,0x01,0x00,0x00,0x8B,
    0x08,0xFF,0x86,0x18,0x01,0x00,0x00,0xFF,0x8E,0x1C,0x01,0x00,
    0x00,0x89,0x8E,0x24,0x01,0x00,0x00,0x5E,0xC3
};
static const uint8_t kSpherePoolAllocatorBytes[] = {
    0x56,0x8B,0xF1,0x83,0xBE,0x24,0x01,0x00,0x00,0x00,0x75,0x05,
    0xE8,0x5F,0x01,0x00,0x00,0x8B,0x86,0x24,0x01,0x00,0x00,0x8B,
    0x08,0xFF,0x86,0x18,0x01,0x00,0x00,0xFF,0x8E,0x1C,0x01,0x00,
    0x00,0x89,0x8E,0x24,0x01,0x00,0x00,0x5E,0xC3
};
static const uint8_t kLargePoolSlabBytes[] = {
    0x69,0x8E,0x14,0x01,0x00,0x00,0xF0,0x00,0x00,0x00,0x81,0xC1,
    0x10,0xFF,0xFF,0xFF,0x03,0xCF,0x3B,0xCF,0x72,0x1E,0x8B,0x86,
    0x24,0x01,0x00,0x00,0x89,0x01,0xFF,0x86,0x1C,0x01,0x00,0x00,
    0x89,0x8E,0x24,0x01,0x00,0x00,0x81,0xE9,0xF0,0x00,0x00,0x00,
    0x3B,0xCF,0x73,0xE2
};
static const uint8_t kSpherePoolSlabBytes[] = {
    0x8B,0x86,0x14,0x01,0x00,0x00,0x8D,0x0C,0x40,0xC1,0xE1,0x05,
    0x83,0xC1,0xA0,0x03,0xCF,0x3B,0xCF,0x72,0x1C,0x90,0x8B,0x86,
    0x24,0x01,0x00,0x00,0x89,0x01,0xFF,0x86,0x1C,0x01,0x00,0x00,
    0x89,0x8E,0x24,0x01,0x00,0x00,0x83,0xE9,0x60,0x3B,0xCF,0x73,
    0xE5
};
static const uint8_t kLargePoolCallsiteBytes[] = {
    0x8D,0x8B,0xE4,0x02,0x00,0x00,0xE8,0x70,0xFB,0xFF,0xFF
};
static const uint8_t kSpherePoolCallsiteBytes[] = {
    0x8D,0x8B,0x0C,0x04,0x00,0x00,0xE8,0x83,0xFB,0xFF,0xFF
};

static uint8_t* CreateRevisionImage() {
    const uint32_t size = 0xA6A000;
    uint8_t* image = static_cast<uint8_t*>(VirtualAlloc(0, size,
        MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
    if (!image) return 0;
    CopyBytes(image + kLargePoolAllocatorRva, kLargePoolAllocatorBytes,
        sizeof(kLargePoolAllocatorBytes));
    CopyBytes(image + kSpherePoolAllocatorRva, kSpherePoolAllocatorBytes,
        sizeof(kSpherePoolAllocatorBytes));
    CopyBytes(image + kLargePoolSlabRva, kLargePoolSlabBytes,
        sizeof(kLargePoolSlabBytes));
    CopyBytes(image + kSpherePoolSlabRva, kSpherePoolSlabBytes,
        sizeof(kSpherePoolSlabBytes));
    CopyBytes(image + kLargePoolCallsiteRva, kLargePoolCallsiteBytes,
        sizeof(kLargePoolCallsiteBytes));
    CopyBytes(image + kSpherePoolCallsiteRva, kSpherePoolCallsiteBytes,
        sizeof(kSpherePoolCallsiteBytes));
    CopyBytes(image + kDirtyUpdateRva, kDirtyUpdateFunctionBytes,
        sizeof(kDirtyUpdateFunctionBytes));
    return image;
}

static void InitializeManifoldPool(uint8_t* context, uint32_t poolKind,
    uint8_t nodes[4][0xF0]) {
    const uint32_t poolOffset = poolKind == 0 ? 0x2E4 : 0x40C;
    const uint32_t elementSize = poolKind == 0 ? 0xF0 : 0x60;
    uint8_t* pool = context + poolOffset;
    *reinterpret_cast<uint32_t*>(pool + 0x114) = 4;
    *reinterpret_cast<uint32_t*>(pool + 0x118) = 1;
    // releaseEmptySlabs can rebuild this chain and then reset the telemetry
    // counter. Deliberately keep it unequal to the traversed count.
    *reinterpret_cast<uint32_t*>(pool + 0x11C) = 0;
    *reinterpret_cast<uint32_t*>(pool + 0x120) = 4 * elementSize;
    *reinterpret_cast<uintptr_t*>(nodes[0]) = reinterpret_cast<uintptr_t>(nodes[1]);
    *reinterpret_cast<uintptr_t*>(nodes[1]) = reinterpret_cast<uintptr_t>(nodes[2]);
    *reinterpret_cast<uintptr_t*>(nodes[2]) = 0;
    *reinterpret_cast<uintptr_t*>(pool + 0x124) =
        reinterpret_cast<uintptr_t>(nodes[0]);
}

static bool ManifoldOrderMatches(uint8_t* context, uint32_t poolKind,
    const uintptr_t* expected, uint32_t count) {
    const uint32_t poolOffset = poolKind == 0 ? 0x2E4 : 0x40C;
    uintptr_t current = *reinterpret_cast<uintptr_t*>(
        context + poolOffset + 0x124);
    for (uint32_t i = 0; i < count; ++i) {
        if (current != expected[i]) return false;
        current = *reinterpret_cast<uintptr_t*>(current);
    }
    return current == 0;
}

static uint32_t DirtyPointerHash(uintptr_t pointer) {
    uint32_t value = static_cast<uint32_t>(pointer);
    value += ~(value << 15);
    value ^= value >> 10;
    value += value << 3;
    value ^= value >> 6;
    value += ~(value << 11);
    value ^= value >> 16;
    return value;
}

static void BuildDirtyHash(const uintptr_t* entries, uint32_t count,
    uint32_t* next, uint32_t capacity, uint32_t* hash, uint32_t hashSize) {
    for (uint32_t i = 0; i < hashSize; ++i) hash[i] = 0xFFFFFFFFu;
    for (uint32_t i = 0; i < capacity; ++i) next[i] = 0xCCCCCCCCu;
    for (uint32_t i = 0; i < count; ++i) {
        const uint32_t bucket = DirtyPointerHash(entries[i]) & (hashSize - 1);
        next[i] = hash[bucket];
        hash[bucket] = i;
    }
}

static void SetDirtyInteraction(uint8_t* interaction, uintptr_t vtable,
    uint32_t type, uintptr_t element0, uintptr_t element1) {
    for (uint32_t i = 0; i < 0x28; ++i) interaction[i] = 0;
    *reinterpret_cast<uintptr_t*>(interaction + 0x00) = vtable;
    *reinterpret_cast<uint16_t*>(interaction + 0x04) = 0xFFFFu;
    *reinterpret_cast<uint16_t*>(interaction + 0x06) = 3u;
    *reinterpret_cast<uintptr_t*>(interaction + 0x08) = vtable + 0x10;
    *reinterpret_cast<uint8_t*>(interaction + 0x1C) =
        static_cast<uint8_t>(type);
    *reinterpret_cast<uintptr_t*>(interaction + 0x20) = element0;
    *reinterpret_cast<uintptr_t*>(interaction + 0x24) = element1;
}

static DirtyInteractionKey DirtyKey(const uint8_t* interaction) {
    const uintptr_t element0 =
        *reinterpret_cast<const uintptr_t*>(interaction + 0x20);
    const uintptr_t element1 =
        *reinterpret_cast<const uintptr_t*>(interaction + 0x24);
    DirtyInteractionKey key = {
        element0 < element1 ? element0 : element1,
        element0 < element1 ? element1 : element0,
        *reinterpret_cast<const uintptr_t*>(interaction),
        *reinterpret_cast<const uint8_t*>(interaction + 0x1C)
    };
    return key;
}

static bool SameDirtyKey(const DirtyInteractionKey& left,
    const DirtyInteractionKey& right) {
    return left.elementLow == right.elementLow &&
        left.elementHigh == right.elementHigh &&
        left.primaryVtable == right.primaryVtable &&
        left.interactionType == right.interactionType;
}

static bool DirtyHashValid(const uintptr_t* entries, uint32_t count,
    const uint32_t* next, const uint32_t* hash, uint32_t hashSize) {
    bool seen[8] = {};
    uint32_t visited = 0;
    for (uint32_t bucket = 0; bucket < hashSize; ++bucket) {
        uint32_t index = hash[bucket];
        while (index != 0xFFFFFFFFu) {
            if (index >= count || seen[index] ||
                (DirtyPointerHash(entries[index]) & (hashSize - 1)) != bucket)
                return false;
            seen[index] = true;
            ++visited;
            index = next[index];
        }
    }
    return visited == count;
}

static void RunDirtyInteractionTests(uint8_t* image, DirtyAction install,
    DirtyAction status, DirtyAction armCapture, DirtyCaptureCopy copyCapture,
    DirtyRestoreArm armRestore, DirtyAction cancel, DirtyAction uninstall) {
    const uint32_t capacity = 8;
    const uint32_t hashSize = 16;
    uint8_t nphase[0x80] = {};
    uint8_t scene[0x4A5] = {};
    __declspec(align(16)) uint8_t storage[256] = {};
    __declspec(align(16)) uint8_t replacementStorage[512] = {};
    uint8_t interactions[4][0x28] = {};
    uintptr_t elements[8] = {};
    *reinterpret_cast<uintptr_t*>(nphase) =
        reinterpret_cast<uintptr_t>(scene);
    uint8_t* set = nphase + 0x44;
    uint32_t* hash = reinterpret_cast<uint32_t*>(storage);
    uint32_t* next = hash + hashSize;
    uintptr_t entriesAddress =
        (reinterpret_cast<uintptr_t>(next + capacity) + 15u) &
        ~static_cast<uintptr_t>(15u);
    uintptr_t* entries = reinterpret_cast<uintptr_t*>(entriesAddress);
    *reinterpret_cast<uintptr_t*>(set + 0x00) =
        reinterpret_cast<uintptr_t>(storage);
    *reinterpret_cast<uintptr_t*>(set + 0x04) = entriesAddress;
    *reinterpret_cast<uintptr_t*>(set + 0x08) =
        reinterpret_cast<uintptr_t>(next);
    *reinterpret_cast<uintptr_t*>(set + 0x0C) =
        reinterpret_cast<uintptr_t>(hash);
    *reinterpret_cast<uint32_t*>(set + 0x10) = capacity;
    *reinterpret_cast<uint32_t*>(set + 0x14) = hashSize;
    *reinterpret_cast<uint32_t*>(set + 0x18) = 0x3F400000u;
    *reinterpret_cast<uint32_t*>(set + 0x1C) = 3;
    *reinterpret_cast<uint32_t*>(set + 0x24) = 3;

    const uintptr_t vtable = 0x12345000u;
    SetDirtyInteraction(interactions[0], vtable, 3,
        reinterpret_cast<uintptr_t>(&elements[0]),
        reinterpret_cast<uintptr_t>(&elements[1]));
    SetDirtyInteraction(interactions[1], vtable, 3,
        reinterpret_cast<uintptr_t>(&elements[2]),
        reinterpret_cast<uintptr_t>(&elements[3]));
    SetDirtyInteraction(interactions[2], vtable, 3,
        reinterpret_cast<uintptr_t>(&elements[4]),
        reinterpret_cast<uintptr_t>(&elements[5]));
    entries[0] = reinterpret_cast<uintptr_t>(interactions[0]);
    entries[1] = reinterpret_cast<uintptr_t>(interactions[1]);
    entries[2] = reinterpret_cast<uintptr_t>(interactions[2]);
    BuildDirtyHash(entries, 3, next, capacity, hash, hashSize);

    const uintptr_t imagePointer = reinterpret_cast<uintptr_t>(image);
    DirtyInteractionOrderReceipt receipt = {};
    Check(install(imagePointer, &receipt) == 1 && receipt.result == 1 &&
        receipt.installed == 1, "dirty hook installs");
    Check(armCapture(imagePointer, &receipt) == 1 && receipt.result == 18 &&
        receipt.armed == 1, "dirty capture arms");
    DirtyUpdate update = reinterpret_cast<DirtyUpdate>(
        image + kDirtyUpdateRva);
    update(nphase);
    receipt = {};
    Check(status(imagePointer, &receipt) == 1 && receipt.result == 1 &&
        receipt.count == 3 && receipt.captures == 1 && receipt.armed == 0,
        "dirty capture completes one-shot");
    update(nphase);
    Check(status(imagePointer, &receipt) == 1 && receipt.result == 1 &&
        receipt.captures == 1 && receipt.armed == 0,
        "dirty capture is dormant after one call");
    DirtyInteractionOrderReceipt capturedReceipt = receipt;
    DirtyInteractionKey captured[3] = {};
    Check(copyCapture(imagePointer, captured, 3, &receipt) == 1 &&
        receipt.result == 1, "dirty capture copies semantic order");
    Check(SameDirtyKey(captured[0], DirtyKey(interactions[0])) &&
        SameDirtyKey(captured[1], DirtyKey(interactions[1])) &&
        SameDirtyKey(captured[2], DirtyKey(interactions[2])),
        "dirty capture uses element identity");

    // Cancel while idle is a true no-op: reset cleanup must not erase the
    // receipt or semantic snapshot from an already completed one-shot capture.
    const uint32_t capturesBeforeIdleCancel = receipt.captures;
    Check(cancel(imagePointer, &receipt) == 1 && receipt.result == 1 &&
        receipt.armed == 0 && receipt.captures == capturesBeforeIdleCancel,
        "dirty idle cancel preserves completed capture");
    Check(copyCapture(imagePointer, captured, 3, &receipt) == 1 &&
        receipt.result == 1, "dirty capture remains copyable after idle cancel");

    // A pending action can still be cancelled.  The hook must remain dormant
    // on the following update and must not increment the one-shot counter.
    Check(armCapture(imagePointer, &receipt) == 1 && receipt.result == 18 &&
        receipt.armed == 1, "dirty second capture arms before cancel");
    Check(cancel(imagePointer, &receipt) == 1 && receipt.result == 10 &&
        receipt.lastError == ERROR_CANCELLED && receipt.armed == 0,
        "dirty pending capture cancels");
    update(nphase);
    Check(status(imagePointer, &receipt) == 1 && receipt.result == 10 &&
        receipt.captures == capturesBeforeIdleCancel && receipt.armed == 0,
        "dirty cancelled capture stays dormant");

    // Reassign pooled objects to different element pairs and scramble dense
    // order. Restore must select current pointers by semantic key, never copy
    // the stale captured CoreInteraction addresses.  Replace the entire
    // CoalescedHashSet backing allocation and grow both capacities as well;
    // those implementation details are allowed to change while NPhaseCore and
    // semantic membership remain exact.
    SetDirtyInteraction(interactions[0], vtable, 3,
        reinterpret_cast<uintptr_t>(&elements[2]),
        reinterpret_cast<uintptr_t>(&elements[3]));
    SetDirtyInteraction(interactions[1], vtable, 3,
        reinterpret_cast<uintptr_t>(&elements[4]),
        reinterpret_cast<uintptr_t>(&elements[5]));
    SetDirtyInteraction(interactions[2], vtable, 3,
        reinterpret_cast<uintptr_t>(&elements[0]),
        reinterpret_cast<uintptr_t>(&elements[1]));
    const uint32_t replacementCapacity = 12;
    const uint32_t replacementHashSize = 32;
    hash = reinterpret_cast<uint32_t*>(replacementStorage);
    next = hash + replacementHashSize;
    entriesAddress = (reinterpret_cast<uintptr_t>(
        next + replacementCapacity) + 15u) & ~static_cast<uintptr_t>(15u);
    entries = reinterpret_cast<uintptr_t*>(entriesAddress);
    *reinterpret_cast<uintptr_t*>(set + 0x00) =
        reinterpret_cast<uintptr_t>(replacementStorage);
    *reinterpret_cast<uintptr_t*>(set + 0x04) = entriesAddress;
    *reinterpret_cast<uintptr_t*>(set + 0x08) =
        reinterpret_cast<uintptr_t>(next);
    *reinterpret_cast<uintptr_t*>(set + 0x0C) =
        reinterpret_cast<uintptr_t>(hash);
    *reinterpret_cast<uint32_t*>(set + 0x10) = replacementCapacity;
    *reinterpret_cast<uint32_t*>(set + 0x14) = replacementHashSize;
    entries[0] = reinterpret_cast<uintptr_t>(interactions[1]);
    entries[1] = reinterpret_cast<uintptr_t>(interactions[2]);
    entries[2] = reinterpret_cast<uintptr_t>(interactions[0]);
    BuildDirtyHash(entries, 3, next, replacementCapacity, hash,
        replacementHashSize);

    // Exact semantic membership is not sufficient if the set belongs to a
    // different NPhaseCore.  Even an otherwise coherent alias must fail
    // closed without touching its container.
    uint8_t foreignNphase[0x80] = {};
    *reinterpret_cast<uintptr_t*>(foreignNphase) =
        reinterpret_cast<uintptr_t>(scene);
    CopyBytes(foreignNphase + 0x44, set, 0x28);
    uintptr_t foreignBefore[3] = {entries[0], entries[1], entries[2]};
    receipt = {};
    Check(armRestore(imagePointer, capturedReceipt.nphaseCore,
        capturedReceipt.entries, capturedReceipt.entriesNext,
        capturedReceipt.hash, capturedReceipt.entriesCapacity,
        capturedReceipt.hashSize, captured, 3, 1, &receipt) == 1,
        "dirty foreign-nphase restore arms");
    update(foreignNphase);
    Check(status(imagePointer, &receipt) == 1 && receipt.result == 15 &&
        receipt.armed == 0 &&
        receipt.nphaseCore == reinterpret_cast<uintptr_t>(foreignNphase) &&
        receipt.entries == entriesAddress &&
        Same(entries, foreignBefore, 3),
        "dirty restore rejects a different nphase core");

    // The same NPhaseCore with a coherent but shorter current set is also not
    // the captured semantic set.  Count mismatch is rejected before writes.
    *reinterpret_cast<uint32_t*>(set + 0x1C) = 2;
    *reinterpret_cast<uint32_t*>(set + 0x24) = 2;
    BuildDirtyHash(entries, 2, next, replacementCapacity, hash,
        replacementHashSize);
    receipt = {};
    Check(armRestore(imagePointer, capturedReceipt.nphaseCore,
        capturedReceipt.entries, capturedReceipt.entriesNext,
        capturedReceipt.hash, capturedReceipt.entriesCapacity,
        capturedReceipt.hashSize, captured, 3, 1, &receipt) == 1,
        "dirty count-mismatch restore arms");
    update(nphase);
    Check(status(imagePointer, &receipt) == 1 && receipt.result == 15 &&
        receipt.armed == 0 && receipt.count == 2 &&
        receipt.entriesCapacity == replacementCapacity &&
        receipt.hashSize == replacementHashSize &&
        entries[0] == foreignBefore[0] &&
        entries[1] == foreignBefore[1],
        "dirty restore rejects semantic count mismatch");
    *reinterpret_cast<uint32_t*>(set + 0x1C) = 3;
    *reinterpret_cast<uint32_t*>(set + 0x24) = 3;
    BuildDirtyHash(entries, 3, next, replacementCapacity, hash,
        replacementHashSize);

    receipt = {};
    Check(armRestore(imagePointer, capturedReceipt.nphaseCore,
        capturedReceipt.entries, capturedReceipt.entriesNext,
        capturedReceipt.hash, capturedReceipt.entriesCapacity,
        capturedReceipt.hashSize, captured, 3, 1, &receipt) == 1 &&
        receipt.result == 18, "dirty restore arms");
    update(nphase);
    receipt = {};
    Check(status(imagePointer, &receipt) == 1 && receipt.result == 1 &&
        receipt.restores == 1 && receipt.orderHashBefore !=
        receipt.orderHashAfter && receipt.restoreMode == 1 &&
        receipt.matchedCount == 3 && receipt.capturedOnlyCount == 0 &&
        receipt.liveOnlyCount == 0, "dirty restore completes one-shot");
    Check(receipt.nphaseCore == capturedReceipt.nphaseCore &&
        receipt.entries == entriesAddress &&
        receipt.entriesNext == reinterpret_cast<uintptr_t>(next) &&
        receipt.hash == reinterpret_cast<uintptr_t>(hash) &&
        receipt.entriesCapacity == replacementCapacity &&
        receipt.hashSize == replacementHashSize,
        "dirty restore receipt reports replacement storage");
    Check(entries[0] == reinterpret_cast<uintptr_t>(interactions[2]) &&
        entries[1] == reinterpret_cast<uintptr_t>(interactions[0]) &&
        entries[2] == reinterpret_cast<uintptr_t>(interactions[1]),
        "dirty restore maps semantics to current pooled pointers");
    Check(DirtyHashValid(entries, 3, next, hash, replacementHashSize),
        "dirty restore rebuilds valid pointer hash chains");
    uintptr_t oneShot[3] = {entries[0], entries[1], entries[2]};
    update(nphase);
    Check(Same(entries, oneShot, 3), "dirty restore is dormant after one call");

    // Missing semantic membership must leave every container array untouched.
    entries[0] = reinterpret_cast<uintptr_t>(interactions[1]);
    entries[1] = reinterpret_cast<uintptr_t>(interactions[2]);
    entries[2] = reinterpret_cast<uintptr_t>(interactions[0]);
    BuildDirtyHash(entries, 3, next, replacementCapacity, hash,
        replacementHashSize);
    Check(armRestore(imagePointer, capturedReceipt.nphaseCore,
        capturedReceipt.entries, capturedReceipt.entriesNext,
        capturedReceipt.hash, capturedReceipt.entriesCapacity,
        capturedReceipt.hashSize, captured, 3, 1, &receipt) == 1,
        "dirty mismatch restore arms");
    SetDirtyInteraction(interactions[1], vtable, 3,
        reinterpret_cast<uintptr_t>(&elements[1]),
        reinterpret_cast<uintptr_t>(&elements[4]));
    uintptr_t entriesBefore[3] = {entries[0], entries[1], entries[2]};
    uint32_t nextBefore[3] = {next[0], next[1], next[2]};
    uint32_t hashBefore[32] = {};
    CopyBytes(reinterpret_cast<uint8_t*>(hashBefore),
        reinterpret_cast<const uint8_t*>(hash), sizeof(hashBefore));
    update(nphase);
    receipt = {};
    Check(status(imagePointer, &receipt) == 1 && receipt.result == 14 &&
        receipt.armed == 0, "dirty membership mismatch fails closed");
    Check(Same(entries, entriesBefore, 3) &&
        next[0] == nextBefore[0] && next[1] == nextBefore[1] &&
        next[2] == nextBefore[2] &&
        memcmp(hash, hashBefore, sizeof(hashBefore)) == 0,
        "dirty membership rejection is non-mutating");

    // Projection mode permits a branch-local replacement without inventing
    // a position for it. The new D entry remains at dense slot 1 while the A
    // and B survivor slots are refilled in checkpoint-relative order.
    entries[0] = reinterpret_cast<uintptr_t>(interactions[0]); // B
    entries[1] = reinterpret_cast<uintptr_t>(interactions[1]); // D
    entries[2] = reinterpret_cast<uintptr_t>(interactions[2]); // A
    BuildDirtyHash(entries, 3, next, replacementCapacity, hash,
        replacementHashSize);
    receipt = {};
    Check(armRestore(imagePointer, capturedReceipt.nphaseCore,
        capturedReceipt.entries, capturedReceipt.entriesNext,
        capturedReceipt.hash, capturedReceipt.entriesCapacity,
        capturedReceipt.hashSize, captured, 3, 2, &receipt) == 1 &&
        receipt.result == 18 && receipt.restoreMode == 2,
        "dirty projection restore arms explicitly");
    update(nphase);
    receipt = {};
    Check(status(imagePointer, &receipt) == 1 && receipt.result == 1 &&
        receipt.restores == 2 && receipt.restoreMode == 2 &&
        receipt.matchedCount == 2 && receipt.capturedOnlyCount == 1 &&
        receipt.liveOnlyCount == 1,
        "dirty projection reports survivor accounting");
    Check(entries[0] == reinterpret_cast<uintptr_t>(interactions[2]) &&
        entries[1] == reinterpret_cast<uintptr_t>(interactions[1]) &&
        entries[2] == reinterpret_cast<uintptr_t>(interactions[0]),
        "dirty projection preserves current-only slot and orders survivors");
    Check(DirtyHashValid(entries, 3, next, hash, replacementHashSize),
        "dirty projection rebuilds valid pointer hash chains");
    uintptr_t projectedOneShot[3] = {entries[0], entries[1], entries[2]};
    update(nphase);
    Check(Same(entries, projectedOneShot, 3),
        "dirty projection is dormant after one call");

    // A larger live set keeps its one current-only slot while all three
    // checkpoint interactions survive. This exercises count growth and a
    // replacement backing allocation in projection mode.
    SetDirtyInteraction(interactions[3], vtable, 3,
        reinterpret_cast<uintptr_t>(&elements[4]),
        reinterpret_cast<uintptr_t>(&elements[5])); // C
    entries[0] = reinterpret_cast<uintptr_t>(interactions[3]); // C
    entries[1] = reinterpret_cast<uintptr_t>(interactions[1]); // D
    entries[2] = reinterpret_cast<uintptr_t>(interactions[0]); // B
    entries[3] = reinterpret_cast<uintptr_t>(interactions[2]); // A
    *reinterpret_cast<uint32_t*>(set + 0x1C) = 4;
    *reinterpret_cast<uint32_t*>(set + 0x24) = 4;
    BuildDirtyHash(entries, 4, next, replacementCapacity, hash,
        replacementHashSize);
    receipt = {};
    Check(armRestore(imagePointer, capturedReceipt.nphaseCore,
        capturedReceipt.entries, capturedReceipt.entriesNext,
        capturedReceipt.hash, capturedReceipt.entriesCapacity,
        capturedReceipt.hashSize, captured, 3, 2, &receipt) == 1,
        "dirty projection with live addition arms");
    update(nphase);
    Check(status(imagePointer, &receipt) == 1 && receipt.result == 1 &&
        receipt.restores == 3 && receipt.count == 4 &&
        receipt.matchedCount == 3 && receipt.capturedOnlyCount == 0 &&
        receipt.liveOnlyCount == 1,
        "dirty projection accepts a larger live set");
    Check(entries[0] == reinterpret_cast<uintptr_t>(interactions[2]) &&
        entries[1] == reinterpret_cast<uintptr_t>(interactions[1]) &&
        entries[2] == reinterpret_cast<uintptr_t>(interactions[0]) &&
        entries[3] == reinterpret_cast<uintptr_t>(interactions[3]) &&
        DirtyHashValid(entries, 4, next, hash, replacementHashSize),
        "dirty projection orders survivors around a live-only slot");

    // A smaller live set contains A and B only. Missing checkpoint C is
    // reported, while the survivor slots still recover A-before-B order.
    entries[0] = reinterpret_cast<uintptr_t>(interactions[0]); // B
    entries[1] = reinterpret_cast<uintptr_t>(interactions[2]); // A
    *reinterpret_cast<uint32_t*>(set + 0x1C) = 2;
    *reinterpret_cast<uint32_t*>(set + 0x24) = 2;
    BuildDirtyHash(entries, 2, next, replacementCapacity, hash,
        replacementHashSize);
    receipt = {};
    Check(armRestore(imagePointer, capturedReceipt.nphaseCore,
        capturedReceipt.entries, capturedReceipt.entriesNext,
        capturedReceipt.hash, capturedReceipt.entriesCapacity,
        capturedReceipt.hashSize, captured, 3, 2, &receipt) == 1,
        "dirty projection with checkpoint deletion arms");
    update(nphase);
    Check(status(imagePointer, &receipt) == 1 && receipt.result == 1 &&
        receipt.restores == 4 && receipt.count == 2 &&
        receipt.matchedCount == 2 && receipt.capturedOnlyCount == 1 &&
        receipt.liveOnlyCount == 0,
        "dirty projection accepts a smaller live set");
    Check(entries[0] == reinterpret_cast<uintptr_t>(interactions[2]) &&
        entries[1] == reinterpret_cast<uintptr_t>(interactions[0]) &&
        DirtyHashValid(entries, 2, next, hash, replacementHashSize),
        "dirty projection orders a checkpoint subset");

    receipt = {};
    Check(uninstall(imagePointer, &receipt) == 1 && receipt.result == 1,
        "dirty hook uninstalls");
    Check(memcmp(image + kDirtyUpdateRva, kDirtyUpdateFunctionBytes,
        6) == 0, "dirty hook restores exact function bytes");
}

static void RunManifoldPoolTests(uint8_t* image, uint32_t poolKind,
    CaptureManifoldSnapshot capture, RestoreManifoldSnapshot restore) {
    const uint32_t poolOffset = poolKind == 0 ? 0x2E4 : 0x40C;
    const uint32_t elementSize = poolKind == 0 ? 0xF0 : 0x60;
    uint8_t context[0x538] = {};
    uint8_t secondContext[0x538] = {};
    uint8_t nodes[4][0xF0] = {};
    InitializeManifoldPool(context, poolKind, nodes);
    InitializeManifoldPool(secondContext, poolKind, nodes);
    const uintptr_t imagePointer = reinterpret_cast<uintptr_t>(image);
    const uintptr_t contextPointer = reinterpret_cast<uintptr_t>(context);
    const uintptr_t poolPointer = reinterpret_cast<uintptr_t>(
        context + poolOffset);
    uintptr_t saved[3] = {};
    ManifoldPoolReceipt receipt = {};

    Check(capture(imagePointer, contextPointer, poolKind, saved, 3,
        &receipt) == 1, "manifold capture succeeds");
    Check(receipt.result == 1 && receipt.apiVersion == 11 &&
        receipt.structSize == sizeof(receipt), "manifold capture receipt");
    Check(receipt.pool == poolPointer && receipt.poolKind == poolKind &&
        receipt.elementSize == elementSize && receipt.traversedCount == 3,
        "manifold capture telemetry");
    uintptr_t expected[3] = {
        reinterpret_cast<uintptr_t>(nodes[0]),
        reinterpret_cast<uintptr_t>(nodes[1]),
        reinterpret_cast<uintptr_t>(nodes[2])
    };
    Check(Same(saved, expected, 3), "manifold capture preserves head order");

    *reinterpret_cast<uintptr_t*>(nodes[2]) = expected[0];
    *reinterpret_cast<uintptr_t*>(nodes[0]) = expected[1];
    *reinterpret_cast<uintptr_t*>(nodes[1]) = 0;
    *reinterpret_cast<uintptr_t*>(context + poolOffset + 0x124) = expected[2];
    receipt = {};
    Check(restore(imagePointer, contextPointer, poolKind, poolPointer, saved,
        3, &receipt) == 1, "manifold restore succeeds");
    Check(receipt.result == 1 && receipt.freeHeadAfter == expected[0] &&
        *reinterpret_cast<uintptr_t*>(nodes[0]) == expected[1] &&
        *reinterpret_cast<uintptr_t*>(nodes[1]) == expected[2] &&
        *reinterpret_cast<uintptr_t*>(nodes[2]) == 0,
        "manifold restore rewires exact head order");
    Check(*reinterpret_cast<uint32_t*>(context + poolOffset + 0x118) == 1 &&
        *reinterpret_cast<uint32_t*>(context + poolOffset + 0x11C) == 0,
        "manifold restore preserves accounting");

    receipt = {};
    Check(restore(imagePointer, contextPointer, poolKind,
        poolPointer + sizeof(uintptr_t), saved, 3, &receipt) == 0 &&
        receipt.result == 14, "wrong manifold pool identity is rejected");
    Check(ManifoldOrderMatches(context, poolKind, expected, 3),
        "manifold identity rejection is non-mutating");
    receipt = {};
    Check(restore(imagePointer, contextPointer, poolKind, poolPointer, saved,
        2, &receipt) == 0 && receipt.result == 15,
        "wrong manifold count is rejected");
    Check(ManifoldOrderMatches(context, poolKind, expected, 3),
        "manifold count rejection is non-mutating");
    uintptr_t duplicate[3] = { saved[0], saved[0], saved[2] };
    receipt = {};
    Check(restore(imagePointer, contextPointer, poolKind, poolPointer,
        duplicate, 3, &receipt) == 0 && receipt.result == 10,
        "duplicate manifold snapshot is rejected");
    Check(ManifoldOrderMatches(context, poolKind, expected, 3),
        "manifold duplicate rejection is non-mutating");
    uintptr_t changed[3] = {
        saved[0], saved[1], reinterpret_cast<uintptr_t>(nodes[3])
    };
    receipt = {};
    Check(restore(imagePointer, contextPointer, poolKind, poolPointer,
        changed, 3, &receipt) == 0 && receipt.result == 16,
        "changed manifold membership is rejected");
    Check(ManifoldOrderMatches(context, poolKind, expected, 3),
        "manifold membership rejection is non-mutating");
    uintptr_t tooSmall[2] = {};
    receipt = {};
    Check(capture(imagePointer, contextPointer, poolKind, tooSmall, 2,
        &receipt) == 0 && receipt.result == 11,
        "short manifold capture buffer is rejected");
    Check(ManifoldOrderMatches(context, poolKind, expected, 3),
        "manifold capture rejection is non-mutating");

    receipt = {};
    Check(restore(imagePointer, reinterpret_cast<uintptr_t>(secondContext),
        poolKind, poolPointer, saved, 3, &receipt) == 0 &&
        receipt.result == 14, "changed manifold context is rejected");

    *reinterpret_cast<uint32_t*>(context + poolOffset + 0x118) = 0;
    *reinterpret_cast<uint32_t*>(context + poolOffset + 0x11C) = 0;
    *reinterpret_cast<uintptr_t*>(context + poolOffset + 0x124) = 0;
    receipt = {};
    Check(capture(imagePointer, contextPointer, poolKind, 0, 0,
        &receipt) == 1 && receipt.result == 1 &&
        receipt.traversedCount == 0, "zero manifold list capture succeeds");
    receipt = {};
    Check(restore(imagePointer, contextPointer, poolKind, poolPointer, 0, 0,
        &receipt) == 1 && receipt.result == 1 &&
        receipt.freeHeadAfter == 0, "zero manifold list restore succeeds");
}

int main(int argc, char** argv) {
    if (argc != 2) {
        printf("usage: Oc2NativeRigidbodyRebuildHistoryHarness <dll>\n");
        return 2;
    }
    HMODULE library = LoadLibraryA(argv[1]);
    if (!library) {
        printf("LoadLibrary failed: %lu\n", GetLastError());
        return 2;
    }
    ApiVersion version = reinterpret_cast<ApiVersion>(
        GetProcAddress(library, "oc2_rigidbody_rebuild_api_version"));
    CaptureSnapshot capture = reinterpret_cast<CaptureSnapshot>(
        GetProcAddress(library, "oc2_contact_manager_pool_capture_snapshot"));
    RestoreSnapshot restore = reinterpret_cast<RestoreSnapshot>(
        GetProcAddress(library, "oc2_contact_manager_pool_restore_snapshot"));
    CaptureManifoldSnapshot captureManifold =
        reinterpret_cast<CaptureManifoldSnapshot>(GetProcAddress(library,
            "oc2_manifold_pool_capture_snapshot"));
    RestoreManifoldSnapshot restoreManifold =
        reinterpret_cast<RestoreManifoldSnapshot>(GetProcAddress(library,
            "oc2_manifold_pool_restore_snapshot"));
    DirtyAction installDirty = reinterpret_cast<DirtyAction>(GetProcAddress(
        library, "oc2_dirty_interaction_order_install"));
    DirtyAction statusDirty = reinterpret_cast<DirtyAction>(GetProcAddress(
        library, "oc2_dirty_interaction_order_status"));
    DirtyAction armDirtyCapture = reinterpret_cast<DirtyAction>(GetProcAddress(
        library, "oc2_dirty_interaction_order_capture_arm"));
    DirtyCaptureCopy copyDirtyCapture =
        reinterpret_cast<DirtyCaptureCopy>(GetProcAddress(library,
            "oc2_dirty_interaction_order_capture_copy"));
    DirtyRestoreArm armDirtyRestore =
        reinterpret_cast<DirtyRestoreArm>(GetProcAddress(library,
            "oc2_dirty_interaction_order_restore_arm"));
    DirtyAction uninstallDirty = reinterpret_cast<DirtyAction>(GetProcAddress(
        library, "oc2_dirty_interaction_order_uninstall"));
    DirtyAction cancelDirty = reinterpret_cast<DirtyAction>(GetProcAddress(
        library, "oc2_dirty_interaction_order_cancel"));
    Check(version && version() == 11, "API version");
    Check(capture != 0, "capture export");
    Check(restore != 0, "restore export");
    Check(captureManifold != 0, "manifold capture export");
    Check(restoreManifold != 0, "manifold restore export");
    Check(installDirty && statusDirty && armDirtyCapture && copyDirtyCapture &&
        armDirtyRestore && cancelDirty && uninstallDirty,
        "dirty interaction exports");
    if (!version || !capture || !restore || !captureManifold ||
        !restoreManifold || !installDirty || !statusDirty || !armDirtyCapture ||
        !copyDirtyCapture || !armDirtyRestore || !cancelDirty ||
        !uninstallDirty) {
        FreeLibrary(library);
        return 1;
    }

    uint8_t context[0x2D0] = {};
    uint8_t managers[4][0x50] = {};
    uintptr_t values[3] = {
        reinterpret_cast<uintptr_t>(managers[0]),
        reinterpret_cast<uintptr_t>(managers[1]),
        reinterpret_cast<uintptr_t>(managers[2])
    };
    *reinterpret_cast<uintptr_t*>(context + 0x2C8) =
        reinterpret_cast<uintptr_t>(values);
    *reinterpret_cast<uint32_t*>(context + 0x2CC) = 3;
    const uintptr_t contextPointer = reinterpret_cast<uintptr_t>(context);
    const uintptr_t arrayPointer = reinterpret_cast<uintptr_t>(values);

    uintptr_t saved[3] = {};
    ContactPoolReceipt receipt = {};
    Check(capture(contextPointer, saved, 3, &receipt) == 1,
        "capture succeeds");
    Check(receipt.result == 1 && receipt.apiVersion == 11 &&
        receipt.structSize == sizeof(receipt), "capture receipt");
    Check(Same(saved, values, 3), "capture copies exact order");

    uintptr_t permuted[3] = { values[2], values[0], values[1] };
    for (uint32_t i = 0; i < 3; ++i) values[i] = permuted[i];
    receipt = {};
    Check(restore(contextPointer, arrayPointer, saved, 3, &receipt) == 1,
        "restore succeeds for identical membership");
    Check(receipt.result == 1 && Same(values, saved, 3),
        "restore writes exact order");

    uintptr_t before[3] = { values[0], values[1], values[2] };
    receipt = {};
    Check(restore(contextPointer, arrayPointer + sizeof(uintptr_t), saved, 3,
        &receipt) == 0 && receipt.result == 9,
        "wrong free-array address is rejected");
    Check(Same(values, before, 3), "array-address rejection is non-mutating");

    receipt = {};
    Check(restore(contextPointer, arrayPointer, saved, 2, &receipt) == 0 &&
        receipt.result == 10, "wrong count is rejected");
    Check(Same(values, before, 3), "count rejection is non-mutating");

    uintptr_t duplicate[3] = { saved[0], saved[0], saved[2] };
    receipt = {};
    Check(restore(contextPointer, arrayPointer, duplicate, 3, &receipt) == 0 &&
        receipt.result == 6, "duplicate snapshot is rejected");
    Check(Same(values, before, 3), "duplicate rejection is non-mutating");

    values[2] = reinterpret_cast<uintptr_t>(managers[3]);
    uintptr_t changed[3] = { values[0], values[1], values[2] };
    receipt = {};
    Check(restore(contextPointer, arrayPointer, saved, 3, &receipt) == 0 &&
        receipt.result == 11, "changed membership is rejected");
    Check(Same(values, changed, 3), "membership rejection is non-mutating");

    receipt = {};
    uintptr_t tooSmall[2] = {};
    Check(capture(contextPointer, tooSmall, 2, &receipt) == 0 &&
        receipt.result == 2, "short capture buffer is rejected");
    Check(Same(values, changed, 3), "capture rejection is non-mutating");

    uint8_t* revisionImage = CreateRevisionImage();
    Check(revisionImage != 0, "revision image allocation");
    if (revisionImage) {
        RunManifoldPoolTests(revisionImage, 0, captureManifold,
            restoreManifold);
        RunManifoldPoolTests(revisionImage, 1, captureManifold,
            restoreManifold);

        uint8_t manifoldContext[0x538] = {};
        uint8_t manifoldNodes[4][0xF0] = {};
        InitializeManifoldPool(manifoldContext, 0, manifoldNodes);
        uintptr_t manifoldSaved[3] = {};
        ManifoldPoolReceipt manifoldReceipt = {};
        revisionImage[kLargePoolCallsiteRva] ^= 1;
        Check(captureManifold(reinterpret_cast<uintptr_t>(revisionImage),
            reinterpret_cast<uintptr_t>(manifoldContext), 0, manifoldSaved,
            3, &manifoldReceipt) == 0 && manifoldReceipt.result == 3,
            "manifold revision mismatch is rejected");
        revisionImage[kLargePoolCallsiteRva] ^= 1;
        manifoldReceipt = {};
        Check(captureManifold(reinterpret_cast<uintptr_t>(revisionImage),
            reinterpret_cast<uintptr_t>(manifoldContext), 2, manifoldSaved,
            3, &manifoldReceipt) == 0 && manifoldReceipt.result == 4,
            "invalid manifold pool kind is rejected");
        RunDirtyInteractionTests(revisionImage, installDirty, statusDirty,
            armDirtyCapture, copyDirtyCapture, armDirtyRestore,
            cancelDirty, uninstallDirty);
        VirtualFree(revisionImage, 0, MEM_RELEASE);
    }

    FreeLibrary(library);
    if (failures) {
        printf("FAILED %d\n", failures);
        return 1;
    }
    printf("PASS caller-owned contact/manifold-pool history\n");
    return 0;
}
