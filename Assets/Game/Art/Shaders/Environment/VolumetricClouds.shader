// Clouds that are actually up there.
//
// The sky this replaces the cloud half of (DesertSkybox) draws its dust as 2D noise on the sky
// sphere. That has two tells you cannot tune away: it has no parallax, so the clouds are pinned to
// your head and slide with you as you cross the map, and it has no depth, so they never pass in
// front of each other and never lie down toward the horizon. This marches a real shell of air.
//
// The shell is spherical, around a planet centre placed far below the camera — not a pair of
// horizontal planes. That single choice is what makes the horizon work: on a sphere the layer curves
// away and the clouds crowd together and thin out as they approach the horizon, exactly as real ones
// do, while between two planes they stretch to infinity and read as a textured ceiling.
//
// The centre follows the camera in XZ, so the player can never walk to the edge of the sky. Nothing
// in the shape depends on absolute world position, so this is invisible: the weather map that
// decides where the clouds are is sampled in world space and does not move with the centre.
Shader "SpaceGame/VolumetricClouds"
{
    Properties
    {
        [NoScaleOffset] _FogNoiseTex ("Cloud Noise (3D)", 3D) = "" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "VolumetricCore.hlsl"

        TEXTURE3D(_FogNoiseTex);
        SAMPLER(sampler_FogNoiseTex);

        // Pushed by CloudLayer.
        float4 _CloudAltitude;   // x bottom (m), y top (m), z planet radius (m), w coverage 0..1
        float4 _CloudColor;      // rgb albedo, a ambient fraction
        float4 _CloudShape;      // x billow scale (m), y erosion, z detail scale, w density
        float4 _CloudMotion;     // xyz world drift (m), w anisotropy
        float4 _CloudLighting;   // x extinction, y powder, z silver lining, w weather map scale
        float4 _CloudSkyLight;   // rgb sky radiance

        float _CloudSteps;
        float _CloudLightSteps;
        float _CloudMaxDistance;

        // xy = one texel of the reduced-resolution march target in uv, zw = that target's size in
        // pixels. Needed by both passes for the same reasons as the fog's equivalent.
        float4 _CloudTexelSize;

        float3 CloudSphereCenter()
        {
            // Under the camera, not under the world origin: a fixed centre would put the player at
            // the edge of the dome after a kilometre of walking, and the clouds would visibly tip.
            return float3(_WorldSpaceCameraPos.x,
                          _WorldSpaceCameraPos.y - _CloudAltitude.z,
                          _WorldSpaceCameraPos.z);
        }

        /// How much cloud the weather wants at this spot on the map, ignoring altitude.
        ///
        /// Sampled in absolute world XZ so the pattern is nailed to the world rather than to the
        /// camera-following shell. Fly a kilometre and you arrive under different clouds.
        float CloudCoverage(float3 positionWS)
        {
            float scale = max(1.0, _CloudLighting.w);
            float3 flat = float3(positionWS.x, 0.0, positionWS.z) + _CloudMotion.xyz;

            // Two octaves at scales that are not a whole-number ratio of each other.
            //
            // The noise volume tiles, so a single octave repeats every `scale` metres — and near
            // the horizon you are looking along tens of kilometres of it at once, so that repeat
            // reads as a grid of identical puffs marching off into the distance. It is invisible
            // overhead, where one tile fills the view, and unmissable at a shallow angle. A second
            // octave at 2.7x the scale pushes the combined period out far enough that the eye
            // stops finding it, for one extra fetch on a function that is already sampling.
            float4 broad = SAMPLE_TEXTURE3D_LOD(_FogNoiseTex, sampler_FogNoiseTex,
                                                flat / (scale * 2.7) + 0.61, 0);
            float4 weatherNoise = SAMPLE_TEXTURE3D_LOD(_FogNoiseTex, sampler_FogNoiseTex,
                                                       flat / scale, 0);

            float map = saturate(weatherNoise.r * 0.5 + weatherNoise.g * 0.2
                               + broad.r * 0.45 - 0.075);

            // The coverage dial has to be a threshold on the map, not a multiplier of it. A
            // multiplier fades every cloud in the sky in and out together, which reads as the whole
            // sky getting dirty; a threshold grows and shrinks the clouds that are there and lets
            // new ones appear, which reads as weather.
            return saturate(VolRemap(map, 1.0 - _CloudAltitude.w, 1.0, 0.0, 1.0));
        }

        /// The vertical profile of a cumulus: rounded underneath, spreading and softening on top.
        ///
        /// Without it the layer is a slab with a flat base and a flat top, and no amount of noise
        /// hides a flat base — it is the part of a cloud a player on the ground looks at most.
        float CloudHeightProfile(float heightFraction, float coverage)
        {
            float bottom = saturate(VolRemap(heightFraction, 0.0, 0.12, 0.0, 1.0));
            float top = saturate(VolRemap(heightFraction, 0.35, 1.0, 1.0, 0.0));

            // Denser weather builds taller: where coverage is high the profile keeps more of its
            // upper half, so thick banks tower and thin ones stay as flat scraps.
            return bottom * top * lerp(0.6, 1.0, coverage);
        }

        /// Density of the layer at a world position.
        ///
        /// `detailFade` goes from 0 close to the camera to 1 far away, and removes the eroding
        /// octave as it rises. This is antialiasing, not art direction: the march step grows with
        /// distance — a near-horizon ray steps hundreds of metres — while the detail octave carves
        /// features a fraction of that size, so far cloud gets point-sampled far below its own
        /// frequency and breaks into speckle. Fading the detail out leaves the smooth base shape,
        /// which the step length can still resolve. The noise volume has no mip chain, so biasing
        /// the sample LOD is not available to do this properly.
        float CloudDensity(float3 positionWS, float3 sphereCenter, float detailFade)
        {
            float radius = length(positionWS - sphereCenter);
            float inner = _CloudAltitude.z + _CloudAltitude.x;
            float outer = _CloudAltitude.z + _CloudAltitude.y;

            float heightFraction = saturate((radius - inner) / max(1.0, outer - inner));
            if (heightFraction <= 0.0 || heightFraction >= 1.0)
                return 0.0;

            float weather = CloudCoverage(positionWS);
            float coverage = weather * CloudHeightProfile(heightFraction, weather);
            if (coverage <= 0.002)
                return 0.0;

            float scale = max(1.0, _CloudShape.x);
            float3 uvw = (positionWS + _CloudMotion.xyz) / scale;

            float4 base = SAMPLE_TEXTURE3D_LOD(_FogNoiseTex, sampler_FogNoiseTex, uvw, 0);
            float lowFrequency = saturate(base.r * 0.6 + base.g * 0.3 + base.b * 0.1);

            float density = VolCoverageRemap(lowFrequency, coverage);
            if (density <= 0.002)
                return 0.0;

            float erosion = _CloudShape.y * (1.0 - saturate(detailFade));
            if (erosion > 0.001)
            {
                float4 fine = SAMPLE_TEXTURE3D_LOD(_FogNoiseTex, sampler_FogNoiseTex,
                                                   uvw * max(1.5, _CloudShape.z) + 0.37, 0);
                float detail = saturate(fine.g * 0.6 + fine.b * 0.3 + fine.a * 0.1);

                // The erosion is strongest at the top and mildest at the base, which is what gives a
                // cumulus its wispy crown over a solid bottom. Eroding uniformly shreds the base too
                // and the whole layer turns to gauze.
                density = VolErode(density, detail, erosion * lerp(0.4, 1.0, heightFraction));
            }

            return density * _CloudShape.w;
        }

        float CloudSunTransmittance(float3 p, float3 sphereCenter, float3 sunDirection, int steps)
        {
            float stepLength = (_CloudAltitude.y - _CloudAltitude.x) * 0.12;
            float optical = 0.0;
            float3 q = p;

            for (int i = 0; i < steps; i++)
            {
                // Growing steps: the near samples decide the shading, the far ones only need to
                // notice that a kilometre of cloud is in the way.
                stepLength *= 1.5;
                q += sunDirection * stepLength;
                optical += CloudDensity(q, sphereCenter, 0.0) * stepLength;
            }

            return VolMultiScatter(optical * _CloudLighting.x);
        }
        ENDHLSL

        Pass
        {
            Name "CloudMarch"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragMarch
            #pragma target 3.5

            float4 FragMarch(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                float rawDepth = SampleSceneDepth(uv);
                float3 scenePos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);

                float3 eye = _WorldSpaceCameraPos;
                float3 toScene = scenePos - eye;
                float sceneDistance = length(toScene);
                float3 rd = sceneDistance > 1e-4 ? toScene / sceneDistance : float3(0, 0, 1);

                // Whether this pixel has any geometry on it at all.
                //
                // This matters far more than it looks. A sky pixel's reconstructed "scene position"
                // is a point on the camera's far PLANE, and a plane is nearest along the forward
                // axis: at a 3 km far clip and a 62 degree field of view, that reconstructed
                // distance is 3000 m at the centre of the screen and 4745 m in the corner. Clamping
                // the march to it therefore clips the layer away hardest exactly where the player is
                // looking, so a cloud visible out of the corner of your eye vanishes the moment you
                // turn to face it. It also cut off everything below about 20 degrees of elevation,
                // which is most of the sky.
                //
                // Geometry still occludes the clouds. Nothing does when there is no geometry.
            #if UNITY_REVERSED_Z
                bool isSky = rawDepth <= 0.0;
            #else
                bool isSky = rawDepth >= 1.0;
            #endif

                float3 center = CloudSphereCenter();
                float inner = _CloudAltitude.z + _CloudAltitude.x;
                float outer = _CloudAltitude.z + _CloudAltitude.y;

                float tNear, tFar;
                if (!VolRayShell(eye, rd, center, inner, outer, tNear, tFar))
                    return 0;

                float reach = isSky ? _CloudMaxDistance : min(sceneDistance, _CloudMaxDistance);
                tFar = min(tFar, reach);
                if (tFar <= tNear)
                    return 0;

                int steps = max(8, (int)_CloudSteps);
                int lightSteps = max(1, (int)_CloudLightSteps);

                // How much of the shell this ray actually marches.
                //
                // Straight up it is the layer's thickness — a kilometre or two. Toward the horizon
                // the same shell is tens of kilometres deep, and dividing THAT by the step count
                // gives steps several times longer than a billow: the march walks straight over the
                // clouds and the deck dissolves into mush exactly where a real one looks densest.
                //
                // So cap the span rather than the step, and accept not reaching the far side. A
                // shallow ray accumulates optical depth quickly — it is travelling along the deck
                // rather than through it — so what lies beyond the cap is behind cloud that is
                // already opaque. Deriving the cap from the billow size and the step count keeps the
                // step under three quarters of a billow at every quality tier by construction.
                float maxSpan = max(1.0, _CloudShape.x) * steps * 0.75;
                float span = min(tFar - tNear, maxSpan);
                float stepLength = span / steps;
                // Against the MARCH target's resolution, not _ScreenParams — see the same note in
                // VolumetricFog.shader. Scaling by the full-resolution size advances the noise two
                // steps per texel written, and the wispy edges of a cloud come out speckled with a
                // fixed pattern that no amount of tuning the erosion removes.
                float jitter = VolJitter(uv * _CloudTexelSize.zw);
                float t = tNear + stepLength * jitter;

                float3 sunDirection = normalize(_MainLightPosition.xyz);
                float3 sunColor = _MainLightColor.rgb;
                float cosAngle = dot(rd, sunDirection);

                // Dual lobe: the forward one is the glare when you look toward the sun through a
                // cloud, the backward one is the silver lining on the far side of it.
                float phase = VolPhaseDual(_CloudMotion.w, 0.3, _CloudLighting.z, cosAngle);

                float transmittance = 1.0;
                float3 scatter = 0.0;

                [loop]
                for (int i = 0; i < steps; i++)
                {
                    float3 p = eye + rd * t;
                    t += stepLength;

                    // Tied to how far along the ray this sample is, which is what the step length
                    // grows with. Full detail nearby, none at the far end of the marched span.
                    float detailFade = saturate((t - tNear) / max(1.0, span));

                    float density = CloudDensity(p, center, detailFade);
                    if (density <= 0.002)
                        continue;

                    float sun = CloudSunTransmittance(p, center, sunDirection, lightSteps);
                    float powder = VolPowder(density, _CloudLighting.y);

                    float3 luminance = _CloudColor.rgb * _CloudColor.a * _CloudSkyLight.rgb
                                     + _CloudColor.rgb * sunColor * sun * phase * powder;

                    float absorbed = 1.0 - exp(-density * _CloudLighting.x * stepLength);
                    scatter += transmittance * absorbed * luminance;
                    transmittance *= 1.0 - absorbed;

                    if (transmittance < 0.01)
                        break;
                }

                float alpha = saturate(1.0 - transmittance);
                return float4(scatter / max(alpha, 1e-4), alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "CloudComposite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            #pragma target 3.5

            TEXTURE2D_X(_CloudTex);

            /// The same depth-aware upsample the fog uses, and for both of its reasons.
            ///
            /// The nine taps average the per-texel march jitter out — a cloud's torn edges are
            /// exactly where a single tap per output pixel shows as speckle — and the depth weight
            /// keeps the result off the silhouette of anything standing in front of the sky.
            float4 SampleCloudBilateral(float2 uv, float centerDepth)
            {
                float2 centre = floor(uv * _CloudTexelSize.zw - 0.5) + 0.5;

                float4 total = 0;
                float totalWeight = 0;

                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 tapUv = (centre + float2(x, y)) * _CloudTexelSize.xy;
                        float spatial = (2.0 - abs((float)x)) * (2.0 - abs((float)y));

                        float tapDepth = LinearEyeDepth(SampleSceneDepth(tapUv), _ZBufferParams);
                        float difference = abs(tapDepth - centerDepth) / max(1.0, centerDepth);
                        float weight = spatial / (1.0 + difference * 32.0);

                        total += SAMPLE_TEXTURE2D_X(_CloudTex, sampler_PointClamp, tapUv) * weight;
                        totalWeight += weight;
                    }
                }

                if (totalWeight < 1e-4)
                    return SAMPLE_TEXTURE2D_X(_CloudTex, sampler_LinearClamp, uv);

                return total / totalWeight;
            }

            float4 FragComposite(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                float centerDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float4 cloud = SampleCloudBilateral(uv, centerDepth);
                float3 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                return float4(lerp(scene, cloud.rgb, saturate(cloud.a)), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
