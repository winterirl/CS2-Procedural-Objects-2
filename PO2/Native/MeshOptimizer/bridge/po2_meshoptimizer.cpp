#include "po2_meshoptimizer.h"

#include "meshoptimizer.h"

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <cstring>
#include <limits>
#include <memory>
#include <new>

namespace {

constexpr uint32_t kAllowedSimplifyOptions =
    meshopt_SimplifyLockBorder |
    meshopt_SimplifySparse |
    meshopt_SimplifyErrorAbsolute |
    meshopt_SimplifyPrune |
    meshopt_SimplifyRegularize |
    meshopt_SimplifyPermissive |
    meshopt_SimplifyRegularizeLight;

bool TryConvertSize(uint64_t value, size_t& result) {
    if (value > static_cast<uint64_t>(std::numeric_limits<size_t>::max())) {
        return false;
    }

    result = static_cast<size_t>(value);
    return true;
}

bool CanAddress(size_t count, size_t stride) {
    return stride == 0 || count <= std::numeric_limits<size_t>::max() / stride;
}

bool HasFinitePositions(
    const float* positions,
    size_t vertexCount,
    size_t stride) {
    const uint8_t* bytes = reinterpret_cast<const uint8_t*>(positions);
    for (size_t i = 0; i < vertexCount; ++i) {
        const float* position = reinterpret_cast<const float*>(bytes + i * stride);
        if (!std::isfinite(position[0]) ||
            !std::isfinite(position[1]) ||
            !std::isfinite(position[2])) {
            return false;
        }
    }

    return true;
}

bool HasFiniteAttributes(
    const float* attributes,
    size_t vertexCount,
    size_t stride,
    size_t attributeCount) {
    const uint8_t* bytes = reinterpret_cast<const uint8_t*>(attributes);
    for (size_t vertex = 0; vertex < vertexCount; ++vertex) {
        const float* values =
            reinterpret_cast<const float*>(bytes + vertex * stride);
        for (size_t attribute = 0; attribute < attributeCount; ++attribute) {
            if (!std::isfinite(values[attribute])) {
                return false;
            }
        }
    }

    return true;
}

bool HasValidWeights(const float* weights, size_t attributeCount) {
    for (size_t i = 0; i < attributeCount; ++i) {
        if (!std::isfinite(weights[i]) || weights[i] < 0.0f) {
            return false;
        }
    }

    return true;
}

bool HasValidVertexLocks(const uint8_t* vertexLock, size_t vertexCount) {
    if (vertexLock == nullptr) {
        return true;
    }

    constexpr uint8_t kAllowedVertexFlags =
        meshopt_SimplifyVertex_Lock |
        meshopt_SimplifyVertex_Protect |
        meshopt_SimplifyVertex_Priority;
    for (size_t i = 0; i < vertexCount; ++i) {
        if ((vertexLock[i] & ~kAllowedVertexFlags) != 0) {
            return false;
        }
    }

    return true;
}

bool HasValidIndices(
    const uint32_t* indices,
    size_t indexCount,
    size_t vertexCount) {
    for (size_t i = 0; i < indexCount; ++i) {
        if (static_cast<size_t>(indices[i]) >= vertexCount) {
            return false;
        }
    }

    return true;
}

}

uint32_t PO2_MESHOPT_CALL po2_meshopt_get_abi_version() {
    return PO2_MESHOPT_ABI_VERSION;
}

uint32_t PO2_MESHOPT_CALL po2_meshopt_get_meshoptimizer_version() {
    return MESHOPTIMIZER_VERSION;
}

