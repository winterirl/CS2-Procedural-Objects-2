#include "PO2SolidTypes.hlsl"

StructuredBuffer<uint> _GeometryFault;
StructuredBuffer<POSolidCandidate> _SolidCandidates;
StructuredBuffer<POSolidNormal> _SolidNormals;
StructuredBuffer<POSolidNode> _SolidNodes;
StructuredBuffer<POSolidTriangle> _SolidTriangles;
RWStructuredBuffer<POSolidResult> _SolidResults;

float4x4 _SolidLocalToWorld, _SolidWorldToLocal, _SolidNormalToWorld;
float4x4 _SolidView, _SolidProjection;
float4 _SolidCameraPosition, _SolidCameraForward, _SolidViewport;

int _SolidOrthographic, _SolidCount, _SolidNodeCount, _SolidTriangleCount, _SolidSerial;

float3 POSolidNormalize(float3 v) {
    float lengthV = length(v);
    return lengthV > 0.00001 ? v / lengthV : 0;
}

float4 POSolidProject(float3 world) {
    float4 view = mul(_SolidView, float4(world, 1));
    float4 clip = mul(_SolidProjection, view);
    float inverseW = abs(clip.w) > 0.000001 ? 1.0 / clip.w : 0;
    float2 pixel = _SolidViewport.xy + (clip.xy * inverseW * 0.5 + 0.5) * _SolidViewport.zw;
    return float4(pixel, -view.z, clip.w);
}

bool POSolidFacing(POSolidCandidate candidate, float3 world, out float bestDot) {
    bestDot = -2;
    if (candidate.alwaysFacing != 0) return true;
    float3 view = -_SolidCameraForward.xyz;

    if (_SolidOrthographic == 0) {
        view = _SolidCameraPosition.xyz - world;
        if (dot(view, view) <= 0.00000001) return true;
        view /= length(view);
    }

    for (uint i = 0; i < candidate.normalCount; i++) {
        POSolidNormal input = _SolidNormals[candidate.firstNormal + i];
        float3 normal = input.a.xyz;
        bool face = input.a.w >= 3;

        if (face) {
            normal = cross(input.b.xyz - input.a.xyz, input.c.xyz - input.a.xyz);
            if (input.a.w == 4) normal += cross(input.c.xyz - input.a.xyz, input.d.xyz - input.a.xyz);
            if (dot(normal, normal) <= 0.00000001) return true;
        }

        normal = POSolidNormalize(mul((float3x3)_SolidNormalToWorld, normal));

        if (dot(normal, normal) <= 0.00000001) {
            if (face) return true;
            continue;
        }

        float facingDot = dot(normal, view);
        bestDot = max(bestDot, facingDot);

        if (facingDot >= -0.0001) return true;
    }
    return false;
}

bool POSolidAxis(float origin, float direction, float minimum, float maximum, inout float nearT, inout float farT) {
    if (abs(direction) <= 0.0000001) return origin >= minimum && origin <= maximum;
    float a = (minimum - origin) / direction;
    float b = (maximum - origin) / direction;
    nearT = max(nearT, min(a, b)); farT = min(farT, max(a, b));
    return farT >= nearT;
}

bool POSolidBounds(float3 origin, float3 direction, float minimumT, float maximumT, POSolidNode node) {
    return POSolidAxis(origin.x, direction.x, node.minimum.x, node.maximum.x, minimumT, maximumT) &&
        POSolidAxis(origin.y, direction.y, node.minimum.y, node.maximum.y, minimumT, maximumT) &&
        POSolidAxis(origin.z, direction.z, node.minimum.z, node.maximum.z, minimumT, maximumT);
}

