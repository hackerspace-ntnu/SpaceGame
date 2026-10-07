// Condensation mist that a lamp can light: the airlock's jets and fog (ColonyInterior.md).
//
// WHY A TEXTURE, WHEN JETSMOKE HAS NONE. JetSmoke's procedural disc is fine for a trail seen at a
// distance; filling a 3 m chamber around the player it read as flat, evenly-lit blobs with straight
// edges. Real-time smoke that holds up close is a baked flipbook (the puff rolls between frames),
// lit, and softened where it meets geometry. Those are the four things this shader adds:
//
//   1. SIX-WAY LIGHTING. tools/mist_flipbook.py raymarches a billow volume and renders every frame
//      lit from +X, +Y, +Z, -X, -Y and -Z (Unity's six-way smoke technique, as in VFX Graph). Here
//      the light's direction is taken into the particle's own quad frame and the six maps are
//      blended by its squared components, so a flat card shades like a volume: lit on the lamp's
//      side, shadowed on the far one, and glowing through its thin parts when the lamp is behind it.
//      Every additional light counts (Forward+ cluster loop), which is what makes the amber beacon
//      sweep visibly through the fog.
//   2. FLIPBOOK FRAME BLENDING. The Texture Sheet Animation module hands two frames and a blend
//      factor down the vertex streams (UV, UV2, AnimBlend), so 64 frames play without stepping.
//   3. ALPHA EROSION, NOT ONLY FADE. The particle's colour alpha (its Colour over Lifetime) is spent
//      as an erosion threshold: the thin edge and the detail noise in the negative map's alpha go
//      first, the dense core last, so a jet's puff breaks into wisps instead of turning uniformly
//      glassy. _Erosion blends that with a plain fade, because a fog layer wants the opposite: eroded
//      at low alpha it shrinks to hard little cores, the "cotton ball" look; faded it thins evenly.
//   4. SOFT PARTICLES AND A NEAR FADE. Alpha fades with the distance to the scene depth behind the
//      pixel, so a card cutting a wall, the floor or the hatch shows no line; and it fades out close
//      to the camera, so a player standing in the fog never has a quad pasted over the lens.
//
// VERTEX STREAMS, IN THIS ORDER, or the mist draws garbage or nothing — silently:
//   Position, Normal, Colour, UV, UV2, AnimBlend, Tangent   (ParticleSystemRenderer.SetActiveVertexStreams)
// Normal and Tangent are the quad's own frame (a stretched jet streak turns with its velocity).
//
// ALPHA BLENDED, like JetSmoke: condensation is matter that hides what is behind it, and an
// additive mist would glow on its own in a dark chamber.
Shader "SpaceGame/Effects/SixWayMist"
{
    Properties
    {
        [NoScaleOffset] _MistPositive ("Six-way +X +Y -Z, coverage (linear)", 2D) = "black" {}
        [NoScaleOffset] _MistNegative ("Six-way -X -Y +Z, erosion detail (linear)", 2D) = "black" {}

        _MistTint      ("Tint",                     Color)          = (0.92, 0.94, 0.96, 1)
        _MistOpacity   ("Opacity",                  Range(0, 2))    = 1
        _DirectGain    ("Direct Light",             Range(0, 4))    = 1
        // The sun's share. Indoor mist wants 0: a shadow map does not seal a room, so sunlight leaks onto
        // the cards through any wall or ceiling that casts no shadow, as stepped bright bands.
        _MainLightGain ("Sun (Main Light)",         Range(0, 1))    = 1
        _BacklightGain ("Backlight (transmission)", Range(0, 4))    = 1.4
        _AmbientGain   ("Ambient Light",            Range(0, 4))    = 1

        [Header(Erosion)]
        // How the particle's colour alpha is spent: 0 fades the whole puff evenly (a fog that thins), 1 eats it
        // from the edges inward (a jet's billow breaking into wisps). Fog and jets want different amounts.
        _Erosion         ("Erode vs Fade",    Range(0, 1))    = 0.7
        _ErosionSoftness ("Erosion Softness", Range(0.01, 1)) = 0.3
        _DetailBite      ("Detail Bite",      Range(0, 1))    = 0.5

        [Header(Fades)]
        _SoftFadeDistance ("Soft Particle Distance (m)", Range(0.01, 4)) = 0.6
        _NearFadeStart    ("Near Fade Start (m)",        Range(0, 4))    = 0.3
        _NearFadeEnd      ("Near Fade End (m)",          Range(0.01, 8)) = 1.5
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType"     = "Plane"
        }

        Pass
        {
            Name "SixWayMist"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MistPositive); SAMPLER(sampler_MistPositive);
            TEXTURE2D(_MistNegative); SAMPLER(sampler_MistNegative);

            CBUFFER_START(UnityPerMaterial)
                float4 _MistTint;
                float _MistOpacity;
                float _DirectGain;
                float _MainLightGain;
                float _BacklightGain;
                float _AmbientGain;
                float _Erosion;
                float _ErosionSoftness;
                float _DetailBite;
                float _SoftFadeDistance;
                float _NearFadeStart;
                float _NearFadeEnd;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float4 uvs        : TEXCOORD0;   // xy = this frame, zw = the next (UV2)
                float  animBlend  : TEXCOORD1;   // how far between the two
                float4 tangentOS  : TANGENT;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float4 uvs        : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float3 tangentWS  : TEXCOORD3;
                float4 screenPos  : TEXCOORD4;
                float  animBlend  : TEXCOORD5;
                float  fogFactor  : TEXCOORD6;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.tangentWS  = TransformObjectToWorldDir(IN.tangentOS.xyz);
                OUT.screenPos  = ComputeScreenPos(pos.positionCS);
                OUT.color      = IN.color;
                OUT.uvs        = IN.uvs;
                OUT.animBlend  = IN.animBlend;
                OUT.fogFactor  = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            // The six lightmaps, already blended between the two flipbook frames.
            struct SixWay
            {
                float3 positive;   // lit from +X (right), +Y (up), -Z (behind the card)
                float3 negative;   // lit from -X (left),  -Y (down), +Z (the viewer's side)
            };

            // Six-way response to one light arriving from direction L (toward the light), with the
            // card's frame (right, up, toward viewer). Squared components sum to one, so a light
            // never counts twice; the sign picks which of each pair of maps answers.
            float SixWayResponse(SixWay maps, float3 L, float3 right, float3 up, float3 facing)
            {
                float3 d = float3(dot(L, right), dot(L, up), dot(L, facing));
                float3 w = d * d;
                float x = d.x > 0.0 ? maps.positive.x : maps.negative.x;
                float y = d.y > 0.0 ? maps.positive.y : maps.negative.y;
                float z = d.z > 0.0 ? maps.negative.z : maps.positive.z * _BacklightGain;
                return x * w.x + y * w.y + z * w.z;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float4 posA = SAMPLE_TEXTURE2D(_MistPositive, sampler_MistPositive, IN.uvs.xy);
                float4 posB = SAMPLE_TEXTURE2D(_MistPositive, sampler_MistPositive, IN.uvs.zw);
                float4 negA = SAMPLE_TEXTURE2D(_MistNegative, sampler_MistNegative, IN.uvs.xy);
                float4 negB = SAMPLE_TEXTURE2D(_MistNegative, sampler_MistNegative, IN.uvs.zw);
                float4 positive = lerp(posA, posB, IN.animBlend);
                float4 negative = lerp(negA, negB, IN.animBlend);

                // ── Coverage, eroded by age ──────────────────────────────────────
                float coverage = positive.a;
                float eroded = coverage * lerp(1.0, negative.a, _DetailBite);
                float erosion = 1.0 - IN.color.a;
                float eaten = saturate((eroded - erosion * (1.0 + _ErosionSoftness)) / _ErosionSoftness);
                float alpha = coverage * lerp(IN.color.a, eaten, _Erosion);

                // ── Soft particles and the near fade ─────────────────────────────
                float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 1e-4);
                float eyeDepth = IN.screenPos.w;
                float rawDepth = SampleSceneDepth(screenUV);
                // An unbound depth texture samples white, which under reversed Z is the NEAR plane:
                // with no depth to compare against, keep the mist rather than erasing it.
                #if UNITY_REVERSED_Z
                    bool noDepth = rawDepth >= 0.9999;
                #else
                    bool noDepth = rawDepth <= 0.0001;
                #endif
                float sceneDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float soft = noDepth ? 1.0 : saturate((sceneDepth - eyeDepth) / _SoftFadeDistance);
                float nearFade = saturate((eyeDepth - _NearFadeStart) / max(_NearFadeEnd - _NearFadeStart, 1e-3));
                alpha *= soft * nearFade * _MistOpacity;
                if (alpha <= 0.003) discard;

                // ── Light ────────────────────────────────────────────────────────
                SixWay maps;
                maps.positive = positive.rgb;
                maps.negative = negative.rgb;

                float3 facing = normalize(IN.normalWS);
                float3 right = normalize(IN.tangentWS - facing * dot(IN.tangentWS, facing));
                float3 up = cross(right, facing);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float3 lit = mainLight.color * mainLight.shadowAttenuation * _MainLightGain
                           * SixWayResponse(maps, mainLight.direction, right, up, facing);

            #ifdef _ADDITIONAL_LIGHTS
                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = facing;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.normalizedScreenSpaceUV = screenUV;

                uint addCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(addCount)
                    Light addLight = GetAdditionalLight(lightIndex, IN.positionWS);
                    lit += addLight.color * addLight.distanceAttenuation
                         * SixWayResponse(maps, addLight.direction, right, up, facing);
                LIGHT_LOOP_END
            #endif

                // Ambient reaches the puff from every side: the mean of the six maps.
                float meanMap = (dot(maps.positive, 1.0) + dot(maps.negative, 1.0)) / 6.0;
                float3 ambient = SampleSH(float3(0.0, 1.0, 0.0)) * meanMap;

                float3 colour = _MistTint.rgb * IN.color.rgb * (lit * _DirectGain + ambient * _AmbientGain);
                colour = MixFog(colour, IN.fogFactor);
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
