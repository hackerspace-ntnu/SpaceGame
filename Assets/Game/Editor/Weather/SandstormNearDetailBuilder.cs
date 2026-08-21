// Builds the grit that whips past your face inside a storm.
//
// Layer 3 of the storm — the one that is purely decorative, and the one that does most of the work
// convincing you the air is full of sand. The volumetric passes give you a wall you cannot see
// through; without this, standing in one is just a coloured screen. What sells motion is
// individual grains crossing the view fast enough to read as streaks.
//
// A builder rather than a hand-made prefab because the numbers matter and want to be re-derivable:
// the emission volume has to sit around the camera, the speed has to beat the player's own top
// speed or the grit hangs in the air, and the lifetime has to be short enough that particles never
// live long enough to be seen turning around.
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class SandstormNearDetailBuilder
    {
        private const string PrefabPath = "Assets/Game/Prefabs/Effects/SandstormGrit.prefab";
        private const string MaterialPath = "Assets/Game/Art/Materials/Environment/SandstormGrit.mat";

        [MenuItem("Tools/World/Build Sandstorm Grit")]
        public static void Build()
        {
            EnsureFolder("Assets/Game/Prefabs", "Effects");

            Material material = BuildMaterial();

            var host = new GameObject("SandstormGrit");
            BuildStreaks(host, material);
            BuildGroundSheet(host, material);

            // Driven by SandstormVisuals through this component; added here so the prefab needs no
            // wiring at all beyond being named on a profile.
            host.AddComponent<SpaceGame.World.Weather.SandstormVfx>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(host, PrefabPath);
            Object.DestroyImmediate(host);

            Debug.Log($"[Sandstorm] Built {PrefabPath}", prefab);
        }

        // The grains crossing your view. This layer is what makes the air read as SAND rather than
        // as smoke: smoke is big soft billows, sand is thousands of hard little streaks moving fast
        // enough that you never resolve one.
        private static void BuildStreaks(GameObject parent, Material material)
        {
            var host = new GameObject("Streaks");
            host.transform.SetParent(parent.transform, false);
            var system = host.AddComponent<ParticleSystem>();
            var renderer = host.GetComponent<ParticleSystemRenderer>();

            // Emission is authored at the rate for a storm at FULL intensity; SandstormVfx scales
            // it down from there, so anything gentler comes out of one multiply.
            ParticleSystem.MainModule main = system.main;
            main.duration = 4f;
            main.loop = true;
            // Small, fast and many. Individually a grain must be too small and too quick to
            // resolve — the moment you can pick one out it reads as a pebble, or an insect, and
            // the illusion is gone. The count is what carries it, not the size of each one.
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(34f, 75f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.19f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.94f, 0.80f, 0.58f, 0.7f), new Color(0.78f, 0.60f, 0.38f, 0.4f));
            main.maxParticles = 60000;
            main.gravityModifier = 0.04f;

            // Local space, so the grit travels with the player instead of being left behind the
            // moment they start running. The streaking comes from velocity, not from the world.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 32000f;

            // A shell around the camera rather than a point: grit has to appear beside and behind
            // you as well as in front, or it reads as being sprayed from your own face. Sized to
            // reach past the fog's clear radius rather than to sit inside it — grains only in the
            // nearest couple of metres read as a smudge on the lens, not as air full of sand — but
            // not far past it, because anything the fog hides is pure fill rate.
            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 15f;
            shape.radiusThickness = 1f;   // fill the volume, not just a shell at arm's length

            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, ShrinkCurve());

            // Fade in and out: a grain that pops into existence at full opacity two metres from the
            // camera is the single most obvious tell that this is a particle system.
            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(FadeGradient());

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(1.6f);
            noise.frequency = 0.4f;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(1.2f);
            noise.quality = ParticleSystemNoiseQuality.Medium;

            // Stretched billboards: a grain moving at 40 m/s is a streak, not a dot, and the streak
            // is what makes the wind's direction readable from inside the storm.
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.11f;
            renderer.lengthScale = 5f;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingFudge = -20f;
        }

        // Sand skimming the ground. Half of what makes a sandstorm feel like sand rather than fog
        // is that it is clearly being dragged along a surface — long, flat, fast sheets low down,
        // which also give the eye something to read the wind direction from while it is blind.
        private static void BuildGroundSheet(GameObject parent, Material material)
        {
            var host = new GameObject("GroundSheets");
            host.transform.SetParent(parent.transform, false);
            host.transform.localPosition = new Vector3(0f, -1.6f, 0f);

            var system = host.AddComponent<ParticleSystem>();
            var renderer = host.GetComponent<ParticleSystemRenderer>();

            ParticleSystem.MainModule main = system.main;
            main.duration = 5f;
            main.loop = true;
            // Small and numerous rather than big and soft. A handful of large sheets reads as
            // smoke drifting; many tight ones read as sand being dragged over a surface.
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(26f, 48f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 2.4f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.90f, 0.74f, 0.50f, 0.22f), new Color(0.76f, 0.58f, 0.36f, 0.12f));
            main.maxParticles = 10000;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 3400f;

            // A flat, wide box hugging the ground rather than a sphere: sheets, not a cloud.
            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(34f, 2f, 34f);

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(FadeGradient());

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(2.5f);
            noise.frequency = 0.25f;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(1.5f);

            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.10f;
            renderer.lengthScale = 3.5f;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingFudge = -10f;
        }

        private static Material BuildMaterial()
        {
            // Reuse the material if it already exists. Re-creating it loses the transparent blend
            // state: this method sets _Surface/_Blend/_ZWrite as raw floats, and URP only turns
            // those into the keywords and Blend modes that actually do anything when the material
            // inspector validates the asset. A freshly written one renders opaque until opened.
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null)
                return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var material = new Material(shader) { name = "SandstormGrit" };

            material.SetFloat("_Surface", 1f);      // transparent
            material.SetFloat("_Blend", 0f);        // alpha
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", 0f);
            material.SetColor("_BaseColor", Color.white);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            // Unity's built-in soft particle dot. A hard-edged quad reads as confetti.
            Texture2D dot = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
            if (dot != null)
                material.SetTexture("_BaseMap", dot);

            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        private static AnimationCurve ShrinkCurve() =>
            new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.35f));

        private static Gradient FadeGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(1f, 0.7f),
                    new GradientAlphaKey(0f, 1f),
                });
            return gradient;
        }

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
                AssetDatabase.CreateFolder(parent, child);
        }
    }
}
