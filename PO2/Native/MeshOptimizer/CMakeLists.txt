cmake_minimum_required(VERSION 3.21)

if(POLICY CMP0091)
    cmake_policy(SET CMP0091 NEW)
endif()

project(PO2MeshOptimizer VERSION 1.0 LANGUAGES CXX)

if(NOT WIN32)
    message(FATAL_ERROR "The PO2 runtime package currently targets Windows x64 only.")
endif()

if(NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "The PO2 meshoptimizer bridge requires a 64-bit target.")
endif()

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

add_library(po2_meshoptimizer SHARED
    bridge/po2_meshoptimizer.cpp
    bridge/po2_meshoptimizer.h
    vendor/meshoptimizer/src/allocator.cpp
    vendor/meshoptimizer/src/meshoptimizer.h
    vendor/meshoptimizer/src/simplifier.cpp
    vendor/meshoptimizer/src/vcacheoptimizer.cpp)

target_include_directories(po2_meshoptimizer PRIVATE
    bridge
    vendor/meshoptimizer/src)

set_target_properties(po2_meshoptimizer PROPERTIES
    INTERPROCEDURAL_OPTIMIZATION_RELEASE TRUE
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

if(MSVC)
    target_compile_options(po2_meshoptimizer PRIVATE
        /W4
        /EHsc
        /fp:precise
        /permissive-)
    target_compile_definitions(po2_meshoptimizer PRIVATE
        WIN32_LEAN_AND_MEAN
        NOMINMAX)
    target_link_options(po2_meshoptimizer PRIVATE
        $<$<CONFIG:Release>:/OPT:REF>
        $<$<CONFIG:Release>:/OPT:ICF>)
endif()

enable_testing()

add_executable(po2_meshoptimizer_tests
    tests/po2_meshoptimizer_tests.cpp)

target_include_directories(po2_meshoptimizer_tests PRIVATE bridge)
target_link_libraries(po2_meshoptimizer_tests PRIVATE po2_meshoptimizer)

if(MSVC)
    target_compile_options(po2_meshoptimizer_tests PRIVATE
        /W4
        /EHsc
        /fp:precise
        /permissive-)
    set_target_properties(po2_meshoptimizer_tests PROPERTIES
        MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

add_test(NAME po2_meshoptimizer_bridge COMMAND po2_meshoptimizer_tests)
