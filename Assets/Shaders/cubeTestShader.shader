Shader "Custom/CubeTestShader"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white"
        _Scale("Scale",float) = 1
        _TimeScale("Time scale",float) =1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                float _Scale, _TimeScale;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            float3 Plasma(float2 uv){
                uv = uv*_Scale - _Scale/2;
                float time = _Time.y * _TimeScale;
                float w1 = sin(uv.x + time);
                float w2 = sin(uv.y + time) + 0.5;
                float w3 = sin(uv.x + uv.y + time);
                float r = sin(sqrt(uv.x*uv.x+uv.y*uv.y)+time);
                float finalvalue = r+ w1 + w2 + w3;
                float3 finalWave = float3(sin(finalvalue*3.1459),cos(finalvalue*3.1459),0);
                return finalWave*0.5 +0.5;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                //half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                float3 plasma = Plasma(IN.uv);
                half4 color = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,IN.uv + plasma.rg); //* _BaseColor;
                return color;
            }
            ENDHLSL
        }
    }
}
