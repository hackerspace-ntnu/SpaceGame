// The three things focus mode has to draw over an item, from one shader.
//
//   the drag tint   flat grey (or red on conflict), an inflated outline, ZTest Always so the item
//                   the player is holding is never lost behind the rig — spec 4.3 makes items
//                   being visible at all times a requirement, not an aspiration.
//   the hover rim   the outline pass alone, depth-tested normally, added as an extra material on
//                   the real item so the item itself lights up rather than a UI box appearing.
//   the footprint   the body pass alone, transparent, on a quad laid on the target surface.
//
// One shader with two switchable passes rather than three shaders, because all three want the same
// two things — a flat colour and a normal-inflated shell — and differ only in blend, depth and
// which half is on. The switches are floats, not keywords: there are three materials, they are
// built once at the start of a session, and a keyword variant per combination is not worth the
// shader compilation.
Shader "SpaceGame/PackDragTint"
{
    Properties
    {
        _Color ("Body Colour", Color) = (0.55, 0.55, 0.55, 1)
        _OutlineColor ("Outline Colour", Color) = (0.9, 0.9, 0.9, 1)

        // Metres of WORLD space, applied along the vertex normal.
        //
        // It used to be object space, and that was the bug behind "the outline is way bigger than
        // the item". A renderer's object space is whatever its import pipeline left it in: every
        // model exported from this project's Blender library arrives as mesh data 100x small under
        // a transform 100x large, so an object-space 0.012 came out as 1.2 METRES of world on a
        // 0.49 m scanner and 1.9 m on a 0.26 m leash, while a third-party FBX at scale 1 got the
        // 12 mm that was meant and the Cixin gun's child scales (up to 82,000) got tens of metres.
        // Four orders of magnitude of spread from one number, none of it authored.
        //
        // In world space the number means what it says. PackHandVisuals still scales it per item —
        // see OutlineWidthFor — so the line stays proportional to the thing it surrounds instead
        // of swallowing the small ones.
        _OutlineWidth ("Outline Width", Range(0, 0.2)) = 0.006

        [Toggle] _BodyOn ("Draw Body", Float) = 1
        [Toggle] _OutlineOn ("Draw Outline", Float) = 1

        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4   // LEqual
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1  // One
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0  // Zero
        [Toggle] _ZWrite ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float4 _OutlineColor;
            float _OutlineWidth;
            float _BodyOn;
            float _OutlineOn;
            float _ZTest;
            float _SrcBlend;
            float _DstBlend;
            float _ZWrite;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
        };

        // A pass switched off is not compiled out — it is pushed off the far plane, where the
        // clipper throws it away before any pixel is shaded. Cheaper than a shader variant and it
        // keeps the material's behaviour a value the C# can change frame to frame.
        float4 Discarded()
        {
            return float4(0, 0, 2, 1);
        }
        ENDHLSL

        // ── Outline ──────────────────────────────────────────────────────────
        // Front faces culled, so what is left is the inside of the shell: it shows only where the
        // inflated copy sticks out past the real silhouette, which is exactly a line around it.
        //
        // Both passes carry an explicit LightMode tag because URP schedules passes BY that tag.
        // One untagged pass still lands in the implicit SRPDefaultUnlit slot, but a shader with
        // two untagged passes is skipped in its entirety — silently, no warning, no magenta —
        // which left every material on this shader (drag tint, hover rim, grid cells) drawing
        // nothing at all. SRPDefaultUnlit and UniversalForward are both in URP's forward draw
        // list, so tagging one pass each way is what lets a two-pass shader draw both.
        Pass
        {
            Name "PackOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZTest [_ZTest]
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]

            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag

            Varyings OutlineVert(Attributes input)
            {
                Varyings output;

                if (_OutlineOn < 0.5)
                {
                    output.positionCS = Discarded();
                    return output;
                }

                // Inflated in WORLD space. Going through object space would scale the width by
                // whatever the renderer's object-to-world happens to be — see _OutlineWidth.
                // TransformObjectToWorldNormal uses the inverse transpose, so this also survives
                // the non-uniform child scales the Cixin gun ships with.
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                // A degenerate normal (a zero-area sliver, or a mesh imported without normals)
                // normalizes to NaN, and one NaN vertex takes its whole triangle off screen.
                float len = length(normalWS);
                normalWS = len > 1e-6 ? normalWS / len : float3(0, 0, 0);

                output.positionCS = TransformWorldToHClip(positionWS + normalWS * _OutlineWidth);
                return output;
            }

            half4 OutlineFrag(Varyings input) : SV_Target
            {
                return half4(_OutlineColor.rgb, _OutlineColor.a);
            }
            ENDHLSL
        }

        // ── Body ─────────────────────────────────────────────────────────────
        // Deliberately unlit and flat. A dragged item is being read as a SHAPE against two very
        // different backgrounds — the pack's canvas and open sand — and shading it would make its
        // legibility depend on where the sun happens to be.
        Pass
        {
            Name "PackBody"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZTest [_ZTest]
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]

            HLSLPROGRAM
            #pragma vertex BodyVert
            #pragma fragment BodyFrag

            Varyings BodyVert(Attributes input)
            {
                Varyings output;

                if (_BodyOn < 0.5)
                {
                    output.positionCS = Discarded();
                    return output;
                }

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 BodyFrag(Varyings input) : SV_Target
            {
                return half4(_Color.rgb, _Color.a);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
