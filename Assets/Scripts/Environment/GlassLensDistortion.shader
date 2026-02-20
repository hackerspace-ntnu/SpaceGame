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
        Cull Off ZWrite Off ZTest Always
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "UnityCG.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };
            
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float2 _LensCenter;
            float _DistortionStrength;
            float _RoundedPower;
            float _BlurSize;
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            
            half4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float2 m2 = uv - _LensCenter;
                
                // Calculate aspect ratio
                float aspectRatio = _MainTex_TexelSize.z / _MainTex_TexelSize.w;
                
                // Rounded box calculation
                float roundedBox = pow(abs(m2.x * aspectRatio), _RoundedPower) + pow(abs(m2.y), _RoundedPower);
                
                // Masks
                float rb1 = saturate((1.0 - roundedBox * 10000.0) * 8.0);
                float rb2 = saturate((0.95 - roundedBox * 9500.0) * 16.0) - saturate(pow(0.9 - roundedBox * 9500.0, 1.0) * 16.0);
                float rb3 = saturate((1.5 - roundedBox * 11000.0) * 2.0) - saturate(pow(1.0 - roundedBox * 11000.0, 1.0) * 2.0);
                
                float transition = smoothstep(0.0, 1.0, rb1 + rb2);
                
                half4 fragColor = half4(0, 0, 0, 0);
                
                if (transition > 0.0)
                {
                    // Apply lens distortion
                    float2 lens = ((uv - 0.5) * (1.0 - roundedBox * _DistortionStrength) + 0.5);
                    
                    // Blur
                    float total = 0.0;
                    for (float x = -4.0; x <= 4.0; x += 1.0)
                    {
                        for (float y = -4.0; y <= 4.0; y += 1.0)
                        {
                            float2 offset = float2(x, y) * _BlurSize * _MainTex_TexelSize.xy;
                            fragColor += tex2D(_MainTex, lens + offset);
                            total += 1.0;
                        }
                    }
                    fragColor /= total;
                    
                    // Lighting
                    float gradient = saturate((clamp(m2.y, 0.0, 0.2) + 0.1) / 2.0) + 
                                    saturate((clamp(-m2.y, -1000.0, 0.2) * rb3 + 0.1) / 2.0);
                    half4 lighting = saturate(fragColor + half4(rb1, rb1, rb1, 0) * gradient + half4(rb2, rb2, rb2, 0) * 0.3);
                    
                    // Antialiasing - blend between distorted and original
                    half4 original = tex2D(_MainTex, uv);
                    fragColor = lerp(original, lighting, transition);
                }
                else
                {
                    fragColor = tex2D(_MainTex, uv);
                }
                
                return fragColor;
            }
            ENDCG
        }
    }
}
