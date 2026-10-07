// Portal fluid — the stuff in the gun's reservoirs, in its muzzle iris, and in
// the blob it throws.
//
// The brief was a fire extinguisher that carries portal fluid instead of foam,
// so this had to look like a liquid under pressure rather than like a lamp: it
// churns, it has a surface level with a meniscus at it, it settles when idle
// and boils when the trigger is pulled. Domain-warped noise gives the churn;
// the fake gradient lighting underneath is what stops it reading as a flat
// gradient with static on it.
//
// Sampled in OBJECT space on purpose. A reservoir is a cylinder held in a
// moving hand, and screen-space or world-space noise would make the fluid swim
// against its own container every time the player turned around.
//
// Opaque, not transparent. The columns are solid geometry inside a cage of
// chrome rods; making them transparent buys nothing but sorting bugs, and the
// glass version of this model was already rejected for being invisible.
Shader "SpaceGame/Portal/PortalFluid"
{
    Properties
    {
        _ColourDeep   ("Deep colour", Color) = (0.12, 0.42, 0.85, 1)
        _ColourBright ("Bright colour", Color) = (0.55, 0.90, 1.00, 1)
        _ColourVapour ("Headspace colour", Color) = (0.05, 0.06, 0.08, 1)

        _Emission     ("Emission", Range(0.0, 8.0)) = 2.0
        _Contrast     ("Churn contrast", Range(0.5, 6.0)) = 2.2

        _Scale        ("Churn scale", Range(0.5, 40.0)) = 11.0
        _Speed        ("Churn speed", Range(0.0, 4.0)) = 0.55
        _Warp         ("Domain warp", Range(0.0, 3.0)) = 1.1

        // 0..1 of the way up _FillAxis. Driven by PortalGunItem so a spent gun
        // and a charged one are the same material with a different number.
        _Fill         ("Fill level", Range(0.0, 1.0)) = 1.0
        _FillAxis     ("Fill axis (object space)", Vector) = (0, 0, 1, 0)

        // Where the reservoir actually starts and ends along that axis, in
        // object space. Needed because a mesh's own extent is not knowable in a
        // shader, and guessing -0.5..0.5 puts the whole column inside the
        // filled half for any model whose origin is not its centre — which is
        // every model in this library, since the convention is origin-at-base.
        _FillMin      ("Fill axis start", Float) = -0.5
        _FillMax      ("Fill axis end", Float) = 0.5
        _Meniscus     ("Meniscus brightness", Range(0.0, 6.0)) = 2.6

        // Spiked by PortalGunItem on the frame a portal is fired, then decayed.
        _Agitation    ("Agitation", Range(0.0, 4.0)) = 0.0

        _Fresnel      ("Rim brightness", Range(0.0, 4.0)) = 1.3
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "PortalFluid"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "PortalNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ColourDeep;
                float4 _ColourBright;
                float4 _ColourVapour;
                float4 _FillAxis;
                float  _Emission;
                float  _Contrast;
                float  _Scale;
                float  _Speed;
                float  _Warp;
                float  _Fill;
                float  _FillMin;
                float  _FillMax;
                float  _Meniscus;
                float  _Agitation;
                float  _Fresnel;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionOS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = pos.positionCS;
                OUT.positionWS  = pos.positionWS;
                OUT.positionOS  = IN.positionOS.xyz;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 axis = normalize(_FillAxis.xyz + float3(0, 0, 1e-6));
                float  along = dot(IN.positionOS, axis);

                // Two axes perpendicular to the fill axis, so the churn is
                // sampled around the column rather than along it. Built from
                // whichever world axis is least parallel to the fill axis,
                // which keeps this stable for any orientation the mesh has.
                float3 helper = abs(axis.z) < 0.9 ? float3(0, 0, 1) : float3(1, 0, 0);
                float3 u = normalize(cross(axis, helper));
                float3 v = cross(axis, u);

                float agitation = max(_Agitation, 0.0);
                float t = _Time.y * (_Speed + agitation * 1.8);

                float2 uv = float2(dot(IN.positionOS, u), dot(IN.positionOS, v) * 0.35
                                                          + along * 1.6) * _Scale;

                float n = PortalWarpedFbm(uv + float2(0.0, -t * 0.4), t,
                                          _Warp * (1.0 + agitation * 0.6));
                n = saturate((n - 0.5) * _Contrast + 0.5);

                // Fake lighting from the noise's own gradient. Cheaper and more
                // controllable than a normal map, and it is what makes the
                // fluid look like it has volume instead of being a pattern.
                float eps = 0.06;
                float dx = PortalWarpedFbm(uv + float2(eps, -t * 0.4), t, _Warp) - n;
                float dy = PortalWarpedFbm(uv + float2(0.0, eps - t * 0.4), t, _Warp) - n;
                float lobe = saturate(0.5 + (dx - dy) * 3.0);

                float3 fluid = lerp(_ColourDeep.rgb, _ColourBright.rgb,
                                    saturate(n * 0.75 + lobe * 0.45));

                // Where the level sits, as a fraction of the reservoir's own
                // declared extent along the fill axis.
                float height = saturate((along - _FillMin) / max(_FillMax - _FillMin, 1e-4));
                float ripple = (PortalFbm(uv * 0.35 + float2(t * 0.6, 0.0), 3) - 0.5)
                             * (0.012 + agitation * 0.03);
                float surface = _Fill + ripple;

                float submerged = step(height, surface);
                float meniscus = saturate(1.0 - abs(height - surface) / 0.035);
                meniscus *= step(0.02, _Fill) * step(_Fill, 0.98);

                float3 col = lerp(_ColourVapour.rgb, fluid, submerged);
                col += _ColourBright.rgb * pow(meniscus, 2.0) * _Meniscus;

                // Rim light so the column reads as a cylinder of liquid rather
                // than a flat billboard when it is seen edge-on in the hand.
                float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float f = pow(1.0 - saturate(dot(normalize(IN.normalWS), viewDir)), 3.0);
                col += _ColourBright.rgb * f * _Fresnel * submerged;

                col *= _Emission * (1.0 + agitation * 0.5);

                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // Without this the fluid columns cast no shadow and the gun looks
        // like it has holes in it under a hard sun.
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
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            float4 ShadowVert(ShadowAttributes IN) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                float4 positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, _LightDirection));

            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }

    Fallback Off
}