uint32_t PO2_MESHOPT_CALL po2_meshopt_simplify(
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
    float* result_error) {
    if (result_index_count != nullptr) {
        *result_index_count = 0;
    }
    if (result_error != nullptr) {
        *result_error = 0.0f;
    }

    if (destination == nullptr ||
        indices == nullptr ||
        vertex_positions == nullptr ||
        result_index_count == nullptr ||
        result_error == nullptr ||
        index_count == 0 ||
        vertex_count == 0 ||
        index_count % 3 != 0 ||
        target_index_count > index_count ||
        target_index_count % 3 != 0 ||
        destination_capacity < index_count ||
        !std::isfinite(target_error) ||
        target_error < 0.0f ||
        (options & ~kAllowedSimplifyOptions) != 0) {
        return PO2_MESHOPT_INVALID_ARGUMENT;
    }

    if (attribute_count > 32 ||
        (attribute_count != 0 &&
            (vertex_attributes == nullptr || attribute_weights == nullptr)) ||
        (attribute_count == 0 &&
            (vertex_attributes != nullptr || attribute_weights != nullptr))) {
        return PO2_MESHOPT_INVALID_ARGUMENT;
    }

    size_t outputCapacity = 0;
    size_t indexCount = 0;
    size_t vertexCount = 0;
    size_t positionStride = 0;
    size_t attributeStride = 0;
    size_t targetIndexCount = 0;
    if (!TryConvertSize(destination_capacity, outputCapacity) ||
        !TryConvertSize(index_count, indexCount) ||
        !TryConvertSize(vertex_count, vertexCount) ||
        !TryConvertSize(vertex_positions_stride, positionStride) ||
        !TryConvertSize(vertex_attributes_stride, attributeStride) ||
        !TryConvertSize(target_index_count, targetIndexCount)) {
        return PO2_MESHOPT_SIZE_OVERFLOW;
    }

    if (vertexCount > std::numeric_limits<uint32_t>::max() ||
        indexCount > std::numeric_limits<size_t>::max() / sizeof(uint32_t) ||
        outputCapacity > std::numeric_limits<size_t>::max() / sizeof(uint32_t) ||
        positionStride < sizeof(float) * 3 ||
        positionStride > 256 ||
        positionStride % sizeof(float) != 0 ||
        !CanAddress(vertexCount, positionStride)) {
        return PO2_MESHOPT_INVALID_ARGUMENT;
    }

    if (attribute_count == 0) {
        if (attributeStride != 0) {
            return PO2_MESHOPT_INVALID_ARGUMENT;
        }
    }
    else if (attributeStride < sizeof(float) * attribute_count ||
        attributeStride > 256 ||
        attributeStride % sizeof(float) != 0 ||
        !CanAddress(vertexCount, attributeStride)) {
        return PO2_MESHOPT_INVALID_ARGUMENT;
    }

    if (!HasValidIndices(indices, indexCount, vertexCount) ||
        !HasFinitePositions(vertex_positions, vertexCount, positionStride) ||
        !HasValidVertexLocks(vertex_lock, vertexCount) ||
        (attribute_count != 0 &&
            (!HasFiniteAttributes(
                vertex_attributes,
                vertexCount,
                attributeStride,
                attribute_count) ||
                !HasValidWeights(attribute_weights, attribute_count)))) {
        return PO2_MESHOPT_INVALID_ARGUMENT;
    }

    try {
        float simplifiedError = 0.0f;
        size_t simplifiedCount = 0;
        if (attribute_count == 0) {
            simplifiedCount = meshopt_simplify(
                destination,
                indices,
                indexCount,
                vertex_positions,
                vertexCount,
                positionStride,
                targetIndexCount,
                target_error,
                options,
                &simplifiedError);
        }
        else {
            simplifiedCount = meshopt_simplifyWithAttributes(
                destination,
                indices,
                indexCount,
                vertex_positions,
                vertexCount,
                positionStride,
                vertex_attributes,
                attributeStride,
                attribute_weights,
                attribute_count,
                vertex_lock,
                targetIndexCount,
                target_error,
                options,
                &simplifiedError);
        }

        if (simplifiedCount > outputCapacity ||
            simplifiedCount % 3 != 0 ||
            !std::isfinite(simplifiedError) ||
            !HasValidIndices(destination, simplifiedCount, vertexCount)) {
            return PO2_MESHOPT_INVALID_OUTPUT;
        }

        *result_index_count = static_cast<uint64_t>(simplifiedCount);
        *result_error = simplifiedError;
        return PO2_MESHOPT_SUCCESS;
    }
    catch (const std::bad_alloc&) {
        return PO2_MESHOPT_ALLOCATION_FAILED;
    }
    catch (...) {
        return PO2_MESHOPT_INTERNAL_ERROR;
    }
}

