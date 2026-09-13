// The storm itself: a volume of sand raymarched inside a bounding shell.
//
// The mesh is only a proxy for where the storm might be on screen — every pixel intersects the
// storm's real analytic shape and marches it, so the silhouette is decided by density and light,
// not by the geometry. That is the difference between a wall of sand and a painted cylinder.
//
// Drawn on BACK faces with the depth test off, and the march clipped by scene depth instead. That
// combination is what lets one shell serve the view from outside, the view from inside, and the
// moment you walk through the edge, without any of the three being a special case.
Shader "SpaceGame/SandstormWall"
{
    Properties
    {
        [NoScaleOffset] _SandstormNoise ("Sand Noise (3D)", 3D) = "" {}
        _StormColor ("Sand Color", Color) = (0.78, 0.56, 0.33, 1)
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Extinction ("Extinction /m", Float) = 0.012
        _NoiseScale ("Noise Scale (m)", Float) = 260
        _Erosion ("Edge Erosion", Range(0, 1)) = 0.55
        _Anisotropy ("Forward Scatter", Range(0, 0.95)) = 0.6
        _Ambient ("Ambient", Range(0, 2)) = 0.9
        _Stretch ("Wind Stretch", Range(1, 6)) = 3
        _Steps ("March Steps", Float) = 24
        _LightSteps ("Light Steps", Float) = 4
        _BillowSpeed ("Billow Speed", Float) = 22
        _StormHeight ("Storm Height", Float) = 900
        _StormBaseY ("Storm Base Y", Float) = 0
        _StormCenter ("Storm Center XZ", Vector) = (0, 0, 0, 0)
        _StormShapeA ("radius, feather, height, heightFeather", Vector) = (400, 150, 900, 350)
        _StormShapeB ("isWall, lateral, headingX, headingZ", Vector) = (0, 0, 0, 1)
        _Intensity ("Intensity", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SandstormVolume"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "SandstormVolume.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
            };

            float4 _StormColor;
            float4 _StormCenter;
            float4 _StormShapeA;
            float4 _StormShapeB;
            float _Opacity;
            float _Extinction;
            float _NoiseScale;
            float _Erosion;
            float _Anisotropy;
            float _Ambient;
            float _Stretch;
            float _Steps;
            float _LightSteps;
            float _BillowSpeed;
            float _StormHeight;
            float _StormBaseY;
            float _Intensity;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);

                // Pin the shell to the far plane instead of letting the rasteriser clip it away.
                //
                // This pass draws BACK faces, and a storm shell is kilometres across: from any
                // normal viewing distance its far side sits past the camera's far plane (1000 m
                // here). Clipped there, the pixels in front of it get no fragment at all and the
                // storm comes out with a straight-edged HOLE punched through the middle of it —
                // rectangular for a wall, because a wall's shell is a box and a plane cuts flat
                // faces along straight lines. The hole shrinks as you walk toward the storm,
                // because more of the far face comes back inside the far plane.
                //
                // Depth is meaningless to this pass — ZTest Always, ZWrite Off, and the march is
                // clipped by the depth TEXTURE rather than by the depth test — so clamping z
                // costs nothing. positionWS is interpolated from w, which is untouched, so the
                // ray the fragment marches is still the real one.
            #if UNITY_REVERSED_Z
                output.positionCS.z = max(output.positionCS.z, 0.0);
            #else
                output.positionCS.z = min(output.positionCS.z, output.positionCS.w);
            #endif

                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 screenUv = input.screenPos.xy / max(1e-4, input.screenPos.w);

                float3 eye = _WorldSpaceCameraPos;
                float3 rd = normalize(input.positionWS - eye);

                StormVolume volume;
                volume.center = float3(_StormCenter.x, _StormBaseY, _StormCenter.y);
                volume.heading = _StormShapeB.zw;
                volume.radius = _StormShapeA.x;
                volume.lateralExtent = _StormShapeB.y;
                volume.edgeFeather = _StormShapeA.y;
                volume.height = _StormHeight;
                volume.heightFeather = _StormShapeA.w;
                volume.isWall = _StormShapeB.x;
                volume.intensity = _Intensity;

                float tNear, tFar;
                if (!StormRayBounds(volume, eye, rd, tNear, tFar))
                    discard;

                // Whatever the scene put in front of the storm ends the march. This is what stands
                // in for a depth test: geometry nearer than the storm hides it, geometry inside it
                // is correctly buried in sand.
                //
                // Only where there IS geometry. A sky pixel's depth is the camera's far PLANE, and
                // a plane is nearest along the forward axis: at a 1000 m far clip the clamp lands
                // at 1000 m in the centre of the screen and past 1600 m in the corner. Applied to
                // sky it therefore cuts the storm off hardest exactly where the player is looking,
                // so a wall standing more than a kilometre away is visible out of the corner of
                // your eye and vanishes the moment you turn to face it — and the cut sweeps across
                // it as you turn, because the plane turns with your head. Nothing occludes a storm
                // seen against the sky, so nothing shortens the march there. Same guard and the
                // same reason as VolumetricFog.shader and VolumetricClouds.shader; the vertex
                // shader's far-plane clamp above solves the rasterisation half of this, not this
                // half.
                float rawDepth = SampleSceneDepth(screenUv);
            #if UNITY_REVERSED_Z
                bool isSky = rawDepth <= 0.0;
            #else
                bool isSky = rawDepth >= 1.0;
            #endif

                if (!isSky)
                {
                    float sceneEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                    float viewCos = max(1e-4, dot(rd, -UNITY_MATRIX_V[2].xyz));
                    tFar = min(tFar, sceneEyeDepth / viewCos);
                }

                if (tFar <= tNear)
                    discard;

                StormLook look;
                look.color = _StormColor.rgb;
                look.extinction = _Extinction;
                look.noiseScale = max(1.0, _NoiseScale);
                look.drift = float3(0.0, -_Time.y * _BillowSpeed, 0.0);
                look.erosion = _Erosion;
                look.anisotropy = _Anisotropy;
                look.ambient = _Ambient;
                look.stretch = _Stretch;
                look.steps = (int)_Steps;
                look.lightSteps = (int)_LightSteps;

                float3 sunDirection = normalize(_MainLightPosition.xyz);
                float3 sunColor = _MainLightColor.rgb;

                float jitter = InterleavedGradientNoise(input.positionCS.xy, 0);
                float4 storm = StormRaymarch(volume, look, eye, rd, tNear, tFar,
                                             sunDirection, sunColor, jitter);

                storm.a *= _Opacity;
                if (storm.a < 0.002)
                    discard;

                return storm;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
