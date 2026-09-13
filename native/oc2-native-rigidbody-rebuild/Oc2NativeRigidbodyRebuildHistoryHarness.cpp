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
#pragma pack(pop)

typedef uint32_t (__cdecl *ApiVersion)();
typedef int (__cdecl *CaptureSnapshot)(uintptr_t, uintptr_t*, uint32_t,
    ContactPoolReceipt*);
typedef int (__cdecl *RestoreSnapshot)(uintptr_t, uintptr_t,
    const uintptr_t*, uint32_t, ContactPoolReceipt*);

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
    Check(version && version() == 6, "API version");
    Check(capture != 0, "capture export");
    Check(restore != 0, "restore export");
    if (!version || !capture || !restore) {
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
    Check(receipt.result == 1 && receipt.apiVersion == 6 &&
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

    FreeLibrary(library);
    if (failures) {
        printf("FAILED %d\n", failures);
        return 1;
    }
    printf("PASS caller-owned contact-pool history\n");
    return 0;
}
