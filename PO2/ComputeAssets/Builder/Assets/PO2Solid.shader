Shader "PO2/Solid Mesh Edit" {
    SubShader {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass {
            Name "SolidMarkers"
            Cull Off ZWrite Off ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "PO2SolidTypes.hlsl"
            StructuredBuffer<POSolidResult> _SolidResults;
            StructuredBuffer<uint> _Visible, _Selected, _Winner;
            float4x4 _DrawViewProjection;
            float4 _DrawViewport, _DrawCameraPosition, _CameraRight, _CameraUp, _MarkerRadius;
            float4 _NormalColor, _HoveredColor, _SelectedColor, _SelectedHoveredColor, _TargetColor;
            float _Near, _Far;
            int _Edges, _TargetSlot, _SuppressHover;
            struct Output {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation uint mask : TEXCOORD1;
                nointerpolation uint intervals : TEXCOORD2;
                nointerpolation float4 color : TEXCOORD3;
            };
            Output Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID) {
                static const float2 corners[6] = {
                    float2(0, -1), float2(1, -1), float2(1, 1),
                    float2(0, -1), float2(1, 1), float2(0, 1)
                };
                float2 corner = corners[vertex];
                uint slot = _Visible[instance];
                POSolidResult r = _SolidResults[slot];
                float3 wa = r.worldA.xyz, wb = r.worldB.xyz;
                float lo = 0, hi = 1;
                if (_Edges != 0) {
                    float dz = r.screenB.z - r.screenA.z;
                    if (abs(dz) > 0.000001) {
                        float n = (_Near - r.screenA.z) / dz, f = (_Far - r.screenA.z) / dz;
                        lo = max(0, min(n, f)); hi = min(1, max(n, f));
                    }
                    else if (r.screenA.z < _Near || r.screenA.z > _Far) hi = -1;
                }
                float4 a = mul(_DrawViewProjection, float4(lerp(wa, wb, lo), 1));
                float4 b = mul(_DrawViewProjection, float4(lerp(wa, wb, hi), 1));
                Output o;
                if (_Edges != 0) {
                    float2 delta = (b.xy / max(abs(b.w), 1e-6) - a.xy / max(abs(a.w), 1e-6)) * _DrawViewport.zw;
                    float len = length(delta);
                    float2 side = len > 1e-5 ? float2(-delta.y, delta.x) / len : float2(0, 1);
                    o.position = lerp(a, b, corner.x);
                    o.position.xy += side * corner.y * 1.5 / _DrawViewport.zw * o.position.w;
                    o.uv = float2(lerp(lo, hi, corner.x), corner.y);
                }
                else {
                    float2 uv = float2(corner.x * 2 - 1, corner.y);
                    float radius = max(0.001, _MarkerRadius.z != 0 ? _MarkerRadius.y :
                        max(0.01, length(r.worldA.xyz - _DrawCameraPosition.xyz)) * _MarkerRadius.x);
                    float3 world = r.worldA.xyz + (uv.x * _CameraRight.xyz + uv.y * _CameraUp.xyz) * radius;
                    o.position = mul(_DrawViewProjection, float4(world, 1));
                    o.uv = uv;
                }
                o.mask = hi >= lo ? r.mask : 0; o.intervals = r.intervalCount;
                bool selected = (_Selected[slot >> 5] & (1u << (slot & 31))) != 0;
                bool hovered = _SuppressHover == 0 && _Winner[0] == slot + 1;
                o.color = hovered ? (selected ? _SelectedHoveredColor : _HoveredColor) : (selected ? _SelectedColor : _NormalColor);
                if (hovered && (int)slot == _TargetSlot) o.color = _TargetColor;
                return o;
            }
            float4 Frag(Output input) : SV_Target {
                if (input.mask == 0 || input.intervals == 0) discard;
                if (_Edges != 0) {
                    uint bit = min((uint)(saturate(input.uv.x) * input.intervals), input.intervals - 1);
                    if ((input.mask & (1u << bit)) == 0) discard;
                }
                else {
                    // sixteen-sided ring and its inner radius
                    float angle = atan2(input.uv.y, input.uv.x);
                    float sector = 6.28318530718 / 16;
                    float radius = length(input.uv) * cos(fmod(angle + 6.28318530718, sector) - sector * 0.5) / cos(sector * 0.5);
                    if (radius > 1 || radius < 0.68) discard;
                }
                return input.color;
            }
            ENDHLSL
        }
    }
}
