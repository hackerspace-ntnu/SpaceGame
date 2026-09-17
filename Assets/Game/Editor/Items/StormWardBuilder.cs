// Builds the storm ward: a placeable stake that beats sandstorms back.
//
//   StormWard.prefab        in the hand: PlaceableItem + GroundPlacement (PlaceablePairBuilder).
//   PlacedStormWard.prefab  on the ground: StormWard, which strokes the ring up and slams it down on
//                           each pulse, a dust shockwave and a glow flash on the impact, the core's
//                           light, and PlacedObject to take it back.
//
// The model is components/props/storm_ward.blend, exported by storm_ward_export.py. Its FX_Emitter
// and LIGHT_Core empties place the flash and the light.
//
// Re-running is safe and is the intended workflow: both prefabs are rebuilt in place. After it,
// run Sync Network Prefabs and Wire Saveable Prefabs (the prefabId is cleared on every rebuild),
// then Generate All Item Icons.
//
// Re-run from: Tools > Items > Build Storm Ward
using SpaceGame.Items;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class StormWardBuilder
    {
        public const string HeldPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/StormWard.prefab";
        public const string PlacedPath = "Assets/Game/Prefabs/Items/Placed/PlacedStormWard.prefab";
        public const string AssetPath = "Assets/Game/Resources/Items/Artifacts/StormWard.asset";
        private const string Fbx = "Assets/Game/Art/Models/Items/storm_ward.fbx";
        private const string EmitterEmpty = "FX_Emitter";
        private const string CoreEmpty = "LIGHT_Core";
        private const string RingMesh = "Mesh_StormWard_Ring";
        private const string DustMaterialPath = "Assets/Game/Art/Materials/Weapons/LaserSmoke.mat";
        private const string GlowMaterialPath = "Assets/Game/Art/Materials/Weapons/LaserSpark.mat";

        // Stake and legs, as the model measures them: 0.62 m across the feet, 0.95 m to the vane.
        private static readonly Vector3 ColliderCentre = new Vector3(0f, 0.48f, 0f);
        private static readonly Vector3 ColliderSize = new Vector3(0.5f, 0.96f, 0.5f);

        private static readonly Color CoreColour = new Color(0.45f, 0.82f, 1f);
        private const float CoreRange = 4f;
        private const float CoreIntensity = 1.6f;

        // The shockwave: a burst of dust puffs round a small ring at knee height, thrown outward.
        // StormWard sets their speed so they reach its radius as they die.
        private static readonly Color DustColour = new Color(0.86f, 0.74f, 0.55f, 0.7f);
        private const float WaveHeight = 0.3f;
        private const float WaveLifetime = 2.2f;
        private const int WavePuffs = 140;
        private const float WaveStartRadius = 0.6f;
        private const float WavePuffSize = 3f;
        private const float WavePuffGrowth = 3.5f;

        private static readonly Color FlashColour = new Color(0.55f, 0.88f, 1f, 1f);
        private const float FlashLifetime = 0.45f;
        private const float FlashSize = 2.5f;

        [MenuItem("Tools/Items/Build Storm Ward")]
        public static void Build()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (source == null)
            {
                Debug.LogError($"No storm ward model at {Fbx}. Export it first:\n" +
                               "  blender --background --python components/props/storm_ward_export.py");
                return;
            }

            var dust = AssetDatabase.LoadAssetAtPath<Material>(DustMaterialPath);
            var glow = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterialPath);
            if (dust == null || glow == null)
                throw new System.InvalidOperationException($"Missing particle material {DustMaterialPath} or {GlowMaterialPath}.");

            InventoryItem asset = PlaceablePairBuilder.EnsureItemAsset(AssetPath, "Storm Ward");
            GameObject placed = BuildPlaced(source, asset, dust, glow);
            GameObject held = PlaceablePairBuilder.BuildHeld(source, "StormWard", HeldPath, asset, placed,
                                                             EmitterEmpty, CoreEmpty);
            PlaceablePairBuilder.Link(asset, held);

            AssetDatabase.SaveAssets();
            Debug.Log($"Storm ward built.\n  held:   {HeldPath}\n  placed: {PlacedPath}\n  asset:  {AssetPath}\n" +
                      "Now run Sync Network Prefabs, Wire Saveable Prefabs and Generate All Item Icons.");
        }

        private static GameObject BuildPlaced(GameObject source, InventoryItem asset, Material dust, Material glow)
        {
            StaticPropBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(PlacedPath).Replace('\\', '/'));
            GameObject root = PlaceablePairBuilder.Instantiate(source, "PlacedStormWard");
            try
            {
                var box = root.AddComponent<BoxCollider>();
                box.center = ColliderCentre;
                box.size = ColliderSize;

                Transform emitter = RequireChild(root, EmitterEmpty);
                Transform core = RequireChild(root, CoreEmpty);
                Transform ring = RequireChild(root, RingMesh);

                // The flash leaves from the ring, so it has to ride the ring's stroke.
                emitter.SetParent(ring, true);

                var light = core.gameObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = CoreColour;
                light.range = CoreRange;
                light.intensity = CoreIntensity;

                ParticleSystem wave = BuildWave(root.transform, dust);
                ParticleSystem flash = BuildFlash(emitter, glow);

                var ward = root.AddComponent<StormWard>();
                var so = new SerializedObject(ward);
                SerializedFields.Set(so, "shockwave", wave);
                SerializedFields.Set(so, "flash", flash);
                SerializedFields.Set(so, "head", ring);
                so.ApplyModifiedPropertiesWithoutUndo();

                PlaceablePairBuilder.AddPlacedWiring(root, asset, "Storm ward");
                return PrefabUtility.SaveAsPrefabAsset(root, PlacedPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static ParticleSystem BuildWave(Transform parent, Material dust)
        {
            var go = new GameObject("Shockwave");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.up * WaveHeight;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = WaveLifetime;
            main.startLifetime = WaveLifetime;
            main.startSize = WavePuffSize;
            main.startColor = DustColour;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = WavePuffs;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, WavePuffs) });

            // A circle's edge, laid flat: puffs leave the ring outward along the ground.
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = WaveStartRadius;
            shape.radiusThickness = 0f;
            shape.rotation = new Vector3(90f, 0f, 0f);

            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(WavePuffGrowth, AnimationCurve.Linear(0f, 1f / WavePuffGrowth, 1f, 1f));

            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = Fade(0.15f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = dust;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            return ps;
        }

        private static ParticleSystem BuildFlash(Transform emitter, Material glow)
        {
            var go = new GameObject("Flash");
            go.transform.SetParent(emitter, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = FlashLifetime;
            main.startLifetime = FlashLifetime;
            main.startSpeed = 0f;
            main.startSize = FlashSize;
            main.startColor = FlashColour;
            main.maxParticles = 1;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;

            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.3f, 1f, 1f));

            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = Fade(0f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = glow;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            return ps;
        }

        /// <summary>Full alpha from <paramref name="holdUntil"/> of the life, to nothing at its end.</summary>
        private static ParticleSystem.MinMaxGradient Fade(float holdUntil)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, holdUntil), new GradientAlphaKey(0f, 1f) });
            return gradient;
        }

        private static Transform RequireChild(GameObject root, string name)
        {
            Transform t = PlaceablePairBuilder.FindChild(root.transform, name);
            if (t == null)
                throw new System.InvalidOperationException($"{Fbx} has no '{name}'; re-export with storm_ward_export.py (keep_empties).");
            return t;
        }
    }
}
