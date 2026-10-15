// The witch unseen: what is left of her while she prowls the clearing between attacks — a faint
// shimmer at her silhouette, broken up and drifting, that a player can catch out of the corner of an
// eye if they are looking (SpriteHamletQuest.md, "Witch").
//
// Drawn on her own skinned meshes in place of their materials (ShimmerCloak swaps them), so the
// shimmer has her exact shape and moves with her animation. Reads POSITION and NORMAL only.
//
// WHAT IT DRAWS. Additive, so she never darkens what is behind her and so a pulse can only ever
// brighten:
//
//   • A fresnel rim, brightest at the silhouette, broken by drifting world-space noise into
//     patches — the shimmer. Scaled by _Visibility, which ShimmerCloak keeps low.
//   • _Pulse (0→1), the moment she is shown: the rim swells, the noise is filled in and a body
//     glow comes up, so for an instant she reads as a bright outline of herself.
//
// Colours are authored saturated and made subtle by coverage, never by dropping chroma — see
// StasisShell.shader's header for why PastelQuantize needs that.
Shader "SpaceGame/Effects/WitchShimmer"
{
    Properties
    {
        [HDR] _Color      ("Colour",             Color)          = (0.55, 1.4, 0.6, 1)
        _Visibility       ("Visibility",         Range(0, 1))    = 0.25

        [Header(Rim)]
        _RimPower         ("Rim Power",          Range(0.5, 8))  = 2.5
        _RimStrength      ("Rim Strength",       Range(0, 3))    = 1.0

        [Header(Shimmer)]
        _NoiseScale       ("Noise Scale (per m)",Range(0.2, 12)) = 3.5
        _NoiseSpeed       ("Noise Drift",        Range(0, 4))    = 1.2
        _Coverage         ("Coverage",           Range(0, 1))    = 0.45

        [Header(Pulse)]
        _Pulse            ("Pulse",              Range(0, 1))    = 0
        _PulseBody        ("Pulse Body Glow",    Range(0, 1))    = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "WitchShimmer"

            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half  _Visibility;
                half  _RimPower;
                half  _RimStrength;
                half  _NoiseScale;
                half  _NoiseSpeed;
                half  _Coverage;
                half  _Pulse;
                half  _PulseBody;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            // Integer-lattice hash: stable across drivers, unlike frac(sin()).
            float Hash(float3 p)
            {
                uint3 q = (uint3)(int3)floor(p) * uint3(1597334673u, 3812015801u, 2798796415u);
                uint n = (q.x ^ q.y ^ q.z) * 1597334673u;
                return n * (1.0 / 4294967295.0);
            }

            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);

                float n000 = Hash(i);
                float n100 = Hash(i + float3(1, 0, 0));
                float n010 = Hash(i + float3(0, 1, 0));
                float n110 = Hash(i + float3(1, 1, 0));
                float n001 = Hash(i + float3(0, 0, 1));
                float n101 = Hash(i + float3(1, 0, 1));
                float n011 = Hash(i + float3(0, 1, 1));
                float n111 = Hash(i + float3(1, 1, 1));

                return lerp(lerp(lerp(n000, n100, u.x), lerp(n010, n110, u.x), u.y),
                            lerp(lerp(n001, n101, u.x), lerp(n011, n111, u.x), u.y), u.z);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 view = normalize(GetWorldSpaceViewDir(input.positionWS));
                float rim = pow(1.0 - saturate(dot(normal, view)), _RimPower) * _RimStrength;

                // Patches of shimmer drifting up her body, like heat over a road.
                float3 p = input.positionWS * _NoiseScale + float3(0, -_Time.y * _NoiseSpeed, 0);
                float noise = ValueNoise(p) * 0.65 + ValueNoise(p * 2.3) * 0.35;
                float patches = saturate((noise - (1.0 - _Coverage)) * 4.0);

                float pulse = saturate(_Pulse);
                float shimmer = rim * lerp(patches, 1.0, pulse) * lerp(_Visibility, 1.0, pulse);
                float body = _PulseBody * pulse;

                return half4(_Color.rgb * (shimmer + body), 1.0);
            }
            ENDHLSL
        }
    }
}
