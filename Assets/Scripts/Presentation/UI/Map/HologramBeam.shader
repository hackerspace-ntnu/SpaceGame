Shader "Hologram/Beam"
{
    // Cone of straight thin streaks. One streak per fan quad. Each quad is a
    // narrow trapezoid from apex (z=0) to base ring (z=1). UVs:
    //   u.x = across width, 0 = left edge, 1 = right edge
    //   u.y = along length, 0 = apex, 1 = base
    //   vertex.color.r = per-ray seed (0..1) for independent shimmer
    Properties
    {
        _Color    ("Tint", Color)            = (0.55, 0.90, 1.00, 1.0)
        _Intensity("Intensity", Range(0, 6)) = 1.4

        _ApexFade      ("Apex Brightness", Range(0, 2)) = 1.0
        _BaseFade      ("Base Brightness", Range(0, 1)) = 0.0
        _EdgeSharpness ("Edge Sharpness",  Range(1, 8)) = 2.0
        _Shimmer       ("Shimmer",         Range(0, 1)) = 0.45
        _ShimmerSpeed  ("Shimmer Speed",   Float)       = 0.6
    }

    SubShader
    {
        Tags { "Queue"="Overlay+1" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Cull   Off
        Lighting Off
        ZWrite Off
        ZTest  LEqual
        Blend  One One

        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
                float  seed: TEXCOORD1;
            };

            float4 _Color;
            float  _Intensity;
            float  _ApexFade, _BaseFade;
            float  _EdgeSharpness;
            float  _Shimmer, _ShimmerSpeed;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos  = UnityObjectToClipPos(v.vertex);
                o.uv   = v.uv;
                o.seed = v.color.r;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float u = i.uv.x;
                float v = saturate(i.uv.y);

                // Single thin streak per quad: triangle peak at center.
                float profile = 1.0 - abs(2.0 * u - 1.0);
                profile = pow(saturate(profile), _EdgeSharpness);

                // Apex→base brightness curve.
                float lenA = lerp(_ApexFade, _BaseFade, v);

                // Per-ray independent shimmer.
                float phase = i.seed * 6.2831853;
                float shimmerWave = 0.5 + 0.5 * sin(_Time.y * _ShimmerSpeed * 6.2831853 + phase);
                float shimmer = lerp(1.0, shimmerWave, _Shimmer);

                float bright = profile * lenA * shimmer;
                fixed3 rgb = _Color.rgb * _Intensity * bright;
                return fixed4(rgb, 1.0);
            }
            ENDCG
        }
    }
}