uint32_t PO2_MESHOPT_CALL po2_meshopt_simplify_position_focused(
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
    float* result_error) {
    if (result_index_count != nullptr) {
        *result_index_count = 0;
    }
    if (result_error != nullptr) {
        *result_error = 0.0f;
    }

    if (destination == nullptr ||
        indices == nullptr ||
        vertex_positions == nullptr ||
        result_index_count == nullptr ||
        result_error == nullptr ||
        index_count == 0 ||
        vertex_count == 0 ||
        index_count % 3 != 0 ||
        target_index_count < 3 ||
        target_index_count > index_count ||
        target_index_count % 3 != 0 ||
        destination_capacity < index_count ||
        !std::isfinite(target_error) ||
        target_error < 0.0f ||
        target_error > 1.0f) {
        return PO2_MESHOPT_INVALID_ARGUMENT;
    }

    size_t outputCapacity = 0;
    size_t indexCount = 0;
    size_t vertexCount = 0;
    size_t positionStride = 0;
    size_t targetIndexCount = 0;
    if (!TryConvertSize(destination_capacity, outputCapacity) ||
        !TryConvertSize(index_count, indexCount) ||
        !TryConvertSize(vertex_count, vertexCount) ||
        !TryConvertSize(vertex_positions_stride, positionStride) ||
        !TryConvertSize(target_index_count, targetIndexCount)) {
        return PO2_MESHOPT_SIZE_OVERFLOW;
    }

    if (vertexCount > std::numeric_limits<uint32_t>::max() ||
        indexCount > std::numeric_limits<size_t>::max() / sizeof(uint32_t) ||
        outputCapacity >
            std::numeric_limits<size_t>::max() / sizeof(uint32_t) ||
        positionStride < sizeof(float) * 3 ||
        positionStride > 256 ||
        positionStride % sizeof(float) != 0 ||
        !CanAddress(vertexCount, positionStride) ||
        !HasValidIndices(indices, indexCount, vertexCount) ||
        !HasFinitePositions(
            vertex_positions,
            vertexCount,
            positionStride)) {
        return PO2_MESHOPT_INVALID_ARGUMENT;
    }

    try {
        float simplifiedError = 0.0f;
        size_t simplifiedCount = meshopt_simplify(
            destination,
            indices,
            indexCount,
            vertex_positions,
            vertexCount,
            positionStride,
            targetIndexCount,
            target_error,
            meshopt_SimplifyLockBorder |
                meshopt_SimplifyPermissive,
            &simplifiedError);

        // An empty result is not useful to the managed pipeline. Returning the
        // source keeps this path non-destructive when the error limit is too
        // strict for even one triangle.
        if (simplifiedCount == 0) {
            std::memcpy(
                destination,
                indices,
                indexCount * sizeof(uint32_t));
            simplifiedCount = indexCount;
            simplifiedError = 0.0f;
        }

        if (simplifiedCount > outputCapacity ||
            simplifiedCount % 3 != 0 ||
            !std::isfinite(simplifiedError) ||
            !HasValidIndices(destination, simplifiedCount, vertexCount)) {
            return PO2_MESHOPT_INVALID_OUTPUT;
        }

        *result_index_count = static_cast<uint64_t>(simplifiedCount);
        *result_error = simplifiedError;
        return PO2_MESHOPT_SUCCESS;
    }
    catch (const std::bad_alloc&) {
        return PO2_MESHOPT_ALLOCATION_FAILED;
    }
    catch (...) {
        return PO2_MESHOPT_INTERNAL_ERROR;
    }
}

