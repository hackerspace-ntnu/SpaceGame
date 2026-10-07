Shader "SpaceGame/FlashlightBeam"
{
    // Simple flashlight beam — a thin bright pillar of light in 3D space.
    //
    // For each fragment we find the closest point on the view ray to the
    // light axis (clamped to the beam region), then shade based on:
    //   - radial distance from the axis (a thin "core" + wider "halo")
    //   - axial distance from the source (linear falloff)
    //
    // No ray marching. Brightness depends only on the WORLD-SPACE position of
    // the cone in 3D, not how long the camera ray spends inside it. So pointing
    // the camera down the beam axis does NOT brighten the screen — the beam
    // looks the same shape and brightness from any angle, which is what a real
    // flashlight beam looks like.
    //
    // Occlusion: clip the sampled point against the scene depth buffer and
    // against the raycast-computed beam end (_FlashlightBeamEnd). The beam
    // stops at walls and ground.
    Properties
    {
        _Color           ("Beam Color",           Color)            = (1, 0.95, 0.85, 1)
        _Intensity       ("Intensity",            Range(0, 5))      = 0.6
        _CoreWidth       ("Core Width (frac)",    Range(0.01, 1))   = 0.15
        _CoreStrength    ("Core Strength",        Range(0, 2))      = 1.0
        _HaloWidth       ("Halo Width (frac)",    Range(0.05, 1))   = 0.55
        _HaloStrength    ("Halo Strength",        Range(0, 2))      = 0.18
        _HaloPow         ("Halo Falloff Power",   Range(1, 8))      = 3.0
        _BeamLength      ("Beam Visible Length (m)",Range(0.5, 100))= 6.0
        _EndFadePow      ("End Fade Power",         Range(1.0, 6.0)) = 3.0
        _NearFade        ("Near Fade Distance",   Range(0, 5))      = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        LOD 100

        Pass
        {
            Name "Beam"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _FlashlightPos;
            float4 _FlashlightDir;
            float4 _FlashlightColor;
            float4 _FlashlightParams;   // x=cosOuter, y=cosInner, z=reach, w=enabled
            float4 _FlashlightFalloff;  // x=k, y=rangeFadeStart
            float4 _FlashlightBeamEnd;  // x=beamLength (raycast-clipped)

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float4 screenPos   : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Intensity;
                float  _CoreWidth;
                float  _CoreStrength;
                float  _HaloWidth;
                float  _HaloStrength;
                float  _HaloPow;
                float  _BeamLength;
                float  _EndFadePow;
                float  _NearFade;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = p.positionCS;
                OUT.positionWS  = p.positionWS;
                OUT.screenPos   = ComputeScreenPos(p.positionCS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                if (_FlashlightParams.w < 0.5)
                    return half4(0, 0, 0, 0);

                float3 camPos = _WorldSpaceCameraPos;
                float3 V = normalize(IN.positionWS - camPos);

                float3 O = _FlashlightPos.xyz;
                float3 D = normalize(_FlashlightDir.xyz);
                float cosOuter = _FlashlightParams.x;
                float beamEnd  = max(_FlashlightBeamEnd.x, 0.01);

                // --- Scene depth clip — how far along V the world is solid ---
                float2 uv = IN.screenPos.xy / IN.screenPos.w;
                float rawDepth = SampleSceneDepth(uv);
                float sceneEye = LinearEyeDepth(rawDepth, _ZBufferParams);
                float3 camFwd = -UNITY_MATRIX_V._m20_m21_m22;
                float sceneT = sceneEye / max(dot(V, camFwd), 1e-4);

                // --- Closest point on view ray to the light axis ---
                // Standard line-line closest-point: line A = (camPos, V), line B = (O, D).
                float DV = dot(D, V);
                float3 w = camPos - O;
                float DW = dot(D, w);
                float VW = dot(V, w);
                float denom = 1.0 - DV * DV;
                float tOnView;
                if (abs(denom) < 1e-4)
                    tOnView = 0.0;
                else
                    tOnView = (DV * DW - VW) / denom;

                // Sampled point must be in front of the camera.
                tOnView = max(tOnView, 0.0);

                // Soft fade rather than hard clip when the sample point is behind
                // a wall — gives the beam a "dissolves into the surface" look
                // instead of a knife edge at the depth boundary.
                float wallFade = saturate((sceneT - tOnView) / 1.5);
                if (wallFade <= 0.0) return half4(0, 0, 0, 0);

                float3 P = camPos + V * tOnView;
                float3 toP = P - O;
                float axial = dot(toP, D);
                if (axial <= 0.0) return half4(0, 0, 0, 0);

                float3 radialVec = toP - D * axial;
                float radialDist = length(radialVec);

                // Cone radius at this axial distance. We DO NOT discard at
                // radialDist > coneR — the radial profile fades to zero before
                // the cone edge (via _HaloWidth), so there's no hard silhouette.
                float sinOuter = sqrt(saturate(1.0 - cosOuter * cosOuter));
                float coneR = sinOuter * axial / max(cosOuter, 1e-4);
                float radialT = radialDist / max(coneR, 1e-4);   // 0 on axis, 1 at cone edge

                // --- End fade (axial) ---
                // ABSOLUTE world distance fade — does NOT scale with the raycast
                // beam end. _BeamLength is the fixed visible length in meters at
                // which the beam fades to zero. This is critical: if we scaled the
                // fade relative to beamEnd, a short raycast (looking at a near
                // wall) would compress the entire fade curve, making the close
                // part of the beam dim — and a long raycast (looking far) would
                // stretch the curve, making the close part bright. That's the
                // inversion the player saw.
                float endT = saturate(axial / max(_BeamLength, 0.01));
                float endFade = pow(1.0 - endT, _EndFadePow);

                // --- Two-zone radial profile ---
                // Core: thin bright pillar at the center, falls off inside _CoreWidth.
                float core = smoothstep(_CoreWidth, 0.0, radialT) * _CoreStrength;
                // Halo: soft glow that dies at radialT = _HaloWidth (well before the
                // cone edge at 1.0). pow(1 - x, _HaloPow) gives a long soft tail with
                // no visible edge. Halo fades with endFade^2 so as the beam reaches a
                // surface the wide glow disappears faster than the core.
                float haloT = saturate(radialT / max(_HaloWidth, 1e-4));
                float halo = pow(1.0 - haloT, _HaloPow) * _HaloStrength * endFade;
                float radial = core + halo;

                // --- Axial source-distance falloff ---
                float k = _FlashlightFalloff.x;
                float distF = 1.0 / (1.0 + k * axial);
                distF *= endFade;

                // --- Helmet near-fade ---
                float nearFade = saturate(tOnView / max(_NearFade, 0.0001));

                // Apply endFade an extra time so the second half decays toward zero
                // hard enough to read as "fading out", not just dimmer. With the halo
                // already pre-multiplied by endFade, the final exponents become:
                //   core ~ endFade^2,  halo ~ endFade^3
                float brightness = radial * distF * endFade * nearFade * wallFade;
                half3 rgb = _Color.rgb * _Intensity * brightness;
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
