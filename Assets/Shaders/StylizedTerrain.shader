Shader "SpaceGame/StylizedTerrain"
{
    Properties
    {
        [Header(Palette Height Bands)]
        _ColorValley   ("Valley Low",   Color) = (0.32, 0.10, 0.06, 1.0)
        _ColorMid      ("Mid",          Color) = (0.62, 0.28, 0.14, 1.0)
        _ColorHigh     ("High",         Color) = (0.82, 0.52, 0.30, 1.0)
        _ColorPeak     ("Peak Dust",    Color) = (0.92, 0.78, 0.58, 1.0)
        _ColorCliff    ("Cliff Steep",  Color) = (0.22, 0.10, 0.07, 1.0)

        [Header(Height Bands)]
        _MinHeight     ("Min World Height", Float) = 0
        _MaxHeight     ("Max World Height", Float) = 200
        _BandSoftness  ("Band Softness", Range(0.001, 0.5)) = 0.08
        _BandNoise     ("Band Noise Amount", Range(0, 1)) = 0.35
        _BandNoiseScale("Band Noise Scale", Float) = 18

        [Header(Slope)]
        _SlopeStart    ("Slope Start", Range(0, 1)) = 0.55
        _SlopeEnd      ("Slope End",   Range(0, 1)) = 0.80

        [Header(Lighting)]
        [Toggle(_FLAT_SHADING)] _FlatShading ("Flat Shading", Float) = 1
        _AmbientBoost  ("Ambient Boost", Range(0, 1)) = 0.25
        _LightWrap     ("Light Wrap", Range(0, 1)) = 0.35

        [Header(Atmosphere)]
        _RimColor      ("Rim Color", Color) = (1.0, 0.55, 0.30, 1.0)
        _RimPower      ("Rim Power", Range(0.5, 8)) = 3.0
        _RimStrength   ("Rim Strength", Range(0, 2)) = 0.4
        _FogTint       ("Distance Tint", Color) = (0.85, 0.55, 0.40, 1.0)
        _FogStart      ("Distance Tint Start", Float) = 80
        _FogEnd        ("Distance Tint End",   Float) = 600
        _FogStrength   ("Distance Tint Strength", Range(0, 1)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"       = "Opaque"
            "RenderPipeline"   = "UniversalPipeline"
            "TerrainCompatible"= "True"
            "Queue"            = "Geometry"
        }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma shader_feature_local _FLAT_SHADING

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorValley;
                float4 _ColorMid;
                float4 _ColorHigh;
                float4 _ColorPeak;
                float4 _ColorCliff;
                float  _MinHeight;
                float  _MaxHeight;
                float  _BandSoftness;
                float  _BandNoise;
                float  _BandNoiseScale;
                float  _SlopeStart;
                float  _SlopeEnd;
                float  _AmbientBoost;
                float  _LightWrap;
                float4 _RimColor;
                float  _RimPower;
                float  _RimStrength;
                float4 _FogTint;
                float  _FogStart;
                float  _FogEnd;
                float  _FogStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // 2D value-noise — cheap, smooth, good for jittering band edges.
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // Smooth 3-stop blend used for the height palette.
            float3 blend3(float3 a, float3 b, float3 c, float t, float soft)
            {
                float w0 = 1.0 - smoothstep(0.5 - soft, 0.5 + soft, t);
                float wMidLow  = smoothstep(0.0 - soft, 0.0 + soft, t) * w0;
                float wMidHigh = smoothstep(0.5 - soft, 0.5 + soft, t);
                float3 lowMid = lerp(a, b, smoothstep(0.0, 0.5 + soft, t));
                return lerp(lowMid, c, smoothstep(0.5 - soft, 1.0, t));
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   vni = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = vpi.positionCS;
                OUT.positionWS = vpi.positionWS;
                OUT.normalWS   = vni.normalWS;
                OUT.fogFactor  = ComputeFogFactor(vpi.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

            #if defined(_FLAT_SHADING)
                float3 dpx = ddx(IN.positionWS);
                float3 dpy = ddy(IN.positionWS);
                float3 N   = normalize(cross(dpy, dpx));
            #else
                float3 N = normalize(IN.normalWS);
            #endif

                // ---- Height palette (with noise-jittered band thresholds) ----
                float jitter = (valueNoise(IN.positionWS.xz / max(0.0001, _BandNoiseScale)) - 0.5)
                               * _BandNoise * (_MaxHeight - _MinHeight);
                float h = saturate((IN.positionWS.y + jitter - _MinHeight)
                                   / max(0.0001, (_MaxHeight - _MinHeight)));

                // 4-stop height ramp: valley -> mid -> high -> peak.
                float soft = _BandSoftness;
                float3 cVM = lerp(_ColorValley.rgb, _ColorMid.rgb,
                                  smoothstep(0.20 - soft, 0.20 + soft, h));
                float3 cMH = lerp(cVM,              _ColorHigh.rgb,
                                  smoothstep(0.55 - soft, 0.55 + soft, h));
                float3 cHP = lerp(cMH,              _ColorPeak.rgb,
                                  smoothstep(0.85 - soft, 0.85 + soft, h));
                float3 albedo = cHP;

                // ---- Slope-based cliff overlay ----
                // dot(N, up): 1 = flat ground, 0 = vertical cliff.
                float ndu = saturate(dot(N, float3(0, 1, 0)));
                float cliff = 1.0 - smoothstep(_SlopeStart, _SlopeEnd, ndu);
                albedo = lerp(albedo, _ColorCliff.rgb, cliff);

                // ---- Lighting (URP main light + wrapped diffuse) ----
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float wrap = _LightWrap;
                float NdotL = saturate((dot(N, mainLight.direction) + wrap) / (1.0 + wrap));
                float3 lit  = mainLight.color * NdotL * mainLight.shadowAttenuation;

                // SH ambient (sky-tinted) + small flat boost so shadow side reads color.
                float3 ambient = SampleSH(N) + _AmbientBoost.xxx;

                float3 color = albedo * (lit + ambient);

                // ---- Rim haze (atmosphere fake) ----
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float rim = pow(1.0 - saturate(dot(N, V)), _RimPower) * _RimStrength;
                color += _RimColor.rgb * rim;

                // ---- Optional distance tint (dust haze) ----
                float dist = length(GetWorldSpaceViewDir(IN.positionWS));
                float t = saturate((dist - _FogStart) / max(0.0001, (_FogEnd - _FogStart)));
                color = lerp(color, _FogTint.rgb, t * _FogStrength);

                // URP fog
                color = MixFog(color, IN.fogFactor);

                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // Shadow caster — required for casting shadows onto self/scene.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float4 GetShadowPositionHClip(A IN)
            {
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            V ShadowVert(A IN)
            {
                V OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionCS = GetShadowPositionHClip(IN);
                return OUT;
            }

            half4 ShadowFrag(V IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // Depth-only — required for depth prepass / SSAO / etc.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex   DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            V DepthVert(A IN)
            {
                V OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 DepthFrag(V IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
