Shader "UI/HelmetHUDDangerVignette"
{
    // A single thin curved warning line on the left or right of the helmet.
    // The arc is a slice of a circle whose center sits OUTSIDE the screen, so
    // the visible stroke reads as a curved line hugging the side of the visor.
    //
    // C# drives:
    //   _Span     — vertical extent of the arc (grows on hit, shrinks over time)
    //   _Pulse    — main alpha (snaps to 1 on hit, fades over fadeDuration)
    //   _Spike    — short impact flash channel (snaps to 1 on hit, fast decay)
    //
    // The shader layers a soft halo + shimmer bead + hit spike + flicker on
    // top of the stroke for a holographic alarm feel.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 0.25, 0.25, 1)

        // 0 = left, 1 = right
        _Side ("Side (0=left, 1=right)", Float) = 0

        // Curvature
        _ArcCenterOffset ("Arc Center Offset", Range(0.5, 4.0)) = 1.6
        _ArcRadius ("Arc Radius", Range(0.5, 4.0)) = 1.85
        _ArcThickness ("Arc Thickness", Range(0.001, 0.05)) = 0.012

        // Vertical extent / fade
        _Span ("Vertical Half-Span", Range(0.0, 0.5)) = 0.0
        _SpanFeather ("End Fade", Range(0.0, 0.3)) = 0.06

        // Halo around the stroke (soft outer glow)
        _HaloThickness ("Halo Thickness", Range(0.0, 0.2)) = 0.05
        _HaloStrength ("Halo Strength", Range(0.0, 1.5)) = 0.55

        // Shimmer bead — bright dot scrolling along the arc
        _ShimmerSpeed ("Shimmer Speed", Range(-3, 3)) = 0.7
        _ShimmerWidth ("Shimmer Width", Range(0.02, 0.6)) = 0.18
        _ShimmerStrength ("Shimmer Strength", Range(0, 2)) = 0.85

        // Flicker — random fast brightness jitter
        _FlickerStrength ("Flicker Strength", Range(0, 1)) = 0.18
        _FlickerSpeed ("Flicker Speed", Range(0, 60)) = 22

        // Hit spike — briefly thickens + brightens the stroke on impact
        _Spike ("Spike 0..1", Range(0, 1)) = 0
        _SpikeThicken ("Spike Thicken", Range(0, 4)) = 1.6
        _SpikeBrighten ("Spike Brighten", Range(0, 4)) = 1.8

        // Overall
        _Pulse ("Pulse", Range(0, 1)) = 0
        _Intensity ("Intensity", Range(0, 4)) = 1.4

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha One
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            fixed4 _Color;
            float _Side;
            float _ArcCenterOffset;
            float _ArcRadius;
            float _ArcThickness;
            float _Span;
            float _SpanFeather;
            float _HaloThickness;
            float _HaloStrength;
            float _ShimmerSpeed;
            float _ShimmerWidth;
            float _ShimmerStrength;
            float _FlickerStrength;
            float _FlickerSpeed;
            float _Spike;
            float _SpikeThicken;
            float _SpikeBrighten;
            float _Pulse;
            float _Intensity;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color;
                return OUT;
            }

            float hash11(float n) { return frac(sin(n) * 43758.5453); }

            fixed4 frag(v2f IN) : SV_Target
            {
                if (_Span <= 0.0001 || _Pulse <= 0.0001)
                    return fixed4(0,0,0,0);

                float2 uv = IN.texcoord;

                // Distance to the arc center (off-screen on the active edge).
                float edgeX = (_Side < 0.5) ? uv.x : (1.0 - uv.x);
                float dx = edgeX + _ArcCenterOffset;
                float dy = uv.y - 0.5;
                float r = sqrt(dx * dx + dy * dy);

                // -- Stroke (with hit-spike thickening) --
                float thickness = _ArcThickness * (1.0 + _Spike * _SpikeThicken);
                float halfT = thickness * 0.5;
                float feather = max(thickness * 0.4, 0.0015);
                float sIn  = smoothstep(_ArcRadius - halfT - feather, _ArcRadius - halfT, r);
                float sOut = 1.0 - smoothstep(_ArcRadius + halfT, _ArcRadius + halfT + feather, r);
                float stroke = saturate(sIn * sOut);

                // -- Halo (soft outer glow) --
                // Distance from the stroke band, normalized by halo thickness.
                float dRadial = abs(r - _ArcRadius);
                float haloT = saturate((dRadial - halfT) / max(0.0001, _HaloThickness));
                float halo = (1.0 - haloT) * (1.0 - haloT) * step(halfT, dRadial); // smooth falloff outside the stroke
                halo *= _HaloStrength;

                // -- Vertical span mask (with feathered ends) --
                float vy = abs(dy);
                float vIn = 1.0 - smoothstep(_Span, _Span + _SpanFeather, vy);

                // -- Shimmer bead — bright pulse moving up/down the arc --
                // beadCenter scrolls in [-_Span, +_Span]
                float scroll = sin(_Time.y * _ShimmerSpeed);
                float beadY = scroll * _Span;
                float beadDist = abs(dy - beadY);
                float bead = exp(-pow(beadDist / max(0.001, _ShimmerWidth), 2.0)); // gaussian bump
                float shimmer = bead * _ShimmerStrength;

                // -- Flicker — short hash-noise jitter --
                float flicker = 1.0 - _FlickerStrength * hash11(floor(_Time.y * _FlickerSpeed));

                // -- Combine --
                // The stroke is the hard line; halo and shimmer add light around it.
                // Spike further brightens the stroke right at hit time.
                float strokeBoost = 1.0 + _Spike * _SpikeBrighten;
                float linePart = stroke * strokeBoost;
                float glowPart = (halo + shimmer * (stroke + halo * 0.6));
                float lit = (linePart + glowPart) * flicker;

                float alpha = lit * vIn * _Pulse * IN.color.a;
                alpha = saturate(alpha);

                fixed3 rgb = _Color.rgb * _Intensity;
                fixed4 col;
                col.rgb = rgb * alpha;
                col.a = alpha;
                return col;
            }
            ENDCG
        }
    }
}
