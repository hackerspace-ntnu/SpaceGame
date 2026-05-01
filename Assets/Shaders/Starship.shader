Shader "Custom/Starship"
{
    Properties
    {
        _MainTex ("Noise Texture", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (0.35, 0.75, 1.25, 1.0)
        _Scale ("Scale", Float) = 1.0
        _Speed ("Speed", Float) = 1.0
        _Brightness ("Brightness", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

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
            float4 _MainTex_ST;
            float4 _BaseColor;
            float _Scale;
            float _Speed;
            float _Brightness;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float2 I = i.uv * 2.0;
                float2 r = float2(0.25, 0.4);
                
                // Manual matrix multiply for rotation: mat2(1,4,4,-3)
                float2 p = I + I - r;
                p = p / r.y / 1e2;
                p = float2(1.0 * p.x + 4.0 * p.y, 4.0 * p.x - 3.0 * p.y);
                
                p.x -= 0.07;
                p.y -= 0.16;
                
                // Rotation matrix
                float a = 0.2;
                float c = cos(a);
                float s = sin(a);
                float2 p_rot = float2(c * p.x - s * p.y, s * p.x + c * p.y);
                p = p_rot;
                
                float2 q = p;
                
                float4 S = float4(0, 0, 0, 0);
                float4 C = float4(1, 2, 3, 0);
                float4 W;
                
                float t = _Time.y * _Speed;
                float T = 0.1 * t + p.y;
                
                for(float i = 0.0; i < 50.0; i++)
                {
                    W = sin(i) * C;
                    float sinVal = sin(i + i * T);
                    
                    float2 texCoord = p / exp(W.x) + float2(i, t) / 8.0;
                    float texVal = tex2D(_MainTex, texCoord).r * 40.0;
                    float2 maxVec = max(p, p / float2(2.0, texVal));
                    float len = length(maxVec);
                    
                    // exp approximation for small values
                    float brightFactor = exp(sinVal);
                    
                    S += (cos(W) + 1.5)
                        * brightFactor
                        / (len + 0.001)
                        / 1e4;
                    
                    p += 0.02 * cos(i * (C.xz + 2.0 + i) + T + T);
                }
                
                
                // Simple tanh approximation with color tint
                float3 srgb = S.rgb * S.rgb * _Brightness;
                srgb = (exp(2.0 * srgb) - 1.0) / (exp(2.0 * srgb) + 1.0);
                srgb *= _BaseColor.rgb;
                
                float falloff = 1.0 - smoothstep(0.15, 0.65, abs(q.x) * 9.0) - smoothstep(0.15, 0.65, abs(q.y) * 3.0);
                falloff = max(0.0, falloff);
                
                float alpha = max(length(S.rgb * S.rgb * _Brightness), 0.0) * falloff;
                
                return float4(srgb, alpha);
            }
            ENDCG
        }
    }
}

