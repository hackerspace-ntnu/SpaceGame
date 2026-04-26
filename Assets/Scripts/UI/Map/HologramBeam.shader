Shader "Hologram/Beam"
{
    Properties
    {
        _Color    ("Tint", Color)            = (0.45, 0.95, 1.0, 1.0)
        _Intensity("Intensity", Range(0, 4)) = 0.6
        _ApexFade ("Apex Fade", Range(0, 1)) = 0.95
        _BaseFade ("Base Fade", Range(0, 1)) = 0.30
        _SoftEdge ("Soft Edge", Range(0, 1)) = 0.6
        _RayCount ("Ray Count", Range(0, 32))= 12
        _RayStrength ("Ray Strength", Range(0,1)) = 0.6
        _RaySharpness("Ray Sharpness", Range(1, 16)) = 5
        _RayDrift  ("Ray Drift", Float)      = 0.15
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

            // Cone with apex at (0,0,0), base at z=1, radius=1.
            // UV.x = around cone (0..1), UV.y = apex (0) to base (1).
            // Normal = outward from cone axis for a fresnel-style soft edge.

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos     : SV_POSITION;
                float2 uv      : TEXCOORD0;
                float3 worldP  : TEXCOORD1;
                float3 worldN  : TEXCOORD2;
                float3 viewDir : TEXCOORD3;
            };

            float4 _Color;
            float  _Intensity;
            float  _ApexFade, _BaseFade;
            float  _SoftEdge;
            float  _RayCount;
            float  _RayStrength;
            float  _RaySharpness;
            float  _RayDrift;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos     = UnityObjectToClipPos(v.vertex);
                o.uv      = v.uv;
                o.worldP  = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldN  = UnityObjectToWorldNormal(v.normal);
                o.viewDir = normalize(_WorldSpaceCameraPos - o.worldP);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Length-wise opacity: bright near apex, faints toward base.
                float lenA = lerp(_ApexFade, _BaseFade, saturate(i.uv.y));

                // Soft view-dependent edge for volumetric thickness.
                float viewDot = abs(dot(normalize(i.worldN), normalize(i.viewDir)));
                float side = pow(1.0 - viewDot, _SoftEdge * 4.0 + 1.0);

                // Discrete rays along the cone surface — FIXED angular positions
                // (no rotation, so it doesn't read as a spinning hypnosis pattern).
                float angle = i.uv.x;
                float ray = 0.5 + 0.5 * cos(angle * _RayCount * 6.2831);
                ray = pow(saturate(ray), _RaySharpness);

                // Each ray fades from bright at apex to dim at base.
                float lengthFade = saturate(1.0 - i.uv.y);
                lengthFade = pow(lengthFade, 1.4);

                // Subtle pulse traveling apex → base (faint flicker, not rotation).
                float pulse = 0.85 + 0.15 * sin(i.uv.y * 8.0 - _Time.y * _RayDrift * 3.0);

                // Final ray fill = static streaks * length fade * pulse.
                float rayFill = ray * lengthFade * pulse;

                // Mix: soft base cone + sharp ray streaks on top.
                float baseFill = side * (1.0 - _RayStrength);
                float fill = baseFill + rayFill * _RayStrength;

                fixed3 rgb = _Color.rgb * _Intensity * lenA * fill;
                return fixed4(rgb, 1.0);
            }
            ENDCG
        }
    }
}
