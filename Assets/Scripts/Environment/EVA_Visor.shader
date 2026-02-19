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
        _FogNoiseStrength("Fog Noise Strength", Range(0, 0.5)) = 0.15
        _BreathStrengthVariation("Breath Strength Variation", Range(0, 0.5)) = 0.2
        
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
                
                // Add organic noise to fog shape (using continuous time-based noise)
                float2 noiseUV = uv * _FogNoiseScale + float2(time * 0.05, time * 0.03);
                float fogNoise = fbm(noiseUV, 3);
                fogNoise = fogNoise * 0.5 + 0.5; // Remap to 0-1
                
                // Apply noise distortion smoothly
                float noiseModulation = 1.0 - _FogNoiseStrength + fogNoise * _FogNoiseStrength;
                totalFog *= noiseModulation;
                
                return saturate(totalFog);
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
                
                // Sample the screen/base texture
                half4 screenColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                
                // ============================================
                // STATIC EFFECTS
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
                
                // Generate respiration fog
                float fog = generateRespirationFog(uv, time);
                fog *= _FogIntensity;
                
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
