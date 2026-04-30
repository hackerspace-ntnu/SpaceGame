Shader "SpaceGame/RuinSecretReveal"
{
    Properties
    {
        _Color        ("Color",          Color) = (0.45, 0.95, 1.0, 1.0)
        _Intensity    ("Intensity",      Range(0, 8)) = 2.0
        _RimPower     ("Rim Power",      Range(0.1, 8)) = 2.0
        _ScanSpeed    ("Scan Speed",     Range(0, 4)) = 0.6
        _ScanWidth    ("Scan Width",     Range(0.001, 0.5)) = 0.04
        _ScanFrequency("Scan Frequency", Range(0.5, 30)) = 6.0
        _RevealAlpha  ("Reveal Alpha",   Range(0, 1)) = 1.0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha One   // additive — works well on top of dark ruin geometry

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNrm : TEXCOORD1;
            };

            fixed4 _Color;
            float  _Intensity;
            float  _RimPower;
            float  _ScanSpeed;
            float  _ScanWidth;
            float  _ScanFrequency;
            float  _RevealAlpha;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos      = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNrm = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Fresnel rim — outlines the silhouette of the hidden object.
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float  ndv     = saturate(dot(normalize(i.worldNrm), viewDir));
                float  rim     = pow(1.0 - ndv, _RimPower);

                // Vertical scanline sweeping along world Y. ('line' is reserved on Metal — call it scan.)
                float  band    = frac(i.worldPos.y * _ScanFrequency - _Time.y * _ScanSpeed);
                float  scan    = smoothstep(_ScanWidth, 0.0, abs(band - 0.5));

                float  bodyLit = 0.18; // soft body so the silhouette is always faintly readable
                float  bright  = saturate(bodyLit + rim + scan * 0.8);

                float  a       = bright * _RevealAlpha;
                fixed3 rgb     = _Color.rgb * _Intensity * a;
                return fixed4(rgb, a);
            }
            ENDCG
        }
    }
    FallBack Off
}
