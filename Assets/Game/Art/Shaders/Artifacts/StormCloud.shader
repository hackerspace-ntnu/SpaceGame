// The cloud a Storm Flask uncorks, and the rain hanging under it (Artifacts/StormFlask.md).
//
// THE MESH IS A UNIT CLOUD, AND THAT IS WHY THIS SHADER HAS NO MEASUREMENTS IN IT — the same
// convention JetFlame.shader's unit cone uses. In the frame this shader shades in:
//
//   * centred on the origin, radius 1 in XZ, +Y up;
//   * geometry at y >= 0 is the CLOUD BODY — a flat lens, per the design's "small, flat,
//     angry disc";
//   * geometry at y < 0 is the RAIN VEIL, hanging to y = -1.
//
// The transform carries the real size (12 m radius, 15 m up, per the design). A mesh with no
// geometry below y = 0 simply gets no rain and nothing breaks, so the cloud and the veil can
// arrive as one mesh or as two objects sharing this material.
//
// BUT THE MESH DOES NOT ARRIVE IN THAT FRAME, AND ASSUMING IT DID WAS A BUG. JetFlame's cone
// is generated in C#, so its object space IS its own frame. An `_exportlib` FBX is not: the
// Blender-to-Unity turn and the centimetre file scale are left on the NODE
// (`lr = (270.02, 0, 0)`, `ls = (100, 100, 100)`), not baked into the vertices. So `positionOS`
// arrives Z-up at 1/100 — `Mesh_StormCloud_RainVolume.bounds` measures
// `size (0.0184, 0.0184, 0.01)` against the 1.84 x 1.84 x 1.0 that storm_cloud.py authors.
// Read raw, `positionOS.y` is a HORIZONTAL axis and `length(positionOS.xz)` comes out near
// 0.009 instead of near 0.92: every radial and vertical term reads the wrong axis at 1/100
// magnitude, and the cloud renders as a solid dark cylinder with no churn, taper or streaks.
// `MeshToCloudFrame` below undoes exactly the node transform, so the shader gets the frame
// storm_cloud.py's header promises it.
//
// WHY THE FIX IS HERE AND NOT IN THE IMPORTER. Over a hundred models share this convention,
// and no importer setting reaches it — `bakeAxisConversion` mirrors the mesh in Z and flips
// the node to compensate, and `globalScale` scales vertices while leaving the node rotation.
//
// WHY THE FRAME IS NORMALISED RATHER THAN METRIC, which is the part that is easy to get wrong
// twice: the prefab scales the body uniformly by 12 and the veil by 12 wide and 15 tall, the
// extra 1.25 living in the veil node's `localScale.z`. Node scale never touches `positionOS`,
// so both objects still hand this shader the same normalised mesh and BOTH come out right —
// the veil's `fall` runs 0..1 across its full length whether it is stretched or not, which is
// what keeps `_RainSpeed` meaningful in veil lengths per second. Converting to metres instead
// would make the stretched veil run 0..1.25 and fade out three metres above its own foot.
//
// IT READS POSITION AND NOTHING ELSE: no UV, no normal, no vertex colour. Every coordinate it
// shades from is derived from that one position, so there is no UV convention to get
// wrong, and no chance of a black cloud because a lathe wrote its V the other way up.
//
// WHY THIS ONE IS TRANSPARENT WHEN THE OTHER THREE ARE OPAQUE. The foam, the ice and the
// slick film all buy their legibility from PastelQuantize's ink pass, which needs them in
// _CameraDepthTexture and therefore opaque. The cloud does not need to buy it: it is a very
// dark thing hanging in front of a very bright sky, and the palette entries confirm the gap
// is enormous — the three cloud bands land on entries 152, 154 and 194 while the pale desert
// sky lands on 135, which is four lightness rows away. Raw contrast does here what the
// outline does elsewhere, so the cloud can afford to be vapour. It must be: an opaque disc
// with rain behind it would occlude its own rain.
//
// READABLE FROM ANY BEARING. Nothing in this shader depends on where the camera is standing
// horizontally. The churn, the streaks and the ragged edge are all functions of the cloud
// frame's own position, and Cull is Off so the far wall of the veil draws as well as the
// near one. The only view-dependent term at all is the extra density a lens picks up when
// seen edge-on, which is symmetric about the cloud's axis. That is how the "invisible from
// one half of the compass" failure was ruled out here.
//
// WHY THREE BANDS AND NOT FOUR. Measured, not chosen: below Oklab L 0.43 the palette's blue
// column runs out and only the grey ramp is left, so a four-step dark ladder collapses — the
// top two steps snapped to the same entry at every chroma tried between 0.055 and 0.10. Three
// bands, spaced on the rows the lattice actually holds, stay three (GDC-L1-TECH-0004).
Shader "SpaceGame/Artifacts/StormCloud"
{
    Properties
    {
        // Authored ON palette entries (#707996 #464F69 #303030 — the cold-blue column at
        // Oklab L 0.58 and 0.43, and the grey ramp below where that column ends), so the
        // quantizer's snap is a no-op on them and the three stay three.
        [Header(Cloud bands)]
        _BandRim  ("Band 1  Rim",  Color) = (0.439, 0.475, 0.588, 1)
        _BandMid  ("Band 2  Mid",  Color) = (0.275, 0.310, 0.412, 1)
        _BandCore ("Band 3  Core", Color) = (0.188, 0.188, 0.188, 1)

        // Multiplies the raw mesh position to reach the unit-radius frame above. 100 undoes
        // the centimetre file scale every `_exportlib` FBX carries on its node; 1 is right for
        // geometry generated in C#, which needs no undoing.
        //
        // It is ONE shared number rather than a per-mesh radius on purpose. The body is
        // authored at radius 1.00 and the veil at 0.92, deliberately, so that the rain falls
        // inside the silhouette that warns you about it. Normalising each mesh by its own
        // bounds would push the veil out to 1.00 and destroy that relationship — so the two
        // objects share one material and one scale, and keep their authored proportions.
        [Header(Mesh frame)]
        _MeshToUnit ("Mesh Units To Unit Radius", Float) = 100

        [Header(Cloud shape)]
        _Density      ("Density",              Range(0, 1))    = 0.92
        _ChurnScale   ("Churn Scale",          Range(0.5, 12)) = 3.2
        _ChurnSpeed   ("Churn Speed",          Range(0, 2))    = 0.25
        _EdgeRagged   ("Edge Raggedness",      Range(0, 0.5))  = 0.18
        _EdgeSoftness ("Edge Softness",        Range(0.01, 0.6)) = 0.12
        _CoreBias     ("Core Bias",            Range(0, 1))    = 0.55

        [Header(Lightning)]
        _Flash      ("Flash",        Range(0, 1))   = 0
        _FlashColor ("Flash Colour", Color)         = (0.898, 0.941, 0.984, 1)
        _FlashSpread("Flash Spread", Range(0.05, 1)) = 0.45

        // Unlike the cloud bands, these are NOT authored on palette entries, and that is the
        // right call for a transparent material: what the quantizer snaps is the COMPOSITED
        // pixel, so the colour that has to land on an entry is the blend, not the source.
        // They were chosen by compositing them at their real alpha and snapping that.
        //
        // Rain is DARKER than what it hangs in front of, which is both what a curtain of it
        // really does and the only version that survives: the first pass at these values
        // composited onto entry 136 against a sky on 135 — one lightness step, visible but not
        // the "readable at 50 m" the design asks for. The streak core now lands on 138, two
        // steps off the sky, and stays distinct over sand, pale sand, rock and dune as well.
        // Opacity carries most of that: the veil wall sits at radius 0.92, inside the edge
        // falloff, so it is permanently multiplied by about 0.74 and never reaches the alpha
        // its own number suggests.
        [Header(Rain)]
        _RainLight   ("Rain Light",           Color)           = (0.451, 0.549, 0.643, 1)
        _RainDark    ("Rain Dark",            Color)           = (0.322, 0.400, 0.475, 1)
        _RainOpacity ("Rain Opacity",         Range(0, 1))     = 0.85
        _RainDensity ("Rain Density",         Range(1, 60))    = 16
        _RainColumns ("Rain Columns",         Range(4, 120))   = 34
        _RainWidth   ("Rain Streak Width",    Range(0.02, 1))  = 0.22
        // At this world's gravity of 18 m/s^2, a drop released 15 m up arrives at about
        // 23 m/s, which over a 15 m veil is roughly 1.5 veil lengths a second. That is where
        // the default comes from; it is deliberately not 9.81's answer, which looks like
        // drizzle here.
        _RainSpeed   ("Rain Speed (veils/s)", Range(0.1, 6))   = 1.5
        _RainTaper   ("Rain Taper",           Range(0, 1))     = 0.35

        [Header(Life)]
        _Form ("Form", Range(0, 1)) = 1
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
            Name "StormCloud"

            // Straight alpha, not additive: a storm cloud takes light away. Additive is what
            // makes a dark cloud impossible, because the brightest thing behind it — the sky —
            // is exactly what it has to be darker than.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ArtifactSubstance.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _MeshToUnit;

                half4 _BandRim;
                half4 _BandMid;
                half4 _BandCore;

                float _Density;
                float _ChurnScale;
                float _ChurnSpeed;
                float _EdgeRagged;
                float _EdgeSoftness;
                float _CoreBias;

                float _Flash;
                half4 _FlashColor;
                float _FlashSpread;

                half4 _RainLight;
                half4 _RainDark;
                float _RainOpacity;
                float _RainDensity;
                float _RainColumns;
                float _RainWidth;
                float _RainSpeed;
                float _RainTaper;

                float _Form;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionCloud : TEXCOORD0;
                float  fogFactor  : TEXCOORD1;
            };

            // Undo the import node, so the rest of the shader works in the frame
            // storm_cloud.py authors: XZ radius 1, +Y up, veil hanging to y = -1.
            //
            // The node's rotation is Unity Euler (270.02, 0, 0), i.e. R_x(-90), which maps
            // (x, y, z) to (x, z, -y): mesh Z becomes up and mesh Y becomes the negated
            // horizontal. The negation is written out rather than folded into a bare `.xzy`
            // swizzle. It costs one instruction and it is genuinely free to be exact — the
            // only consumers of that component are `length`, a `floor` cell index and the
            // noise, all of which would survive a mirrored axis, but leaving a deliberate
            // mirror in the code means the next person has to re-derive that it is harmless.
            float3 MeshToCloudFrame(float3 positionOS)
            {
                return float3(positionOS.x, positionOS.z, -positionOS.y) * _MeshToUnit;
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionCloud.xyz);
                OUT.positionCS = positions.positionCS;
                OUT.positionCloud = MeshToCloudFrame(IN.positionCloud.xyz);
                OUT.fogFactor  = ComputeFogFactor(positions.positionCS.z);
                return OUT;
            }

            // The cloud body: a flat lens of churning vapour, dark in the middle and lighter
            // at the ragged rim.
            void ShadeCloud(float3 position, float radius, out float3 colour, out float alpha)
            {
                // Object space with a slow vertical scroll. The cloud does not travel, so a
                // world-space field would only mean two storms in the same place share a
                // pattern; its own frame keeps each one its own weather.
                float3 churnSample = position * _ChurnScale
                                   + float3(0.0, _Time.y * _ChurnSpeed, _Time.y * _ChurnSpeed * 0.4);
                float churn = SubstanceTurbulence(churnSample);

                // The rim is eaten into by the churn, so the disc is not a circle. A perfect
                // circle in the sky is the thing that reads as a decal rather than as weather,
                // and it is also what the quantizer's ink would outline most cleanly — the one
                // place a crisp outline is the wrong answer.
                float raggedRadius = radius + (churn - 0.5) * _EdgeRagged;
                alpha = _Density * _Form
                      * (1.0 - smoothstep(1.0 - _EdgeSoftness, 1.0, raggedRadius));

                // Darkest through the middle, where a real cloud is thickest, biased so most
                // of the disc sits in the two dark bands and only the edge lifts. An evenly
                // lit cloud reads as a grey plate.
                float depth = saturate((1.0 - raggedRadius) * (1.0 + _CoreBias) - _CoreBias * churn);
                colour = SubstanceLadder3(1.0 - depth, _BandCore.rgb, _BandMid.rgb, _BandRim.rgb);

                // A bolt lights the cloud from inside, brightest at the middle and reaching
                // out as far as the spread allows. Mixed into the colour BEFORE it leaves this
                // function, never composited over the finished frame, so the lit cloud still
                // lands on a palette entry — the same rule PastelQuantize's ink follows.
                float lit = _Flash * saturate(1.0 - raggedRadius / max(_FlashSpread, 1e-3));
                colour = lerp(colour, _FlashColor.rgb, saturate(lit));
            }

            // The rain veil: thin streaks falling out of the cloud, thinning as they go.
            void ShadeRain(float3 position, float radius, out float3 colour, out float alpha)
            {
                // 0 at the cloud, 1 at the bottom of the veil. The mesh defines the veil as
                // one object unit tall, which is what makes _RainSpeed meaningful in veil
                // lengths a second rather than in a unit nobody can picture.
                float fall = saturate(-position.y);

                // One phase per column, so neighbouring streaks are not in lockstep. Quantized
                // into columns rather than driven continuously, because a continuous phase
                // shears the streaks into a spiral as they wrap.
                float2 column = floor(position.xz * _RainColumns);
                float phase = SubstanceHash(float3(column, 0.0));

                float streakCoord = fall * _RainDensity - _Time.y * _RainSpeed * _RainDensity + phase;
                float streak = frac(streakCoord);

                // A hard-ended streak, not a soft blob: rain at this distance is a line, and a
                // soft one is fog. Two flats rather than a gradient along the streak, for the
                // same reason every other ladder here has flats — the quantizer would band it
                // anyway and authored bands can be tuned.
                float body = 1.0 - smoothstep(0.0, _RainWidth, streak);
                colour = lerp(_RainDark.rgb, _RainLight.rgb, SubstanceBand(body, 2.0));

                // Thinner at the bottom, where drops have spread apart, and gone before the
                // veil's own geometry ends so the mesh has no visible bottom edge. Also thins
                // toward the outside of the disc, so the curtain has a shape rather than
                // stopping at a cylinder wall.
                float taper = (1.0 - smoothstep(1.0 - _RainTaper, 1.0, fall))
                            * (1.0 - smoothstep(1.0 - _EdgeSoftness, 1.0, radius));

                alpha = body * _RainOpacity * taper * _Form;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float radius = length(IN.positionCloud.xz);

                float3 colour;
                float alpha;
                if (IN.positionCloud.y >= 0.0)
                {
                    ShadeCloud(IN.positionCloud, radius, colour, alpha);
                }
                else
                {
                    ShadeRain(IN.positionCloud, radius, colour, alpha);
                }

                if (alpha <= 0.003)
                {
                    discard;
                }

                colour = MixFog(colour, IN.fogFactor);
                return half4(colour, saturate(alpha));
            }
            ENDHLSL
        }
    }

    FallBack Off
}
