Shader "SpaceGame/CaveTriplanarAlgaeGlow"
{
    Properties
    {
        [Header(Triplanar Rock Colors)]
        _ColorX ("Color X (Side walls)", Color) = (0.55, 0.32, 0.20, 1)
        _ColorY ("Color Y (Floor / ceiling)", Color) = (0.62, 0.45, 0.28, 1)
        _ColorZ ("Color Z (Front / back walls)", Color) = (0.42, 0.22, 0.16, 1)
        _BlendSharpness ("Triplanar Blend Sharpness", Range(1, 16)) = 4

        [Header(Algae Patches  Big slow blobs of color)]
        _PatchScale ("Patch Scale (smaller = bigger patches)", Range(0.05, 1)) = 0.25
        _PatchThreshold ("Patch Threshold (lower = more patches)", Range(0, 1)) = 0.55
        _PatchEdgeSoftness ("Patch Edge Softness  unused (kept for compat)", Range(0.0, 0.5)) = 0.18
        _PatchFalloff ("Patch Falloff Power (higher = denser middle, sparser edges)", Range(0.5, 6)) = 2.5
        _PatchAlbedoStrength ("Patch Color Strength on Rock", Range(0, 1)) = 0.55
        _PatchAffinityStrength ("Patch Affinity (0=any rock 1=strict host)", Range(0, 1)) = 0.3
        _PatchAffinityAxis ("Patch Host Axis (0=X 1=Y 2=Z)", Range(0, 2)) = 2

        [Header(Algae Color Palette  4 sci fi colors)]
        _ColorAlga1 ("Algae Color 1", Color) = (0.20, 0.85, 1.00, 1)
        _ColorAlga2 ("Algae Color 2", Color) = (0.30, 0.95, 0.55, 1)
        _ColorAlga3 ("Algae Color 3", Color) = (0.95, 0.30, 0.85, 1)
        _ColorAlga4 ("Algae Color 4", Color) = (0.95, 0.85, 0.30, 1)
        _ColorMixScale ("Color Mix Scale (smaller = bigger color zones inside patch)", Range(0.5, 20)) = 5

        [Header(Algae Specs  Small individual dots inside patches)]
        _SpecDensity ("Specs per Patch (higher = more dots)", Range(5, 200)) = 80
        _SpecSizeMin ("Spec Size Min", Range(0.0, 1.0)) = 0.05
        _SpecSizeMax ("Spec Size Max", Range(0.0, 1.0)) = 0.35
        _SpecShapeNoise ("Spec Shape Distortion", Range(0, 2)) = 0.8

        [Header(Pointillism  Renders filaments as tiny dots)]
        _DotScale ("Dot Scale (higher = smaller dots)", Range(1, 200)) = 70
        _DotSize ("Dot Size (0..1)", Range(0, 1)) = 0.4
        _DotFadeStart ("Dot Fade Start Distance (m)", Range(0, 50)) = 6
        _DotFadeEnd ("Dot Fade End Distance (m)", Range(1, 100)) = 18
        _DistantGlowMultiplier ("Distant Glow Multiplier (0..1)", Range(0, 1)) = 0.3

        [Header(Distance Simplification  Antiflicker LOD for far algae)]
        _LodFadeStart ("LOD Fade Start (m)  small-dot emission begins simplifying", Range(0, 200)) = 12
        _LodFadeEnd ("LOD Fade End (m)  fully simplified to soft glow", Range(1, 500)) = 60
        _LodBoldFadeStart ("Bold LOD Fade Start (m)", Range(0, 500)) = 80
        _LodBoldFadeEnd ("Bold LOD Fade End (m)", Range(1, 1000)) = 250

        [Header(Bold Specs  Few big bright standout algae per patch)]
        _BoldDensity ("Bold Spec Density (lower = fewer per area)", Range(1, 30)) = 6
        _BoldFraction ("Fraction of cells that produce a bold spec", Range(0, 1)) = 0.55
        _BoldSizeMin ("Bold Spec Size Min", Range(0.1, 1)) = 0.25
        _BoldSizeMax ("Bold Spec Size Max", Range(0.1, 1)) = 0.55
        _BoldIntensity ("Bold Spec Glow Intensity (only on emitting specs)", Range(0, 30)) = 12
        _BoldGlowFraction ("Fraction of bold specs that ALSO glow", Range(0, 1)) = 0.25

        [Header(Glow  Only on bright specs)]
        _GlowingPatchFraction ("Fraction of patches with glow", Range(0, 1)) = 0.7
        _GlowingSpecFraction ("Fraction of thread sections (within glow patch) that glow", Range(0, 1)) = 0.25
        _GlowingDotFraction ("Fraction of individual dots (within glow section) that emit", Range(0, 1)) = 0.3
        _GlowIntensity ("Glow Intensity", Range(0, 30)) = 6
        _PulseSpeed ("Pulse Speed", Range(0, 2)) = 0.5
        _PulseAmount ("Pulse Amount (0..1)", Range(0, 1)) = 0.4

        [Header(Surface)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0
        _Metallic ("Metallic", Range(0, 1)) = 0

        [Header(Lighting)]
        _AmbientBoost ("Ambient Boost (cave fill light)", Range(0, 1)) = 0.02
        _LightWrap    ("Light Wrap", Range(0, 1)) = 0.0
        _SkyAmbientStrength ("Sky/SH Ambient Strength (0 = pitch black w/o lights)", Range(0, 1)) = 0.0
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
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorX, _ColorY, _ColorZ;
                float  _BlendSharpness;
                float  _PatchScale, _PatchThreshold, _PatchEdgeSoftness, _PatchFalloff;
                float  _PatchAlbedoStrength;
                float  _PatchAffinityStrength, _PatchAffinityAxis;
                float4 _ColorAlga1, _ColorAlga2, _ColorAlga3, _ColorAlga4;
                float  _ColorMixScale;
                float  _SpecDensity, _SpecSizeMin, _SpecSizeMax, _SpecShapeNoise;
                float  _DotScale, _DotSize;
                float  _DotFadeStart, _DotFadeEnd, _DistantGlowMultiplier;
                float  _LodFadeStart, _LodFadeEnd;
                float  _LodBoldFadeStart, _LodBoldFadeEnd;
                float  _BoldDensity, _BoldFraction, _BoldSizeMin, _BoldSizeMax, _BoldIntensity, _BoldGlowFraction;
                float  _GlowingPatchFraction, _GlowingSpecFraction, _GlowingDotFraction, _GlowIntensity;
                float  _PulseSpeed, _PulseAmount;
                float  _Smoothness, _Metallic;
                float  _AmbientBoost, _LightWrap, _SkyAmbientStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float fogFactor : TEXCOORD2; };

            float hash13(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }
            float hash11(float n) { return frac(sin(n) * 43758.5453); }
            float3 hash33(float3 p) { return float3(hash13(p+1.7), hash13(p+11.3), hash13(p+27.9)); }

            float vnoise(float3 p)
            {
                float3 i = floor(p), f = frac(p), u = f*f*(3.0-2.0*f);
                float n000 = hash13(i+float3(0,0,0)), n100 = hash13(i+float3(1,0,0));
                float n010 = hash13(i+float3(0,1,0)), n110 = hash13(i+float3(1,1,0));
                float n001 = hash13(i+float3(0,0,1)), n101 = hash13(i+float3(1,0,1));
                float n011 = hash13(i+float3(0,1,1)), n111 = hash13(i+float3(1,1,1));
                return lerp(lerp(lerp(n000,n100,u.x), lerp(n010,n110,u.x), u.y),
                            lerp(lerp(n001,n101,u.x), lerp(n011,n111,u.x), u.y), u.z);
            }
            float fbm(float3 p)
            {
                float v = 0.0, a = 0.5;
                for (int i = 0; i < 4; i++) { v += a * vnoise(p); p *= 2.03; a *= 0.5; }
                return v;
            }

            // ---- Filament/spec layer ----
            // Generates structural fungus-like threads with varying density: distorted Worley
            // gives F1 (nearest cell distance) and F2 (second-nearest), and (F2 - F1) produces
            // thin lines along the boundaries between cells — these read as filament "threads".
            // Combined with fine FBM the density varies naturally — some areas dense thread mats,
            // others sparse strands.
            //
            // Returns: filament mass 0..1, and an "id" used for glow gating + animation phase
            // (sampled coarsely so neighboring filaments share gating, looking like a connected
            // mycelium).
            // specMass: full filament render (used for albedo tinting; merges to threads at distance)
            // dotOnlyMass: pure dot pricks (used for emission so light never smears into lines)
            void evaluateSpec(float3 worldP, out float specMass, out float dotOnlyMass, out float specId)
            {
                // Domain warp the input so cells aren't axis-aligned
                float specCellScale = _SpecDensity * 0.012;
                float3 warp = float3(fbm(worldP * specCellScale * 0.7),
                                     fbm(worldP * specCellScale * 0.7 + 11.3),
                                     fbm(worldP * specCellScale * 0.7 + 27.7)) - 0.5;
                float3 wp = worldP + warp * 1.5 / specCellScale * _SpecShapeNoise;

                float3 base = floor(wp * specCellScale);
                float3 fp = frac(wp * specCellScale);

                // Worley F1 + F2 distances over a 3x3x3 neighborhood
                float f1 = 10.0;
                float f2 = 10.0;
                float3 closestId = base;
                [unroll] for (int z = -1; z <= 1; z++)
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float3 cellOff = float3(x, y, z);
                    float3 cellId = base + cellOff;
                    float3 jitter = hash33(cellId);
                    float3 d = cellOff + jitter - fp;
                    float dl = length(d);
                    if (dl < f1) { f2 = f1; f1 = dl; closestId = cellId; }
                    else if (dl < f2) { f2 = dl; }
                }

                // Filament mass = bright on cell edges (F2 - F1 ≈ 0), fading away from edges.
                // Gives organic vein/thread look rather than circular dots.
                float edge = f2 - f1;
                // Map: edge 0 → mass 1 (strong filament), edge > _SpecSizeMax → mass 0
                float threadStrength = 1.0 - smoothstep(_SpecSizeMin, _SpecSizeMax, edge);

                // Bright nodes at cell centers (F1 small) — varied size per cell
                float nodeSize = lerp(0.15, 0.35, hash13(closestId + 41.1));
                float nodeStrength = 1.0 - smoothstep(nodeSize * 0.5, nodeSize, f1);

                // Combine: threads + brighter nodes where they meet
                specMass = saturate(max(threadStrength * 0.7, nodeStrength));

                // Density modulation by fine FBM so some areas are dense thread mats, others sparse
                float densityMod = smoothstep(0.35, 0.7, fbm(worldP * specCellScale * 2.5));
                specMass *= densityMod;

                // ---- Dot-render the filament ----
                // Multiply by a very-high-frequency Worley dot pattern so the continuous filament
                // shape is rendered as a constellation of tiny dots instead of solid lines.
                // _DotScale controls how fine the dots are (higher = smaller).
                float3 dotBase = floor(worldP * _DotScale);
                float3 dotFp   = frac(worldP * _DotScale);
                float dotF1 = 10.0;
                [unroll] for (int dz = -1; dz <= 1; dz++)
                [unroll] for (int dy = -1; dy <= 1; dy++)
                [unroll] for (int dx = -1; dx <= 1; dx++)
                {
                    float3 dCellOff = float3(dx, dy, dz);
                    float3 dJitter = hash33(dotBase + dCellOff + 53.1);
                    float3 dd = dCellOff + dJitter - dotFp;
                    float ddl = length(dd);
                    if (ddl < dotF1) dotF1 = ddl;
                }
                // Dot size — smaller = sparser pointillism feel
                float dotMask = 1.0 - smoothstep(_DotSize * 0.5, _DotSize, dotF1);

                // Distance-based fade: as the camera gets far enough that individual dots are
                // sub-pixel, blend toward solid filament (mask = 1) so dots stop popping in/out
                // as the camera moves. _DotFadeStart = distance where fade begins; _DotFadeEnd =
                // fully merged. Eliminates the sparkle/static at distance with zero quality loss
                // up close.
                // Albedo path: fade dot mask toward 1 at distance so the painted thread shape
                // remains visible when individual dots become sub-pixel.
                float distToCam = distance(GetCameraPositionWS(), worldP);
                float fadeT = saturate((distToCam - _DotFadeStart) / max(_DotFadeEnd - _DotFadeStart, 0.001));
                float dotMaskFaded = lerp(dotMask, 1.0, fadeT);

                // Emission path: gate by the strict (non-faded) dot mask so light stays as
                // discrete pricks only — never smears into filament/thread lines, no matter the distance.
                dotOnlyMass = specMass * dotMask;
                specMass    *= dotMaskFaded;

                specId = hash13(closestId + 23.7);
            }

            // ---- Color tapestry: blend 4 algae colors based on continuous color noise ----
            float3 algaeTapestry(float3 worldP)
            {
                // Hash-driven 4-way selection: smoothly interpolated regions of each color
                float n1 = vnoise(worldP * _ColorMixScale * 0.1 + 0.3);
                float n2 = vnoise(worldP * _ColorMixScale * 0.1 + 7.7);

                // Use n1, n2 as 2D coords to interpolate between the 4 colors (bilinear in color-space)
                float3 cTop = lerp(_ColorAlga1.rgb, _ColorAlga2.rgb, smoothstep(0.2, 0.8, n1));
                float3 cBot = lerp(_ColorAlga3.rgb, _ColorAlga4.rgb, smoothstep(0.2, 0.8, n1));
                return lerp(cTop, cBot, smoothstep(0.2, 0.8, n2));
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

                // Triplanar rock base
                float3 absN = pow(abs(N), _BlendSharpness);
                float3 w    = absN / (absN.x + absN.y + absN.z + 1e-5);
                float3 baseColor = _ColorX.rgb * w.x + _ColorY.rgb * w.y + _ColorZ.rgb * w.z;

                // ---- Patch layer ----
                // Big slow FBM defines whether THIS region is inside a patch at all.
                // _PatchScale lower = bigger patches; values around 0.05-0.10 = huge patches.
                // The mass curve is intentionally NOT a hard threshold — we map the FBM value
                // continuously above the threshold so the center of a patch (high noise value)
                // is dense and the edges (just-above-threshold) are sparse. This gives the
                // natural "heavy middle, sparse edges" look without competed/abrupt borders.
                float patchNoise = fbm(IN.positionWS * _PatchScale);
                float patchMass = saturate((patchNoise - _PatchThreshold) /
                                            max(1.0 - _PatchThreshold, 0.001));
                // Power curve sharpens the center vs edges (higher power = more concentrated middle)
                patchMass = pow(patchMass, _PatchFalloff);

                // Patch ID — used to gate glow per-patch (each patch decides if it has glow at all).
                // Sampled coarsely so the same patch returns the same id everywhere inside it.
                float3 patchCellId = floor(IN.positionWS * _PatchScale * 0.5);
                float patchId = hash13(patchCellId + 3.3);
                float patchHasGlow = step(1.0 - _GlowingPatchFraction, patchId);

                // Soft rock-axis affinity (algae prefer host but spread)
                float patchWeight =
                    (_PatchAffinityAxis < 0.5) ? w.x :
                    (_PatchAffinityAxis < 1.5) ? w.y : w.z;
                float affinity = lerp(1.0 - _PatchAffinityStrength, 1.0,
                                      smoothstep(0.2, 0.7, patchWeight));
                patchMass *= affinity;

                // Patch color: each PATCH picks one tapestry color (sampled at the patch cell center,
                // not per-fragment), so all specs within a patch share roughly the same tint.
                float3 patchCellWorld = (patchCellId + 0.5) / (_PatchScale * 0.5);
                float3 patchColor = algaeTapestry(patchCellWorld);

                // ---- Spec layer ----
                float specMass; float dotOnlyMass; float specId;
                evaluateSpec(IN.positionWS, specMass, dotOnlyMass, specId);
                // Specs only exist where there's a patch
                specMass    *= patchMass;
                dotOnlyMass *= patchMass;

                // Spec color = patch color with a TINY per-spec hue jitter (not full re-sample).
                // This keeps each patch coherent (mostly one color) while individual specs can vary slightly.
                float3 hueJitter = (hash33(float3(specId, specId * 1.7, specId * 2.3)) - 0.5) * 0.15;
                float3 specColor = saturate(patchColor + hueJitter);

                // ---- Albedo: rock stays as rock; algae only shows as discrete specs/pricks,
                //              never as a smeared patch-wide wash. ----
                float3 albedo = baseColor;
                albedo = lerp(albedo, specColor, specMass * _PatchAlbedoStrength);

                // ---- Glow gating ----
                // Two layers:
                //   • specHasGlow: gates whole thread sections (per-Worley-cell). Sections of the
                //     filament either light up or stay dark — gives the "only part of the patch
                //     emits" look.
                //   • dotHasGlow: a finer per-dot gate so even within a glowing section, only some
                //     individual dots actually emit. Reads as "scattered bright dots inside lit areas".
                float specHasGlow = step(1.0 - _GlowingSpecFraction, specId);
                // Per-dot gate: hash by the dot grid cell so each dot is consistently on or off
                float3 dotCellWorld = floor(IN.positionWS * _DotScale);
                float dotHasGlow  = step(1.0 - _GlowingDotFraction, hash13(dotCellWorld + 71.3));
                float glowGate = patchHasGlow * specHasGlow * dotHasGlow;

                // Distance fade for glow/animation: dim the glow and reduce pulse amount when
                // the camera is far away. Uses the same fadeT we used for dot merging.
                float distToCamGlow = distance(GetCameraPositionWS(), IN.positionWS);
                float fadeTGlow = saturate((distToCamGlow - _DotFadeStart) / max(_DotFadeEnd - _DotFadeStart, 0.001));
                float distantGlowMul = lerp(1.0, _DistantGlowMultiplier, fadeTGlow);
                float distantPulseMul = lerp(1.0, 0.0, fadeTGlow); // kill animation entirely at distance

                float pulse = 1.0 + sin(_Time.y * _PulseSpeed * 6.2831 + specId * 6.2831) * _PulseAmount * distantPulseMul;
                // Use dotOnlyMass (strict per-dot, never faded to threads) so emission stays as point lights.
                // Emission deliberately ignores scene lighting and is NOT faded with distance — algae must
                // remain visible no matter the ambient light level (caves are intentionally near pitch-black
                // without light sources). Pulse is clamped so it never dips emission below 50% of peak.
                float pulseClamped = max(0.5, pulse);

                // ---- Distance LOD on small-dot emission (anti-flicker) ----
                // Close up: strict per-dot pricks (dotOnlyMass — high-frequency, aliases when sub-pixel).
                // Far away: blend toward a smooth low-frequency approximation = specMass (filament shape,
                // slowly varying, no aliasing) scaled by the average dot coverage so total emitted energy
                // is roughly preserved.
                float dotCoverage = saturate(3.14159 * _DotSize * _DotSize); // π r² in unit cell
                float lodFadeT = saturate((distToCamGlow - _LodFadeStart) /
                                          max(_LodFadeEnd - _LodFadeStart, 0.001));
                float emissionMass = lerp(dotOnlyMass, specMass * dotCoverage, lodFadeT);

                float3 emission = specColor * _GlowIntensity * emissionMass * glowGate * pulseClamped;

                // ---- Bold spec layer: a few big visible standout algae per patch ----
                // Sparse Worley grid with cells much larger than the regular dot grid. Each cell
                // produces ONE big visible algae spec if it passes the per-cell gate AND is inside a patch.
                // The spec is a real visible algae (strong albedo tint), and INDEPENDENTLY some of
                // them also emit light. Most are just big colored algae.
                float boldCellScale = _BoldDensity * 0.01;
                float3 boldBase = floor(IN.positionWS * boldCellScale);
                float3 boldFp   = frac(IN.positionWS * boldCellScale);
                float boldF1 = 10.0;
                float3 boldClosest = boldBase;
                [unroll] for (int bz = -1; bz <= 1; bz++)
                [unroll] for (int by = -1; by <= 1; by++)
                [unroll] for (int bx = -1; bx <= 1; bx++)
                {
                    float3 bOff = float3(bx, by, bz);
                    float3 bId  = boldBase + bOff;
                    float3 bJit = hash33(bId + 91.7);
                    float3 bd   = bOff + bJit - boldFp;
                    float bdl   = length(bd);
                    if (bdl < boldF1) { boldF1 = bdl; boldClosest = bId; }
                }
                // Per-cell randomized size and gate — only some cells have a bold spec
                float boldExists = step(1.0 - _BoldFraction, hash13(boldClosest + 33.3));
                float boldSizeRand = lerp(_BoldSizeMin, _BoldSizeMax, hash13(boldClosest + 47.1));
                // Distance LOD for bold specs: at far distance fade out organic FBM distortion (the 8x
                // high-frequency noise aliases) and widen the smoothstep edge so the dot edge stays
                // pixel-smooth instead of sharp. Bold specs are bigger so they alias later than small dots.
                float boldLodT = saturate((distance(GetCameraPositionWS(), IN.positionWS) - _LodBoldFadeStart) /
                                          max(_LodBoldFadeEnd - _LodBoldFadeStart, 0.001));
                float boldShapeNoiseScale = lerp(0.3, 0.0, boldLodT);
                float boldShapeNoise = (fbm(IN.positionWS * boldCellScale * 8.0) - 0.5) * boldSizeRand * boldShapeNoiseScale;
                // Widen the inner edge of the smoothstep at distance to soften the dot border.
                float boldEdgeInner = lerp(boldSizeRand * 0.5, boldSizeRand * 0.2, boldLodT);
                float boldMass = saturate(1.0 - smoothstep(boldEdgeInner, boldSizeRand, boldF1 + boldShapeNoise));
                // Gated: only inside a patch + only if cell-gate passes
                boldMass *= boldExists * patchMass;

                // Bold spec color = patch color slightly shifted (kept cohesive with patch palette)
                float3 boldColor = saturate(patchColor + 0.10);

                // ---- Bold albedo: ALWAYS visible (this is the actual algae, not just light) ----
                albedo = lerp(albedo, boldColor, boldMass * 0.85);

                // ---- Bold glow: separate gate, only some bold specs emit ----
                float boldEmits = step(1.0 - _BoldGlowFraction, hash13(boldClosest + 67.7));
                float boldPulse = 1.0 + sin(_Time.y * _PulseSpeed * 6.2831 * 0.6 + hash13(boldClosest) * 6.2831) * _PulseAmount * distantPulseMul;
                float boldPulseClamped = max(0.5, boldPulse);
                // Bold glow also ignores distance fade — bright algae stay visible across the cave.
                emission += boldColor * _BoldIntensity * boldMass * boldEmits * boldPulseClamped;

                // ---- Lighting ----
                // Caves should be near pitch-black without a light source. _AmbientBoost adds
                // a tiny floor (default 0.02) and _SkyAmbientStrength scales the sky/SH probe
                // (default 0 — caves ignore the skybox so they stay dark when there's no light).
                // Point/spot lights (torches, lanterns) light the cave via the additional-lights loop.
                float wrap = _LightWrap;

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float mainNdotL = saturate((dot(N, mainLight.direction) + wrap) / (1.0 + wrap));
                float3 lit = mainLight.color * mainNdotL * mainLight.shadowAttenuation;

                // Additional (point/spot) lights — required for torches/lanterns to actually light caves.
                // LIGHT_LOOP_BEGIN expands correctly for both Forward and Forward+ (tile-binned) paths.
            #ifdef _ADDITIONAL_LIGHTS
                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = N;
                inputData.viewDirectionWS = normalize(GetWorldSpaceViewDir(IN.positionWS));
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                uint addCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(addCount)
                    Light addLight = GetAdditionalLight(lightIndex, IN.positionWS);
                    float addNdotL = saturate((dot(N, addLight.direction) + wrap) / (1.0 + wrap));
                    lit += addLight.color * addNdotL * addLight.distanceAttenuation * addLight.shadowAttenuation;
                LIGHT_LOOP_END
            #endif

                float3 ambient = SampleSH(N) * _SkyAmbientStrength + _AmbientBoost.xxx;

                float3 color = albedo * (lit + ambient) + emission;
                color = MixFog(color, IN.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct AttribS { float4 pos : POSITION; float3 nrm : NORMAL; };
            struct VaryS  { float4 pos : SV_POSITION; };

            float4 GetShadowPositionHClip(AttribS IN)
            {
                float3 wp = TransformObjectToWorld(IN.pos.xyz);
                float3 wn = TransformObjectToWorldNormal(IN.nrm);
                float4 clip = TransformWorldToHClip(ApplyShadowBias(wp, wn, _LightDirection));
                #if UNITY_REVERSED_Z
                    clip.z = min(clip.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    clip.z = max(clip.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return clip;
            }

            VaryS vertShadow(AttribS IN) { VaryS OUT; OUT.pos = GetShadowPositionHClip(IN); return OUT; }
            half4 fragShadow(VaryS IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
