// Kept free of the private-field access shim: MSVC encodes a member's access
// level in its symbol name. The original lazy ActorPair method must bind to
// the SDK's unmodified private declaration in ScActorPair.cpp.
#include "ScActorPair.h"

extern "C" void __cdecl oc2_physx333_joined_lazy_report(void* actorPair)
{
    static_cast<physx::Sc::ActorPair*>(actorPair)->getContactStreamManager();
}
