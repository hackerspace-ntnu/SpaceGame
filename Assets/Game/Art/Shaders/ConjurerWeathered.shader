// Weathered metal whose colour comes from the mesh, not from this material.
//
// The Lightning Conjurer's body carries a per-vertex colour attribute baked by
// _Source~/rustify.py: a continuous position along a khaki -> grey -> verdigris
// ramp, evaluated from a domain-warped noise field in world space. The GPU
// interpolates it across each triangle, and that interpolation is where the
// gradient comes from. Three earlier attempts assigned palette MATERIALS per
// object and then per face, and both are discrete by construction -- a face gets
// one material, so every transition is a hard step at a polygon edge.
//
// So this shader exists to do two things a stock URP/Lit cannot:
//
//   1. Use vertex colour as base colour. URP/Lit ignores it entirely.
//   2. Add detail FINER than the mesh. Vertex colours are bounded by vertex
//      density -- about 0.09 m here -- and the speckle in the reference photos is
//      finer than that. The grunge below is generated in the fragment shader from
//      world position, so it has no such limit and needs no UVs, which matters
//      because none of these 68 parts are unwrapped.
//
// Vertex ALPHA carries how weathered the point is (0 dry, 1 corroded), and drives
// roughness and metallic together: corrosion is an oxide, so the more weathered a
// spot is the rougher and less conductive it gets. That coupling is most of why
// the surface reads as material rather than as paint.
//
// Triplanar, not UV: the grunge is projected from world space on all three axes
// and blended by the world normal. Cost is three noise samples on the parts that
// face a corner, one on anything flat-on.
Shader "SpaceGame/ConjurerWeathered"
{
    Properties
    {
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)

        _Metallic("Metallic (dry)", Range(0,1)) = 0.75
        _MetallicWeathered("Metallic (corroded)", Range(0,1)) = 0.15
        _Smoothness("Smoothness (dry)", Range(0,1)) = 0.45
        _SmoothnessWeathered("Smoothness (corroded)", Range(0,1)) = 0.08

        _GrungeScale("Grunge scale (per metre)", Float) = 6.0
        _GrungeAmount("Grunge amount", Range(0,1)) = 0.22
        _GrungeContrast("Grunge contrast", Range(0.5,4)) = 1.6

        // Streaks run DOWN. Rust bleeds with gravity, and a weathering shader
        // with no vertical bias reads as camouflage rather than as age.
        _StreakScale("Streak scale", Float) = 2.5
        _StreakStretch("Streak stretch (vertical)", Float) = 7.0
        _StreakAmount("Streak amount", Range(0,1)) = 0.18
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "Queue" = "Geometry"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Metallic;
                float _MetallicWeathered;
                float _Smoothness;
                float _SmoothnessWeathered;
                float _GrungeScale;
                float _GrungeAmount;
                float _GrungeContrast;
                float _StreakScale;
                float _StreakStretch;
                float _StreakAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 color      : COLOR;
                float fogCoord    : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // --- value noise -------------------------------------------------
            // A hash rather than a texture, so the detail costs no memory, no UVs
            // and no import settings. Three octaves is enough: this is grain on
            // top of a field the mesh already carries, not the pattern itself.
            float hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float vnoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = hash13(i + float3(0,0,0));
                float n100 = hash13(i + float3(1,0,0));
                float n010 = hash13(i + float3(0,1,0));
                float n110 = hash13(i + float3(1,1,0));
                float n001 = hash13(i + float3(0,0,1));
                float n101 = hash13(i + float3(1,0,1));
                float n011 = hash13(i + float3(0,1,1));
                float n111 = hash13(i + float3(1,1,1));

                float x00 = lerp(n000, n100, f.x);
                float x10 = lerp(n010, n110, f.x);
                float x01 = lerp(n001, n101, f.x);
                float x11 = lerp(n011, n111, f.x);
                return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
            }

            float fbm3(float3 p)
            {
                return vnoise(p) * 0.5 + vnoise(p * 2.03) * 0.3 + vnoise(p * 4.11) * 0.2;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = nrm.normalWS;
                OUT.color = IN.color;
                OUT.fogCoord = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 n = normalize(IN.normalWS);
                float3 wp = IN.positionWS;

                // Weathering amount, straight off the mesh.
                float weathered = saturate(IN.color.a);

                // --- grunge, projected from world space on three axes ---------
                float3 blend = pow(abs(n), 4.0);
                blend /= max(1e-4, blend.x + blend.y + blend.z);

                float3 gp = wp * _GrungeScale;
                float gx = fbm3(float3(gp.y, gp.z, 0.7));
                float gy = fbm3(float3(gp.x, gp.z, 3.1));
                float gz = fbm3(float3(gp.x, gp.y, 8.4));
                float grunge = gx * blend.x + gy * blend.y + gz * blend.z;
                grunge = saturate(pow(saturate(grunge), _GrungeContrast));

                // --- streaks: same idea, stretched vertically -----------------
                float3 sp = float3(wp.x, wp.y / max(0.001, _StreakStretch), wp.z)
                            * _StreakScale;
                float streak = fbm3(sp + 11.0);
                // Only below the horizontal: a run starts somewhere and travels
                // down, so upward-facing surfaces stay comparatively clean.
                streak *= saturate(0.5 - n.y * 0.5);

                // --- albedo ---------------------------------------------------
                float3 albedo = IN.color.rgb * _BaseColor.rgb;

                // Grunge darkens rather than tints: dirt and pitting take light
                // away. Tinting it would fight the ramp the mesh already carries.
                albedo *= lerp(1.0, grunge, _GrungeAmount);
                albedo *= lerp(1.0, 1.0 - streak, _StreakAmount);

                // Corrosion is an oxide: rougher and far less conductive.
                float metallic = lerp(_Metallic, _MetallicWeathered, weathered);
                float smoothness = lerp(_Smoothness, _SmoothnessWeathered, weathered);
                // Grunge also breaks up the specular, which is what stops the
                // whole creature reading as one continuous sheet of metal.
                smoothness *= lerp(1.0, grunge, _GrungeAmount * 0.8);

                InputData inputData = (InputData)0;
                inputData.positionWS = wp;
                inputData.normalWS = n;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(wp);
                inputData.shadowCoord = TransformWorldToShadowCoord(wp);
                inputData.fogCoord = IN.fogCoord;
                inputData.bakedGI = SampleSH(n);
                inputData.normalizedScreenSpaceUV =
                    GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.metallic = metallic;
                surfaceData.smoothness = smoothness;
                surfaceData.occlusion = 1.0;
                surfaceData.alpha = 1.0;
                surfaceData.specular = half3(0, 0, 0);
                surfaceData.emission = half3(0, 0, 0);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }

        // Shadows and depth come from URP's own passes -- there is nothing custom
        // about this material's geometry, so there is nothing to reimplement.
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }

    FallBack "Universal Render Pipeline/Lit"
}
