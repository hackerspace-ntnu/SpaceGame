// Assets/Game/Editor/Vehicles/MonowheelPresentationBuilder.cs
//
// Builds the five monowheel ART prefabs (spec §5): the model, MonowheelPresentation with its wheels
// measured from geometry, and three world-space particle systems per wheel. The Strider gameplay
// prefabs (StriderMonowheelBuilder, Strider spec §2.3) wrap these; they never rebuild the effects.
//
// Everything geometric is measured, because the FBX importer re-orients bones and the doubles are
// cambered 7° and 20°: the axle is the ring mesh's plane normal, the radius the paddles' reach.
//
// Re-run from: Tools ▸ Vehicles ▸ Build Monowheel Presentation
using System.Collections.Generic;
using SpaceGame.Vehicles.Monowheel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceGame.EditorTools
{
    public static class MonowheelPresentationBuilder
    {
        public static readonly (string variant, string fbx, int rings)[] Variants =
        {
            ("Runner", "desert_monowheel_runner", 1),
            ("Hauler", "desert_monowheel_hauler", 1),
            ("Patched", "desert_monowheel_patched", 1),
            ("Double", "desert_monowheel_double", 2),
            ("DoubleWide", "desert_monowheel_double_wide", 2),
        };

        public const string ModelFolder = "Assets/Game/Art/Models/Vehicles/Monowheel";
        public const string PrefabFolder = "Assets/Game/Prefabs/Vehicles/Monowheel";
        private const string MaterialFolder = "Assets/Game/Art/Materials/Vehicles";
        private const string DustMaterialPath = MaterialFolder + "/MonowheelDust.mat";
        private const string SmokeMaterialPath = MaterialFolder + "/MonowheelHubSmoke.mat";
        private const string SmokeShader = "SpaceGame/Effects/JetSmoke";

        // The build recipe for the three layers (spec §3). Runtime rates live on the component.
        private static readonly Color SandTint = new Color(0.78f, 0.66f, 0.47f, 1f);
        private static readonly Color SmokeTint = new Color(0.36f, 0.33f, 0.30f, 1f);
        public const int SprayCap = 60, DustCap = 120, SmokeCap = 40;

        public static string PrefabPath(string variant) => $"{PrefabFolder}/Monowheel_{variant}.prefab";

        [MenuItem("Tools/Vehicles/Build Monowheel Presentation")]
        public static void BuildAll()
        {
            Material dust = SmokeMaterial(DustMaterialPath, SandTint);
            Material smoke = SmokeMaterial(SmokeMaterialPath, SmokeTint);
            foreach (var (variant, fbx, rings) in Variants)
            {
                string path = Build(variant, $"{ModelFolder}/{fbx}.fbx", rings, dust, smoke);
                SelfCheck(path);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Monowheel] Built and verified {Variants.Length} presentation prefabs in {PrefabFolder}.");
        }

        // ── materials ───────────────────────────────────────────────────────────

        // A script-created ParticleSystem with no material draws NOTHING, silently (Jetpack gotcha),
        // so both are created here and a missing shader is an error, not an empty field.
        private static Material SmokeMaterial(string path, Color tint)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find(SmokeShader);
                if (shader == null)
                    throw new System.InvalidOperationException($"Shader '{SmokeShader}' not found — the monowheel dust would draw nothing.");
                System.IO.Directory.CreateDirectory(MaterialFolder);
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_Color", tint);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ── one prefab ──────────────────────────────────────────────────────────

        private static string Build(string variant, string fbxPath, int ringCount, Material dust, Material smoke)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null) throw new System.IO.FileNotFoundException("Monowheel model missing", fbxPath);

            // The FBX's own root is the ARMATURE node — tilted with the rig, axis-converted and
            // scaled ×100 — so its +Z is not the vehicle's forward. The prefab gets a clean root at
            // identity with the model nested under it, also at identity: in that frame +Z is
            // forward (the ski) and +Y up, which is what MonowheelPresentation reads speed along.
            var root = new GameObject($"Monowheel_{variant}");
            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.transform.SetParent(root.transform, false);
            try
            {
                var measured = new List<MonowheelWheel>();
                foreach (Transform bone in FindRingBones(root.transform))
                {
                    MonowheelWheel wheel = Measure(root.transform, bone);
                    string side = bone.name.Substring("Bone_Ring".Length);   // "", "L" or "R"
                    wheel.spray = Spray(root.transform, $"FX_Spray{side}", wheel.localContact);
                    wheel.dust = Dust(root.transform, $"FX_Dust{side}", wheel.localContact, dust);
                    wheel.smoke = Smoke(root.transform, $"FX_HubSmoke{side}", wheel.localHub, smoke);
                    Renderer(wheel.spray, dust);
                    measured.Add(wheel);
                }
                if (measured.Count != ringCount)
                    throw new System.InvalidOperationException($"{variant}: found {measured.Count} ring bone(s), expected {ringCount}.");
                RequireForwardIsPlusZ(root.transform, measured, variant);

                root.AddComponent<MonowheelPresentation>().Configure(measured.ToArray());

                System.IO.Directory.CreateDirectory(PrefabFolder);
                string path = PrefabPath(variant);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return path;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // MonowheelPresentation reads speed along the root's +Z. If the FBX import ever rotated the
        // root (as some Blender imports do), speed would be measured along the wrong axis and the
        // wheels would spin sideways to the motion, silently. Every variant has a front cowl ahead
        // of the wheels, so its position against the hubs says which way the root faces.
        private static void RequireForwardIsPlusZ(Transform root, List<MonowheelWheel> wheels, string variant)
        {
            Transform cowl = null;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Mesh_FrontCowl")) cowl = t;
            if (cowl == null) throw new System.InvalidOperationException($"{variant}: no Mesh_FrontCowl* to tell front from back.");
            Bounds b = cowl.GetComponent<Renderer>().bounds;
            float hubZ = 0f;
            foreach (MonowheelWheel w in wheels) hubZ += w.localHub.z;
            hubZ /= wheels.Count;
            if (root.InverseTransformPoint(b.center).z <= hubZ)
                throw new System.InvalidOperationException(
                    $"{variant}: the front cowl is not ahead of the wheels along the root's +Z — the import turned the model; fix the import, not this check.");
        }

        private static IEnumerable<Transform> FindRingBones(Transform root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "Bone_Ring" || t.name == "Bone_RingL" || t.name == "Bone_RingR")
                    yield return t;
        }

        // The ring is the Mesh_Ring* directly under the bone; the paddles and mounts are under it.
        private static MonowheelWheel Measure(Transform root, Transform bone)
        {
            MeshFilter ring = null;
            foreach (Transform child in bone)
                if (child.name.StartsWith("Mesh_Ring") && child.TryGetComponent(out MeshFilter mf)) ring = mf;
            if (ring == null) throw new System.InvalidOperationException($"No Mesh_Ring* under {bone.name}.");

            var bandPoints = new List<Vector3>();
            foreach (Vector3 v in ring.sharedMesh.vertices) bandPoints.Add(ring.transform.TransformPoint(v));
            Vector3 axle = MonowheelPresentationMath.PlaneNormal(bandPoints);
            Vector3 hub = Vector3.zero;
            foreach (Vector3 p in bandPoints) hub += p;
            hub /= bandPoints.Count;

            // The rolling radius is the paddles' reach, measured perpendicular to the axle.
            float radius = 0f;
            foreach (MeshFilter part in ring.GetComponentsInChildren<MeshFilter>(true))
                foreach (Vector3 v in part.sharedMesh.vertices)
                {
                    Vector3 d = part.transform.TransformPoint(v) - hub;
                    radius = Mathf.Max(radius, (d - axle * Vector3.Dot(d, axle)).magnitude);
                }

            // The lowest point of the (possibly cambered) wheel: down, projected into the ring's plane.
            Vector3 down = -root.up;
            Vector3 inPlaneDown = (down - axle * Vector3.Dot(down, axle)).normalized;
            Vector3 contact = hub + inPlaneDown * radius;

            // Sign the axle so a positive turn moves the bottom of the wheel BACKWARDS — that is
            // rolling forward. Decided numerically, so Unity's handedness is never guessed at.
            Vector3 r = contact - hub;
            Vector3 moved = Quaternion.AngleAxis(1f, axle) * r - r;
            if (Vector3.Dot(moved, root.forward) > 0f) axle = -axle;

            return new MonowheelWheel
            {
                ringBone = bone,
                localAxle = bone.InverseTransformDirection(axle).normalized,
                paddleRadius = radius,
                localContact = root.InverseTransformPoint(contact),
                localHub = root.InverseTransformPoint(hub),
            };
        }

        // ── the three layers (spec §3) ──────────────────────────────────────────

        private static ParticleSystem NewSystem(Transform root, string name, Vector3 localPos,
                                                Quaternion localRot, int cap, float minLife, float maxLife)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
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
            emission.rateOverTime = 0f;   // MonowheelPresentation drives it
            return ps;
        }

        private static ParticleSystem Spray(Transform root, string name, Vector3 contact)
        {
            // Thrown up and back off the paddles.
            Quaternion aim = Quaternion.LookRotation(new Vector3(0f, 0.75f, -0.66f));
            ParticleSystem ps = NewSystem(root, name, contact, aim, SprayCap, 0.5f, 0.8f);
            ParticleSystem.MainModule main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.startColor = new Color(SandTint.r, SandTint.g, SandTint.b, 0.9f);
            main.gravityModifier = 1f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 22f;
            shape.radius = 0.25f;
            FadeOut(ps, 0.9f);
            return ps;
        }

        private static ParticleSystem Dust(Transform root, string name, Vector3 contact, Material mat)
        {
            // Few, big, faint puffs that hang for a long time: a cloud, not a stream. Overdraw is paid per
            // pixel covered, so the size went up and the count and alpha stayed down (GDC-L1-TECH-0002).
            ParticleSystem ps = NewSystem(root, name, contact, Quaternion.identity, DustCap, 10f, 14f);
            ParticleSystem.MainModule main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(2.8f, 4.5f);
            main.startColor = new Color(SandTint.r, SandTint.g, SandTint.b, 0.3f);
            main.gravityModifier = -0.01f;   // hangs, drifting up a little
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1.5f, 0.3f, 1.5f);
            shape.position = new Vector3(0f, 1.4f, 0f);   // about half a puff up: the shader has no depth fade, so a
                                                          // puff born on the contact would cut a hard line in the sand
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 3f));
            ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 0.6f;
            FadeOut(ps, 0.3f);
            Renderer(ps, mat);
            return ps;
        }

        private static ParticleSystem Smoke(Transform root, string name, Vector3 hub, Material mat)
        {
            ParticleSystem ps = NewSystem(root, name, hub, Quaternion.LookRotation(Vector3.up), SmokeCap, 2.5f, 3.5f);
            ParticleSystem.MainModule main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startColor = new Color(SmokeTint.r, SmokeTint.g, SmokeTint.b, 0.5f);
            main.gravityModifier = -0.05f;   // rises
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 3f));
            FadeOut(ps, 0.5f);
            Renderer(ps, mat);
            return ps;
        }

        private static void FadeOut(ParticleSystem ps, float startAlpha)
        {
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(startAlpha * 0.6f, 0.5f),
                              new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        private static void Renderer(ParticleSystem ps, Material mat)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // ── self-check (spec §5) ────────────────────────────────────────────────

        // Drives each built prefab over a ground plane in a PREVIEW scene: standing still must
        // smoke but throw nothing; moving at speed must spin every ring (about its own hub) and
        // emit all three layers. Fails loudly — the jetpack once shipped smoke that never drew.
        private static void SelfCheck(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var presentation = go.GetComponent<MonowheelPresentation>();
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                SceneManager.MoveGameObjectToScene(ground, scene);
                ground.layer = LayerMask.NameToLayer("Ground");
                float lowest = float.MaxValue;
                foreach (MonowheelWheel w in presentation.Wheels)
                    lowest = Mathf.Min(lowest, go.transform.TransformPoint(w.localContact).y);
                ground.transform.position = new Vector3(0f, lowest - 0.5f + 0.05f, 0f);
                ground.transform.localScale = new Vector3(40f, 1f, 400f);
                Physics.SyncTransforms();

                const float dt = 1f / 60f;
                presentation.ResetBaseline();
                Step(presentation, 60, dt, Vector3.zero);   // 1 s: the 2/s idle smoke must show
                foreach (MonowheelWheel w in presentation.Wheels)
                {
                    Require(w.spray.particleCount == 0, prefabPath, "throws sand while standing still");
                    Require(w.smoke.particleCount > 0, prefabPath, "has no idle hub smoke");
                }

                var hubsBefore = new List<Vector3>();
                var rotBefore = new List<Quaternion>();
                foreach (MonowheelWheel w in presentation.Wheels)
                {
                    hubsBefore.Add(RingCentre(w));
                    rotBefore.Add(w.ringBone.localRotation);
                }
                Step(presentation, 60, dt, go.transform.forward * (18f * dt));   // 18 m/s for 1 s
                for (int i = 0; i < presentation.Wheels.Count; i++)
                {
                    MonowheelWheel w = presentation.Wheels[i];
                    Require(Quaternion.Angle(rotBefore[i], w.ringBone.localRotation) > 1f, prefabPath, $"{w.ringBone.name} did not spin");
                    Vector3 drift = RingCentre(w) - hubsBefore[i] - go.transform.forward * 18f;
                    Require(drift.magnitude < 0.02f, prefabPath, $"{w.ringBone.name} orbits instead of spinning ({drift.magnitude:0.000} m)");
                    Require(w.spray.particleCount > 0 && w.dust.particleCount > 0, prefabPath, $"{w.ringBone.name} threw no sand at speed");
                    Require(w.spray.GetComponent<ParticleSystemRenderer>().sharedMaterial != null, prefabPath, "spray has no material");
                }
                Debug.Log($"[Monowheel] Verified {prefab.name}: {presentation.Wheels.Count} wheel(s) spin in place and throw sand.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void Step(MonowheelPresentation p, int frames, float dt, Vector3 perFrame)
        {
            for (int f = 0; f < frames; f++)
            {
                p.transform.position += perFrame;
                Physics.SyncTransforms();
                p.Present(dt, float.NaN);   // full effect: no editor camera may LOD it away
                foreach (MonowheelWheel w in p.Wheels)
                {
                    w.spray.Simulate(dt, false, false, false);
                    w.dust.Simulate(dt, false, false, false);
                    w.smoke.Simulate(dt, false, false, false);
                }
            }
        }

        private static Vector3 RingCentre(MonowheelWheel w)
        {
            MeshFilter ring = null;
            foreach (Transform child in w.ringBone)
                if (child.name.StartsWith("Mesh_Ring") && child.TryGetComponent(out MeshFilter mf)) ring = mf;
            Vector3 sum = Vector3.zero;
            Vector3[] verts = ring.sharedMesh.vertices;
            foreach (Vector3 v in verts) sum += ring.transform.TransformPoint(v);
            return sum / verts.Length;
        }

        private static void Require(bool ok, string prefab, string what)
        {
            if (!ok) throw new System.InvalidOperationException($"[Monowheel] {prefab}: {what}.");
        }
    }
}
