Shader "SpaceGame/LaserSpark"
{
    // Sparks and embers thrown off where the Laser Staff's beam cuts. Additive, unlit, and
    // driven entirely by the particle system's own vertex colour, so one material serves both the
    // fast white-hot sparks and the slow falling embers — the difference between them is start
    // colour, lifetime and gravity, all of which belong on the emitter rather than in a shader.
    //
    // Procedural rather than textured, for the same reason the beam is: a spark is a tapered
    // streak, which is two smoothsteps. Shipping a texture for that would add an asset to import,
    // a filtering mode to get wrong, and a file the next person has to trace back to this shader.
    //
    // Meant for Stretched Billboard particles. UV x runs ALONG the stretch, y across it, so the
    // taper is separable and stays correct however far velocity stretches the quad — a radial
    // falloff would turn into a fat ellipse the moment a spark moved quickly.
    Properties
    {
        _Intensity  ("Intensity",          Range(0, 20)) = 6
        _Sharpness  ("Cross Sharpness",    Range(0.5, 8)) = 2.0
        _Taper      ("End Taper",          Range(0.1, 6)) = 1.4
        _CoreBoost  ("White Hot Core",     Range(0, 6)) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType"     = "Plane"
        }

        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                half4  color       : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _Sharpness;
                float _Taper;
                float _CoreBoost;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float across = abs(IN.uv.y * 2.0 - 1.0);
                float along  = saturate(IN.uv.x);

                float body = pow(saturate(1.0 - across), _Sharpness);

                // Tapered at both ends so a spark reads as a moving point with a trail rather than
                // as a rectangle with soft long edges.
                float ends = pow(sin(along * 3.14159265), _Taper);

                float shape = body * ends;

                // The leading end is the hot one. A spark is brightest where the metal actually is
                // and dimmer back along the trail it has already left behind.
                float core = pow(saturate(shape), 4.0) * smoothstep(0.35, 1.0, along);

                float3 colour = IN.color.rgb * shape + core * _CoreBoost;

                return half4(colour * _Intensity * IN.color.a, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
