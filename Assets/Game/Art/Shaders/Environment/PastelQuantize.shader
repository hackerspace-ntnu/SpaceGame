Shader "Hidden/PastelQuantize"
{
    // No properties: the palette, blend, ink and noise come from
    // PastelQuantizeRenderFeature each frame, so a material-level copy would only be a
    // second source of truth that drifts.
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
            // The speckle hash is integer arithmetic; 2.5 has no uint.
            #pragma target 3.5

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
            float _InkWidth;
            float _InkTint;
            float4 _InkColor;         // Oklab, converted once on the CPU
            float _NoiseKind;         // matches the NoiseKind enum
            float _NoiseAmount;
            float _NoiseScale;
            float _NoiseDensity;

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

                // Scaled by the line breadth: reaching further finds gentler edges as
                // well as drawing a wider line, which is why one dial does both.
                float2 texel = max(_InkWidth, 0.5) / _ScreenParams.xy;
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

            // Integer hash rather than frac(sin(...)): the sine version drifts between
            // drivers, and noise that changes with the driver is not a look.
            uint NoiseHash(uint2 cell, uint salt)
            {
                uint h = cell.x * 374761393u + cell.y * 668265263u + salt * 3266489917u;
                h = (h ^ (h >> 13u)) * 1274126177u;
                return h ^ (h >> 16u);
            }

            float NoiseCell(uint2 cell, uint salt)
            {
                return float(NoiseHash(cell, salt) & 0xFFFFFFu) / 16777215.0;
            }

            float ValueNoise(float2 p)
            {
                float2 corner = floor(p);
                float2 f = p - corner;
                // Smoothstep: bilinear alone leaves visible creases along the lattice.
                f = f * f * (3.0 - 2.0 * f);
                uint2 cell = uint2(int2(corner) + 4096);
                return lerp(
                    lerp(NoiseCell(cell, 0u),               NoiseCell(cell + uint2(1u, 0u), 0u), f.x),
                    lerp(NoiseCell(cell + uint2(0u, 1u), 0u), NoiseCell(cell + uint2(1u, 1u), 0u), f.x),
                    f.y);
            }

            // Applied before the snap, so the noise decides which palette entry a pixel
            // lands on and nothing off-palette is ever emitted. Screen-anchored on purpose:
            // these are marks on the picture, not on the world — see NoiseShape.
            float3 Noise(float3 okl, float2 uv)
            {
                if (_NoiseAmount <= 0.0 || _NoiseKind < 0.5)
                {
                    return okl;
                }

                float2 pixel = uv * _ScreenParams.xy;
                float shift = 0.0;

                if (_NoiseKind < 1.5) // Paper
                {
                    float2 p = pixel / max(_NoiseScale, 1.0);
                    float field = lerp(ValueNoise(p), ValueNoise(p * 2.37 + 17.0), 0.5);
                    shift = (field - 0.5) * 2.0;
                }
                else if (_NoiseKind < 2.5) // Dither
                {
                    shift = (NoiseCell(uint2(pixel), 1u) - 0.5) * 2.0;
                }
                else // Speckle
                {
                    uint2 cell = uint2(pixel / max(_NoiseScale, 1.0));
                    // One fleck per cell at most, and only in cells the density picks, so
                    // the result reads as spatter rather than as a texture.
                    if (NoiseCell(cell, 2u) < _NoiseDensity)
                    {
                        shift = -NoiseCell(cell, 3u);
                    }
                }

                okl.x = saturate(okl.x + shift * _NoiseAmount);
                return okl;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 source = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, input.texcoord);

                // Both move the colour before the snap rather than being composited
                // after it, so each lands on a palette entry instead of introducing a
                // colour the palette does not hold. The ink's edge detector reads the
                // untouched source, so noise never grows an outline of its own.
                float3 okl = LinearToOklab(saturate(source.rgb));
                okl = Noise(okl, input.texcoord);

                float edge = InkEdge(input.texcoord);
                okl.x = saturate(okl.x - _InkAmount * edge);
                // Toward the pen's own colour only as far as `tint` asks. At 0 a line stays
                // the hue of what it crosses, which is what keeps it reading as a wash
                // rather than as a sticker laid over the frame.
                okl = lerp(okl, _InkColor.xyz, saturate(_InkTint * edge));
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
