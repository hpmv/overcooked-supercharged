cmake_minimum_required(VERSION 3.20)
project(Oc2NativePhysicsTrace LANGUAGES CXX)

add_library(Oc2NativePhysicsTrace SHARED Oc2NativePhysicsTrace.cpp)
target_compile_features(Oc2NativePhysicsTrace PRIVATE cxx_std_14)
target_compile_definitions(Oc2NativePhysicsTrace PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX)
target_compile_options(Oc2NativePhysicsTrace PRIVATE /W4 /WX /GS- /guard:cf-)
set_target_properties(Oc2NativePhysicsTrace PROPERTIES
    OUTPUT_NAME "Oc2NativePhysicsTrace"
    PREFIX ""
)

if(NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
    message(FATAL_ERROR "Oc2NativePhysicsTrace must be built for Win32/x86")
endif()
