Shader "UI/HelmetHUDHologramSolid"
{
    // UI-compatible counterpart to Hologram/Solid: additive HDR fill driven by
    // the sprite's alpha (so the triangle/disc shape comes from the texture)
    // with a slow sine pulse. No scanlines, no chromatic aberration, no flicker.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color     ("Tint", Color)              = (0.45, 0.95, 1.0, 1.0)
        _Intensity ("Intensity", Range(0, 8))   = 2.0
        _Pulse     ("Pulse Strength", Range(0, 1)) = 0.18
        _PulseSpeed("Pulse Speed", Float)       = 1.4

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
        Blend One One
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
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

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float  _Intensity;
            float  _Pulse;
            float  _PulseSpeed;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float a = tex2D(_MainTex, IN.texcoord).a;
                float pulse = 1.0 + sin(_Time.y * _PulseSpeed * 6.2831) * _Pulse;
                fixed3 rgb = IN.color.rgb * _Intensity * pulse * a * IN.color.a;
                return fixed4(rgb, a * IN.color.a);
            }
            ENDCG
        }
    }
}
