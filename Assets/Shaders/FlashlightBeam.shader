Shader "SpaceGame/FlashlightBeam"
{
    Properties
    {
        _Color("Beam Color", Color) = (1, 0.95, 0.85, 1)
        _Intensity("Intensity", Range(0, 5)) = 1.2
        _EdgeFalloff("Edge Falloff (fresnel power)", Range(0.5, 8)) = 2.5
        _NearFade("Near Fade Distance", Range(0, 5)) = 0.5
        _FarFade("Far Fade Start (0-1 along length)", Range(0, 1)) = 0.7
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        LOD 100

        Pass
        {
            Name "Beam"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 viewDirWS   : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                float  depthVS     : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Intensity;
                float  _EdgeFalloff;
                float  _NearFade;
                float  _FarFade;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = p.positionCS;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS   = GetWorldSpaceViewDir(p.positionWS);
                OUT.uv          = IN.uv;
                OUT.depthVS     = -TransformWorldToView(p.positionWS).z;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(IN.viewDirWS);
                // Soft edges: where the cone surface is grazing the view ray, alpha is highest.
                float ndv = saturate(1.0 - abs(dot(N, V)));
                float edge = pow(ndv, _EdgeFalloff);

                // Length fade along the cone (uv.y goes 0 at tip -> 1 at far end with our procedural mesh)
                float lengthFade = smoothstep(0.0, 0.15, IN.uv.y) * (1.0 - smoothstep(_FarFade, 1.0, IN.uv.y));

                // Near-camera fade so it doesn't pop in the helmet
                float nearFade = saturate(IN.depthVS / max(_NearFade, 0.0001));

                float a = edge * lengthFade * nearFade * _Intensity;
                return half4(_Color.rgb, a * _Color.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
