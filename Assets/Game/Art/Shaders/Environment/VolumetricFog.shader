// Every fog volume in the scene, in one march.
//
// Pass 0 marches at reduced resolution and writes scattered colour plus coverage. Pass 1 composites
// it back at full resolution with a depth-aware upsample, because the one thing a half-resolution
// volumetric CANNOT get away with is bleeding across the silhouette of something standing in front
// of it — fog has no edges of its own, but the things it hangs behind do.
//
// The march is fullscreen rather than per-volume geometry. That is the same decision the sandstorm
// shell makes and for the same reason: a fullscreen ray clipped by scene depth serves the view from
// outside a volume, the view from inside it, and the moment of walking through its edge with no
// special case for any of them. Per-volume meshes would need front faces outside, back faces
// inside, a correct sort between overlapping volumes, and would still crack at the near plane.
Shader "SpaceGame/VolumetricFog"
{
    Properties
    {
        // On the material rather than as a global, so this and the clouds cannot end up sampling
        // different noise and reading as two unrelated kinds of air.
        [NoScaleOffset] _FogNoiseTex ("Fog Noise (3D)", 3D) = "" {}
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
        #include "VolumetricFog.hlsl"
        ENDHLSL

        Pass
        {
            Name "FogMarch"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragMarch
            #pragma target 3.5

            float4 FragMarch(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                // Reconstructing the scene's world position gives both the ray direction and where
                // to stop: the fog must not be drawn over anything nearer than it is.
                float rawDepth = SampleSceneDepth(uv);
                float3 scenePos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);

                float3 eye = _WorldSpaceCameraPos;
                float3 toScene = scenePos - eye;
                float sceneDistance = length(toScene);
                float3 rd = sceneDistance > 1e-4 ? toScene / sceneDistance : float3(0, 0, 1);

                // A sky pixel has no geometry to be occluded by, and its reconstructed position sits
                // on the camera's far PLANE — which is nearest along the forward axis, so clamping
                // to it eats the fog hardest at the centre of the screen. It bites less here than in
                // the clouds only because maxDistance is usually well inside the far clip; on a
                // camera with a short far plane a distant bank would fade out as you turned to face
                // it. See the fuller note in VolumetricClouds.shader.
            #if UNITY_REVERSED_Z
                bool isSky = rawDepth <= 0.0;
            #else
                bool isSky = rawDepth >= 1.0;
            #endif

                // The union of every volume's interval, so a ray that clips the corner of one small
                // volume a hundred metres away marches those few metres and not the hundred.
                float tMin = 1e9;
                float tMax = 0.0;
                float smallestFeature = 1e9;

                [loop]
                for (int i = 0; i < _FogVolumeCount; i++)
                {
                    float tNear, tFar;
                    if (!FogVolumeBounds(i, eye, rd, tNear, tFar))
                        continue;

                    tMin = min(tMin, tNear);
                    tMax = max(tMax, tFar);

                    // The march step is capped against the smallest billow in play. Taking the
                    // largest instead would step straight over the finest volume on screen.
                    smallestFeature = min(smallestFeature, max(1.0, _FogNoise[i].x) /
                                                           max(1.0, _FogNoise[i].z));
                }

                if (tMax <= tMin)
                    return 0;

                float reach = isSky ? _FogMaxDistance : min(sceneDistance, _FogMaxDistance);
                tMax = min(tMax, reach);
                tMin = max(tMin, 0.0);
                if (tMax <= tMin)
                    return 0;

                float3 sunDirection = normalize(_MainLightPosition.xyz);
                float3 sunColor = _MainLightColor.rgb;

                // Dither, or a low step count bands into visible rings across the whole volume.
                //
                // Against the MARCH target's resolution, not _ScreenParams. This pass writes a
                // half-resolution texture while _ScreenParams still describes the full-resolution
                // camera, so scaling by it advances the noise two steps per texel written — the
                // pattern lands at a frequency the upsample cannot filter, and thin fog comes out
                // covered in fixed-pattern stipple.
                float jitter = VolJitter(uv * _FogTexelSize.zw);

                return FogRaymarch(eye, rd, tMin, tMax, smallestFeature, sunDirection, sunColor, jitter);
            }
            ENDHLSL
        }

        Pass
        {
            Name "FogComposite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            #pragma target 3.5

            // Point, not linear: the four taps are read at exact low-resolution texel centres and
            // then weighted by hand below. A linear sampler would blend neighbours into each tap
            // before the depth test could reject them, which is the halo this upsample exists to
            // avoid. sampler_PointClamp and sampler_LinearClamp both come from Blit.hlsl.
            TEXTURE2D_X(_FogTex);

            /// Depth-aware upsample.
            ///
            /// Plain bilinear halos the fog around every foreground silhouette, because a low
            /// resolution texel that straddles an edge holds the fog for the FAR side and bilinear
            /// then smears it over the near one. Weighting the four taps by how well each one's
            /// depth agrees with this pixel's throws away the taps that belong to the other surface.
            ///
            /// The low resolution pass sampled full resolution depth at its own texel centres, so
            /// re-sampling the same full resolution texture at those centres reproduces exactly the
            /// depth each tap actually marched against. No second depth target is needed.
            float4 SampleFogBilateral(float2 uv, float centerDepth)
            {
                float2 centre = floor(uv * _FogTexelSize.zw - 0.5) + 0.5;

                float4 total = 0;
                float totalWeight = 0;

                // Three by three, not two by two. A 2x2 kernel spans exactly one march texel per
                // output pixel in each axis, so the per-texel jitter survives it intact and shows
                // as fixed-pattern stipple wherever the fog is thin enough to see through. Nine
                // taps average a full neighbourhood of jitter offsets away, which is what the
                // jitter was always meant to be resolved by.
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 tapUv = (centre + float2(x, y)) * _FogTexelSize.xy;

                        // A tent, so the centre texel still dominates and the result does not
                        // smear the fog a full texel sideways.
                        float spatial = (2.0 - abs((float)x)) * (2.0 - abs((float)y));

                        float tapDepth = LinearEyeDepth(SampleSceneDepth(tapUv), _ZBufferParams);

                        // Relative, not absolute: a metre of disagreement is nothing at two
                        // hundred metres and everything at two.
                        float difference = abs(tapDepth - centerDepth) / max(1.0, centerDepth);
                        float weight = spatial / (1.0 + difference * 32.0);

                        total += SAMPLE_TEXTURE2D_X(_FogTex, sampler_PointClamp, tapUv) * weight;
                        totalWeight += weight;
                    }
                }

                // Every tap disagreeing is a thin sliver of geometry with no good neighbour. Falling
                // back to the plain bilinear fetch is better than dividing by nearly zero.
                if (totalWeight < 1e-4)
                    return SAMPLE_TEXTURE2D_X(_FogTex, sampler_LinearClamp, uv);

                return total / totalWeight;
            }

            float4 FragComposite(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                float centerDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float4 fog = SampleFogBilateral(uv, centerDepth);

                float3 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                return float4(lerp(scene, fog.rgb, saturate(fog.a)), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
