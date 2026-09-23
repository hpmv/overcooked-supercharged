cmake_minimum_required(VERSION 3.20)
project(physx333_query_cross_swap_check LANGUAGES CXX)

set(PHYSX333_SDK "${CMAKE_CURRENT_LIST_DIR}/work/PhysXSDK"
    CACHE PATH "Isolated cross-swap PhysX 3.3.3 SDK" FORCE)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded")
add_subdirectory(../harness harness-build)
add_executable(physx333_query_cross_swap_check
  CrossSwapCheck.cpp CrossSwapImage.cpp ../query_image/QueryImage.cpp)
target_compile_features(physx333_query_cross_swap_check PRIVATE cxx_std_11)
target_compile_definitions(physx333_query_cross_swap_check PRIVATE
  WIN32 NDEBUG _CRT_SECURE_NO_DEPRECATE _CRT_NONSTDC_NO_DEPRECATE)
target_compile_options(physx333_query_cross_swap_check PRIVATE /W4 /WX- /EHsc)
target_include_directories(physx333_query_cross_swap_check PRIVATE
  $<TARGET_PROPERTY:physx333_offline_harness,INCLUDE_DIRECTORIES>
  "${CMAKE_CURRENT_LIST_DIR}/work/PhysXSDK/Source/SceneQuery"
  "${CMAKE_CURRENT_LIST_DIR}/work/PhysXSDK/Source/PhysX/src"
  "${CMAKE_CURRENT_LIST_DIR}/../query_image")
set(PHYSX333_LIB "${CMAKE_CURRENT_LIST_DIR}/work/PhysXSDK/Lib/vc12win32")
set(PHYSX333_BIN "${CMAKE_CURRENT_LIST_DIR}/work/PhysXSDK/Bin/vc12win32")
target_link_libraries(physx333_query_cross_swap_check PRIVATE
  "${PHYSX333_LIB}/PhysX3_x86.lib"
  "${PHYSX333_LIB}/PhysX3Common_x86.lib")
add_custom_command(TARGET physx333_query_cross_swap_check POST_BUILD
  COMMAND ${CMAKE_COMMAND} -E copy_if_different
    "${PHYSX333_BIN}/PhysX3_x86.dll"
    "${PHYSX333_BIN}/PhysX3Common_x86.dll"
    $<TARGET_FILE_DIR:physx333_query_cross_swap_check>)
