#include <windows.h>
#include <stdint.h>
#include <stdio.h>

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
#pragma pack(pop)

static_assert(sizeof(ManifoldPoolReceipt) == 200,
    "Unexpected Win32 manifold-pool receipt ABI");

typedef uint32_t (__cdecl *ApiVersion)();
typedef int (__cdecl *CaptureSnapshot)(uintptr_t, uintptr_t*, uint32_t,
    ContactPoolReceipt*);
typedef int (__cdecl *RestoreSnapshot)(uintptr_t, uintptr_t,
    const uintptr_t*, uint32_t, ContactPoolReceipt*);
typedef int (__cdecl *CaptureManifoldSnapshot)(uintptr_t, uintptr_t, uint32_t,
    uintptr_t*, uint32_t, ManifoldPoolReceipt*);
typedef int (__cdecl *RestoreManifoldSnapshot)(uintptr_t, uintptr_t, uint32_t,
    uintptr_t, const uintptr_t*, uint32_t, ManifoldPoolReceipt*);

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
        MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
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
    Check(receipt.result == 1 && receipt.apiVersion == 9 &&
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
    Check(version && version() == 9, "API version");
    Check(capture != 0, "capture export");
    Check(restore != 0, "restore export");
    Check(captureManifold != 0, "manifold capture export");
    Check(restoreManifold != 0, "manifold restore export");
    if (!version || !capture || !restore || !captureManifold ||
        !restoreManifold) {
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
    Check(receipt.result == 1 && receipt.apiVersion == 9 &&
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
