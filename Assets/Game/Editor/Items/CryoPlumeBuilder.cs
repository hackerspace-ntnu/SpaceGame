using System;
using System.IO;
using System.Linq;
using SpaceGame.Items;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Authors everything the cryo sprayer's cold is made of: the plume that leaves the nozzle, the
    /// crystals thrown down it, the mist that hangs behind it, the frost blooming where it lands and
    /// the vapour blowing off a surface that will not take ice.
    ///
    /// <para>
    /// A script rather than hand-authored YAML for the reason the flamethrower's fire is one: a
    /// particle system is roughly two hundred serialized fields across a dozen modules, and five of
    /// them written by hand is twenty thousand lines nobody can review, re-tune or diff. Tuning
    /// belongs in the constants below, and re-running is how the plume is changed.
    /// </para>
    /// <para>
    /// It EDITS the sprayer's prefab rather than rebuilding it. That prefab carries the model, the
    /// grip, the gauge, the tank, the item asset and the network registration, none of which this
    /// builder knows how to reproduce. It owns three named subtrees — <c>Jet</c>, <c>Bite</c> and
    /// <c>Blowoff</c> — and nothing else, and it leaves each of them where it found it so the
    /// plume keeps coming out of the barrel rather than out of the grip.
    /// </para>
    /// <para>
    /// <b>The reach is the number to keep in step.</b> <c>CryoSprayerArtifact.range</c> decides what
    /// actually freezes; <see cref="CoreSpeedMin"/> times <see cref="CoreLifeMin"/> decides what the
    /// player SEES reaching. A plume that stops short of the range is a gun that appears not to be
    /// working at the distance it works at, which is the one mismatch a player reads as a bug
    /// (GDC-L1-FEEL-0004). There is no drag on the core and no limit curve, precisely so the visible
    /// reach can be read off two lines here.
    /// </para>
    /// </summary>
    public static class CryoPlumeBuilder
    {
        private const string PrefabPath =
            "Assets/Game/Prefabs/Items/Artifacts/Gadgets/CryoSprayer.prefab";

        private const string MaterialDir = "Assets/Game/Art/Materials/Items";
        private const string VapourMatPath = MaterialDir + "/CryoVapourSpray.mat";
        private const string ShardMatPath = MaterialDir + "/CryoShard.mat";
        private const string MistMatPath = MaterialDir + "/CryoMist.mat";

        private const string CryoShader = "SpaceGame/Effects/CryoVapour";

        /// <summary>The three subtrees this builder owns on the prefab.</summary>
        private const string JetName = "Jet";
        private const string BiteName = "Bite";
        private const string BlowoffName = "Blowoff";

        /// <summary>The layers hung under the jet. Replaced whole on every run.</summary>
        private const string ShardsName = "Shards";
        private const string MistName = "Mist";

        // ── The numbers ────────────────────────────────────────────────────────

        /// <summary>Metres per second the core leaves the nozzle at.</summary>
        private const float CoreSpeedMin = 30f;
        private const float CoreSpeedMax = 34f;

        /// <summary>
        /// Seconds the core lives. Times the speed above, that is the reach — sixteen to twenty
        /// metres, which brackets <c>CryoSprayerArtifact.range</c>'s eighteen.
        /// </summary>
        private const float CoreLifeMin = 0.55f;
        private const float CoreLifeMax = 0.6f;

        /// <summary>
        /// Half-angle of the visible plume at the nozzle, in degrees. Narrower than the fifteen
        /// <c>CryoSprayerNozzle</c> opens the parent system to, because that one is driven every
        /// frame by the trigger and these children are not — and because the noise widens the
        /// stream on its own by the time it arrives.
        /// </summary>
        private const float JetAngle = 6f;

        // How the vapour is CUT, shared by all three materials: only brightness, noise scale, cut
        // depth, facets and sparkle differ between them. They live here rather than being left to
        // the shader's defaults because a material freezes those at creation — see EnsureMaterial.

        /// <summary>How fast the noise field scrolls up through the particles, so the plume sags.</summary>
        private const float NoiseFall = 1.1f;

        /// <summary>
        /// Vertical elongation of the noise. Far closer to round than the flame's 0.22: fire is
        /// four times taller than it is wide and cold spills sideways.
        /// </summary>
        private const float VerticalDraw = 0.75f;

        /// <summary>How far the coarse octave displaces the fine one.</summary>
        private const float Curl = 1.4f;

        /// <summary>How deep the noise bites into the silhouette, and how fine the second bite is.</summary>
        private const float NoiseBite = 1f;
        private const float DetailBite = 0.55f;

        /// <summary>How hard the alpha edge is. Small, because the stylized look is a hard cut.</summary>
        private const float CutWidth = 0.09f;

        /// <summary>How much of the quad is solid before the falloff starts. See the shader.</summary>
        private const float BodyFill = 1.9f;

        /// <summary>Where the four colour bands change over, coldest first.</summary>
        private const float CoreBand = 0.74f;
        private const float IceBand = 0.48f;
        private const float DeepBand = 0.22f;

        // The cold's palette. Four steps, white to deep shadow blue, shared by every layer so the
        // plume, the crystals in it and the frost it leaves read as one substance rather than as
        // three blues that nearly match. Taken off FrozenStatue.shader, which is what the statue at
        // the far end of the plume is made of.
        private static readonly Color IceCore = new Color(0.98f, 1.00f, 1.00f);
        private static readonly Color IceMid = new Color(0.72f, 0.93f, 1.00f);
        private static readonly Color IceDeep = new Color(0.36f, 0.68f, 0.96f);
        private static readonly Color IceShadow = new Color(0.16f, 0.31f, 0.62f);

        [MenuItem("Tools/SpaceGame/Items/Build Cryo Plume")]
        public static void Build()
        {
            Material vapour = EnsureMaterial(VapourMatPath, brightness: 2.4f, noiseScale: 2.8f,
                                             cut: 0.3f, halo: 0.16f, facets: 5f, shard: 0.45f,
                                             sparkle: 1.6f, rim: 0.8f);
            Material shard = EnsureMaterial(ShardMatPath, brightness: 4.6f, noiseScale: 7f,
                                            cut: 0.2f, halo: 0.06f, facets: 3f, shard: 0.85f,
                                            sparkle: 3.4f, rim: 0.2f);
            Material mist = EnsureMaterial(MistMatPath, brightness: 1.5f, noiseScale: 1.8f,
                                           cut: 0.36f, halo: 0.2f, facets: 7f, shard: 0.25f,
                                           sparkle: 0.5f, rim: 1.1f);
            if (vapour == null || shard == null || mist == null) return;

            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null)
            {
                Debug.LogError($"[CryoSprayer] No prefab at {PrefabPath}.");
                return;
            }

            try
            {
                ParticleSystem jet = Rebuild(root, JetName, BuildJet, vapour, shard, mist);
                ParticleSystem bite = Rebuild(root, BiteName, BuildBite, vapour, shard, mist);
                ParticleSystem blowoff = Rebuild(root, BlowoffName, BuildBlowoff, vapour, shard, mist);
                if (jet == null || bite == null || blowoff == null) return;

                WireNozzle(root, jet, bite, blowoff);
                CheckReach(root);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[CryoSprayer] Plume rebuilt on " + PrefabPath + ".");
        }

        /// <summary>
        /// Replace one of this builder's subtrees in place: same parent, same local pose, same name,
        /// new contents.
        ///
        /// <para>
        /// The pose is what makes this worth doing rather than creating the objects fresh under the
        /// root. <c>Jet</c> hangs off the muzzle and <c>Bite</c> is moved to the hit point every
        /// sweep; an emitter rebuilt at the prefab's origin would put the plume in the holder's
        /// fist, which is the failure the flamethrower's own builder documents.
        /// </para>
        /// </summary>
        private static ParticleSystem Rebuild(GameObject root, string name,
                                              Func<Transform, Material, Material, Material, ParticleSystem> build,
                                              Material vapour, Material shard, Material mist)
        {
            Transform existing = FindDeep(root.transform, name);
            if (existing == null)
            {
                Debug.LogError($"[CryoSprayer] No '{name}' transform on the prefab; there is " +
                               "nothing to hang the cold off.");
                return null;
            }

            Transform parent = existing.parent;
            Vector3 position = existing.localPosition;
            Quaternion rotation = existing.localRotation;

            UnityEngine.Object.DestroyImmediate(existing.gameObject);

            ParticleSystem built = build(parent, vapour, shard, mist);
            built.name = name;
            built.transform.localPosition = position;
            built.transform.localRotation = rotation;
            return built;
        }

        // ── The plume ──────────────────────────────────────────────────────────

        /// <summary>
        /// The stream itself: dense, fast and white at the nozzle, with the crystals and the mist
        /// hung under it.
        ///
        /// <para>
        /// The hierarchy is the interface. <c>CryoSprayerNozzle</c> holds one reference and plays,
        /// stops and opens it WITH its children, so a layer added here needs no new serialized
        /// field and no second thing to remember to shut off.
        /// </para>
        /// <para>
        /// Everything simulates in WORLD space, and that is the single most important line in this
        /// file. Vapour already in the air has to stay where it was sprayed when the player swings
        /// the gun; in local space the whole plume swings rigidly with the barrel, which is exactly
        /// what "the spray only comes out of the nozzle" looks like.
        /// </para>
        /// </summary>
        private static ParticleSystem BuildJet(Transform parent, Material vapour, Material shard,
                                               Material mist)
        {
            ParticleSystem jet = NewSystem(parent, JetName);

            var main = jet.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(CoreLifeMin, CoreLifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(CoreSpeedMin, CoreSpeedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.42f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            // Cold falls. Small and positive, against the flame's small and negative — it is what
            // makes a plume held on a wall pour down it rather than climb it.
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0.05f, 0.18f);
            main.maxParticles = 700;

            Rate(jet, 220f);
            Cone(jet, JetAngle, 0.03f);

            // Vapour expands as it loses pressure. Without the growth a stream of discrete quads
            // reads as a line of pellets however good the shader on them is.
            Grow(jet, 0.5f, 3.4f);
            Chill(jet, hold: 0.5f);
            Turbulence(jet, strength: 2.2f, frequency: 1.6f, scroll: 1.2f);

            ConfigureRenderer(jet.GetComponent<ParticleSystemRenderer>(), vapour);

            BuildShards(jet.transform, shard);
            BuildMist(jet.transform, mist);
            return jet;
        }

        /// <summary>
        /// The crystals: small, very fast, stretched along their own velocity and thrown about
        /// hard. They are what says the plume is ice rather than steam, and they are the layer that
        /// carries the eye out to the end of the reach.
        /// </summary>
        private static ParticleSystem BuildShards(Transform parent, Material material)
        {
            ParticleSystem ps = NewSystem(parent, ShardsName);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.68f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(34f, 40f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.15f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0.15f, 0.5f);
            main.maxParticles = 400;

            Rate(ps, 90f);
            Cone(ps, JetAngle * 1.6f, 0.025f);
            Chill(ps, hold: 0.7f);
            Turbulence(ps, strength: 1.6f, frequency: 2.8f, scroll: 1.4f);

            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            ConfigureRenderer(renderer, material);
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.05f;
            renderer.lengthScale = 3.2f;
            return ps;
        }

        /// <summary>
        /// The freezing fog the jet leaves behind it: slow, fat, sinking and long-lived.
        ///
        /// <para>
        /// This is the layer that answers "the gun should keep the space in front of it cold". The
        /// core crosses eighteen metres in about half a second and is gone; these hang for two,
        /// spreading and settling, so a held trigger fills the ground ahead of the player instead
        /// of drawing one thin line.
        /// </para>
        /// </summary>
        private static ParticleSystem BuildMist(Transform parent, Material material)
        {
            ParticleSystem ps = NewSystem(parent, MistName);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(14f, 24f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0.08f, 0.25f);
            main.maxParticles = 500;

            Rate(ps, 90f);
            Cone(ps, JetAngle * 2.4f, 0.05f);
            Grow(ps, 0.6f, 4f);
            Chill(ps, hold: 0.3f);
            Turbulence(ps, strength: 1.8f, frequency: 0.9f, scroll: 0.8f);

            // They lose their speed as they settle, which is what turns a jet into a bank of fog at
            // the far end instead of a lance that stops dead.
            Drag(ps, 2.2f);

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), material);
            return ps;
        }

        // ── The landing ────────────────────────────────────────────────────────

        /// <summary>
        /// Frost taking hold where the plume lands — on a body it is freezing, or on ground that
        /// will accept a sheet of ice.
        ///
        /// <para>
        /// Moved to the hit point fifteen times a second by <c>CryoSprayerNozzle</c>, which is why
        /// it must simulate in WORLD space: in local space the crystals already in the air are
        /// dragged along with the system and the frost smears across the ground.
        /// </para>
        /// </summary>
        private static ParticleSystem BuildBite(Transform parent, Material vapour, Material shard,
                                                Material mist)
        {
            ParticleSystem ps = NewSystem(parent, BiteName);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.85f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.34f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.15f, 0.05f);
            main.maxParticles = 260;

            Rate(ps, 110f);

            // A hemisphere rather than a cone: the frost has to bloom back out of whatever was hit
            // in every direction, and the system is re-aimed at nothing — it is only ever moved.
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.12f;
            shape.radiusThickness = 0f;

            Grow(ps, 0.4f, 2.4f);
            Chill(ps, hold: 0.45f);
            Turbulence(ps, strength: 1.1f, frequency: 2.2f, scroll: 0.9f);

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), shard);
            return ps;
        }

        /// <summary>
        /// Vapour blowing off a surface that will not freeze — dry sand, a wall. This is the
        /// refusal, and it is deliberately the dullest thing this builder makes: it has to be
        /// legible as "nothing is happening here" at a glance (GDC-L1-SYS-0006).
        /// </summary>
        private static ParticleSystem BuildBlowoff(Transform parent, Material vapour,
                                                   Material shard, Material mist)
        {
            ParticleSystem ps = NewSystem(parent, BlowoffName);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.25f, -0.05f);
            main.maxParticles = 200;

            Rate(ps, 70f);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.2f;
            shape.radiusThickness = 1f;

            Grow(ps, 0.7f, 3f);
            Chill(ps, hold: 0.2f);
            Turbulence(ps, strength: 1.4f, frequency: 1.2f, scroll: 0.7f);
            Drag(ps, 1.6f);

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), mist);
            return ps;
        }

        // ── Module helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// An emitter that is PLAYED rather than emitted into, and that starts silent.
        ///
        /// <para>
        /// <c>playOnAwake</c> is off and emission starts stopped, because
        /// <c>CryoSprayerNozzle</c> decides when the valve is open; a system that played on awake
        /// would pour vapour out of a gun lying in the sand. Culling is <c>AlwaysSimulate</c>: the
        /// emitter is in the player's hands and the cold is up to eighteen metres away, so the
        /// emitter's own visibility says nothing about the plume's.
        /// </para>
        /// <para>
        /// Scaling is <c>Local</c>, so <c>ItemGrip</c> rescaling the gun to fit a hand does not
        /// rescale the eighteen metres of plume with it.
        /// </para>
        /// </summary>
        private static ParticleSystem NewSystem(Transform parent, string name)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);

            var ps = holder.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = true;
            main.duration = 4f;
            main.playOnAwake = false;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;

            ps.Stop(withChildren: false, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        private static void Rate(ParticleSystem ps, float perSecond)
        {
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = perSecond;
            emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
        }

        private static void Cone(ParticleSystem ps, float angle, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = radius;
        }

        private static void Grow(ParticleSystem ps, float from, float to)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, from), new Keyframe(1f, to)));
        }

        /// <summary>
        /// The cold's own gradient, and the single most load-bearing setting in this file.
        ///
        /// <para>
        /// <c>CryoVapour</c> reads the particle's colour as how cold it is rather than reading its
        /// age, so this gradient — not the shader — decides how a puff ages from white through ice
        /// blue to shadow, and its alpha decides how much of the particle is left at all. Retuning
        /// the plume means retuning this.
        /// </para>
        /// </summary>
        private static void Chill(ParticleSystem ps, float hold)
        {
            var colour = ps.colorOverLifetime;
            colour.enabled = true;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(IceCore, 0f),
                    new GradientColorKey(IceMid, 0.3f),
                    new GradientColorKey(IceDeep, 0.68f),
                    new GradientColorKey(IceShadow, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, Mathf.Clamp01(hold)),
                    new GradientAlphaKey(0f, 1f)
                });

            colour.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        /// <summary>The boil. This is what separates a plume from a smooth cone of blue.</summary>
        private static void Turbulence(ParticleSystem ps, float strength, float frequency, float scroll)
        {
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(strength * 0.35f, strength);
            noise.frequency = frequency;
            noise.damping = false;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.scrollSpeed = scroll;
        }

        /// <summary>Air resistance, so a layer slows and spreads instead of flying on forever.</summary>
        private static void Drag(ParticleSystem ps, float drag)
        {
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0f;
            limit.drag = new ParticleSystem.MinMaxCurve(drag);
            limit.multiplyDragByParticleSize = true;
        }

        private static void ConfigureRenderer(ParticleSystemRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            // The plume blends rather than adding, so its quads DO occlude one another — but they
            // are one pale substance seen against itself, and sorting hundreds of them every frame
            // buys a difference nobody can see (GDC-L1-PERF-0004).
            renderer.sortMode = ParticleSystemSortMode.None;
        }

        // ── Assets ─────────────────────────────────────────────────────────────

        /// <summary>
        /// One of the cold materials, created on first run and re-tuned on every run after. All
        /// three are the same shader with different bites out of it, which is what keeps the plume,
        /// its crystals and the fog behind it reading as one substance.
        /// </summary>
        private static Material EnsureMaterial(string path, float brightness, float noiseScale,
                                               float cut, float halo, float facets, float shard,
                                               float sparkle, float rim)
        {
            Shader shader = Shader.Find(CryoShader);
            if (shader == null)
            {
                Debug.LogError($"[CryoSprayer] Shader '{CryoShader}' not found.");
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
            material.SetColor("_CoreColor", IceCore);
            material.SetColor("_IceColor", IceMid);
            material.SetColor("_DeepColor", IceDeep);
            material.SetColor("_ShadowColor", IceShadow);
            material.SetFloat("_Brightness", brightness);
            material.SetFloat("_NoiseScale", noiseScale);
            material.SetFloat("_Cut", cut);
            material.SetFloat("_Halo", halo);
            material.SetFloat("_Facets", facets);
            material.SetFloat("_Shard", shard);
            material.SetFloat("_Sparkle", sparkle);
            material.SetFloat("_Rim", rim);

            // EVERY remaining value is written too, including the ones that match the shader's own
            // defaults. A .mat freezes the defaults it was BORN with, so retuning a default in the
            // shader changes nothing on a material that already exists — the look of the plume
            // would then depend on when its material happened to be created.
            material.SetFloat("_NoiseSpeed", NoiseFall);
            material.SetFloat("_Stretch", VerticalDraw);
            material.SetFloat("_Curl", Curl);
            material.SetFloat("_Warp", NoiseBite);
            material.SetFloat("_Detail", DetailBite);
            material.SetFloat("_Softness", CutWidth);
            material.SetFloat("_Fill", BodyFill);
            material.SetFloat("_CoreEnd", CoreBand);
            material.SetFloat("_IceEnd", IceBand);
            material.SetFloat("_DeepEnd", DeepBand);

            EditorUtility.SetDirty(material);
            return material;
        }

        // ── Wiring ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Say so, loudly, when the plume no longer reaches as far as the gun does.
        ///
        /// <para>
        /// The two numbers live in different files and one of them is SERIALIZED ON THE PREFAB, so
        /// editing the artifact's default alone changes nothing that ships — which is exactly how
        /// the sprayer spent a version freezing at five metres while its own source said nine. A
        /// warning rather than an overwrite: the reach is a tuning decision and belongs in the
        /// Inspector, so this checks it rather than taking it.
        /// </para>
        /// </summary>
        private static void CheckReach(GameObject root)
        {
            var artifact = root.GetComponent<CryoSprayerArtifact>();
            if (artifact == null)
            {
                Debug.LogError("[CryoSprayer] No CryoSprayerArtifact on the prefab.");
                return;
            }

            SerializedProperty range = new SerializedObject(artifact).FindProperty("range");
            if (range == null) return;

            float shortest = CoreSpeedMin * CoreLifeMin;
            float longest = CoreSpeedMax * CoreLifeMax;

            if (range.floatValue >= shortest && range.floatValue <= longest) return;

            Debug.LogWarning($"[CryoSprayer] The prefab freezes at {range.floatValue} m but the " +
                             $"plume reaches {shortest:0.#}-{longest:0.#} m. One of the two is " +
                             "wrong, and the player reads the difference as a gun that does not " +
                             "work at the distance it works at.");
        }

        private static void WireNozzle(GameObject root, ParticleSystem jet, ParticleSystem bite,
                                       ParticleSystem blowoff)
        {
            var nozzle = root.GetComponentInChildren<CryoSprayerNozzle>(true);
            if (nozzle == null)
            {
                Debug.LogError("[CryoSprayer] No CryoSprayerNozzle on the prefab.");
                return;
            }

            SetPrivate(nozzle, "plume", jet);
            SetPrivate(nozzle, "bite", bite);
            SetPrivate(nozzle, "blowoff", blowoff);
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
                Debug.LogError($"[CryoSprayer] No serialized field '{field}' on " +
                               $"{target.GetType().Name}.");
                return;
            }

            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