bool POSolidTriangleHit(float3 origin, float3 direction, float minimumT, float maximumT, POSolidTriangle tri) {
    float3 edge1 = tri.b.xyz - tri.a.xyz;
    float3 edge2 = tri.c.xyz - tri.a.xyz;
    float3 p = cross(direction, edge2);
    float determinant = dot(edge1, p);

    if (abs(determinant) <= 0.0000001) return false;

    float inverse = 1.0 / determinant;
    float3 t = origin - tri.a.xyz;
    float u = dot(t, p) * inverse;

    if (u < 0 || u > 1) return false;

    float3 q = cross(t, edge1);
    float v = dot(direction, q) * inverse;

    if (v < 0 || u + v > 1) return false;

    float distanceT = dot(edge2, q) * inverse;

    return distanceT > minimumT && distanceT < maximumT;
}

bool POSolidVisible(float3 target, inout POSolidResult result) {
    float3 origin = mul(_SolidWorldToLocal, float4(_SolidCameraPosition.xyz, 1)).xyz;
    float3 direction;
    float targetDistance;

    if (_SolidOrthographic != 0) {
        direction = mul((float3x3)_SolidWorldToLocal, _SolidCameraForward.xyz);
        direction = dot(direction, direction) > 0.00000001 ? normalize(direction) : float3(0, 0, 1);
        targetDistance = 10;
        if (_SolidNodeCount > 0) targetDistance = max(10.0, length(_SolidNodes[0].maximum.xyz - _SolidNodes[0].minimum.xyz) * 2 + 10);
        origin = target - direction * targetDistance;
    }
    else {
        direction = target - origin;
        targetDistance = length(direction);
        if (targetDistance <= 0.000001) return true;
        direction /= targetDistance;
    }

    float epsilon = max(0.001, targetDistance * 0.00001);
    float maximumT = targetDistance - epsilon;

    if (maximumT <= epsilon || _SolidNodeCount == 0 || _SolidTriangleCount == 0) return true;

    int nodeIndex = 0;
    uint visits = 0;

    while (nodeIndex < _SolidNodeCount) {
        if (nodeIndex < 0 || visits++ >= (uint)_SolidNodeCount) { result.fault |= 1; return false; }

        POSolidNode node = _SolidNodes[nodeIndex];
        result.nodeVisits++;

        if (node.escape <= nodeIndex || node.escape > _SolidNodeCount) { result.fault |= 2; return false; }

        if (!POSolidBounds(origin, direction, epsilon, maximumT, node)) { nodeIndex = node.escape; continue; }

        if (node.triangleCount > 0) {
            if (node.firstTriangle < 0 || node.firstTriangle + node.triangleCount > _SolidTriangleCount) { result.fault |= 4; return false; }
            for (int i = 0; i < node.triangleCount; i++) {
                result.triangleTests++;
                if (POSolidTriangleHit(origin, direction, epsilon, maximumT, _SolidTriangles[node.firstTriangle + i])) return false;
            }
            nodeIndex = node.escape;
        }
        else {
            if (node.child <= nodeIndex || node.child >= node.escape) { result.fault |= 8; return false; }
            nodeIndex = node.child;
        }
    }
    return true;
}


AppendStructuredBuffer<uint> _Visible;
StructuredBuffer<uint> _Selected;
int _SolidEdges, _SolidPart, _SolidGroups;
float _SolidNear, _SolidFar;
float4 _Pointer, _Rectangle;
RWStructuredBuffer<float2> _PickValues;
RWStructuredBuffer<float4> _PickGroups;
RWStructuredBuffer<float4> _PickMinimum;
RWStructuredBuffer<uint> _Winner;
RWStructuredBuffer<uint> _Reply;
int _RequestSerial;

