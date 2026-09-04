Shader "Hidden/PastelQuantize"
{
    // No properties: the palette and blend come from PastelQuantizeRenderFeature each
    // frame, so a material-level copy would only be a second source of truth that drifts.
    Properties { }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "PastelQuantize"

            HLSLPROGRAM
            #pragma vertex FullscreenVert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord   : TEXCOORD0;
            };

            Varyings FullscreenVert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.texcoord = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            TEXTURE2D(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            // Must equal MaxPaletteSize in PastelQuantizeRenderFeature.cs: a material's
            // vector-array size freezes the first time it is set, so the C# side always
            // uploads exactly this many entries and uses _PaletteCount as the loop bound.
            #define MAX_PALETTE 256

            float4 _PaletteLinear[MAX_PALETTE]; // linear RGB, what gets written out
            float4 _PaletteOklab[MAX_PALETTE];  // CPU-precomputed, what gets matched against
            int _PaletteCount;
            float _Blend;

            // Bjorn Ottosson's Oklab. Nearest-neighbour distances here track perceived
            // colour; the same search in raw RGB drags greens toward grey and crushes blues.
            float3 LinearToOklab(float3 c)
            {
                float3 lms = float3(
                    dot(c, float3(0.4122214708, 0.5363325363, 0.0514459929)),
                    dot(c, float3(0.2119034982, 0.6806995451, 0.1073969566)),
                    dot(c, float3(0.0883024619, 0.2817188376, 0.6299787005)));
                lms = pow(max(lms, 0.0), 1.0 / 3.0);
                return float3(
                    dot(lms, float3(0.2104542553,  0.7936177850, -0.0040720468)),
                    dot(lms, float3(1.9779984951, -2.4285922050,  0.4505937099)),
                    dot(lms, float3(0.0259040371,  0.7827717662, -0.8086757660)));
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 source = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, input.texcoord);
                float3 okl = LinearToOklab(saturate(source.rgb));

                int best = 0;
                float bestDist = 1e10;
                for (int i = 0; i < _PaletteCount; i++)
                {
                    float3 d = okl - _PaletteOklab[i].xyz;
                    float dist = dot(d, d);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = i;
                    }
                }

                half3 result = lerp(source.rgb, _PaletteLinear[best].rgb, _Blend);
                return half4(saturate(result), source.a);
            }
            ENDHLSL
        }
    }
}
