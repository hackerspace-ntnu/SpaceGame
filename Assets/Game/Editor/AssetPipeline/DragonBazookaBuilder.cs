using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Builds the Dragon Bazooka artifact: its launcher prefab, the two rocket prefabs it fires,
    /// its <see cref="InventoryItem"/> asset, and its entry in the network prefab list.
    ///
    /// <para>
    /// A script rather than hand-authored YAML because the prefabs nest imported FBXs, and the
    /// file ids Unity assigns inside a model are decided at import time — a hand-written prefab
    /// referencing guessed ids loads with a missing model and no error.
    /// </para>
    /// <para>
    /// <b>Only the launcher is registered as a network prefab.</b> The rockets must not be: every
    /// machine instantiates its own copy locally from a shared seed, and only what
    /// <c>GameServices.World.Spawn</c> is handed belongs in that list. Registering a projectile
    /// there does not fail loudly — it just adds a spawn nobody asked for.
    /// </para>
    /// <para>
    /// Build order matters and is not alphabetical: the whelp exists before the hero rocket,
    /// because the hero holds a reference to it, and both exist before the launcher.
    /// </para>
    /// <para>
    /// Re-runnable, and re-running REPLACES the prefabs wholesale. Tuning belongs in the numbers
    /// below, not in the Inspector.
    /// </para>
    /// </summary>
    public static class DragonBazookaBuilder
    {
        private const string LauncherModel = "Assets/Game/Art/Models/Items/dragon_bazooka.fbx";
        private const string RocketModel   = "Assets/Game/Art/Models/Items/dragon_rocket.fbx";
        private const string WhelpModel    = "Assets/Game/Art/Models/Items/dragon_rocket_whelp.fbx";

        private const string PrefabDir     = "Assets/Game/Prefabs/Items/Artifacts/Gadgets";
        private const string LauncherPath  = PrefabDir + "/DragonBazooka.prefab";
        private const string RocketPath    = PrefabDir + "/DragonRocket.prefab";
        private const string WhelpPath     = PrefabDir + "/DragonRocketWhelp.prefab";

        private const string ItemPath = "Assets/Game/Resources/Items/Artifacts/DragonBazooka.asset";

        private const string MaterialDir = "Assets/Game/Art/Materials/Weapons";
        private const string SmokeMatPath = MaterialDir + "/LaserSmoke.mat";
        private const string SparkMatPath = MaterialDir + "/LaserSpark.mat";
        private const string RingMatPath =
            "Assets/Game/Art/Materials/Artifacts/RepulsorBlastRing.mat";
        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

        // Festival reds and golds, matching the lacquer and gold-leaf palette entries the model
        // is finished in. The trail is the item's signature, so it is a saturated red rather than
        // a realistic grey-brown smoke — this is a firework, not ordnance.
        private static readonly Color SmokeHot   = new Color(1.00f, 0.32f, 0.22f);
        private static readonly Color SmokeDeep  = new Color(0.62f, 0.06f, 0.05f);
        private static readonly Color FlameCore  = new Color(1.00f, 0.86f, 0.45f);
        private static readonly Color FlameEdge  = new Color(1.00f, 0.38f, 0.10f);
        private static readonly Color SparkGold  = new Color(1.00f, 0.80f, 0.35f);

        [MenuItem("Tools/Build Dragon Bazooka Artifact")]
        public static void Build()
        {
            var smokeMat = AssetDatabase.LoadAssetAtPath<Material>(SmokeMatPath);
            var sparkMat = AssetDatabase.LoadAssetAtPath<Material>(SparkMatPath);
            var ringMat = AssetDatabase.LoadAssetAtPath<Material>(RingMatPath);
            if (smokeMat == null || sparkMat == null || ringMat == null)
            {
                // The smoke and spark materials belong to the laser staff's build and the ring to
                // the repulsor's; all three are reused rather than duplicated.
                Debug.LogError("[DragonBazooka] Missing a shared material. Run Tools/Build Laser " +
                               "Staff Artifact and Tools/Build Repulsor Gauntlet Artifact first.");
                return;
            }

            // Whelp before hero before launcher: each holds a reference to the one before it.
            // Model and effect scales are separate numbers, because the two rounds come from
            // two DIFFERENT FBXs — the whelp mesh is already half-size on disk, so scaling its
            // model by the same factor as its effects would shrink it twice.
            DragonRocket whelp = BuildRocket(WhelpPath, WhelpModel, "DragonRocketWhelp",
                                             smokeMat, sparkMat, ringMat, whelpPrefab: null,
                                             modelScale: 1.1f, effectScale: 0.6f);
            if (whelp == null) return;

            DragonRocket rocket = BuildRocket(RocketPath, RocketModel, "DragonRocket",
                                              smokeMat, sparkMat, ringMat, whelpPrefab: whelp,
                                              modelScale: 1.95f, effectScale: 1.95f);
            if (rocket == null) return;

            GameObject launcher = BuildLauncher(rocket, smokeMat, sparkMat);
            if (launcher == null) return;

            InventoryItem item = EnsureItem(launcher);
            WireItemIntoPickup(launcher, item);
            RegisterNetworkPrefab(launcher);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[DragonBazooka] Built {LauncherPath}, {RocketPath}, {WhelpPath} and " +
                      $"{ItemPath}. Run Tools/Generate All Item Icons for the inventory icon.");
        }

        // ── The rockets ────────────────────────────────────────────────────────

        /// <summary>
        /// One rocket prefab. No NetworkObject, no Rigidbody, no collider: it is not spawned, not
        /// picked up, and moves itself along an analytic path rather than being pushed by physics.
        /// </summary>
        private static DragonRocket BuildRocket(string path, string modelPath, string name,
                                                Material smokeMat, Material sparkMat,
                                                Material ringMat, DragonRocket whelpPrefab,
                                                float modelScale, float effectScale)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogError($"[DragonBazooka] No model at {modelPath}. " +
                               "Run models/gear/dragon_bazooka_export.py first.");
                return null;
            }

            var root = new GameObject(name);

            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.name = "Model";
            modelInstance.transform.SetParent(root.transform, false);
            // Blender FBXs land under a -90 degree X rotation, so the rocket's own long axis is
            // mesh-local Z but prefab-space Y. Turning the MODEL rather than the root is what lets
            // DragonRocket point the root down its heading with a plain LookRotation.
            modelInstance.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            modelInstance.transform.localScale = Vector3.one * modelScale;

            // The exhaust end. The model's origin is already the nozzle face (see the component's
            // docstring), so the trail hangs off the back by construction.
            var exhaust = new GameObject("Exhaust");
            exhaust.transform.SetParent(root.transform, false);
            exhaust.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            ParticleSystem trail = BuildTrail(exhaust.transform, smokeMat, effectScale);
            ParticleSystem flame = BuildFlame(exhaust.transform, sparkMat, effectScale);
            ParticleSystem embers = BuildEmbers(exhaust.transform, sparkMat, effectScale);
            ParticleSystem halo = BuildHalo(root.transform, smokeMat, effectScale);
            ParticleSystem burst = BuildBurst(root.transform, smokeMat, sparkMat, effectScale);

            var glowObject = new GameObject("Glow");
            glowObject.transform.SetParent(exhaust.transform, false);
            Light glow = glowObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = FlameEdge;
            glow.range = 9f * effectScale;
            glow.intensity = 6f;
            glow.shadows = LightShadows.None;

            var rocket = root.AddComponent<DragonRocket>();
            SetPrivate(rocket, "trail", trail);
            SetPrivate(rocket, "flame", flame);
            SetPrivate(rocket, "embers", embers);
            SetPrivate(rocket, "halo", halo);
            SetPrivate(rocket, "burst", burst);
            SetPrivate(rocket, "glow", glow);
            SetPrivate(rocket, "ringMaterial", ringMat);
            SetPrivate(rocket, "whelpPrefab", whelpPrefab);

            // A whelp is smaller, faster, wilder and far weaker than its parent, and it must not
            // burst again — four whelps each making four is sixteen, and the frame after that
            // is sixty-four.
            if (whelpPrefab == null)
            {
                // Faster and tighter than its parent, on purpose. The hero is a slow drifting
                // spectacle you watch; four whelps doing the same thing at once would be four
                // more things to track and the burst would read as mush.
                SetPrivate(rocket, "speed", 19f);
                SetPrivate(rocket, "wanderAmplitude", 2.0f);
                SetPrivate(rocket, "driftRate", 1.6f);
                SetPrivate(rocket, "settleSeconds", 0.1f);
                SetPrivate(rocket, "wanderFrequency", 2.6f);
                SetPrivate(rocket, "lifetime", 1.8f);
                SetPrivate(rocket, "hitRadius", 0.25f);
                SetPrivate(rocket, "blastRadius", 3.4f);
                SetPrivate(rocket, "blastDamage", 16);
                SetPrivate(rocket, "flingSpeed", 12f);
                SetPrivate(rocket, "whelpCount", 0);
                SetPrivate(rocket, "maxGenerations", 0);
            }

            SetPrivateLayerMask(rocket, "hitMask", ~0);

            Directory.CreateDirectory(PrefabDir);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);

            if (prefab == null)
            {
                Debug.LogError($"[DragonBazooka] Saving {path} failed.");
                return null;
            }

            return prefab.GetComponent<DragonRocket>();
        }

        // ── The launcher ───────────────────────────────────────────────────────

        private static GameObject BuildLauncher(DragonRocket rocket, Material smokeMat,
                                                Material sparkMat)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(LauncherModel);
            if (model == null)
            {
                Debug.LogError($"[DragonBazooka] No model at {LauncherModel}. " +
                               "Run models/gear/dragon_bazooka_export.py first.");
                return null;
            }

            var root = new GameObject("DragonBazooka");

            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.name = "Model";
            modelInstance.transform.SetParent(root.transform, false);

            // The markers exported with the mesh exist only to carry coordinates across the FBX;
            // turned into plain transforms they become the muzzle, the grip and the venturi.
            Transform muzzle = AdoptMarker(root.transform, modelInstance.transform,
                                           "Marker_Muzzle", "Muzzle");
            Transform grip = AdoptMarker(root.transform, modelInstance.transform,
                                          "Marker_Grip", "GripPoint");
            Transform breech = AdoptMarker(root.transform, modelInstance.transform,
                                            "Marker_Breech", "Breech");

            // Aim the muzzle down the bore. Both markers sit on the barrel axis, so breech to
            // muzzle IS the firing direction — measured rather than typed, because the FBX import
            // rotation is exactly the kind of constant that goes silently wrong.
            Vector3 fireDir = (muzzle.localPosition - breech.localPosition).normalized;
            muzzle.localRotation = Quaternion.LookRotation(fireDir);
            breech.localRotation = Quaternion.LookRotation(-fireDir);

            // The dragon's lower jaw stays a live transform — it is the one part that moves, and
            // the FBX carries its hinge in its own object pivot so a local X rotation is the whole
            // roar. Found by name rather than adopted, because unlike a marker it is real geometry.
            Transform jaw = FindDeep(modelInstance.transform, "Mesh_DragonJaw_Roaring");
            if (jaw == null)
                Debug.LogWarning("[DragonBazooka] No jaw in the FBX; the launcher will not roar.");

            ParticleSystem muzzleFire = BuildMuzzleFire(muzzle, sparkMat);
            ParticleSystem muzzleSmoke = BuildMuzzleSmoke(muzzle, smokeMat);
            ParticleSystem backblast = BuildBackblast(breech, smokeMat, sparkMat);

            var flashObject = new GameObject("MuzzleFlash");
            flashObject.transform.SetParent(muzzle, false);
            Light flash = flashObject.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = FlameCore;
            flash.range = 9f;
            flash.intensity = 10f;
            flash.shadows = LightShadows.None;
            flash.enabled = false;

            // ── Pickup / world presence ──
            // Mirrors LightningSpell.prefab component for component: the same prefab is both the
            // thing in your hand and the thing lying in the sand.
            var netObject = root.AddComponent<NetworkObject>();
            netObject.SynchronizeTransform = true;

            AddInternal(root, "SpaceGame.Items.PickupableItem");

            // The body, a collider the shape of the bazooka, the sizing and the netcode that lets
            // another machine watch it be shoved about. One shared block — this used to be a
            // hand-written sphere of radius 0.18, which is a marble, and it rolled like one.
            ItemWorldPresence.Apply(root);

            root.AddComponent<SpaceGame.Core.NetRelay>();
            root.AddComponent<SpaceGame.Core.Persistence.SaveableEntity>();
            root.AddComponent<SpaceGame.Core.Persistence.TransformSaveable>();

            // ── Grip ──
            // Zero offsets, like the gravel blaster: the same Blender front (-Y) and export flags
            // land the same orientation in the hand. holdSize past the longarm bracket because
            // this one genuinely is a shoulder weapon and the dragon head has to stay readable.
            var itemGrip = root.AddComponent<ItemGrip>();
            SetPrivate(itemGrip, "gripPoint", grip);
            SetPrivate(itemGrip, "holdSize", 1.25f);
            SetPrivate(itemGrip, "sizeReference", modelInstance.transform);

            // ── The artifact ──
            var artifact = root.AddComponent<DragonBazookaArtifact>();
            SetPrivate(artifact, "rocketPrefab", rocket);
            SetPrivate(artifact, "muzzle", muzzle);
            SetPrivate(artifact, "jaw", jaw);
            SetPrivate(artifact, "muzzleFire", muzzleFire);
            SetPrivate(artifact, "muzzleSmoke", muzzleSmoke);
            SetPrivate(artifact, "backblast", backblast);
            SetPrivate(artifact, "muzzleFlash", flash);
            SetPrivateEnum(artifact, "useSoundId", "WeaponProjectileWhoosh");

            // Five rockets and no refill. Serialized on UsableItem, so it is set the way the
            // Inspector would: the field is private on the base class.
            SetPrivate(artifact, "maxUses", 5);

            Directory.CreateDirectory(PrefabDir);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, LauncherPath);
            UnityEngine.Object.DestroyImmediate(root);

            if (prefab == null) Debug.LogError("[DragonBazooka] Launcher prefab save failed.");
            return prefab;
        }

        // ── The emitters ───────────────────────────────────────────────────────

        /// <summary>
        /// The red smoke the rocket leaves in the sky. Emitted over DISTANCE rather than over
        /// time, so a swerve lays down a dense curve and a straight run does not — which is what
        /// draws the flight path as a ribbon instead of a dotted line, and the whole reason the
        /// trail is worth having.
        /// </summary>
        private static ParticleSystem BuildTrail(Transform parent, Material material, float scale)
        {
            ParticleSystem ps = NewSystem(parent, "Trail", loop: true);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 6.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.42f * scale, 0.95f * scale);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.06f, -0.02f);
            main.maxParticles = 1400;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(SmokeHot, SmokeDeep);

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 55f;
            emission.rateOverDistance = 34f / Mathf.Max(scale, 0.1f);

            Sphere(ps, 0.09f * scale);

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(Ramp(
                new[] { (SmokeHot, 0f), (SmokeDeep, 0.45f), (SmokeDeep, 1f) },
                new[] { (0f, 0f), (0.85f, 0.08f), (0.55f, 0.5f), (0f, 1f) }));

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.35f), new Keyframe(0.4f, 1f), new Keyframe(1f, 1.5f)));

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), material,
                              ParticleSystemRenderMode.Billboard);
            return ps;
        }

        /// <summary>Fire wrapped round the rocket, streaming off the nozzle.</summary>
        private static ParticleSystem BuildFlame(Transform parent, Material material, float scale)
        {
            ParticleSystem ps = NewSystem(parent, "Flame", loop: true);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.38f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f * scale, 7f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f * scale, 0.38f * scale);
            main.maxParticles = 420;
            main.startColor = new ParticleSystem.MinMaxGradient(FlameCore, FlameEdge);

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 190f;

            Cone(ps, 19f, 0.05f * scale);

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(Ramp(
                new[] { (FlameCore, 0f), (FlameEdge, 0.55f), (SmokeDeep, 1f) },
                new[] { (1f, 0f), (0.9f, 0.4f), (0f, 1f) }));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            ConfigureRenderer(renderer, material, ParticleSystemRenderMode.Stretch);
            renderer.velocityScale = 0.03f;
            renderer.lengthScale = 2.2f;
            return ps;
        }

        /// <summary>
        /// Gold embers shed continuously along the flight, falling under gravity.
        ///
        /// The single detail that separates a firework from an exhaust plume: a plume streams
        /// backward and dies, embers keep falling out of the sky after the rocket has swerved
        /// away. Emitted over DISTANCE as well as time, so a hard swerve throws a denser shower
        /// than a straight run — which is what draws the corner rather than just the path.
        /// </summary>
        private static ParticleSystem BuildEmbers(Transform parent, Material material, float scale)
        {
            ParticleSystem ps = NewSystem(parent, "Embers", loop: true);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 2.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * scale, 7f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f * scale, 0.11f * scale);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
            main.maxParticles = 700;
            main.startColor = new ParticleSystem.MinMaxGradient(SparkGold, FlameEdge);

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 90f;
            emission.rateOverDistance = 14f / Mathf.Max(scale, 0.1f);

            Cone(ps, 42f, 0.05f * scale);

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(Ramp(
                new[] { (FlameCore, 0f), (SparkGold, 0.35f), (FlameEdge, 1f) },
                new[] { (1f, 0f), (1f, 0.5f), (0f, 1f) }));

            // Twinkle: embers on a firework wink rather than fade evenly, and the flicker is what
            // stops several hundred identical dots reading as a dust cloud.
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.35f, 0.45f), new Keyframe(0.6f, 0.95f),
                new Keyframe(1f, 0.1f)));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            ConfigureRenderer(renderer, material, ParticleSystemRenderMode.Stretch);
            renderer.velocityScale = 0.025f;
            renderer.lengthScale = 2.6f;
            return ps;
        }

        /// <summary>
        /// A soft glow travelling with the rocket, under the flame.
        ///
        /// Sits on the rocket itself rather than the exhaust, and in LOCAL space unlike everything
        /// else here — it is the one effect that is supposed to stay stuck to the body, so a slow
        /// heavy rocket reads as lit from within rather than as a model with sparks behind it.
        /// </summary>
        private static ParticleSystem BuildHalo(Transform parent, Material material, float scale)
        {
            ParticleSystem ps = NewSystem(parent, "Halo", loop: true);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.6f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f * scale, 1.0f * scale);
            main.maxParticles = 90;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(FlameEdge, SmokeHot);

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 45f;

            Sphere(ps, 0.10f * scale);

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(Ramp(
                new[] { (FlameCore, 0f), (FlameEdge, 1f) },
                new[] { (0f, 0f), (0.45f, 0.3f), (0f, 1f) }));

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), material,
                              ParticleSystemRenderMode.Billboard);
            return ps;
        }

        /// <summary>
        /// The burst. One parent system with two children, so <c>Play(withChildren: true)</c>
        /// fires the whole thing and the artifact holds one reference.
        /// </summary>
        private static ParticleSystem BuildBurst(Transform parent, Material smokeMat,
                                                 Material sparkMat, float scale)
        {
            ParticleSystem ps = NewSystem(parent, "Burst", loop: false);

            var main = ps.main;
            main.duration = 1.6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f * scale, 16f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f * scale, 1.5f * scale);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.08f, 0.02f);
            main.maxParticles = 260;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(SmokeHot, SmokeDeep);

            Burst(ps, (short)(110 * scale + 30));
            Sphere(ps, 0.34f * scale);

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(Ramp(
                new[] { (FlameCore, 0f), (SmokeHot, 0.25f), (SmokeDeep, 1f) },
                new[] { (1f, 0f), (0.9f, 0.3f), (0f, 1f) }));

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), smokeMat,
                              ParticleSystemRenderMode.Billboard);

            BuildSparks(ps.transform, sparkMat, "BurstSparks", (short)(90 * scale + 30),
                        spread: 180f, speed: 22f * scale);
            return ps;
        }

        private static ParticleSystem BuildMuzzleFire(Transform muzzle, Material material)
        {
            ParticleSystem ps = NewSystem(muzzle, "MuzzleFire", loop: false);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.34f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(9f, 20f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.4f);
            main.maxParticles = 160;
            main.startColor = new ParticleSystem.MinMaxGradient(FlameCore, FlameEdge);

            Burst(ps, 70);
            Cone(ps, 13f, 0.05f);

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(Ramp(
                new[] { (FlameCore, 0f), (FlameEdge, 0.5f), (SmokeDeep, 1f) },
                new[] { (1f, 0f), (0.85f, 0.45f), (0f, 1f) }));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            ConfigureRenderer(renderer, material, ParticleSystemRenderMode.Stretch);
            renderer.velocityScale = 0.02f;
            renderer.lengthScale = 2f;

            BuildSparks(ps.transform, material, "MuzzleSparks", 40, spread: 22f, speed: 16f);
            return ps;
        }

        private static ParticleSystem BuildMuzzleSmoke(Transform muzzle, Material material)
        {
            ParticleSystem ps = NewSystem(muzzle, "MuzzleSmoke", loop: false);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 2.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.06f, 0.01f);
            main.maxParticles = 120;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(SmokeHot, SmokeDeep);

            Burst(ps, 34);
            Cone(ps, 26f, 0.07f);

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(Ramp(
                new[] { (SmokeHot, 0f), (SmokeDeep, 1f) },
                new[] { (0f, 0f), (0.8f, 0.12f), (0f, 1f) }));

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.4f), new Keyframe(1f, 1.6f)));

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), material,
                              ParticleSystemRenderMode.Billboard);
            return ps;
        }

        /// <summary>
        /// Backblast out of the venturi. Wider, slower and dirtier than the muzzle: it goes off
        /// behind the shooter, and it has to read as the tube venting rather than as a second
        /// shot.
        /// </summary>
        private static ParticleSystem BuildBackblast(Transform breech, Material smokeMat,
                                                     Material sparkMat)
        {
            ParticleSystem ps = NewSystem(breech, "Backblast", loop: false);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 15f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.95f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.05f, 0.02f);
            main.maxParticles = 160;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(SmokeDeep, SmokeHot);

            Burst(ps, 46);
            Cone(ps, 42f, 0.09f);

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(Ramp(
                new[] { (SmokeHot, 0f), (SmokeDeep, 1f) },
                new[] { (0f, 0f), (0.7f, 0.15f), (0f, 1f) }));

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), smokeMat,
                              ParticleSystemRenderMode.Billboard);

            BuildSparks(ps.transform, sparkMat, "BackblastSparks", 50, spread: 48f, speed: 14f);
            return ps;
        }

        private static ParticleSystem BuildSparks(Transform parent, Material material, string name,
                                                  short count, float spread, float speed)
        {
            ParticleSystem ps = NewSystem(parent, name, loop: false);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.45f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
            main.maxParticles = 200;
            main.startColor = new ParticleSystem.MinMaxGradient(SparkGold, FlameEdge);

            Burst(ps, count);
            Cone(ps, spread, 0.03f);

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(Ramp(
                new[] { (SparkGold, 0f), (FlameEdge, 0.5f), (FlameEdge, 1f) },
                new[] { (1f, 0f), (1f, 0.55f), (0f, 1f) }));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            ConfigureRenderer(renderer, material, ParticleSystemRenderMode.Stretch);
            renderer.velocityScale = 0.02f;
            renderer.lengthScale = 2.4f;
            return ps;
        }

        // ── Particle plumbing ──────────────────────────────────────────────────

        private static ParticleSystem NewSystem(Transform parent, string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = loop ? 4f : 1.4f;
            main.loop = loop;
            main.playOnAwake = false;
            // World space, so smoke already laid down stays where it was laid rather than
            // dragging along behind a rocket that is still swerving. On the trail this is the
            // difference between a ribbon in the sky and a comet stuck to the nose.
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

        private static void Sphere(ParticleSystem ps, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
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

        // ── Markers ────────────────────────────────────────────────────────────

        /// <summary>
        /// Turn an exported marker cube into a plain transform on the prefab root. The 4 mm mesh
        /// exists only to carry a coordinate across the FBX; leaving its renderer would float a
        /// cube in the model.
        /// </summary>
        private static Transform AdoptMarker(Transform root, Transform model, string markerName,
                                             string wantedName)
        {
            var adopted = new GameObject(wantedName);
            adopted.transform.SetParent(root, false);

            Transform marker = FindDeep(model, markerName);
            if (marker == null)
            {
                Debug.LogWarning($"[DragonBazooka] No {markerName} in the FBX; " +
                                 $"{wantedName} left at origin.");
                return adopted.transform;
            }

            adopted.transform.localPosition = root.InverseTransformPoint(marker.position);
            marker.gameObject.SetActive(false);
            return adopted.transform;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        // ── Item asset, pickup, network registration ───────────────────────────

        private static InventoryItem EnsureItem(GameObject prefab)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ItemPath) ?? ".");

            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<InventoryItem>();
                AssetDatabase.CreateAsset(item, ItemPath);
            }

            item.itemName = "Dragon Bazooka";
            item.itemPrefab = prefab;
            EditorUtility.SetDirty(item);
            return item;
        }

        /// <summary>
        /// The item asset references the saved prefab and the prefab references the item, so one
        /// of the two links can only be made once both files exist.
        /// </summary>
        private static void WireItemIntoPickup(GameObject prefab, InventoryItem item)
        {
            Component pickup = prefab.GetComponents<Component>()
                .FirstOrDefault(c => c != null &&
                                     c.GetType().FullName == "SpaceGame.Items.PickupableItem");
            if (pickup == null)
            {
                Debug.LogError("[DragonBazooka] PickupableItem missing.");
                return;
            }

            var so = new SerializedObject(pickup);
            so.FindProperty("item").objectReferenceValue = item;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SavePrefabAsset(prefab);
        }

        /// <summary>
        /// The list NetworkManager actually reads — NOT Assets/DefaultNetworkPrefabs.asset, which
        /// regenerates itself. An unregistered item prefab fails on CLIENTS ONLY, so solo
        /// playtesting cannot find the mistake.
        ///
        /// The launcher only. See the class summary for why the rockets stay out.
        /// </summary>
        private static void RegisterNetworkPrefab(GameObject prefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (list == null)
            {
                Debug.LogError($"[DragonBazooka] No list at {NetworkPrefabsPath}.");
                return;
            }

            if (list.Contains(prefab)) return;

            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
        }

        // ── Reflection helpers ─────────────────────────────────────────────────
        //
        // Item components serialize private fields, which is right for runtime code and simply
        // means an editor script goes in the way the Inspector does. PickupableItem is
        // additionally internal to Assembly-CSharp, so it cannot be named from here at all.

        private static void AddInternal(GameObject go, string typeName)
        {
            Type type = typeof(ItemGrip).Assembly.GetType(typeName);
            if (type == null) { Debug.LogError($"[DragonBazooka] No type {typeName}."); return; }
            go.AddComponent(type);
        }

        private static FieldInfo Field(object target, string name)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo info = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (info != null) return info;
            }

            Debug.LogError($"[DragonBazooka] No field '{name}' on {target.GetType().Name}.");
            return null;
        }

        private static void SetPrivate(object target, string name, object value) =>
            Field(target, name)?.SetValue(target, value);

        private static void SetPrivateLayerMask(object target, string name, int mask) =>
            Field(target, name)?.SetValue(target, (LayerMask)mask);

        private static void SetPrivateEnum(object target, string name, string enumValue)
        {
            FieldInfo field = Field(target, name);
            if (field == null) return;

            try { field.SetValue(target, Enum.Parse(field.FieldType, enumValue)); }
            catch (ArgumentException)
            {
                Debug.LogWarning($"[DragonBazooka] '{enumValue}' is not a {field.FieldType.Name}; " +
                                 "left at default.");
            }
        }
    }
}
