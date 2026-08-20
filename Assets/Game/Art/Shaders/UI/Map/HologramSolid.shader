Shader "Hologram/Solid"
{
    Properties
    {
        _Color    ("Tint", Color)            = (0.45, 0.95, 1.0, 1.0)
        _Intensity("Intensity", Range(0, 8)) = 2.0
        _Pulse    ("Pulse Strength", Range(0, 1)) = 0.0
        _PulseSpeed("Pulse Speed", Float)    = 3.0
    }

    SubShader
    {
        Tags { "Queue"="Overlay+2" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Cull   Off
        Lighting Off
        ZWrite Off
        ZTest  Always
        Blend  One One

        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f     { float4 pos    : SV_POSITION; };

            float4 _Color;
            float  _Intensity;
            float  _Pulse;
            float  _PulseSpeed;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float pulse = 1.0 + sin(_Time.y * _PulseSpeed * 6.2831) * _Pulse;
                fixed3 rgb = _Color.rgb * _Intensity * pulse;
                return fixed4(rgb, 1.0);
            }
            ENDCG
        }
    }
}
