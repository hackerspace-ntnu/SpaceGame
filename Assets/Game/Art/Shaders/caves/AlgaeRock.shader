Shader "SpaceGame/AlgaeRock"
{
    Properties
    {
        [Header(Rock)]
        _RockColor       ("Rock Color", Color)         = (0.06, 0.055, 0.05, 1)
        _BlendSharpness  ("Triplanar Blend Sharpness", Range(1, 16)) = 4
        _Smoothness      ("Smoothness", Range(0, 1))   = 0
        _Metallic        ("Metallic",   Range(0, 1))   = 0

        [Header(Algae Patches  Foundational Structures)]
        _PatchScale      ("Patch Scale (smaller = bigger patches)", Range(0.02, 1)) = 0.18
        _PatchThreshold  ("Patch Threshold (lower = more coverage)", Range(0, 1))   = 0.5
        _PatchFalloff    ("Patch Falloff Power (higher = denser middle)", Range(0.5, 6)) = 2.0

        [Header(Network Structure  Coarse Veins to Fine Branches)]
        _NetworkDensity  ("Trunk Cell Density (low = bigger main veins)", Range(2, 20)) = 6
        _NetworkWidth    ("Trunk Width", Range(0.0, 0.6))      = 0.28
        _NetworkWarp     ("Trunk Flow Distortion", Range(0, 3)) = 1.8
        _BranchDensity   ("Branch Cell Density", Range(5, 50)) = 22
        _BranchWidth     ("Branch Width", Range(0.0, 0.6))     = 0.15
        _BranchStrength  ("Branch vs Trunk Mix", Range(0, 1))  = 0.6

        [Header(Microscopic Dots)]
        _DotScale        ("Dot Scale (higher = smaller dots)", Range(20, 300)) = 110
        _DotSize         ("Dot Size", Range(0, 1)) = 0.42
        _DotFadeStart    ("Dot Fade Start (m)", Range(0, 50))  = 5
        _DotFadeEnd      ("Dot Fade End (m)",   Range(1, 100)) = 16

        [Header(Algae Colors  Picked Per Dot)]
        _AlgaeColorA     ("Algae Color A (Blue)",  Color) = (0.20, 0.55, 1.00, 1)
        _AlgaeColorB     ("Algae Color B (Green)", Color) = (0.25, 1.00, 0.55, 1)
        _ColorBalance    ("Color Balance (0 = all A, 1 = all B)", Range(0, 1)) = 0.5
        _AlbedoTintStrength ("Albedo Tint Strength", Range(0, 1)) = 0.5

        [Header(Glow  Only A Few Light Up)]
        _GlowingPatchFraction ("Fraction of Patches With Glow", Range(0, 1)) = 0.7
        _GlowingSectionFraction ("Fraction of Sections (within glow patch) That Glow", Range(0, 1)) = 0.25
        _GlowingDotFraction ("Fraction of Dots (within glow section) That Emit", Range(0, 1)) = 0.3
        _GlowIntensity   ("Glow Intensity (HDR) - the few bright ones", Range(0, 30)) = 8
        _BaselineGlow    ("Baseline Glow - ALL algae always visible", Range(0, 5)) = 1.2
        _PulseSpeed      ("Pulse Speed", Range(0, 2)) = 0.5
        _PulseAmount     ("Pulse Amount (0..1)", Range(0, 1)) = 0.4
        _DistantGlowMul  ("Distant Glow Multiplier", Range(0, 1)) = 0.25

        [Header(Clusters  Small Scale Rosettes)]
        _ClusterScale    ("Cluster Cell Density (centimeter-scale clumps)", Range(20, 200)) = 70
        _ClusterRadius   ("Cluster Radius (0..0.5)", Range(0.05, 0.5)) = 0.32
        _ClusterStrength ("Cluster Mask Strength (0 = off, 1 = only clusters)", Range(0, 1)) = 0.7

        [Header(Lighting)]
        _AmbientBoost    ("Ambient Boost (cave fill)", Range(0, 1)) = 0.02
        _LightWrap       ("Light Wrap", Range(0, 1))                = 0.0
        _SkyAmbientStrength ("Sky/SH Ambient Strength", Range(0, 1)) = 0.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "../Flashlight.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _RockColor;
                float  _BlendSharpness, _Smoothness, _Metallic;

                float  _PatchScale, _PatchThreshold, _PatchFalloff;

                float  _NetworkDensity, _NetworkWidth, _NetworkWarp;
                float  _BranchDensity, _BranchWidth, _BranchStrength;

                float  _DotScale, _DotSize, _DotFadeStart, _DotFadeEnd;

                float4 _AlgaeColorA, _AlgaeColorB;
                float  _ColorBalance, _AlbedoTintStrength;

                float  _GlowingPatchFraction, _GlowingSectionFraction, _GlowingDotFraction;
                float  _GlowIntensity, _BaselineGlow, _PulseSpeed, _PulseAmount, _DistantGlowMul;
                float  _ClusterScale, _ClusterRadius, _ClusterStrength;

                float  _AmbientBoost, _LightWrap, _SkyAmbientStrength;
            CBUFFER_END

            // ---- Interaction-wave globals (set by AlgaePulse.cs) ----
            // _AlgaePulseOrigins[i].xyz = world position of impact, .w = start time
            // _AlgaePulseParams[i]  .x = active (0/1), .y = end time, .z = patchId
            // _AlgaePulseShape       .x = wave speed (m/s), .y = thickness, .z = intensity
            #define ALGAE_MAX_WAVES 4
            float4 _AlgaePulseOrigins[ALGAE_MAX_WAVES];
            float4 _AlgaePulseParams [ALGAE_MAX_WAVES];
            float4 _AlgaePulseShape;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float fogFactor : TEXCOORD2; };

            // ---- Hash / noise utilities ----
            float hash13(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }
            float3 hash33(float3 p) { return float3(hash13(p + 1.7), hash13(p + 11.3), hash13(p + 27.9)); }

            float vnoise(float3 p)
            {
                float3 i = floor(p), f = frac(p), u = f * f * (3.0 - 2.0 * f);
                float n000 = hash13(i + float3(0,0,0)), n100 = hash13(i + float3(1,0,0));
                float n010 = hash13(i + float3(0,1,0)), n110 = hash13(i + float3(1,1,0));
                float n001 = hash13(i + float3(0,0,1)), n101 = hash13(i + float3(1,0,1));
                float n011 = hash13(i + float3(0,1,1)), n111 = hash13(i + float3(1,1,1));
                return lerp(lerp(lerp(n000,n100,u.x), lerp(n010,n110,u.x), u.y),
                            lerp(lerp(n001,n101,u.x), lerp(n011,n111,u.x), u.y), u.z);
            }
            float fbm(float3 p)
            {
                float v = 0, a = 0.5;
                for (int i = 0; i < 4; i++) { v += a * vnoise(p); p *= 2.03; a *= 0.5; }
                return v;
            }

            // Single-level Worley F2-F1 — returns (mass, closestCellId) for a given cell scale.
            // Helper used by evaluateNetwork below to build hierarchical structures.
            void worleyEdge(float3 worldP, float scale, float warpAmt, float width, out float mass, out float3 cellId)
            {
                // Domain warp so cells flow along noise gradients instead of an axis grid —
                // this is what turns "static" cells into curving, cooperating veins.
                float3 warp = float3(fbm(worldP * scale * 0.7),
                                     fbm(worldP * scale * 0.7 + 11.3),
                                     fbm(worldP * scale * 0.7 + 27.7)) - 0.5;
                float3 wp = worldP + warp * (1.5 / max(scale, 0.0001)) * warpAmt;

                float3 base = floor(wp * scale);
                float3 fp   = frac(wp * scale);

                float f1 = 10.0, f2 = 10.0;
                float3 closest = base;
                [unroll] for (int z = -1; z <= 1; z++)
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float3 off  = float3(x, y, z);
                    float3 cId  = base + off;
                    float3 jit  = hash33(cId);
                    float3 d    = off + jit - fp;
                    float dl    = length(d);
                    if (dl < f1)      { f2 = f1; f1 = dl; closest = cId; }
                    else if (dl < f2) { f2 = dl; }
                }

                float edge = f2 - f1;
                mass   = 1.0 - smoothstep(width * 0.4, width, edge);
                cellId = closest;
            }

            // Hierarchical network: coarse "trunk" veins + finer "branches" that follow them.
            // Returns total mass and the TRUNK cell id (used for per-section glow gating — branches
            // share the trunk id so glow is coherent along a connected vein, not noisy per-branch).
            //
            // The trick that makes it look like a network rather than two unrelated layers:
            // branches are sampled at a position shifted toward the nearest trunk cell, so they
            // tend to cluster along trunks and form Y-junctions instead of crossing randomly.
            void evaluateNetwork(float3 worldP, out float networkMass, out float3 sectionId)
            {
                float trunkScale = _NetworkDensity * 0.012;
                float branchScale = _BranchDensity * 0.012;

                float trunkMass; float3 trunkId;
                worleyEdge(worldP, trunkScale, _NetworkWarp, _NetworkWidth, trunkMass, trunkId);

                // Sample branches at a SHIFTED position — biased toward the trunk-cell center
                // so branch network coordinates align with trunk structure → branches grow from trunks.
                float3 trunkCenter = (trunkId + 0.5) / max(trunkScale, 0.0001);
                float3 toTrunk = trunkCenter - worldP;
                float3 branchSamplePos = worldP + toTrunk * 0.15;

                float branchMass; float3 branchId;
                worleyEdge(branchSamplePos, branchScale, _NetworkWarp * 0.6, _BranchWidth, branchMass, branchId);

                // Combine: trunk is always visible; branches add density, weighted by _BranchStrength.
                // max() instead of sum() so widths don't accumulate visually into thick blobs.
                networkMass = max(trunkMass, branchMass * _BranchStrength);

                // Density modulation so some areas are dense, others sparse — cooperation, not uniform fill.
                networkMass *= smoothstep(0.25, 0.7, fbm(worldP * trunkScale * 2.0));

                sectionId = trunkId;
            }

            // High-frequency Worley dot mask — turns filaments into a constellation of dots.
            // Returns (dotMaskFaded for albedo, dotMaskStrict for emission, dotCellId for per-dot glow gating).
            void evaluateDotMask(float3 worldP, out float dotMaskFaded, out float dotMaskStrict, out float3 dotCellId)
            {
                float3 base = floor(worldP * _DotScale);
                float3 fp   = frac(worldP * _DotScale);
                float dotF1 = 10.0;
                float3 closest = base;
                [unroll] for (int z = -1; z <= 1; z++)
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float3 off = float3(x, y, z);
                    float3 cId = base + off + 53.1;
                    float3 jit = hash33(cId);
                    float3 d   = off + jit - fp;
                    float dl   = length(d);
                    if (dl < dotF1) { dotF1 = dl; closest = base + off; }
                }
                dotCellId = closest;

                float strictMask = 1.0 - smoothstep(_DotSize * 0.5, _DotSize, dotF1);
                dotMaskStrict = strictMask;

                // Albedo path fades dot mask toward 1 at distance so the painted thread shape
                // remains visible when individual dots become sub-pixel — kills flicker.
                float distToCam = distance(GetCameraPositionWS(), worldP);
                float fadeT = saturate((distToCam - _DotFadeStart) / max(_DotFadeEnd - _DotFadeStart, 0.001));
                dotMaskFaded = lerp(strictMask, 1.0, fadeT);
            }

            // Cluster mask: Worley F1 against a fine grid produces soft circular blobs at
            // centimeter scale. Each blob is one "rosette" / "colony" of algae — dots only
            // appear inside these blobs, so the dot field reads as a population of small
            // clumps (centimeters across) instead of evenly-peppered noise. The blob spacing
            // is jittered, so clumps form small groups rather than a regular pattern.
            float evaluateClusterMask(float3 worldP)
            {
                float3 base = floor(worldP * _ClusterScale);
                float3 fp   = frac(worldP * _ClusterScale);
                float f1 = 10.0;
                [unroll] for (int z = -1; z <= 1; z++)
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float3 off = float3(x, y, z);
                    float3 cId = base + off + 37.7;
                    float3 jit = hash33(cId);
                    float3 d   = off + jit - fp;
                    float dl   = length(d);
                    if (dl < f1) f1 = dl;
                }
                // Soft blob: 1 at cell center, 0 past radius.
                float blob = 1.0 - smoothstep(_ClusterRadius * 0.6, _ClusterRadius, f1);
                // Strength = how much the cluster mask gates the dots. 0 = no clustering
                // (dots everywhere on the network), 1 = dots only inside cluster blobs.
                return lerp(1.0, blob, _ClusterStrength);
            }

            // Per-dot algae color — each microscopic dot independently picks A or B.
            // Hashed off the dot cell id so colors stay stable per-dot but mix together
            // at the dot scale (no large uniform color zones).
            float3 algaeColorPerDot(float3 dotCellId)
            {
                float pick = hash13(dotCellId + 91.7);
                float t = step(_ColorBalance, pick); // 0 → A, 1 → B
                return lerp(_AlgaeColorA.rgb, _AlgaeColorB.rgb, t);
            }

            // Sample all active interaction waves and return (mass, intensityMul).
            //   waveMass:      0..1 — how strongly the wavefront overlaps this fragment (banded).
            //   waveIntensity: extra HDR emission to add inside the wavefront.
            //
            // Scoping: the wave is naturally a spherical band expanding from the impact point. It
            // dies after waveLifetime seconds, so `maxRadius = waveSpeed * waveLifetime` caps how
            // far it can spread. We DON'T match patchId — float rounding between CPU and GPU made
            // hash matching unreliable. Geometric falloff handles scoping fine.
            //
            // worldP is the fragment world position. fragPatchId is unused now (kept for API stability).
            void evaluateWaves(float3 worldP, float fragPatchId, out float waveMass, out float waveIntensity)
            {
                waveMass = 0.0;
                waveIntensity = 0.0;
                float speed     = _AlgaePulseShape.x;
                float thickness = _AlgaePulseShape.y;
                float intensity = _AlgaePulseShape.z;

                [unroll] for (int i = 0; i < ALGAE_MAX_WAVES; i++)
                {
                    float active = _AlgaePulseParams[i].x;
                    if (active < 0.5) continue;

                    float3 origin = _AlgaePulseOrigins[i].xyz;
                    float  start  = _AlgaePulseOrigins[i].w;
                    float  end    = _AlgaePulseParams[i].y;

                    float elapsed = _Time.y - start;
                    if (elapsed < 0.0) continue;

                    float radius = elapsed * speed;
                    float dist   = distance(worldP, origin);

                    // Wavefront band: bright at the leading edge, fading inward over `thickness`.
                    float band = saturate(1.0 - abs(dist - radius) / max(thickness, 0.001));

                    // Fade out the whole wave smoothly as it nears end time so it doesn't pop off.
                    float life = saturate((end - _Time.y) / max(end - start, 0.001));
                    band *= life;

                    waveMass      = max(waveMass, band);
                    waveIntensity = max(waveIntensity, band * intensity);
                }
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   vni = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS = vpi.positionCS;
                OUT.positionWS = vpi.positionWS;
                OUT.normalWS   = vni.normalWS;
                OUT.fogFactor  = ComputeFogFactor(vpi.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);

                // Triplanar weights — used for the rock base; algae layer is fully 3D so it
                // doesn't need triplanar projection, it just samples in world space.
                float3 absN = pow(abs(N), _BlendSharpness);
                float3 w    = absN / (absN.x + absN.y + absN.z + 1e-5);

                float3 albedo = _RockColor.rgb;

                // ---- Patch layer: big slow regions that define WHERE algae lives ----
                float patchNoise = fbm(IN.positionWS * _PatchScale);
                float patchMass  = saturate((patchNoise - _PatchThreshold) / max(1.0 - _PatchThreshold, 0.001));
                patchMass = pow(patchMass, _PatchFalloff);

                // Per-patch id — coarse hash so the whole patch shares it. Gates patch-level glow.
                float3 patchCellId = floor(IN.positionWS * _PatchScale * 0.5);
                float  patchId     = hash13(patchCellId + 3.3);
                float  patchHasGlow = step(1.0 - _GlowingPatchFraction, patchId);

                // ---- Network layer: hierarchical trunk + branch vein structure inside patches ----
                float networkMass; float3 sectionId;
                evaluateNetwork(IN.positionWS, networkMass, sectionId);
                networkMass *= patchMass;

                // ---- Microscopic dot layer: renders networks as dots, not lines ----
                float dotMaskFaded, dotMaskStrict; float3 dotCellId;
                evaluateDotMask(IN.positionWS, dotMaskFaded, dotMaskStrict, dotCellId);

                // ---- Cluster layer: centimeter-scale clumps so dots form rosettes, not random pepper ----
                float clusterMask = evaluateClusterMask(IN.positionWS);

                // Effective algae masks (network defines macro region, cluster defines micro clumps, dots define points)
                float algaeAlbedoMass   = networkMass * clusterMask * dotMaskFaded;   // smooth at distance, no flicker
                float algaeEmissionMass = networkMass * clusterMask * dotMaskStrict;  // strict per-dot — never smears

                // ---- Color (per-dot blue/green pick) ----
                float3 aColor = algaeColorPerDot(dotCellId);
                albedo = lerp(albedo, aColor, algaeAlbedoMass * _AlbedoTintStrength);

                // ---- Glow gating (patch × section × dot) — picks the *bright* ones ----
                float sectionHasGlow = step(1.0 - _GlowingSectionFraction, hash13(sectionId + 23.7));
                float dotHasGlow     = step(1.0 - _GlowingDotFraction,     hash13(dotCellId + 71.3));
                float brightGate     = patchHasGlow * sectionHasGlow * dotHasGlow;

                // ---- Interaction waves ----
                // When a wave is active and its patchId matches this fragment's patchId, all algae
                // in the patch light up — overrides the bright gate. The wave expands outward as a
                // band, so you see a ring sweep across the patch.
                float waveMass, waveIntensity;
                evaluateWaves(IN.positionWS, patchId, waveMass, waveIntensity);

                // Distance fade for glow — dim & freeze pulse at distance
                float distToCam = distance(GetCameraPositionWS(), IN.positionWS);
                float glowFadeT = saturate((distToCam - _DotFadeStart) / max(_DotFadeEnd - _DotFadeStart, 0.001));
                float distantGlowMul  = lerp(1.0, _DistantGlowMul, glowFadeT);
                float distantPulseMul = lerp(1.0, 0.0, glowFadeT);

                float pulsePhase  = hash13(sectionId + 41.7) * 6.2831;
                float pulse       = 1.0 + sin(_Time.y * _PulseSpeed * 6.2831 + pulsePhase) * _PulseAmount * distantPulseMul;
                float pulseClamped = max(0.5, pulse);

                // ---- Emission: two layers ----
                //   • Baseline: EVERY algae dot emits a soft glow so it's always visible in the dark.
                //     This is what makes the cave shimmer with thousands of faint dots without a light.
                //   • Bright: only dots gated by the patch×section×dot hash light up at full intensity,
                //     plus interaction wave forces every dot in the hit patch to be bright.
                float brightLevel = saturate(brightGate + waveMass);
                float intensity   = _BaselineGlow + (_GlowIntensity + waveIntensity) * brightLevel;
                float3 emission   = aColor * intensity * algaeEmissionMass * distantGlowMul * pulseClamped;

                // ---------------- Lighting ----------------
                float wrap = _LightWrap;

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light  mainLight   = GetMainLight(shadowCoord);
                float  mainNdotL   = saturate((dot(N, mainLight.direction) + wrap) / (1.0 + wrap));
                float3 lit         = mainLight.color * mainNdotL * mainLight.shadowAttenuation;

                // Additional lights — Forward AND Forward+ (clustered). LIGHT_LOOP_BEGIN expands
                // to use inputData.normalizedScreenSpaceUV on the clustered path, so we always
                // populate InputData. Guard covers both keywords so the body compiles either way.
            #if defined(_ADDITIONAL_LIGHTS) || defined(_CLUSTER_LIGHT_LOOP)
                InputData inputData = (InputData)0;
                inputData.positionWS              = IN.positionWS;
                inputData.normalWS                = N;
                inputData.viewDirectionWS         = normalize(GetWorldSpaceViewDir(IN.positionWS));
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                uint addCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(addCount)
                    Light addLight = GetAdditionalLight(lightIndex, IN.positionWS);
                    float addNdotL = saturate((dot(N, addLight.direction) + wrap) / (1.0 + wrap));
                    lit += addLight.color * addNdotL * addLight.distanceAttenuation * addLight.shadowAttenuation;
                LIGHT_LOOP_END
            #endif

                lit += SampleFlashlight(IN.positionWS, N, wrap);

                float3 ambient = SampleSH(N) * _SkyAmbientStrength + _AmbientBoost.xxx;

                float3 color = albedo * (lit + ambient) + emission;
                color = MixFog(color, IN.fogFactor);
                return half4(color, 1.0);
            }

            ENDHLSL
        }

        // ---------------- Shadow caster ----------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   shadowVert
            #pragma fragment shadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct ShadowAttr { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct ShadowVary { float4 positionCS : SV_POSITION; };

            ShadowVary shadowVert(ShadowAttr IN)
            {
                ShadowVary OUT;
                float3 posWS  = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normWS = TransformObjectToWorldNormal(IN.normalOS);
                float4 posCS  = TransformWorldToHClip(ApplyShadowBias(posWS, normWS, _LightDirection));
            #if UNITY_REVERSED_Z
                posCS.z = min(posCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                posCS.z = max(posCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                OUT.positionCS = posCS;
                return OUT;
            }

            half4 shadowFrag(ShadowVary IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // ---------------- Depth-only ----------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex   depthVert
            #pragma fragment depthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthAttr { float4 positionOS : POSITION; };
            struct DepthVary { float4 positionCS : SV_POSITION; };

            DepthVary depthVert(DepthAttr IN)
            {
                DepthVary OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 depthFrag(DepthVary IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack Off
}
