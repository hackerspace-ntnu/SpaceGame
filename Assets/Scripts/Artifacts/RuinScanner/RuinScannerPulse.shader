Shader "SpaceGame/RuinScannerPulse"
{
    // Cone-of-light beam shader. The mesh is a hollow cone shell (no caps), so
    // the trick is making the shell *read* like a soft volumetric beam from any
    // viewing angle — including looking straight down its axis.
    //
    // Two tricks:
    //   1. Body alpha based on `1 - depth` along the cone (brightest near the
    //      tip, dims toward the base). With additive blending and back faces
    //      enabled, this stacks brightness through both walls of the cone so
    //      it reads as a filled beam, not a hollow ring.
    //   2. Fresnel boost — back-faces and grazing edges get a slight extra
    //      lift so the silhouette is always defined even when the camera is
    //      inside the cone.
    Properties
    {
        _Color      ("Color",     Color) = (0.45, 0.95, 1.0, 1.0)
        _Intensity  ("Intensity", Range(0, 8))   = 1.6
        _BodyFalloff("Body Falloff (along cone)", Range(0.1, 4)) = 1.4
        _TipFade    ("Tip Fade (near origin)",    Range(0, 0.5)) = 0.1
        _FresnelBoost("Fresnel Boost",            Range(0, 2)) = 0.6
        _FresnelPower("Fresnel Power",            Range(0.5, 8)) = 2.5
        _Progress   ("Progress",  Range(0, 1))   = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off            // render both walls so additive stacking fills the beam
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha One  // additive

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float4 pos      : SV_POSITION;
                float2 uv       : TEXCOORD0;     // x = angular pos, y = 0 at tip..1 at base
                float3 worldPos : TEXCOORD1;
                float3 worldNrm : TEXCOORD2;
            };

            fixed4 _Color;
            float  _Intensity;
            float  _BodyFalloff;
            float  _TipFade;
            float  _FresnelBoost;
            float  _FresnelPower;
            float  _Progress;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos      = UnityObjectToClipPos(v.vertex);
                o.uv       = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNrm = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Depth along the cone (0 = tip, 1 = base).
                float depth = saturate(i.uv.y);

                // Bright body that fades out toward the base.
                float body = pow(saturate(1.0 - depth), _BodyFalloff);

                // Tip bias — keep the muzzle from being a hard hot dot.
                float tip = smoothstep(0.0, _TipFade, depth);

                // Fresnel glow — accentuates the silhouette when the camera is
                // inside or near the beam, and softens grazing angles.
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float  ndv     = saturate(abs(dot(normalize(i.worldNrm), viewDir)));
                float  fres    = pow(1.0 - ndv, _FresnelPower) * _FresnelBoost;

                // Pulse fade-out.
                float life = 1.0 - _Progress;

                float a    = saturate((body + fres) * tip * life);
                fixed3 rgb = _Color.rgb * _Intensity * a;
                return fixed4(rgb, a);
            }
            ENDCG
        }
    }
    FallBack Off
}
