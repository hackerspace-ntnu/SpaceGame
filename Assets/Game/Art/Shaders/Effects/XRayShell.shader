Shader "SpaceGame/XRayShell"
{
    // The cartoon "electrocuted" look: the body turns to a glowing glass outline and the skeleton
    // shows through it. Drawn on a GHOST copy of each body renderer (ShockedVisual), never on the
    // body's own materials, so nothing about the body has to be put back afterwards.
    //
    // Additive, so it can only add light: what shows through the shell is whatever is behind it,
    // which during a shock is the skeleton (drawn opaque, before the transparent queue) and the
    // ground. A rim (fresnel) carries the silhouette; a faint fill keeps the body's volume; a band
    // of current crawling up the body in world metres keeps it from reading as a static outline.
    Properties
    {
        [HDR] _RimColor  ("Rim",  Color) = (0.35, 0.85, 2.4, 1)
        [HDR] _FillColor ("Fill", Color) = (0.02, 0.07, 0.16, 1)
        _RimPower     ("Rim Power",               Range(0.5, 8)) = 2.2
        [HDR] _BandColor ("Current band", Color) = (0.6, 1.2, 2.6, 1)
        _BandScale    ("Band cycles per metre",   Range(0.5, 20)) = 4
        _BandSpeed    ("Band speed (m per sec)",  Range(0, 20)) = 6
        _BandSharpness("Band sharpness",          Range(1, 32)) = 10
        _Intensity    ("Intensity",               Range(0, 4)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "XRayShell"

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _RimColor;
                float4 _FillColor;
                float  _RimPower;
                float4 _BandColor;
                float  _BandScale;
                float  _BandSpeed;
                float  _BandSharpness;
                float  _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float rim = pow(1.0 - saturate(dot(n, v)), _RimPower);

                // A thin bright band every 1/_BandScale metres, travelling up the body.
                float phase = frac(i.positionWS.y * _BandScale - _Time.y * _BandSpeed);
                float band = pow(1.0 - abs(phase * 2.0 - 1.0), _BandSharpness);

                float3 colour = _FillColor.rgb + _RimColor.rgb * rim + _BandColor.rgb * band * (0.25 + rim);
                return half4(colour * _Intensity, 1.0);
            }
            ENDHLSL
        }
    }
}
