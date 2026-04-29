Shader "UI/HelmetHUDDangerVignette"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 0.15, 0.15, 1)
        _Side ("Side (0=left,1=right,2=top,3=bottom)", Float) = 0
        _Falloff ("Edge Falloff Power", Range(0.5, 6)) = 2.4
        _Width ("Vignette Width (0..1)", Range(0.05, 0.6)) = 0.32
        _Pulse ("Pulse Phase 0..1", Range(0, 1)) = 1
        _ScanDensity ("Scanline Density", Range(0, 600)) = 180
        _ScanStrength ("Scanline Strength", Range(0, 1)) = 0.35
        _Intensity ("Intensity", Range(0, 6)) = 2.0

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
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

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
            };

            fixed4 _Color;
            float _Side;
            float _Falloff;
            float _Width;
            float _Pulse;
            float _ScanDensity;
            float _ScanStrength;
            float _Intensity;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;

                // Distance from the "danger edge"
                float dist;
                if (_Side < 0.5)        dist = uv.x;            // left edge -> low x
                else if (_Side < 1.5)   dist = 1.0 - uv.x;      // right edge -> high x
                else if (_Side < 2.5)   dist = 1.0 - uv.y;      // top edge -> high y
                else                    dist = uv.y;            // bottom edge -> low y

                // Normalize within the vignette band width
                float t = saturate(dist / _Width);
                // Inverse falloff so brightest near the edge
                float intensity = pow(1.0 - t, _Falloff);

                // Subtle horizontal scanlines (vertical for top/bottom)
                float scanCoord = (_Side < 1.5) ? uv.y : uv.x;
                float scan = 0.5 + 0.5 * sin(scanCoord * _ScanDensity + _Time.y * 6.0);
                intensity *= 1.0 - _ScanStrength * (1.0 - scan);

                fixed4 col = _Color;
                col.rgb *= _Intensity;
                col.a = intensity * _Pulse * IN.color.a;
                col.rgb *= col.a;
                return col;
            }
            ENDCG
        }
    }
}
