Shader "Hidden/GlassLensDistortion"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _LensCenter ("Lens Center", Vector) = (0.5, 0.5, 0, 0)
        _LensRadius ("Lens Radius", Float) = 0.35
        _DistortionStrength ("Distortion Strength", Range(0, 5000)) = 0.7
        _ChromaticAberration ("Chromatic Aberration", Range(0, 1)) = 0.2
        _LensZoom ("Lens Zoom", Range(1, 3)) = 1.5
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
            float _LensRadius;
            float _DistortionStrength;
            float _ChromaticAberration;
            float _LensZoom;
            
            // Get distorted UV based on direction and factor
            float2 GetDistortedUv(float2 uv, float2 direction, float factor)
            {
                return uv - direction * factor;
            }
            
            // Struct to hold distortion results
            struct DistortedLens
            {
                float2 uv_R;
                float2 uv_G;
                float2 uv_B;
                float focusSdf;
                float sphereSdf;
                float inside;
            };
            
            // Calculate lens distortion with chromatic aberration
            DistortedLens GetLensDistortion(
                float2 p,
                float2 uv,
                float2 sphereCenter,
                float sphereRadius,
                float focusFactor,
                float chromaticAberrationFactor
            )
            {
                float2 distortionDirection = normalize(p - sphereCenter);
                
                float focusRadius = sphereRadius * focusFactor;
                float focusStrength = sphereRadius / 2000.0;
                
                float focusSdf = length(sphereCenter - p) - focusRadius;
                float sphereSdf = length(sphereCenter - p) - sphereRadius;
                float inside = clamp(-sphereSdf / fwidth(sphereSdf), 0.0, 1.0);
                
                float magnifierFactor = focusSdf / (sphereRadius - focusRadius);
                
                float mFactor = clamp(magnifierFactor * inside, 0.0, 1.0);
                mFactor = pow(mFactor, 4.0);
                
                float3 distortionFactors = float3(
                    mFactor * focusStrength * (1.0 + chromaticAberrationFactor),
                    mFactor * focusStrength,
                    mFactor * focusStrength * (1.0 - chromaticAberrationFactor)
                );
                
                float2 uv_R = GetDistortedUv(uv, distortionDirection, distortionFactors.r);
                float2 uv_G = GetDistortedUv(uv, distortionDirection, distortionFactors.g);
                float2 uv_B = GetDistortedUv(uv, distortionDirection, distortionFactors.b);
                
                DistortedLens result;
                result.uv_R = uv_R;
                result.uv_G = uv_G;
                result.uv_B = uv_B;
                result.focusSdf = focusSdf;
                result.sphereSdf = sphereSdf;
                result.inside = inside;
                
                return result;
            }
            
            // Zoom UV around a center point
            float2 ZoomUV(float2 uv, float2 center, float zoom)
            {
                float zoomFactor = 1.0 / zoom;
                float2 centeredUV = uv - center;
                centeredUV *= zoomFactor;
                return centeredUV + center;
            }
            
            half4 frag (Varyings input) : SV_Target
            {
                float2 vUv = input.texcoord;
                float2 p = vUv * _BlitTexture_TexelSize.zw; // Convert to pixel coordinates
                
                float2 textureSize = _BlitTexture_TexelSize.zw;
                float2 sphereCenter = _LensCenter * textureSize;
                
                float sphereRadius = textureSize.y * _LensRadius;
                
                // Zoom the UV
                float2 zoomedUv = ZoomUV(vUv, _LensCenter, _LensZoom);
                
                // Get lens distortion
                DistortedLens distortion = GetLensDistortion(
                    p,
                    zoomedUv,
                    sphereCenter,
                    sphereRadius,
                    _DistortionStrength,
                    _ChromaticAberration
                );
                
                // Sample each color channel separately
                float imageDistorted_R = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, distortion.uv_R).r;
                float imageDistorted_G = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, distortion.uv_G).g;
                float imageDistorted_B = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, distortion.uv_B).b;
                
                half3 imageDistorted = half3(imageDistorted_R, imageDistorted_G, imageDistorted_B);
                
                // Sample original image
                half3 image = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, vUv).rgb;
                
                // Mix between original and distorted based on the lens mask
                half3 result = lerp(image, imageDistorted, distortion.inside);
                
                return half4(result, 1.0);
            }
            ENDHLSL
        }
    }
}
