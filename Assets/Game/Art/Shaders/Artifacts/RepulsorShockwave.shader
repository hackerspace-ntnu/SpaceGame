Shader "SpaceGame/Artifacts/RepulsorShockwave"
{
    // Expanding ground ring for the repulsor gauntlet's blast. The annulus mesh maps V across the
    // ring width (0 = inner edge, 1 = outer rim): a hot leading edge rides the outer rim, a faint
    // trailing skirt falls off behind it, and the whole wave dies out as _Progress reaches 1.
    Properties
    {
        _Color         ("Color",          Color)         = (0.55, 0.8, 1.0, 1.0)
        _Intensity     ("Intensity",      Range(0, 8))   = 3
        _Progress      ("Progress",       Range(0, 1))   = 0
        _TrailStrength ("Trail Strength", Range(0, 1))   = 0.35
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One   // additive
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Intensity;
            float _Progress;
            float _TrailStrength;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float across = saturate(i.uv.y);          // 0 inner edge -> 1 outer rim
                float lead = pow(across, 4.0);            // hot leading edge
                float trail = across * _TrailStrength;    // faint skirt behind it
                float fade = pow(saturate(1.0 - _Progress), 1.5); // wave dies as it lands
                float a = saturate(lead + trail) * fade;
                return fixed4(_Color.rgb * _Intensity * a, a * _Color.a);
            }
            ENDCG
        }
    }
    FallBack Off
}
