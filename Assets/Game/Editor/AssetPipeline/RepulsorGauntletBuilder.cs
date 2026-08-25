using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FirstGearGames.SmoothCameraShaker;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Builds the Repulsor Gauntlet artifact: its blast-ring and air-warp materials, its two shake
    /// assets, its prefab — greybox model, ammo capacitor and the whole blast VFX rig — its
    /// <see cref="InventoryItem"/> asset, its entry in the network prefab list, and the
    /// <see cref="FlungBody"/> landing on the player prefab.
    ///
    /// <para>
    /// The model is a GREYBOX built from primitives — the real mesh is a deferred follow-up, and
    /// authoring the prefab now means the whole use flow can be proven before any art exists.
    /// </para>
    /// <para>
    /// It is re-runnable, and re-running it REPLACES the prefab wholesale. Anything hand-added in
    /// the inspector afterwards is destroyed by the next run, so tuning belongs in the numbers
    /// below rather than in the scene.
    /// </para>
    /// <para>
    /// <b>The shipped prefab is STALE and this builder has to be run.</b> See <see cref="Build"/>.
    /// </para>
    /// </summary>
    public static class RepulsorGauntletBuilder
    {
        private const string PrefabPath  = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/RepulsorGauntlet.prefab";
        private const string ItemPath    = "Assets/Game/Resources/Items/Artifacts/RepulsorGauntlet.asset";
        private const string RingMatPath = "Assets/Game/Art/Materials/Artifacts/RepulsorBlastRing.mat";
        private const string GlowMatPath = "Assets/Game/Art/Materials/Artifacts/RepulsorChargeGlow.mat";
        private const string ConeMatPath = "Assets/Game/Art/Materials/Artifacts/RepulsorAirWarp.mat";
        private const string ShockwaveShaderPath = "Assets/Game/Art/Shaders/Artifacts/RepulsorShockwave.shader";
        private const string AirWarpShaderPath   = "Assets/Game/Art/Shaders/Artifacts/RepulsorAirWarp.shader";

        /// <summary>
        /// The particle materials the blast BORROWS from the weapon builds. Dust and sparks belong
        /// to the laser staff, the debris rock to the gravel blaster; one grit-and-smoke look
        /// across the artifacts is worth more than a fourth private copy of the same three shaders.
        /// </summary>
        private const string WeaponMaterialDir = "Assets/Game/Art/Materials/Weapons";
        private const string SmokeMatPath  = WeaponMaterialDir + "/LaserSmoke.mat";
        private const string SparkMatPath  = WeaponMaterialDir + "/LaserSpark.mat";
        private const string DebrisMatPath = WeaponMaterialDir + "/GravelDebris.mat";

        /// <summary>Capacitor tint. Alpha is the additive strength, not an opacity.</summary>
        private static readonly Color GlowColor = new Color(0.45f, 0.85f, 1f, 1f);

        /// <summary>
        /// A shake asset that does not exist yet starts as a copy of the shipped damage shake and is
        /// tuned in the Inspector afterwards. Both of these already exist and are authored — the copy
        /// is only the first-run path, and EnsureShake deliberately never overwrites a live asset.
        /// </summary>
        private const string ShakeSourcePath = "Assets/Game/ScriptableObjects/Shake/DamageShake.asset";
        private const string BlastShakePath  = "Assets/Game/ScriptableObjects/Shake/RepulsorBlastShake.asset";
        private const string FlungShakePath  = "Assets/Game/ScriptableObjects/Shake/RepulsorFlungShake.asset";

        /// <summary>
        /// The prefab this builder OPENS to reach the player. Its root is a nested instance of
        /// PlayerCharacter.prefab, so the component added below is added to that instance and
        /// <c>SaveAsPrefabAsset</c> writes it through to the base prefab; what stays here is the
        /// property override. See <see cref="WireFlungBodyIntoPlayer"/>.
        /// </summary>
        private const string PlayerPrefabPath =
            "Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab";
        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

        /// <summary>The ground layer DropItemPhysics settles against, shared by every artifact.</summary>
        private const int GroundLayerMask = 128;

        // ── Blast VFX tuning ───────────────────────────────────────────────────
        //
        // Read every number below against the artifact's own: a 20 m reach and a 100° cone, spent
        // in one instant. The particles are not the blast — the cone shell and the ground ring draw
        // that — they are its MASS, the thing that makes a wall of air visible on the way out. So
        // they are wide, fast and brief: a narrow slow puff would read as a nozzle venting, which
        // is the opposite of what a thundergun is.

        /// <summary>Where the blast leaves the gauntlet: metres in front of the cuff, along +Z.</summary>
        private const float EmitterForwardOffset = 0.16f;

        private const float DustCone = 45f;
        private const short DustCount = 55;
        private const float DustSpeedMin = 15f, DustSpeedMax = 22f;
        private const float DustLifeMin = 0.45f, DustLifeMax = 0.7f;
        private const float DustSizeMin = 0.35f, DustSizeMax = 0.9f;
        /// <summary>Negative: displaced air billows UP for the moment before it settles.</summary>
        private const float DustGravityMin = -0.12f, DustGravityMax = -0.02f;

        private const float StreakCone = 40f;
        private const short StreakCount = 70;
        private const float StreakSpeedMin = 34f, StreakSpeedMax = 45f;
        private const float StreakLifeMin = 0.18f, StreakLifeMax = 0.3f;
        private const float StreakSizeMin = 0.05f, StreakSizeMax = 0.14f;
        /// <summary>Stretch: the streak is the frame's motion, so it is length that carries speed.</summary>
        private const float StreakLengthScale = 4f, StreakVelocityScale = 0.12f;

        private const float DebrisCone = 30f;
        private const short DebrisCount = 35;
        private const float DebrisSpeedMin = 18f, DebrisSpeedMax = 25f;
        private const float DebrisLifeMin = 0.9f, DebrisLifeMax = 1.4f;
        private const float DebrisSizeMin = 0.02f, DebrisSizeMax = 0.06f;
        private const float DebrisGravityMin = 0.9f, DebrisGravityMax = 1.4f;

        /// <summary>Point light at the emitter, lit for the artifact's flashSeconds.</summary>
        private const float FlashRange = 9f, FlashIntensity = 14f;

        /// <summary>
        /// Rest scale of the capacitor sphere. The artifact overwrites this every frame from its own
        /// capacitorGlowScale — this is only what the prefab looks like sitting in the project view.
        /// </summary>
        private const float CapacitorRestScale = 0.14f;

        /// <summary>Compressed air and the desert it tears up.</summary>
        private static readonly Color DustLight  = new Color(0.78f, 0.72f, 0.60f);
        private static readonly Color DustDark   = new Color(0.46f, 0.42f, 0.35f);
        private static readonly Color StreakCore = new Color(0.88f, 0.95f, 1.00f);
        private static readonly Color StreakEdge = new Color(0.45f, 0.70f, 1.00f);
        private static readonly Color Grit       = new Color(0.42f, 0.36f, 0.29f);
        /// <summary>Matches the air-warp shader's authored rim colour, so flash and cone agree.</summary>
        private static readonly Color FlashCool  = new Color(0.62f, 0.82f, 1.00f);

        /// <summary>
        /// Build the gauntlet.
        ///
        /// <para>
        /// <b>This has to be RUN after the instant-fire rewrite.</b> The prefab currently on disk was
        /// saved by an older run and pins <c>blastAngle</c>, <c>upwardTilt</c>, <c>recoilSpeed</c>,
        /// <c>leapHeight</c> and <c>blastFovKick</c> to the charge-era values, which OVERRIDE the new
        /// defaults in <see cref="RepulsorGauntletArtifact"/> — a serialized prefab value always
        /// wins over a field initialiser. It also still carries the pre-rename <c>chargeGlow</c> and
        /// has no cone material, particle systems or muzzle flash at all. A re-run replaces the
        /// prefab wholesale and is the only thing that clears all of it.
        /// </para>
        /// </summary>
        [MenuItem("Tools/SpaceGame/Items/Build Repulsor Gauntlet")]
        public static void Build()
        {
            Material ringMat = EnsureRingMaterial();
            Material glowMat = EnsureGlowMaterial();
            Material coneMat = EnsureAirWarpMaterial();
            BlastMaterials blastMats = LoadBlastMaterials();
            if (!blastMats.Complete) return;

            ShakeData blastShake = EnsureShake(BlastShakePath);
            if (blastShake == null) return;

            GameObject root = BuildHierarchy(ringMat, glowMat, coneMat, blastMats, blastShake);

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath) ?? ".");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null) { Debug.LogError("[RepulsorGauntlet] Prefab save failed."); return; }

            InventoryItem item = EnsureItem(prefab);
            WireItemIntoPickup(prefab, item);
            RegisterNetworkPrefab(prefab);
            WireFlungBodyIntoPlayer();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[RepulsorGauntlet] Built {PrefabPath} and {ItemPath}. " +
                      "Run Tools/Generate All Item Icons for the inventory icon.");
        }

        // ── Hierarchy ──────────────────────────────────────────────────────────

        private static GameObject BuildHierarchy(Material ringMat, Material glowMat, Material coneMat,
                                                 BlastMaterials blastMats, ShakeData blastShake)
        {
            var root = new GameObject("RepulsorGauntlet");

            // ── Greybox model ──
            // Primitives, not an FBX: the real gauntlet mesh is a deferred follow-up, and the
            // builder simply re-runs over this slot when it lands. A cylinder cuff the forearm
            // slides through, and a flat plate over the back of the hand. The colliders the
            // primitives arrive with would fight the root's own pickup sphere.
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);

            GameObject cuff = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cuff.name = "Cuff";
            cuff.transform.SetParent(model.transform, false);
            cuff.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // Long axis along +Z.
            cuff.transform.localScale = new Vector3(0.09f, 0.11f, 0.09f);
            UnityEngine.Object.DestroyImmediate(cuff.GetComponent<Collider>());

            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Plate";
            plate.transform.SetParent(model.transform, false);
            plate.transform.localPosition = new Vector3(0f, 0.02f, 0.13f);
            plate.transform.localScale = new Vector3(0.1f, 0.04f, 0.08f);
            UnityEngine.Object.DestroyImmediate(plate.GetComponent<Collider>());

            // ── Grip ──
            var grip = new GameObject("GripPoint");
            grip.transform.SetParent(root.transform, false);
            grip.transform.localPosition = new Vector3(0f, 0f, -0.05f);

            // ── Ammo capacitor ──
            // Lit while a shot is loaded, dark while it recharges — the gauntlet's whole magazine
            // readout, driven by the artifact's UpdateCapacitor. Built inactive; the artifact turns
            // it on from Awake, so a gauntlet lying in the sand already shows a full magazine.
            //
            // Its own flat additive material, NOT the ring's. The shockwave shader reads uv.y as
            // "across the annulus width" and sweeps it with _Progress; on a sphere that coordinate
            // is latitude and nothing animates _Progress, so the ring material renders the glow
            // bright at one pole and invisible at the other.
            GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glow.name = "Capacitor";
            glow.transform.SetParent(root.transform, false);
            glow.transform.localPosition = new Vector3(0f, 0f, 0.12f);
            glow.transform.localScale = Vector3.one * CapacitorRestScale;
            UnityEngine.Object.DestroyImmediate(glow.GetComponent<Collider>());
            glow.GetComponent<MeshRenderer>().sharedMaterial = glowMat;
            glow.SetActive(false);

            // ── Blast emitter ──
            // One transform carrying the muzzle position, with the three bursts and the flash at
            // zero offset under it. That split is load-bearing: the artifact's PlayBurst writes each
            // system's world ROTATION every shot and never its position, so a system that carried an
            // offset of its own would swing around the hand as the player turned.
            var emitter = new GameObject("BlastEmitter");
            emitter.transform.SetParent(root.transform, false);
            emitter.transform.localPosition = new Vector3(0f, 0f, EmitterForwardOffset);

            ParticleSystem dust = BuildBlastDust(emitter.transform, blastMats.Dust);
            ParticleSystem streaks = BuildBlastStreaks(emitter.transform, blastMats.Streaks);
            ParticleSystem debris = BuildBlastDebris(emitter.transform, blastMats.Debris);
            Light flash = BuildMuzzleFlash(emitter.transform);

            // ── Pickup / world presence ──
            // Mirrors LightningSpell.prefab component for component: the same prefab is both the
            // thing in your hand and the thing lying in the sand.
            var netObject = root.AddComponent<NetworkObject>();
            netObject.SynchronizeTransform = true;

            SphereCollider sphere = root.AddComponent<SphereCollider>();
            sphere.radius = 0.14f;

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = true;

            AddInternal(root, "SpaceGame.Items.PickupableItem");

            var drop = root.AddComponent<DropItemPhysics>();
            SetPrivate(drop, "rb", body);
            SetPrivateLayerMask(drop, "groundLayer", GroundLayerMask);

            root.AddComponent<SpaceGame.Core.NetRelay>();
            root.AddComponent<SpaceGame.Core.Persistence.SaveableEntity>();
            root.AddComponent<SpaceGame.Core.Persistence.TransformSaveable>();

            var itemGrip = root.AddComponent<ItemGrip>();
            SetPrivate(itemGrip, "gripPoint", grip.transform);
            SetPrivate(itemGrip, "holdSize", 0.3f);
            SetPrivate(itemGrip, "sizeReference", model.transform);

            // ── The artifact ──
            var artifact = root.AddComponent<RepulsorGauntletArtifact>();
            SetPrivate(artifact, "capacitorGlow", glow.transform);
            SetPrivate(artifact, "ringMaterial", ringMat);
            SetPrivate(artifact, "coneMaterial", coneMat);
            SetPrivate(artifact, "blastDust", dust);
            SetPrivate(artifact, "blastStreaks", streaks);
            SetPrivate(artifact, "blastDebris", debris);
            SetPrivate(artifact, "muzzleFlash", flash);
            SetPrivate(artifact, "blastShake", blastShake);

            return root;
        }

        // ── The blast emitters ─────────────────────────────────────────────────
        //
        // All bursts, never rates: the thundergun empties itself in one frame, so everything it
        // throws exists from frame one. The recipes are the gravel blaster's, widened and sped up —
        // that build already proved this shape of system reads at speed, and the difference here is
        // one of scale, not of kind.
        //
        // The four plumbing helpers at the bottom (NewBurstSystem/Burst/Cone/ConfigureRenderer, and
        // Ramp) are now the SECOND copy of GravelBlasterBuilder's; they want lifting into a shared
        // editor utility the next time one of these builders is opened, which is a change to that
        // file and not to this one.

        /// <summary>
        /// The wall itself: a broad billowing sheet of displaced air and sand, the widest and
        /// slowest of the three so it is still hanging there after the streaks have gone.
        /// </summary>
        private static ParticleSystem BuildBlastDust(Transform parent, Material material)
        {
            ParticleSystem ps = NewBurstSystem(parent, "BlastDust");

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(DustLifeMin, DustLifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(DustSpeedMin, DustSpeedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(DustSizeMin, DustSizeMax);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(DustGravityMin, DustGravityMax);
            main.maxParticles = DustCount * 2;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(DustDark, DustLight);

            Burst(ps, DustCount);
            Cone(ps, DustCone, 0.06f);

            // Snap in, drift out. A cloud that faded in symmetrically would put its brightest frame
            // after the thunderclap had already landed, which is the whole reason the old effect
            // read as weak (GDC-L1-FEEL-0002: the peak belongs on the frame of the input).
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(Ramp(
                new[] { (Color.white, 0f), (Color.white, 1f) },
                new[] { (0.9f, 0f), (0.75f, 0.25f), (0f, 1f) }));

            // Growth is what turns a spray of dots into a front: each puff keeps expanding as the
            // pressure behind it drops.
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.45f), new Keyframe(1f, 1.6f)));

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), material,
                              ParticleSystemRenderMode.Billboard);
            return ps;
        }

        /// <summary>
        /// Stretched air lines down the cone axis: the fastest and shortest-lived of the three, and
        /// the only one that says which WAY the force went. Stretch mode draws each particle along
        /// its own velocity, so speed becomes visible length rather than an invisible dot.
        /// </summary>
        private static ParticleSystem BuildBlastStreaks(Transform parent, Material material)
        {
            ParticleSystem ps = NewBurstSystem(parent, "BlastStreaks");

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(StreakLifeMin, StreakLifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(StreakSpeedMin, StreakSpeedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(StreakSizeMin, StreakSizeMax);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0f);
            main.maxParticles = StreakCount * 2;
            main.startColor = new ParticleSystem.MinMaxGradient(StreakCore, StreakEdge);

            Burst(ps, StreakCount);
            Cone(ps, StreakCone, 0.03f);

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(Ramp(
                new[] { (StreakCore, 0f), (StreakEdge, 0.6f), (StreakEdge, 1f) },
                new[] { (1f, 0f), (0.8f, 0.4f), (0f, 1f) }));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            ConfigureRenderer(renderer, material, ParticleSystemRenderMode.Stretch);
            renderer.velocityScale = StreakVelocityScale;
            renderer.lengthScale = StreakLengthScale;
            return ps;
        }

        /// <summary>
        /// Grit torn off the ground and thrown down the cone. The narrowest and longest-lived
        /// system, and the only one that collides: chunks skipping off the sand are what tie the
        /// blast to the world it went off in rather than leaving it a decal on the camera.
        /// </summary>
        private static ParticleSystem BuildBlastDebris(Transform parent, Material material)
        {
            ParticleSystem ps = NewBurstSystem(parent, "BlastDebris");

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(DebrisLifeMin, DebrisLifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(DebrisSpeedMin, DebrisSpeedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(DebrisSizeMin, DebrisSizeMax);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(DebrisGravityMin, DebrisGravityMax);
            main.maxParticles = DebrisCount * 2;
            main.startColor = new ParticleSystem.MinMaxGradient(Grit, Grit * 0.7f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            Burst(ps, DebrisCount);
            Cone(ps, DebrisCone, 0.04f);

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

        /// <summary>
        /// The flash at the emitter. Built DISABLED: the artifact switches it on in Present and off
        /// again on its flashSeconds deadline, so a light left enabled here would be a lamp welded
        /// to the player's hand.
        /// </summary>
        private static Light BuildMuzzleFlash(Transform parent)
        {
            var flashObject = new GameObject("MuzzleFlash");
            flashObject.transform.SetParent(parent, false);

            Light flash = flashObject.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = FlashCool;
            flash.range = FlashRange;
            flash.intensity = FlashIntensity;
            flash.shadows = LightShadows.None;
            flash.enabled = false;
            return flash;
        }

        private static ParticleSystem NewBurstSystem(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            // World space, so air already thrown keeps its own arc when the hand swings away.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            // Shape, NOT Hierarchy: EquipItemSocket rescales the whole gauntlet to ItemGrip.holdSize
            // when it is seated in a hand, and under Hierarchy that prop-sized factor would multiply
            // every particle's SPEED as well. The blast has to reach the same distance the artifact's
            // 20 m range says it does, whatever size the model ended up being held at.
            main.scalingMode = ParticleSystemScalingMode.Shape;
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

        // ── Materials and shakes ───────────────────────────────────────────────

        private static Material EnsureRingMaterial()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShockwaveShaderPath);
            if (shader == null)
            {
                Debug.LogError($"[RepulsorGauntlet] No shader at {ShockwaveShaderPath}; " +
                               "falling back to URP Unlit.");
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(RingMatPath) ?? ".");

            var material = AssetDatabase.LoadAssetAtPath<Material>(RingMatPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, RingMatPath);
            }

            // Re-pointed on every run, not only at creation: a first build that ran before the
            // shockwave shader had compiled left the material on the URP Unlit fallback, and
            // without this the re-run that would fix it silently returns the broken one.
            material.shader = shader;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// The charge glow's own material: URP Unlit, additively blended, so a sphere that grows
        /// out of the gauntlet reads as light rather than as a painted ball. Deliberately plain —
        /// the glow's whole animation is its SCALE, driven by the artifact, so a material with
        /// swept parameters of its own would only fight it.
        /// </summary>
        private static Material EnsureGlowMaterial()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GlowMatPath) ?? ".");

            var material = AssetDatabase.LoadAssetAtPath<Material>(GlowMatPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, GlowMatPath);
            }

            material.shader = Shader.Find("Universal Render Pipeline/Unlit");
            material.SetColor("_BaseColor", GlowColor);

            // URP Unlit's transparency is not a shader variant you can pick by name — it is these
            // properties plus the keyword plus the queue, exactly as the material inspector writes
            // them. Setting the blend factors alone leaves the material opaque.
            material.SetFloat("_Surface", 1f);  // 0 opaque, 1 transparent
            material.SetFloat("_Blend", 2f);    // 0 alpha, 1 premultiply, 2 additive, 3 multiply
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// The swept air cone's material: the RepulsorAirWarp shader, which refracts
        /// _CameraOpaqueTexture instead of painting over it. Every parameter it has is authored in
        /// the shader's own Properties block and animated per shot by RepulsorBlastCone through
        /// _Progress, so this only has to exist and point at the right shader.
        /// </summary>
        private static Material EnsureAirWarpMaterial()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(AirWarpShaderPath);
            if (shader == null)
            {
                Debug.LogError($"[RepulsorGauntlet] No shader at {AirWarpShaderPath}; " +
                               "falling back to URP Unlit — the cone will be a flat decal.");
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ConeMatPath) ?? ".");

            var material = AssetDatabase.LoadAssetAtPath<Material>(ConeMatPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, ConeMatPath);
            }

            // Re-pointed on every run for the same reason as the ring's: a build that ran before the
            // shader had compiled leaves the material stuck on the URP Unlit fallback, and without
            // this the re-run that would fix it silently hands back the broken one.
            material.shader = shader;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>The three shared particle materials, loaded together so one miss reports once.</summary>
        private readonly struct BlastMaterials
        {
            public readonly Material Dust;
            public readonly Material Streaks;
            public readonly Material Debris;

            public BlastMaterials(Material dust, Material streaks, Material debris)
            {
                Dust = dust;
                Streaks = streaks;
                Debris = debris;
            }

            public bool Complete => Dust != null && Streaks != null && Debris != null;
        }

        private static BlastMaterials LoadBlastMaterials()
        {
            var materials = new BlastMaterials(
                AssetDatabase.LoadAssetAtPath<Material>(SmokeMatPath),
                AssetDatabase.LoadAssetAtPath<Material>(SparkMatPath),
                AssetDatabase.LoadAssetAtPath<Material>(DebrisMatPath));

            if (!materials.Complete)
                Debug.LogError($"[RepulsorGauntlet] Missing a particle material under {WeaponMaterialDir}. " +
                               "Run Tools/Build Laser Staff Artifact (LaserSmoke, LaserSpark) and " +
                               "Tools/Build Gravel Blaster Artifact (GravelDebris) first.");

            return materials;
        }

        private static ShakeData EnsureShake(string path)
        {
            var shake = AssetDatabase.LoadAssetAtPath<ShakeData>(path);
            if (shake != null) return shake;

            if (!AssetDatabase.CopyAsset(ShakeSourcePath, path))
            {
                Debug.LogError($"[RepulsorGauntlet] Could not copy {ShakeSourcePath} to {path}.");
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<ShakeData>(path);
        }

        // ── The player's landing ───────────────────────────────────────────────

        /// <summary>
        /// Put <see cref="FlungBody"/> on the player prefab and hand it its landing shake.
        ///
        /// <para>
        /// A separate menu entry as well as part of the build, because the component lives on the
        /// PLAYER prefab rather than the gauntlet's — being flung is something that happens to the
        /// victim, whoever's gauntlet did it. Idempotent: a re-run finds the existing component.
        /// </para>
        /// <para>
        /// WHERE it lands is not where this path points. PlayerCharacterNetworked's root is a
        /// nested PlayerCharacter.prefab instance, so <c>AddComponent</c> on the loaded contents
        /// adds to that instance and <c>SaveAsPrefabAsset</c> writes the component itself into the
        /// BASE prefab, <c>Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab</c>. The
        /// networked prefab inherits it and keeps only the <c>flungShake</c> override.
        /// </para>
        /// <para>
        /// That is the outcome we want and it is left alone: every player prefab built on the base
        /// can be flung, rather than only the networked one. It is written down because the code
        /// reads as if it edits the file named here, and a reader who checks that file finds
        /// nothing but an override and concludes the wiring failed.
        /// </para>
        /// </summary>
        [MenuItem("Tools/SpaceGame/Items/Wire FlungBody Into Player")]
        public static void WireFlungBodyIntoPlayer()
        {
            ShakeData flungShake = EnsureShake(FlungShakePath);
            if (flungShake == null) return;

            GameObject playerRoot = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                FlungBody flung = playerRoot.GetComponent<FlungBody>();
                if (flung == null) flung = playerRoot.AddComponent<FlungBody>();

                var so = new SerializedObject(flung);
                so.FindProperty("flungShake").objectReferenceValue = flungShake;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(playerRoot, PlayerPrefabPath);
                Debug.Log("[RepulsorGauntlet] FlungBody wired into the base PlayerCharacter.prefab " +
                          $"via {PlayerPrefabPath}, which keeps the flungShake override.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(playerRoot);
            }
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

            item.itemName = "Repulsor Gauntlet";
            item.itemPrefab = prefab;
            EditorUtility.SetDirty(item);
            return item;
        }

        /// <summary>
        /// Point the prefab's pickup at its own item asset. Done after the prefab is saved,
        /// because the item asset references the saved prefab and the prefab references the item —
        /// one of the two links can only be made once both files exist.
        /// </summary>
        private static void WireItemIntoPickup(GameObject prefab, InventoryItem item)
        {
            Component pickup = prefab.GetComponents<Component>()
                .FirstOrDefault(c => c != null && c.GetType().FullName == "SpaceGame.Items.PickupableItem");
            if (pickup == null)
            {
                Debug.LogError("[RepulsorGauntlet] PickupableItem missing from the built prefab.");
                return;
            }

            var so = new SerializedObject(pickup);
            so.FindProperty("item").objectReferenceValue = item;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SavePrefabAsset(prefab);
        }

        /// <summary>
        /// Add the gauntlet to the list NetworkManager actually reads. Not
        /// <c>Assets/DefaultNetworkPrefabs.asset</c>, which regenerates itself and is not the list
        /// in use. An unregistered item prefab fails on CLIENTS ONLY — dropping one routes through
        /// World.Spawn — so solo playtesting cannot find this mistake.
        /// </summary>
        private static void RegisterNetworkPrefab(GameObject prefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (list == null)
            {
                Debug.LogError($"[RepulsorGauntlet] No network prefab list at {NetworkPrefabsPath}.");
                return;
            }

            if (list.Contains(prefab)) return;

            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
            Debug.Log("[RepulsorGauntlet] Registered as a network prefab.");
        }

        // ── Reflection helpers ─────────────────────────────────────────────────
        //
        // The item components keep their fields private and serialize them, which is right for
        // runtime code and simply means an editor script has to go in the same way the inspector
        // does. PickupableItem is additionally internal to Assembly-CSharp, so it cannot even be
        // named from this assembly — hence the type lookup rather than a typeof.

        private static void AddInternal(GameObject go, string typeName)
        {
            Type type = typeof(ItemGrip).Assembly.GetType(typeName);
            if (type == null)
            {
                Debug.LogError($"[RepulsorGauntlet] Type '{typeName}' not found.");
                return;
            }

            go.AddComponent(type);
        }

        private static void SetPrivate(Component target, string field, object value)
        {
            FieldInfo info = Field(target, field);
            info?.SetValue(target, value);
        }

        private static void SetPrivateLayerMask(Component target, string field, int mask)
        {
            FieldInfo info = Field(target, field);
            info?.SetValue(target, (LayerMask)mask);
        }

        private static FieldInfo Field(Component target, string name)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo info = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (info != null) return info;
            }

            Debug.LogError($"[RepulsorGauntlet] No field '{name}' on {target.GetType().Name}.");
            return null;
        }
    }
}