// All interaction endpoints come from the same local t as the visible wire.
bool SolidSegment(POSolidResult r, float t0, float t1, out float3 a, out float3 b) {
    a = lerp(r.worldA.xyz, r.worldB.xyz, t0);
    b = lerp(r.worldA.xyz, r.worldB.xyz, t1);
    float za = lerp(r.screenA.z, r.screenB.z, t0), zb = lerp(r.screenA.z, r.screenB.z, t1);
    float lo = 0, hi = 1, delta = zb - za;

    if (abs(delta) < 0.000001) {
        if (za < _SolidNear || za > _SolidFar) return false;
    }
    else {
        float n = (_SolidNear - za) / delta, f = (_SolidFar - za) / delta;
        lo = max(lo, min(n, f)); hi = min(hi, max(n, f));
        if (lo > hi) return false;
    }

    float3 start = a, end = b;
    a = POSolidProject(lerp(start, end, lo)).xyz;
    b = POSolidProject(lerp(start, end, hi)).xyz;

    if (!all(isfinite(a)) || !all(isfinite(b))) return false;

    // Clip screen queries to the same viewport that clips the rasterized wire.
    float screenLo = 0, screenHi = 1;
    float2 d = b.xy - a.xy;

    if (!POSolidAxis(a.x, d.x, _SolidViewport.x, _SolidViewport.x + _SolidViewport.z, screenLo, screenHi) ||
        !POSolidAxis(a.y, d.y, _SolidViewport.y, _SolidViewport.y + _SolidViewport.w, screenLo, screenHi)) return false;

    float3 pa = a, pb = b;
    a.xy = lerp(pa.xy, pb.xy, screenLo); b.xy = lerp(pa.xy, pb.xy, screenHi);
    a.z = _SolidOrthographic != 0 ? lerp(pa.z, pb.z, screenLo) : 1 / lerp(1 / pa.z, 1 / pb.z, screenLo);
    b.z = _SolidOrthographic != 0 ? lerp(pa.z, pb.z, screenHi) : 1 / lerp(1 / pa.z, 1 / pb.z, screenHi);

    return true;
}

float2 SolidDistance(POSolidResult r) {
    float2 best = float2(1e30, 1e30);

    if (r.mask == 0 || r.fault != 0) return best;

    if (_SolidEdges == 0) {
        float d = dot(r.screenA.xy - _Pointer.xy, r.screenA.xy - _Pointer.xy);
        if (d <= 100) best = float2(d, r.screenA.z);
        return best;
    }

    for (uint i = 0; i < r.intervalCount; i++) {
        if ((r.mask & (1u << i)) == 0) continue;
        float3 a, b;
        if (!SolidSegment(r, (float)i / r.intervalCount, (float)(i + 1) / r.intervalCount, a, b)) continue;
        float2 delta = b.xy - a.xy;
        float t = saturate(dot(_Pointer.xy - a.xy, delta) / max(dot(delta, delta), 1e-12));
        float2 offset = a.xy + delta * t - _Pointer.xy;
        float distance = dot(offset, offset);
        float depth = _SolidOrthographic != 0 ? lerp(a.z, b.z, t) : 1 / lerp(1 / a.z, 1 / b.z, t);
        if (distance <= 144 && (distance < best.x || (distance == best.x && depth < best.y))) best = float2(distance, depth);
    }

    return best;
}

bool SolidRectSegment(float2 a, float2 b) {
    float lo = 0, hi = 1;
    float2 d = b - a;
    return POSolidAxis(a.x, d.x, _Rectangle.x, _Rectangle.z, lo, hi) &&
        POSolidAxis(a.y, d.y, _Rectangle.y, _Rectangle.w, lo, hi);
}

bool SolidInBox(POSolidResult r) {
    if (r.mask == 0 || r.fault != 0) return false;
    if (_SolidEdges == 0) return all(r.screenA.xy >= _Rectangle.xy) && all(r.screenA.xy < _Rectangle.zw);
    for (uint i = 0; i < r.intervalCount; i++) {
        if ((r.mask & (1u << i)) == 0) continue;
        float3 a, b;
        if (SolidSegment(r, (float)i / r.intervalCount, (float)(i + 1) / r.intervalCount, a, b) &&
            SolidRectSegment(a.xy, b.xy)) return true;
    }
    return false;
}

