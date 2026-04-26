Shader "Hologram/MapProjection"
{
    Properties
    {
        _MainTex          ("Render Texture", 2D)        = "black" {}
        _Color            ("Tint", Color)               = (0.3, 0.95, 1.0, 1.0)
        _Intensity        ("Intensity", Range(0, 5))    = 1.6
        _ScanlineSpeed    ("Scanline Speed", Float)     = 1.4
        _ScanlineDensity  ("Scanline Density", Float)   = 90
        _ScanlineStrength ("Scanline Strength", Range(0,1)) = 0.18
        _EdgeFade         ("Edge Fade", Range(0, 0.5))  = 0.08
        _FlickerAmount    ("Flicker Amount", Range(0,1))= 0.06
        _RimWidth         ("Rim Width", Range(0,0.5))   = 0.06
        _RimStrength      ("Rim Strength", Range(0,4))  = 1.5
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "DisableBatching"="True"
        }

        Cull   Off
        Lighting Off
        ZWrite Off
        ZTest  LEqual
        Blend  One One   // Additive: black RT pixels become invisible, bright glows.

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex   : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4    _Color;
            float     _Intensity;
            float     _ScanlineSpeed;
            float     _ScanlineDensity;
            float     _ScanlineStrength;
            float     _EdgeFade;
            float     _FlickerAmount;
            float     _RimWidth;
            float     _RimStrength;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.texcoord;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 src = tex2D(_MainTex, i.uv);

                // Edge fade — soft falloff at the borders so the projection
                // doesn't cut off as a hard rectangle.
                float ex = smoothstep(0.0, _EdgeFade, i.uv.x) * smoothstep(0.0, _EdgeFade, 1.0 - i.uv.x);
                float ey = smoothstep(0.0, _EdgeFade, i.uv.y) * smoothstep(0.0, _EdgeFade, 1.0 - i.uv.y);
                float edge = ex * ey;

                // Rim glow — bright outline near the inner edge.
                float rx = step(i.uv.x, _RimWidth) + step(1.0 - _RimWidth, i.uv.x);
                float ry = step(i.uv.y, _RimWidth) + step(1.0 - _RimWidth, i.uv.y);
                float rim = saturate(rx + ry) * _RimStrength;

                // Scanline — moves vertically over time.
                float scan = 1.0 - _ScanlineStrength
                             + _ScanlineStrength * sin((i.uv.y + _Time.y * _ScanlineSpeed) * _ScanlineDensity);

                // Flicker — high-frequency low-amplitude wobble.
                float flick = 1.0 + sin(_Time.y * 27.3) * _FlickerAmount
                                  + sin(_Time.y * 71.7) * _FlickerAmount * 0.4;

                fixed3 rgb = (src.rgb + rim * _Color.rgb) * _Color.rgb * _Intensity * scan * flick * edge;
                // Pre-mult by source alpha so transparent areas of the RT add nothing.
                rgb *= src.a + rim;

                return fixed4(rgb, 1.0);
            }
            ENDCG
        }
    }
}
