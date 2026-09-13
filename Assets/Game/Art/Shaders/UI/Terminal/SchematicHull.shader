Shader "SpaceGame/SchematicHull"
{
    // The FACES of the terminal's wireframe lander — the dark card the lines are drawn on.
    //
    // Its job is depth, not colour. Filling the hull's solid form into the depth buffer is what
    // makes SchematicWire's lines a hidden-line drawing rather than an x-ray of every edge in the
    // ship at once; a wireframe you can see straight through has no near side and no far side, and
    // a reader cannot tell which motor they are looking at.
    //
    // So it draws almost black, with a faint wash that rises where the surface turns away, and lets
    // the lines carry the picture. Every colour arrives per renderer in a MaterialPropertyBlock
    // (ShipSchematicStage), so one material serves the fitted hull, the missing modules and the
    // module under the cursor.
    Properties
    {
        _Color     ("Phosphor",          Color)         = (0.42, 1.0, 0.60, 1)
        _Fill      ("Face Wash",         Range(0, 1))   = 0.20
        _FaceDim   ("Face Dim",          Range(0, 1))   = 0.22
        _RimPower  ("Rim Power",         Range(0.5, 8)) = 2.4
        _Scan      ("Scanline Strength", Range(0, 1))   = 0.18
        _ScanScale ("Scanline Period px", Float)        = 4
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "SchematicFace"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewWS     : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Fill;
                float  _FaceDim;
                float  _RimPower;
                float  _Scan;
                float  _ScanScale;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS);

                output.positionCS = position.positionCS;
                output.normalWS = normal.normalWS;

                // Orthographic: every pixel is looked at down the same axis, so the view direction
                // is the camera's forward rather than a direction to the lens. Reading it off the
                // matrix keeps the wash correct under both projections.
                output.viewWS = unity_OrthoParams.w > 0.5
                    ? -UNITY_MATRIX_V[2].xyz
                    : normalize(GetCameraPositionWS() - position.positionWS);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float facing = saturate(dot(normal, normalize(input.viewWS)));

                // A little more light where the surface turns away, so a flat panel still reads as
                // a surface between its edges rather than as a hole.
                float wash = _Fill * _FaceDim * (0.6 + 0.8 * pow(1.0 - facing, _RimPower));

                // positionCS is the pixel centre here, so the period is honestly in pixels of the
                // render texture — which is what makes the lines hold still as the hull turns.
                float scan = 1.0 - _Scan * step(0.5, frac(input.positionCS.y / max(2.0, _ScanScale)));

                return half4(_Color.rgb * wash * scan, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