uint32_t PO2_MESHOPT_CALL po2_meshopt_simplify_aggressive(
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
    float* result_error) {
    if (pruned_index_count != nullptr) {
        *pruned_index_count = 0;
    }
    if (result_index_count != nullptr) {
        *result_index_count = 0;
    }
    if (result_error != nullptr) {
        *result_error = 0.0f;
    }

    if (destination == nullptr ||
        indices == nullptr ||
        vertex_positions == nullptr ||
        pruned_index_count == nullptr ||
        result_index_count == nullptr ||
        result_error == nullptr ||
        index_count == 0 ||
        vertex_count == 0 ||
        index_count % 3 != 0 ||
        target_index_count < 3 ||
        target_index_count > index_count ||
        target_index_count % 3 != 0 ||
        destination_capacity < index_count ||
        !std::isfinite(target_error) ||
        target_error < 0.0f ||
        target_error > 1.0f ||
        !std::isfinite(prune_error) ||
        prune_error < 0.0f ||
        prune_error > 1.0f) {
        return PO2_MESHOPT_INVALID_ARGUMENT;
    }

    size_t outputCapacity = 0;
    size_t indexCount = 0;
    size_t vertexCount = 0;
    size_t positionStride = 0;
    size_t targetIndexCount = 0;
    if (!TryConvertSize(destination_capacity, outputCapacity) ||
        !TryConvertSize(index_count, indexCount) ||
        !TryConvertSize(vertex_count, vertexCount) ||
        !TryConvertSize(vertex_positions_stride, positionStride) ||
        !TryConvertSize(target_index_count, targetIndexCount)) {
        return PO2_MESHOPT_SIZE_OVERFLOW;
    }

    if (vertexCount > std::numeric_limits<uint32_t>::max() ||
        indexCount > std::numeric_limits<size_t>::max() / sizeof(uint32_t) ||
        outputCapacity >
            std::numeric_limits<size_t>::max() / sizeof(uint32_t) ||
        positionStride < sizeof(float) * 3 ||
        positionStride > 256 ||
        positionStride % sizeof(float) != 0 ||
        !CanAddress(vertexCount, positionStride) ||
        !HasValidIndices(indices, indexCount, vertexCount) ||
        !HasFinitePositions(
            vertex_positions,
            vertexCount,
            positionStride)) {
        return PO2_MESHOPT_INVALID_ARGUMENT;
    }

    try {
        std::unique_ptr<uint32_t[]> pruned(new uint32_t[indexCount]);
        size_t prunedCount = meshopt_simplifyPrune(
            pruned.get(),
            indices,
            indexCount,
            vertex_positions,
            vertexCount,
            positionStride,
            prune_error);

        // Do not let a badly scaled or tiny area disappear entirely. The
        // caller can still decide that retaining it is not worthwhile.
        if (prunedCount == 0) {
            std::memcpy(
                pruned.get(),
                indices,
                indexCount * sizeof(uint32_t));
            prunedCount = indexCount;
        }

        size_t simplifiedCount = prunedCount;
        float simplifiedError = prune_error;
        if (prunedCount > targetIndexCount) {
            simplifiedCount = meshopt_simplifySloppy(
                destination,
                pruned.get(),
                prunedCount,
                vertex_positions,
                vertexCount,
                positionStride,
                nullptr,
                targetIndexCount,
                target_error,
                &simplifiedError);
            simplifiedError = std::max(simplifiedError, prune_error);
        }
        else {
            std::memcpy(
                destination,
                pruned.get(),
                prunedCount * sizeof(uint32_t));
        }

        if (simplifiedCount == 0) {
            std::memcpy(
                destination,
                pruned.get(),
                prunedCount * sizeof(uint32_t));
            simplifiedCount = prunedCount;
            simplifiedError = prune_error;
        }

        if (prunedCount > outputCapacity ||
            prunedCount % 3 != 0 ||
            simplifiedCount > outputCapacity ||
            simplifiedCount % 3 != 0 ||
            !std::isfinite(simplifiedError) ||
            !HasValidIndices(
                destination,
                simplifiedCount,
                vertexCount)) {
            return PO2_MESHOPT_INVALID_OUTPUT;
        }

        *pruned_index_count = static_cast<uint64_t>(prunedCount);
        *result_index_count = static_cast<uint64_t>(simplifiedCount);
        *result_error = simplifiedError;
        return PO2_MESHOPT_SUCCESS;
    }
    catch (const std::bad_alloc&) {
        return PO2_MESHOPT_ALLOCATION_FAILED;
    }
    catch (...) {
        return PO2_MESHOPT_INTERNAL_ERROR;
    }
}

uint32_t PO2_MESHOPT_CALL po2_meshopt_optimize_vertex_cache(
    uint32_t* destination,
    uint64_t destination_capacity,
    const uint32_t* indices,
    uint64_t index_count,
    uint64_t vertex_count) {
    if (destination == nullptr ||
        indices == nullptr ||
        index_count == 0 ||
        vertex_count == 0 ||
        index_count % 3 != 0 ||
        destination_capacity < index_count) {
        return PO2_MESHOPT_INVALID_ARGUMENT;
    }

    size_t outputCapacity = 0;
    size_t indexCount = 0;
    size_t vertexCount = 0;
    if (!TryConvertSize(destination_capacity, outputCapacity) ||
        !TryConvertSize(index_count, indexCount) ||
        !TryConvertSize(vertex_count, vertexCount)) {
        return PO2_MESHOPT_SIZE_OVERFLOW;
    }

    if (vertexCount > std::numeric_limits<uint32_t>::max() ||
        indexCount > std::numeric_limits<size_t>::max() / sizeof(uint32_t) ||
        outputCapacity > std::numeric_limits<size_t>::max() / sizeof(uint32_t) ||
        !HasValidIndices(indices, indexCount, vertexCount)) {
        return PO2_MESHOPT_INVALID_ARGUMENT;
    }

    try {
        meshopt_optimizeVertexCache(
            destination,
            indices,
            indexCount,
            vertexCount);

        if (!HasValidIndices(destination, indexCount, vertexCount)) {
            return PO2_MESHOPT_INVALID_OUTPUT;
        }

        return PO2_MESHOPT_SUCCESS;
    }
    catch (const std::bad_alloc&) {
        return PO2_MESHOPT_ALLOCATION_FAILED;
    }
    catch (...) {
        return PO2_MESHOPT_INTERNAL_ERROR;
    }
}
