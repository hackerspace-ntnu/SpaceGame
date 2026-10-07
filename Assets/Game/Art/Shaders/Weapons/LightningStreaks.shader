Shader "Custom/LightningPlasma"
{
    Properties
    {
        [Header(Plasma Settings)]
        [MainColor] _PrimaryColor("Primary Plasma Color", Color) = (0.2, 0.5, 1.0, 1.0)
        _SecondaryColor("Secondary Plasma Color", Color) = (1.0, 0.2, 1.0, 1.0)
        _CoreColor("Core Glow Color", Color) = (1.0, 1.0, 1.0, 1.0)
        
        [Header(Animation)]
        _AnimationSpeed("Animation Speed", Float) = 2.0
        _NoiseScale("Noise Scale", Float) = 5.0
        _NoiseIntensity("Noise Intensity", Float) = 3.0
        _LightningDensity("Lightning Density", Range(0.0, 1.0)) = 0.8
        
        [Header(Effects)]
        _PlasmaIntensity("Plasma Intensity", Float) = 1.0
        _AlphaMultiplier("Alpha Multiplier", Float) = 0.5
        _GlowFalloff("Glow Falloff", Float) = 2.0
        _EdgeSharpness("Edge Sharpness", Range(0.0, 1.0)) = 0.4
        _CoreBrightness("Core Brightness", Float) = 2.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Blend One One
        ZWrite Off

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
                float3 positionWS : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _PrimaryColor;
                half4 _SecondaryColor;
                half4 _CoreColor;
                float _AnimationSpeed;
                float _NoiseScale;
                float _NoiseIntensity;
                float _LightningDensity;
                float _PlasmaIntensity;
                float _AlphaMultiplier;
                float _GlowFalloff;
                float _EdgeSharpness;
                float _CoreBrightness;
            CBUFFER_END

            // Perlin-like noise
            float fade(float t) { return t * t * t * (t * (t * 6.0 - 15.0) + 10.0); }

            float hash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.13456);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

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

            // Fast value noise
            float valueNoise(float2 uv)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);
                f = f * f * (3.0 - 2.0 * f);

                float a = hash(i + float2(0.0, 0.0));
                float b = hash(i + float2(1.0, 0.0));
                float c = hash(i + float2(0.0, 1.0));
                float d = hash(i + float2(1.0, 1.0));

                float ab = lerp(a, b, f.x);
                float cd = lerp(c, d, f.x);
                return lerp(ab, cd, f.y);
            }

            // Lightning bolt generation
            float lightning(float2 uv, float time)
            {
                float bolt = 0.0;
                float2 pos = uv;

                // Main lightning strike
                float mainStrike = abs(sin(pos.x * 3.0 + time) * 0.1) / (abs(pos.y - 0.5) + 0.1);
                bolt += mainStrike * _LightningDensity;

                // Branch lightning
                for(int i = 0; i < 3; i++)
                {
                    float offset = float(i) * 0.33;
                    float branch = abs(sin((pos.x + offset) * 5.0 + time * 1.5) * 0.08) / (abs(pos.y - 0.3 - offset * 0.2) + 0.2);
                    bolt += branch * _LightningDensity * 0.6;
                }

                // Add noise modulation
                float noise = valueNoise(pos * _NoiseScale + time);
                bolt *= (0.5 + noise * 0.5);

                return bolt;
            }

            // Plasma generation
            float3 plasma(float2 uv, float time)
            {
                // Create animated noise field with many layers
                float n1 = valueNoise(uv * _NoiseScale + time * _AnimationSpeed);
                float n2 = valueNoise(uv * _NoiseScale * 0.5 + time * _AnimationSpeed * 0.7);
                float n3 = valueNoise(uv * _NoiseScale * 2.0 + time * _AnimationSpeed * 1.3);
                float n4 = valueNoise(uv * _NoiseScale * 4.0 + time * _AnimationSpeed * 1.8);
                float n5 = valueNoise(uv * _NoiseScale * 8.0 + time * _AnimationSpeed * 2.5);

                // Combine noise layers with high intensity
                float plasma = n1 * 0.3 + n2 * 0.25 + n3 * 0.2 + n4 * 0.15 + n5 * 0.1;
                plasma *= _NoiseIntensity;
                plasma = smoothstep(0.2, 0.8, plasma);

                // Add lightning bolts
                float bolt = lightning(uv, time);
                plasma += bolt;

                // Create color based on plasma intensity
                float3 color = lerp(_PrimaryColor.rgb, _SecondaryColor.rgb, sin(plasma + time) * 0.5 + 0.5);
                color = lerp(color, _CoreColor.rgb, bolt);

                return color;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float time = _Time.y;
                
                // Generate plasma pattern
                float3 plasmaColor = plasma(IN.uv, time);
                
                // Add radial glow from center
                float2 centerDist = abs(IN.uv - 0.5) * 2.0;
                float centerGlow = exp(-length(centerDist) * _GlowFalloff);
                centerGlow = pow(centerGlow, _EdgeSharpness + 1.0);
                
                // Combine plasma with glow
                float3 finalColor = plasmaColor * (1.0 + centerGlow * _CoreBrightness);
                
                // Calculate final alpha with intensity
                float alpha = max(max(finalColor.r, finalColor.g), finalColor.b) * _PlasmaIntensity;
                alpha = smoothstep(0.0, 1.0, alpha) * _AlphaMultiplier;
                
                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}
