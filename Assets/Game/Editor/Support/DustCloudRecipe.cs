// The one lingering-cloud particle recipe: the monowheel's dust, and anything else that should throw
// up a cloud that then hangs -- other vehicles' dust, and the same cloud in black as engine smoke.
//
// Every puff is thrown out along the system's forward, drag stops it within about a second, and it
// billows out, churns and lingers where it stopped. The system simulates in world space and inherits
// nothing from a moving emitter, so a moving vehicle leaves a wall of cloud behind it rather than
// dragging the cloud along. Emission is off at build time: whatever owns the system drives
// rateOverTime (a rolling wheel) or calls Emit (a foot landing).
//
// Overdraw is paid per pixel covered, so the alpha stays moderate and the count is capped by the caller
// (GDC-L1-TECH-0002). The material is the textureless JetSmoke shader; its _SoftFade hides where a
// puff meets the ground, and should be 0 for a cloud that never touches anything.
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class DustCloudRecipe
    {
        public const string Shader = "SpaceGame/Effects/JetSmoke";
        public const float MinLife = 8f, MaxLife = 12f;
        /// <summary>A puff's diameter at birth (m), before the recipe billows it about 4x.</summary>
        public const float MinSize = 2f, MaxSize = 3.2f;

        /// <summary>The cap that keeps a cloud whole at a given peak emission rate (particles/s).</summary>
        public static int CapFor(float peakRate) => Mathf.CeilToInt(peakRate * MaxLife);

        /// <summary>
        /// A JetSmoke material, created at <paramref name="path"/> the first time and re-tinted on every
        /// build. A script-created ParticleSystem with no material draws nothing, silently (Jetpack
        /// gotcha), so a missing shader is an error rather than an empty field.
        /// </summary>
        public static Material Material(string path, Color tint, float softFade)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = UnityEngine.Shader.Find(Shader);
                if (shader == null)
                    throw new System.InvalidOperationException($"Shader '{Shader}' not found; {path} would draw nothing.");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_Color", tint);
            mat.SetFloat("_SoftFade", softFade);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>A looping, world-space, emission-off particle system under <paramref name="parent"/>.</summary>
        public static ParticleSystem NewSystem(Transform parent, string name, Vector3 localPos, Quaternion localRot,
                                               int cap, float minLife, float maxLife)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.maxParticles = cap;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;   // the owner drives it
            return ps;
        }

        /// <summary>
        /// A lingering cloud, thrown along <paramref name="aim"/> (in the parent's space) from
        /// <paramref name="localPos"/>, in <paramref name="tint"/>. <paramref name="shapeOffset"/> lifts
        /// the birth point along the system's own axes, e.g. just off the sand.
        /// </summary>
        public static ParticleSystem Cloud(Transform parent, string name, Vector3 localPos, Quaternion aim,
                                           Material material, Color tint, int cap, Vector3 shapeOffset)
        {
            ParticleSystem ps = NewSystem(parent, name, localPos, aim, cap, MinLife, MaxLife);
            ParticleSystem.MainModule main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 7.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(MinSize, MaxSize);
            main.startColor = new Color(tint.r, tint.g, tint.b, 0.6f);
            main.gravityModifier = -0.02f;   // the hanging cloud drifts up a little
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 30f;
            shape.radius = 0.6f;
            shape.position = shapeOffset;
            ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 2.5f;
            drag.multiplyDragByParticleSize = false;   // the puffs grow 4x; their drag must not grow with them
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            var billow = new AnimationCurve(new Keyframe(0f, 1f, 0f, 8f), new Keyframe(0.25f, 2.8f), new Keyframe(1f, 4.2f, 1f, 0f));
            size.size = new ParticleSystem.MinMaxCurve(1f, billow);
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.15f;
            noise.scrollSpeed = 0.1f;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            ParticleSystem.RotationOverLifetimeModule spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-10f * Mathf.Deg2Rad, 10f * Mathf.Deg2Rad);
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.06f),
                                 new GradientAlphaKey(0.75f, 0.45f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;
            Renderer(ps, material);
            return ps;
        }

        public static void Renderer(ParticleSystem ps, Material material)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }
}
