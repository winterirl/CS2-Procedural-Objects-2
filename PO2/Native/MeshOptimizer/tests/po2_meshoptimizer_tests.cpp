#include "po2_meshoptimizer.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <iostream>
#include <iterator>
#include <limits>
#include <string>
#include <vector>

namespace {

constexpr uint32_t kSimplifyLockBorder = 1u << 0;
constexpr uint32_t kSimplifyPermissive = 1u << 5;

struct TestContext {
    int failures = 0;

    void Check(bool condition, const std::string& name) {
        if (condition) {
            return;
        }

        ++failures;
        std::cerr << "FAIL: " << name << '\n';
    }
};

struct TestMesh {
    std::vector<float> positions;
    std::vector<float> attributes;
    std::vector<uint32_t> indices;
};

TestMesh BuildGrid(uint32_t side) {
    TestMesh mesh;
    uint32_t row = side + 1;
    mesh.positions.reserve(static_cast<size_t>(row) * row * 3);
    mesh.attributes.reserve(static_cast<size_t>(row) * row * 3);
    mesh.indices.reserve(static_cast<size_t>(side) * side * 6);

    for (uint32_t z = 0; z <= side; ++z) {
        for (uint32_t x = 0; x <= side; ++x) {
            float height = 0.05f * std::sin(static_cast<float>(x) * 0.4f) *
                std::cos(static_cast<float>(z) * 0.35f);
            mesh.positions.push_back(static_cast<float>(x));
            mesh.positions.push_back(height);
            mesh.positions.push_back(static_cast<float>(z));

            mesh.attributes.push_back(0.0f);
            mesh.attributes.push_back(1.0f);
            mesh.attributes.push_back(0.0f);
        }
    }

    for (uint32_t z = 0; z < side; ++z) {
        for (uint32_t x = 0; x < side; ++x) {
            uint32_t bottomLeft = z * row + x;
            uint32_t bottomRight = bottomLeft + 1;
            uint32_t topLeft = bottomLeft + row;
            uint32_t topRight = topLeft + 1;

            mesh.indices.push_back(bottomLeft);
            mesh.indices.push_back(topLeft);
            mesh.indices.push_back(bottomRight);
            mesh.indices.push_back(bottomRight);
            mesh.indices.push_back(topLeft);
            mesh.indices.push_back(topRight);
        }
    }

    return mesh;
}

TestMesh BuildRendererStreamGrid(uint32_t side) {
    TestMesh mesh;
    uint32_t row = side + 1;
    mesh.positions.reserve(static_cast<size_t>(row) * row * 3);
    mesh.attributes.reserve(static_cast<size_t>(row) * row * 19);
    mesh.indices.reserve(static_cast<size_t>(side) * side * 6);

    for (uint32_t z = 0; z <= side; ++z) {
        for (uint32_t x = 0; x <= side; ++x) {
            float u = static_cast<float>(x) / static_cast<float>(side);
            float v = static_cast<float>(z) / static_cast<float>(side);
            mesh.positions.push_back(static_cast<float>(x) * 2.0f);
            mesh.positions.push_back(0.0f);
            mesh.positions.push_back(static_cast<float>(z) * 0.5f);

            const float attributes[] = {
                0.0f, 1.0f, 0.0f,
                1.0f, 0.0f, 0.0f, 1.0f,
                u, v, u + v, 1.0f,
                127.0f / 255.0f,
                191.0f / 255.0f,
                223.0f / 255.0f,
                1.0f,
                u, v, u + v, 1.0f,
            };
            mesh.attributes.insert(
                mesh.attributes.end(),
                std::begin(attributes),
                std::end(attributes));
        }
    }

    for (uint32_t z = 0; z < side; ++z) {
        for (uint32_t x = 0; x < side; ++x) {
            uint32_t bottomLeft = z * row + x;
            uint32_t bottomRight = bottomLeft + 1;
            uint32_t topLeft = bottomLeft + row;
            uint32_t topRight = topLeft + 1;

            mesh.indices.push_back(bottomLeft);
            mesh.indices.push_back(topLeft);
            mesh.indices.push_back(bottomRight);
            mesh.indices.push_back(bottomRight);
            mesh.indices.push_back(topLeft);
            mesh.indices.push_back(topRight);
        }
    }

    return mesh;
}

TestMesh BuildSplitRendererStreamGrid(uint32_t side) {
    TestMesh shared = BuildRendererStreamGrid(side);
    TestMesh split;
    split.positions.reserve(shared.indices.size() * 3);
    split.attributes.reserve(shared.indices.size() * 19);
    split.indices.reserve(shared.indices.size());

    for (uint32_t sourceIndex : shared.indices) {
        size_t positionOffset = static_cast<size_t>(sourceIndex) * 3;
        split.positions.insert(
            split.positions.end(),
            shared.positions.begin() + positionOffset,
            shared.positions.begin() + positionOffset + 3);

        size_t attributeOffset = static_cast<size_t>(sourceIndex) * 19;
        split.attributes.insert(
            split.attributes.end(),
            shared.attributes.begin() + attributeOffset,
            shared.attributes.begin() + attributeOffset + 19);

        split.indices.push_back(
            static_cast<uint32_t>(split.indices.size()));
    }

    return split;
}

uint32_t SimplifyGrid(
    const TestMesh& mesh,
    std::vector<uint32_t>& destination,
    uint64_t& resultCount,
    float& resultError) {
    const float weights[] = { 1.0f, 1.0f, 1.0f };
    uint64_t targetCount = (mesh.indices.size() / 2 / 3) * 3;
    return po2_meshopt_simplify(
        destination.data(),
        destination.size(),
        mesh.indices.data(),
        mesh.indices.size(),
        mesh.positions.data(),
        mesh.positions.size() / 3,
        sizeof(float) * 3,
        mesh.attributes.data(),
        sizeof(float) * 3,
        weights,
        3,
        nullptr,
        targetCount,
        1.0f,
        kSimplifyLockBorder,
        &resultCount,
        &resultError);
}

void TestVersions(TestContext& context) {
    context.Check(
        po2_meshopt_get_abi_version() == PO2_MESHOPT_ABI_VERSION,
        "Bridge ABI version");
    context.Check(
        po2_meshopt_get_meshoptimizer_version() ==
            PO2_MESHOPT_MESHOPTIMIZER_VERSION,
        "Pinned meshoptimizer version");
}

void TestSimplification(TestContext& context) {
    TestMesh mesh = BuildGrid(24);
    std::vector<uint32_t> first(mesh.indices.size());
    std::vector<uint32_t> second(mesh.indices.size());
    uint64_t firstCount = 0;
    uint64_t secondCount = 0;
    float firstError = 0.0f;
    float secondError = 0.0f;

    uint32_t firstStatus =
        SimplifyGrid(mesh, first, firstCount, firstError);
    uint32_t secondStatus =
        SimplifyGrid(mesh, second, secondCount, secondError);

    context.Check(firstStatus == PO2_MESHOPT_SUCCESS, "Grid simplify status");
    context.Check(secondStatus == PO2_MESHOPT_SUCCESS, "Repeated simplify status");
    context.Check(firstCount > 0, "Grid simplify produced triangles");
    context.Check(firstCount < mesh.indices.size(), "Grid simplify reduced triangles");
    context.Check(firstCount % 3 == 0, "Grid simplify triangle alignment");
    context.Check(std::isfinite(firstError), "Grid simplify finite error");
    context.Check(firstCount == secondCount, "Grid simplify deterministic count");
    context.Check(firstError == secondError, "Grid simplify deterministic error");
    context.Check(
        std::equal(first.begin(), first.begin() + firstCount, second.begin()),
        "Grid simplify deterministic indices");

    size_t vertexCount = mesh.positions.size() / 3;
    bool validIndices = true;
    for (size_t i = 0; i < firstCount; ++i) {
        validIndices &= first[i] < vertexCount;
    }
    context.Check(validIndices, "Grid simplify index range");
}

void TestPositionFocusedSimplification(TestContext& context) {
    TestMesh mesh = BuildRendererStreamGrid(40);
    std::vector<uint32_t> first(mesh.indices.size());
    std::vector<uint32_t> second(mesh.indices.size());
    uint64_t firstCount = 0;
    uint64_t secondCount = 0;
    float firstError = 0.0f;
    float secondError = 0.0f;
    uint64_t targetCount = mesh.indices.size() * 2 / 5 / 3 * 3;

    uint32_t firstStatus = po2_meshopt_simplify_position_focused(
        first.data(),
        first.size(),
        mesh.indices.data(),
        mesh.indices.size(),
        mesh.positions.data(),
        mesh.positions.size() / 3,
        sizeof(float) * 3,
        targetCount,
        0.005f,
        &firstCount,
        &firstError);
    uint32_t secondStatus = po2_meshopt_simplify_position_focused(
        second.data(),
        second.size(),
        mesh.indices.data(),
        mesh.indices.size(),
        mesh.positions.data(),
        mesh.positions.size() / 3,
        sizeof(float) * 3,
        targetCount,
        0.005f,
        &secondCount,
        &secondError);

    context.Check(
        firstStatus == PO2_MESHOPT_SUCCESS &&
            secondStatus == PO2_MESHOPT_SUCCESS,
        "Position-focused simplify status");
    context.Check(
        firstCount > 0 && firstCount < mesh.indices.size(),
        "Position-focused simplify reduction");
    context.Check(
        firstCount % 3 == 0 && std::isfinite(firstError),
        "Position-focused simplify output validity");
    context.Check(
        firstCount == secondCount && firstError == secondError,
        "Position-focused simplify deterministic measurements");
    context.Check(
        std::equal(first.begin(), first.begin() + firstCount, second.begin()),
        "Position-focused simplify deterministic indices");
}

std::vector<std::array<uint32_t, 3>> TriangleSet(
    const std::vector<uint32_t>& indices) {
    std::vector<std::array<uint32_t, 3>> triangles;
    triangles.reserve(indices.size() / 3);
    for (size_t i = 0; i < indices.size(); i += 3) {
        std::array<uint32_t, 3> triangle = {
            indices[i],
            indices[i + 1],
            indices[i + 2],
        };
        std::sort(triangle.begin(), triangle.end());
        triangles.push_back(triangle);
    }
    std::sort(triangles.begin(), triangles.end());
    return triangles;
}

void TestVertexCacheOptimization(TestContext& context) {
    TestMesh mesh = BuildGrid(12);
    std::vector<uint32_t> optimized(mesh.indices.size());
    uint32_t status = po2_meshopt_optimize_vertex_cache(
        optimized.data(),
        optimized.size(),
        mesh.indices.data(),
        mesh.indices.size(),
        mesh.positions.size() / 3);

    context.Check(status == PO2_MESHOPT_SUCCESS, "Cache optimization status");
    context.Check(
        TriangleSet(mesh.indices) == TriangleSet(optimized),
        "Cache optimization preserves triangles");
}

void TestRendererStreamSequence(TestContext& context) {
    TestMesh mesh = BuildRendererStreamGrid(20);
    std::vector<uint32_t> lod1(mesh.indices.size());
    std::vector<uint32_t> lod2(mesh.indices.size());
    float uvWeight = std::sqrt(800.0f);
    const float weights[] = {
        1.0f, 1.0f, 1.0f,
        1.0f, 1.0f, 1.0f, 1.0f,
        uvWeight, uvWeight, uvWeight, uvWeight,
        1.0f, 1.0f, 1.0f, 1.0f,
        1.0f, 1.0f, 1.0f, 1.0f,
    };
    uint64_t lod1Count = 0;
    uint64_t lod2Count = 0;
    float lod1Error = 0.0f;
    float lod2Error = 0.0f;

    uint32_t lod1Status = po2_meshopt_simplify(
        lod1.data(),
        lod1.size(),
        mesh.indices.data(),
        mesh.indices.size(),
        mesh.positions.data(),
        mesh.positions.size() / 3,
        sizeof(float) * 3,
        mesh.attributes.data(),
        sizeof(float) * 19,
        weights,
        19,
        nullptr,
        mesh.indices.size() / 2 / 3 * 3,
        0.005f,
        kSimplifyLockBorder,
        &lod1Count,
        &lod1Error);
    context.Check(
        lod1Status == PO2_MESHOPT_SUCCESS,
        "Renderer-stream LOD1 status");
    context.Check(
        lod1Count > 0 && lod1Count < mesh.indices.size(),
        "Renderer-stream LOD1 reduction");

    uint32_t cacheStatus = po2_meshopt_optimize_vertex_cache(
        lod1.data(),
        lod1.size(),
        lod1.data(),
        lod1Count,
        mesh.positions.size() / 3);
    context.Check(
        cacheStatus == PO2_MESHOPT_SUCCESS,
        "Renderer-stream in-place cache optimization");

    uint32_t lod2Status = po2_meshopt_simplify(
        lod2.data(),
        lod2.size(),
        lod1.data(),
        lod1Count,
        mesh.positions.data(),
        mesh.positions.size() / 3,
        sizeof(float) * 3,
        mesh.attributes.data(),
        sizeof(float) * 19,
        weights,
        19,
        nullptr,
        lod1Count / 2 / 3 * 3,
        0.05f,
        kSimplifyLockBorder,
        &lod2Count,
        &lod2Error);
    context.Check(
        lod2Status == PO2_MESHOPT_SUCCESS,
        "Renderer-stream LOD2 status");
    context.Check(
        lod2Count > 0 && lod2Count < lod1Count,
        "Renderer-stream sequential LOD2 reduction");
    context.Check(
        std::isfinite(lod1Error) && std::isfinite(lod2Error),
        "Renderer-stream finite errors");
}

bool UsesPosition(
    const TestMesh& mesh,
    const std::vector<uint32_t>& indices,
    uint64_t indexCount,
    float x,
    float z) {
    for (uint64_t i = 0; i < indexCount; ++i) {
        size_t positionOffset = static_cast<size_t>(indices[i]) * 3;
        if (mesh.positions[positionOffset] == x &&
            mesh.positions[positionOffset + 2] == z) {
            return true;
        }
    }

    return false;
}

void TestSplitVertexSimplification(TestContext& context) {
    constexpr uint32_t side = 20;
    TestMesh mesh = BuildSplitRendererStreamGrid(side);
    std::vector<uint32_t> strictDestination(mesh.indices.size());
    std::vector<uint32_t> destination(mesh.indices.size());
    float uvWeight = std::sqrt(800.0f);
    const float weights[] = {
        1.0f, 1.0f, 1.0f,
        1.0f, 1.0f, 1.0f, 1.0f,
        uvWeight, uvWeight, uvWeight, uvWeight,
        1.0f, 1.0f, 1.0f, 1.0f,
        1.0f, 1.0f, 1.0f, 1.0f,
    };
    uint64_t strictCount = 0;
    float strictError = 0.0f;
    uint32_t strictStatus = po2_meshopt_simplify(
        strictDestination.data(),
        strictDestination.size(),
        mesh.indices.data(),
        mesh.indices.size(),
        mesh.positions.data(),
        mesh.positions.size() / 3,
        sizeof(float) * 3,
        mesh.attributes.data(),
        sizeof(float) * 19,
        weights,
        19,
        nullptr,
        mesh.indices.size() / 2 / 3 * 3,
        0.005f,
        kSimplifyLockBorder,
        &strictCount,
        &strictError);

    uint64_t resultCount = 0;
    float resultError = 0.0f;

    uint32_t status = po2_meshopt_simplify(
        destination.data(),
        destination.size(),
        mesh.indices.data(),
        mesh.indices.size(),
        mesh.positions.data(),
        mesh.positions.size() / 3,
        sizeof(float) * 3,
        mesh.attributes.data(),
        sizeof(float) * 19,
        weights,
        19,
        nullptr,
        mesh.indices.size() / 2 / 3 * 3,
        0.005f,
        kSimplifyLockBorder | kSimplifyPermissive,
        &resultCount,
        &resultError);

    context.Check(
        strictStatus == PO2_MESHOPT_SUCCESS,
        "Strict split-vertex simplify status");
    context.Check(
        status == PO2_MESHOPT_SUCCESS,
        "Split-vertex simplify status");
    context.Check(
        resultCount > 0 && resultCount < mesh.indices.size(),
        "Split-vertex mesh reduction");
    context.Check(
        resultCount < strictCount,
        "Permissive seam handling improves split-vertex reduction");
    context.Check(
        resultCount % 3 == 0,
        "Split-vertex triangle alignment");
    context.Check(
        std::isfinite(resultError),
        "Split-vertex finite error");

    std::vector<uint32_t> uvDestination(mesh.indices.size());
    const float uvWeights[] = { 0.01f, 0.01f, 0.01f, 0.01f };
    uint64_t uvCount = 0;
    float uvError = 0.0f;
    uint32_t uvStatus = po2_meshopt_simplify(
        uvDestination.data(),
        uvDestination.size(),
        mesh.indices.data(),
        mesh.indices.size(),
        mesh.positions.data(),
        mesh.positions.size() / 3,
        sizeof(float) * 3,
        mesh.attributes.data() + 7,
        sizeof(float) * 19,
        uvWeights,
        4,
        nullptr,
        mesh.indices.size() / 2 / 3 * 3,
        0.005f,
        kSimplifyLockBorder | kSimplifyPermissive,
        &uvCount,
        &uvError);
    context.Check(
        uvStatus == PO2_MESHOPT_SUCCESS,
        "UV0-focused split-vertex simplify status");
    context.Check(
        uvCount > 0 && uvCount < mesh.indices.size(),
        "UV0-focused split-vertex reduction");
    context.Check(
        uvCount % 3 == 0 && std::isfinite(uvError),
        "UV0-focused split-vertex output validity");

    bool boundaryPreserved = true;
    for (uint32_t i = 0; i <= side; ++i) {
        float x = static_cast<float>(i) * 2.0f;
        float z = static_cast<float>(i) * 0.5f;
        boundaryPreserved &= UsesPosition(
            mesh,
            destination,
            resultCount,
            x,
            0.0f);
        boundaryPreserved &= UsesPosition(
            mesh,
            destination,
            resultCount,
            x,
            static_cast<float>(side) * 0.5f);
        boundaryPreserved &= UsesPosition(
            mesh,
            destination,
            resultCount,
            0.0f,
            z);
        boundaryPreserved &= UsesPosition(
            mesh,
            destination,
            resultCount,
            static_cast<float>(side) * 2.0f,
            z);
    }
    context.Check(
        boundaryPreserved,
        "Split-vertex locked border preservation");

    std::vector<uint32_t> positionDestination(mesh.indices.size());
    uint64_t positionCount = 0;
    float positionError = 0.0f;
    uint32_t positionStatus = po2_meshopt_simplify_position_focused(
        positionDestination.data(),
        positionDestination.size(),
        mesh.indices.data(),
        mesh.indices.size(),
        mesh.positions.data(),
        mesh.positions.size() / 3,
        sizeof(float) * 3,
        mesh.indices.size() / 2 / 3 * 3,
        0.005f,
        &positionCount,
        &positionError);
    context.Check(
        positionStatus == PO2_MESHOPT_SUCCESS,
        "Position-focused split-vertex simplify status");
    context.Check(
        positionCount > 0 && positionCount < mesh.indices.size(),
        "Position-focused split-vertex reduction");
    context.Check(
        positionCount % 3 == 0 && std::isfinite(positionError),
        "Position-focused split-vertex output validity");
}

void TestAggressiveSimplification(TestContext& context) {
    TestMesh mesh = BuildGrid(64);
    std::vector<uint32_t> first(mesh.indices.size());
    std::vector<uint32_t> second(mesh.indices.size());
    uint64_t firstPrunedCount = 0;
    uint64_t firstResultCount = 0;
    uint64_t secondPrunedCount = 0;
    uint64_t secondResultCount = 0;
    float firstError = 0.0f;
    float secondError = 0.0f;
    uint64_t targetCount = 120 * 3;

    uint32_t firstStatus = po2_meshopt_simplify_aggressive(
        first.data(),
        first.size(),
        mesh.indices.data(),
        mesh.indices.size(),
        mesh.positions.data(),
        mesh.positions.size() / 3,
        sizeof(float) * 3,
        targetCount,
        0.3f,
        0.001f,
        &firstPrunedCount,
        &firstResultCount,
        &firstError);
    uint32_t secondStatus = po2_meshopt_simplify_aggressive(
        second.data(),
        second.size(),
        mesh.indices.data(),
        mesh.indices.size(),
        mesh.positions.data(),
        mesh.positions.size() / 3,
        sizeof(float) * 3,
        targetCount,
        0.3f,
        0.001f,
        &secondPrunedCount,
        &secondResultCount,
        &secondError);

    context.Check(
        firstStatus == PO2_MESHOPT_SUCCESS &&
            secondStatus == PO2_MESHOPT_SUCCESS,
        "Aggressive simplify status");
    context.Check(
        firstPrunedCount == mesh.indices.size(),
        "Aggressive simplify retained the connected source");
    context.Check(
        firstResultCount > 0 && firstResultCount <= targetCount,
        "Aggressive simplify reached its deep target");
    context.Check(
        firstError >= 0.001f,
        "Aggressive simplify retained the pruning error bound");
    context.Check(
        firstResultCount == secondResultCount &&
            firstPrunedCount == secondPrunedCount &&
            firstError == secondError,
        "Aggressive simplify deterministic counts");
    context.Check(
        std::equal(
            first.begin(),
            first.begin() + firstResultCount,
            second.begin()),
        "Aggressive simplify deterministic indices");
}

void TestAggressivePruning(TestContext& context) {
    TestMesh mesh = BuildGrid(8);
    uint32_t tinyStart =
        static_cast<uint32_t>(mesh.positions.size() / 3);
    const float tinyPositions[] = {
        20.0f, 0.0f, 0.0f,
        20.001f, 0.0f, 0.0f,
        20.0f, 0.001f, 0.0f,
    };
    mesh.positions.insert(
        mesh.positions.end(),
        std::begin(tinyPositions),
        std::end(tinyPositions));
    mesh.indices.push_back(tinyStart);
    mesh.indices.push_back(tinyStart + 1);
    mesh.indices.push_back(tinyStart + 2);

    std::vector<uint32_t> destination(mesh.indices.size());
    uint64_t prunedCount = 0;
    uint64_t resultCount = 0;
    float resultError = 0.0f;
    uint32_t status = po2_meshopt_simplify_aggressive(
        destination.data(),
        destination.size(),
        mesh.indices.data(),
        mesh.indices.size(),
        mesh.positions.data(),
        mesh.positions.size() / 3,
        sizeof(float) * 3,
        mesh.indices.size(),
        0.3f,
        0.01f,
        &prunedCount,
        &resultCount,
        &resultError);

    context.Check(
        status == PO2_MESHOPT_SUCCESS,
        "Aggressive pruning status");
    context.Check(
        prunedCount + 3 == mesh.indices.size(),
        "Aggressive pruning removed the tiny component");
    context.Check(
        resultCount == prunedCount,
        "Aggressive pruning preserved its under-budget result");
    context.Check(
        resultError == 0.01f,
        "Aggressive pruning reported its error bound");
}

void TestInvalidInputs(TestContext& context) {
    TestMesh mesh = BuildGrid(4);
    std::vector<uint32_t> destination(mesh.indices.size());
    const float weights[] = { 1.0f, 1.0f, 1.0f };
    uint64_t resultCount = 99;
    float resultError = 99.0f;

    auto simplify = [&](
        const uint32_t* indices,
        uint64_t indexCount,
        const float* positions,
        uint64_t outputCapacity,
        const float* suppliedWeights,
        uint32_t attributeCount,
        uint64_t targetCount,
        uint32_t options) {
        resultCount = 99;
        resultError = 99.0f;
        return po2_meshopt_simplify(
            destination.data(),
            outputCapacity,
            indices,
            indexCount,
            positions,
            mesh.positions.size() / 3,
            sizeof(float) * 3,
            attributeCount == 0 ? nullptr : mesh.attributes.data(),
            attributeCount == 0 ? 0 : sizeof(float) * 3,
            suppliedWeights,
            attributeCount,
            nullptr,
            targetCount,
            0.5f,
            options,
            &resultCount,
            &resultError);
    };

    uint32_t status = simplify(
        mesh.indices.data(),
        mesh.indices.size(),
        mesh.positions.data(),
        mesh.indices.size() - 1,
        weights,
        3,
        mesh.indices.size() / 2 / 3 * 3,
        0);
    context.Check(status == PO2_MESHOPT_INVALID_ARGUMENT, "Reject short output");
    context.Check(resultCount == 0 && resultError == 0.0f, "Clear results on failure");

    status = simplify(
        mesh.indices.data(),
        mesh.indices.size() - 1,
        mesh.positions.data(),
        mesh.indices.size(),
        weights,
        3,
        mesh.indices.size() / 2 / 3 * 3,
        0);
    context.Check(status == PO2_MESHOPT_INVALID_ARGUMENT, "Reject partial triangle");

    std::vector<uint32_t> invalidIndices = mesh.indices;
    invalidIndices[0] = static_cast<uint32_t>(mesh.positions.size() / 3);
    status = simplify(
        invalidIndices.data(),
        invalidIndices.size(),
        mesh.positions.data(),
        destination.size(),
        weights,
        3,
        mesh.indices.size() / 2 / 3 * 3,
        0);
    context.Check(status == PO2_MESHOPT_INVALID_ARGUMENT, "Reject invalid index");

    std::vector<float> invalidPositions = mesh.positions;
    invalidPositions[0] = std::numeric_limits<float>::quiet_NaN();
    status = simplify(
        mesh.indices.data(),
        mesh.indices.size(),
        invalidPositions.data(),
        destination.size(),
        weights,
        3,
        mesh.indices.size() / 2 / 3 * 3,
        0);
    context.Check(status == PO2_MESHOPT_INVALID_ARGUMENT, "Reject NaN position");

    const float invalidWeights[] = { 1.0f, -1.0f, 1.0f };
    status = simplify(
        mesh.indices.data(),
        mesh.indices.size(),
        mesh.positions.data(),
        destination.size(),
        invalidWeights,
        3,
        mesh.indices.size() / 2 / 3 * 3,
        0);
    context.Check(status == PO2_MESHOPT_INVALID_ARGUMENT, "Reject negative weight");

    status = simplify(
        mesh.indices.data(),
        mesh.indices.size(),
        mesh.positions.data(),
        destination.size(),
        weights,
        33,
        mesh.indices.size() / 2 / 3 * 3,
        0);
    context.Check(status == PO2_MESHOPT_INVALID_ARGUMENT, "Reject excess attributes");

    status = simplify(
        mesh.indices.data(),
        mesh.indices.size(),
        mesh.positions.data(),
        destination.size(),
        weights,
        3,
        mesh.indices.size() / 2 / 3 * 3 + 1,
        0);
    context.Check(status == PO2_MESHOPT_INVALID_ARGUMENT, "Reject partial target triangle");

    status = simplify(
        mesh.indices.data(),
        mesh.indices.size(),
        mesh.positions.data(),
        destination.size(),
        weights,
        3,
        mesh.indices.size() / 2 / 3 * 3,
        1u << 31);
    context.Check(status == PO2_MESHOPT_INVALID_ARGUMENT, "Reject private options");

    std::vector<uint8_t> invalidLocks(mesh.positions.size() / 3, 0);
    invalidLocks[0] = 1u << 7;
    resultCount = 99;
    resultError = 99.0f;
    status = po2_meshopt_simplify(
        destination.data(),
        destination.size(),
        mesh.indices.data(),
        mesh.indices.size(),
        mesh.positions.data(),
        mesh.positions.size() / 3,
        sizeof(float) * 3,
        mesh.attributes.data(),
        sizeof(float) * 3,
        weights,
        3,
        invalidLocks.data(),
        mesh.indices.size() / 2 / 3 * 3,
        0.5f,
        0,
        &resultCount,
        &resultError);
    context.Check(status == PO2_MESHOPT_INVALID_ARGUMENT, "Reject private lock flags");

    status = po2_meshopt_optimize_vertex_cache(
        destination.data(),
        destination.size() - 1,
        mesh.indices.data(),
        mesh.indices.size(),
        mesh.positions.size() / 3);
    context.Check(status == PO2_MESHOPT_INVALID_ARGUMENT, "Reject short cache output");

    uint64_t prunedCount = 99;
    status = po2_meshopt_simplify_aggressive(
        destination.data(),
        destination.size(),
        mesh.indices.data(),
        mesh.indices.size(),
        mesh.positions.data(),
        mesh.positions.size() / 3,
        sizeof(float) * 3,
        0,
        0.3f,
        0.01f,
        &prunedCount,
        &resultCount,
        &resultError);
    context.Check(
        status == PO2_MESHOPT_INVALID_ARGUMENT,
        "Reject empty aggressive target");
    context.Check(
        prunedCount == 0 && resultCount == 0 && resultError == 0.0f,
        "Clear aggressive results on failure");

    resultCount = 99;
    resultError = 99.0f;
    status = po2_meshopt_simplify_position_focused(
        destination.data(),
        destination.size(),
        mesh.indices.data(),
        mesh.indices.size(),
        mesh.positions.data(),
        mesh.positions.size() / 3,
        sizeof(float) * 3,
        0,
        0.005f,
        &resultCount,
        &resultError);
    context.Check(
        status == PO2_MESHOPT_INVALID_ARGUMENT,
        "Reject empty position-focused target");
    context.Check(
        resultCount == 0 && resultError == 0.0f,
        "Clear position-focused results on failure");
}

void TestDegenerateInput(TestContext& context) {
    const float positions[] = {
        0.0f, 0.0f, 0.0f,
        0.0f, 0.0f, 0.0f,
        0.0f, 0.0f, 0.0f,
    };
    const uint32_t indices[] = { 0, 1, 2 };
    uint32_t destination[3] = {};
    uint64_t resultCount = 0;
    float resultError = 0.0f;

    uint32_t status = po2_meshopt_simplify(
        destination,
        3,
        indices,
        3,
        positions,
        3,
        sizeof(float) * 3,
        nullptr,
        0,
        nullptr,
        0,
        nullptr,
        0,
        1.0f,
        0,
        &resultCount,
        &resultError);

    context.Check(status == PO2_MESHOPT_SUCCESS, "Degenerate mesh does not fail bridge");
    context.Check(resultCount % 3 == 0, "Degenerate result triangle alignment");
}

}

int main() {
    TestContext context;
    TestVersions(context);
    TestSimplification(context);
    TestPositionFocusedSimplification(context);
    TestVertexCacheOptimization(context);
    TestRendererStreamSequence(context);
    TestSplitVertexSimplification(context);
    TestAggressiveSimplification(context);
    TestAggressivePruning(context);
    TestInvalidInputs(context);
    TestDegenerateInput(context);

    if (context.failures != 0) {
        std::cerr << context.failures << " native bridge test(s) failed.\n";
        return 1;
    }

    std::cout << "PASS: PO2 meshoptimizer native bridge checks.\n";
    return 0;
}
