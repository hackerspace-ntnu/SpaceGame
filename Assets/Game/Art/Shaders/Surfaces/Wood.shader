Shader "Custom/Wood"
{
    Properties
    {
        [Header(Wood Color)]
        _WoodColor1("Dark Wood Color", Color) = (0.4, 0.25, 0.1, 1.0)
        _WoodColor2("Light Wood Color", Color) = (0.7, 0.5, 0.2, 1.0)
        _WoodColor3("Highlight Color", Color) = (0.9, 0.7, 0.4, 1.0)
        
        [Header(Wood Pattern)]
        _Scale("Wood Scale", Float) = 8.0
        _Roughness("Surface Roughness", Range(0.0, 1.0)) = 0.6
        _RingWidth("Ring Width Variation", Float) = 0.15
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.1
        
        [Header(Grain Detail)]
        _GrainScale("Grain Scale", Float) = 25.0
        _GrainAmount("Grain Amount", Float) = 0.5
        _NoiseScale("Noise Detail", Float) = 3.0
        _FBMOctaves("FBM Octaves", int) = 5
        _FBMLacunarity("FBM Lacunarity", Float) = 2.0
        _FBMPersistence("FBM Persistence", Float) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _WoodColor1;
                half4 _WoodColor2;
                half4 _WoodColor3;
                float _Scale;
                float _Roughness;
                float _RingWidth;
                float _Metallic;
                float _GrainScale;
                float _GrainAmount;
                float _NoiseScale;
                int _FBMOctaves;
                float _FBMLacunarity;
                float _FBMPersistence;
            CBUFFER_END

            // Hash function for noise
            float hash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.13456);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // Perlin-like noise
            float fade(float t) { return t * t * t * (t * (t * 6.0 - 15.0) + 10.0); }

            float perlinNoise(float2 p)
            {
                float2 pi = floor(p);
                float2 pf = frac(p);
                float2 u = pf * pf * (3.0 - 2.0 * pf);

                float a = hash(pi + float2(0.0, 0.0));
                float b = hash(pi + float2(1.0, 0.0));
                float c = hash(pi + float2(0.0, 1.0));
                float d = hash(pi + float2(1.0, 1.0));

                float ab = lerp(a, b, u.x);
                float cd = lerp(c, d, u.x);
                return lerp(ab, cd, u.y);
            }

            // Wood ring pattern using sine waves
            float woodRings(float2 uv)
            {
                float dist = sqrt(uv.x * uv.x + uv.y * uv.y);
                float rings = sin(dist * _Scale * 3.14159 * 2.0) * 0.5 + 0.5;
                return rings;
            }

            // Fractional Brownian Motion for detailed noise
            float fbm(float2 uv, int octaves, float lacunarity, float persistence)
            {
                float value = 0.0;
                float amplitude = 1.0;
                float frequency = 1.0;
                float maxValue = 0.0;
                
                for(int i = 0; i < 8; i++)
                {
                    if(i >= octaves) break;
                    
                    value += amplitude * perlinNoise(uv * frequency);
                    maxValue += amplitude;
                    
                    amplitude *= persistence;
                    frequency *= lacunarity;
                }
                
                return value / maxValue;
            }
            
            // Grain detail using FBM noise
            float grain(float2 uv)
            {
                float fbmGrain = fbm(uv * _GrainScale, _FBMOctaves, _FBMLacunarity, _FBMPersistence);
                float fbmDetail = fbm(uv * _GrainScale * 0.5, _FBMOctaves - 1, _FBMLacunarity, _FBMPersistence * 0.8);
                float fbmFinesse = fbm(uv * _GrainScale * 0.25, _FBMOctaves - 2, _FBMLacunarity, _FBMPersistence * 0.6);
                
                float grainPattern = fbmGrain * 0.5 + fbmDetail * 0.3 + fbmFinesse * 0.2;
                return grainPattern;
            }

            // Wood texture generation
            float3 woodTexture(float2 uv)
            {
                // Create offset variation for grain direction using FBM
                float offsetX = fbm(uv * _NoiseScale, _FBMOctaves - 1, _FBMLacunarity, _FBMPersistence) * 0.4;
                float offsetY = fbm(uv * _NoiseScale + 7.5, _FBMOctaves - 1, _FBMLacunarity, _FBMPersistence) * 0.4;
                float2 uvOffset = uv + float2(offsetX, offsetY);
                
                // Main wood ring pattern with FBM-based variation
                float rings = woodRings(uvOffset);
                
                // Add variation to ring width using FBM
                float ringVar = fbm(uv * _Scale * 0.5, _FBMOctaves - 2, _FBMLacunarity, _FBMPersistence) * _RingWidth;
                rings = smoothstep(0.3 - ringVar, 0.7 + ringVar, rings);
                
                // Combine detailed grain pattern
                float grainPattern = grain(uv);
                
                // High-frequency detail noise using FBM
                float fbmDetail = fbm(uv * _NoiseScale * 5.0, _FBMOctaves - 1, _FBMLacunarity, _FBMPersistence * 0.7);
                
                // Medium-frequency variation
                float fbmMedium = fbm(uv * _NoiseScale * 2.0, _FBMOctaves, _FBMLacunarity, _FBMPersistence);
                
                // Blend all patterns together
                float woodPattern = lerp(rings, grainPattern, 0.4);
                woodPattern = lerp(woodPattern, fbmMedium * 0.5, 0.25);
                woodPattern = lerp(woodPattern, fbmDetail * 0.3, 0.15);
                
                // Create color based on pattern
                float3 darkWood = _WoodColor1.rgb;
                float3 lightWood = _WoodColor2.rgb;
                float3 highlight = _WoodColor3.rgb;
                
                // Blend colors based on wood pattern with more complexity
                float3 color = lerp(darkWood, lightWood, woodPattern);
                color = lerp(color, highlight, grainPattern * _GrainAmount);
                color = lerp(color, darkWood, fbmDetail * 0.2);
                
                return color;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Generate wood texture
                float3 woodColor = woodTexture(IN.uv);
                
                // Add detailed roughness variation using FBM
                float roughnessVariation = fbm(IN.uv * _NoiseScale * 2.5, _FBMOctaves - 1, _FBMLacunarity, _FBMPersistence);
                float fineDetail = fbm(IN.uv * _NoiseScale * 8.0, _FBMOctaves - 2, _FBMLacunarity, _FBMPersistence * 0.6);
                float finalRoughness = _Roughness * (0.6 + roughnessVariation * 0.3 + fineDetail * 0.1);
                
                // Apply simple lighting to enhance wood appearance
                float3 normal = normalize(IN.normalWS);
                float lightContribution = abs(normal.y) * 0.5 + 0.5;
                woodColor *= lightContribution;
                
                // Add subtle specular highlight with detail
                float specular = pow(roughnessVariation, 1.0 - finalRoughness) * _Metallic * 0.5;
                specular += pow(fineDetail, 2.0) * _Metallic * 0.2;
                woodColor += specular;
                
                return half4(woodColor, 1.0);
            }
            ENDHLSL
        }
    }
}
