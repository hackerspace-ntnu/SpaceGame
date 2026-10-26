// The shimmer of stasis: the shell round a body held out of time, the sprite hamlet's dome, and
// the Oathbranch's bolt (SpriteHamletQuest.md, Artifacts/StatusEffects.md).
//
// THE MESH IS ANY CLOSED SURFACE WITH NORMALS — Unity's sphere primitive for the shell and the
// bolt, a hemisphere for the dome. It reads POSITION and NORMAL; no UV, so a stretched ellipsoid
// round a tall body does not stretch its veins.
//
// WHAT IT DRAWS. Three terms, additive, so the shell lightens whatever is behind it and never hides
// it — a body in stasis must stay readable as the body it is:
//
//   • The rim, view-dependent: a fresnel band, brightest at the silhouette. It is what makes a
//     clear bubble read as a bubble at all.
//   • The veins, view-independent: crackling filaments drifting over the surface in WORLD space,
//     the electric veins of the reference (a glass dome laced with blue light). World space so the
//     dome's veins do not swim when the camera moves, and so two shells side by side are not the
//     same picture.
//   • A faint body tint so the face-on centre is not empty.
//
// WHY THE COLOURS ARE SATURATED. PastelQuantize snaps the finished frame to a pastel lattice, and
// a faint, desaturated tint collapses onto the grey ramp there (see SlickSheen.shader's header for
// the measurement). So the colour is authored bright and made subtle by COVERAGE — a thin rim and
// thin veins — never by turning the chroma down.
//
// _Appear (0→1) swells it in; _Remaining (1→0) is how much of the stasis is left, and the last
// quarter flickers so the thaw is telegraphed. The dome leaves both at 1.
Shader "SpaceGame/Effects/StasisShell"
{
    Properties
    {
        [HDR] _Color      ("Colour",            Color)          = (0.45, 0.85, 1.6, 1)
        _BodyAlpha        ("Body Tint",         Range(0, 0.5))  = 0.06

        [Header(Rim)]
        _RimStrength      ("Rim Strength",      Range(0, 3))    = 1.1
        _RimPower         ("Rim Power",         Range(0.5, 8))  = 3

        [Header(Veins)]
        _VeinStrength     ("Vein Strength",     Range(0, 3))    = 1.2
        _VeinScale        ("Vein Scale (per m)",Range(0.05, 8)) = 1.4
        _VeinWidth        ("Vein Width",        Range(0.005, 0.3)) = 0.06
        _VeinSpeed        ("Vein Drift",        Range(0, 3))    = 0.6

        [Header(Life)]
        _Appear           ("Appear",            Range(0, 1))    = 1
        _Remaining        ("Remaining",         Range(0, 1))    = 1
        _FlickerBelow     ("Flicker Below",     Range(0, 1))    = 0.25
        _FlickerRate      ("Flicker Rate (Hz)", Range(1, 30))   = 12

        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "StasisShell"

            // Additive: light ADDED to the scene behind, so the body inside stays exactly as
            // legible as it was. Alpha only scales how much is added.
            Blend One One
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half  _BodyAlpha;
                half  _RimStrength;
                half  _RimPower;
                half  _VeinStrength;
                half  _VeinScale;
                half  _VeinWidth;
                half  _VeinSpeed;
                half  _Appear;
                half  _Remaining;
                half  _FlickerBelow;
                half  _FlickerRate;
                half  _Cull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                // Swell in from 80 % size: a shell that pops in at full size reads as a glitch.
                float grow = lerp(0.8, 1.0, saturate(_Appear));
                float3 positionOS = input.positionOS.xyz * grow;

                Varyings output;
                output.positionWS = TransformObjectToWorld(positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            // Integer-lattice hash: stable across drivers, unlike frac(sin()).
            float Hash(float3 p)
            {
                uint3 q = (uint3)(int3)floor(p) * uint3(1597334673u, 3812015801u, 2798796415u);
                uint n = (q.x ^ q.y ^ q.z) * 1597334673u;
                return n * (1.0 / 4294967295.0);
            }

            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);

                float n000 = Hash(i);
                float n100 = Hash(i + float3(1, 0, 0));
                float n010 = Hash(i + float3(0, 1, 0));
                float n110 = Hash(i + float3(1, 1, 0));
                float n001 = Hash(i + float3(0, 0, 1));
                float n101 = Hash(i + float3(1, 0, 1));
                float n011 = Hash(i + float3(0, 1, 1));
                float n111 = Hash(i + float3(1, 1, 1));

                return lerp(lerp(lerp(n000, n100, u.x), lerp(n010, n110, u.x), u.y),
                            lerp(lerp(n001, n101, u.x), lerp(n011, n111, u.x), u.y), u.z);
            }

            // Filaments: the zero-crossings of two octaves of noise, which wander like lightning
            // rather than tiling like a grid.
            float Veins(float3 positionWS)
            {
                float t = _Time.y * _VeinSpeed;
                float3 p = positionWS * _VeinScale;

                float a = ValueNoise(p + float3(0, t, 0)) - 0.5;
                float b = ValueNoise(p * 2.1 + float3(t * 0.7, 0, -t)) - 0.5;
                float field = a + 0.5 * b;

                float pixelsAway = abs(field) / max(fwidth(field), 1e-4);
                float filament = 1.0 - saturate(abs(field) / _VeinWidth);
                // Keep the filament at least a pixel wide at range, so the dome's veins do not
                // break up into dots across the hamlet.
                return max(filament * filament, saturate(1.0 - pixelsAway) * 0.5);
            }

            half4 Frag(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                float3 normal = normalize(input.normalWS) * (frontFace ? 1.0 : -1.0);
                float3 view = normalize(GetWorldSpaceViewDir(input.positionWS));

                float rim = pow(1.0 - saturate(dot(normal, view)), _RimPower) * _RimStrength;
                float veins = Veins(input.positionWS) * _VeinStrength;

                float flicker = 1.0;
                if (_Remaining < _FlickerBelow)
                    flicker = 0.35 + 0.65 * step(0.5, frac(_Time.y * _FlickerRate));

                float intensity = (_BodyAlpha + rim + veins) * saturate(_Appear) * flicker;
                return half4(_Color.rgb * intensity, 1.0);
            }
            ENDHLSL
        }
    }
}
