using System;
using System.IO;
using System.Linq;
using FirstGearGames.SmoothCameraShaker;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// The emitters a <see cref="PelletGunFx"/> drives, and the wiring that hands them to it —
    /// shared by every pellet gun's builder (<see cref="GravelBlasterBuilder"/>,
    /// <see cref="BasicGunBuilder"/>) so the two guns differ in the numbers they pass, not in two
    /// copies of how a spark is made.
    ///
    /// <para>
    /// All muzzle effects are bursts, not rates: a gun discharges in one instant, so everything it
    /// throws exists from frame one. All world-space, so debris already in the air keeps its arc
    /// when the gun is swung away. The per-pellet systems are emitted into by hand — see
    /// <see cref="Manual"/>.
    /// </para>
    /// </summary>
    public static class PelletGunFxBuilder
    {
        private const string MaterialDir = "Assets/Game/Art/Materials/Weapons";
        private const string DebrisMatPath = MaterialDir + "/GravelDebris.mat";
        private const string SparkMatPath  = MaterialDir + "/LaserSpark.mat";
        private const string SmokeMatPath  = MaterialDir + "/LaserSmoke.mat";

        /// <summary>
        /// Every gun's shake is SEEDED from the shared damage shake on first run and then belongs
        /// to that gun — copied rather than referenced so tuning a gun's kick cannot retune what
        /// being hit feels like. <see cref="EnsureShake"/> never overwrites a live asset.
        /// </summary>
        private const string ShakeSourcePath = "Assets/Game/ScriptableObjects/Shake/DamageShake.asset";

        /// <summary>Desert gravel: dry brown-grey rock, and the dust it throws.</summary>
        private static readonly Color Gravel     = new Color(0.42f, 0.36f, 0.29f);
        private static readonly Color DustLight  = new Color(0.76f, 0.68f, 0.52f);
        private static readonly Color DustDark   = new Color(0.48f, 0.42f, 0.33f);
        private static readonly Color SparkHot   = new Color(1.00f, 0.83f, 0.55f);
        private static readonly Color SparkCool  = new Color(1.00f, 0.52f, 0.18f);
        private static readonly Color FlashWarm  = new Color(1.00f, 0.72f, 0.38f);

        /// <summary>The three materials every pellet gun's effects are drawn with.</summary>
        public readonly struct Materials
        {
            public readonly Material Debris;
            public readonly Material Spark;
            public readonly Material Smoke;

            public Materials(Material debris, Material spark, Material smoke)
            {
                Debris = debris;
                Spark = spark;
                Smoke = smoke;
            }
        }

        /// <summary>Everything a built gun hands its <see cref="PelletGunFx"/>. Leave a field null to go without it.</summary>
        public sealed class Rig
        {
            public Transform Muzzle;
            public ParticleSystem MuzzleBurst;
            public ParticleSystem MuzzleDust;
            public ParticleSystem MuzzleSparks;
            public ParticleSystem MuzzleSmoke;
            public ParticleSystem BlastWave;
            public Light MuzzleFlash;
            public ParticleSystem Tracers;
            public ParticleSystem ImpactSparks;
            public ParticleSystem ImpactDust;
            public ParticleSystem ImpactDebris;
            public ParticleSystem Backfire;
            public ShakeData Shake;
        }

        /// <summary>
        /// The debris material this build owns, plus the laser staff's spark and smoke — reused
        /// rather than duplicated, for one grit-and-sparks look across the artifacts. False, with
        /// the reason logged, when any is missing.
        /// </summary>
        public static bool TryLoadMaterials(string logTag, out Materials materials)
        {
            Material debris = EnsureDebrisMaterial(logTag);
            var spark = AssetDatabase.LoadAssetAtPath<Material>(SparkMatPath);
            var smoke = AssetDatabase.LoadAssetAtPath<Material>(SmokeMatPath);
            materials = new Materials(debris, spark, smoke);

            if (debris != null && spark != null && smoke != null) return true;

            Debug.LogError($"[{logTag}] Missing material. Run Tools/Build Laser Staff Artifact first " +
                           "if LaserSpark/LaserSmoke are absent.");
            return false;
        }

        /// <summary>
        /// This gun's camera kick at <paramref name="path"/>, seeded from the shared damage shake
        /// on first run. Never overwrites an existing asset — once it is on disk it is somebody's
        /// tuning.
        /// </summary>
        public static ShakeData EnsureShake(string path, string logTag)
        {
            var shake = AssetDatabase.LoadAssetAtPath<ShakeData>(path);
            if (shake != null) return shake;

            if (!AssetDatabase.CopyAsset(ShakeSourcePath, path))
            {
                Debug.LogError($"[{logTag}] Could not copy {ShakeSourcePath} to {path}.");
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<ShakeData>(path);
        }

        /// <summary>
        /// Add a <see cref="PelletGunFx"/> to <paramref name="root"/> and hand it the rig. Through
        /// SerializedObject, the way the Inspector does it, because the component's fields are
        /// private.
        /// </summary>
        public static PelletGunFx AddFx(GameObject root, Rig rig)
        {
            var fx = root.AddComponent<PelletGunFx>();
            var so = new SerializedObject(fx);
            so.FindProperty("muzzle").objectReferenceValue = rig.Muzzle;
            so.FindProperty("muzzleBurst").objectReferenceValue = rig.MuzzleBurst;
            so.FindProperty("muzzleDust").objectReferenceValue = rig.MuzzleDust;
            so.FindProperty("muzzleSparks").objectReferenceValue = rig.MuzzleSparks;
            so.FindProperty("muzzleSmoke").objectReferenceValue = rig.MuzzleSmoke;
            so.FindProperty("blastWave").objectReferenceValue = rig.BlastWave;
            so.FindProperty("muzzleFlash").objectReferenceValue = rig.MuzzleFlash;
            so.FindProperty("pelletTracers").objectReferenceValue = rig.Tracers;
            so.FindProperty("impactSparks").objectReferenceValue = rig.ImpactSparks;
            so.FindProperty("impactDust").objectReferenceValue = rig.ImpactDust;
            so.FindProperty("impactDebris").objectReferenceValue = rig.ImpactDebris;
            so.FindProperty("backfireBurst").objectReferenceValue = rig.Backfire;
            so.FindProperty("blastShake").objectReferenceValue = rig.Shake;
            so.ApplyModifiedPropertiesWithoutUndo();
            return fx;
        }

        // ── Muzzle ─────────────────────────────────────────────────────────────

        /// <summary>A brief warm point light at the muzzle, off until a shot turns it on.</summary>
        public static Light BuildMuzzleFlash(Transform muzzle, float range, float intensity)
        {
            var flashObject = new GameObject("MuzzleFlash");
            flashObject.transform.SetParent(muzzle, false);
            Light flash = flashObject.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = FlashWarm;
            flash.range = range;
            flash.intensity = intensity;
            flash.shadows = LightShadows.None;
            flash.enabled = false;
            return flash;
        }

        /// <summary>Tumbling rock chunks: opaque cube-mesh particles that bounce off the world.</summary>
        public static ParticleSystem BuildGravel(Transform parent, Material material, string name,
                                                 short count, float minSpeed, float maxSpeed,
                                                 float cone)
        {
            ParticleSystem ps = NewSystem(parent, name);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.038f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0.9f, 1.4f);
            main.maxParticles = 200;
            main.startColor = new ParticleSystem.MinMaxGradient(Gravel, Gravel * 0.7f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            Burst(ps, count);
            Cone(ps, cone, 0.03f);

            // The bounce is the detail that sells gravel as rock rather than as glowing VFX — it
            // is the one part of the blast that acknowledges the world it lands in.
            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.bounce = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
            collision.dampen = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
            collision.lifetimeLoss = 0.2f;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            ConfigureRenderer(renderer, material, ParticleSystemRenderMode.Mesh);
            renderer.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            return ps;
        }

        /// <summary>Hot steel-and-powder sparks.</summary>
        public static ParticleSystem BuildSparks(Transform parent, Material material, string name,
                                                 short count, float cone)
        {
            ParticleSystem ps = NewSystem(parent, name);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(9f, 22f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.045f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.maxParticles = 120;
            main.startColor = new ParticleSystem.MinMaxGradient(SparkHot, SparkCool);

            Burst(ps, count);
            Cone(ps, cone, 0.02f);

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(Ramp(
                new[] { (SparkHot, 0f), (SparkCool, 0.5f), (SparkCool, 1f) },
                new[] { (1f, 0f), (1f, 0.55f), (0f, 1f) }));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            ConfigureRenderer(renderer, material, ParticleSystemRenderMode.Stretch);
            renderer.velocityScale = 0.015f;
            renderer.lengthScale = 1.8f;
            return ps;
        }

        /// <summary>The powder cloud.</summary>
        public static ParticleSystem BuildDust(Transform parent, Material material, string name,
                                               short count, float cone, bool dark = false)
        {
            ParticleSystem ps = NewSystem(parent, name);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.32f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.06f, 0.02f);
            main.maxParticles = 80;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = dark
                ? new ParticleSystem.MinMaxGradient(DustDark * 0.5f, DustDark)
                : new ParticleSystem.MinMaxGradient(DustDark, DustLight);

            Burst(ps, count);
            Cone(ps, cone, 0.05f);

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(Ramp(
                new[] { (Color.white, 0f), (Color.white, 1f) },
                new[] { (0f, 0f), (0.65f, 0.15f), (0f, 1f) }));

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.4f), new Keyframe(1f, 1f)));

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), material,
                              ParticleSystemRenderMode.Billboard);
            return ps;
        }

        /// <summary>
        /// The plume that hangs off the barrel once the shot has gone: slow, thin and long-lived,
        /// so the discharge leaves a mark on the frame after the flash is over.
        /// </summary>
        public static ParticleSystem BuildMuzzleSmoke(Transform parent, Material material,
                                                      short count)
        {
            ParticleSystem ps = BuildDust(parent, material, "MuzzleSmoke", count, cone: 12f);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.42f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.12f, -0.02f);
            return ps;
        }

        /// <summary>
        /// The pressure wave: a handful of big sheets thrown a couple of metres down the barrel
        /// and gone inside a quarter of a second. This is what gives the discharge a silhouette —
        /// without it thin streaks read as a spray of dots rather than as a blast.
        /// </summary>
        public static ParticleSystem BuildBlastWave(Transform parent, Material material,
                                                    short count, float sizeScale)
        {
            ParticleSystem ps = NewSystem(parent, "BlastWave");

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.16f, 0.26f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 14f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.45f * sizeScale, 0.8f * sizeScale);
            main.gravityModifier = 0f;
            main.maxParticles = 12;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(DustLight, DustDark);

            Burst(ps, count);
            Cone(ps, 20f, 0.04f);

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(Ramp(
                new[] { (Color.white, 0f), (Color.white, 1f) },
                new[] { (0.9f, 0f), (0.5f, 0.35f), (0f, 1f) }));

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.35f), new Keyframe(1f, 3.4f)));

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), material,
                              ParticleSystemRenderMode.Billboard);
            return ps;
        }

        // ── Per pellet ─────────────────────────────────────────────────────────
        //
        // Parented to the gun's ROOT rather than to the muzzle, because these are moved to wherever
        // a pellet landed — a hundred metres from the gun, in a direction the barrel is no longer
        // pointing.

        /// <summary>
        /// The pellets themselves, one stretched streak each.
        ///
        /// <para>
        /// No shape and no start speed: <see cref="PelletGunFx"/> hands every particle its own
        /// direction and a lifetime measured from the traced flight, so a streak dies exactly on
        /// the surface its pellet struck. Anything the shape module contributed here would be
        /// spread the trace did not agree to. A streak's drawn length is
        /// <c>size × lengthScale + speed × velocityScale</c>.
        /// </para>
        /// </summary>
        public static ParticleSystem BuildTracers(Transform parent, Material material,
                                                  float minSize, float maxSize,
                                                  float velocityScale, float lengthScale)
        {
            ParticleSystem ps = Manual(NewSystem(parent, "PelletTracers"));

            var main = ps.main;
            main.startLifetime = 1f;                 // overwritten per pellet
            main.startSpeed = 0f;                    // the emit carries the velocity
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.gravityModifier = 0f;               // hitscan: gravity is not the point
            main.maxParticles = 400;
            main.startColor = new ParticleSystem.MinMaxGradient(SparkHot, DustLight);

            var shape = ps.shape;
            shape.enabled = false;

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(Ramp(
                new[] { (SparkHot, 0f), (DustDark, 1f) },
                new[] { (1f, 0f), (0.85f, 0.6f), (0f, 1f) }));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            ConfigureRenderer(renderer, material, ParticleSystemRenderMode.Stretch);
            renderer.velocityScale = velocityScale;
            renderer.lengthScale = lengthScale;
            return ps;
        }

        /// <summary>Sparks struck off the surface a pellet hit.</summary>
        public static ParticleSystem BuildImpactSparks(Transform parent, Material material)
        {
            ParticleSystem ps = Manual(BuildSparks(parent, material, "ImpactSparks", count: 0,
                                                   cone: 55f));

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.32f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 11f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.03f);
            main.maxParticles = 400;
            return ps;
        }

        /// <summary>The puff punched out of whatever a pellet hit; tinted red on something alive.</summary>
        public static ParticleSystem BuildImpactDust(Transform parent, Material material)
        {
            ParticleSystem ps = Manual(BuildDust(parent, material, "ImpactDust", count: 0,
                                                 cone: 60f));

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.34f);
            main.maxParticles = 300;
            return ps;
        }

        /// <summary>Chips knocked loose, which bounce and settle where the shot landed.</summary>
        public static ParticleSystem BuildImpactDebris(Transform parent, Material material)
        {
            ParticleSystem ps = Manual(BuildGravel(parent, material, "ImpactDebris", count: 0,
                                                   minSpeed: 2f, maxSpeed: 7f, cone: 45f));

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.024f);
            main.maxParticles = 300;
            return ps;
        }

        /// <summary>
        /// Turn a built system into one that is EMITTED INTO rather than played.
        ///
        /// <para>
        /// Three things have to be true at once for that: it must be playing (a stopped system
        /// never simulates the particles handed to it), it must not emit on its own (an authored
        /// burst on a looping system goes off at the gun the moment it is equipped), and it must
        /// keep simulating while the emitter is off screen — the gun is in the player's hands and
        /// the impacts are far away, so the emitter's own visibility says nothing about theirs.
        /// </para>
        /// <para>
        /// Local scaling as well: these hang off a prefab that <see cref="ItemGrip"/> rescales to
        /// fit the hand, and a hit on a distant wall must not be drawn at the size of the gun.
        /// </para>
        /// </summary>
        private static ParticleSystem Manual(ParticleSystem ps)
        {
            var main = ps.main;
            main.loop = true;
            main.duration = 5f;
            main.playOnAwake = true;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.scalingMode = ParticleSystemScalingMode.Local;

            var emission = ps.emission;
            emission.enabled = false;
            emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
            return ps;
        }

        private static ParticleSystem NewSystem(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            return ps;
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

        private static void ConfigureRenderer(ParticleSystemRenderer renderer, Material material,
                                              ParticleSystemRenderMode mode)
        {
            renderer.sharedMaterial = material;
            renderer.renderMode = mode;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.sortMode = ParticleSystemSortMode.None;
        }

        private static Gradient Ramp((Color colour, float time)[] colours,
                                     (float alpha, float time)[] alphas)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                colours.Select(c => new GradientColorKey(c.colour, c.time)).ToArray(),
                alphas.Select(a => new GradientAlphaKey(a.alpha, a.time)).ToArray());
            return gradient;
        }

        /// <summary>Opaque lit rock for the cube-mesh chunks — the one material this build owns.</summary>
        private static Material EnsureDebrisMaterial(string logTag)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null)
            {
                Debug.LogError($"[{logTag}] URP Simple Lit shader not found.");
                return null;
            }

            Directory.CreateDirectory(MaterialDir);

            var material = AssetDatabase.LoadAssetAtPath<Material>(DebrisMatPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, DebrisMatPath);
            }

            material.shader = shader;
            material.SetColor("_BaseColor", Gravel);
            material.SetFloat("_Smoothness", 0.05f);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
