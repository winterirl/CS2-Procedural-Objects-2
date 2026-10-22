#ifndef PO2_SOLID_TYPES
#define PO2_SOLID_TYPES
// Explicit lanes match POSolidGpuData.cs 
// Results remain on the GPU.
struct POSolidCandidate {
    float4 localA, localB;
    uint firstNormal, normalCount, alwaysFacing, part;
};
struct POSolidNormal { float4 a, b, c, d; };
struct POSolidNode {
    float4 minimum, maximum;
    int firstTriangle, triangleCount, child, escape;
};
struct POSolidTriangle { float4 a, b, c; };
struct POSolidResult {
    float4 worldA, worldB, screenA, screenB;
    uint intervalCount, mask, facing, fault;
    uint nodeVisits, triangleTests, serial, abi;
};
#endif
