cmake_minimum_required(VERSION 3.20)
project(physx333_joined_manifold_pool LANGUAGES CXX)

if(NOT WIN32 OR NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "The joined-manifold pool image requires Windows x86")
endif()

set(PHYSX333_SDK "${CMAKE_CURRENT_LIST_DIR}/../build/work/PhysXSDK"
    CACHE PATH "Mirrored, locally built PhysX 3.3.3 SDK")
get_filename_component(PHYSX333_SDK "${PHYSX333_SDK}" ABSOLUTE)
set(PHYSX333_SOURCE "${PHYSX333_SDK}/Source")
set(PHYSX333_LIB "${PHYSX333_SDK}/Lib/vc12win32")
set(PHYSX333_BIN "${PHYSX333_SDK}/Bin/vc12win32")
if(NOT EXISTS "${PHYSX333_LIB}/PhysX3_x86.lib")
  message(FATAL_ERROR "Build the pinned PhysX 3.3.3 Win32 Release mirror first")
endif()

set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded")
add_executable(physx333_joined_manifold_pool
  JoinedManifoldPool.cpp
  ../joined_contact_image/JoinedContactImage.cpp
  ../oracle/Oracle.cpp
  ../cache/TransformCacheImage.cpp
  ../sap/SapImage.cpp
  ../island/IslandImage.cpp
  ../memblock/MemBlockImage.cpp
  ../shape_cache/ShapeCacheBindings.cpp
  ../body/BodyImage.cpp
  ../scene_clock/SceneClockImage.cpp
  ../context_image/ContextImage.cpp
  ../query_image/QueryImage.cpp
  ../aux_interactions/AuxInteractionImage.cpp
  ../actor_pair_graph/ActorPairGraphImage.cpp)
target_compile_features(physx333_joined_manifold_pool PRIVATE cxx_std_11)
target_compile_definitions(physx333_joined_manifold_pool PRIVATE
  WIN32 NDEBUG _CRT_SECURE_NO_DEPRECATE _CRT_NONSTDC_NO_DEPRECATE)
if(MSVC)
  target_compile_options(physx333_joined_manifold_pool PRIVATE /W4 /WX- /EHsc)
endif()

target_include_directories(physx333_joined_manifold_pool PRIVATE
  "${PHYSX333_SDK}/Include"
  "${PHYSX333_SDK}/Include/foundation"
  "${PHYSX333_SDK}/Include/common"
  "${PHYSX333_SDK}/Include/geometry"
  "${PHYSX333_SDK}/Include/pxtask"
  "${PHYSX333_SDK}/Include/particles"
  "${PHYSX333_SDK}/Include/cloth"
  "${PHYSX333_SDK}/Include/physxprofilesdk"
  "${PHYSX333_SDK}/Include/physxvisualdebuggersdk"
  "${PHYSX333_SOURCE}/foundation/include"
  "${PHYSX333_SOURCE}/Common/src"
  "${PHYSX333_SOURCE}/Common/src/windows"
  "${PHYSX333_SOURCE}/GeomUtils/headers"
  "${PHYSX333_SOURCE}/GeomUtils/src"
  "${PHYSX333_SOURCE}/GeomUtils/src/common"
  "${PHYSX333_SOURCE}/GeomUtils/src/contact"
  "${PHYSX333_SOURCE}/GeomUtils/src/convex"
  "${PHYSX333_SOURCE}/GeomUtils/src/distance"
  "${PHYSX333_SOURCE}/GeomUtils/src/gjk"
  "${PHYSX333_SOURCE}/GeomUtils/src/hf"
  "${PHYSX333_SOURCE}/GeomUtils/src/intersection"
  "${PHYSX333_SOURCE}/GeomUtils/src/mesh"
  "${PHYSX333_SOURCE}/GeomUtils/src/pcm"
  "${PHYSX333_SOURCE}/GeomUtils/src/sweep"
  "${PHYSX333_SOURCE}/LowLevel/API/include"
  "${PHYSX333_SOURCE}/LowLevel/common/include"
  "${PHYSX333_SOURCE}/LowLevel/common/include/collision"
  "${PHYSX333_SOURCE}/LowLevel/common/include/math"
  "${PHYSX333_SOURCE}/LowLevel/common/include/pipeline"
  "${PHYSX333_SOURCE}/LowLevel/common/include/utils"
  "${PHYSX333_SOURCE}/LowLevel/software/include"
  "${PHYSX333_SOURCE}/LowLevel/windows/include"
  "${PHYSX333_SOURCE}/PhysX/src"
  "${PHYSX333_SOURCE}/PhysX/src/buffering"
  "${PHYSX333_SOURCE}/PhysX/src/device"
  "${PHYSX333_SOURCE}/SceneQuery"
  "${PHYSX333_SOURCE}/SimulationController/include"
  "${PHYSX333_SOURCE}/SimulationController/src"
  "${PHYSX333_SOURCE}/SimulationController/src/framework"
  "${PHYSX333_SOURCE}/PhysXMetaData/core/include"
  "${PHYSX333_SOURCE}/PvdRuntime/src"
  "${PHYSX333_SDK}/externals/nvToolsExt/1/include"
  "${CMAKE_CURRENT_LIST_DIR}/../build/compat")

target_link_libraries(physx333_joined_manifold_pool PRIVATE
  "${PHYSX333_LIB}/PhysX3_x86.lib"
  "${PHYSX333_LIB}/PhysX3Common_x86.lib")
add_custom_command(TARGET physx333_joined_manifold_pool POST_BUILD
  COMMAND ${CMAKE_COMMAND} -E copy_if_different
    "${PHYSX333_BIN}/PhysX3_x86.dll"
    "${PHYSX333_BIN}/PhysX3Common_x86.dll"
    $<TARGET_FILE_DIR:physx333_joined_manifold_pool>)