// Each entry specializes this function with a literal policy. ClassifyXRay
// has no facing/triangle/node resources and emits one full edge interval.
void ClassifyElement(uint index, bool xray) {
    if (index >= (uint)_SolidCount) return;

    POSolidCandidate c = _SolidCandidates[index];
    POSolidResult r = (POSolidResult)0;
    r.fault = _GeometryFault[0];

    if (r.fault != 0) { _SolidResults[index] = r; return; }

    r.worldA = mul(_SolidLocalToWorld, float4(c.localA.xyz, 1));
    r.worldB = mul(_SolidLocalToWorld, float4(c.localB.xyz, 1));
    r.screenA = POSolidProject(r.worldA.xyz); r.screenB = POSolidProject(r.worldB.xyz);
    bool scope = _SolidPart < 0 || c.part == (uint)_SolidPart;
    float bestDot;

    if (xray) r.facing = scope ? 1 : 0;
    else r.facing = scope && POSolidFacing(c, _SolidEdges != 0 ? (r.worldA.xyz + r.worldB.xyz) * 0.5 : r.worldA.xyz, bestDot) ? 1 : 0;

    r.intervalCount = 1;

    if (_SolidEdges != 0) {
        float len = length(r.screenB.xy - r.screenA.xy);
        r.intervalCount = r.screenA.z <= 0 || r.screenB.z <= 0 || !isfinite(len) ? 0 :
            (xray ? 1 : (uint)clamp(ceil(max(1.0, len) / 24.0), 1.0, 32.0));
    }
    else if (r.screenA.z < _SolidNear || r.screenA.z > _SolidFar || !all(isfinite(r.screenA)) ||
        any(r.screenA.xy < _SolidViewport.xy) || any(r.screenA.xy > _SolidViewport.xy + _SolidViewport.zw)) r.facing = 0;

    if (r.facing != 0 && xray) r.mask = r.intervalCount != 0 ? 1 : 0;
    else if (r.facing != 0) {
        for (uint i = 0; i < r.intervalCount; i++) {
            float t = _SolidEdges != 0 ? (i + 0.5) / r.intervalCount : 0;
            if (POSolidVisible(lerp(c.localA.xyz, c.localB.xyz, t), r)) r.mask |= 1u << i;
        }
    }

    _SolidResults[index] = r;

    if (r.mask != 0 && r.fault == 0) _Visible.Append(index);
}

[numthreads(64, 1, 1)]
void Classify(uint3 id : SV_DispatchThreadID) { ClassifyElement(id.x, false); }

[numthreads(64, 1, 1)]
void ClassifyXRay(uint3 id : SV_DispatchThreadID) { ClassifyElement(id.x, true); }

groupshared float distances[64];
groupshared uint faults[64];

[numthreads(64, 1, 1)]
void PickDistances(uint3 id : SV_DispatchThreadID, uint lane : SV_GroupIndex, uint3 group : SV_GroupID) {
    float2 value = id.x < (uint)_SolidCount ? SolidDistance(_SolidResults[id.x]) : float2(1e30, 1e30);

    if (id.x < (uint)_SolidCount) _PickValues[id.x] = value;

    distances[lane] = value.x;
    faults[lane] = id.x < (uint)_SolidCount ? _SolidResults[id.x].fault : 0;
    GroupMemoryBarrierWithGroupSync();

    for (uint stride = 32; stride > 0; stride >>= 1) {
        if (lane < stride) {
            distances[lane] = min(distances[lane], distances[lane + stride]);
            faults[lane] |= faults[lane + stride];
        }
        GroupMemoryBarrierWithGroupSync();
    }
    if (lane == 0) _PickGroups[group.x] = float4(distances[0], 0, asfloat(faults[0]), 0);
}

[numthreads(1, 1, 1)]
void PickMinimum(uint3 id : SV_DispatchThreadID) {
    float minimum = 1e30;
    for (int i = 0; i < _SolidGroups; i++) minimum = min(minimum, _PickGroups[i].x);
    _PickMinimum[0] = float4(minimum, 0, 0, 0);
}

[numthreads(1, 1, 1)]
void PickWinners(uint3 group : SV_GroupID) {
    float limit = _PickMinimum[0].x + 0.0001;
    float depth = 1e30; uint winner = 0;
    for (uint i = group.x * 64; i < min(group.x * 64 + 64, (uint)_SolidCount); i++) {
        float2 value = _PickValues[i];
        if (value.x < 1e29 && value.x <= limit && value.y < depth) { depth = value.y; winner = i + 1; }
    }
    _PickGroups[group.x] = float4(depth, asfloat(winner), _PickGroups[group.x].z, 0);
}

