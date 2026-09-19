using System;
using System.IO;
using System.Linq;
using SpaceGame.Items;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Authors everything the flamethrower's fire is made of: the jet that leaves the muzzle, the
    /// billows rolling off its tip, the embers it throws, the smoke behind it, the pilot flame, and
    /// the patch prefab left burning where it lands.
    ///
    /// <para>
    /// A script rather than hand-authored YAML for the reason the foam gun's spray is one: a
    /// particle system is roughly two hundred serialized fields across a dozen modules, and six of
    /// them written by hand is twenty thousand lines nobody can review, re-tune or diff. Tuning
    /// belongs in the constants below, and re-running is how the fire is changed.
    /// </para>
    /// <para>
    /// It EDITS the lance's prefab rather than rebuilding it. That prefab carries the model, the
    /// grip, the gauge, the tank, the item asset and the network registration, none of which this
    /// builder knows how to reproduce; it replaces the <c>Jet</c> subtree and nothing else. The
    /// ground-fire prefab is the opposite case and is built whole, because it is entirely this
    /// builder's own and is deliberately NOT a network prefab — see <see cref="GroundFire"/>.
    /// </para>
    /// </summary>
    public static class FlamethrowerJetBuilder
    {
        private const string PrefabPath =
            "Assets/Game/Prefabs/Items/Artifacts/Gadgets/Flamethrower.prefab";

        private const string GroundFirePath =
            "Assets/Game/Prefabs/Items/Artifacts/Gadgets/GroundFire.prefab";

        /// <summary>
        /// The fire that stands on a burning body. In Resources because
        /// <see cref="SpaceGame.Items.BurningVisual"/> is attached to bodies at runtime and so has
        /// no Inspector anybody could wire it through.
        /// </summary>
        private const string BodyFirePath = "Assets/Game/Resources/Effects/BodyFire.prefab";

        private const string MaterialDir = "Assets/Game/Art/Materials/Items";
        private const string CoreMatPath = MaterialDir + "/FlameCore.mat";
        private const string BillowMatPath = MaterialDir + "/FlameBillow.mat";
        private const string EmberMatPath = MaterialDir + "/FlameEmber.mat";

        /// <summary>
        /// The smoke is the laser staff's, on purpose. It is a plain dark unlit particle material
        /// and a second identical one would be a second thing to keep in step for no gain — the
        /// rule of three has not been reached (`references/code-quality.md`).
        /// </summary>
        private const string SmokeMatPath = "Assets/Game/Art/Materials/Weapons/LaserSmoke.mat";

        private const string FlameShader = "SpaceGame/Effects/FlameBillboard";

        /// <summary>The names this builder owns. Everything under them is replaced on every run.</summary>
        private const string FlameName = "Flame";
        private const string EmbersName = "Embers";
        private const string SmokeName = "Smoke";
        private const string PilotName = "Pilot";
        private const string LightName = "FlameLight";

        // ── The numbers ────────────────────────────────────────────────────────
        //
        // The jet's reach is speed times lifetime and nothing else — no drag on the core, and no
        // limit-over-lifetime curve — precisely so that it can be read off these two lines and
        // matched against the item's own six-metre range. Anything that decelerated the core would
        // make the visible reach a thing you can only find by looking.

        /// <summary>Metres per second the core leaves the muzzle at.</summary>
        private const float CoreSpeedMin = 12f;
        private const float CoreSpeedMax = 16f;

        /// <summary>Seconds the core lives. Times the speed above, that is the reach.</summary>
        private const float CoreLifeMin = 0.38f;
        private const float CoreLifeMax = 0.46f;

        /// <summary>
        /// Half-angle of the visible jet at the muzzle, in degrees. Deliberately much narrower than
        /// the item's 25° gameplay cone: a jet reads as pressure, and the noise widens it into
        /// something close to the cone by the time it arrives. The cone being the more generous of
        /// the two is the right way round — a player should never be surprised that something they
        /// clearly hit did not catch.
        /// </summary>
        private const float JetAngle = 10f;

        // How the flame is CUT, shared by all three materials: only brightness, noise scale, cut
        // depth and halo differ between them. These live here rather than being left to the
        // shader's defaults because a material freezes those at creation — see EnsureFlameMaterial.

        /// <summary>How fast the noise field scrolls down through the flame, so it licks upward.</summary>
        private const float NoiseRise = 1.6f;

        /// <summary>
        /// Vertical elongation of the noise, below 1: features come out taller than they are wide.
        /// It used to be the number that decided whether the fire read as tongues or as bubbles,
        /// and it was pushed hard for that. The quad's own candle profile carries the axis now, so
        /// this only has to keep the features from being round — pushed as far as it was, the noise
        /// barely changed up a quad and a whole tongue swelled and shrank as one lump.
        /// </summary>
        private const float VerticalDraw = 0.35f;

        /// <summary>How far the coarse octave displaces the fine one. Filaments rather than cells.</summary>
        private const float Curl = 1.9f;

        /// <summary>How deep the noise bites into the silhouette, and how fine the second bite is.</summary>
        private const float NoiseBite = 1.15f;
        private const float DetailBite = 0.9f;

        /// <summary>How hard the alpha edge is. Small, because the stylized look is a hard cut.</summary>
        private const float CutWidth = 0.07f;

        /// <summary>
        /// How hard the flame's edge is, measured against its own local width so the drawn tip is
        /// cut as crisply as the belly.
        /// </summary>
        private const float EdgeHardness = 2.6f;

        // The candle profile the shader cuts out of every quad: half-width at the belly, how
        // quickly the root rounds off, and the exponent that draws the tip to a point. A quad shaped
        // like a flame is the whole reason the jet stopped reading as a raft of bubbles — a radial
        // falloff is round however hard the noise chews it.
        private const float FlameWidth = 0.72f;
        private const float RootRound = 0.55f;
        private const float TipDraw = 0.9f;

        /// <summary>How far the noise leans the upper half of a tongue off its axis.</summary>
        private const float TongueSway = 0.45f;

        /// <summary>
        /// How much harder the noise breathes the tip than the root. Above 1 the tip can be pinched
        /// off entirely, which is what throws a lick clear of the tongue below it.
        /// </summary>
        private const float TipShredding = 2.4f;

        /// <summary>How far the bands cool between the root of a flame and its tip.</summary>
        private const float TipCooling = 0.55f;

        /// <summary>What is left of a flame's width as it dies: it is drawn thin, never faded.</summary>
        private const float DeathWidth = 0.8f;

        /// <summary>
        /// Radians either side of upright a flame quad is started at. Small on purpose: the shader
        /// draws a tongue with a tip and a root, so the quads have to keep their axis. A full 0..2pi
        /// spin is what forces a fire sprite to be round in the first place, and a round sprite is a
        /// bubble. The jitter is only there to stop hundreds of quads reading as one stamp.
        /// </summary>
        private const float TongueTilt = 12f * Mathf.Deg2Rad;

        /// <summary>Where the four colour bands change over, hottest first.</summary>
        private const float CoreBand = 0.72f;
        private const float MidBand = 0.46f;
        private const float EdgeBand = 0.2f;

        // The fire's palette. Four steps, hot to soot, shared by every layer so the jet and the
        // patch it leaves behind are visibly the same fire rather than two oranges that nearly
        // match. Taken off JetFlame.shader's bands, which is what the jetpack already burns.
        private static readonly Color FireCore = new Color(1.00f, 0.97f, 0.85f);
        private static readonly Color FireMid = new Color(1.00f, 0.72f, 0.20f);
        private static readonly Color FireEdge = new Color(0.95f, 0.33f, 0.05f);
        private static readonly Color FireSoot = new Color(0.36f, 0.09f, 0.03f);

        [MenuItem("Tools/SpaceGame/Items/Build Flamethrower Fire")]
        public static void Build()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null)
            {
                Debug.LogError($"[Flamethrower] No prefab at {PrefabPath}.");
                return;
            }

            try
            {
                Transform jetRoot = FindDeep(root.transform, "Jet");
                if (jetRoot == null)
                {
                    Debug.LogError("[Flamethrower] No 'Jet' transform on the prefab; there is " +
                                   "nothing to hang the fire off.");
                    return;
                }

                if (!AttachFire(root, jetRoot)) return;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Flamethrower] Fire rebuilt on " + PrefabPath + " and " + GroundFirePath + ".");
        }

        /// <summary>
        /// Hang the whole fire — jet, embers, smoke, pilot, light, and the ground-fire prefab it
        /// lays — under <paramref name="jetRoot"/> on a prefab that carries a
        /// <see cref="FlameJet"/> and a <see cref="FlamethrowerArtifact"/>.
        ///
        /// <para>
        /// Public because the lance is no longer the only thing that throws this fire: the Flame
        /// Gauntlet's builder calls it on a prefab it has just assembled. The materials and the
        /// ground/body fire prefabs are (re)built on every call, which is how the lance's build
        /// always worked; two callers now share one fire rather than each owning a copy of it.
        /// </para>
        /// </summary>
        public static bool AttachFire(GameObject root, Transform jetRoot)
        {
            Material core = EnsureFlameMaterial(CoreMatPath, brightness: 4.2f, noiseScale: 14f,
                                                cut: 0.26f, halo: 0.1f);
            Material billow = EnsureFlameMaterial(BillowMatPath, brightness: 2.6f, noiseScale: 9f,
                                                  cut: 0.32f, halo: 0.08f);
            Material ember = EnsureFlameMaterial(EmberMatPath, brightness: 6f, noiseScale: 22f,
                                                 cut: 0.18f, halo: 0.22f);
            if (core == null || billow == null || ember == null) return false;

            var smoke = AssetDatabase.LoadAssetAtPath<Material>(SmokeMatPath);
            if (smoke == null)
            {
                Debug.LogError($"[Flamethrower] No smoke material at {SmokeMatPath}.");
                return false;
            }

            GameObject groundFire = BuildGroundFirePrefab(billow, core, smoke);
            if (groundFire == null) return false;
            if (BuildBodyFirePrefab(billow, core, smoke) == null) return false;

            ClearOwned(jetRoot, FlameName, EmbersName, SmokeName, PilotName);

            ParticleSystem flame = BuildJet(jetRoot, core, billow);
            ParticleSystem embers = BuildEmbers(jetRoot, ember);
            ParticleSystem fumes = BuildSmoke(jetRoot, smoke);
            ParticleSystem pilot = BuildPilot(jetRoot, core);
            Light light = EnsureLight(jetRoot);

            WireJet(root, jetRoot, flame, embers, fumes, pilot, light);
            WireArtifact(root, groundFire);
            return true;
        }

        // ── The jet ────────────────────────────────────────────────────────────

        /// <summary>
        /// The flame itself: one parent system with the rest of the fire hung under it.
        ///
        /// <para>
        /// The hierarchy is the interface. <see cref="FlameJet"/> holds one reference per channel
        /// and plays, stops and throttles it WITH its children, so a layer added here needs no new
        /// serialized field and no second thing to remember to put out.
        /// </para>
        /// <para>
        /// Everything simulates in WORLD space, and that is the single most important line in this
        /// file. Fire already in the air has to keep going where it was thrown when the player
        /// swings the lance; in local space the whole jet swings rigidly with the barrel, which is
        /// exactly what "the flames only come out of the gun" looks like.
        /// </para>
        /// </summary>
        private static ParticleSystem BuildJet(Transform parent, Material core, Material billow)
        {
            ParticleSystem jet = NewSystem(parent, FlameName);

            var main = jet.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(CoreLifeMin, CoreLifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(CoreSpeedMin, CoreSpeedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(0.28f, 0.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-TongueTilt, TongueTilt);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.12f, 0.02f);
            main.maxParticles = 700;

            Rate(jet, 210f);
            Cone(jet, JetAngle, 0.035f);

            // Fire expands as it burns. Without the growth a stream of discrete quads reads as a
            // line of pellets however good the shader on them is.
            Grow(jet, 0.55f, 3.2f);
            Heat(jet, hold: 0.45f);
            Turbulence(jet, strength: 2.6f, frequency: 1.8f, scroll: 1.6f);

            ConfigureRenderer(jet.GetComponent<ParticleSystemRenderer>(), core);

            BuildBillows(jet.transform, billow);
            BuildWisps(jet.transform, core);
            return jet;
        }

        /// <summary>
        /// The rolling fire the jet leaves behind it: slow, fat, buoyant and long-lived.
        ///
        /// <para>
        /// This is the layer that answers "it should persist like a real flamethrower". The core
        /// crosses six metres in under half a second and is gone; these hang in the air behind it
        /// for over a second, rising and spreading, so the space in front of the player stays full
        /// of fire for as long as the trigger is down instead of showing one thin lance.
        /// </para>
        /// </summary>
        private static ParticleSystem BuildBillows(Transform parent, Material material)
        {
            ParticleSystem ps = NewSystem(parent, "Billows");

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.95f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-TongueTilt, TongueTilt);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.55f, -0.2f);
            main.maxParticles = 500;

            Rate(ps, 100f);
            Cone(ps, JetAngle * 1.8f, 0.06f);
            Grow(ps, 0.6f, 3.6f);
            Heat(ps, hold: 0.35f);
            Turbulence(ps, strength: 2.2f, frequency: 1.1f, scroll: 1.1f);

            // They lose their speed as they lose their heat, which is what turns a jet into a cloud
            // at the far end instead of a lance that stops dead.
            Drag(ps, 1.9f);

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), material);
            return ps;
        }

        /// <summary>
        /// The licks: small, fast, very hot, and thrown about hard. The chaos in the fire — they
        /// break the jet's silhouette so it never reads as a smooth cone.
        /// </summary>
        private static ParticleSystem BuildWisps(Transform parent, Material material)
        {
            ParticleSystem ps = NewSystem(parent, "Wisps");

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(13f, 19f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.3f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-TongueTilt, TongueTilt);
            main.maxParticles = 500;

            Rate(ps, 160f);
            Cone(ps, JetAngle * 1.4f, 0.03f);
            Grow(ps, 0.7f, 2.2f);
            Heat(ps, hold: 0.5f);
            Turbulence(ps, strength: 4.5f, frequency: 3.2f, scroll: 2.4f);

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), material);
            return ps;
        }

        /// <summary>
        /// Burning specks thrown clear of the jet. Heavy, stretched along their own velocity, and
        /// they bounce — the only layer that touches the world, and the one that says where the
        /// ground is.
        /// </summary>
        private static ParticleSystem BuildEmbers(Transform parent, Material material)
        {
            ParticleSystem ps = NewSystem(parent, EmbersName);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(9f, 18f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
            main.maxParticles = 300;

            Rate(ps, 40f);
            Cone(ps, JetAngle * 2.2f, 0.04f);
            Heat(ps, hold: 0.5f);
            Turbulence(ps, strength: 1.4f, frequency: 2.6f, scroll: 1f);
            Bounce(ps);

            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            ConfigureRenderer(renderer, material);
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.06f;
            renderer.lengthScale = 2.6f;
            return ps;
        }

        /// <summary>Smoke lifting off the tail of the jet, well behind the fire itself.</summary>
        private static ParticleSystem BuildSmoke(Transform parent, Material material)
        {
            ParticleSystem ps = NewSystem(parent, SmokeName);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.5f, -0.15f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.16f, 0.14f, 0.13f), new Color(0.30f, 0.27f, 0.25f));
            main.maxParticles = 260;

            Rate(ps, 32f);
            Cone(ps, JetAngle * 2.4f, 0.05f);
            Grow(ps, 0.7f, 4.2f);
            Fade(ps, rise: 0.25f, hold: 0.45f, peak: 0.5f);
            Turbulence(ps, strength: 1.6f, frequency: 0.8f, scroll: 0.6f);
            Drag(ps, 1.4f);

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), material);
            return ps;
        }

        /// <summary>
        /// The pilot flame at the muzzle. Lit for as long as the lance is held, whether or not the
        /// trigger is down — the only warning anything standing in front of it gets
        /// (GDC-L1-FEEL-0004).
        /// </summary>
        private static ParticleSystem BuildPilot(Transform parent, Material material)
        {
            ParticleSystem ps = NewSystem(parent, PilotName);
            ps.transform.localPosition = new Vector3(-0.024f, 0f, 0.014f);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.24f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-TongueTilt, TongueTilt);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.25f, -0.05f);
            main.maxParticles = 60;

            Rate(ps, 45f);
            Cone(ps, 14f, 0.012f);
            Grow(ps, 0.8f, 1.6f);
            Heat(ps, hold: 0.35f);

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), material);
            return ps;
        }

        /// <summary>
        /// The muzzle light. Kept rather than rebuilt when it is already there, because
        /// <see cref="FlameJet"/> moves it down the plume with the throttle and its authored
        /// position is only where it sits at rest.
        /// </summary>
        private static Light EnsureLight(Transform parent)
        {
            Transform existing = parent.Find(LightName);

            GameObject holder = existing != null
                ? existing.gameObject
                : new GameObject(LightName);

            holder.transform.SetParent(parent, false);

            var light = holder.GetComponent<Light>();
            if (light == null) light = holder.AddComponent<Light>();

            light.type = LightType.Point;
            light.color = FireMid;
            light.shadows = LightShadows.None;
            light.enabled = false;
            return light;
        }

        // ── The fire left on the ground ────────────────────────────────────────

        /// <summary>
        /// The patch prefab, built whole because it is entirely this builder's own.
        ///
        /// <para>
        /// It has no NetworkObject, no SaveableEntity and no PickupableItem, and none of those is
        /// an oversight. Every machine lays its own patches from the same aim stream, and fire on
        /// the sand is not something a save should bring back five minutes later — see
        /// <see cref="GroundFire"/>.
        /// </para>
        /// </summary>
        private static GameObject BuildGroundFirePrefab(Material billow, Material core, Material smoke)
        {
            var root = new GameObject("GroundFire");

            try
            {
                ParticleSystem flame = BuildPatchFlame(root.transform, billow, core);
                ParticleSystem scorch = BuildPatchScorch(root.transform, smoke);

                var glowHolder = new GameObject("Glow");
                glowHolder.transform.SetParent(root.transform, false);
                glowHolder.transform.localPosition = new Vector3(0f, 0.45f, 0f);

                Light glow = glowHolder.AddComponent<Light>();
                glow.type = LightType.Point;
                glow.color = FireEdge;
                glow.shadows = LightShadows.None;
                glow.enabled = false;

                var fire = root.AddComponent<GroundFire>();
                SetPrivate(fire, "flame", flame);
                SetPrivate(fire, "scorch", scorch);
                SetPrivate(fire, "glow", glow);

                Directory.CreateDirectory(Path.GetDirectoryName(GroundFirePath) ?? string.Empty);
                return PrefabUtility.SaveAsPrefabAsset(root, GroundFirePath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The patch's own flames. LOCAL simulation space, unlike the jet: a patch does not move,
        /// and local space is what lets one authored disc of fire be dropped anywhere in the world
        /// without every emitter having to be re-aimed.
        /// </summary>
        private static ParticleSystem BuildPatchFlame(Transform parent, Material billow, Material core)
        {
            ParticleSystem ps = NewSystem(parent, "Flame", world: false);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.9f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-TongueTilt, TongueTilt);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.7f, -0.3f);
            main.maxParticles = 120;

            Rate(ps, 34f);
            Rise(ps, 0.9f, 14f);
            Grow(ps, 0.7f, 2.1f);
            Heat(ps, hold: 0.35f);
            Turbulence(ps, strength: 1.1f, frequency: 1.6f, scroll: 1.2f);

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), billow);

            BuildPatchLicks(ps.transform, core);
            return ps;
        }

        /// <summary>The bright tongues at the heart of a patch, so it is not one flat orange smudge.</summary>
        private static ParticleSystem BuildPatchLicks(Transform parent, Material material)
        {
            ParticleSystem ps = NewSystem(parent, "Licks", world: false);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 3.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.28f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-TongueTilt, TongueTilt);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.9f, -0.4f);
            main.maxParticles = 90;

            Rate(ps, 28f);
            Rise(ps, 0.6f, 10f);
            Grow(ps, 0.8f, 1.7f);
            Heat(ps, hold: 0.45f);
            Turbulence(ps, strength: 2.4f, frequency: 2.8f, scroll: 2f);

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), material);
            return ps;
        }

        /// <summary>The smoke off a patch. Thin, slow, and it outlives the flames above it.</summary>
        private static ParticleSystem BuildPatchScorch(Transform parent, Material material)
        {
            ParticleSystem ps = NewSystem(parent, "Scorch", world: false);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.4f, -0.15f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.14f, 0.12f, 0.11f), new Color(0.28f, 0.25f, 0.23f));
            main.maxParticles = 60;

            Rate(ps, 9f);
            Rise(ps, 0.75f, 18f);
            Grow(ps, 0.6f, 3f);
            Fade(ps, rise: 0.2f, hold: 0.4f, peak: 0.45f);
            Turbulence(ps, strength: 0.8f, frequency: 0.7f, scroll: 0.5f);

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), material);
            return ps;
        }

        // ── The fire on a burning body ─────────────────────────────────────────

        /// <summary>
        /// The flames that stand on anything the fire has caught: a crate, a creature, a player.
        ///
        /// <para>
        /// Authored at ONE METRE and scaled to the body it lands on by
        /// <see cref="SpaceGame.Items.BurningVisual"/>, which is what lets one prefab serve a
        /// pebble and an ostrich. Every number below is therefore a fraction of a metre rather than
        /// a size, and the emitters are shaped as a sphere so the fire wraps a body from every side
        /// instead of standing on top of it like a patch does.
        /// </para>
        /// <para>
        /// LOCAL simulation space, unlike the jet. A burning creature runs, and its fire has to go
        /// with it; in world space the flames would be left standing where the creature was lit.
        /// </para>
        /// </summary>
        private static GameObject BuildBodyFirePrefab(Material billow, Material core, Material smoke)
        {
            var root = new GameObject("BodyFire");

            try
            {
                ParticleSystem flame = NewSystem(root.transform, "Flame", world: false);

                var main = flame.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.42f);
                main.startRotation = new ParticleSystem.MinMaxCurve(-TongueTilt, TongueTilt);
                main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.5f, -0.2f);
                main.maxParticles = 70;

                // Radius times two is BurningVisual.PrefabWidth, which is what the fitted scale
                // divides the body's width by. Change one and change the other.
                Rate(flame, 22f);
                Wrap(flame, 0.4f);
                Grow(flame, 0.7f, 1.6f);
                Heat(flame, hold: 0.35f);
                Turbulence(flame, strength: 0.9f, frequency: 1.8f, scroll: 1.2f);

                ConfigureRenderer(flame.GetComponent<ParticleSystemRenderer>(), billow);
                ScaleWithBody(flame);

                ScaleWithBody(BuildBodyLicks(flame.transform, core));
                ScaleWithBody(BuildBodySmoke(flame.transform, smoke));

                Directory.CreateDirectory(Path.GetDirectoryName(BodyFirePath) ?? string.Empty);
                return PrefabUtility.SaveAsPrefabAsset(root, BodyFirePath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>The bright tongues, so a burning body is not one flat orange smear.</summary>
        private static ParticleSystem BuildBodyLicks(Transform parent, Material material)
        {
            ParticleSystem ps = NewSystem(parent, "Licks", world: false);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.18f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-TongueTilt, TongueTilt);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.7f, -0.3f);
            main.maxParticles = 50;

            Rate(ps, 18f);
            Wrap(ps, 0.3f);
            Grow(ps, 0.8f, 1.4f);
            Heat(ps, hold: 0.45f);
            Turbulence(ps, strength: 1.4f, frequency: 2.8f, scroll: 1.6f);

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), material);
            return ps;
        }

        /// <summary>
        /// The smoke off a burning body, and the one layer that simulates in WORLD space: smoke
        /// left behind is what makes a creature running while alight read as running while alight
        /// rather than as carrying a lamp.
        /// </summary>
        private static ParticleSystem BuildBodySmoke(Transform parent, Material material)
        {
            ParticleSystem ps = NewSystem(parent, "Smoke");

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.28f, 0.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.45f, -0.15f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.15f, 0.13f, 0.12f), new Color(0.29f, 0.26f, 0.24f));
            main.maxParticles = 35;

            Rate(ps, 5f);
            Wrap(ps, 0.3f);
            Grow(ps, 0.7f, 2.2f);
            Fade(ps, rise: 0.2f, hold: 0.4f, peak: 0.4f);
            Turbulence(ps, strength: 0.9f, frequency: 0.8f, scroll: 0.6f);

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), material);
            return ps;
        }

        // ── Module helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// An emitter that is PLAYED rather than emitted into, and that starts silent.
        ///
        /// <para>
        /// <c>playOnAwake</c> is off and emission starts stopped, because <see cref="FlameJet"/>
        /// and <see cref="GroundFire"/> both decide when their fire is lit; a system that played on
        /// awake would pour flame out of a lance lying in the sand. Culling is
        /// <c>AlwaysSimulate</c>: the emitter is in the player's hands and the fire is up to six
        /// metres away, so the emitter's own visibility says nothing about the flame's.
        /// </para>
        /// </summary>
        private static ParticleSystem NewSystem(Transform parent, string name, bool world = true)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);

            var ps = holder.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = true;
            main.duration = 4f;
            main.playOnAwake = false;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.simulationSpace = world
                ? ParticleSystemSimulationSpace.World
                : ParticleSystemSimulationSpace.Local;
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

        /// <summary>
        /// A wide, shallow cone standing on the ground: the shape a patch of fire is.
        ///
        /// <para>
        /// NOT a Circle. A circle emitter throws its particles RADIALLY OUTWARD in its own plane,
        /// so a circle laid flat on the sand fires every flame sideways across the ground and the
        /// patch reads as a shockwave. A cone emits along its axis, and the shape rotation stands
        /// that axis up.
        /// </para>
        /// </summary>
        private static void Rise(ParticleSystem ps, float radius, float angle)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.radius = radius;
            shape.radiusThickness = 1f;
            shape.angle = angle;

            // A particle shape emits along its own +Z and the patch's transform is upright, so the
            // cone has to be stood on end by hand.
            shape.rotation = new Vector3(-90f, 0f, 0f);
        }

        /// <summary>
        /// Let the fire take its size from the transform it is hung under.
        ///
        /// <para>
        /// <c>NewSystem</c> leaves every emitter on <c>Local</c> scaling, which is right for the
        /// jet: <c>ItemGrip</c> rescales the lance to fit a hand, and on <c>Hierarchy</c> that would
        /// resize the flame with it. The body fire is the opposite case — one prefab authored at a
        /// metre and scaled by <c>BurningVisual</c> to whatever it landed on — and on <c>Local</c>
        /// it would ignore that scale entirely and draw a crate and an ostrich the same size.
        /// </para>
        /// </summary>
        private static void ScaleWithBody(ParticleSystem ps)
        {
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        }

        /// <summary>
        /// A shell around the body, so the fire wraps it rather than standing on top of it. The
        /// emission is from the sphere's SURFACE — a solid sphere would bury most of the flames
        /// inside the thing that is burning, where nobody can see them.
        /// </summary>
        private static void Wrap(ParticleSystem ps, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
            shape.radiusThickness = 0f;
        }

        private static void Grow(ParticleSystem ps, float from, float to)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, from), new Keyframe(1f, to)));
        }

        /// <summary>
        /// The fire's own cooling curve, and the single most load-bearing setting in this file.
        ///
        /// <para>
        /// <c>FlameBillboard</c> reads the particle's colour as its HEAT rather than reading its
        /// age, so this gradient — not the shader — is what decides how a flame ages from white-hot
        /// through orange to soot, and its alpha is what decides how much of the particle is still
        /// alight. Retuning the fire means retuning this.
        /// </para>
        /// </summary>
        private static void Heat(ParticleSystem ps, float hold)
        {
            var colour = ps.colorOverLifetime;
            colour.enabled = true;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(FireCore, 0f),
                    new GradientColorKey(FireMid, 0.28f),
                    new GradientColorKey(FireEdge, 0.62f),
                    new GradientColorKey(FireSoot, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, Mathf.Clamp01(hold)),
                    new GradientAlphaKey(0f, 1f)
                });

            colour.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        /// <summary>A plain fade for the layers that are smoke rather than fire.</summary>
        private static void Fade(ParticleSystem ps, float rise, float hold, float peak)
        {
            var colour = ps.colorOverLifetime;
            colour.enabled = true;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(peak, Mathf.Clamp01(rise)),
                    new GradientAlphaKey(peak, Mathf.Clamp01(hold)),
                    new GradientAlphaKey(0f, 1f)
                });

            colour.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        /// <summary>The boil. This is what separates fire from a smooth cone of orange.</summary>
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

        /// <summary>
        /// Embers stop where they land. Medium quality is the deliberate middle: High casts per
        /// particle and there are hundreds in the air (GDC-L1-PERF-0004).
        /// </summary>
        private static void Bounce(ParticleSystem ps)
        {
            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.bounce = new ParticleSystem.MinMaxCurve(0.1f, 0.35f);
            collision.dampen = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            collision.lifetimeLoss = 0.2f;
        }

        private static void ConfigureRenderer(ParticleSystemRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            // Additive fire needs no depth sort between its own quads, and sorting hundreds of them
            // every frame is a cost paid for a difference nobody can see.
            renderer.sortMode = ParticleSystemSortMode.None;
        }

        // ── Assets ─────────────────────────────────────────────────────────────

        /// <summary>
        /// One of the flame materials, created on first run and re-tuned on every run after. All
        /// three are the same shader with different bites out of it, which is what keeps the jet,
        /// its billows and its embers reading as one fire.
        /// </summary>
        private static Material EnsureFlameMaterial(string path, float brightness, float noiseScale,
                                                    float cut, float halo)
        {
            Shader shader = Shader.Find(FlameShader);
            if (shader == null)
            {
                Debug.LogError($"[Flamethrower] Shader '{FlameShader}' not found.");
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
            material.SetColor("_CoreColor", FireCore);
            material.SetColor("_MidColor", FireMid);
            material.SetColor("_EdgeColor", FireEdge);
            material.SetColor("_SootColor", FireSoot);
            material.SetFloat("_Brightness", brightness);
            material.SetFloat("_NoiseScale", noiseScale);
            material.SetFloat("_Cut", cut);
            material.SetFloat("_Halo", halo);

            // EVERY remaining value is written too, including the ones that match the shader's own
            // defaults. A .mat freezes the defaults it was BORN with, so retuning a default in the
            // shader changes nothing on a material that already exists — the shape of the flame
            // would then depend on when its material happened to be created.
            material.SetFloat("_NoiseSpeed", NoiseRise);
            material.SetFloat("_Stretch", VerticalDraw);
            material.SetFloat("_Curl", Curl);
            material.SetFloat("_Warp", NoiseBite);
            material.SetFloat("_Detail", DetailBite);
            material.SetFloat("_Softness", CutWidth);
            material.SetFloat("_Fill", EdgeHardness);
            material.SetFloat("_Belly", FlameWidth);
            material.SetFloat("_Root", RootRound);
            material.SetFloat("_Tip", TipDraw);
            material.SetFloat("_Sway", TongueSway);
            material.SetFloat("_TipBite", TipShredding);
            material.SetFloat("_Cool", TipCooling);
            material.SetFloat("_Wither", DeathWidth);
            material.SetFloat("_CoreEnd", CoreBand);
            material.SetFloat("_MidEnd", MidBand);
            material.SetFloat("_EdgeEnd", EdgeBand);

            EditorUtility.SetDirty(material);
            return material;
        }

        // ── Wiring ─────────────────────────────────────────────────────────────

        private static void WireJet(GameObject root, Transform jetRoot, ParticleSystem flame,
                                    ParticleSystem embers, ParticleSystem smoke,
                                    ParticleSystem pilot, Light light)
        {
            var jet = root.GetComponentInChildren<FlameJet>(true);
            if (jet == null)
            {
                Debug.LogError("[Flamethrower] No FlameJet on the prefab.");
                return;
            }

            SetPrivate(jet, "jetRoot", jetRoot);
            SetPrivate(jet, "flame", flame);
            SetPrivate(jet, "embers", embers);
            SetPrivate(jet, "smoke", smoke);
            SetPrivate(jet, "pilot", pilot);
            SetPrivate(jet, "flameLight", light);
        }

        private static void WireArtifact(GameObject root, GameObject groundFire)
        {
            var artifact = root.GetComponent<FlamethrowerArtifact>();
            if (artifact == null)
            {
                Debug.LogError("[Flamethrower] No FlamethrowerArtifact on the prefab.");
                return;
            }

            SetPrivate(artifact, "groundFirePrefab", groundFire);
        }

        /// <summary>
        /// Remove this builder's own subtrees so a re-run replaces rather than duplicates them.
        /// Anything it did not author is left alone, which is the whole reason it edits the prefab
        /// instead of rebuilding it.
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
                Debug.LogError($"[Flamethrower] No serialized field '{field}' on " +
                               $"{target.GetType().Name}.");
                return;
            }

            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
