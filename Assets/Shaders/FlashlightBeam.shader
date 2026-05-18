Shader "SpaceGame/FlashlightBeam"
{
    // Analytical volumetric beam.
    //
    // For each fragment we reconstruct the world-space view ray (camera -> pixel).
    // We then compute the closest distance from that ray to the flashlight axis,
    // and the depth that ray reaches (clipped against scene geometry via the camera
    // depth texture for soft intersection). Brightness is the product of:
    //   - radial term: exp(-density * distFromAxis)   (bright on axis, fades outward)
    //   - axial term:  1 / (1 + k * distFromOrigin)   (bright near source)
    //   - travel term: how far the ray traveled inside the cone, with a soft cap
    //   - near-camera fade so it doesn't pop in the helmet
    //
    // The procedural cone mesh that this is rendered on is just a bounding volume —
    // it limits where the shader runs. Brightness comes from the analytical math,
    // so the mesh silhouette is invisible (no hard cone edge).
    Properties
    {
        _Color           ("Beam Color",          Color)            = (1, 0.95, 0.85, 1)
        _Intensity       ("Intensity",           Range(0, 10))     = 2.5
        _RadialDensity   ("Radial Density",      Range(0.05, 8))   = 1.2
        _AxialFalloff    ("Axial Falloff",       Range(0, 0.2))    = 0.015
        _MaxTravel       ("Max Travel (m)",      Range(1, 500))    = 80
        _NearFade        ("Near Fade Distance",  Range(0, 5))      = 0.6
        _SoftIntersect   ("Soft Intersect (m)",  Range(0.01, 10))  = 2.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        LOD 100

        Pass
        {
            Name "Beam"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One        // pure additive — flashlight beam adds light
            ZWrite Off
            Cull Off             // we want to see the beam from inside the cone too
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // Globals pushed by Flashlight.cs — see Flashlight.hlsl for the contract.
            float4 _FlashlightPos;
            float4 _FlashlightDir;
            float4 _FlashlightColor;
            float4 _FlashlightParams;   // x=cosOuter, y=cosInner, z=range, w=enabled

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float4 screenPos   : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Intensity;
                float  _RadialDensity;
                float  _AxialFalloff;
                float  _MaxTravel;
                float  _NearFade;
                float  _SoftIntersect;
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

            // Closest distance from point P to the infinite line (origin O, dir D unit).
            float PointLineDistance(float3 P, float3 O, float3 D)
            {
                float3 v = P - O;
                float t = dot(v, D);
                float3 proj = O + D * t;
                return length(P - proj);
            }

            // Distance from a ray (cam, V unit) to a line (O, D unit). Used to find how
            // close the view ray passes to the light axis — the bright core of the beam.
            // Returns minimum distance and the t along the view ray where it occurs.
            void RayLineClosest(float3 cam, float3 V, float3 O, float3 D, out float minDist, out float tOnRay)
            {
                float3 w0 = cam - O;
                float a = 1.0;             // dot(V, V)
                float b = dot(V, D);
                float c = 1.0;             // dot(D, D)
                float dd = dot(V, w0);
                float e = dot(D, w0);
                float denom = a * c - b * b;
                float sc, tc;
                if (abs(denom) < 1e-5)
                {
                    // Rays parallel — use any point.
                    sc = 0.0;
                    tc = (b > c ? dd / b : e / c);
                }
                else
                {
                    sc = (b * e - c * dd) / denom;   // along V (view ray)
                    tc = (a * e - b * dd) / denom;   // along D (light axis)
                }
                tOnRay = sc;
                float3 closestOnRay  = cam + V * sc;
                float3 closestOnAxis = O + D * tc;
                minDist = length(closestOnRay - closestOnAxis);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                if (_FlashlightParams.w < 0.5)
                    return half4(0,0,0,0);

                float3 camPos = _WorldSpaceCameraPos;
                float3 V      = IN.positionWS - camPos;
                float  fragDist = length(V);
                V /= max(fragDist, 1e-4);

                float3 O = _FlashlightPos.xyz;
                float3 D = normalize(_FlashlightDir.xyz);

                // --- Soft scene intersection: clip the view ray at scene depth ---
                float2 uv = IN.screenPos.xy / IN.screenPos.w;
                float rawDepth = SampleSceneDepth(uv);
                float sceneEye = LinearEyeDepth(rawDepth, _ZBufferParams);
                // Eye-space depth of THIS fragment (the cone surface):
                float fragEye = -TransformWorldToView(IN.positionWS).z;

                // How close is this fragment to a solid surface behind it?
                // softFade = 0 at the surface, 1 once fully separated.
                float softFade = saturate((sceneEye - fragEye) / max(_SoftIntersect, 0.001));

                // --- Bright core: how close does the view ray pass to the light axis? ---
                float distFromAxis;
                float tNearest;
                RayLineClosest(camPos, V, O, D, distFromAxis, tNearest);

                // Make sure the closest point is actually in front of the source and
                // within range.
                float3 nearestOnAxis = O + D * max(dot((camPos + V * max(tNearest, 0)) - O, D), 0);
                float axialDist = length(nearestOnAxis - O);

                // Radial falloff (Beer-Lambert-ish) — bright on axis, exponential fade.
                float radial = exp(-_RadialDensity * distFromAxis);

                // Axial falloff — bright near source, gentler than inverse-square.
                float axial = 1.0 / (1.0 + _AxialFalloff * axialDist);

                // Range cutoff — don't add light past the flashlight's reach.
                float rangeMask = 1.0 - smoothstep(_FlashlightParams.z * 0.7, _FlashlightParams.z, axialDist);

                // Cone mask — fragment must be inside the spot cone, with a soft edge.
                // Project the closest point on the view ray back to test against the cone.
                float3 hitWS = camPos + V * max(tNearest, fragDist * 0.0);
                float3 dirFromLight = normalize(hitWS - O);
                float cosA = dot(dirFromLight, D);
                float cosOuter = _FlashlightParams.x;
                // Soft cone edge (1 at axis, 0 at outer ring, with squared falloff).
                float coneEdge = saturate((cosA - cosOuter) / max(1.0 - cosOuter, 0.0001));
                coneEdge *= coneEdge;

                // --- Near-camera fade so we don't see the cone tip in the helmet ---
                float nearFade = saturate(fragDist / max(_NearFade, 0.0001));

                // Travel scaling — caps brightness for very deep rays (prevents blowout
                // when looking down the beam axis).
                float travelMask = saturate(fragDist / _MaxTravel);
                travelMask = 1.0 - travelMask * 0.4; // mild dampening, not full cutoff

                float brightness = radial * axial * coneEdge * rangeMask * nearFade * softFade * travelMask;

                half3 rgb = _Color.rgb * _Intensity * brightness;
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
