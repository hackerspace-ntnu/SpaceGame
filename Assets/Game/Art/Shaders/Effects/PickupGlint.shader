// The slash of light that flashes across a weapon lying on the ground, so it can be spotted from
// across the arena before anybody picks it up (ArenaPickupGlint drives it).
//
// DRAWN, NOT TEXTURED. The crescent is a band along a circular arc in the quad's own uv: thin and
// pointed at the tail, swelling towards the head, tapering again at the very end, with a few barbs
// nicked out of its inner edge. A texture would need its own import settings and would blur when
// the quad is large on screen; the arc stays crisp at any size because its edges are anti-aliased
// with fwidth.
//
// ONE PARAMETER ANIMATES IT. _Progress runs 0 -> 1 over a flash. The head sweeps along the arc in
// the first part of it, the tail chases it, and the whole thing fades over the last part — so the
// script only has to advance a number.
//
// A BILLBOARD IN THE VERTEX SHADER. The quad is turned to face the camera here rather than by a
// script per camera, and is nudged towards the viewer so it is not cut in half by the very weapon
// it is drawn over. _Spin turns the slash in the screen plane so consecutive flashes do not stamp
// the same shape.
//
// PREMULTIPLIED. The white core is drawn opaque, so it reads on bright sand where purely additive
// white would vanish; the cyan halo around it is added on top, which is what makes it glow.
Shader "SpaceGame/Effects/PickupGlint"
{
    Properties
    {
        _CoreColor  ("Core Colour", Color) = (1, 1, 1, 1)
        _RimColor   ("Rim Colour",  Color) = (0.45, 0.95, 1, 1)
        _Brightness ("Brightness (HDR)", Range(0, 8)) = 2.5

        _Progress ("Progress", Range(0, 1)) = 0.5
        _Spin     ("Spin (radians)", Float) = 0

        _Thickness ("Thickness",   Range(0.02, 0.6)) = 0.36
        _RimWidth  ("Rim Width",   Range(0, 0.2))    = 0.035
        _Halo      ("Halo Reach",  Range(0, 0.5))    = 0.14
        _Barbs     ("Barb Depth",  Range(0, 1))      = 0.35

        _TowardCamera ("Toward Camera (m)", Range(0, 2)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType"     = "Plane"
        }

        Pass
        {
            Name "PickupGlint"

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _RimColor;
                float _Brightness;
                float _Progress;
                float _Spin;
                float _Thickness;
                float _RimWidth;
                float _Halo;
                float _Barbs;
                float _TowardCamera;
            CBUFFER_END

            // The arc the slash is drawn along, in uv space centred on the quad (-1..1).
            static const float2 ArcCentre = float2(-0.45, -0.45);
            static const float  ArcRadius = 1.15;
            static const float  TailAngle = 1.92;   // ~110 deg: the thin end, upper left
            static const float  HeadAngle = -0.35;  // ~-20 deg: the thick end, lower right

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;

                float3 centreWS = TransformObjectToWorld(float3(0, 0, 0));
                float scale = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));

                float s, c;
                sincos(_Spin, s, c);
                float2 corner = float2(c * input.positionOS.x - s * input.positionOS.y,
                                       s * input.positionOS.x + c * input.positionOS.y) * scale;

                // View space looks down -z, so +z is towards the camera.
                float3 positionVS = TransformWorldToView(centreWS) + float3(corner, _TowardCamera);
                o.positionCS = TransformWViewToHClip(positionVS);
                o.uv = input.uv;
                return o;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float2 fromCentre = p - ArcCentre;

                // Where along the arc this pixel sits: 0 at the tail, 1 at the head.
                float angle = atan2(fromCentre.y, fromCentre.x);
                float t = (TailAngle - angle) / (TailAngle - HeadAngle);

                // Swelling from a needle at the tail to its widest near the head, then tapering.
                float along = saturate(t);
                float width = _Thickness * sin(PI * pow(along, 2.4));

                // Barbs: a few short notches bitten out of the inner edge towards the head.
                float notch = step(0.65, frac(along * 4.0)) * smoothstep(0.4, 0.9, along);
                float inner = width * (1.0 - _Barbs * notch);

                // The outer edge is the circle itself; the band grows inward from it.
                float r = length(fromCentre);
                float depth = ArcRadius - r;                       // > 0 inside the circle
                float bandDist = max(-depth, depth - inner);       // < 0 inside the band

                // The sweep: the head runs ahead, the tail chases, everything fades at the end.
                // Soft at both ends, so the glow around the slash trails off instead of being cut
                // by a straight line where the sweep has not reached yet.
                float head = saturate(_Progress * 1.8);
                float tail = saturate(_Progress * 1.8 - 0.75);
                const float soft = 0.06;
                float window = smoothstep(tail - soft, tail, t) * (1.0 - smoothstep(head, head + soft, t))
                             * smoothstep(-soft, 0.0, t) * (1.0 - smoothstep(1.0, 1.0 + soft, t));
                float fade = 1.0 - smoothstep(0.55, 1.0, _Progress);

                float aa = max(fwidth(bandDist), 1e-4);
                float body = (1.0 - smoothstep(-aa, aa, bandDist)) * window;
                float core = (1.0 - smoothstep(-aa, aa, bandDist + _RimWidth)) * window;
                float rim = saturate(body - core);
                float halo = exp(-max(bandDist, 0.0) / max(_Halo, 1e-3) * 3.0) * window * (1.0 - body);

                float3 colour = _CoreColor.rgb * core * _Brightness
                              + _RimColor.rgb * rim * _Brightness
                              + _RimColor.rgb * halo * _Brightness * 0.6;
                float alpha = body;

                return float4(colour, alpha) * fade;
            }
            ENDHLSL
        }
    }
}
