Shader "Hidden/PastelQuantize"
{
    // No properties: the palette, blend and ink come from PastelQuantizeRenderFeature
    // each frame, so a material-level copy would only be a second source of truth that
    // drifts.
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
            // _CameraDepthTexture + SampleSceneDepth, for the silhouette half of the ink.
            // The pass both calls ConfigureInput(Depth), which is what makes URP produce
            // the texture at all, and UseAllGlobalTextures, which is what lets a pass this
            // late in the frame read it. Miss either and the depth gradient reads flat and
            // only the lightness edges draw — no error, so check here first if silhouettes
            // go missing.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

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
            float _InkAmount;
            float _InkLumaThreshold;
            float _InkDepthThreshold;
            float _InkSoftness;

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

            // The exact inverse, mirroring the second half of PastelPalette.OklchToLinear.
            // The ink is cut in Oklab and the result is handed back as linear colour, so
            // both directions are needed in one frame.
            float3 OklabToLinear(float3 okl)
            {
                float3 lms = float3(
                    okl.x + 0.3963377774 * okl.y + 0.2158037573 * okl.z,
                    okl.x - 0.1055613458 * okl.y - 0.0638541728 * okl.z,
                    okl.x - 0.0894841775 * okl.y - 1.2914855480 * okl.z);
                lms = lms * lms * lms;
                return float3(
                    dot(lms, float3( 4.0767416621, -3.3077115913,  0.2309699292)),
                    dot(lms, float3(-1.2684380046,  2.6097574011, -0.3413193965)),
                    dot(lms, float3(-0.0041960863, -0.7034186147,  1.7076147010)));
            }

            float SampleLightness(float2 uv)
            {
                float3 c = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, uv).rgb;
                return LinearToOklab(saturate(c)).x;
            }

            // The pen on top of the watercolour: how strongly a line is drawn here, 0..1.
            //
            // Two sources, combined with max rather than added, so a silhouette that is
            // also a shading boundary does not come out twice as dark as either. Lightness
            // gradients give the interior strokes — creases, shading boundaries, the edge
            // of a shadow — and depth gradients give the silhouettes that unlit edges have
            // no contrast to reveal.
            float InkEdge(float2 uv)
            {
                if (_InkAmount <= 0.0)
                {
                    return 0.0;
                }

                float2 texel = 1.0 / _ScreenParams.xy;
                float2 dx = float2(texel.x, 0.0);
                float2 dy = float2(0.0, texel.y);

                // Central differences rather than a full Sobel: 4 taps instead of 8, and
                // at one pixel the extra smoothing of a Sobel only widens the line.
                float lumaGradient =
                    abs(SampleLightness(uv - dx) - SampleLightness(uv + dx)) +
                    abs(SampleLightness(uv - dy) - SampleLightness(uv + dy));

                float depth = Linear01Depth(SampleSceneDepth(uv), _ZBufferParams);
                float depthGradient =
                    abs(Linear01Depth(SampleSceneDepth(uv - dx), _ZBufferParams) -
                        Linear01Depth(SampleSceneDepth(uv + dx), _ZBufferParams)) +
                    abs(Linear01Depth(SampleSceneDepth(uv - dy), _ZBufferParams) -
                        Linear01Depth(SampleSceneDepth(uv + dy), _ZBufferParams));
                // Relative to the depth here: an absolute threshold inks every distant
                // surface seen at a glancing angle and nothing at all up close.
                depthGradient /= max(depth, 1e-4);

                return max(
                    smoothstep(_InkLumaThreshold, _InkLumaThreshold + _InkSoftness, lumaGradient),
                    smoothstep(_InkDepthThreshold, _InkDepthThreshold + _InkSoftness, depthGradient));
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 source = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, input.texcoord);

                // Ink darkens lightness before the snap rather than compositing a line
                // after it, so the line lands on a palette entry a step or two below what
                // it crosses instead of introducing a colour the palette does not hold.
                float3 okl = LinearToOklab(saturate(source.rgb));
                okl.x = saturate(okl.x - _InkAmount * InkEdge(input.texcoord));
                float3 painted = OklabToLinear(okl);

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

                // Against the painted colour, not the raw sample: _Blend is the snap's own
                // mix, and everything before it has its own amplitude to turn down.
                half3 result = lerp(painted, _PaletteLinear[best].rgb, _Blend);
                return half4(saturate(result), source.a);
            }
            ENDHLSL
        }
    }
}
