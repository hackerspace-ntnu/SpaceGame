// Data passes for the wanted-poster portrait (PortraitCapture). Editor only; never in a build.
//   _PassMode 0: view-space normal * 0.5 + 0.5 in rgb, eye depth in metres in alpha (float target).
//   _PassMode 1: _PassId as written, a flat id colour per material slot (linear 8-bit target).
// Cull Off with the normal flipped on back faces, because capes and coats are single-sided.
Shader "Hidden/SpaceGame/PortraitPasses"
{
    Properties
    {
        _PassMode ("Pass Mode", Float) = 0
        _PassId ("Pass Id", Vector) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "PortraitData"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _PassMode;
                float4 _PassId;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalVS : TEXCOORD0;
                float eyeDepth : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.normalVS = TransformWorldToViewDir(TransformObjectToWorldNormal(input.normalOS));
                output.eyeDepth = -position.positionVS.z;
                return output;
            }

            float4 Frag(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                float3 normal = normalize(input.normalVS) * (frontFace ? 1.0 : -1.0);
                float4 data = float4(normal * 0.5 + 0.5, input.eyeDepth);
                return _PassMode > 0.5 ? _PassId : data;
            }
            ENDHLSL
        }
    }
}
