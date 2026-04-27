Shader "Hologram/Beam"
{
    // Sunray fan: a small set of flat triangle quads radiating from a
    // shared apex, each one a single ray. The fan rotates as a whole via
    // the GameObject's transform Z-roll. Per-quad shader work:
    //   - soft horizontal profile (bright at width center, fading to edges)
    //   - apex→base length fade
    //   - per-quad shimmer driven by a stable per-quad seed encoded in
    //     vertex.color.r so each ray brightens/dims independently.
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
                float4 color  : COLOR; // r = per-ray seed (0..1)
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
                float u = i.uv.x; // across width: 0 = left edge, 1 = right edge
                float v = saturate(i.uv.y); // apex 0 → base 1

                // Soft horizontal profile: 1 at center, fading to edges.
                // 1 - |2u - 1| is a triangle that peaks at center; raise to a
                // power for sharper falloff.
                float profile = 1.0 - abs(2.0 * u - 1.0);
                profile = pow(saturate(profile), _EdgeSharpness);

                // Length fade: bright at apex, dim at base.
                float lenA = lerp(_ApexFade, _BaseFade, v);

                // Per-ray shimmer using the seed encoded in vertex color.
                // Each ray gets an independent slow sine-driven brightness
                // multiplier in [1 - shimmer, 1 + shimmer * 0.5].
                float phase = i.seed * 6.2831853;
                float shimmerWave = 0.5 + 0.5 * sin(_Time.y * _ShimmerSpeed * 6.2831853 + phase);
                float shimmer = lerp(1.0, shimmerWave, _Shimmer);

                // Final brightness.
                float bright = profile * lenA * shimmer;

                fixed3 rgb = _Color.rgb * _Intensity * bright;
                return fixed4(rgb, 1.0);
            }
            ENDCG
        }
    }
}
