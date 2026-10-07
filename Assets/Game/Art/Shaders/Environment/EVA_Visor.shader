Shader "Custom/EVA_Visor"
{
    Properties
    {
        [Header(Base Settings)]
        _MainTex("Screen Texture", 2D) = "white" {}
        _OverallIntensity("Overall Effect Intensity", Range(0, 1)) = 0.5
        
        [Header(Scratches)]
        _ScratchIntensity("Scratch Intensity", Range(0, 1)) = 0.3
        _ScratchDensity("Scratch Density", Range(0, 100)) = 25
        _ScratchLength("Scratch Length", Range(0.01, 0.2)) = 0.05
        _ScratchWidth("Scratch Width", Range(0.0001, 0.005)) = 0.001
        
        [Header(Dirt and Smudges)]
        _DirtIntensity("Dirt Intensity", Range(0, 1)) = 0.2
        _DirtScale("Dirt Scale", Range(1, 50)) = 15
        _DirtColor("Dirt Color", Color) = (0.3, 0.25, 0.2, 1)
        _SmudgeIntensity("Smudge Intensity", Range(0, 1)) = 0.15
        
        [Header(Lens Dirt)]
        _LensDirtIntensity("Lens Dirt Intensity", Range(0, 1)) = 0.25
        _LensDirtScale("Lens Dirt Scale", Range(5, 30)) = 12
        
        [Header(Respiration Fog)]
        _FogIntensity("Fog Intensity", Range(0, 1)) = 0.4
        _FogColor("Fog Color", Color) = (0.8, 0.85, 0.9, 1)
        _BreathingRate("Breathing Rate (breaths/sec)", Range(0.1, 1)) = 0.25
        _FogAccumulationRate("Fog Accumulation Rate", Range(0.1, 5)) = 1.0
        _FogDecayRate("Fog Decay Rate", Range(0.1, 3)) = 0.5
        _FogCenterHeight("Fog Center Height", Range(0, 0.5)) = 0.2
        _FogFalloffRadius("Fog Falloff Radius", Range(0.1, 1)) = 0.3
        _FogNoiseScale("Fog Noise Scale", Range(1, 20)) = 8
        _FogNoiseStrength("Fog Noise Strength", Range(0, 1)) = 0.35
        _BreathStrengthVariation("Breath Strength Variation", Range(0, 0.5)) = 0.2
        _FogDetailScale("Fog Detail Scale", Range(10, 50)) = 25
        _FogWispiness("Fog Wispiness", Range(0, 1)) = 0.4
        _FogDropletDensity("Fog Droplet Density", Range(0, 1)) = 0.3
        _FogVaporStreaks("Fog Vapor Streaks", Range(0, 1)) = 0.25
        _FogRefractionStrength("Fog Refraction Strength", Range(0, 1)) = 0.04
        _FogBlurStrength("Fog Blur Strength", Range(0, 10)) = 3.0
        _FogChromaticAberration("Fog Chromatic Aberration", Range(0, 0.05)) = 0.02
        _FogContrast("Fog Area Contrast Boost", Range(0, 2)) = 0.5
        _FogLightScatter("Fog Light Scattering", Range(0, 1)) = 0.4
        
        [Header(Blur Vignette)]
        _VignetteBlurIntensity("Vignette Blur Intensity", Range(0, 15)) = 5.0
        _VignetteBlurRadius("Vignette Blur Radius", Range(0.3, 1)) = 0.7
        _VignetteBlurSoftness("Vignette Blur Softness", Range(0.1, 0.5)) = 0.3
        
        [Header(Light Effects)]
        _BloomThreshold("Bloom Threshold", Range(0.5, 2)) = 0.8
        _BloomIntensity("Bloom Intensity", Range(0, 5)) = 2.0
        _BloomRadius("Bloom Radius", Range(0.001, 0.02)) = 0.008
        _LensFlareIntensity("Lens Flare Intensity", Range(0, 5)) = 2.5
        _LensFlareColor("Lens Flare Color", Color) = (1.4, 1.2, 1.0, 1)
        _LensFlareMinBrightness("Min Brightness for Flare", Range(0, 1)) = 0.3
        _LightSourcePosition("Light Source Position (Screen)", Vector) = (0.5, 0.5, 0, 0)
        
        [Header(Vignette)]
        _VignetteIntensity("Vignette Intensity", Range(0, 1)) = 0.2
        _VignetteSoftness("Vignette Softness", Range(0.1, 2)) = 0.5
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Overlay"
            "RenderPipeline" = "UniversalPipeline" 
        }

        Pass
        {
            Name "EVA Visor Overlay"
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float _OverallIntensity;
                float _ScratchIntensity;
                float _ScratchDensity;
                float _ScratchLength;
                float _ScratchWidth;
                float _DirtIntensity;
                float _DirtScale;
                half4 _DirtColor;
                float _SmudgeIntensity;
                float _LensDirtIntensity;
                float _LensDirtScale;
                float _FogIntensity;
                half4 _FogColor;
                float _BreathingRate;
                float _FogAccumulationRate;
                float _FogDecayRate;
                float _FogCenterHeight;
                float _FogFalloffRadius;
                float _FogNoiseScale;
                float _FogNoiseStrength;
                float _BreathStrengthVariation;
                float _FogDetailScale;
                float _FogWispiness;
                float _FogDropletDensity;
                float _FogVaporStreaks;
                float _FogRefractionStrength;
                float _FogBlurStrength;
                float _FogChromaticAberration;
                float _FogContrast;
                float _FogLightScatter;
                float _VignetteBlurIntensity;
                float _VignetteBlurRadius;
                float _VignetteBlurSoftness;
                float _BloomThreshold;
                float _BloomIntensity;
                float _BloomRadius;
                float _LensFlareIntensity;
                half4 _LensFlareColor;
                float _LensFlareMinBrightness;
                float4 _LightSourcePosition;
                float _VignetteIntensity;
                float _VignetteSoftness;
            CBUFFER_END

            // ============================================
            // NOISE FUNCTIONS
            // ============================================
            
            // Hash function for pseudo-random numbers
            float hash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }
            
            float hash13(float3 p3)
            {
                p3 = frac(p3 * 0.1031);
                p3 += dot(p3, p3.zyx + 31.32);
                return frac((p3.x + p3.y) * p3.z);
            }

            // 2D Perlin-like noise
            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                
                float a = hash(i);
                float b = hash(i + float2(1.0, 0.0));
                float c = hash(i + float2(0.0, 1.0));
                float d = hash(i + float2(1.0, 1.0));
                
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // Fractal Brownian Motion
            float fbm(float2 p, int octaves)
            {
                float value = 0.0;
                float amplitude = 0.5;
                float frequency = 1.0;
                
                for (int i = 0; i < octaves; i++)
                {
                    value += amplitude * noise(p * frequency);
                    frequency *= 2.0;
                    amplitude *= 0.5;
                }
                
                return value;
            }

            // Voronoi noise for organic patterns
            float voronoi(float2 p)
            {
                float2 n = floor(p);
                float2 f = frac(p);
                
                float minDist = 1.0;
                
                for (int j = -1; j <= 1; j++)
                {
                    for (int i = -1; i <= 1; i++)
                    {
                        float2 neighbor = float2(i, j);
                        float2 randomPoint = float2(hash(n + neighbor), hash(n + neighbor + 0.5));
                        float2 diff = neighbor + randomPoint - f;
                        float dist = length(diff);
                        minDist = min(minDist, dist);
                    }
                }
                
                return minDist;
            }
            

            // ============================================
            // SCRATCH GENERATION
            // ============================================
            
            float generateScratches(float2 uv)
            {
                float scratches = 0.0;
                
                // Generate multiple scratches with different angles and positions
                for (int i = 0; i < (int)_ScratchDensity; i++)
                {
                    float seed = float(i) * 12.9898;
                    float2 seedVec = float2(seed, seed * 1.618);
                    
                    // Random position
                    float2 scratchPos = float2(hash(seedVec), hash(seedVec + 1.0));
                    
                    // Random angle
                    float angle = hash(seedVec + 2.0) * 6.28318;
                    float2 scratchDir = float2(cos(angle), sin(angle));
                    
                    // Random length variation
                    float lengthVar = hash(seedVec + 3.0) * 0.5 + 0.5;
                    float scratchLen = _ScratchLength * lengthVar;
                    
                    // Calculate distance to scratch line
                    float2 toPoint = uv - scratchPos;
                    float alongLine = dot(toPoint, scratchDir);
                    alongLine = clamp(alongLine, -scratchLen, scratchLen);
                    
                    float2 closestPoint = scratchPos + scratchDir * alongLine;
                    float dist = distance(uv, closestPoint);
                    
                    // Add scratch if within width
                    float scratchMask = smoothstep(_ScratchWidth, _ScratchWidth * 0.5, dist);
                    scratchMask *= smoothstep(scratchLen, scratchLen * 0.8, abs(alongLine));
                    
                    // Vary scratch intensity
                    float intensity = hash(seedVec + 4.0) * 0.5 + 0.5;
                    scratches += scratchMask * intensity;
                }
                
                return saturate(scratches);
            }

            // ============================================
            // DIRT AND SMUDGE GENERATION
            // ============================================
            
            float generateDirt(float2 uv)
            {
                float2 p = uv * _DirtScale;
                
                // Layer multiple noise functions for realistic dirt
                float dirt = fbm(p, 4);
                dirt = pow(dirt, 2.0); // Make dirt more sparse
                
                // Add spots
                float spots = voronoi(p * 2.0);
                spots = 1.0 - spots;
                spots = pow(spots, 4.0);
                
                // Combine
                float combined = dirt * 0.7 + spots * 0.3;
                
                return combined;
            }
            
            float generateSmudges(float2 uv)
            {
                float2 p = uv * 8.0;
                
                // Larger, softer smudges
                float smudge = fbm(p * 0.5, 3);
                smudge = pow(smudge, 1.5);
                
                // Add some variation
                float variation = noise(p * 2.0);
                smudge *= variation * 0.5 + 0.5;
                
                return smudge;
            }

            // ============================================
            // LENS DIRT GENERATION
            // ============================================
            
            float generateLensDirt(float2 uv)
            {
                float2 centered = uv - 0.5;
                float2 p = uv * _LensDirtScale;
                
                // Create fingerprint-like patterns
                float pattern = fbm(p, 5);
                
                // Add circular smudges
                float circles = 0.0;
                for (int i = 0; i < 8; i++)
                {
                    float2 seedVec = float2(i * 7.34, i * 3.12);
                    float2 circlePos = float2(hash(seedVec), hash(seedVec + 1.0)) - 0.5;
                    float radius = hash(seedVec + 2.0) * 0.15 + 0.05;
                    float dist = length(centered - circlePos);
                    circles += smoothstep(radius, radius * 0.7, dist) * 0.3;
                }
                
                // Combine patterns
                float lensDirt = pattern * 0.7 + circles * 0.3;
                lensDirt = pow(lensDirt, 2.0);
                
                // More dirt toward edges (natural accumulation)
                float edgeFalloff = 1.0 - length(centered) * 1.5;
                edgeFalloff = saturate(edgeFalloff);
                lensDirt *= edgeFalloff * 0.5 + 0.5;
                
                return lensDirt;
            }

            // ============================================
            // RESPIRATION FOG - ACCUMULATION SYSTEM
            // ============================================
            
            float generateRespirationFog(float2 uv, float time)
            {
                // Fog center at bottom-center of screen
                float2 fogCenter = float2(0.5, _FogCenterHeight);
                float2 toPixel = uv - fogCenter;
                
                // Distance from fog center with elliptical falloff
                float dist = length(toPixel / float2(1.0, 0.8));
                
                // Smooth falloff from center (Gaussian-like)
                float distanceFalloff = exp(-pow(dist / _FogFalloffRadius, 2.0) * 3.0);
                
                // Breathing cycle timing
                float cycleTime = 1.0 / _BreathingRate;
                
                // Simulate accumulated fog using continuous integration
                float totalFog = 0.0;
                float maxLookback = 20.0; // Look back 20 seconds
                int numSamples = 48; // More samples for smoother result
                
                for (int i = 0; i < numSamples; i++)
                {
                    // Time point in the past (continuous)
                    float lookbackTime = (float(i) / float(numSamples)) * maxLookback;
                    float sampleTime = time - lookbackTime;
                    
                    // Continuous breath cycle phase (no floor!)
                    float continuousCycleTime = sampleTime / cycleTime;
                    float samplePhase = frac(continuousCycleTime);
                    
                    // Smoothly varying breath parameters using continuous time
                    // Use slow-changing sine waves instead of discrete noise
                    float breathVariationWave = sin(continuousCycleTime * 0.3) * 0.5 + 0.5;
                    float breathVariation = breathVariationWave * _BreathStrengthVariation;
                    float sampleBreathStrength = 1.0 + breathVariation;
                    
                    // Exhale parameters that vary smoothly
                    float sampleExhaleDuration = 0.3 + breathVariation * 0.2;
                    float sampleExhaleStart = 0.5 - sampleExhaleDuration * 0.5;
                    float sampleExhaleEnd = 0.5 + sampleExhaleDuration * 0.5;
                    
                    // Smooth exhale detection
                    float wasExhaling = smoothstep(sampleExhaleStart - 0.08, sampleExhaleStart + 0.02, samplePhase) * 
                                        smoothstep(sampleExhaleEnd + 0.08, sampleExhaleEnd - 0.02, samplePhase);
                    
                    // Continuous fog accumulation at this time point
                    float timeStep = maxLookback / float(numSamples);
                    float fogAdded = wasExhaling * _FogAccumulationRate * distanceFalloff * sampleBreathStrength * timeStep * 0.1;
                    
                    // Smooth exponential decay - this naturally creates continuous fade
                    float decayFactor = exp(-_FogDecayRate * lookbackTime);
                    float fogRemaining = fogAdded * decayFactor;
                    
                    totalFog += fogRemaining;
                }
                
                // ========================================
                // ADVANCED FOG DETAILING
                // ========================================
                
                // Base organic noise (coarse layer)
                float2 noiseUV = uv * _FogNoiseScale + float2(time * 0.05, time * 0.03);
                float fogNoise = fbm(noiseUV, 4);
                fogNoise = fogNoise * 0.5 + 0.5;
                
                // Fine detail noise (creates texture)
                float2 detailUV = uv * _FogDetailScale + float2(time * 0.02, -time * 0.01);
                float detailNoise = fbm(detailUV, 3);
                detailNoise = detailNoise * 0.5 + 0.5;
                
                // Wispy vapor streaks (vertical flow)
                float2 streakUV = float2(uv.x * 15.0, uv.y * 30.0) + float2(sin(time * 0.1 + uv.y * 10.0) * 0.1, time * 0.08);
                float streaks = fbm(streakUV, 2);
                streaks = pow(saturate(streaks), 3.0); // Sharp streaks
                
                // Horizontal vapor bands (breathing condensation)
                float2 bandUV = float2(uv.x * 8.0, uv.y * 20.0 + time * 0.15);
                float bands = sin(bandUV.y + noise(float2(bandUV.x, 0)) * 2.0) * 0.5 + 0.5;
                bands = pow(bands, 4.0) * _FogWispiness;
                
                // Micro-droplets (small water droplets)
                float2 dropletUV = uv * 40.0;
                float droplets = voronoi(dropletUV + float2(sin(time * 0.05) * 0.2, time * 0.03));
                droplets = 1.0 - droplets;
                droplets = pow(saturate(droplets), 8.0) * _FogDropletDensity;
                
                // Larger condensation droplets (scattered)
                float2 largeDropletUV = uv * 15.0;
                float largeDroplets = voronoi(largeDropletUV + float2(time * 0.02, time * 0.01));
                largeDroplets = 1.0 - largeDroplets;
                largeDroplets = pow(saturate(largeDroplets), 12.0) * _FogDropletDensity * 0.5;
                
                // Edge crystallization effect (frost-like patterns)
                float2 crystalUV = uv * 18.0 + float2(time * 0.01, 0);
                float crystals = abs(noise(crystalUV) * noise(crystalUV * 1.7));
                crystals = pow(crystals, 3.0) * 0.3;
                
                // Combine all noise layers
                float combinedNoise = fogNoise * (1.0 - _FogNoiseStrength) + 
                                      (fogNoise * detailNoise) * _FogNoiseStrength;
                
                // Add vapor effects
                float vaporLayer = streaks * _FogVaporStreaks + bands;
                combinedNoise = saturate(combinedNoise + vaporLayer * 0.3);
                
                // Add droplet details
                float dropletLayer = droplets + largeDroplets;
                
                // Apply all effects to fog
                totalFog *= combinedNoise;
                totalFog += dropletLayer * totalFog * 0.5; // Droplets appear on fogged areas
                totalFog += crystals * totalFog * 0.3; // Crystallization on fogged areas
                
                // Add subtle variation in fog opacity
                float opacityVariation = noise(uv * 12.0 + float2(time * 0.03, time * 0.02)) * 0.5 + 0.5;
                totalFog *= lerp(0.85, 1.0, opacityVariation);
                
                return saturate(totalFog);
            }

            // ============================================
            // BLUR SAMPLING
            // ============================================
            
            half3 sampleBlur(float2 uv, float blurAmount)
            {
                if (blurAmount < 0.01) return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                
                half3 color = half3(0, 0, 0);
                float totalWeight = 0.0;
                
                // Gaussian blur with variable kernel size
                int samples = 9;
                float angleStep = 3.14159 * 2.0 / float(samples);
                
                for (int i = 0; i < samples; i++)
                {
                    float angle = float(i) * angleStep;
                    float2 offset = float2(cos(angle), sin(angle)) * blurAmount * 0.003;
                    
                    half3 sample1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + offset).rgb;
                    half3 sample2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + offset * 0.5).rgb;
                    
                    color += sample1 + sample2;
                    totalWeight += 2.0;
                }
                
                color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb * 2.0;
                totalWeight += 2.0;
                
                return color / totalWeight;
            }

            // ============================================
            // FOG REFRACTION
            // ============================================
            
            float2 calculateFogRefraction(float2 uv, float time, float fogAmount)
            {
                // Create refraction offset based on droplet positions and fog density
                float2 refraction = float2(0, 0);
                
                // Droplet-based refraction (individual droplets bend light strongly)
                float2 dropletUV = uv * 40.0;
                float2 dropletCenter = floor(dropletUV) + 0.5;
                float2 toDropletCenter = dropletUV - dropletCenter;
                float dropletDist = length(toDropletCenter);
                
                // Each droplet creates a strong radial distortion
                float dropletEffect = exp(-dropletDist * 3.0) * voronoi(dropletUV + float2(sin(time * 0.05) * 0.2, time * 0.03));
                refraction += normalize(toDropletCenter) * dropletEffect * 1.2;
                
                // Larger droplets (much stronger refraction)
                float2 largeDropletUV = uv * 15.0;
                float2 largeDropletCenter = floor(largeDropletUV) + 0.5;
                float2 toLargeCenter = largeDropletUV - largeDropletCenter;
                float largeDist = length(toLargeCenter);
                float largeEffect = exp(-largeDist * 2.0) * voronoi(largeDropletUV + float2(time * 0.02, time * 0.01));
                refraction += normalize(toLargeCenter) * largeEffect * 1.8;
                
                // Vapor density gradient causes strong refraction
                float2 gradientUV = uv * 12.0 + float2(time * 0.03, time * 0.02);
                float2 gradient = float2(
                    noise(gradientUV + float2(0.01, 0)) - noise(gradientUV - float2(0.01, 0)),
                    noise(gradientUV + float2(0, 0.01)) - noise(gradientUV - float2(0, 0.01))
                );
                refraction += gradient * 1.5;
                
                // Wispy streaks create directional distortion
                float2 streakUV = float2(uv.x * 15.0, uv.y * 30.0) + float2(sin(time * 0.1 + uv.y * 10.0) * 0.1, time * 0.08);
                float streakGradient = fbm(streakUV + float2(0, 0.01), 2) - fbm(streakUV - float2(0, 0.01), 2);
                refraction += float2(streakGradient * 0.8, streakGradient * 0.4);
                
                // Add turbulent flow patterns
                float2 turbulenceUV = uv * 8.0 + float2(time * 0.05, time * 0.08);
                float2 turbulence = float2(
                    fbm(turbulenceUV, 2),
                    fbm(turbulenceUV + float2(5.2, 1.3), 2)
                ) - 0.5;
                refraction += turbulence * 0.6;
                
                // Scale refraction by fog amount and strength
                refraction *= fogAmount * _FogRefractionStrength;
                
                return refraction;
            }

            // ============================================
            // ADVANCED LENS FLARE (Based on peterekepeter's algorithm)
            // ============================================
            
            half3 generateLensFlare(float2 uv, float2 lightPos, float visibilityIntensity, half3 sceneColor)
            {
                // Early exit if no visibility from script
                if (visibilityIntensity <= 0.0) return half3(0, 0, 0);
                
                // Calculate brightness at light position
                half3 lightSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, lightPos).rgb;
                float lightBrightness = dot(lightSample, float3(0.299, 0.587, 0.114));
                
                // Combine screen brightness with visibility intensity
                float intensityMult = max(lightBrightness * 0.5 + visibilityIntensity, visibilityIntensity);
                
                // Main flare calculation
                float2 main = uv - lightPos;
                float2 uvd = uv * length(uv);
                
                float ang = atan2(main.x, main.y);
                float dist = length(main);
                dist = pow(dist, 0.1);
                
                // Use scene noise for variation
                float n = noise(float2(ang * 16.0, dist * 32.0));
                
                // Central bright spot with noise modulation
                float f0 = 1.0 / (length(uv - lightPos) * 16.0 + 1.0);
                f0 = f0 + f0 * (sin(noise(sin(ang * 2.0 + lightPos.x) * 4.0 - cos(ang * 3.0 + lightPos.y)) * 16.0) * 0.1 + dist * 0.1 + 0.8);
                
                // First ghost (bright circular)
                float f1 = max(0.01 - pow(length(uv + 1.2 * lightPos), 1.9), 0.0) * 7.0;
                
                // Multiple smaller ghosts with chromatic separation
                float f2 = max(1.0 / (1.0 + 32.0 * pow(length(uvd + 0.8 * lightPos), 2.0)), 0.0) * 0.25;
                float f22 = max(1.0 / (1.0 + 32.0 * pow(length(uvd + 0.85 * lightPos), 2.0)), 0.0) * 0.23;
                float f23 = max(1.0 / (1.0 + 32.0 * pow(length(uvd + 0.9 * lightPos), 2.0)), 0.0) * 0.21;
                
                // Mixed UV for different ghost positions
                float2 uvx = lerp(uv, uvd, -0.5);
                
                float f4 = max(0.01 - pow(length(uvx + 0.4 * lightPos), 2.4), 0.0) * 6.0;
                float f42 = max(0.01 - pow(length(uvx + 0.45 * lightPos), 2.4), 0.0) * 5.0;
                float f43 = max(0.01 - pow(length(uvx + 0.5 * lightPos), 2.4), 0.0) * 3.0;
                
                uvx = lerp(uv, uvd, -0.4);
                
                float f5 = max(0.01 - pow(length(uvx + 0.2 * lightPos), 5.5), 0.0) * 2.0;
                float f52 = max(0.01 - pow(length(uvx + 0.4 * lightPos), 5.5), 0.0) * 2.0;
                float f53 = max(0.01 - pow(length(uvx + 0.6 * lightPos), 5.5), 0.0) * 2.0;
                
                uvx = lerp(uv, uvd, -0.5);
                
                float f6 = max(0.01 - pow(length(uvx - 0.3 * lightPos), 1.6), 0.0) * 6.0;
                float f62 = max(0.01 - pow(length(uvx - 0.325 * lightPos), 1.6), 0.0) * 3.0;
                float f63 = max(0.01 - pow(length(uvx - 0.35 * lightPos), 1.6), 0.0) * 5.0;
                
                // Combine all components with chromatic separation
                half3 flare = half3(0, 0, 0);
                flare.r += f2 + f4 + f5 + f6;
                flare.g += f22 + f42 + f52 + f62;
                flare.b += f23 + f43 + f53 + f63;
                
                // Apply color modulation and intensity
                flare = flare * 1.3 - half3(length(uvd) * 0.05, length(uvd) * 0.05, length(uvd) * 0.05);
                flare += half3(f0, f0, f0);
                
                // Color tinting
                flare *= _LensFlareColor.rgb;
                
                // Scale by light intensity and parameter
                return flare * _LensFlareIntensity * intensityMult;
            }

            // ============================================
            // BLOOM GENERATION
            // ============================================
            
            half3 generateBloom(float2 uv, half3 sceneColor)
            {
                float brightness = dot(sceneColor, float3(0.299, 0.587, 0.114));
                
                // Threshold for bloom
                if (brightness < _BloomThreshold) return half3(0, 0, 0);
                
                float excess = pow(brightness - _BloomThreshold, 2.0);
                
                // Sample bright areas with radial blur pattern
                half3 bloom = half3(0, 0, 0);
                int samples = 12;
                
                for (int i = 0; i < samples; i++)
                {
                    float angle = float(i) / float(samples) * 6.28318;
                    float2 offset = float2(cos(angle), sin(angle)) * _BloomRadius;
                    
                    // Multiple rings for stronger bloom
                    bloom += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + offset * 0.5).rgb;
                    bloom += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + offset).rgb;
                    bloom += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + offset * 1.5).rgb;
                }
                
                bloom /= float(samples * 3);
                
                // Only keep the bright parts
                bloom = max(bloom - _BloomThreshold, 0.0);
                
                return bloom * _BloomIntensity * excess;
            }

            // ============================================
            // VIGNETTE
            // ============================================
            
            float generateVignette(float2 uv)
            {
                float2 centered = uv - 0.5;
                float dist = length(centered);
                float vignette = smoothstep(0.8, 0.8 - _VignetteSoftness, dist);
                return vignette;
            }

            // ============================================
            // VERTEX SHADER
            // ============================================
            
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            // ============================================
            // FRAGMENT SHADER
            // ============================================
            
            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float time = _Time.y;
                
                // ============================================
                // CALCULATE FOG AND VIGNETTE
                // ============================================
                
                // Pre-calculate fog to determine all distortion effects
                float fogAmount = generateRespirationFog(uv, time) * _FogIntensity;
                
                // Calculate vignette distance for blur
                float2 centered = uv - 0.5;
                float vignetteDist = length(centered);
                float vignetteBlur = smoothstep(_VignetteBlurRadius, _VignetteBlurRadius - _VignetteBlurSoftness, vignetteDist);
                vignetteBlur = (1.0 - vignetteBlur) * _VignetteBlurIntensity;
                
                // ============================================
                // REFRACTION + CHROMATIC ABERRATION
                // ============================================
                
                // Calculate refraction offset
                float2 refractionOffset = calculateFogRefraction(uv, time, fogAmount);
                
                // Sample with chromatic aberration (water droplets split light into colors)
                float chromaticAmount = fogAmount * _FogChromaticAberration;
                float2 redUV = saturate(uv + refractionOffset * 1.0 + centered * chromaticAmount);
                float2 greenUV = saturate(uv + refractionOffset);
                float blurAmount = fogAmount * _FogBlurStrength + vignetteBlur;
                
                // Blue channel shifts opposite direction
                float2 blueUV = saturate(uv + refractionOffset * 0.95 - centered * chromaticAmount * 0.5);
                
                // ============================================
                // ADAPTIVE BLUR BASED ON FOG DENSITY
                // ============================================
                
                half3 screenColor;
                if (blurAmount > 0.5)
                {
                    // Heavy fog = chromatic aberration + blur per channel
                    half3 redBlur = sampleBlur(redUV, blurAmount * 1.1);
                    half3 greenBlur = sampleBlur(greenUV, blurAmount);
                    half3 blueBlur = sampleBlur(blueUV, blurAmount * 0.9);
                    
                    screenColor = half3(redBlur.r, greenBlur.g, blueBlur.b);
                }
                else
                {
                    // Light fog = simple chromatic aberration
                    half redChannel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, redUV).r;
                    half greenChannel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, greenUV).g;
                    half blueChannel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, blueUV).b;
                    
                    screenColor = half3(redChannel, greenChannel, blueChannel);
                }
                
                // ============================================
                // BRIGHT LIGHT BLOOM & LENS FLARE
                // ============================================
                
                // Generate bloom from bright areas (light "pops")
                half3 bloom = generateBloom(uv, screenColor);
                screenColor += bloom;
                
                // Generate lens flare from light source position
                // _LightSourcePosition.z contains visibility intensity from script
                float2 lightPos = _LightSourcePosition.xy;
                float visibilityIntensity = _LightSourcePosition.z;
                half3 lensFlare = generateLensFlare(uv, lightPos, visibilityIntensity, screenColor);
                screenColor += lensFlare;
                
                // ============================================
                // FOG LIGHT SCATTERING
                // ============================================
                
                // Fog scatters light, creating glow in bright areas
                // FOG LIGHT SCATTERING
                // ============================================
                
                // Fog scatters light, creating glow in bright areas
                float brightness = dot(screenColor, float3(0.299, 0.587, 0.114));
                float scatter = pow(brightness, 2.0) * fogAmount * _FogLightScatter;
                screenColor += _FogColor.rgb * scatter * 0.5;
                
                // ============================================
                // ADAPTIVE CONTRAST IN FOG
                // ============================================
                
                // Boost contrast in foggy areas to maintain visibility
                float contrastBoost = fogAmount * _FogContrast;
                screenColor = lerp(screenColor, (screenColor - 0.5) * (1.0 + contrastBoost) + 0.5, fogAmount * 0.7);
                
                // ============================================
                // STATIC EFFECTS (SCRATCHES, DIRT)
                // ============================================
                
                // Generate scratches
                float scratches = generateScratches(uv);
                
                // Generate dirt layers
                float dirt = generateDirt(uv);
                float smudges = generateSmudges(uv);
                float lensDirt = generateLensDirt(uv);
                
                // Combine dirt effects
                float totalDirt = dirt * _DirtIntensity + 
                                 smudges * _SmudgeIntensity + 
                                 lensDirt * _LensDirtIntensity;
                totalDirt = saturate(totalDirt);
                
                // ============================================
                // DYNAMIC EFFECTS
                // ============================================
                
                // Use pre-calculated fog (already generated for refraction)
                float fog = fogAmount;
                
                // Generate vignette
                float vignette = generateVignette(uv);
                
                // ============================================
                // COMBINE ALL EFFECTS
                // ============================================
                
                // Apply scratches (brighten screen slightly)
                half3 scratchColor = screenColor.rgb + scratches * _ScratchIntensity * 0.5;
                
                // Apply dirt (darken with dirt color tint)
                half3 dirtTinted = lerp(scratchColor, scratchColor * _DirtColor.rgb, totalDirt);
                
                // Apply fog (blend with fog color)
                half3 foggedColor = lerp(dirtTinted, _FogColor.rgb, fog);
                
                // Apply vignette (darken edges)
                half3 finalColor = foggedColor * lerp(1.0 - _VignetteIntensity, 1.0, vignette);
                
                // Calculate final alpha
                float totalAlpha = saturate(
                    scratches * _ScratchIntensity * 0.3 +
                    totalDirt * 0.5 +
                    fog +
                    (1.0 - vignette) * _VignetteIntensity
                );
                
                // Apply overall intensity control
                totalAlpha *= _OverallIntensity;
                
                // Output with proper blending
                return half4(finalColor, totalAlpha);
            }
            ENDHLSL
        }
    }
    
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
