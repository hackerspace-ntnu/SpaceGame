Shader "Hidden/GlassLensDistortion"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _LensCenter ("Lens Center", Vector) = (0.5, 0.5, 0, 0)
        _DistortionStrength ("Distortion Strength", Float) = 5000.0
        _RoundedPower ("Rounded Power", Float) = 8.0
        _BlurSize ("Blur Size", Float) = 0.5
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        
        Cull Off ZWrite Off ZTest Always
        
        Pass
        {
            Name "GlassDistortion"
            
            HLSLPROGRAM
            #pragma vertex FullscreenVert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            
            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord   : TEXCOORD0;
            };
            
            Varyings FullscreenVert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.texcoord = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }
            
            TEXTURE2D(_BlitTexture);
            SAMPLER(sampler_BlitTexture);
            float4 _BlitTexture_TexelSize;
            
            float2 _LensCenter;
            float _DistortionStrength;
            float _RoundedPower;
            float _BlurSize;
            
            half4 frag (Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 m2 = uv - _LensCenter;
                
                // Calculate aspect ratio
                float aspectRatio = _BlitTexture_TexelSize.z / _BlitTexture_TexelSize.w;
                
                // Rounded box calculation (creates the visor shape)
                float roundedBox = pow(abs(m2.x * aspectRatio), _RoundedPower) + pow(abs(m2.y), _RoundedPower);
                
                // Masks for different zones of the visor
                float rb1 = saturate((1.0 - roundedBox * 10000.0) * 8.0);
                float rb2 = saturate((0.95 - roundedBox * 9500.0) * 16.0) - saturate(pow(0.9 - roundedBox * 9500.0, 1.0) * 16.0);
                float rb3 = saturate((1.5 - roundedBox * 11000.0) * 2.0) - saturate(pow(1.0 - roundedBox * 11000.0, 1.0) * 2.0);
                
                // Smooth transition for edge blending
                float transition = smoothstep(0.0, 1.0, rb1 + rb2);
                
                half4 fragColor;
                
                if (transition > 0.01)
                {
                    // Apply lens distortion with proper scaling
                    float distortAmount = _DistortionStrength * 0.00001;
                    float2 lens = ((uv - 0.5) * (1.0 - roundedBox * distortAmount) + 0.5);
                    
                    // Clamp to prevent sampling outside texture
                    lens = saturate(lens);
                    
                    // Apply blur effect
                    fragColor = half4(0, 0, 0, 0);
                    float total = 0.0;
                    
                    for (float x = -2.0; x <= 2.0; x += 1.0)
                    {
                        for (float y = -2.0; y <= 2.0; y += 1.0)
                        {
                            float2 offset = float2(x, y) * _BlurSize * _BlitTexture_TexelSize.xy;
                            fragColor += SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, lens + offset);
                            total += 1.0;
                        }
                    }
                    fragColor /= total;
                    
                    // Add lighting/gradient effects
                    float gradient = saturate((clamp(m2.y, 0.0, 0.2) + 0.1) / 2.0) + 
                                    saturate((clamp(-m2.y, -1000.0, 0.2) * rb3 + 0.1) / 2.0);
                    half4 lighting = saturate(fragColor + half4(rb1, rb1, rb1, 0) * gradient + half4(rb2, rb2, rb2, 0) * 0.3);
                    
                    // Blend distorted with original based on transition
                    half4 original = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, uv);
                    fragColor = lerp(original, lighting, transition);
                }
                else
                {
                    // Outside visor area - show original
                    fragColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, uv);
                }
                
                return fragColor;
            }
            ENDHLSL
        }
    }
}
