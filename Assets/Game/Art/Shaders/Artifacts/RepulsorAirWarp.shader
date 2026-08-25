Shader "SpaceGame/Artifacts/RepulsorAirWarp"
{
    // The Repulsor Gauntlet's blast, drawn as a wall of compressed air rather than a painted cone.
    //
    // The shell itself is almost invisible: what you see is the WORLD BEHIND IT being shoved
    // sideways. Each pixel offsets its own screen-space coordinate along a turbulent noise field
    // and re-samples _CameraOpaqueTexture there, so the scene bends around the blast front and
    // springs back as the wave dissipates. A flat additive cone reads as a decal stuck to the
    // camera; a refraction reads as pressure, because the eye has no other way to explain the
    // world moving.
    //
    // The mesh is the procedural cone shell from RepulsorBlastCone.cs: apex at the gauntlet,
    // open rim at the blast front, UV.v = 0 at the apex and 1 at the rim, UV.u wrapping around.
    // _Progress is driven 0 -> 1 over the shot's lifetime by that same component.
    //
    // Note the opaque texture is captured BEFORE the transparent queue, so this refracts opaque
    // geometry only. Other transparents (including a second blast) do not bend — deliberate, and
    // the alternative costs a full extra colour copy per shot.
    Properties
    {
        _Progress      ("Progress",          Range(0, 1))      = 0

        [Header(Refraction)]
        _WarpStrength  ("Warp Strength",     Range(0, 0.15))   = 0.045
        _NoiseScale    ("Noise Scale",       Float)            = 6
        _NoiseSpeed    ("Noise Speed",       Float)            = 3
        _LeadingEdge   ("Leading Edge",      Range(0, 1))      = 0.75
        _BandWidth     ("Front Band Width",  Range(0.02, 1))   = 0.28
        _SkirtStrength ("Trailing Skirt",    Range(0, 1))      = 0.35
        _FlowBias      ("Outward Flow Bias", Range(0, 1))      = 0.6

        [Header(Rim)]
        [HDR] _RimColor ("Rim Color",        Color)            = (0.62, 0.82, 1.0, 1)
        _RimPower      ("Rim Power",         Range(0.5, 8))    = 3
        _RimIntensity  ("Rim Intensity",     Range(0, 8))      = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent+100"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "RepulsorAirWarp"

            // Premultiplied alpha, not straight SrcAlpha/OneMinusSrcAlpha. This pass has to do two
            // different things to the frame at once: REPLACE the scene with the refracted sample
            // (weighted by coverage) and ADD the rim glow on top. With premultiplied blending the
            // rgb channel carries both — coverage-scaled refraction plus unscaled glow — while
            // alpha carries only how much of the original scene to erase. Straight alpha blending
            // would multiply the glow by the coverage too, and the rim would vanish exactly where
            // it is supposed to be brightest: over the thin, low-coverage edges of the shell.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Declares TEXTURE2D_X(_CameraOpaqueTexture), its sampler, its _TexelSize, and
            // SampleSceneColor(). Using URP's own helper rather than hand-rolling the sample is
            // what keeps this correct under XR single-pass instancing (it applies the stereo
            // slice transform) and under dynamic resolution (it clamps to the scaled viewport).
            // Requires "Opaque Texture" on the URP asset, which PC_RPAsset has.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                float4 screenPos   : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _RimColor;
                float _Progress;
                float _WarpStrength;
                float _NoiseScale;
                float _NoiseSpeed;
                float _LeadingEdge;
                float _BandWidth;
                float _SkirtStrength;
                float _FlowBias;
                float _RimPower;
                float _RimIntensity;
            CBUFFER_END

            // Procedural noise on purpose: this material ships with no texture assigned, and a
            // one-shot effect spawned per shot should not drag a texture reference along with it.
            float hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float vnoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = hash13(i + float3(0, 0, 0));
                float n100 = hash13(i + float3(1, 0, 0));
                float n010 = hash13(i + float3(0, 1, 0));
                float n110 = hash13(i + float3(1, 1, 0));
                float n001 = hash13(i + float3(0, 0, 1));
                float n101 = hash13(i + float3(1, 0, 1));
                float n011 = hash13(i + float3(0, 1, 1));
                float n111 = hash13(i + float3(1, 1, 1));

                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                            lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            float turbulence(float3 p)
            {
                return vnoise(p) * 0.65 + vnoise(p * 2.17 + 11.3) * 0.35;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv          = IN.uv;

                // Screen UVs are derived here rather than from SV_Position in the fragment, because
                // ComputeScreenPos gives homogeneous coordinates that interpolate perspective-
                // correctly; dividing by w in the fragment then lands exactly on the pixel this
                // fragment covers. Doing the same job from SV_Position would need _ScreenSize and
                // would still have to be re-fixed for dynamic resolution — SampleSceneColor already
                // handles that half, so keep this half plain.
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float v   = saturate(IN.uv.y);          // 0 apex / trailing edge -> 1 leading rim
                float ang = IN.uv.x * TWO_PI;           // wraps once around the cone

                // Life envelope. The attack is short but not zero — snapping to full strength on
                // frame one pops, and the blast has already travelled a little by then anyway. The
                // decay is the important half: warp and glow must both reach exactly 0 at
                // _Progress = 1, or the cone disappears mid-effect when the component destroys it.
                float birth = smoothstep(0.0, 0.10, _Progress);
                float death = pow(saturate(1.0 - _Progress), 1.5);
                float life  = birth * death;

                // Air only piles up where the shell is actually pushing. A uniform warp over the
                // whole cone reads as a smeared lens on the camera, so the distortion is BANDED:
                // a tight gaussian around _LeadingEdge is the compression front, and a soft skirt
                // trails back toward the apex as the wake behind it. Without the skirt the front
                // looks like a floating ring detached from the gauntlet.
                float d     = (v - _LeadingEdge) / max(_BandWidth, 1e-3);
                float band  = exp2(-d * d * 4.0);
                float skirt = pow(saturate(v / max(_LeadingEdge, 1e-3)), 3.0) * _SkirtStrength;
                float shape = saturate(band + skirt);

                // The cone mesh is an open shell: its rim is a hard geometric edge, and a warp that
                // is still at full strength there terminates in a visible straight cut across the
                // screen. Feather the last sliver of v so the distortion runs out before the
                // triangles do.
                shape *= smoothstep(1.0, 0.94, v);

                float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // A procedurally built mesh may arrive without normals; falling back to the view
                // direction makes fresnel evaluate to 0 rather than producing NaNs.
                float  normalLen = length(IN.normalWS);
                float3 normalWS  = normalLen > 1e-4 ? IN.normalWS / normalLen : viewDirWS;

                // Sampling 3D noise along a CIRCLE in the xy plane is what makes the turbulence
                // seamless around the cone: the path is closed, so u = 0 and u = 1 land on the same
                // point in the noise field. A plain 2D noise over (u, v) would show a hard seam
                // running down one side of the blast. The z axis carries v plus scrolling time, so
                // the turbulence rushes outward along the cone instead of sitting still on it.
                float  t  = _Time.y * _NoiseSpeed;
                float3 np = float3(cos(ang), sin(ang), 0.0) * _NoiseScale
                          + float3(0.0, 0.0, v * _NoiseScale * 2.0 - t);

                float n1 = turbulence(np);
                float n2 = turbulence(np + 37.71);

                // Refraction direction: mostly the shell's own outward direction in VIEW space, so
                // the world visibly bulges away from the blast like a lens, jittered by the noise
                // so the edge boils instead of sliding as one clean sheet.
                float2 outward = TransformWorldToViewDir(normalWS, true).xy;
                float2 jitter  = float2(n1, n2) * 2.0 - 1.0;
                float2 raw     = outward * _FlowBias + jitter * (1.0 - _FlowBias);
                float2 dir     = raw / max(length(raw), 1e-4);

                float2 offset = dir * (_WarpStrength * shape * life * (0.55 + 0.45 * n1));

                // Offsets are in screen UV, where one unit of x and one unit of y are different
                // numbers of pixels. Dividing x by the aspect ratio keeps the warp isotropic
                // instead of stretching it horizontally on a wide monitor.
                offset.x /= max(_ScreenParams.x / max(_ScreenParams.y, 1.0), 1e-4);

                float2 screenUv = IN.screenPos.xy / max(IN.screenPos.w, 1e-4);
                float3 refracted = SampleSceneColor(screenUv + offset);

                // Coverage is how much of the frame this pixel replaces with its displaced sample.
                float coverage = saturate(shape * life);

                // In a material preview, an inspector thumbnail, or any camera rendering without
                // the opaque pass, _CameraOpaqueTexture is unbound: its _TexelSize comes back as
                // zero or as a 4x4 default, and SampleSceneColor returns flat black. Compositing
                // that would paint a solid black cone. Gate the refraction on a plausible texture
                // width so those cases fall back to the rim glow alone.
                coverage *= step(8.0, _CameraOpaqueTexture_TexelSize.z);

                // Grazing-angle glow. abs() on the dot because Cull is Off — back faces arrive with
                // their normals pointing away and would otherwise fresnel to a constant. This is
                // the only part of the effect visible against a flat sky or on a dark night, where
                // there is no detail behind the shell for the refraction to bend, so it cannot be
                // dropped — but it stays faint, because the warp is the effect and this is insurance.
                float  fresnel = pow(1.0 - saturate(abs(dot(normalWS, viewDirWS))), _RimPower);
                float3 glow    = _RimColor.rgb * (_RimIntensity * fresnel * life * (0.55 + 0.45 * shape));

                // Premultiplied output: rgb already carries its own alpha weighting.
                float3 colour = refracted * coverage + glow;

                // Nothing to composite and nothing to add: skip the blend entirely. Peak channel
                // rather than a luminance weighting — this is a visibility test, not a colour
                // conversion, and the rim is a saturated blue whose luminance undersells it.
                if (coverage < 0.002 && max(glow.r, max(glow.g, glow.b)) < 0.002)
                    discard;

                return half4(colour, coverage);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
