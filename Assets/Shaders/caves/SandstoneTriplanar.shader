Shader "SpaceGame/SandstoneTriplanar"
{
    Properties
    {
        [Header(Triplanar Rock Colors  Darker base browns)]
        _ColorX ("Color X (Side walls)", Color) = (0.32, 0.22, 0.14, 1)
        _ColorY ("Color Y (Floor / ceiling)", Color) = (0.38, 0.26, 0.16, 1)
        _ColorZ ("Color Z (Front / back walls)", Color) = (0.28, 0.18, 0.12, 1)
        _BlendSharpness ("Triplanar Blend Sharpness", Range(1, 16)) = 4

        [Header(Sand Blobs  Big stylized patches of lighter sandy colors)]
        _BlobScale ("Blob Scale (smaller = bigger blobs)", Range(0.02, 1)) = 0.12
        _BlobThreshold ("Blob Threshold (lower = more blobs)", Range(0, 1)) = 0.5
        _BlobEdgeSoftness ("Blob Edge Softness (lower = more stylized hard edges)", Range(0.001, 0.3)) = 0.04
        _BlobStrength ("Blob Color Strength", Range(0, 1)) = 1.0

        [Header(Sand Blob Palette  3 lighter tones)]
        _SandColor1 ("Sand Color 1 (Light Yellow)", Color) = (0.92, 0.82, 0.55, 1)
        _SandColor2 ("Sand Color 2 (Sand Brown)", Color) = (0.78, 0.62, 0.38, 1)
        _SandColor3 ("Sand Color 3 (Warm Tan)", Color) = (0.85, 0.70, 0.45, 1)
        _SandMixScale ("Sand Color Mix Scale (smaller = bigger color zones)", Range(0.05, 5)) = 0.6

        [Header(Inner Detail  Small secondary blobs inside big blobs)]
        _InnerBlobScale ("Inner Blob Scale", Range(0.1, 4)) = 0.8
        _InnerBlobStrength ("Inner Blob Strength", Range(0, 1)) = 0.35

        [Header(Surface)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0
        _Metallic ("Metallic", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorX, _ColorY, _ColorZ;
                float  _BlendSharpness;
                float  _BlobScale, _BlobThreshold, _BlobEdgeSoftness, _BlobStrength;
                float4 _SandColor1, _SandColor2, _SandColor3;
                float  _SandMixScale;
                float  _InnerBlobScale, _InnerBlobStrength;
                float  _Smoothness, _Metallic;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float fogFactor : TEXCOORD2; };

            float hash13(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float vnoise(float3 p)
            {
                float3 i = floor(p), f = frac(p), u = f*f*(3.0-2.0*f);
                float n000 = hash13(i+float3(0,0,0)), n100 = hash13(i+float3(1,0,0));
                float n010 = hash13(i+float3(0,1,0)), n110 = hash13(i+float3(1,1,0));
                float n001 = hash13(i+float3(0,0,1)), n101 = hash13(i+float3(1,0,1));
                float n011 = hash13(i+float3(0,1,1)), n111 = hash13(i+float3(1,1,1));
                return lerp(lerp(lerp(n000,n100,u.x), lerp(n010,n110,u.x), u.y),
                            lerp(lerp(n001,n101,u.x), lerp(n011,n111,u.x), u.y), u.z);
            }
            float fbm(float3 p)
            {
                float v = 0.0, a = 0.5;
                for (int i = 0; i < 4; i++) { v += a * vnoise(p); p *= 2.03; a *= 0.5; }
                return v;
            }

            // Pick a sand color from the 3-color palette using two noise fields as coords.
            float3 sandTapestry(float3 worldP)
            {
                float n1 = vnoise(worldP * _SandMixScale + 0.3);
                float n2 = vnoise(worldP * _SandMixScale + 7.7);
                // Three-way blend: n1 picks between 1<->2, n2 mixes in 3
                float3 c12 = lerp(_SandColor1.rgb, _SandColor2.rgb, smoothstep(0.2, 0.8, n1));
                return lerp(c12, _SandColor3.rgb, smoothstep(0.3, 0.7, n2));
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   vni = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS = vpi.positionCS;
                OUT.positionWS = vpi.positionWS;
                OUT.normalWS   = vni.normalWS;
                OUT.fogFactor  = ComputeFogFactor(vpi.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);

                // Triplanar dark-brown rock base
                float3 absN = pow(abs(N), _BlendSharpness);
                float3 w    = absN / (absN.x + absN.y + absN.z + 1e-5);
                float3 baseColor = _ColorX.rgb * w.x + _ColorY.rgb * w.y + _ColorZ.rgb * w.z;

                // ---- Big stylized sand blobs ----
                // Low-frequency FBM defines large organic regions.
                // Narrow edge softness gives crisp stylized blob borders.
                float blobNoise = fbm(IN.positionWS * _BlobScale);
                float blobMass  = smoothstep(_BlobThreshold,
                                             _BlobThreshold + _BlobEdgeSoftness,
                                             blobNoise);

                // Sand color tapestry inside blobs
                float3 sandColor = sandTapestry(IN.positionWS);

                // Inner detail: smaller secondary blobs inside the big ones brighten/darken
                // slightly to add stylized internal variation without breaking the flat look.
                float innerNoise = fbm(IN.positionWS * _BlobScale * _InnerBlobScale + 13.7);
                float innerShift = (innerNoise - 0.5) * 2.0 * _InnerBlobStrength;
                sandColor = saturate(sandColor + innerShift * 0.15);

                // Final albedo: dark brown base, replaced by sand color where blob mass is high
                float3 albedo = lerp(baseColor, sandColor, blobMass * _BlobStrength);

                // ---- Lighting ----
                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS   = N;
                inputData.viewDirectionWS = SafeNormalize(GetCameraPositionWS() - IN.positionWS);
                inputData.shadowCoord     = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord        = IN.fogFactor;
                inputData.bakedGI         = SampleSH(N);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo     = albedo;
                surfaceData.metallic   = _Metallic;
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS   = float3(0, 0, 1);
                surfaceData.occlusion  = 1.0;
                surfaceData.emission   = float3(0, 0, 0);
                surfaceData.alpha      = 1.0;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, IN.fogFactor);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct AttribS { float4 pos : POSITION; float3 nrm : NORMAL; };
            struct VaryS  { float4 pos : SV_POSITION; };

            float4 GetShadowPositionHClip(AttribS IN)
            {
                float3 wp = TransformObjectToWorld(IN.pos.xyz);
                float3 wn = TransformObjectToWorldNormal(IN.nrm);
                float4 clip = TransformWorldToHClip(ApplyShadowBias(wp, wn, _LightDirection));
                #if UNITY_REVERSED_Z
                    clip.z = min(clip.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    clip.z = max(clip.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return clip;
            }

            VaryS vertShadow(AttribS IN) { VaryS OUT; OUT.pos = GetShadowPositionHClip(IN); return OUT; }
            half4 fragShadow(VaryS IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
