// The gunk coming out of the nozzle.
//
// WHY THIS IS NOT A SOFT SPRITE. A billboard with a blurry blob texture is what most particle paint
// is, and it always reads as smoke tinted the wrong colour: it is translucent, it has no highlight,
// it does not occlude the thing behind it, and a hundred of them pile into fog rather than into
// liquid. Thickness is a LIGHTING property, not an opacity one. So every droplet here is shaded as
// an actual sphere.
//
// The technique is the impostor: the quad's own UV is read as a position on a unit disc, the third
// component of the normal is recovered from it as sqrt(1 - x² - y²), and anything outside the disc
// is discarded. That gives each particle a real hemispherical normal, so it takes a specular
// highlight that MOVES as the droplet flies and a fresnel that brightens its silhouette. It is
// opaque and writes depth, which is what makes a dense stream merge into one running mass instead
// of averaging into haze — the droplets genuinely occlude one another, and they occlude the world.
//
// WHAT WAS REJECTED, and why, because both are the "correct" answers and both are wrong here:
//
//   • Screen-space fluid (Obi's route): splat particles into full-screen thickness and depth
//     buffers, blur, threshold, then light the result. It is the real thing and it genuinely looks
//     like liquid. It is also a set of full-screen passes per frame, which is the exact price this
//     project already refused once — PortalRenderer was deleted for costing one extra scene render
//     per aperture. Paying it again for the muzzle of one gun is not a trade worth making.
//
//   • Raymarched metaballs (the Shader Graph route): evaluate a smooth-union SDF over live particle
//     positions and sphere-trace it. Correct, and the author of the best-known Unity write-up of it
//     says in the same breath that it is "arguably too expensive for a real-time 3d application".
//
// The impostor gets most of the read for a per-fragment cost of a dot product and a sqrt. The
// merged, metaball look is kept for the ONE place it is affordable and matters most — the splat
// left on the wall, which is a single quad and does evaluate the real smooth-union field. See
// PortalSplat.shader.
//
// The colour comes from the particle system's own start colour, so one material serves both barrels
// and PortalGunItem tints the stream by setting the system's colour rather than by instancing a
// material per barrel.
Shader "SpaceGame/Portal/PortalGoo"
{
    Properties
    {
        _Glossiness  ("Glossiness", Range(1.0, 128.0)) = 42.0
        _SpecStrength("Specular strength", Range(0.0, 4.0)) = 1.6
        _Fresnel     ("Fresnel", Range(0.0, 4.0)) = 1.5
        _ShadeDepth  ("Core darkening", Range(0.0, 1.0)) = 0.45
        _Cutoff      ("Silhouette cutoff", Range(0.0, 1.0)) = 0.5

        [Toggle(_GOO_STRETCH)] _Stretch ("Stretch along velocity", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry+10"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector"= "True"
        }

        Pass
        {
            Name "PortalGoo"

            // Opaque and depth-writing, which is the whole reason this reads as a substance. A
            // transparent droplet cannot occlude the droplet behind it, and a stream of things that
            // do not occlude one another is fog however it is coloured.
            Blend Off
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Glossiness;
                float _SpecStrength;
                float _Fresnel;
                float _ShadeDepth;
                float _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 colour     : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float4 colour      : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);

                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = positions.positionCS;
                OUT.positionWS  = positions.positionWS;
                OUT.uv          = IN.uv;
                OUT.colour      = IN.colour;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // The impostor. The quad's UV becomes a point on a unit disc; anything past the
                // rim of that disc is not part of this droplet at all.
                float2 p = IN.uv * 2.0 - 1.0;
                float  r2 = dot(p, p);
                clip(_Cutoff * 4.0 - r2 * 2.0);

                // The recovered hemisphere. This single line is what turns a flat billboard into
                // something the light can find a highlight on.
                float3 normalVS = float3(p, sqrt(saturate(1.0 - r2)));

                // Into world space through the camera's basis, so the highlight tracks the real sun
                // rather than sliding with the screen.
                float3 normalWS = normalize(
                    normalVS.x * unity_CameraToWorld._m00_m10_m20 +
                    normalVS.y * unity_CameraToWorld._m01_m11_m21 +
                    normalVS.z * unity_CameraToWorld._m02_m12_m22);

                float3 viewWS = normalize(GetCameraPositionWS() - IN.positionWS);

                Light sun = GetMainLight();
                float3 lightWS = normalize(sun.direction);

                // Half-lambert. A droplet lit to black on its dark side reads as a hole; paint in
                // shadow is still obviously paint.
                float ndotl = saturate(dot(normalWS, lightWS)) * 0.5 + 0.5;

                float3 halfWS = normalize(lightWS + viewWS);
                float spec = pow(saturate(dot(normalWS, halfWS)), _Glossiness) * _SpecStrength;

                // Bright at the silhouette: a wet, rounded volume catches light around its edge,
                // and this is most of what sells "thick" at a glance.
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewWS)), 3.0) * _Fresnel;

                // Darker through the middle, where a real blob is deepest and least translucent.
                float body = lerp(1.0 - _ShadeDepth, 1.0, sqrt(saturate(r2)));

                float3 albedo = IN.colour.rgb;
                float3 col = albedo * (ndotl * body) * (sun.color * 0.6 + 0.4);

                col += albedo * fresnel;
                col += (sun.color * 0.5 + 0.5) * spec;

                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // Casting shadows is what stops a stream of paint looking like it is made of light. The
        // silhouette has to match the impostor's, so the disc test is repeated rather than the
        // built-in caster being used.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex shadowVert
            #pragma fragment shadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Glossiness;
                float _SpecStrength;
                float _Fresnel;
                float _ShadeDepth;
                float _Cutoff;
            CBUFFER_END

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct ShadowVaryings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            ShadowVaryings shadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT;
                OUT.positionHCS = GetVertexPositionInputs(IN.positionOS.xyz).positionCS;
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 shadowFrag(ShadowVaryings IN) : SV_Target
            {
                float2 p = IN.uv * 2.0 - 1.0;
                clip(_Cutoff * 4.0 - dot(p, p) * 2.0);
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
