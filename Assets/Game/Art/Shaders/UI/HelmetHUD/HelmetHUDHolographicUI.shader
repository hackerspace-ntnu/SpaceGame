Shader "UI/HelmetHUDHolographic"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (0.55, 0.95, 1.0, 1.0)
        _GlowColor ("Glow Color", Color) = (1.0, 1.0, 1.0, 1.0)

        _ScanlineDensity ("Scanline Density", Range(0, 800)) = 220
        _ScanlineStrength ("Scanline Strength", Range(0, 1)) = 0.35
        _ScanlineScrollSpeed ("Scanline Scroll Speed", Range(-10, 10)) = 1.5

        _EdgeFresnel ("Edge Fresnel", Range(0, 4)) = 1.6
        _ChromaShift ("Chroma Shift (UV)", Range(0, 0.05)) = 0.004

        _FlickerStrength ("Flicker Strength", Range(0, 1)) = 0.18
        _FlickerSpeed ("Flicker Speed", Range(0, 50)) = 18

        _Intensity ("Intensity (HDR boost)", Range(0, 6)) = 1.4
        _Alpha ("Alpha", Range(0, 1)) = 1.0

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha One
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _GlowColor;
            float _ScanlineDensity;
            float _ScanlineStrength;
            float _ScanlineScrollSpeed;
            float _EdgeFresnel;
            float _ChromaShift;
            float _FlickerStrength;
            float _FlickerSpeed;
            float _Intensity;
            float _Alpha;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            float hash(float n) { return frac(sin(n) * 43758.5453); }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;

                // Chromatic aberration sample
                float cs = _ChromaShift;
                float r = tex2D(_MainTex, uv + float2( cs, 0)).r;
                float g = tex2D(_MainTex, uv).g;
                float b = tex2D(_MainTex, uv + float2(-cs, 0)).b;
                float a = tex2D(_MainTex, uv).a;
                fixed4 col = fixed4(r, g, b, a);

                col *= IN.color;

                // Edge fresnel (distance from UV center)
                float2 d = uv - 0.5;
                float edge = saturate(length(d) * 2.0);
                float fresnel = pow(edge, _EdgeFresnel);
                col.rgb += _GlowColor.rgb * fresnel * 0.6;

                // Scanlines (horizontal, scroll over time)
                float scan = sin((uv.y + _Time.y * _ScanlineScrollSpeed * 0.05) * _ScanlineDensity);
                scan = 1.0 - _ScanlineStrength * (0.5 + 0.5 * scan);
                col.rgb *= scan;

                // Flicker
                float flicker = 1.0 - _FlickerStrength * hash(floor(_Time.y * _FlickerSpeed));
                col.rgb *= flicker;

                // HDR boost
                col.rgb *= _Intensity;

                col.a *= _Alpha;
                col.rgb *= col.a;

                return col;
            }
            ENDCG
        }
    }
}
