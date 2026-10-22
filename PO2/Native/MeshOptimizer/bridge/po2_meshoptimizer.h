#pragma once

#include <stdint.h>

#if defined(_WIN32)
#define PO2_MESHOPT_EXPORT extern "C" __declspec(dllexport)
#define PO2_MESHOPT_CALL __cdecl
#else
#define PO2_MESHOPT_EXPORT extern "C" __attribute__((visibility("default")))
#define PO2_MESHOPT_CALL
#endif

#define PO2_MESHOPT_ABI_VERSION 3u
#define PO2_MESHOPT_MESHOPTIMIZER_VERSION 1020u

enum po2_meshopt_status : uint32_t {
    PO2_MESHOPT_SUCCESS = 0,
    PO2_MESHOPT_INVALID_ARGUMENT = 1,
    PO2_MESHOPT_SIZE_OVERFLOW = 2,
    PO2_MESHOPT_ALLOCATION_FAILED = 3,
    PO2_MESHOPT_INTERNAL_ERROR = 4,
    PO2_MESHOPT_INVALID_OUTPUT = 5,
};

PO2_MESHOPT_EXPORT uint32_t PO2_MESHOPT_CALL
po2_meshopt_get_abi_version();

PO2_MESHOPT_EXPORT uint32_t PO2_MESHOPT_CALL
po2_meshopt_get_meshoptimizer_version();

PO2_MESHOPT_EXPORT uint32_t PO2_MESHOPT_CALL po2_meshopt_simplify(
    uint32_t* destination,
    uint64_t destination_capacity,
    const uint32_t* indices,
    uint64_t index_count,
    const float* vertex_positions,
    uint64_t vertex_count,
    uint64_t vertex_positions_stride,
    const float* vertex_attributes,
    uint64_t vertex_attributes_stride,
    const float* attribute_weights,
    uint32_t attribute_count,
    const uint8_t* vertex_lock,
    uint64_t target_index_count,
    float target_error,
    uint32_t options,
    uint64_t* result_index_count,
    float* result_error);

PO2_MESHOPT_EXPORT uint32_t PO2_MESHOPT_CALL
po2_meshopt_simplify_position_focused(
    uint32_t* destination,
    uint64_t destination_capacity,
    const uint32_t* indices,
    uint64_t index_count,
    const float* vertex_positions,
    uint64_t vertex_count,
    uint64_t vertex_positions_stride,
    uint64_t target_index_count,
    float target_error,
    uint64_t* result_index_count,
    float* result_error);

PO2_MESHOPT_EXPORT uint32_t PO2_MESHOPT_CALL
po2_meshopt_simplify_aggressive(
    uint32_t* destination,
    uint64_t destination_capacity,
    const uint32_t* indices,
    uint64_t index_count,
    const float* vertex_positions,
    uint64_t vertex_count,
    uint64_t vertex_positions_stride,
    uint64_t target_index_count,
    float target_error,
    float prune_error,
    uint64_t* pruned_index_count,
    uint64_t* result_index_count,
    float* result_error);

PO2_MESHOPT_EXPORT uint32_t PO2_MESHOPT_CALL
po2_meshopt_optimize_vertex_cache(
    uint32_t* destination,
    uint64_t destination_capacity,
    const uint32_t* indices,
    uint64_t index_count,
    uint64_t vertex_count);
