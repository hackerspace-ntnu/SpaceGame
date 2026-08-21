// The inside of a sandstorm — the half of the effect that makes it impossible to see through.
//
// Same volumetric core as the silhouette shell, so walking into the storm does not change what the
// sand is made of: the same eroded shape, the same self-shadowing, the same forward scattering.
// What changes is the extinction, which comes from the profile's gameplay visibility, and the fact
// that this pass is guaranteed to cover the screen.
//
// Pass 0 marches at reduced resolution and writes sand colour plus coverage. Pass 1 composites at
// full resolution and adds the grit — a little screen warp and a little grain, both scaled by
// coverage so they cost nothing in clear air.
Shader "SpaceGame/Sandstorm"
{
    Properties
    {
        // Assigned on the material rather than as a global, so the fullscreen pass and the shell
        // cannot end up sampling different sand.
        [NoScaleOffset] _SandstormNoise ("Sand Noise (3D)", 3D) = "" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        // Runtime/Utilities, not ShaderLibrary — the location most examples quote is wrong for the
        // core package version this project ships. Provides Vert, Varyings, _BlitTexture and
        // sampler_LinearClamp, and is what Blitter.BlitTexture expects the material to be built on.
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "SandstormVolume.hlsl"

        // Set from SandstormVisuals, field for field with StormFootprint.
        float4 _SandstormCenter;   // xz = centre, y = base height, w = 1 while a storm is active
        float4 _SandstormShapeA;   // radius, edge feather, height, height feather
        float4 _SandstormShapeB;   // kind (0 cell, 1 wall), lateral extent, heading x, heading z
        float4 _SandstormColor;    // rgb = sand colour, a = lifecycle intensity
        float4 _SandstormParams;   // visibility (m), noise scale (m), noise speed (m/s), distortion
        float4 _SandstormLook;     // erosion, forward scatter, ambient, wind stretch
        float4 _SandstormInterior; // clear radius (m), max opacity, unused, unused
        float _SandstormGrain;

        // Set per frame from the render feature's quality tier.
        float _Steps;
        float _LightSteps;
        float _MaxDistance;

        float Hash13(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.yzx + 33.33);
            return frac((p.x + p.y) * p.z);
        }
        ENDHLSL

        Pass
        {
            Name "SandstormFog"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragFog
            #pragma target 3.5

            float4 FragFog(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                // Reconstructing the scene's world position gives both the ray direction and where
                // to stop: the sand must not be drawn over anything nearer than it is.
                float rawDepth = SampleSceneDepth(uv);
                float3 scenePos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);

                float3 eye = _WorldSpaceCameraPos;
                float3 toScene = scenePos - eye;
                float distance = length(toScene);
                float3 rd = distance > 1e-4 ? toScene / distance : float3(0, 0, 1);

                StormVolume volume;
                volume.center = _SandstormCenter.xyz;
                volume.heading = _SandstormShapeB.zw;
                volume.radius = _SandstormShapeA.x;
                volume.lateralExtent = _SandstormShapeB.y;
                volume.edgeFeather = _SandstormShapeA.y;
                volume.height = _SandstormShapeA.z;
                volume.heightFeather = _SandstormShapeA.w;
                volume.isWall = _SandstormShapeB.x;
                volume.intensity = _SandstormColor.a;

                float tNear, tFar;
                if (!StormRayBounds(volume, eye, rd, tNear, tFar))
                    return 0;

                // Past the far clamp the sand is already opaque in any storm worth the name, so
                // marching further only costs.
                tFar = min(tFar, min(distance, _MaxDistance));

                // Hold the sand off your face. A march that starts at the camera fogs the ground
                // under your own boots first, which is the one piece of the world a player inside
                // a storm has to keep.
                tNear = max(tNear, _SandstormInterior.x);
                if (tFar <= tNear)
                    return 0;

                StormLook look;
                look.color = _SandstormColor.rgb;
                // Chosen so a full-density metre count equal to the profile's visibility leaves
                // about 5% of the view through, which is what "you can see this far" has to mean
                // for the number to be honest.
                look.extinction = 3.0 / max(0.01, _SandstormParams.x);
                look.noiseScale = max(1.0, _SandstormParams.y);
                look.drift = float3(_SandstormShapeB.z, -0.35, _SandstormShapeB.w) *
                             (_Time.y * _SandstormParams.z);
                look.erosion = _SandstormLook.x;
                look.anisotropy = _SandstormLook.y;
                look.ambient = _SandstormLook.z;
                look.stretch = _SandstormLook.w;
                look.steps = (int)_Steps;
                look.lightSteps = (int)_LightSteps;

                float3 sunDirection = normalize(_MainLightPosition.xyz);
                float3 sunColor = _MainLightColor.rgb;

                // Dither, or eight steps band into visible rings that the grain cannot hide.
                float jitter = InterleavedGradientNoise(uv * _ScreenParams.xy, 0);

                float4 fog = StormRaymarch(volume, look, eye, rd, tNear, tFar, sunDirection, sunColor, jitter);

                // Capped, never 1. Full opacity is a blank screen, and no amount of tuning the
                // extinction fixes that — the integral always gets there eventually.
                fog.a = min(fog.a, _SandstormInterior.y);
                return fog;
            }
            ENDHLSL
        }

        Pass
        {
            Name "SandstormComposite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            #pragma target 3.5

            TEXTURE2D_X(_SandstormFogTex);
            SAMPLER(sampler_SandstormFogTex);

            float4 FragComposite(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                float4 fog = SAMPLE_TEXTURE2D_X(_SandstormFogTex, sampler_SandstormFogTex, uv);

                // Grit moving in front of the eye, not a drunk filter: the warp is a fraction of a
                // percent of the screen and only exists where the fog does.
                float2 warpSeed = uv * 9.0 + _Time.y * float2(0.7, 0.35);
                float2 warp = float2(Hash13(float3(warpSeed, 0.0)), Hash13(float3(warpSeed, 7.3))) - 0.5;
                float2 sceneUv = uv + warp * _SandstormParams.w * fog.a;

                float3 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sceneUv).rgb;
                float3 color = lerp(scene, fog.rgb, fog.a);

                float grain = Hash13(float3(uv * _ScreenParams.xy, frac(_Time.y) * 100.0)) - 0.5;
                color *= 1.0 + grain * _SandstormGrain * fog.a;

                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
