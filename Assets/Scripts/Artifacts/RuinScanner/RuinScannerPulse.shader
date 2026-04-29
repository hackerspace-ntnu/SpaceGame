Shader "SpaceGame/RuinScannerPulse"
{
    // Soft downward cone of light. Reads as a scanner beam pointing at the ground
    // — no fullscreen flash. Drawn additively, ZTest LEqual so geometry properly
    // occludes the cone (it stops at the ground rather than punching through).
    Properties
    {
        _Color     ("Color",     Color) = (0.45, 0.95, 1.0, 1.0)
        _Intensity ("Intensity", Range(0, 8))   = 1.6
        _EdgeSoft  ("Edge Softness (radial falloff)", Range(0, 1)) = 0.55
        _TipFade   ("Tip Fade (near origin)",         Range(0, 1)) = 0.25
        _Progress  ("Progress",  Range(0, 1))   = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha One   // additive

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                // x = radial distance from cone axis [0..1], y = depth along cone [0=tip, 1=base]
                float2 uv  : TEXCOORD0;
            };

            fixed4 _Color;
            float  _Intensity;
            float  _EdgeSoft;
            float  _TipFade;
            float  _Progress;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Soft radial falloff from the axis to the rim.
                float radial = saturate(1.0 - smoothstep(1.0 - _EdgeSoft, 1.0, i.uv.x));
                // Fade in from the tip so the device itself isn't washed out.
                float tip    = smoothstep(0.0, _TipFade, i.uv.y);
                // Fade out as the pulse winds down.
                float life   = 1.0 - _Progress;

                float a      = radial * tip * life;
                fixed3 rgb   = _Color.rgb * _Intensity * a;
                return fixed4(rgb, a);
            }
            ENDCG
        }
    }
    FallBack Off
}