[numthreads(1, 1, 1)]
void PickResolve(uint3 id : SV_DispatchThreadID) {
    float depth = 1e30; uint winner = 0, fault = 0;
    for (int i = 0; i < _SolidGroups; i++) {
        float4 value = _PickGroups[i];
        fault |= asuint(value.z);
        if (value.x < depth) { depth = value.x; winner = asuint(value.y); }
    }
    _Winner[0] = winner;
    _Reply[0] = (uint)_RequestSerial; _Reply[1] = winner; _Reply[2] = fault; _Reply[3] = 3;
}

[numthreads(64, 1, 1)]
void BoxQuery(uint3 id : SV_DispatchThreadID) {
    uint first = id.x * 32;
    if (first >= (uint)_SolidCount) return;
    uint bits = 0;
    for (uint i = 0; i < 32 && first + i < (uint)_SolidCount; i++) {
        if (SolidInBox(_SolidResults[first + i])) bits |= 1u << i;
    }
    _Reply[4 + id.x] = bits;
    if (id.x == 0) { _Reply[0] = (uint)_RequestSerial; _Reply[1] = (uint)_SolidCount; _Reply[3] = 3; }
}

// ~Source updates.
// Integer references stay separate from float data.
struct POSourcePatch { uint index; float3 value; };
struct POFacingRefs { int4 vertices; int4 kind; };
StructuredBuffer<POSourcePatch> _SourcePatches;
RWStructuredBuffer<float3> _SourceDestination;
StructuredBuffer<float3> _SourcePositions, _SourceNormals;
StructuredBuffer<int4> _CandidateSources, _TriangleSources;
StructuredBuffer<POFacingRefs> _FacingSources;
StructuredBuffer<uint> _UpdateWork;
RWStructuredBuffer<uint> _UpdateFault;
RWStructuredBuffer<POSolidCandidate> _UpdateCandidates;
RWStructuredBuffer<POSolidNormal> _UpdateFacing;
RWStructuredBuffer<POSolidTriangle> _UpdateTriangles;
StructuredBuffer<POSolidTriangle> _RefitTriangles;
RWStructuredBuffer<POSolidNode> _UpdateNodes;
int _SourceCount, _SourcePatchCount, _UpdateCount, _UpdateOffset, _UpdateLimit, _UpdateDense, _RefitTriangleCount;

bool POSourceIndex(int index) { return index >= 0 && index < _SourceCount; }
bool POUpdateIndex(uint thread, out uint index) {
    index = 0;
    if (thread >= (uint)_UpdateCount) return false;
    index = _UpdateDense != 0 ? thread : _UpdateWork[_UpdateOffset + thread];
    if (index >= (uint)_UpdateLimit) { InterlockedOr(_UpdateFault[0], 16u); return false; }
    return true;
}

[numthreads(64, 1, 1)]
void ApplySourcePatches(uint3 id : SV_DispatchThreadID) {
    if (id.x >= (uint)_SourcePatchCount) return;
    POSourcePatch p = _SourcePatches[id.x];
    if (p.index >= (uint)_SourceCount || !all(isfinite(p.value))) { InterlockedOr(_UpdateFault[0], 16u); return; }
    _SourceDestination[p.index] = p.value;
}

[numthreads(64, 1, 1)]
void UpdateCandidates(uint3 id : SV_DispatchThreadID) {
    uint index; if (!POUpdateIndex(id.x, index)) return;
    int4 s = _CandidateSources[index];
    if (!POSourceIndex(s.x) || !POSourceIndex(s.y)) { InterlockedOr(_UpdateFault[0], 16u); return; }
    float3 a = _SourcePositions[s.x], b = _SourcePositions[s.y];
    if (!all(isfinite(a)) || !all(isfinite(b))) { InterlockedOr(_UpdateFault[0], 16u); return; }
    POSolidCandidate c = _UpdateCandidates[index];
    c.localA = float4(a, 0); c.localB = float4(b, 0); _UpdateCandidates[index] = c;
}

