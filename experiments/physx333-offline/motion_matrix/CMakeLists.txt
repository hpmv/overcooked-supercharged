cmake_minimum_required(VERSION 3.20)
project(physx333_motion_matrix LANGUAGES CXX)

# Borrow only the pinned SDK configuration; this build does not compile the
# source mirror or the unrelated joined harness target.
add_subdirectory(../harness harness-build)

add_executable(physx333_motion_matrix
  MotionMatrix.cpp
  ../body/BodyImage.cpp
  ../island/IslandImage.cpp
  ../scene_clock/SceneClockImage.cpp)
target_compile_features(physx333_motion_matrix PRIVATE cxx_std_11)
target_compile_definitions(physx333_motion_matrix PRIVATE
  WIN32 NDEBUG _CRT_SECURE_NO_DEPRECATE _CRT_NONSTDC_NO_DEPRECATE)
target_compile_options(physx333_motion_matrix PRIVATE /W4 /WX- /EHsc)
target_include_directories(physx333_motion_matrix PRIVATE
  $<TARGET_PROPERTY:physx333_offline_harness,INCLUDE_DIRECTORIES>)
set(PHYSX333_LIB "${CMAKE_CURRENT_LIST_DIR}/../build/work/PhysXSDK/Lib/vc12win32")
set(PHYSX333_BIN "${CMAKE_CURRENT_LIST_DIR}/../build/work/PhysXSDK/Bin/vc12win32")
target_link_libraries(physx333_motion_matrix PRIVATE
  "${PHYSX333_LIB}/PhysX3_x86.lib"
  "${PHYSX333_LIB}/PhysX3Common_x86.lib")
add_custom_command(TARGET physx333_motion_matrix POST_BUILD
  COMMAND ${CMAKE_COMMAND} -E copy_if_different
    "${PHYSX333_BIN}/PhysX3_x86.dll"
    "${PHYSX333_BIN}/PhysX3Common_x86.dll"
    $<TARGET_FILE_DIR:physx333_motion_matrix>)
