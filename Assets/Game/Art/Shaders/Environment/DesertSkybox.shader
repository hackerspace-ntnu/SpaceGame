Shader "Custom/DesertSkybox"
{
    Properties
    {
        // [Header(Sky Colors Day)]
        _SkyColorTop ("Sky Color (Top)", Color) = (0.5, 0.7, 0.6, 1)
        _SkyColorHorizon ("Sky Color (Horizon)", Color) = (0.9, 0.85, 0.7, 1)
        
        // [Header(Sky Colors - Sunset Sunrise)]
        _SunsetColorTop ("Sunset Sky Top", Color) = (0.4, 0.3, 0.5, 1)
        _SunsetColorHorizon ("Sunset Horizon", Color) = (1, 0.5, 0.3, 1)
        _SunsetIntensity ("Sunset Intensity", Range(0, 1)) = 0.8
        
        // [Header(Sky Colors - Night)]
        _NightColorTop ("Night Sky Top", Color) = (0.05, 0.05, 0.15, 1)
        _NightColorHorizon ("Night Horizon", Color) = (0.1, 0.1, 0.2, 1)
        
        // [Header(Sky Settings)]
        _HorizonHeight ("Horizon Height", Range(-1, 1)) = 0
        _HorizonBlend ("Horizon Blend Sharpness", Range(0.1, 10)) = 2
        
        [Header(Sun)]
        _SunColor ("Sun Color (Day)", Color) = (1, 0.95, 0.7, 1)
        _SunSunsetColor ("Sun Color (Sunset)", Color) = (1, 0.5, 0.2, 1)
        _SunNightColor ("Sun Color (Night)", Color) = (0.8, 0.8, 0.9, 1)
        _SunSize ("Sun Size", Range(0.01, 0.3)) = 0.03
        _SunIntensity ("Sun Intensity (Day)", Range(0, 10)) = 5
        _SunSunsetIntensity ("Sun Intensity (Sunset)", Range(0, 10)) = 8
        _SunNightIntensity ("Sun Intensity (Night)", Range(0, 10)) = 0.5
        _SunGlow ("Sun Glow", Range(0, 1)) = 0.15
        _SunOutlineColor ("Sun Outline Color", Color) = (1, 0.9, 0.3, 1)
        _SunOutlineWidth ("Sun Outline Width", Range(0, 0.1)) = 0.01
        _SunRayCount ("Sun Ray Count", Range(4, 16)) = 8
        _SunRayLength ("Sun Ray Length", Range(0, 0.5)) = 0.15
        _SunRayWidth ("Sun Ray Width", Range(0.01, 0.1)) = 0.03
        _SunFlickerSpeed ("Sun Flicker Speed", Range(0, 10)) = 3.0
        _SunFlickerAmount ("Sun Flicker Amount", Range(0, 0.3)) = 0.1
        _HeatWaveSpeed ("Heat Wave Speed", Range(0, 5)) = 2.0
        _HeatWaveAmount ("Heat Wave Amount", Range(0, 0.1)) = 0.03
        
        [Header(Sand Dust Clouds)]
        _DustColor ("Dust Color (Day)", Color) = (0.85, 0.9, 0.88, 1)
        _DustSunsetColor ("Dust Color (Sunset)", Color) = (1, 0.7, 0.5, 1)
        _DustNightColor ("Dust Color (Night)", Color) = (0.15, 0.15, 0.25, 1)
        _DustHeight ("Dust Height", Range(-1, 0.5)) = -0.1
        _DustThickness ("Dust Thickness", Range(0, 1)) = 0.5
        _DustDensity ("Dust Density", Range(0, 1)) = 0.7
        _DustScale ("Dust Scale", Range(1, 50)) = 8
        _DustSpeed ("Dust Speed", Range(0, 1)) = 0.05
        _DustWarpSpeed ("Dust Warp Speed", Range(0, 5)) = 0.5
        
        [Header(Cartoon Style)]
        _Bands ("Color Bands", Range(2, 10)) = 5
        _BandSmoothness ("Band Smoothness", Range(0, 0.5)) = 0.0
        
        [Header(Stars)]
        _StarDensity ("Star Density", Range(0.1, 2)) = 1.0
        _StarBrightness ("Star Brightness", Range(0, 2)) = 1.0
        _StarSize ("Star Size", Range(0.1, 3)) = 1.0
        
        [Header(Distant Mountains)]
        _MountainHeight ("Mountain Height", Range(-0.5, 0.2)) = -0.1
        _MountainColor ("Mountain Color (Day)", Color) = (0.3, 0.25, 0.2, 1)
        _MountainSunsetColor ("Mountain Color (Sunset)", Color) = (0.5, 0.3, 0.2, 1)
        _MountainNightColor ("Mountain Color (Night)", Color) = (0.1, 0.1, 0.15, 1)
        _MountainScale ("Mountain Scale", Range(1, 20)) = 8
        _MountainSharpness ("Mountain Sharpness", Range(0.1, 5)) = 2.0
        _MountainLightPatchScale ("Light Patch Scale", Range(0.5, 10)) = 2.5
        _MountainLightPatchThreshold ("Light Patch Threshold", Range(0, 1)) = 0.4
        _MountainLightPatchSmoothness ("Light Patch Smoothness", Range(0, 0.5)) = 0.2
    }
    
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off
        
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 viewDir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            
            CBUFFER_START(UnityPerMaterial)
                half4 _SkyColorTop;
                half4 _SkyColorHorizon;
                half4 _SunsetColorTop;
                half4 _SunsetColorHorizon;
                half4 _NightColorTop;
                half4 _NightColorHorizon;
                half _SunsetIntensity;
                half _HorizonHeight;
                half _HorizonBlend;
                half4 _SunColor;
                half4 _SunSunsetColor;
                half4 _SunNightColor;
                half _SunSize;
                half _SunIntensity;
                half _SunSunsetIntensity;
                half _SunNightIntensity;
                half _SunGlow;
                half4 _SunOutlineColor;
                half _SunOutlineWidth;
                half _SunRayCount;
                half _SunRayLength;
                half _SunRayWidth;
                half _SunFlickerSpeed;
                half _SunFlickerAmount;
                half _HeatWaveSpeed;
                half _HeatWaveAmount;
                half4 _DustColor;
                half4 _DustSunsetColor;
                half4 _DustNightColor;
                half _DustHeight;
                half _DustThickness;
                half _DustDensity;
                half _DustScale;
                half _DustSpeed;
                half _DustWarpSpeed;
                half _Bands;
                half _BandSmoothness;
                half _StarDensity;
                half _StarBrightness;
                half _StarSize;
                half _MountainHeight;
                half4 _MountainColor;
                half4 _MountainSunsetColor;
                half4 _MountainNightColor;
                half _MountainScale;
                half _MountainSharpness;
                half _MountainLightPatchScale;
                half _MountainLightPatchThreshold;
                half _MountainLightPatchSmoothness;
            CBUFFER_END
            
            // Hash function for noise (2D)
            float hash(float2 p)
            {
                p = frac(p * float2(443.897, 441.423));
                p += dot(p, p.yx + 19.19);
                return frac(p.x * p.y);
            }
            
            // 3D Hash function for stars
            float hash3D(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }
            
            // Value noise
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
            
            // Cartoon banding function with sharp steps
            float cartoonBand(float value, float bands, float smoothness)
            {
                if (smoothness < 0.01)
                {
                    // Pure stepped for very sharp transitions
                    return floor(value * bands) / bands;
                }
                float stepped = floor(value * bands) / bands;
                float nextStep = ceil(value * bands) / bands;
                float blend = smoothstep(0.5 - smoothness, 0.5 + smoothness, frac(value * bands));
                return lerp(stepped, nextStep, blend);
            }
            
            // Star field function
            float3 starField(float3 dir, float visibility)
            {
                float3 p = normalize(dir);
                float density = 150.0 * _StarDensity;
                
                float3 id = floor(p * density);
                float3 starColor = float3(0.0, 0.0, 0.0);
                
                // Check neighboring cells
                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int z = -1; z <= 1; z++)
                        {
                            float3 offset = float3(x, y, z);
                            float3 cell = id + offset;
                            
                            float rnd = hash3D(cell);
                            if (rnd > 0.997) // Only ~0.3% of cells have stars
                            {
                                // Random position inside the cell
                                float3 randOffset = float3(
                                    hash3D(cell + 11.0),
                                    hash3D(cell + 37.0),
                                    hash3D(cell + 71.0)
                                );
                                float3 starPos = normalize((cell + randOffset) / density);
                                
                                // Random star size
                                float sizeRnd = hash3D(cell + 13.57);
                                float starSize = lerp(150.0, 450.0, sizeRnd) * _StarSize;
                                
                                float dist = length(p - starPos);
                                
                                // Star color variation (bluish to yellowish)
                                float3 starCol = lerp(
                                    float3(0.6, 0.6, 1.0),  // bluish
                                    float3(1.0, 0.9, 0.7),  // yellow/white
                                    sizeRnd
                                );
                                
                                // Star glow
                                float glow = exp(-pow(dist * starSize, 0.9));
                                
                                // Twinkling effect
                                float phase = hash3D(cell + 123.456) * 6.2831; // [0, 2π]
                                float blink = 0.5 + 0.5 * sin(_Time.y * 2.0 + phase);
                                blink = pow(blink, 3.0); // emphasize bright phase
                                
                                // Mix star color with white when twinkling
                                float3 twinkledCol = lerp(starCol, float3(1.0, 1.0, 1.0), blink * 0.3);
                                
                                // Apply glow and twinkle
                                starColor += twinkledCol * glow * (0.5 + blink * 0.5) * _StarBrightness;
                            }
                        }
                    }
                }
                
                // Fade stars based on visibility (night factor)
                return starColor * visibility;
            }
            
            // Mountain ridges function - returns mask and lighting
            void mountainSilhouette(float3 dir, float3 lightDir, out float mask, out float lighting)
            {
                mask = 0.0;
                lighting = 0.0;
                
                // Only render mountains near horizon
                float height = dir.y;
                if (height > _MountainHeight + 0.3) return;
                
                // Use seamless polar coordinates (map to circle instead of angle)
                // This eliminates the seam at -π/π
                float2 circlePos = normalize(dir.xz);
                
                // Create mountain peaks using seamless noise on the circle
                float2 mountainUV = circlePos * _MountainScale;
                
                // Multiple layers of mountains at different distances
                float mountains = 1.0; // Start at 1 (sky), decrease where mountains are
                float combinedHeight = 0.0;
                
                // Distant layer (smaller peaks)
                float layer1 = fbm(mountainUV * 0.5 + float2(100.0, 0.0), 4);
                layer1 = pow(saturate(layer1), _MountainSharpness);
                float height1 = _MountainHeight + layer1 * 0.15;
                mountains = min(mountains, step(height1, height));
                combinedHeight = layer1;
                
                // Mid layer (medium peaks)
                float layer2 = fbm(mountainUV * 0.8 + float2(50.0, 0.0), 5);
                layer2 = pow(saturate(layer2), _MountainSharpness);
                float height2 = _MountainHeight + layer2 * 0.2;
                float mask2 = step(height2, height);
                if (mask2 < mountains)
                {
                    mountains = mask2;
                    combinedHeight = layer2;
                }
                
                // Near layer (tallest peaks)
                float layer3 = fbm(mountainUV * 1.2, 6);
                layer3 = pow(saturate(layer3), _MountainSharpness);
                float height3 = _MountainHeight + layer3 * 0.25;
                float mask3 = step(height3, height);
                if (mask3 < mountains)
                {
                    mountains = mask3;
                    combinedHeight = layer3;
                }
                
                // Only calculate lighting if we're actually on a mountain
                mask = 1.0 - mountains; // 1 = mountain, 0 = sky
                
                if (mask > 0.01)
                {
                    // Create organic patches for color variation
                    // Use seamless polar coordinates for patches too
                    float normalizedHeight = saturate((height - _MountainHeight) / 0.3);
                    float2 patchUV = float2(circlePos.x * _MountainLightPatchScale * 3.0, 
                                           normalizedHeight * _MountainLightPatchScale * 5.0);
                    
                    // Large organic patches
                    float patches = fbm(patchUV, 5);
                    
                    // Add finer detail patches at different scale
                    float detail = fbm(patchUV * 2.8 + float2(123.4, 567.8), 4);
                    patches = patches * 0.5 + detail * 0.5;
                    
                    // Threshold patches to create distinct lit areas
                    lighting = smoothstep(_MountainLightPatchThreshold, 
                                         _MountainLightPatchThreshold + _MountainLightPatchSmoothness, 
                                         patches);
                }
                else
                {
                    lighting = 0.0;
                }
            }
            
            // Smooth minimum function for organic cloud merging
            float smoothmin(float a, float b, float k)
            {
                float h = saturate(0.5 + 0.5 * (b - a) / k);
                return lerp(b, a, h) - k * h * (1.0 - h);
            }
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.viewDir = input.positionOS.xyz;
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                float3 viewDir = normalize(input.viewDir);
                float3 lightDir = normalize(_MainLightPosition.xyz);
                
                // Calculate height (y component of view direction)
                float height = viewDir.y;
                
                // Calculate sun elevation for day/sunset/night transitions
                float sunHeight = lightDir.y; // -1 (below horizon) to 1 (zenith)
                
                // Calculate transition factors
                // Day: sun high (sunHeight > 0.3)
                // Sunset: sun near horizon (-0.1 to 0.3)
                // Night: sun below horizon (sunHeight < -0.1)
                
                float dayFactor = smoothstep(0.0, 0.5, sunHeight); // 1 when sun high, 0 at horizon
                
                // Sunset factor: peaks at horizon, fades as sun goes down (no bounce back)
                // Smooth transitions to avoid snapping
                float sunsetFactor = smoothstep(-0.3, 0.1, sunHeight); // Fade in from below
                sunsetFactor *= smoothstep(0.5, 0.0, sunHeight); // Fade out when high
                sunsetFactor = pow(sunsetFactor, 1.2) * _SunsetIntensity;
                
                float nightFactor = smoothstep(0.0, -0.3, sunHeight); // 0 at horizon, 1 when sun deep below
                
                // Sky gradient with sharp cartoon banding
                float skyGradient = saturate((height - _HorizonHeight) * _HorizonBlend + 0.5);
                skyGradient = cartoonBand(skyGradient, _Bands, _BandSmoothness);
                
                // Blend colors based on time of day: Day → Sunset → Night
                // Blend all three phases simultaneously to avoid reverting
                half3 currentColorTop = _SkyColorTop.rgb * dayFactor + 
                                       _SunsetColorTop.rgb * sunsetFactor + 
                                       _NightColorTop.rgb * nightFactor;
                                       
                half3 currentColorHorizon = _SkyColorHorizon.rgb * dayFactor + 
                                           _SunsetColorHorizon.rgb * sunsetFactor + 
                                           _NightColorHorizon.rgb * nightFactor;
                
                // Pure color separation - no blending
                half3 skyColor = skyGradient > 0.5 ? currentColorTop : currentColorHorizon;
                
                // Add intermediate band for more cartoon steps
                if (skyGradient > 0.33 && skyGradient <= 0.66)
                {
                    skyColor = lerp(currentColorHorizon, currentColorTop, 0.5);
                }
                
                // Add warm glow near sun during sunset
                if (sunsetFactor > 0.1)
                {
                    float sunProximity = dot(viewDir, lightDir);
                    float sunGlowArea = smoothstep(0.0, 0.7, sunProximity);
                    half3 sunsetGlow = _SunsetColorHorizon.rgb * 1.2;
                    skyColor = lerp(skyColor, sunsetGlow, sunGlowArea * sunsetFactor * 0.5);
                }
                
                // === STARS (rendered before clouds) ===
                // Stars fade in during sunset and get brighter at night
                // Calculate star visibility: dimmer during sunset, full brightness at night
                float starVisibility = saturate(sunsetFactor * 0.15 + nightFactor * 1.0);
                
                // Add stars to the sky
                float3 stars = starField(viewDir, starVisibility);
                skyColor += stars;
                
                // === PUFFY CARTOON CLOUDS (Like reference image) ===
                float dustMask = 0.0;
                
                // Only render clouds in lower part of sky
                if (height < _DustHeight + _DustThickness)
                {
                    // Separate time controls
                    float moveTime = _Time.y * _DustSpeed;      // Global cloud movement
                    float warpTime = _Time.y * _DustWarpSpeed;  // Warping/morphing speed
                    
                    // Proper spherical UV mapping for skybox
                    // Use seamless polar coordinates for clouds (eliminate seam)
                    float angle = atan2(viewDir.x, viewDir.z); // [-pi, pi]
                    float2 baseUV;
                    baseUV.x = frac((angle / 6.28318) + 0.5 + moveTime); // Use moveTime for global movement
                    baseUV.y = (height - _DustHeight) / _DustThickness; // 0 to 1 vertically in cloud layer
                    baseUV *= _DustScale;
                    
                    // --- MULTI-STAGE WARPING SYSTEM ---
                    // Stage 1: Primary large-scale distortion field
                    float2 warp1 = float2(
                        fbm(baseUV * 1.3 + float2(warpTime * 0.15, warpTime * 0.08), 4),
                        fbm(baseUV * 1.5 + float2(-warpTime * 0.12, warpTime * 0.18), 4)
                    ) * 0.35 - 0.175; // Center around 0
                    
                    float2 warpedUV1 = baseUV + warp1;
                    
                    // Stage 2: Medium-scale swirling distortion
                    float2 warp2 = float2(
                        fbm(warpedUV1 * 2.8 + float2(warpTime * 0.22, -warpTime * 0.15), 3),
                        fbm(warpedUV1 * 2.4 + float2(-warpTime * 0.18, warpTime * 0.25), 3)
                    ) * 0.25 - 0.125;
                    
                    float2 warpedUV2 = warpedUV1 + warp2;
                    
                    // Stage 3: Fine detail turbulence
                    float2 warp3 = float2(
                        fbm(warpedUV2 * 5.5 + float2(warpTime * 0.35, warpTime * 0.28), 2),
                        fbm(warpedUV2 * 4.8 + float2(-warpTime * 0.32, warpTime * 0.38), 2)
                    ) * 0.15 - 0.075;
                    
                    float2 cloudUV = warpedUV2 + warp3;
                    
                    // --- MULTI-LAYERED CLOUDS WITH DIFFERENT BEHAVIORS ---
                    // Layer 1: Large base clouds that morph slowly
                    float2 uv1 = cloudUV * 0.8 + float2(warpTime * 0.06, warpTime * 0.04);
                    float cloud1 = fbm(uv1, 5);
                    
                    // Add shape-shifting to layer 1
                    float morph1 = fbm(uv1 * 1.3 + warpTime * 0.08, 3) * 0.15;
                    cloud1 = saturate(cloud1 + morph1);
                    
                    // Layer 2: Medium clouds moving differently
                    float2 uv2 = cloudUV * 1.3 + float2(-warpTime * 0.11, warpTime * 0.09);
                    float cloud2 = fbm(uv2, 4);
                    
                    // Add shape-shifting to layer 2
                    float morph2 = fbm(uv2 * 1.5 + warpTime * 0.13, 3) * 0.18;
                    cloud2 = saturate(cloud2 + morph2);
                    
                    // Layer 3: Fast-moving detail clouds
                    float2 uv3 = cloudUV * 2.0 + float2(warpTime * 0.17, -warpTime * 0.15);
                    float cloud3 = fbm(uv3, 3);
                    
                    // Layer 4: Fine turbulent detail
                    float2 uv4 = cloudUV * 3.2 + float2(-warpTime * 0.24, warpTime * 0.21);
                    float cloud4 = fbm(uv4, 2);
                    
                    // --- ORGANIC MERGING WITH SMOOTHMIN ---
                    // Smoothly merge layers - this creates the organic "clouds flowing together" effect
                    float merged = smoothmin(cloud1, cloud2, 0.28);
                    merged = smoothmin(merged, cloud3 * 0.85, 0.32);
                    
                    // Blend in fine detail with regular lerp
                    merged = lerp(merged, cloud4, 0.12);
                    
                    // --- DYNAMIC SHAPE EVOLUTION ---
                    // Add continuous shape changes that make clouds morph over time
                    float evolution1 = fbm(cloudUV * 1.6 + warpTime * 0.09, 3);
                    float evolution2 = fbm(cloudUV * 2.3 + warpTime * 0.14, 2);
                    float shapeShift = (evolution1 * 0.6 + evolution2 * 0.4) * 0.2;
                    merged = saturate(merged + shapeShift);
                    
                    // --- ORGANIC SPAWN/DISAPPEAR SYSTEM ---
                    // Use multi-scale noise to create areas where clouds naturally form and dissipate
                    
                    // Large atmospheric zones where clouds form (very slow)
                    float spawnZone1 = fbm(baseUV * 0.35 + float2(warpTime * 0.02, warpTime * 0.015), 4);
                    
                    // Medium zones with different timing
                    float spawnZone2 = fbm(baseUV * 0.65 + float2(-warpTime * 0.035, warpTime * 0.028), 4);
                    
                    // Smaller local pockets where clouds cluster
                    float spawnZone3 = fbm(baseUV * 1.1 + float2(warpTime * 0.048, -warpTime * 0.042), 3);
                    
                    // Combine spawn zones - weight towards larger scales
                    float spawnMask = spawnZone1 * 0.5 + spawnZone2 * 0.3 + spawnZone3 * 0.2;
                    
                    // Smooth threshold to create organic appearance/disappearance
                    spawnMask = smoothstep(0.25, 0.7, spawnMask);
                    
                    // Apply with high base - clouds mostly stay, but some areas fade significantly
                    merged *= (0.5 + spawnMask * 0.5);
                    
                    // --- CLOUD CLUSTERING / MERGING ZONES ---
                    // Create areas where clouds naturally cluster together
                    float clusterNoise = fbm(baseUV * 0.45 + warpTime * 0.025, 3);
                    float clusterMask = smoothstep(0.4, 0.6, clusterNoise);
                    
                    // In cluster zones, boost cloud density to create "cloud groups"
                    merged += clusterMask * 0.15;
                    
                    // --- GENTLE FLOWING/CONDENSATION EFFECT ---
                    // Create very slow, gentle flow variations (like humidity changes)
                    float flowNoise = fbm(baseUV * 0.6 + float2(warpTime * 0.035, warpTime * 0.028), 3);
                    flowNoise = smoothstep(0.3, 0.7, flowNoise);
                    merged *= lerp(0.92, 1.0, flowNoise);
                    
                    // --- WISPY EDGE VARIATION ---
                    // Create soft, wispy variations at cloud boundaries
                    float wispNoise = fbm(cloudUV * 7.5 + float2(warpTime * 0.15, warpTime * 0.12), 2);
                    wispNoise = smoothstep(0.35, 0.65, wispNoise);
                    
                    // Only affect the edges, not the dense centers
                    float edgeDetect = saturate(1.0 - merged * 2.0); // Detect thin areas
                    merged *= lerp(1.0, 0.8 + wispNoise * 0.2, edgeDetect * 0.5);
                    
                    // Make clouds billowy and puffy
                    float clouds = pow(saturate(merged * 1.4), 1.3);
                    
                    // Height-based density: dense at bottom, wispy at top
                    float heightInCloud = 1.0 - saturate((height - _DustHeight) / _DustThickness);
                    float densityGradient = pow(heightInCloud, 0.4);
                    
                    // Threshold for cloud appearance
                    float threshold = 1.0 - _DustDensity;
                    threshold = lerp(threshold * 0.3, threshold * 0.8, 1.0 - densityGradient);
                    
                    // Create hard cloud edges
                    dustMask = smoothstep(threshold, threshold + 0.1, clouds);
                    
                    // Apply height-based fading
                    dustMask *= densityGradient;
                    
                    // Cartoon-style opacity levels
                    if (dustMask > 0.1)
                    {
                        if (dustMask < 0.35)
                            dustMask = 0.5; // Light wispy clouds
                        else if (dustMask < 0.65)
                            dustMask = 0.8; // Medium clouds
                        else
                            dustMask = 1.0; // Dense puffy clouds
                    }
                    else
                    {
                        dustMask = 0.0;
                    }
                }
                
                // Blend dust color through day/sunset/night
                half3 dustColorDay = _DustColor.rgb * dayFactor;
                half3 dustColorSunset = _DustSunsetColor.rgb * sunsetFactor;
                half3 dustColorNight = _DustNightColor.rgb * nightFactor;
                half3 dustColor = dustColorDay + dustColorSunset + dustColorNight;
                
                // Blend dust with sky (smooth for cloud edges, but still cartoon banded)
                half3 color = lerp(skyColor, dustColor, dustMask);
                
                // === DISTANT MOUNTAINS (in front of clouds) ===
                float mountainMask;
                float mountainLighting;
                mountainSilhouette(viewDir, lightDir, mountainMask, mountainLighting);
                
                // Base mountain color based on time of day
                half3 mountainBaseColor = _MountainColor.rgb * dayFactor + 
                                         _MountainSunsetColor.rgb * sunsetFactor + 
                                         _MountainNightColor.rgb * nightFactor;
                
                // Lighter color for lit areas (sun-facing slopes)
                half3 mountainLitColor = mountainBaseColor * 1.8; // Brighter for lit areas
                
                // Mix base and lit colors based on lighting
                half3 mountainColor = lerp(mountainBaseColor, mountainLitColor, mountainLighting);
                
                // Blend mountains with the scene (over clouds)
                color = lerp(color, mountainColor, mountainMask);
                
                // === SUN WITH RAYS, OUTLINE, AND HEAT FLICKER ===
                // --- Stable 2D projection for sun ---
                // Project view and sun direction onto XZ (horizon) and Y (vertical)
                float2 skyUV = float2(atan2(viewDir.x, viewDir.z) / 6.28318 + 0.5, saturate(viewDir.y * 0.5 + 0.5));
                float2 sunPos = float2(atan2(lightDir.x, lightDir.z) / 6.28318 + 0.5, saturate(lightDir.y * 0.5 + 0.5));
                float2 sunUV = skyUV - sunPos;
                // Wrap X axis for seamless skybox
                sunUV.x = (sunUV.x > 0.5) ? sunUV.x - 1.0 : (sunUV.x < -0.5) ? sunUV.x + 1.0 : sunUV.x;
                float dist = length(sunUV) * 2.0;
                float angle = atan2(sunUV.y, sunUV.x);
                // Use public properties for sun/ray control
                float sunSharpness = 12.0;
                float sunCoreIntensity = _SunIntensity * 6.0;
                float rayLength = lerp(2.0, 6.0, saturate(_SunRayLength * 2.0));
                float raySharpness = lerp(2.5, 6.0, saturate(_SunRayWidth * 10.0));
                float rayIntensity = _SunIntensity * 1.2;
                float glowFalloff = lerp(8.0, 3.0, saturate(_SunGlow * 2.0));
                // Sun core (sharp, bright disk)
                float sunCore = max(0.01 - pow(dist, _SunSize * sunSharpness), 0.0) * sunCoreIntensity;
                // Frills/rays (length and sharpness controlled by public values)
                float frill1 = max(0.1 / pow(dist * rayLength + 0.01, raySharpness), 0.0) * abs(sin(angle * 8.0 + _Time.y)) / rayIntensity;
                float frill2 = max(0.1 / pow(dist * (rayLength+1.5) + 0.01, 1.0/(raySharpness+0.5)), 0.0) * abs(sin(angle * 12.0 + _Time.y)) / (rayIntensity*10.8);
                // Central color burst (less intense, but scales with intensity)
                float bright = 0.08 * _SunIntensity;
                float sunBurst = (max(bright / pow(dist * 4.0 + 0.01, 0.5), 0.0) * 2.0) * float3(0.2, 0.21, 0.3).x * 2.0;
                // Color cycling (subtle)
                float3 circColor = 0.7 + 0.3 * sin(float3(0.9, 0.2, 0.1) + _Time.y*0.2);
                float3 circColor2 = 0.7 + 0.3 * sin(float3(0.3, 0.1, 0.9) + _Time.y*0.3);
                float3 sunColor = lerp(circColor, circColor2, 0.5 + 0.5 * sin(_Time.y*0.1));
                // Compose sun
                float3 sun = sunCore * sunColor;
                sun += frill1 * sunColor;
                sun += frill2 * sunColor;
                sun += sunBurst * float3(0.2, 0.21, 0.3);
                // Sharper roll-off (less glow, controlled by _SunGlow)
                sun *= exp(1.0 - dist) / glowFalloff;
                // Blend with scene
                color += sun;
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
    
    FallBack Off
}
