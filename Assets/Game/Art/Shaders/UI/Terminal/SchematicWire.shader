Shader "SpaceGame/SchematicWire"
{
    // The lines of the terminal's wireframe lander: flat phosphor, no lighting, no thickness.
    //
    // Drawn from a line-topology mesh of the model's feature edges (FeatureEdges), not from a
    // geometry shader — Metal has none — and not from barycentric coordinates in the fragment
    // shader, which would need every triangle unwelded and would ink all 46,000 of them.
    //
    // Depth is TESTED but not written: the dark faces of SchematicHull have already filled the
    // depth buffer, so a line behind the hull is correctly hidden, while a line lying exactly on
    // the surface it belongs to would z-fight without the offset below.
    //
    // Queued after the opaques on purpose. PC_Renderer primes depth, which forces every opaque-queue
    // draw to ZTest Equal against the prepass; a line that writes no depth of its own, offset off
    // the surface, never equals it, and the whole wireframe vanished. Past the opaque range the
    // pass keeps its own ZTest against the hull's primed depth.
    Properties
    {
        _Color ("Phosphor", Color)       = (0.42, 1.0, 0.60, 1)
        _Wire  ("Line Brightness", Range(0, 6)) = 1.4
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "SchematicWire"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Wire;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetVertexPositionInputs(input.positionOS.xyz).positionCS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(_Color.rgb * _Wire, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