[numthreads(64, 1, 1)]
void UpdateFacing(uint3 id : SV_DispatchThreadID) {
    uint index; if (!POUpdateIndex(id.x, index)) return;
    POFacingRefs s = _FacingSources[index]; int4 v = s.vertices;
    if (!POSourceIndex(v.x) || (s.kind.x != 0 &&
        (!POSourceIndex(v.y) || !POSourceIndex(v.z) || !POSourceIndex(v.w) || (s.kind.x != 3 && s.kind.x != 4)))) {
        InterlockedOr(_UpdateFault[0], 16u); return;
    }
    POSolidNormal n = (POSolidNormal)0;
    if (s.kind.x == 0) n.a = float4(_SourceNormals[v.x], 0);
    else {
        n.a = float4(_SourcePositions[v.x], s.kind.x); n.b = float4(_SourcePositions[v.y], 0);
        n.c = float4(_SourcePositions[v.z], 0); n.d = float4(_SourcePositions[v.w], 0);
    }
    if (!all(isfinite(n.a)) || !all(isfinite(n.b)) || !all(isfinite(n.c)) || !all(isfinite(n.d))) {
        InterlockedOr(_UpdateFault[0], 16u); return;
    }
    _UpdateFacing[index] = n;
}

[numthreads(64, 1, 1)]
void UpdateTriangles(uint3 id : SV_DispatchThreadID) {
    uint index; if (!POUpdateIndex(id.x, index)) return;
    int4 s = _TriangleSources[index];
    if (!POSourceIndex(s.x) || !POSourceIndex(s.y) || !POSourceIndex(s.z)) { InterlockedOr(_UpdateFault[0], 16u); return; }
    POSolidTriangle t;
    t.a = float4(_SourcePositions[s.x], 0); t.b = float4(_SourcePositions[s.y], 0); t.c = float4(_SourcePositions[s.z], 0);
    if (!all(isfinite(t.a)) || !all(isfinite(t.b)) || !all(isfinite(t.c))) { InterlockedOr(_UpdateFault[0], 16u); return; }
    _UpdateTriangles[index] = t;
}

[numthreads(64, 1, 1)]
void RefitNodes(uint3 id : SV_DispatchThreadID) {
    uint index; if (!POUpdateIndex(id.x, index)) return;
    POSolidNode n = _UpdateNodes[index];
    float3 lo = 3.402823466e+38F, hi = -3.402823466e+38F;
    if (n.triangleCount > 0) {
        if (n.firstTriangle < 0 || n.triangleCount > _RefitTriangleCount || n.firstTriangle > _RefitTriangleCount - n.triangleCount) {
            InterlockedOr(_UpdateFault[0], 16u); return;
        }
        for (int i = 0; i < n.triangleCount; i++) {
            POSolidTriangle t = _RefitTriangles[n.firstTriangle + i];
            lo = min(lo, min(t.a.xyz, min(t.b.xyz, t.c.xyz))); hi = max(hi, max(t.a.xyz, max(t.b.xyz, t.c.xyz)));
        }
    }
    else {
        if (n.child <= (int)index || n.child >= _UpdateLimit) { InterlockedOr(_UpdateFault[0], 16u); return; }
        POSolidNode left = _UpdateNodes[n.child];
        int rightIndex = left.escape;
        if (rightIndex <= n.child || rightIndex >= _UpdateLimit) { InterlockedOr(_UpdateFault[0], 16u); return; }
        POSolidNode right = _UpdateNodes[rightIndex];
        lo = min(left.minimum.xyz, right.minimum.xyz); hi = max(left.maximum.xyz, right.maximum.xyz);
    }
    if (!all(isfinite(lo)) || !all(isfinite(hi)) || any(lo > hi)) { InterlockedOr(_UpdateFault[0], 16u); return; }
    n.minimum = float4(lo, 0); n.maximum = float4(hi, 0); _UpdateNodes[index] = n;
}
