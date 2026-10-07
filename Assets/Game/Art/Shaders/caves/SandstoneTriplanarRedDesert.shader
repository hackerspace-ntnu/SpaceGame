Shader "SpaceGame/SandstoneTriplanarRedDesert"
{
    Properties
    {
        [Header(Triplanar Rock Colors  Rusty iron oxide red rock)]
        _ColorX ("Color X (Side walls)", Color) = (0.55, 0.24, 0.15, 1)
        _ColorY ("Color Y (Floor / ceiling)", Color) = (0.62, 0.30, 0.18, 1)
        _ColorZ ("Color Z (Front / back walls)", Color) = (0.50, 0.21, 0.13, 1)
        _BlendSharpness ("Triplanar Blend Sharpness", Range(1, 16)) = 4

        [Header(Sand Blobs  Big stylized patches of lighter sandy colors)]
        _BlobScale ("Blob Scale (smaller = bigger blobs)", Range(0.02, 1)) = 0.12
        _BlobThreshold ("Blob Threshold (lower = more blobs)", Range(0, 1)) = 0.5
        _BlobEdgeSoftness ("Blob Edge Softness (lower = more stylized hard edges)", Range(0.001, 0.3)) = 0.015
        _BlobStrength ("Blob Color Strength", Range(0, 1)) = 1.0

        [Header(Sand Blob Palette  3 lighter terracotta tones)]
        _SandColor1 ("Sand Color 1 (Pale Clay)", Color) = (0.86, 0.58, 0.42, 1)
        _SandColor2 ("Sand Color 2 (Terracotta)", Color) = (0.78, 0.45, 0.30, 1)
        _SandColor3 ("Sand Color 3 (Soft Adobe)", Color) = (0.82, 0.52, 0.36, 1)
        _SandMixScale ("Sand Color Mix Scale (smaller = bigger color zones)", Range(0.05, 5)) = 0.6

        [Header(Accent Layer  Sedimentary streaks plus large accent blobs)]
        _AccentColor ("Accent Color", Color) = (0.36, 0.13, 0.11, 1)
        _StreakScale ("Streak Scale (smaller = wider bands)", Range(0.005, 1.5)) = 0.15
        _StreakWarp ("Streak Horizontal Warp (jagged sedimentary feel)", Range(0, 1)) = 0.35
        _StreakWarpScale ("Streak Warp Frequency", Range(0.01, 1)) = 0.08
        _StreakThreshold ("Streak Threshold (lower = more streaks)", Range(0, 1)) = 0.55
        _StreakEdgeSoftness ("Streak Edge Softness", Range(0.001, 0.3)) = 0.05
        _StreakStrength ("Streak Color Strength", Range(0, 1)) = 0.8
        _AccentBlobScale ("Accent Blob Scale (smaller = bigger blobs)", Range(0.01, 0.5)) = 0.07
        _AccentBlobThreshold ("Accent Blob Threshold (lower = more)", Range(0, 1)) = 0.62
        _AccentBlobEdgeSoftness ("Accent Blob Edge Softness", Range(0.001, 0.3)) = 0.05
        _AccentBlobStrength ("Accent Blob Color Strength", Range(0, 1)) = 0.85

        [Header(Inner Detail  Small secondary blobs inside big blobs)]
        _InnerBlobScale ("Inner Blob Scale", Range(0.1, 4)) = 0.8
        _InnerBlobStrength ("Inner Blob Strength", Range(0, 1)) = 0.35

        [Header(Surface)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0
        _Metallic ("Metallic", Range(0, 1)) = 0

        [Header(Procedural Normal Detail)]
        _BumpStrength ("Bump Strength (0 = off)", Range(0, 2)) = 0.6
        _BumpScale ("Bump Scale (smaller = bigger lumps)", Range(0.1, 5)) = 1.0
        _BumpDetailMix ("Bump High-Freq Detail Mix", Range(0, 1)) = 0.85

        [Header(Lighting)]
        _AmbientBoost ("Ambient Boost (cave fill light)", Range(0, 1)) = 0.08
        _LightWrap    ("Light Wrap", Range(0, 1)) = 0.0
        _SkyAmbientStrength ("Sky/SH Ambient Strength (0 = pitch black w/o lights)", Range(0, 1)) = 0.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorX, _ColorY, _ColorZ;
                float  _BlendSharpness;
                float  _BlobScale, _BlobThreshold, _BlobEdgeSoftness, _BlobStrength;
                float4 _SandColor1, _SandColor2, _SandColor3;
                float  _SandMixScale;
                float4 _AccentColor;
                float  _StreakScale, _StreakWarp, _StreakWarpScale;
                float  _StreakThreshold, _StreakEdgeSoftness, _StreakStrength;
                float  _AccentBlobScale, _AccentBlobThreshold, _AccentBlobEdgeSoftness, _AccentBlobStrength;
                float  _InnerBlobScale, _InnerBlobStrength;
                float  _Smoothness, _Metallic;
                float  _BumpStrength, _BumpScale, _BumpDetailMix;
                float  _AmbientBoost, _LightWrap, _SkyAmbientStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float fogFactor : TEXCOORD2; };

            float hash13(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float vnoise(float3 p)
            {
                float3 i = floor(p), f = frac(p), u = f*f*(3.0-2.0*f);
                float n000 = hash13(i+float3(0,0,0)), n100 = hash13(i+float3(1,0,0));
                float n010 = hash13(i+float3(0,1,0)), n110 = hash13(i+float3(1,1,0));
                float n001 = hash13(i+float3(0,0,1)), n101 = hash13(i+float3(1,0,1));
                float n011 = hash13(i+float3(0,1,1)), n111 = hash13(i+float3(1,1,1));
                return lerp(lerp(lerp(n000,n100,u.x), lerp(n010,n110,u.x), u.y),
                            lerp(lerp(n001,n101,u.x), lerp(n011,n111,u.x), u.y), u.z);
            }
            float fbm(float3 p)
            {
                float v = 0.0, a = 0.5;
                for (int i = 0; i < 4; i++) { v += a * vnoise(p); p *= 2.03; a *= 0.5; }
                return v;
            }

            // Procedural normal-perturbation — reuses fbm above so bumps match the sand patches'
            // visual personality. Applied at the top of frag().
            #define CAVE_BUMP_FBM(p) fbm(p)
            #include "CaveBump.hlsl"

            // Pick a sand color from the 3-color palette using two noise fields as coords.
            float3 sandTapestry(float3 worldP)
            {
                float n1 = vnoise(worldP * _SandMixScale + 0.3);
                float n2 = vnoise(worldP * _SandMixScale + 7.7);
                // Three-way blend: n1 picks between 1<->2, n2 mixes in 3
                float3 c12 = lerp(_SandColor1.rgb, _SandColor2.rgb, smoothstep(0.2, 0.8, n1));
                return lerp(c12, _SandColor3.rgb, smoothstep(0.3, 0.7, n2));
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
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
                float3 N = normalize(IN.normalWS);
                // Procedural micro-bump derived from the same fbm field that drives the patches —
                // makes the sandstone look textured under direct light without needing textures.
                N = ApplyCaveBump(IN.positionWS, N, _BumpStrength, _BumpScale, _BumpDetailMix);

                // Triplanar light-sand rock base
                float3 absN = pow(abs(N), _BlendSharpness);
                float3 w    = absN / (absN.x + absN.y + absN.z + 1e-5);
                float3 baseColor = _ColorX.rgb * w.x + _ColorY.rgb * w.y + _ColorZ.rgb * w.z;

                // ---- Big stylized sand blobs ----
                // Low-frequency FBM defines large organic regions.
                // Narrow edge softness gives crisp stylized blob borders.
                float blobNoise = fbm(IN.positionWS * _BlobScale);
                float blobMass  = smoothstep(_BlobThreshold,
                                             _BlobThreshold + _BlobEdgeSoftness,
                                             blobNoise);

                // Sand color tapestry inside blobs
                float3 sandColor = sandTapestry(IN.positionWS);

                // Inner detail: smaller secondary blobs inside the big ones brighten/darken
                // slightly to add stylized internal variation without breaking the flat look.
                float innerNoise = fbm(IN.positionWS * _BlobScale * _InnerBlobScale + 13.7);
                float innerShift = (innerNoise - 0.5) * 2.0 * _InnerBlobStrength;
                sandColor = saturate(sandColor + innerShift * 0.15);

                // Final albedo: light base, replaced by sand color where blob mass is high
                float3 albedo = lerp(baseColor, sandColor, blobMass * _BlobStrength);

                // ---- Accent layer: sedimentary streaks ----
                // Stylized horizontal sediment banding. World-Y drives the band coordinate so streaks are
                // mostly horizontal (sedimentary feel). XZ-driven FBM warp gives them jagged, irregular
                // borders rather than perfectly flat lines.
                float warp = (fbm(IN.positionWS.xzx * _StreakWarpScale) - 0.5) * 2.0 * _StreakWarp;
                float streakCoord = IN.positionWS.y * _StreakScale + warp;
                // Use vnoise on the 1D streak coordinate (lifted to 3D) so bands are smooth low-frequency.
                float streakNoise = vnoise(float3(streakCoord, streakCoord * 0.31, streakCoord * 1.7));
                float streakMass = smoothstep(_StreakThreshold,
                                              _StreakThreshold + _StreakEdgeSoftness,
                                              streakNoise);

                // ---- Accent layer: large accent blobs ----
                // Big low-frequency blobs of the accent color, crisp anti-aliased borders.
                float accentBlobNoise = fbm(IN.positionWS * _AccentBlobScale + 41.7);
                float accentBlobMass  = smoothstep(_AccentBlobThreshold,
                                                   _AccentBlobThreshold + _AccentBlobEdgeSoftness,
                                                   accentBlobNoise);

                // Combine streak + accent blob masks; both push toward the accent color.
                float accentMass = saturate(max(streakMass * _StreakStrength,
                                                accentBlobMass * _AccentBlobStrength));
                albedo = lerp(albedo, _AccentColor.rgb, accentMass);

                // ---- Lighting ----
                // Caves should be near pitch-black without a light source.
                float wrap = _LightWrap;

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float mainNdotL = saturate((dot(N, mainLight.direction) + wrap) / (1.0 + wrap));
                float3 lit = mainLight.color * mainNdotL * mainLight.shadowAttenuation;

            #ifdef _ADDITIONAL_LIGHTS
                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = N;
                inputData.viewDirectionWS = normalize(GetWorldSpaceViewDir(IN.positionWS));
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                uint addCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(addCount)
                    Light addLight = GetAdditionalLight(lightIndex, IN.positionWS);
                    float addNdotL = saturate((dot(N, addLight.direction) + wrap) / (1.0 + wrap));
                    lit += addLight.color * addNdotL * addLight.distanceAttenuation * addLight.shadowAttenuation;
                LIGHT_LOOP_END
            #endif

                float3 ambient = SampleSH(N) * _SkyAmbientStrength + _AmbientBoost.xxx;

                float3 color = albedo * (lit + ambient);
                color = MixFog(color, IN.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct AttribS { float4 pos : POSITION; float3 nrm : NORMAL; };
            struct VaryS  { float4 pos : SV_POSITION; };

            float4 GetShadowPositionHClip(AttribS IN)
            {
                float3 wp = TransformObjectToWorld(IN.pos.xyz);
                float3 wn = TransformObjectToWorldNormal(IN.nrm);
                float4 clip = TransformWorldToHClip(ApplyShadowBias(wp, wn, _LightDirection));
                #if UNITY_REVERSED_Z
                    clip.z = min(clip.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    clip.z = max(clip.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return clip;
            }

            VaryS vertShadow(AttribS IN) { VaryS OUT; OUT.pos = GetShadowPositionHClip(IN); return OUT; }
            half4 fragShadow(VaryS IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
