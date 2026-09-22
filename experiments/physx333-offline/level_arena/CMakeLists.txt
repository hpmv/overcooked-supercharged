cmake_minimum_required(VERSION 3.20)
project(physx333_level_arena LANGUAGES CXX)

if(NOT WIN32 OR NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "The level-arena diagnostic requires Windows x86")
endif()

# Reuse the level graph's compile contract without copying its long list of
# pinned-source include roots. The graph target is built only as a dependency.
add_subdirectory("${CMAKE_CURRENT_LIST_DIR}/../level_graph"
                 "${CMAKE_CURRENT_BINARY_DIR}/level_graph")
get_target_property(LEVEL_INCLUDES physx333_level_graph INCLUDE_DIRECTORIES)
get_target_property(LEVEL_LIBRARIES physx333_level_graph LINK_LIBRARIES)

set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded")
add_executable(physx333_level_arena
  LevelArena.cpp
  ../arena_snapshot/ArenaSnapshot.cpp
  ../arena_snapshot/ArenaAabbPadding.cpp
  ../oracle/Oracle.cpp
  ../cache/TransformCacheImage.cpp
  ../sap/SapImage.cpp
  ../island/IslandImage.cpp
  ../aux_interactions/AuxInteractionImage.cpp
  ../actor_pair_graph/ActorPairGraphImage.cpp)
target_compile_features(physx333_level_arena PRIVATE cxx_std_11)
target_compile_definitions(physx333_level_arena PRIVATE
  WIN32 NDEBUG _CRT_SECURE_NO_DEPRECATE _CRT_NONSTDC_NO_DEPRECATE)
if(MSVC)
  target_compile_options(physx333_level_arena PRIVATE /W4 /WX- /EHsc)
endif()
target_include_directories(physx333_level_arena PRIVATE ${LEVEL_INCLUDES})
target_link_libraries(physx333_level_arena PRIVATE ${LEVEL_LIBRARIES})

set(PHYSX333_BIN "${CMAKE_CURRENT_LIST_DIR}/../build/work/PhysXSDK/Bin/vc12win32")
add_custom_command(TARGET physx333_level_arena POST_BUILD
  COMMAND ${CMAKE_COMMAND} -E copy_if_different
    "${PHYSX333_BIN}/PhysX3_x86.dll"
    "${PHYSX333_BIN}/PhysX3Common_x86.dll"
    $<TARGET_FILE_DIR:physx333_level_arena>)
