#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#if !defined(_M_IX86)
#error Build this harness for x86.
#endif

#pragma pack(push, 8)
struct TraceStatus {
    uint32_t apiVersion, structSize, eventSize, installedMask;
    uint32_t installedHookCount, capacity;
    int32_t latestSequence, droppedEstimate;
    uint32_t lastError;
};

struct TraceEvent {
    volatile LONG sequence;
    uint32_t kind, threadId;
    int64_t qpc;
    uintptr_t self, returnAddress;
    uintptr_t stack[8], payload[8], frames[8], extra[5];
};
#pragma pack(pop)

typedef uint32_t (__cdecl *ApiVersion)();
typedef int (__cdecl *Install)(uintptr_t, uint32_t);
typedef int (__cdecl *Uninstall)();
typedef int (__cdecl *Status)(TraceStatus*);
typedef int (__cdecl *Read)(int32_t, TraceEvent*, int32_t);

struct Bytes { uint32_t rva; const uint8_t* value; uint32_t count; };

static void Put(uint8_t* image, const Bytes& item) {
    memcpy(image + item.rva, item.value, item.count);
}

int main(int argc, char** argv) {
    if (argc != 2) { fprintf(stderr, "usage: harness trace.dll\n"); return 2; }
    HMODULE module = LoadLibraryA(argv[1]);
    if (!module) { fprintf(stderr, "LoadLibrary failed: %lu\n", GetLastError()); return 3; }
    ApiVersion api = reinterpret_cast<ApiVersion>(GetProcAddress(module, "oc2_trace_api_version"));
    Install install = reinterpret_cast<Install>(GetProcAddress(module, "oc2_trace_install"));
    Uninstall uninstall = reinterpret_cast<Uninstall>(GetProcAddress(module, "oc2_trace_uninstall"));
    Status status = reinterpret_cast<Status>(GetProcAddress(module, "oc2_trace_status"));
    Read read = reinterpret_cast<Read>(GetProcAddress(module, "oc2_trace_read"));
    if (!api || !install || !uninstall || !status || !read || api() != 1) return 4;

    const size_t imageSize = 0xB30000;
    uint8_t* image = static_cast<uint8_t*>(VirtualAlloc(0, imageSize,
        MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
    if (!image) return 5;

    static const uint8_t awake[] = {0x55,0x8B,0xEC,0x83,0xEC,0x0C};
    static const uint8_t create[] = {0x55,0x8B,0xEC,0x81,0xEC,0x90,0x00,0x00,0x00};
    static const uint8_t move[] = {0x55,0x8B,0xEC,0x8B,0x45,0x08};
    static const uint8_t kinematic[] = {0x55,0x8B,0xEC,0x83,0xEC,0x2C};
    static const uint8_t scbPose[] = {0x55,0x8B,0xEC,0x56,0x8B,0xF1};
    static const uint8_t npPose[] = {0x55,0x8B,0xEC,0x83,0xEC,0x44};
    static const uint8_t npTarget[] = {0x55,0x8B,0xEC,0x83,0xEC,0x38};
    static const uint8_t scPose[] = {0x55,0x8B,0xEC,0x8B,0x55,0x08};
    static const uint8_t cmass[] = {0x55,0x8B,0xEC,0x83,0xEC,0x48,0x53,0x8B,0xD9};
    static const uint8_t inertia[] = {0x55,0x8B,0xEC,0x8B,0x55,0x08,0x83,0xEC,0x0C};
    static const uint8_t mass[] = {0x55,0x8B,0xEC,0x83,0xEC,0x4C,0x53,0x56,0x57};
    static const uint8_t diagonalize[] = {0x55,0x8B,0xEC,0x81,0xEC,0xA0,0x00,0x00,0x00};
    static const uint8_t boxPose[] = {0x55,0x8B,0xEC,0x83,0xEC,0x40};
    static const uint8_t syncTransforms[] = {0x55,0x8B,0xEC,0x83,0xEC,0x3C};
    static const uint8_t queueChanges[] = {0x56,0x8B,0x71,0x20,0x8B,0x46,0x20};
    static const uint8_t animatorUpdate[] = {0x55,0x8B,0xEC,0x83,0xEC,0x68};
    static const uint8_t animatorWrite[] = {0x55,0x8B,0xEC,0x56,0x8B,0xF1};
    static const uint8_t directorPrepare[] = {0x55,0x8B,0xEC,0x83,0xEC,0x18};
    static const uint8_t directorProcess[] = {0x55,0x8B,0xEC,0x8B,0x45,0x08};
    static const uint8_t controllerPrepare[] = {0x55,0x8B,0xEC,0x56,0x8B,0xF1};
    static const uint8_t clearFirstEvaluation[] = {0x56,0x8B,0xF1,0x8B,0x86,0xA0,0x00,0x00,0x00};
    static const uint8_t controllerUpdateGraph[] = {0x55,0x8B,0xEC,0x83,0xEC,0x5C};
    static const uint8_t evaluateStateMachine[] = {0x55,0x8B,0xEC,0x83,0xEC,0x40};
    static const uint8_t evaluateStateMachineEarlyExit[] = {0x5B,0x8B,0xE5,0x5D,0xC3};
    static const uint8_t evaluateStateMachineExit[] = {0x5B,0x8B,0xE5,0x5D,0xC3};
    static const uint8_t animatorConditionFloat[] = {0x0F,0x2F,0x4E,0x08,0x5F};
    static const uint8_t animatorEndTransition[] = {0x55,0x8B,0xEC,0x83,0xEC,0x08};
    static const uint8_t animatorStartInterruptedTransition[] = {0x55,0x8B,0xEC,0x53,0x8B,0xD9};
    static const uint8_t getShapes[] = {0x55,0x8B,0xEC,0x83,0xC1,0x14,0x5D,0xE9};
    static const uint8_t getType[] = {0x8B,0x41,0x74,0xC3};
    static const uint8_t getBox[] = {0x55,0x8B,0xEC,0x83,0x79,0x74,0x03};
    static const uint8_t getCapsule[] = {0x55,0x8B,0xEC,0x83,0x79,0x74,0x02};
    static const uint8_t getFlags[] = {0x55,0x8B,0xEC,0xF6,0x41,0x24,0x40};
    static const uint8_t getLocalPose[] = {0x55,0x8B,0xEC,0xF6,0x41,0x24,0x04,0x56};
    static const uint8_t pcm[] = {0x53,0x8B,0xDC,0x83,0xEC,0x08,0x83,0xE4,0xF0};
    static const uint8_t overlapCreated[] = {0x55,0x8B,0xEC,0x81,0xEC,0x94,0x00,0x00,0x00};
    static const uint8_t createContactManager[] = {0x55,0x8B,0xEC,0x53,0x8B,0xD9};
    static const uint8_t updateDirtyInteractions[] = {0x55,0x8B,0xEC,0x83,0xEC,0x34};
    static const uint8_t pairCreateManager[] = {0x55,0x8B,0xEC,0x81,0xEC,0x94,0x00,0x00,0x00};
    const Bytes bytes[] = {
        {0x481B50,awake,sizeof(awake)},{0x482510,create,sizeof(create)},
        {0x483550,move,sizeof(move)},{0x4840D0,kinematic,sizeof(kinematic)},
        {0xA136D0,scbPose,sizeof(scbPose)},{0xA143D0,npPose,sizeof(npPose)},
        {0xA149E0,npTarget,sizeof(npTarget)},{0xA3A9B0,scPose,sizeof(scPose)},
        {0xA13E90,cmass,sizeof(cmass)},{0xA14E80,inertia,sizeof(inertia)},
        {0x484D80,mass,sizeof(mass)},{0x465240,boxPose,sizeof(boxPose)},
        {0xB2E3A0,diagonalize,sizeof(diagonalize)},
        {0x47C8B0,syncTransforms,sizeof(syncTransforms)},
        {0x43F35A,queueChanges,sizeof(queueChanges)},
        {0x62B5D0,animatorUpdate,sizeof(animatorUpdate)},
        {0x62C750,animatorWrite,sizeof(animatorWrite)},
        {0x2F2030,directorPrepare,sizeof(directorPrepare)},
        {0x2F2230,directorProcess,sizeof(directorProcess)},
        {0x649090,controllerPrepare,sizeof(controllerPrepare)},
        {0x646A60,clearFirstEvaluation,sizeof(clearFirstEvaluation)},
        {0x649CE0,controllerUpdateGraph,sizeof(controllerUpdateGraph)},
        {0x663C50,evaluateStateMachine,sizeof(evaluateStateMachine)},
        {0x663C8A,evaluateStateMachineEarlyExit,sizeof(evaluateStateMachineEarlyExit)},
        {0x664650,evaluateStateMachineExit,sizeof(evaluateStateMachineExit)},
        {0x663524,animatorConditionFloat,sizeof(animatorConditionFloat)},
        {0x646CD0,animatorEndTransition,sizeof(animatorEndTransition)},
        {0x649C60,animatorStartInterruptedTransition,sizeof(animatorStartInterruptedTransition)},
        {0xA10740,getShapes,sizeof(getShapes)},{0x842360,getType,sizeof(getType)},
        {0xA0D0B0,getBox,sizeof(getBox)},{0xA0D0F0,getCapsule,sizeof(getCapsule)},
        {0xA0D1D0,getFlags,sizeof(getFlags)},{0xA0D2A0,getLocalPose,sizeof(getLocalPose)},
        {0xA9E900,pcm,sizeof(pcm)},{0xA51210,overlapCreated,sizeof(overlapCreated)},
        {0xA69E80,createContactManager,sizeof(createContactManager)},
        {0xA540F0,updateDirtyInteractions,sizeof(updateDirtyInteractions)},
        {0xA54430,pairCreateManager,sizeof(pairCreateManager)}
    };
    for (uint32_t i=0;i<sizeof(bytes)/sizeof(bytes[0]);++i) Put(image,bytes[i]);

    // Complete the four newly executable synthetic functions with ABI-correct
    // epilogues. The other patch points are install/uninstall byte fixtures and
    // are intentionally never invoked by this harness.
    static const uint8_t cdeclEpilogue[] = {0x8B,0xE5,0x5D,0xC3};
    static const uint8_t writeEpilogue[] = {0x5E,0x5D,0xC2,0x08,0x00};
    static const uint8_t thiscallOneArgEpilogue[] = {0x8B,0xE5,0x5D,0xC2,0x04,0x00};
    static const uint8_t processEpilogue[] = {0x5D,0xC2,0x04,0x00};
    memcpy(image+0x62B5D0+sizeof(animatorUpdate),cdeclEpilogue,sizeof(cdeclEpilogue));
    memcpy(image+0x62C750+sizeof(animatorWrite),writeEpilogue,sizeof(writeEpilogue));
    memcpy(image+0x2F2030+sizeof(directorPrepare),thiscallOneArgEpilogue,sizeof(thiscallOneArgEpilogue));
    memcpy(image+0x2F2230+sizeof(directorProcess),processEpilogue,sizeof(processEpilogue));
    // These synthetic bodies write to otherwise-unobserved words on self so
    // the harness proves that ECX and both StartInterruptedTransition stack
    // arguments reached the trampoline, in addition to proving final cleanup.
    static const uint8_t endTransitionBody[] = {
        0xC7,0x41,0x28,0x78,0x56,0x34,0x12, // mov [ecx+28h],12345678h
        0x8B,0xE5,0x5D,0xC3
    };
    static const uint8_t startInterruptedTransitionBody[] = {
        0x8B,0x45,0x08,0x89,0x43,0x20, // mov eax,[ebp+8]; mov [ebx+20h],eax
        0x8B,0x45,0x0C,0x89,0x43,0x24, // mov eax,[ebp+0Ch]; mov [ebx+24h],eax
        0x5B,0x5D,0xC2,0x08,0x00
    };
    memcpy(image+0x646CD0+sizeof(animatorEndTransition),endTransitionBody,sizeof(endTransitionBody));
    memcpy(image+0x649C60+sizeof(animatorStartInterruptedTransition),
        startInterruptedTransitionBody,sizeof(startInterruptedTransitionBody));

    TraceStatus value = {};
    int bad = install(reinterpret_cast<uintptr_t>(image),0x400u);
    status(&value);
    if (bad != 0 || value.lastError != 0xE100u || value.installedHookCount != 0) {
        fprintf(stderr,"argument test failed: result=%d error=0x%X hooks=%u\n",
            bad,value.lastError,value.installedHookCount); return 6;
    }
    int focused = install(reinterpret_cast<uintptr_t>(image),128u);
    status(&value);
    if (focused != 1 || value.installedMask != 128 || value.installedHookCount != 7 || value.lastError != 0) return 7;

    uintptr_t graphWords[32] = {};
    graphWords[0x10/4] = 0x11223344u;
    graphWords[0x14/4] = 0x55667788u;
    graphWords[0x20/4] = 0x99AABBCCu;
    graphWords[0x24/4] = 0xDDEEFF00u;
    uintptr_t outputWords[8] = {};
    outputWords[0x14/4] = reinterpret_cast<uintptr_t>(graphWords);
    uintptr_t outputData[1] = {reinterpret_cast<uintptr_t>(outputWords)};
    uintptr_t outputList[3] = {reinterpret_cast<uintptr_t>(outputData),1u,1u};
    uintptr_t graphNodeWords[8] = {};
    graphNodeWords[0x14/4] = reinterpret_cast<uintptr_t>(graphWords);
    uintptr_t animatorWords[0x2A0/4] = {};
    animatorWords[0x78/4] = 0x01020304u;
    animatorWords[0x7C/4] = 0x11121314u;
    animatorWords[0x80/4] = 0x21222324u;
    animatorWords[0x84/4] = 0x31323334u;
    animatorWords[0x268/4] = 0x41424344u;
    animatorWords[0x288/4] = 0x51525354u;
    animatorWords[0x28C/4] = reinterpret_cast<uintptr_t>(graphNodeWords);
    animatorWords[0x290/4] = 0x61626364u;

    typedef void (__cdecl *UpdateAvatars)(void*,int,int,int);
    typedef void (__thiscall *WriteProperties)(void*,float,float);
    typedef void (__thiscall *DirectorStage)(void*,int);
    reinterpret_cast<UpdateAvatars>(image+0x62B5D0)(outputList,1,0,1);
    reinterpret_cast<WriteProperties>(image+0x62C750)(animatorWords,1.25f,2.5f);
    reinterpret_cast<DirectorStage>(image+0x2F2030)(graphWords,3);
    reinterpret_cast<DirectorStage>(image+0x2F2230)(graphWords,4);

    TraceEvent events[8] = {};
    int count = read(0,events,8);
    if (count != 4 || events[0].kind != 53 || events[1].kind != 54 ||
        events[2].kind != 55 || events[3].kind != 56) return 8;
    if (events[0].self != reinterpret_cast<uintptr_t>(outputList) ||
        events[0].payload[1] != 1 || events[0].payload[3] != reinterpret_cast<uintptr_t>(graphWords) ||
        events[0].payload[4] != 0x11223344u || events[0].payload[7] != 0xDDEEFF00u) return 9;
    if (events[1].self != reinterpret_cast<uintptr_t>(animatorWords) ||
        events[1].payload[0] != 0x01020304u || events[1].payload[7] != 0x61626364u ||
        events[1].extra[0] != reinterpret_cast<uintptr_t>(graphWords) ||
        events[1].extra[1] != 0x11223344u || events[1].extra[4] != 0xDDEEFF00u) return 10;
    if (events[2].payload[0] != 3 || events[3].payload[0] != 4) return 11;
    if (uninstall() != 1) return 12;

    int transitionLifecycle = install(reinterpret_cast<uintptr_t>(image),512u);
    status(&value);
    if (transitionLifecycle != 1 || value.installedMask != 512 ||
        value.installedHookCount != 2 || value.lastError != 0) return 18;
    const int32_t transitionAfter = value.latestSequence;

    uintptr_t clip0[0x110/4] = {};
    uintptr_t clip1[0x110/4] = {};
    uintptr_t clip2[0x110/4] = {};
    clip0[0] = reinterpret_cast<uintptr_t>(image) + 0xE83610;
    clip1[0] = reinterpret_cast<uintptr_t>(image) + 0xE83610;
    clip2[0] = reinterpret_cast<uintptr_t>(image) + 0xE83610;
    clip0[0x108/4] = 0x11111111u;
    clip1[0x108/4] = 0x22222222u;
    clip2[0x108/4] = 0x33333333u;
    uintptr_t branchEntries0[3] = {0x3F800000u,reinterpret_cast<uintptr_t>(clip0),0x10u};
    uintptr_t branchEntries1[3] = {0x3F000000u,reinterpret_cast<uintptr_t>(clip1),0x20u};
    uintptr_t branchEntries2[3] = {0u,reinterpret_cast<uintptr_t>(clip2),0x30u};
    uintptr_t branchInternal0[8] = {};
    uintptr_t branchInternal1[8] = {};
    uintptr_t branchInternal2[8] = {};
    branchInternal0[0x10/4] = reinterpret_cast<uintptr_t>(branchEntries0); branchInternal0[0x18/4] = 1;
    branchInternal1[0x10/4] = reinterpret_cast<uintptr_t>(branchEntries1); branchInternal1[0x18/4] = 1;
    branchInternal2[0x10/4] = reinterpret_cast<uintptr_t>(branchEntries2); branchInternal2[0x18/4] = 1;
    uintptr_t branch0[0xB0/4] = {};
    uintptr_t branch1[0xB0/4] = {};
    uintptr_t branch2[0xB0/4] = {};
    branch0[0] = reinterpret_cast<uintptr_t>(image) + 0xE83744;
    branch1[0] = reinterpret_cast<uintptr_t>(image) + 0xE83744;
    branch2[0] = reinterpret_cast<uintptr_t>(image) + 0xE83744;
    branch0[0x10/4] = reinterpret_cast<uintptr_t>(branchInternal0);
    branch1[0x10/4] = reinterpret_cast<uintptr_t>(branchInternal1);
    branch2[0x10/4] = reinterpret_cast<uintptr_t>(branchInternal2);
    reinterpret_cast<uint8_t*>(branch0)[0xA0] = 1;
    reinterpret_cast<uint8_t*>(branch1)[0xA4] = 2;
    reinterpret_cast<uint8_t*>(branch2)[0xA5] = 3;
    uintptr_t outerEntries[9] = {
        0x3F800000u,reinterpret_cast<uintptr_t>(branch0),0x100u,
        0u,reinterpret_cast<uintptr_t>(branch1),0x200u,
        0u,reinterpret_cast<uintptr_t>(branch2),0x300u
    };
    uintptr_t outerInternal[8] = {};
    outerInternal[0x10/4] = reinterpret_cast<uintptr_t>(outerEntries);
    outerInternal[0x18/4] = 3;
    uintptr_t outer[0xB0/4] = {};
    outer[0] = reinterpret_cast<uintptr_t>(image) + 0xE839C8;
    outer[0x10/4] = reinterpret_cast<uintptr_t>(outerInternal);
    reinterpret_cast<uint8_t*>(outer)[0xA0] = 4;
    reinterpret_cast<uint8_t*>(outer)[0xA4] = 5;
    reinterpret_cast<uint8_t*>(outer)[0xA5] = 6;

    typedef void (__thiscall *EndTransition)(void*);
    typedef void (__thiscall *StartInterruptedTransition)(void*,uint32_t,uint32_t);
    reinterpret_cast<EndTransition>(image+0x646CD0)(outer);
    reinterpret_cast<StartInterruptedTransition>(image+0x649C60)(outer,0x11223344u,0x55667788u);
    if (outer[0x28/4] != 0x12345678u || outer[0x20/4] != 0x11223344u ||
        outer[0x24/4] != 0x55667788u) return 25;

    TraceEvent transitionEvents[8] = {};
    int transitionCount = read(transitionAfter,transitionEvents,8);
    if (transitionCount != 4 || transitionEvents[0].kind != 68 ||
        transitionEvents[1].kind != 69 || transitionEvents[2].kind != 70 ||
        transitionEvents[3].kind != 71) return 19;
    for (int i=0;i<4;++i) {
        if (transitionEvents[i].self != reinterpret_cast<uintptr_t>(outer) ||
            transitionEvents[i].payload[2] != 3 ||
            (transitionEvents[i].extra[4] & 0x0007FF03u) != 0x00070903u) return 20;
    }
    if (transitionEvents[1].stack[3] != static_cast<uintptr_t>(transitionEvents[0].sequence) ||
        transitionEvents[3].stack[3] != static_cast<uintptr_t>(transitionEvents[2].sequence)) return 21;
    if (transitionEvents[2].stack[5] != 0x11223344u ||
        transitionEvents[2].stack[6] != 0x55667788u ||
        transitionEvents[3].stack[5] != 0x11223344u ||
        transitionEvents[3].stack[6] != 0x55667788u) return 22;
    if (transitionEvents[0].payload[3] != transitionEvents[1].payload[3] ||
        transitionEvents[2].payload[3] != transitionEvents[3].payload[3] ||
        transitionEvents[0].frames[6] == 0xFFFFFFFFu ||
        transitionEvents[0].extra[0] == 0xFFFFFFFFu ||
        transitionEvents[0].extra[2] == 0xFFFFFFFFu) return 23;
    if (uninstall() != 1) return 24;

    int stateMachine = install(reinterpret_cast<uintptr_t>(image),256u);
    status(&value);
    if (stateMachine != 1 || value.installedMask != 256 ||
        value.installedHookCount != 5 || value.lastError != 0) return 13;
    if (uninstall() != 1) return 14;

    int narrowPhase = install(reinterpret_cast<uintptr_t>(image),16u);
    status(&value);
    if (narrowPhase != 1 || value.installedMask != 16 ||
        value.installedHookCount != 5 || value.lastError != 0) return 26;
    if (uninstall() != 1) return 27;

    int good = install(reinterpret_cast<uintptr_t>(image),229u);
    status(&value);
    printf("api=%u result=%d mask=%u hooks=%u error=0x%X\n",
        value.apiVersion,good,value.installedMask,value.installedHookCount,value.lastError);
    if (good != 1 || value.installedMask != 229 || value.installedHookCount != 22 || value.lastError != 0) return 15;
    if (uninstall() != 1) return 16;
    status(&value);
    if (value.installedMask != 0 || value.installedHookCount != 0) return 17;
    VirtualFree(image,0,MEM_RELEASE);
    FreeLibrary(module);
    return 0;
}
