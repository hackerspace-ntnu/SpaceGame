using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Per-feature settings for <see cref="CaveEntranceFeature"/>. All values are tunable in the spawner
    /// inspector via the <c>[SerializeReference]</c> settings object; kept separate from the shared
    /// <see cref="TerrainFeatureTuning"/> so the four mandated knobs remain untouched.
    ///
    /// Mental model: a BIG ROCK STANDING UP with a TUNNEL bored into it that goes DEEP. Two knobs
    /// dominate the look — <see cref="rockSize"/> sizes the rock, <see cref="tunnelSteepness"/> tilts
    /// the tunnel from flat to a near-vertical shaft. Everything else is secondary shaping.
    /// </summary>
    [System.Serializable]
    public class CaveEntranceSettings
    {
        [Header("Rock")]
        [Tooltip("Radius of the standing rock at its base, in metres. Small = a boulder you can walk " +
                 "around; large = a whole hillside. The tunnel and mouth scale with this.")]
        [Range(8f, 60f)] public float rockSize = 20f;

        [Tooltip("How tall the rock stands, as a multiple of rockSize. 1 = as tall as it is wide; " +
                 "2+ = a tall prominent landmark rock towering over the terrain.")]
        [Range(1f, 3f)] public float rockHeight = 1.8f;

        [Tooltip("How sharply the rock narrows from base to top. 0 = a straight-sided pillar; 1 = " +
                 "tapers nearly to a point. ~0.35 reads as a chunky natural standing rock.")]
        [Range(0f, 0.85f)] public float taper = 0.35f;

        [Header("Tunnel")]
        [Tooltip("THE STEEPNESS KNOB. 0 = the tunnel runs flat, straight into the rock. 1 = it plunges " +
                 "steeply downward like a mine shaft. In between gives a descending ramp.")]
        [Range(0f, 1f)] public float tunnelSteepness = 0.4f;

        [Tooltip("How far the tunnel bores, in METRES (measured along the slope). 15 m and up gives a " +
                 "long tunnel; when 'Through Tunnel' is on this is the full length out the far side. " +
                 "The rock auto-grows so the bore always stays enclosed.")]
        [Range(6f, 120f)] public float tunnelLength = 30f;

        [Tooltip("THROUGH-TUNNEL MODE. Off = the tunnel dead-ends deep inside the rock (one entrance). " +
                 "On = the bore is driven clean THROUGH the rock so it opens a second mouth on the far " +
                 "side — you can walk in one end and out the other.")]
        public bool throughTunnel = false;

        [Tooltip("Radius of the tunnel bore, in metres. Must comfortably clear a walking agent. The " +
                 "mouth where it opens on the rock face is flared a little wider than this.")]
        [Range(1.5f, 6f)] public float tunnelRadius = 2.8f;

        [Header("Shaping")]
        [Tooltip("Smooth-blend radius for fusing the rock lumps. Larger = more eroded, rounded rock; " +
                 "smaller = sharper, blockier rock.")]
        [Range(0.5f, 6f)] public float smoothBlendK = 2.5f;

        [Tooltip("Noise amplitude eroding the rock surface, in metres. Gives the rock a natural " +
                 "crumbling-sandstone look instead of a smooth geometric solid. Auto-clamped so it " +
                 "cannot punch holes through the rock.")]
        [Range(0f, 5f)] public float surfaceNoise = 2f;
    }

    /// <summary>
    /// CAVE ENTRANCE — a big rock standing up on the terrain with a tunnel bored into it that goes
    /// deep. A single voxel SDF feature (negative = solid rock, positive = air).
    ///
    /// CONSTRUCTION — deliberately simple and robust (every step is a BOUNDED primitive, so no step
    /// can ever carve solid rock into air outside the region it is meant to touch):
    ///
    ///   1. ROCK         — a vertical tapered capsule (broad base on the terrain, narrower top high
    ///                     above) is the dominant standing mass. A few deterministic satellite spheres
    ///                     are smooth-min'd onto it so the silhouette reads as a natural lumpy rock.
    ///   2. TUNNEL AIR   — a single straight capsule from a mouth on the rock face to a dead-end deep
    ///                     inside. Its direction is the horizontal heading tilted DOWN by
    ///                     <see cref="CaveEntranceSettings.tunnelSteepness"/> (0 = flat, 1 = a steep
    ///                     shaft). A short wider capsule at the start flares the mouth.
    ///   3. CARVE        — air = max(rock, -tunnel): a hard SDF subtraction. Because the tunnel is a
    ///                     finite capsule it can only ever remove rock along its own length — there is
    ///                     no half-space plane that can eat the rest of the rock (the bug in the old
    ///                     implementation).
    ///   4. EROSION      — surface noise displaces the iso-surface, HARD-CLAMPED to a safe fraction of
    ///                     the tunnel radius so it can never punch a spurious hole.
    ///
    /// The capsule tunnel already has a smooth curved floor an agent walks along; no separate
    /// floor-flatten pass is needed (the old masked-half-space floor was the source of the disappearing
    /// rock). At high <c>tunnelSteepness</c> the shaft is genuinely too steep to walk — that is the
    /// knob behaving as asked, not a bug.
    ///
    /// ORIENTATION: with a valid <see cref="FeaturePath"/> the tunnel's horizontal heading follows the
    /// path's first segment (mouth at path start); otherwise it faces the box's long axis.
    ///
    /// Registration: <c>Register(() => new CaveEntranceFeature());</c>
    /// </summary>
    public sealed class CaveEntranceFeature : TerrainFeature
    {
        /// <summary>Per-feature settings; injected via <see cref="ApplySettings"/> before build.</summary>
        public CaveEntranceSettings Settings { get; set; } = new CaveEntranceSettings();

        /// <inheritdoc/>
        public override TerrainFeatureType FeatureType => TerrainFeatureType.CaveEntrance;

        /// <inheritdoc/>
        public override TerrainDensityKind DensityKind => TerrainDensityKind.Voxel;

        /// <summary>The tunnel descends below the terrain — skip the skirt blend so the underground
        /// shaft is not lifted up and resealed. <see cref="TerrainFeature.HasSubTerrainGeometry"/>.</summary>
        public override bool HasSubTerrainGeometry => true;

        /// <inheritdoc/>
        public override object CreateDefaultSettings() => new CaveEntranceSettings();

        /// <inheritdoc/>
        public override void ApplySettings(object settings)
        {
            Settings = settings as CaveEntranceSettings ?? new CaveEntranceSettings();
        }

        /// <inheritdoc/>
        public override ITerrainDensity BuildDensity(FeatureContext context)
        {
            CaveEntranceSettings cfg = Settings ?? new CaveEntranceSettings();
            TerrainFeatureTuning tuning = context.Tuning;
            int seed = context.Seed;

            // --- Anchor: the rock stands ON the terrain at the footprint centre. ----------------
            Vector3 centre = context.LocalBounds.center;
            float groundY = context.LocalGroundHeight(centre.x, centre.z);

            // --- Tunnel direction. The tunnel is a straight bore: a horizontal heading tilted DOWN
            //     by tunnelSteepness (0 = flat, 1 = a steep mine shaft). --------------------------
            Vector3 heading = TunnelHeading(context);
            float steep = Mathf.Clamp01(cfg.tunnelSteepness);
            float pitch = Mathf.Lerp(0f, 78f * Mathf.Deg2Rad, steep);  // 0 = flat, 1 = steep shaft
            Vector3 boreDir = (heading * Mathf.Cos(pitch) + Vector3.down * Mathf.Sin(pitch)).normalized;

            float boreR = Mathf.Max(1f, cfg.tunnelRadius);
            bool through = cfg.throughTunnel;
            // tunnelLength is now an ABSOLUTE length in metres, measured along the bore slope.
            float boreLen = Mathf.Max(6f, cfg.tunnelLength);

            // --- Rock dimensions FIRST. The rock is a tall standing mass with a deep underground
            //     root. Its radius is sized so the bore is fully contained:
            //       • Dead-end mode  — the rock must be wide enough to swallow the whole bore.
            //       • Through mode   — the rock must be NARROWER than the bore so the bore exits the
            //                          far side; the bore length then sets the rock width.
            //     Everything that depends on the rock face (the mouths) is placed off the REAL radius.
            float horizReach = boreLen * Mathf.Cos(pitch);                  // bore's XZ travel
            float rockR;
            if (through)
                // The rock spans LESS than the bore's horizontal travel so the bore pierces clean
                // through; clamped so the rock still reads as a substantial mass around the tunnel.
                rockR = Mathf.Clamp(horizReach * 0.42f, Mathf.Max(8f, cfg.rockSize * 0.5f), cfg.rockSize);
            else
                // The rock fully encloses the dead-ending bore (extra margin past the bore end).
                rockR = Mathf.Max(Mathf.Max(2f, cfg.rockSize), horizReach + boreR * 4f);
            float rockH = cfg.rockSize * Mathf.Max(1f, cfg.rockHeight);
            float topR = Mathf.Lerp(rockR, rockR * 0.15f, Mathf.Clamp01(cfg.taper));

            // Mouth opens on the real rock face at a comfortable standing height above the terrain.
            float mouthY = groundY + Mathf.Max(boreR + 2f, rockH * 0.22f);
            // The face point is on the rock's true surface; the bore STARTS a little outside it (in
            // clear air) so the capsule provably punches a clean hole through the surface.
            Vector3 facePoint = new Vector3(centre.x, mouthY, centre.z) - heading * rockR;
            Vector3 mouth = facePoint - boreDir * (boreR * 1.5f);   // start outside, in air
            Vector3 boreEnd = mouth + boreDir * boreLen;

            // How far the bore descends below the terrain — the rock root must reach at least this
            // deep so the shaft stays ENCLOSED in rock instead of drilling out the rock's underside.
            float boreLowestY = Mathf.Min(mouth.y, boreEnd.y) - boreR;
            float rootDepth = Mathf.Max(cfg.rockSize * 0.3f, groundY - boreLowestY + cfg.rockSize * 0.4f);
            Vector3 rockBase = new Vector3(centre.x, groundY - rootDepth, centre.z);  // deep root
            Vector3 rockTop = new Vector3(centre.x, groundY + rockH, centre.z);

            // --- Entrance alcove — a generous bowl carved into the rock face right at the mouth so
            //     the opening reads as a clear, walk-in entrance and not a pinhole tube. It is a
            //     sphere centred just inside the face; the bore continues from its back wall. ------
            float alcoveR = boreR * 2.2f;
            Vector3 alcoveCentre = facePoint + heading * (alcoveR * 0.35f);

            // Through-tunnel: a matching exit alcove flares the second mouth where the bore leaves the
            // far rock face, so the far end reads as a proper opening too. Centre it on the bore axis
            // a little BEFORE the bore end (the bore already over-shoots the far surface).
            bool hasExit = through;
            Vector3 exitAlcoveCentre = boreEnd - boreDir * (alcoveR * 0.35f);

            // Flared throat — a short wider capsule joining the alcove to the bore.
            Vector3 flareEnd = mouth + boreDir * (boreR * 4f);
            float flareR = boreR * 1.6f;

            // --- Satellite lumps — deterministic spheres fused on for an organic silhouette. They are
            //     kept on the BACK and SIDES of the rock (never blocking the entrance face) so they
            //     cannot bury the mouth. ----------------------------------------------------------
            float entranceAng = Mathf.Atan2(-heading.z, -heading.x);  // direction the mouth faces
            const int lumpCount = 5;
            var lumps = new Vector4[lumpCount];                  // xyz = centre, w = radius
            for (int i = 0; i < lumpCount; i++)
            {
                // Spread lumps around the rear hemisphere, away from the entrance heading.
                float spread = (TerrainNoiseHelper.Hash01(seed, i * 13 + 1) - 0.5f) * Mathf.PI * 1.3f;
                float ang = entranceAng + Mathf.PI + spread;
                float hT = TerrainNoiseHelper.Hash01(seed, i * 13 + 2) * 0.55f;       // 0..0.55 up rock
                float dist = rockR * (0.5f + TerrainNoiseHelper.Hash01(seed, i * 13 + 3) * 0.35f);
                float r = Mathf.Lerp(rockR * 0.6f, rockR * 0.25f, hT)
                        * (0.75f + TerrainNoiseHelper.Hash01(seed, i * 13 + 4) * 0.45f);
                float lx = centre.x + Mathf.Cos(ang) * dist;
                float lz = centre.z + Mathf.Sin(ang) * dist;
                float ly = groundY + rockH * hT;
                lumps[i] = new Vector4(lx, ly, lz, r);
            }

            // --- Erosion amplitude — the feature's own surfaceNoise PLUS the shared detail layer,
            //     clamped so even a violently jagged setting can never punch through the rock. ----
            float rawNoise = cfg.surfaceNoise + (tuning != null ? tuning.detailStrength : 0f);
            float noiseAmp = Mathf.Min(rawNoise, boreR * 0.45f);

            // --- Capture immutables for the deterministic SDF lambda. ---------------------------
            float blendK = cfg.smoothBlendK;
            // Capture the whole tuning so the erosion routes through the shared DetailedNoise — the
            // central "Surface detail" dials (octaves / roughness / lacunarity / ridged) then reach
            // this feature exactly like every heightfield feature.
            TerrainFeatureTuning noiseTuning = tuning;
            Vector3 rBase = rockBase, rTop = rockTop;
            Vector3 bMouth = mouth, bEnd = boreEnd, fEnd = flareEnd;
            Vector3 aCentre = alcoveCentre, exitCentre = exitAlcoveCentre;
            bool exitOpen = hasExit;
            float baseR = rockR, topRadius = topR, boreRadius = boreR, flareRadius = flareR;
            float alcoveRadius = alcoveR;
            Vector4[] rockLumps = lumps;

            // --- SDF lambda ---------------------------------------------------------------------
            System.Func<Vector3, float> sdf = p =>
            {
                // 1. ROCK — a vertical tapered capsule, the dominant standing mass.
                float rock = TaperedCapsule(p, rBase, rTop, baseR, topRadius);

                // 1b. Satellite lumps fused on for a natural silhouette.
                for (int i = 0; i < rockLumps.Length; i++)
                {
                    Vector4 l = rockLumps[i];
                    float s = SdfPrimitives.Sphere(p, new Vector3(l.x, l.y, l.z), l.w);
                    rock = SdfPrimitives.SmoothMin(rock, s, blendK);
                }

                // 2. EROSION — applied to the ROCK SURFACE ONLY, before the tunnel is carved. Gating
                //    on the rock field (not the carved field) guarantees noise can never fill or seal
                //    the tunnel: the tunnel walls are interior to the rock, far from rock==0, so the
                //    falloff there is zero. Displacement is clamped so it stays cosmetic.
                if (noiseAmp > 0f)
                {
                    float falloff = 1f - Mathf.Clamp01(Mathf.Abs(rock) / Mathf.Max(0.01f, noiseAmp * 2.5f));
                    if (falloff > 0f)
                    {
                        float n = TerrainNoiseHelper.DetailUnit(p, noiseTuning, seed);
                        rock += n * noiseAmp * falloff;
                    }
                }

                // 3. TUNNEL AIR — the carve-away volume: an entrance ALCOVE (a sphere bowl at the
                //    rock face giving a clear walk-in opening) unioned with the BORE capsule (the
                //    shaft driving deep) and a FLARE throat joining them. Union = nearest air.
                float alcove = SdfPrimitives.Sphere(p, aCentre, alcoveRadius);
                float bore = SdfPrimitives.Capsule(p, bMouth, bEnd, boreRadius);
                float flare = SdfPrimitives.Capsule(p, bMouth, fEnd, flareRadius);
                float tunnel = Mathf.Min(alcove, Mathf.Min(bore, flare));

                // 3b. Through-tunnel — a matching exit alcove flares the far mouth so the bore opens a
                //     clean second portal you can walk out of.
                if (exitOpen)
                    tunnel = Mathf.Min(tunnel, SdfPrimitives.Sphere(p, exitCentre, alcoveRadius));

                // 4. CARVE — air = max(rock, -tunnel). A HARD subtraction (no smooth lip): a smooth
                //    blend would round the mouth shut where the tunnel grazes the rock surface.
                return Mathf.Max(rock, -tunnel);
            };

            // --- Volume bounds — enclose the rock, every lump and the bore, with padding. --------
            Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            void Encl(Vector3 c, float r)
            {
                lo = Vector3.Min(lo, c - Vector3.one * r);
                hi = Vector3.Max(hi, c + Vector3.one * r);
            }
            Encl(rockBase, rockR);
            Encl(rockTop, Mathf.Max(topR, 1f));
            foreach (Vector4 l in lumps) Encl(new Vector3(l.x, l.y, l.z), l.w);
            Encl(mouth, flareR);
            Encl(alcoveCentre, alcoveR);
            Encl(boreEnd, boreR);
            if (hasExit) Encl(exitAlcoveCentre, alcoveR);

            float pad = context.VoxelSize * 2f + noiseAmp + blendK;
            lo -= Vector3.one * pad;
            hi += Vector3.one * pad;
            Bounds volumeBounds = new Bounds((lo + hi) * 0.5f, hi - lo);

            return new VoxelSdfDensity(sdf, volumeBounds);
        }

        // -----------------------------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// SDF of a capsule whose radius varies linearly from <paramref name="rA"/> at <paramref name="a"/>
        /// to <paramref name="rB"/> at <paramref name="b"/> — a rounded cone. Used for the standing rock
        /// so it tapers from a broad base to a narrower top. Negative inside, positive outside.
        /// </summary>
        static float TaperedCapsule(Vector3 p, Vector3 a, Vector3 b, float rA, float rB)
        {
            Vector3 pa = p - a;
            Vector3 ba = b - a;
            float baLen2 = Vector3.Dot(ba, ba);
            float h = baLen2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(pa, ba) / baLen2) : 0f;
            float axisDist = (pa - ba * h).magnitude;
            return axisDist - Mathf.Lerp(rA, rB, h);
        }

        /// <summary>
        /// Normalised horizontal heading the tunnel drives into the rock. Uses the path first-segment
        /// direction when the path is valid (≥2 points), otherwise the box's longer XZ axis. Always
        /// horizontal — vertical pitch comes from <c>tunnelSteepness</c>.
        /// </summary>
        static Vector3 TunnelHeading(FeatureContext context)
        {
            var spline = new FeatureSpline(context.Path);
            if (spline.IsValid)
            {
                Vector3 t = spline.Tangent(0f);
                t.y = 0f;
                if (t.sqrMagnitude > 1e-4f) return t.normalized;
            }
            Bounds b = context.LocalBounds;
            return b.size.x >= b.size.z ? Vector3.right : Vector3.forward;
        }
    }
}
