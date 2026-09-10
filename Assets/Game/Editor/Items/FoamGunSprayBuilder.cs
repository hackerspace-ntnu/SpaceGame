using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FirstGearGames.SmoothCameraShaker;
using SpaceGame.Items;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Authors the foam gun's spray: the jet that leaves the bell, the blast on the trigger, the
    /// gob thrown back out of a landed dab, and the camera kick behind them.
    ///
    /// <para>
    /// It is FOAM, and the numbers here are what say so. The clumps are opaque, their silhouettes
    /// are bitten rather than round, and the emission rates are high enough that the stream reads
    /// as one mass instead of as countable balls — roughly 4200 clumps a second with the froth
    /// behind it. Thin any of that out, or round the edges back off, and it goes straight back to
    /// looking like a bubble gun.
    /// </para>
    ///
    /// <para>
    /// A script rather than hand-authored YAML because a particle system is roughly two hundred
    /// serialized fields across a dozen modules, and a hand-written one is unreviewable and unable
    /// to be re-tuned. Tuning belongs in the numbers below.
    /// </para>
    /// <para>
    /// It EDITS the existing prefab rather than rebuilding it — unlike the gravel blaster, this
    /// gun's prefab is hand-authored and carries wiring (the gauge, the grip, the tank, the item
    /// asset, the network registration) that no builder here knows how to reproduce. Re-running
    /// replaces the effect subtrees wholesale and leaves everything else alone.
    /// </para>
    /// </summary>
    public static class FoamGunSprayBuilder
    {
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/FoamGun.prefab";
        private const string MaterialDir = "Assets/Game/Art/Materials/Items";
        private const string FoamMatPath = MaterialDir + "/FoamSpray.mat";
        private const string MistMatPath = MaterialDir + "/FoamMist.mat";

        /// <summary>
        /// Both spray materials are the same shader, tuned differently. It ray traces each quad
        /// into a sphere and then bites the silhouette with a noise field, so a clump is a lumpy
        /// opaque piece of foam rather than the untextured RECTANGLE a stock particle material
        /// draws, and rather than the perfect circle a plain impostor draws — see
        /// FoamSpray.shader's header for why every bubble cue was removed.
        /// </summary>
        private const string FoamShader = "SpaceGame/Artifacts/FoamSpray";

        /// <summary>
        /// The kick is SEEDED from the shared damage shake on first run and belongs to this gun
        /// from then on — copied rather than referenced so tuning the spray cannot retune what
        /// being hit feels like. Never overwrites a live asset.
        /// </summary>
        private const string ShakeSourcePath = "Assets/Game/ScriptableObjects/Shake/DamageShake.asset";
        private const string SprayShakePath = "Assets/Game/ScriptableObjects/Shake/FoamSprayShake.asset";

        /// <summary>The names this builder owns. Anything under them is replaced on every run.</summary>
        private const string JetName = "Jet";
        private const string BlastName = "MuzzleBlast";
        private const string SplatName = "ImpactSplat";

        // NO START COLOURS HERE, and that is the palette contract rather than an omission. The
        // shader picks each bubble's colour off a four-entry ladder chosen to sit on one column of
        // the lattice, and the vertex colour is MULTIPLIED IN AFTER that pick — so a tinted
        // emitter would drag every band off its entry and the quantizer would scatter the result.
        // White here means the vertex stream carries the fade's alpha and nothing else, and the
        // colour variation is made where it can be made safely: on the scalar, before the snap.
        // See FoamSpray.shader and ArtifactSubstance.hlsl.

        [MenuItem("Tools/Build Foam Gun Spray VFX")]
        public static void Build()
        {
            // The gobs: solid foam, tightly celled and raggedly edged.
            Material foam = EnsureFoamMaterial(FoamMatPath, opacity: 1.0f, cellScale: 8f,
                                               edgeBite: 0.42f, shadeJitter: 0.26f);

            // The froth around the core: the same substance at a coarser cell and a rougher edge,
            // and very slightly thinner so the mass has some depth to it. Still opaque — a
            // see-through outer layer is what made the whole spray read as soap.
            Material mist = EnsureFoamMaterial(MistMatPath, opacity: 0.9f, cellScale: 5f,
                                               edgeBite: 0.55f, shadeJitter: 0.34f);
            if (foam == null || mist == null) return;

            ShakeData shake = EnsureShake();
            if (shake == null) return;

            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null) { Debug.LogError($"[FoamGun] No prefab at {PrefabPath}."); return; }

            try
            {
                Transform muzzle = FindDeep(root.transform, "Muzzle");
                if (muzzle == null)
                {
                    Debug.LogError("[FoamGun] No Muzzle transform on the prefab; nothing to hang " +
                                   "the jet off.");
                    return;
                }

                ClearOwned(muzzle, JetName, BlastName);
                ClearOwned(root.transform, SplatName);

                ParticleSystem jet = BuildJet(muzzle, foam, mist);
                ParticleSystem blast = BuildBlast(muzzle, mist);
                ParticleSystem splat = BuildSplat(root.transform, foam);
                ParticleSystem splatRing = BuildSplatRing(splat.transform, mist);

                WireNozzle(root, jet);
                WireFx(root, splat, splatRing, blast, shake);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[FoamGun] Spray VFX rebuilt on " + PrefabPath + ".");
        }

        // ── The jet ────────────────────────────────────────────────────────────

        /// <summary>
        /// The stream itself: one parent system with the rest of the spray hung under it.
        ///
        /// <para>
        /// The hierarchy is the interface. <see cref="FoamGunNozzle"/> holds ONE reference and
        /// plays it with its children, and aims it by rotating that one transform — so every layer
        /// added here is picked up with no new serialized field and no second thing to remember to
        /// stop.
        /// </para>
        /// <para>
        /// Everything simulates in WORLD space. Foam already in the air must keep its own arc when
        /// the player swings the gun; in local space the whole stream would swing rigidly with the
        /// barrel, which is the single tell that separates a jet from a cone stuck to a gun.
        /// </para>
        /// </summary>
        private static ParticleSystem BuildJet(Transform muzzle, Material foam, Material mist)
        {
            ParticleSystem jet = NewSystem(muzzle, JetName, loop: true);

            // ── The core: the fat gobs that carry the stream. Mesh particles rather than
            // billboards, because foam that is about to become a solid sphere should already look
            // like one in the air.
            var main = jet.main;
            // THE THREE NUMBERS THAT HAVE TO AGREE WITH FoamGunArtifact. The gun traces the dab's
            // landing along SprayArc from sprayTravelSpeed (22), sprayGravity (1.4) and
            // sprayFlightTime (2 s); the stream the player watches is an ordinary ParticleSystem
            // under Unity's own gravity, and it flies that same parabola only while its start
            // speed, gravity modifier and longest life are those same three numbers. Disagree and
            // the foam lands somewhere the stream was never seen to go, which reads as the gun
            // being inaccurate rather than as a mismatch. FoamGunWiringTests holds them together.
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(19f, 25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.20f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(1.1f, 1.7f);
            // Rate times longest life. Reached only when spraying at open sky, where nothing kills
            // a droplet early — a stream that lands is a fraction of it, about 1400 in the air on
            // a level shot. It has to be the full product even so: a system at its ceiling stops
            // emitting, which starves the stream at the BELL rather than trimming its tail.
            main.maxParticles = 8400;
            main.startColor = Color.white;

            // NO ROTATION, on any system here. A sphere impostor is rotation-invariant, so a spin
            // buys nothing — and startRotation3D on a BILLBOARD actively breaks it: it tilts the
            // camera-facing quad out of plane and the round silhouette foreshortens into a flat
            // lens. That was left over from when these were sphere meshes, and it is what the
            // stray flying discs in the spray turned out to be.

            Rate(jet, 4200f);
            Cone(jet, 6.5f, 0.035f);

            // Foam expands as it leaves the pressure — the growth is what makes a stream of
            // discrete particles read as a continuous mass rather than as a line of pellets.
            Grow(jet, 0.6f, 1.7f);

            // The turbulence is the "violent" in this gun. Without it a jet at this rate is a
            // clean, calm cylinder; with it the stream boils.
            Turbulence(jet, strength: 1.5f, frequency: 2.2f);

            // Foam that hits a wall short of the aim point has to stop there, or the stream
            // visibly passes through the geometry the dab lands on.
            Splash(jet, bounce: 0.12f, lifetimeLoss: 0.55f);

            // Billboard, not Mesh. The shader ray traces the quad into a sphere, so a mesh here
            // would be several hundred triangles buying a silhouette two triangles already have.
            ConfigureRenderer(jet.GetComponent<ParticleSystemRenderer>(), foam,
                              ParticleSystemRenderMode.Billboard);

            BuildFroth(jet.transform, mist);
            BuildSpatter(jet.transform, foam);
            BuildBellVent(jet.transform, mist);
            return jet;
        }

        /// <summary>
        /// The cloud around the core. Slow, wide and short-lived: this is the layer that gives the
        /// jet a silhouette, and without it the core alone reads as a spray of dots.
        ///
        /// <para>
        /// NOT see-through. This is builders' foam, not soap — the shell is nearly opaque and the
        /// thin film is cut to a trace of what a bubble wants. A translucent version of this layer
        /// is what made the whole spray read as bubble gum.
        /// </para>
        /// </summary>
        private static ParticleSystem BuildFroth(Transform parent, Material material)
        {
            ParticleSystem ps = NewSystem(parent, "Froth", loop: true);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(7f, 15f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.34f);
            // Under the core's fall, not at it: the cloud is the lighter half of the same foam and
            // hangs behind the gobs as the stream droops, which is what gives an arc a silhouette.
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.maxParticles = 2200;
            main.startColor = Color.white;

            Rate(ps, 1800f);
            Cone(ps, 15f, 0.05f);
            Grow(ps, 0.45f, 1.9f);
            Fade(ps, hold: 0.35f);
            Turbulence(ps, strength: 1.1f, frequency: 1.4f);

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), material,
                              ParticleSystemRenderMode.Billboard);
            return ps;
        }

        /// <summary>
        /// Flecks thrown off the stream: small, fast, heavy, and stretched along their own
        /// velocity. They carry past the aim point and fall, which is what makes the jet look like
        /// it is under pressure rather than being placed.
        /// </summary>
        private static ParticleSystem BuildSpatter(Transform parent, Material material)
        {
            ParticleSystem ps = NewSystem(parent, "Spatter", loop: true);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(20f, 34f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.11f);
            // Heavier than the core, still: flecks that outrun the stream and drop out of it are
            // what make the jet read as pressure rather than as placement.
            main.gravityModifier = new ParticleSystem.MinMaxCurve(1.7f, 2.4f);
            main.maxParticles = 900;
            main.startColor = Color.white;

            Rate(ps, 420f);
            Cone(ps, 13f, 0.03f);
            Splash(ps, bounce: 0.25f, lifetimeLoss: 0.3f);

            // BILLBOARD, not Stretch, and this was measured rather than assumed. Stretch scales
            // the quad along the velocity while the impostor keeps tracing a round sphere inside
            // it, so a fleck came out as a flat lens seen edge-on — a flying contact lens, not a
            // droplet. The speed reads off the motion; it does not need the quad's help.
            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), material,
                              ParticleSystemRenderMode.Billboard);
            return ps;
        }

        /// <summary>
        /// What escapes back around the bell. A wide, slow, nearly-still puff at the muzzle: it is
        /// the detail that says the gun is venting pressure it cannot fully contain.
        /// </summary>
        private static ParticleSystem BuildBellVent(Transform parent, Material material)
        {
            ParticleSystem ps = NewSystem(parent, "BellVent", loop: true);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.17f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.1f, 0.05f);
            main.maxParticles = 600;
            main.startColor = Color.white;

            Rate(ps, 540f);
            Cone(ps, 78f, 0.05f);
            Grow(ps, 0.5f, 1.7f);
            Fade(ps, hold: 0.3f);

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), material,
                              ParticleSystemRenderMode.Billboard);
            return ps;
        }

        // ── The press and the impact ───────────────────────────────────────────

        /// <summary>
        /// The pressure wave on the trigger going down: a handful of big sheets thrown a couple of
        /// metres and gone inside a third of a second. It exists so the START of a spray is an
        /// event rather than a fade-in (GDC-L1-FEEL-0004).
        /// </summary>
        private static ParticleSystem BuildBlast(Transform muzzle, Material material)
        {
            ParticleSystem ps = NewSystem(muzzle, BlastName, loop: false);

            var main = ps.main;
            main.duration = 0.4f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(9f, 17f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.42f);
            main.gravityModifier = 0f;
            main.maxParticles = 90;
            main.startColor = Color.white;

            Burst(ps, 14);
            Cone(ps, 24f, 0.05f);
            Grow(ps, 0.4f, 2.2f);
            Fade(ps, hold: 0.25f);

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), material,
                              ParticleSystemRenderMode.Billboard);
            return ps;
        }

        /// <summary>
        /// The gob thrown back out of a landed dab. EMITTED INTO rather than played: one system
        /// serves every impact, moved to each landing point in turn — see
        /// <see cref="Manual"/> for the three things that have to be true for that to work.
        ///
        /// <para>
        /// Parented to the ROOT, not the muzzle, because it is moved to wherever the dab landed —
        /// up to the gun's full reach away, in a direction the barrel is no longer pointing.
        /// </para>
        /// </summary>
        private static ParticleSystem BuildSplat(Transform root, Material foam)
        {
            ParticleSystem ps = Manual(NewSystem(root, SplatName, loop: true));

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.15f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0.7f, 1.5f);
            main.maxParticles = 2600;
            main.startColor = Color.white;

            // A hemisphere off the surface: the shape module spreads the burst, which is the whole
            // reason the system is moved to the landing point rather than the particles placed at it.
            Cone(ps, 62f, 0.06f);
            Splash(ps, bounce: 0.2f, lifetimeLoss: 0.4f);

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), foam,
                              ParticleSystemRenderMode.Billboard);
            return ps;
        }

        /// <summary>
        /// The flat puff that spreads across the surface under a splat.
        ///
        /// <para>
        /// A CHILD of the gob system, so moving that one to a landing point moves this one too —
        /// but wired to <see cref="FoamSprayFx"/> as its own reference, because
        /// <c>ParticleSystem.Emit</c> reaches exactly one system and never its children. A ring
        /// left to be "emitted with its parent" simply never appears.
        /// </para>
        /// </summary>
        private static ParticleSystem BuildSplatRing(Transform parent, Material material)
        {
            ParticleSystem ps = Manual(NewSystem(parent, "SplatRing", loop: true));

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.32f);
            main.gravityModifier = 0f;
            main.maxParticles = 900;
            main.startColor = Color.white;

            // Flat against the surface rather than out of it: the ring spreads, the gobs fly.
            Cone(ps, 84f, 0.05f);
            Grow(ps, 0.45f, 1.9f);
            Fade(ps, hold: 0.2f);

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), material,
                              ParticleSystemRenderMode.Billboard);
            return ps;
        }

        // ── Module helpers ─────────────────────────────────────────────────────

        private static ParticleSystem NewSystem(Transform parent, string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1f;
            main.loop = loop;

            // Never on awake: a gun lying in the sand would spray, and the nozzle is the one thing
            // that decides when foam comes out.
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // LOCAL scaling, not Hierarchy: ItemGrip rescales this prefab to fit the hand, and a
            // splat twenty metres away must not be drawn at the size of the gun.
            main.scalingMode = ParticleSystemScalingMode.Local;
            return ps;
        }

        private static void Rate(ParticleSystem ps, float perSecond)
        {
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = perSecond;
            emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
        }

        private static void Burst(ParticleSystem ps, short count)
        {
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });
        }

        private static void Cone(ParticleSystem ps, float angle, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = radius;
        }

        /// <summary>
        /// Foam expands as it leaves the pressure. A share of start size, over life.
        ///
        /// <para>
        /// TWO curves, randomised between per particle, rather than one shared curve. The start
        /// size range alone varies how big a bubble begins; without this they all then swell by
        /// the same factor, and a spray of visibly different bubbles becomes a spray of identical
        /// ones a third of a second later.
        /// </para>
        /// </summary>
        private static void Grow(ParticleSystem ps, float from, float to)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, from), new Keyframe(1f, to * 0.55f)),
                new AnimationCurve(new Keyframe(0f, from), new Keyframe(1f, to)));
        }

        /// <summary>Full opacity for <paramref name="hold"/> of the life, then out.</summary>
        private static void Fade(ParticleSystem ps, float hold)
        {
            var colour = ps.colorOverLifetime;
            colour.enabled = true;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, Mathf.Clamp01(hold)),
                    new GradientAlphaKey(0f, 1f)
                });

            colour.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        /// <summary>The boil. This is what separates a violent jet from a clean cone.</summary>
        private static void Turbulence(ParticleSystem ps, float strength, float frequency)
        {
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(strength * 0.4f, strength);
            noise.frequency = frequency;
            noise.damping = false;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.scrollSpeed = 1.4f;
        }

        /// <summary>
        /// Foam stops where it lands. Medium quality is the deliberate middle: High casts per
        /// particle, and this jet has hundreds in the air at once (GDC-L1-PERF-0004).
        /// </summary>
        private static void Splash(ParticleSystem ps, float bounce, float lifetimeLoss)
        {
            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.bounce = new ParticleSystem.MinMaxCurve(bounce * 0.4f, bounce);
            collision.dampen = new ParticleSystem.MinMaxCurve(0.5f, 0.85f);
            collision.lifetimeLoss = lifetimeLoss;
        }

        /// <summary>
        /// Turn a built system into one that is EMITTED INTO rather than played.
        ///
        /// <para>
        /// Three things have to be true at once: it must be playing (a stopped system never
        /// simulates the particles handed to it), it must not emit on its own (an authored rate
        /// would pour foam out of the gun the moment it is equipped), and it must keep simulating
        /// while the emitter is off screen — the gun is in the player's hands and the impact is up
        /// to twenty metres away, so the emitter's own visibility says nothing about theirs.
        /// </para>
        /// </summary>
        private static ParticleSystem Manual(ParticleSystem ps)
        {
            var main = ps.main;
            main.loop = true;
            main.duration = 5f;
            main.playOnAwake = true;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            var emission = ps.emission;
            emission.enabled = false;
            emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
            return ps;
        }

        private static void ConfigureRenderer(ParticleSystemRenderer renderer, Material material,
                                              ParticleSystemRenderMode mode)
        {
            renderer.sharedMaterial = material;
            renderer.renderMode = mode;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            // Sorted, not None. These are alpha-blended shells now: unsorted, a near bubble drawn
            // before a far one punches a hole in it, which reads as bubbles flickering out.
            renderer.sortMode = ParticleSystemSortMode.Distance;

            SendBubbleStreams(renderer);
        }

        /// <summary>
        /// Hand each particle its own random, which is what makes a spray look like a spray.
        ///
        /// <para>
        /// The ORDER is the interface, not the list. Unity packs the active streams into the
        /// vertex layout in exactly this sequence, so Position lands on POSITION, Color on COLOR,
        /// UV on TEXCOORD0.xy, and StableRandom.xy on TEXCOORD0.zw — which is the layout
        /// FoamSpray.shader declares. Insert a stream anywhere but the end and every clump's
        /// shape and shade are read out of whatever now occupies those two floats.
        /// </para>
        /// <para>
        /// Normal is deliberately absent. The shader reconstructs the sphere's normal analytically
        /// from the quad's UV, so the one the emitter would send is a per-vertex cost paid for a
        /// value nothing reads.
        /// </para>
        /// </summary>
        private static void SendBubbleStreams(ParticleSystemRenderer renderer)
        {
            var streams = new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position,
                ParticleSystemVertexStream.Color,
                ParticleSystemVertexStream.UV,
                ParticleSystemVertexStream.StableRandomXY,
            };

            renderer.SetActiveVertexStreams(streams);
        }

        // ── Assets ─────────────────────────────────────────────────────────────

        /// <summary>
        /// One of the two spray materials, created on first run and re-tuned on every run after.
        ///
        /// <para>
        /// Deliberately NOT the foam surface shader: that one welds against the blob field and
        /// dissolves on a clock, neither of which a particle has. The four shading bands are left
        /// at the shader's defaults, which ARE FoamSurface's four — the sprayed foam and the lump
        /// it becomes have to be one substance, and two materials that each name their own
        /// off-white is how that quietly stops being true.
        /// </para>
        /// </summary>
        private static Material EnsureFoamMaterial(string path, float opacity, float cellScale,
                                                   float edgeBite, float shadeJitter)
        {
            Shader shader = Shader.Find(FoamShader);
            if (shader == null)
            {
                Debug.LogError($"[FoamGun] Shader '{FoamShader}' not found.");
                return null;
            }

            Directory.CreateDirectory(MaterialDir);

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;

            // EVERY value this build cares about is written here, including the ones that match
            // the shader's own defaults. A .mat freezes the defaults it was BORN with, so a
            // property left to the shader keeps whatever that shader said the day the material
            // was first created — retuning the default afterwards silently changes nothing.
            material.SetFloat("_LightWrap", 0.55f);
            material.SetFloat("_AmbientFloor", 0.16f);
            material.SetFloat("_EdgeShade", 0.35f);
            material.SetFloat("_CellDepth", 0.45f);
            material.SetFloat("_SoftFade", 0.35f);
            material.SetFloat("_Opacity", opacity);
            material.SetFloat("_CellScale", cellScale);
            material.SetFloat("_EdgeBite", edgeBite);
            material.SetFloat("_ShadeJitter", shadeJitter);

            EditorUtility.SetDirty(material);
            return material;
        }

        private static ShakeData EnsureShake()
        {
            var shake = AssetDatabase.LoadAssetAtPath<ShakeData>(SprayShakePath);
            if (shake != null) return shake;

            if (!AssetDatabase.CopyAsset(ShakeSourcePath, SprayShakePath))
            {
                Debug.LogError($"[FoamGun] Could not copy {ShakeSourcePath} to {SprayShakePath}.");
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<ShakeData>(SprayShakePath);
        }

        // ── Wiring ─────────────────────────────────────────────────────────────

        private static void WireNozzle(GameObject root, ParticleSystem jet)
        {
            var nozzle = root.GetComponentInChildren<FoamGunNozzle>(true);
            if (nozzle == null) { Debug.LogError("[FoamGun] No FoamGunNozzle on the prefab."); return; }

            SetPrivate(nozzle, "jet", jet);
        }

        /// <summary>
        /// The impact half, added on the first run and re-wired on every run after. On the ROOT,
        /// beside the artifact that calls it, rather than on the bell — a landing point fourteen
        /// metres away is not the bell's business.
        /// </summary>
        private static void WireFx(GameObject root, ParticleSystem splat, ParticleSystem splatRing,
                                   ParticleSystem blast, ShakeData shake)
        {
            var fx = root.GetComponent<FoamSprayFx>();
            if (fx == null) fx = root.AddComponent<FoamSprayFx>();

            SetPrivate(fx, "splat", splat);
            SetPrivate(fx, "splatRing", splatRing);
            SetPrivate(fx, "blast", blast);
            SetPrivate(fx, "sprayShake", shake);

            var artifact = root.GetComponent<FoamGunArtifact>();
            if (artifact == null) { Debug.LogError("[FoamGun] No FoamGunArtifact on the prefab."); return; }

            SetPrivate(artifact, "fx", fx);
        }

        /// <summary>
        /// Remove this builder's own subtrees so a re-run replaces rather than duplicates them.
        /// Anything the builder did not author is left alone, which is the whole reason this edits
        /// the prefab instead of rebuilding it.
        /// </summary>
        private static void ClearOwned(Transform parent, params string[] names)
        {
            foreach (string name in names)
            {
                Transform existing = parent.Find(name);
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }
        }

        private static Transform FindDeep(Transform parent, string name) =>
            parent.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        /// <summary>
        /// Item components serialize private fields, which is right for runtime code and simply
        /// means an editor script goes in the way the Inspector does.
        /// </summary>
        private static void SetPrivate(Component target, string field, UnityEngine.Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[FoamGun] No serialized field '{field}' on {target.GetType().Name}.");
                return;
            }

            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
