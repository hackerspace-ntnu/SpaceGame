using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Weapons;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Builds the Laser Staff artifact: its two beam materials, its prefab, its
    /// <see cref="InventoryItem"/> asset, and its entry in the network prefab list.
    ///
    /// <para>
    /// This exists as a script rather than as hand-authored YAML because the prefab has to nest an
    /// imported FBX, and the file ids Unity assigns inside a model are decided at import time. A
    /// hand-written prefab referencing guessed ids does not fail loudly — it loads with a missing
    /// model and no error, which is the worst of both.
    /// </para>
    /// <para>
    /// It is re-runnable, and re-running it REPLACES the prefab wholesale. Anything hand-added in
    /// the inspector afterwards is destroyed by the next run, so tuning belongs in the numbers
    /// below rather than in the scene.
    /// </para>
    /// </summary>
    public static class LaserStaffBuilder
    {
        private const string ModelPath    = "Assets/Game/Art/Models/Weapons/LaserStaff/laser_staff.fbx";
        private const string PrefabPath   = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/LaserStaff.prefab";
        private const string ItemPath     = "Assets/Game/Resources/Items/Artifacts/LaserStaff.asset";
        private const string MaterialDir  = "Assets/Game/Art/Materials/Weapons";
        private const string BeamMatPath  = MaterialDir + "/LightningBeam.mat";
        private const string ImpactMatPath = MaterialDir + "/LaserImpact.mat";
        private const string SparkMatPath = MaterialDir + "/LaserSpark.mat";
        private const string SmokeMatPath = MaterialDir + "/LaserSmoke.mat";
        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

        /// <summary>Crimson, as chosen: near-white core, crimson body, deep-red halo.</summary>
        private static readonly Color Core = new Color(1.000f, 0.894f, 0.894f);
        private static readonly Color Body = new Color(1.000f, 0.169f, 0.169f);
        private static readonly Color Halo = new Color(0.420f, 0.000f, 0.000f);

        /// <summary>
        /// The arc's own two, kept apart from the impact rig's.
        ///
        /// Red only, and one hue at three exposures rather than three colours. The filament stays
        /// firmly red instead of going to the near-white the laser's core used, because bloom takes
        /// it towards white on its own — authoring the whiteness as well is how a "red" beam ends up
        /// looking pink.
        /// </summary>
        private static readonly Color ArcCore = new Color(1.000f, 0.300f, 0.240f);
        private static readonly Color ArcGlow = new Color(0.450f, 0.015f, 0.000f);

        /// <summary>The strike graph the arc drops on whatever it rests on. Tinted at runtime.</summary>
        private const string StrikeVfxPath = "Assets/Game/Prefabs/VisualEffects/Lightning/Lightning.prefab";

        [MenuItem("Tools/Build Laser Staff Artifact")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[LaserStaff] No model at {ModelPath}. Run walking_staff_export.py first.");
                return;
            }

            Material beamMat   = EnsureMaterial(BeamMatPath, "SpaceGame/LightningBeam", ConfigureBeamMaterial);
            Material impactMat = EnsureMaterial(ImpactMatPath, "SpaceGame/LaserImpact", ConfigureImpactMaterial);
            Material sparkMat  = EnsureMaterial(SparkMatPath, "SpaceGame/LaserSpark", ConfigureSparkMaterial);
            Material smokeMat  = EnsureMaterial(SmokeMatPath, "SpaceGame/LaserSmoke", ConfigureSmokeMaterial);
            if (beamMat == null || impactMat == null || sparkMat == null || smokeMat == null) return;

            GameObject root = BuildHierarchy(model, beamMat, impactMat, sparkMat, smokeMat);

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath) ?? ".");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);

            if (prefab == null)
            {
                Debug.LogError("[LaserStaff] Prefab save failed.");
                return;
            }

            InventoryItem item = EnsureItem(prefab);
            WireItemIntoPickup(prefab, item);
            RegisterNetworkPrefab(prefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[LaserStaff] Built {PrefabPath} and {ItemPath}. " +
                      "Run Tools/Generate All Item Icons to give it an inventory icon.");
        }

        // ── Hierarchy ──────────────────────────────────────────────────────────

        private static GameObject BuildHierarchy(GameObject model, Material beamMat, Material impactMat,
                                                 Material sparkMat, Material smokeMat)
        {
            var root = new GameObject("LaserStaff");

            // ── The model ──
            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.name = "Model";
            modelInstance.transform.SetParent(root.transform, false);

            // The staff was authored along +Z with its origin at the grip (see
            // walking_staff_BUILD.md), and Unity's convention for a held item is that it points
            // along the item's own +Z too — so no correction rotation is applied here. The
            // LightningSpell prefab's -90° X rotation is that item's own posing, not a units fix.
            Bounds local = LocalBounds(root.transform, modelInstance);

            // ── Muzzle, at the far end of the longest axis ──
            // Measured rather than typed. The fork is 1.48 m from the grip on this variation and
            // would be somewhere else on any other, and a hard-coded offset is a number that goes
            // silently wrong the day someone exports a different staff into this slot.
            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(root.transform, false);
            muzzle.transform.localPosition = MuzzleOffset(local);

            // ── Beam ──
            var beamObject = new GameObject("Beam");
            beamObject.transform.SetParent(root.transform, false);

            LineRenderer beam = beamObject.AddComponent<LineRenderer>();
            beam.sharedMaterial = beamMat;
            beam.useWorldSpace = true;
            beam.positionCount = 2;
            beam.widthMultiplier = 0.13f;
            beam.numCapVertices = 4;

            // The arc is drawn with a couple of dozen displaced points, and every one of them is a
            // corner. Without rounded joins the ribbon pinches to nothing at each kink and the bolt
            // reads as a chain of separate darts.
            beam.numCornerVertices = 3;
            beam.textureMode = LineTextureMode.Stretch;
            beam.alignment = LineAlignment.View;
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beam.receiveShadows = false;
            beam.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            beam.enabled = false;   // Lit only while firing; the artifact turns it on.

            // ── Impact ──
            //
            // One parent for the whole rig. The artifact turns this so its +Z is the surface
            // normal, and everything under it inherits that — which is the only reason the sparks
            // come OUT of a wall instead of going into it.
            var impactRoot = new GameObject("Impact");
            impactRoot.transform.SetParent(root.transform, false);

            GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glow.name = "ImpactGlow";
            glow.transform.SetParent(impactRoot.transform, false);
            glow.transform.localScale = Vector3.one * 0.55f;

            // A live collider on the impact quad would let the beam's own splash block the beam.
            UnityEngine.Object.DestroyImmediate(glow.GetComponent<Collider>());

            var glowRenderer = glow.GetComponent<MeshRenderer>();
            glowRenderer.sharedMaterial = impactMat;
            glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            glowRenderer.receiveShadows = false;
            glowRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            // Still billboarded even though its parent now faces along the normal: the glow is a
            // bloom in the camera, not a decal on the surface, so it should face the viewer while
            // the particles obey the wall.
            glow.AddComponent<BillboardFaceCamera>();
            glow.SetActive(false);

            ParticleSystem sparks = BuildSparks(impactRoot.transform, sparkMat);
            ParticleSystem embers = BuildEmbers(impactRoot.transform, sparkMat);
            ParticleSystem smokeSystem = BuildSmoke(impactRoot.transform, smokeMat);

            var lightObject = new GameObject("ImpactLight");
            lightObject.transform.SetParent(impactRoot.transform, false);
            Light impactLight = lightObject.AddComponent<Light>();
            impactLight.type = LightType.Point;
            impactLight.color = Body;
            impactLight.range = 9f;
            impactLight.intensity = 7f;
            impactLight.shadows = LightShadows.None;
            impactLight.enabled = false;

            // ── Pickup / world presence ──
            // Mirrors LightningSpell.prefab component for component: the same prefab is both the
            // thing in your hand and the thing lying in the sand.
            var netObject = root.AddComponent<NetworkObject>();
            netObject.SynchronizeTransform = true;

            AddInternal(root, "SpaceGame.Items.PickupableItem");

            // The body, a collider the shape of the item, the sizing and the netcode that lets
            // another machine watch it be shoved about. One shared block - see ItemWorldPresence
            // for what nine hand-written copies of it cost, and why the sphere it replaces here
            // made a dropped item roll like a marble.
            ItemWorldPresence.Apply(root);

            root.AddComponent<SpaceGame.Core.NetRelay>();
            root.AddComponent<SpaceGame.Core.Persistence.SaveableEntity>();
            root.AddComponent<SpaceGame.Core.Persistence.TransformSaveable>();

            // ── Grip ──
            var grip = root.AddComponent<ItemGrip>();
            SetPrivate(grip, "holdSize", 1.35f);
            SetPrivate(grip, "sizeReference", modelInstance.transform);

            // Both zero, and that is the answer rather than a placeholder.
            //
            // The FBX importer rotates the model −90° about X, which puts the staff's long axis on
            // prefab +Y with the crown up. ItemGrip's zero pose already means "+Y out the thumb
            // side, as a torch's flame would" — so a staff gripped at its own origin, crown out of
            // the top of the fist, is what no rotation gives. And the origin IS the grip
            // (walking_staff_BUILD.md), which is what makes the position offset zero too: the
            // whole point of that origin choice was that holding it costs no per-model number.
            SetPrivate(grip, "rotationOffset", Vector3.zero);
            SetPrivate(grip, "positionOffset", Vector3.zero);

            // ── The artifact ──
            var artifact = root.AddComponent<LaserStaffArtifact>();
            SetPrivate(artifact, "muzzle", muzzle.transform);
            SetPrivate(artifact, "beam", beam);
            SetPrivate(artifact, "impactRoot", impactRoot.transform);
            SetPrivate(artifact, "impactGlow", glow.transform);
            SetPrivate(artifact, "sparks", sparks);
            SetPrivate(artifact, "embers", embers);
            SetPrivate(artifact, "smoke", smokeSystem);
            SetPrivate(artifact, "impactLight", impactLight);

            // The strike itself. Missing is survivable — the artifact simply does not strike — so
            // this warns rather than aborting a build that is otherwise complete.
            var strikeVfx = AssetDatabase.LoadAssetAtPath<GameObject>(StrikeVfxPath);
            if (strikeVfx == null)
                Debug.LogWarning($"[LaserStaff] No strike VFX at {StrikeVfxPath}; the arc will land without one.");
            else
                SetPrivate(artifact, "strikeVfx", strikeVfx);

            // The ignition report. There is no sustained loop: Sfx has no way to stop one, and the
            // FMOD project is lost, so a looping beam hum could not be authored or cut off anyway.
            SetPrivateEnum(artifact, "useSoundId", "WeaponEnergyFire");

            return root;
        }

        // ── The impact emitters ────────────────────────────────────────────────
        //
        // Three systems rather than one, because the three things flying off a laser cut behave
        // nothing alike: sparks are fast, hot and nearly weightless; embers are slower, heavier and
        // cool as they fall; smoke rises. One system with a wide random range would produce a fog
        // of in-between particles that reads as none of them.
        //
        // All three simulate in WORLD space. In local space the whole spray would be dragged along
        // as the beam sweeps, so sparks already in the air would slide across the wall with the
        // impact point instead of arcing away from where they were struck.

        private static ParticleSystem BuildSparks(Transform parent, Material material)
        {
            var go = new GameObject("Sparks");
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.05f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(1.1f, 1.8f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 500;
            main.startColor = new ParticleSystem.MinMaxGradient(Core, Body);

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 220f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 38f;
            shape.radius = 0.02f;

            // Sparks cool as they fly: white hot, then crimson, then out. Fading the ALPHA rather
            // than the colour is what makes them wink out individually instead of the whole spray
            // dimming together.
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(Ramp(
                new[] { (Core, 0f), (Body, 0.45f), (Halo, 1f) },
                new[] { (1f, 0f), (1f, 0.6f), (0f, 1f) }));

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, Falling());

            ConfigureRenderer(ps, material, ParticleSystemRenderMode.Stretch, lengthScale: 2.0f);
            return ps;
        }

        private static ParticleSystem BuildEmbers(Transform parent, Material material)
        {
            var go = new GameObject("Embers");
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.032f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 220;
            main.startColor = new ParticleSystem.MinMaxGradient(Body, Halo);

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 38f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 62f;
            shape.radius = 0.03f;

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(Ramp(
                new[] { (Body, 0f), (Halo, 0.55f), (Halo, 1f) },
                new[] { (1f, 0f), (0.85f, 0.5f), (0f, 1f) }));

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, Falling());

            // Embers bounce off the floor, and that is the detail that sells the whole impact —
            // it is the one part of the effect that acknowledges the world it is happening in.
            // Only on this system: the sparks are too short-lived to ever reach anything, so
            // collision for them would be cost with nothing to show.
            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.bounce = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
            collision.dampen = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            collision.lifetimeLoss = 0.25f;

            ConfigureRenderer(ps, material, ParticleSystemRenderMode.Stretch, lengthScale: 1.5f);
            return ps;
        }

        private static ParticleSystem BuildSmoke(Transform parent, Material material)
        {
            var go = new GameObject("Smoke");
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.30f, 0.85f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.34f);

            // Negative gravity, so it rises. Smoke off a cut is hot.
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.14f, -0.04f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 140;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            // Dark and desaturated. Smoke lit by a crimson beam is still smoke — tinting it red
            // would make it read as more fire, and the impact needs one element that is not glowing.
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.10f, 0.09f, 0.09f), new Color(0.24f, 0.21f, 0.20f));

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 28f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 22f;
            shape.radius = 0.04f;

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(Ramp(
                new[] { (Color.white, 0f), (Color.white, 1f) },
                new[] { (0f, 0f), (0.7f, 0.18f), (0f, 1f) }));

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, Growing());

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.9f, 0.9f);

            ConfigureRenderer(ps, material, ParticleSystemRenderMode.Billboard);
            return ps;
        }

        private static void ConfigureRenderer(ParticleSystem ps, Material material,
                                              ParticleSystemRenderMode mode, float lengthScale = 1f)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = mode;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.sortMode = ParticleSystemSortMode.None;

            if (mode != ParticleSystemRenderMode.Stretch) return;

            // Length from velocity, not a fixed multiple of size: a spark's streak IS how far it
            // travelled this frame, so a slow ember reads as a dot and a fast spark as a dash from
            // the same emitter settings.
            //
            // A stretched particle's length is roughly `size * lengthScale + speed * velocityScale`,
            // and that second term is the one that bites. At 0.12 a 16 m/s spark draws a TWO METRE
            // streak — the first render of this effect came back looking like a dozen more lasers
            // firing out of the wall rather than like sparks. 0.015 keeps the longest one to about
            // a handspan, which is what actually reads as hot metal being thrown off.
            renderer.velocityScale = 0.015f;
            renderer.lengthScale = lengthScale;
        }

        /// <summary>A colour/alpha ramp, spelled out because Gradient wants the two keyed apart.</summary>
        private static Gradient Ramp((Color colour, float time)[] colours, (float alpha, float time)[] alphas)
        {
            var gradient = new Gradient();

            var colourKeys = new GradientColorKey[colours.Length];
            for (int i = 0; i < colours.Length; i++)
                colourKeys[i] = new GradientColorKey(colours[i].colour, colours[i].time);

            var alphaKeys = new GradientAlphaKey[alphas.Length];
            for (int i = 0; i < alphas.Length; i++)
                alphaKeys[i] = new GradientAlphaKey(alphas[i].alpha, alphas[i].time);

            gradient.SetKeys(colourKeys, alphaKeys);
            return gradient;
        }

        /// <summary>1 → 0, for anything that shrinks as it dies.</summary>
        private static AnimationCurve Falling() =>
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));

        /// <summary>Small → large, for smoke expanding as it rises and cools.</summary>
        private static AnimationCurve Growing() =>
            new AnimationCurve(new Keyframe(0f, 0.45f), new Keyframe(1f, 1f));

        /// <summary>Bounds of every renderer under <paramref name="instance"/>, in the root's space.</summary>
        private static Bounds LocalBounds(Transform space, GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);

            bool started = false;
            var bounds = new Bounds();

            foreach (Renderer r in renderers)
            {
                if (r is ParticleSystemRenderer) continue;

                Bounds world = r.bounds;
                var localCentre = space.InverseTransformPoint(world.center);
                var localSize = space.InverseTransformVector(world.size);
                localSize = new Vector3(
                    Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));

                var b = new Bounds(localCentre, localSize);
                if (!started) { bounds = b; started = true; }
                else bounds.Encapsulate(b);
            }

            return bounds;
        }

        /// <summary>
        /// The forked crown of the staff, which is where the beam comes out.
        ///
        /// Found by elimination rather than by looking for a fork. walking_staff_BUILD.md fixes
        /// the two facts this needs: the origin is the CENTRE OF THE GRIP, and the butt is the far
        /// end. A staff is gripped in its upper third — that is what makes it a walking staff — so
        /// along its longest axis the butt is always the extremity further from the origin and the
        /// crown is always the nearer one. Measured on this export: butt at −1.22 m, crown at
        /// +0.33 m, and the crown's cross-section is twice the shaft's where the prongs split.
        ///
        /// Taking the FURTHER extremity instead, which is the obvious reading of "the tip", puts
        /// the emitter on the ground spike — the staff then fires out of its own foot, and nothing
        /// about the prefab looks wrong until you watch it.
        /// </summary>
        private static Vector3 MuzzleOffset(Bounds local)
        {
            Vector3 min = local.min;
            Vector3 max = local.max;
            Vector3 size = local.size;

            int axis = 0;
            if (size.y > size.x) axis = 1;
            if (size.z > size[axis]) axis = 2;

            float crown = Mathf.Abs(max[axis]) < Mathf.Abs(min[axis]) ? max[axis] : min[axis];

            var offset = Vector3.zero;
            offset[axis] = crown;
            return offset;
        }

        // ── Materials ──────────────────────────────────────────────────────────

        private static Material EnsureMaterial(string path, string shaderName, Action<Material> configure)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[LaserStaff] Shader '{shaderName}' not found. Let Unity compile it first.");
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
            configure(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// The arc. One hue at three exposures — see the shader — so nothing in the beam is any
        /// colour but red.
        ///
        /// The filament is far narrower and sharper than the laser's was, and the glow far wider.
        /// That gap between a thin channel and a broad haze is what makes a discharge look like one
        /// rather than like a coloured stripe.
        /// </summary>
        private static void ConfigureBeamMaterial(Material m)
        {
            m.SetColor("_CoreColor", ArcCore);
            m.SetColor("_BoltColor", Body);
            m.SetColor("_GlowColor", ArcGlow);
            m.SetFloat("_Intensity", 4.2f);
            m.SetFloat("_CoreWidth", 0.14f);
            m.SetFloat("_CoreSharpness", 3.4f);
            m.SetFloat("_GlowFalloff", 0.55f);
            m.SetFloat("_CrackleScale", 6f);
            m.SetFloat("_CrackleSpeed", 34f);
            m.SetFloat("_CrackleDepth", 0.55f);
            m.SetFloat("_StrikeRate", 22f);
            m.SetFloat("_StrikeDepth", 0.35f);
            m.SetFloat("_MuzzleTaper", 0.05f);
            m.SetFloat("_TipFlare", 2.6f);
            m.SetFloat("_TipWidth", 0.14f);
            m.renderQueue = 3000;
        }

        private static void ConfigureImpactMaterial(Material m)
        {
            m.SetColor("_CoreColor", Core);
            m.SetColor("_EdgeColor", Body);
            m.SetFloat("_Intensity", 7.5f);
            m.SetFloat("_CoreSize", 0.26f);
            m.SetFloat("_Falloff", 2.0f);
            m.SetFloat("_RayCount", 7f);
            m.SetFloat("_RayLength", 0.72f);
            m.SetFloat("_Spin", 1.1f);
            m.SetFloat("_Pulse", 0.45f);
            m.SetFloat("_PulseSpeed", 9f);
            m.SetFloat("_RingSpeed", 2.2f);
            m.SetFloat("_RingWidth", 0.17f);
            m.SetFloat("_RingBoost", 0.85f);
            m.renderQueue = 3000;
        }

        private static void ConfigureSparkMaterial(Material m)
        {
            m.SetFloat("_Intensity", 7f);
            m.SetFloat("_Sharpness", 2.0f);
            m.SetFloat("_Taper", 1.4f);
            m.SetFloat("_CoreBoost", 2.2f);
            m.renderQueue = 3000;
        }

        private static void ConfigureSmokeMaterial(Material m)
        {
            m.SetFloat("_Softness", 0.78f);
            m.SetFloat("_NoiseScale", 1.9f);
            m.SetFloat("_Erosion", 0.62f);
            m.SetFloat("_Drift", 0.35f);
            m.SetFloat("_Opacity", 0.55f);
            m.renderQueue = 3050;   // After the additive impact, so it reads as sitting in front.
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

            item.itemName = "Laser Staff";
            item.itemPrefab = prefab;
            EditorUtility.SetDirty(item);
            return item;
        }

        /// <summary>
        /// Point the prefab's pickup at its own item asset.
        ///
        /// Done after the prefab is saved rather than during the build, because the item asset has
        /// to reference the saved prefab and the prefab has to reference the item — one of the two
        /// links can only be made once both files exist.
        /// </summary>
        private static void WireItemIntoPickup(GameObject prefab, InventoryItem item)
        {
            Component pickup = prefab.GetComponents<Component>()
                .FirstOrDefault(c => c != null && c.GetType().FullName == "SpaceGame.Items.PickupableItem");

            if (pickup == null)
            {
                Debug.LogError("[LaserStaff] PickupableItem missing from the built prefab.");
                return;
            }

            var so = new SerializedObject(pickup);
            so.FindProperty("item").objectReferenceValue = item;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SavePrefabAsset(prefab);
        }

        /// <summary>
        /// Add the staff to the list NetworkManager actually reads.
        ///
        /// Not <c>Assets/DefaultNetworkPrefabs.asset</c>, which regenerates itself and is not the
        /// list in use. An unregistered item prefab fails on CLIENTS ONLY — dropping one routes
        /// through World.Spawn — so solo playtesting cannot find this mistake.
        /// </summary>
        private static void RegisterNetworkPrefab(GameObject prefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (list == null)
            {
                Debug.LogError($"[LaserStaff] No network prefab list at {NetworkPrefabsPath}.");
                return;
            }

            if (list.Contains(prefab))
            {
                Debug.Log("[LaserStaff] Already registered as a network prefab.");
                return;
            }

            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
            Debug.Log("[LaserStaff] Registered as a network prefab.");
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
                Debug.LogError($"[LaserStaff] Type '{typeName}' not found.");
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

        private static void SetPrivateEnum(Component target, string field, string valueName)
        {
            FieldInfo info = Field(target, field);
            if (info == null) return;

            try { info.SetValue(target, Enum.Parse(info.FieldType, valueName)); }
            catch (ArgumentException)
            {
                Debug.LogWarning($"[LaserStaff] '{valueName}' is not a {info.FieldType.Name}; left at its default.");
            }
        }

        private static FieldInfo Field(Component target, string name)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo info = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (info != null) return info;
            }

            Debug.LogError($"[LaserStaff] No field '{name}' on {target.GetType().Name}.");
            return null;
        }
    }
}
