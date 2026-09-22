cmake_minimum_required(VERSION 3.20)
project(physx333_memblock_restore_check LANGUAGES CXX)

# Reuse the pinned SDK headers, libraries and fixture compiler settings. This
# local build checks the restore translation unit without editing the harness.
add_subdirectory(../harness "${CMAKE_CURRENT_BINARY_DIR}/harness" EXCLUDE_FROM_ALL)
set(PHYSX333_TEST_BIN
    "${CMAKE_CURRENT_LIST_DIR}/../build/work/PhysXSDK/Bin/vc12win32")

add_executable(memblock_restore_test
  Test.cpp
  MemBlockRestore.cpp
  ../memblock/MemBlockImage.cpp
  ../sap/SapImage.cpp
  ../oracle/Oracle.cpp
  ../cache/TransformCacheImage.cpp
  ../nphase/NPhaseTopology.cpp
  ../island/IslandImage.cpp
)
get_target_property(_harness_includes physx333_offline_harness INCLUDE_DIRECTORIES)
get_target_property(_harness_definitions physx333_offline_harness COMPILE_DEFINITIONS)
get_target_property(_harness_options physx333_offline_harness COMPILE_OPTIONS)
get_target_property(_harness_libraries physx333_offline_harness LINK_LIBRARIES)
target_compile_features(memblock_restore_test PRIVATE cxx_std_11)
target_include_directories(memblock_restore_test PRIVATE ${_harness_includes})
target_compile_definitions(memblock_restore_test PRIVATE ${_harness_definitions})
target_compile_options(memblock_restore_test PRIVATE ${_harness_options})
if(MSVC)
  target_compile_options(memblock_restore_test PRIVATE /Gy)
  target_link_options(memblock_restore_test PRIVATE /OPT:REF)
endif()
target_link_libraries(memblock_restore_test PRIVATE ${_harness_libraries})
add_custom_command(TARGET memblock_restore_test POST_BUILD
  COMMAND ${CMAKE_COMMAND} -E copy_if_different
    "${PHYSX333_TEST_BIN}/PhysX3_x86.dll"
    "${PHYSX333_TEST_BIN}/PhysX3Common_x86.dll"
    $<TARGET_FILE_DIR:memblock_restore_test>
)
