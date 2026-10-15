using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Builds the soldering torch: its prefab (the model, the flame, the sparks and molten glow where it lands, the flickering
    /// light, the gas canister), its <see cref="InventoryItem"/>, its icon, its network registration, and its place on the
    /// ship's gear wall. Re-runnable, and re-running REPLACES the prefab wholesale: tuning belongs in the numbers below.
    ///
    /// <para>
    /// DefaultNetworkPrefabs.asset and the gear wall are shared: take the scratchpad's netprefabs.lock before running it.
    /// </para>
    /// </summary>
    public static class SolderingTorchBuilder
    {
        private const string ModelPath = "Assets/Game/Art/Models/Items/soldering_torch.fbx";
        public const string PrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/SolderingTorch.prefab";
        public const string ItemPath = "Assets/Game/Resources/Items/Artifacts/SolderingTorch.asset";
        public const string GearWallPath = "Assets/Game/Prefabs/Items/Equipment/InventoryWall.prefab";
        private const string NetworkPrefabsPath = "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";
        private const string FlameSourcePath = "Assets/Game/Art/Materials/Items/FlameCore.mat";
        private const string EmberPath = "Assets/Game/Art/Materials/Items/FlameEmber.mat";
        public const string TorchFlamePath = "Assets/Game/Art/Materials/Items/TorchFlame.mat";

        // Measured off the export (soldering_torch_export.py's describe()): the nozzle tip and the way it points, in the FBX's
        // own frame, which is the prefab's — the origin is where the hand closes round the canister.
        private static readonly Vector3 NozzleTip = new(0f, 0.0767f, 0.2883f);
        private static readonly Vector3 NozzleAim = new(0f, -0.208f, 0.978f);

        // Longest axis in the hand, in metres: a hand tool on a 3 m body (true length 0.32 m).
        private const float HoldSize = 0.55f;

        // The gas: about a hundred seconds of flame, back to full in twenty once the trigger is up.
        private const float DrainPerSecond = 0.01f;
        private const float RefillPerSecond = 0.05f;
        private const float RefillDelay = 1f;
        private const float RestartFraction = 0.1f;

        // The flame, in the prefab's own metres (it scales with the held item).
        private const float FlameLength = 0.13f;
        private const float FlameWidth = 0.035f;

        private static readonly Color FlameCore = new(0.92f, 0.97f, 1f, 1f);
        private static readonly Color FlameMid = new(0.45f, 0.72f, 1f, 1f);
        private static readonly Color FlameEdge = new(0.16f, 0.36f, 1f, 1f);
        private static readonly Color FlameSoot = new(0.05f, 0.1f, 0.4f, 1f);
        private static readonly Color SparkHot = new(1f, 0.85f, 0.45f, 1f);
        private static readonly Color SparkCool = new(1f, 0.38f, 0.08f, 1f);
        private static readonly Color MoltenGlow = new(1f, 0.5f, 0.1f, 1f);
        private static readonly Color TorchLight = new(0.62f, 0.8f, 1f, 1f);

        [MenuItem("Tools/SpaceGame/Items/Build Soldering Torch")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[SolderingTorch] No model at {ModelPath}. Run soldering_torch_export.py first.");
                return;
            }

            Material flame = EnsureFlameMaterial();
            var ember = AssetDatabase.LoadAssetAtPath<Material>(EmberPath);
            if (flame == null || ember == null)
            {
                Debug.LogError($"[SolderingTorch] Missing a flame material ({FlameSourcePath}, {EmberPath}).");
                return;
            }

            var root = new GameObject("SolderingTorch");
            GameObject prefab;
            try
            {
                BuildHierarchy(root, model, flame, ember);
                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath) ?? ".");
                prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            if (prefab == null)
            {
                Debug.LogError("[SolderingTorch] Prefab save failed.");
                return;
            }

            InventoryItem item = EnsureItem(prefab);
            WireItemIntoPickup(prefab, item);
            RegisterNetworkPrefab(prefab);
            StockGearWall(item);
            AssetDatabase.SaveAssets();

            // First build: give the item its icon, then draw it again over itself — the first render after an import is
            // Unity's placeholder and comes out blank (INVARIANTS: a shader that reports no errors). A rebuild just redraws.
            bool drawn = (item.icon != null || BatchIconGenerator.CreateFor(item, out _)) && BatchIconGenerator.GenerateFor(item, out string note);
            if (!drawn) Debug.LogWarning("[SolderingTorch] The icon could not be drawn.");
            AssetDatabase.SaveAssets();
            Verify();
        }

        // ── The prefab ───────────────────────────────────────────────────────

        private static void BuildHierarchy(GameObject root, GameObject model, Material flame, Material ember)
        {

            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.name = "Model";
            modelInstance.transform.SetParent(root.transform, false);

            var grip = new GameObject("GripPoint").transform;
            grip.SetParent(root.transform, false);

            var nozzle = new GameObject("Nozzle").transform;
            nozzle.SetParent(root.transform, false);
            nozzle.localPosition = NozzleTip;
            nozzle.localRotation = Quaternion.LookRotation(NozzleAim.normalized);

            ParticleSystem jet = Flame(nozzle, flame);
            Glow(nozzle, jet);

            // The landings are moved to wherever the flame lands every sweep, so they live off the root in world space.
            ParticleSystem sparks = Sparks(root.transform, ember, "SolderSparks", rate: 140f, speed: new Vector2(1.2f, 3.6f), size: new Vector2(0.008f, 0.02f));
            Molten(sparks.transform, flame);
            ParticleSystem scatter = Sparks(root.transform, ember, "SurfaceSparks", rate: 35f, speed: new Vector2(0.6f, 2f), size: new Vector2(0.006f, 0.014f));

            CryoSprayerNozzle rig = nozzle.gameObject.AddComponent<CryoSprayerNozzle>();
            SerializedFields.Edit(rig, so =>
            {
                SerializedFields.Set(so, "plume", jet);
                SerializedFields.SetFloat(so, "shutConeDegrees", 1f);
                SerializedFields.SetFloat(so, "openConeDegrees", 4f);
                SerializedFields.Set(so, "bite", sparks);
                SerializedFields.Set(so, "blowoff", scatter);
                SerializedFields.SetObjects(so, "rimedParts", System.Array.Empty<Renderer>());
                SerializedFields.SetEnumByName(so, "sprayLoop", nameof(SfxId.PortalSprayLoop));
            });

            // ── Pickup / world presence ──
            var netObject = root.AddComponent<NetworkObject>();
            netObject.SynchronizeTransform = true;
            root.AddComponent(typeof(ItemGrip).Assembly.GetType("SpaceGame.Items.PickupableItem"));
            ItemWorldPresence.Apply(root);
            root.AddComponent<NetRelay>();
            root.AddComponent<SaveableEntity>();
            root.AddComponent<TransformSaveable>();

            var itemGrip = root.AddComponent<ItemGrip>();
            SerializedFields.Edit(itemGrip, so =>
            {
                SerializedFields.Set(so, "gripPoint", grip);
                SerializedFields.SetFloat(so, "holdSize", HoldSize);
                SerializedFields.Set(so, "sizeReference", modelInstance.transform);
                SerializedFields.SetEnumByName(so, "holdStyle", nameof(ItemGrip.HoldStyle.OneHanded));
            });

            SupplyReservoir gas = root.AddComponent<SupplyReservoir>();
            SerializedFields.Edit(gas, so =>
            {
                SerializedFields.SetEnumByName(so, "kind", nameof(SupplyKind.Reagent));
                SerializedFields.SetFloat(so, "startingCharge", 1f);
                SerializedFields.SetFloat(so, "drainPerSecond", DrainPerSecond);
                SerializedFields.SetFloat(so, "refillPerSecond", RefillPerSecond);
                SerializedFields.SetFloat(so, "refillDelay", RefillDelay);
                SerializedFields.SetFloat(so, "restartFraction", RestartFraction);
            });

            var torch = root.AddComponent<SolderingTorchArtifact>();
            SerializedFields.Edit(torch, so =>
            {
                SerializedFields.Set(so, "tank", gas);
                SerializedFields.Set(so, "nozzle", rig);
                SerializedFields.SetInt(so, "maxUses", -1);
            });
        }

        /// <summary>A short, stiff blue-white jet: quick particles stretched along their travel, scaled with the held item.</summary>
        private static ParticleSystem Flame(Transform parent, Material material)
        {
            ParticleSystem jet = Emitter(parent, "Flame", material, ParticleSystemSimulationSpace.Local);

            ParticleSystem.MainModule main = jet.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.07f, 0.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(FlameLength / 0.1f, FlameLength / 0.07f);
            main.startSize = new ParticleSystem.MinMaxCurve(FlameWidth * 0.7f, FlameWidth);
            main.maxParticles = 120;

            ParticleSystem.EmissionModule emission = jet.emission;
            emission.rateOverTime = 260f;

            ParticleSystem.ShapeModule shape = jet.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 4f;
            shape.radius = 0.004f;

            ParticleSystem.ColorOverLifetimeModule colour = jet.colorOverLifetime;
            colour.enabled = true;
            colour.color = Fade(FlameCore, FlameEdge);

            ParticleSystem.SizeOverLifetimeModule size = jet.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.4f, 1f), new Keyframe(1f, 0.2f)));

            var renderer = jet.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.04f;
            renderer.lengthScale = 1.6f;
            return jet;
        }

        /// <summary>The flame's own light: blue-white, short, flickering while the jet burns (<see cref="FlameFlicker"/>).</summary>
        private static void Glow(Transform nozzle, ParticleSystem jet)
        {
            var holder = new GameObject("FlameLight");
            holder.transform.SetParent(nozzle, false);
            holder.transform.localPosition = Vector3.forward * (FlameLength * 0.5f);

            var light = holder.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = TorchLight;
            light.range = 1.4f;
            light.intensity = 2.5f;
            light.shadows = LightShadows.None;
            light.enabled = false;

            var flicker = holder.AddComponent<FlameFlicker>();
            SerializedFields.Edit(flicker, so => SerializedFields.Set(so, "flame", jet));
        }

        /// <summary>Sparks thrown off where the flame lands: hot streaks that fall and cool.</summary>
        private static ParticleSystem Sparks(Transform parent, Material material, string name, float rate, Vector2 speed, Vector2 size)
        {
            ParticleSystem sparks = Emitter(parent, name, material, ParticleSystemSimulationSpace.World);

            ParticleSystem.MainModule main = sparks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.gravityModifier = 1.2f;
            main.maxParticles = 300;

            ParticleSystem.EmissionModule emission = sparks.emission;
            emission.rateOverTime = rate;

            ParticleSystem.ShapeModule shape = sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.02f;

            ParticleSystem.ColorOverLifetimeModule colour = sparks.colorOverLifetime;
            colour.enabled = true;
            colour.color = Fade(SparkHot, SparkCool);

            var renderer = sparks.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.03f;
            renderer.lengthScale = 2f;
            return sparks;
        }

        /// <summary>The puddle of molten orange under the sparks: slow, close, played with them.</summary>
        private static void Molten(Transform sparks, Material material)
        {
            ParticleSystem glow = Emitter(sparks, "MoltenGlow", material, ParticleSystemSimulationSpace.World);

            ParticleSystem.MainModule main = glow.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.maxParticles = 40;

            ParticleSystem.EmissionModule emission = glow.emission;
            emission.rateOverTime = 30f;

            ParticleSystem.ShapeModule shape = glow.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.01f;

            ParticleSystem.ColorOverLifetimeModule colour = glow.colorOverLifetime;
            colour.enabled = true;
            colour.color = Fade(MoltenGlow, SparkCool);
        }

        private static ParticleSystem Emitter(Transform parent, string name, Material material, ParticleSystemSimulationSpace space)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = space;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        private static ParticleSystem.MinMaxGradient Fade(Color from, Color to)
        {
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
                             new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(0f, 1f) });
            return new ParticleSystem.MinMaxGradient(gradient);
        }

        /// <summary>The project's stylized flame shader, recoloured from fire to a gas torch's blue: a copy of FlameCore.</summary>
        private static Material EnsureFlameMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(TorchFlamePath);
            if (material == null)
            {
                var source = AssetDatabase.LoadAssetAtPath<Material>(FlameSourcePath);
                if (source == null) return null;
                material = new Material(source) { name = "TorchFlame" };
                AssetDatabase.CreateAsset(material, TorchFlamePath);
            }

            material.SetColor("_CoreColor", FlameCore);
            material.SetColor("_MidColor", FlameMid);
            material.SetColor("_EdgeColor", FlameEdge);
            material.SetColor("_SootColor", FlameSoot);
            EditorUtility.SetDirty(material);
            return material;
        }

        // ── The item, the network, the wall ──────────────────────────────────

        private static InventoryItem EnsureItem(GameObject prefab)
        {
            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<InventoryItem>();
                AssetDatabase.CreateAsset(item, ItemPath);
            }

            item.itemName = "Soldering Torch";
            item.itemPrefab = prefab;
            item.menacing = false;
            EditorUtility.SetDirty(item);
            return item;
        }

        private static void WireItemIntoPickup(GameObject prefab, InventoryItem item)
        {
            Component pickup = prefab.GetComponents<Component>().First(c => c != null && c.GetType().Name == "PickupableItem");
            SerializedFields.Edit(pickup, so =>
            {
                SerializedFields.Set(so, "item", item);
                SerializedFields.SetEnumByName(so, "pickupId", nameof(SfxId.InteractPickupMetal));
            });
            PrefabUtility.SavePrefabAsset(prefab);
        }

        private static void RegisterNetworkPrefab(GameObject prefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (list == null)
            {
                Debug.LogError($"[SolderingTorch] No network prefab list at {NetworkPrefabsPath}.");
                return;
            }

            if (list.Contains(prefab)) return;
            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
        }

        /// <summary>
        /// One torch in the ship's gear wall's fixed manifest: the plant needs one, it never wears out, so a second would be a
        /// duplicate with nothing to do (the battery's rule, Oxygen.md).
        /// </summary>
        private static void StockGearWall(InventoryItem item)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(GearWallPath);
            try
            {
                var wall = root.GetComponent<WallInventory>();
                var so = new SerializedObject(wall);
                SerializedProperty list = so.FindProperty("startingMainItems");
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == item) return;

                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = item;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, GearWallPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void Verify()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            var wall = AssetDatabase.LoadAssetAtPath<GameObject>(GearWallPath).GetComponent<WallInventory>();
            var stocked = new SerializedObject(wall).FindProperty("startingMainItems");

            string problem =
                prefab == null ? "no prefab" :
                prefab.GetComponent<NetworkObject>().PrefabIdHash == 0 ? "the NetworkObject hash is 0" :
                item == null || item.itemPrefab != prefab ? "the item does not point at the prefab" :
                list == null || !list.Contains(prefab) ? "not in the network prefab list" :
                !Enumerable.Range(0, stocked.arraySize).Any(i => stocked.GetArrayElementAtIndex(i).objectReferenceValue == item) ? "not on the gear wall" :
                item.icon == null ? "no icon" : null;

            if (problem != null) Debug.LogError($"[SolderingTorch] {problem}.");
            else Debug.Log($"[SolderingTorch] Built {PrefabPath}, {ItemPath}; registered; on the gear wall.");
        }
    }
}
