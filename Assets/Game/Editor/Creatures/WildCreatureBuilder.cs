// Turns an exported creature (FBX + <Name>.creature.json manifest, both written by the Blender
// creature pipeline) into a wild-animal agent prefab that walks, runs, grazes, fights back, dies,
// blinks, saves and replicates.
//
// Input, per creature, in Assets/Game/Art/Models/Creatures/Organic/<Name>/:
//   <snake>.fbx                 one skinned body + eyeballs, an armature, every clip as a take
//                               "Arm_<Name>|<Name>_<Slot>" (exported through _exportlib)
//   Textures/*.png              the painted coat
//   <Name>.creature.json        clips (slot -> take, loop), stride speeds measured off the feet,
//                               bounds, eyeballs (centre, gaze, radius) and the eye colours
//
// Output:
//   Assets/Game/Art/Animations/Creatures/<Name>.overrideController   the creature's clips on Esko.controller
//   Assets/Game/ScriptableObjects/Eyes/Creatures/<Name>.asset        its EyeStyle (baked by StylizedEyeBuilder)
//   Assets/Game/Prefabs/Agents/creatures/Wild/<Name>.prefab          the agent
//
// ## Why Esko
//
// Esko.prefab is the finished peaceful-until-provoked animal: perception, wander, flee or fight,
// provocation, ragdoll, savers and netcode, all tuned and play-tested. A wild creature is that stack
// minus the mount (MountModule, SteerModule, saddle and rider-pose components), so the builder copies
// the Esko's root components field for field instead of describing them a second time here. Its
// controller already carries every parameter and trigger those modules fire (Hurt, Death, Roar, Ram,
// Happy, IsGrazing), so each creature only overrides the clips.
//
// ## Speeds
//
// The manifest's stride is the speed a planted foot slides under the body in the in-place clip, so
// moving the body at exactly that speed leaves the feet planted: NavMeshAgent.speed = run stride,
// walkSpeedMultiplier = walk / run, animatorSpeedScale = 1. Esko.controller's Locomotion tree plays
// Walk from SpeedY 1.6 to 3.3 and Run at 15, so animationSpeedMultiplier maps the run speed to 15
// and walkAnimBoost lands the walk at 2.4.
//
// ## Create-only
//
// A prefab that exists is never rebuilt: prefabs are authored assets and may carry hand edits. The
// import settings, override controller and eye style ARE refreshed every run, so a re-exported FBX
// reaches an existing prefab. Delete a prefab to rebuild it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Core.Persistence.EditorTools;
using SpaceGame.Gameplay.Ragdoll;
using Newtonsoft.Json.Linq;
using SpaceGame.Presentation;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.EditorTools
{
    public static class WildCreatureBuilder
    {
        private const string ModelRoot = "Assets/Game/Art/Models/Creatures/Organic";
        private const string TemplatePrefab = "Assets/Game/Prefabs/Agents/creatures/Esko.prefab";
        private const string TemplateController = "Assets/Game/Art/Animations/Creatures/Esko.controller";
        private const string ControllerFolder = "Assets/Game/Art/Animations/Creatures";
        private const string PrefabFolder = "Assets/Game/Prefabs/Agents/creatures/Wild";
        private const string EyeStyleFolder = "Assets/Game/ScriptableObjects/Eyes/Creatures";
        private const string CatchlightDonorStyle = "Amber";

        // Esko.controller thresholds on SpeedY: Walk plays from 1.6 to 3.3, Run at 15.
        private const float WalkParam = 2.4f;
        private const float RunParam = 15f;
        // Below this a measured stride is noise (a hop whose feet never slide) and a size-based guess is used.
        private const float MinimumStride = 0.2f;
        // Esko's ragdoll mass is tuned for its 1.86 m body; others scale with volume.
        private const float TemplateHeight = 1.86f;

        // Mount and saddle parts of the template: a wild creature is not rideable.
        private static readonly HashSet<string> NotCloned = new HashSet<string>
        {
            "MountModule", "SteerModule", "SaddleSocket", "SaddleQuickRelease", "SaddleSaveable",
            "MountSaveable", "MountNetworkSync", "MountedRiderPose",
        };

        // Esko.controller clip -> the creature's slots that may play it, best first. A candidate is
        // skipped if it does not move the skeleton: some donors' "Idle" is a held pose (the mallard's),
        // and a creature standing on it looks frozen.
        private static readonly (string esko, string[] slots)[] ClipMap =
        {
            ("Esko_Idle", new[] { "Idle", "IdleTailWag", "IdleRuffle", "Ruffle", "TailWag", "IdleShake", "Stand", "AlertIdle" }),
            ("Esko_Walk", new[] { "Walk", "Hop", "Trot", "Idle" }),
            ("Esko_Run", new[] { "Run", "Trot", "Sprint", "HopFast", "Hop", "Walk" }),
            ("Esko_TurnL", new[] { "TurnL", "WalkL", "Walk", "Hop" }),
            ("Esko_TurnR", new[] { "TurnR", "WalkR", "Walk", "Hop" }),
            ("Esko_Graze", new[] { "Graze", "Eat", "Drink", "Idle", "IdleTailWag", "Ruffle", "TailWag", "IdleShake" }),
            ("Esko_Happy", new[] { "Shake", "IdleShake", "Groom", "Alert", "Idle" }),
            ("Esko_Roar", new[] { "Roar", "Bark", "Howl", "Taunt", "Alert", "Attack", "IdleShake", "Idle" }),
            ("Esko_Ram", new[] { "Attack", "AttackDouble", "AttackL", "AttackLeft", "Bite", "Roar", "Alert", "IdleShake", "Idle" }),
            ("Esko_Hurt", new[] { "Hurt", "HurtR", "HurtL", "HurtLeft", "HurtBack", "Alert", "Idle" }),
            // The ragdoll takes over at death; a creature without a death clip falls through to a flinch.
            ("Esko_Death", new[] { "Death", "DeathR", "DeathAir", "HurtBack", "Hurt", "Idle" }),
            ("Esko_Jump", new[] { "Jump", "Run", "Walk" }),
        };

        // Played by a looping Esko state, so they loop whatever the manifest says.
        private static readonly HashSet<string> AlwaysLoop = new HashSet<string>
        {
            "IdleTailWag", "IdleRuffle", "Ruffle", "TailWag", "Hover", "Fly", "FlyFast",
        };

        private class Manifest
        {
            public string name;
            public string fbx;
            public float height, length, width;
            public List<Clip> clipList = new List<Clip>();
            public Dictionary<string, float> stride = new Dictionary<string, float>();
            public List<Eye> eyes = new List<Eye>();
            public EyeColours eyeStyle = new EyeColours();
        }

        private class Clip { public string slot, take; public bool loop; }
        private class Eye { public string obj; public Vector3 centre, gaze; public float radius; }
        private class EyeColours
        {
            public string sclera = "#e9e2d2", iris = "#c07a2a", pupilColour = "#0b0806", pupil = "round";
            public float pupilSize = 0.5f, irisSize = 0.62f;
        }

        [MenuItem("Tools/SpaceGame/Creatures/Build Wild Creatures")]
        public static void BuildAll()
        {
            var manifests = LoadManifests();
            int built = 0, refreshed = 0;
            foreach (var m in manifests)
            {
                if (!ConfigureImport(m)) continue;
                var controller = BuildOverrideController(m);
                if (controller == null) continue;
                var eyeMaterial = BuildEyeMaterial(m);
                string path = $"{PrefabFolder}/{m.name}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                    refreshed++;
                else if (BuildPrefab(m, controller, eyeMaterial, path))
                    built++;
                StampPrefabId(path);
            }
            // Only the list sync is project-wide, and it only adds entries. The ragdoll and save
            // wiring passes are NOT run: they rewrite every prefab in the project that lacks wiring
            // (re-serialising unrelated prefabs), and this builder's prefabs get theirs directly
            // (AgentRagdoll/RagdollRig and the savers come with the Esko's stack, SaveablePolicy
            // fills any gap, StampPrefabId writes the id).
            Debug.Log($"[WildCreatureBuilder] {NetworkPrefabRegistrar.Sync(out int added, out int total)}");
            Debug.Log($"[WildCreatureBuilder] {manifests.Count} manifests: {built} prefabs built, {refreshed} existing " +
                      $"(import + controller + eyes refreshed). Run Tools > SpaceGame > Creatures > Verify Wild Creatures.");
        }

        // ------------------------------------------------------------------ manifests

        private static List<Manifest> LoadManifests()
        {
            var list = new List<Manifest>();
            foreach (string file in Directory.GetFiles(ModelRoot, "*.creature.json", SearchOption.AllDirectories))
            {
                var m = Parse(File.ReadAllText(file));
                if (string.IsNullOrEmpty(m.name) || string.IsNullOrEmpty(m.fbx))
                    throw new InvalidDataException($"{file}: manifest has no name or fbx.");
                list.Add(m);
            }
            return list;
        }

        private static Manifest Parse(string json)
        {
            var root = JObject.Parse(json);
            var m = new Manifest
            {
                name = (string)root["name"],
                fbx = (string)root["fbx"],
                height = (float)root["height"],
                length = (float)root["length"],
                width = (float)root["width"],
            };
            foreach (var kv in (JObject)root["clips"])
                m.clipList.Add(new Clip { slot = kv.Key, take = (string)kv.Value["take"], loop = (bool)kv.Value["loop"] });
            foreach (var kv in (JObject)root["stride"])
                m.stride[kv.Key] = (float)kv.Value;
            foreach (JObject e in (JArray)root["eyes"])
                m.eyes.Add(new Eye { obj = (string)e["object"], centre = Vec(e["centre"]), gaze = Vec(e["gaze"]), radius = (float)e["radius"] });
            if (root["eye_style"] is JObject st)
            {
                m.eyeStyle.sclera = (string)st["sclera"] ?? m.eyeStyle.sclera;
                m.eyeStyle.iris = (string)st["iris"] ?? m.eyeStyle.iris;
                m.eyeStyle.pupilColour = (string)st["pupil_color"] ?? m.eyeStyle.pupilColour;
                m.eyeStyle.pupil = (string)st["pupil"] ?? m.eyeStyle.pupil;
                m.eyeStyle.pupilSize = (float?)st["pupil_size"] ?? m.eyeStyle.pupilSize;
                m.eyeStyle.irisSize = (float?)st["iris_size"] ?? m.eyeStyle.irisSize;
            }
            return m;
        }

        // Blender (x, y, z) arrives in Unity as (-x, z, -y): front -Y -> +Z, left +X -> -X.
        private static Vector3 Vec(JToken t) => new Vector3(-(float)t[0], (float)t[2], -(float)t[1]);

        // ------------------------------------------------------------------ import + animator

        private static bool ConfigureImport(Manifest m)
        {
            var importer = AssetImporter.GetAtPath(m.fbx) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[WildCreatureBuilder] {m.name}: {m.fbx} is not imported.");
                return false;
            }
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.materialName = ModelImporterMaterialName.BasedOnTextureName;
            importer.materialSearch = ModelImporterMaterialSearch.RecursiveUp;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.optimizeGameObjects = false;
            importer.importAnimation = true;

            var bySlot = m.clipList.ToDictionary(c => c.take, c => c);
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                string action = clip.takeName.Substring(clip.takeName.IndexOf('|') + 1);
                if (!bySlot.TryGetValue(action, out Clip spec)) continue;
                clip.name = spec.slot;
                clip.loopTime = spec.loop || AlwaysLoop.Contains(spec.slot);
                clip.loopPose = false;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            return true;
        }

        private static AnimatorOverrideController BuildOverrideController(Manifest m)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(m.fbx).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name, c => c);
            var missing = m.clipList.Where(c => !clips.ContainsKey(c.slot)).Select(c => c.slot).ToList();
            if (missing.Count > 0)
            {
                // SaveAndReimport does not always make new clips readable in the same run (ArtPipeline Gotchas).
                Debug.LogError($"[WildCreatureBuilder] {m.name}: clips {string.Join(", ", missing)} not readable yet. " +
                               "Run the build again.");
                return null;
            }

            var template = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(TemplateController);
            string path = $"{ControllerFolder}/{m.name}.overrideController";
            var aoc = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
            if (aoc == null)
            {
                aoc = new AnimatorOverrideController(template) { name = m.name };
                AssetDatabase.CreateAsset(aoc, path);
            }
            aoc.runtimeAnimatorController = template;
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            aoc.GetOverrides(overrides);
            var model = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(m.fbx));
            try
            {
                for (int i = 0; i < overrides.Count; i++)
                {
                    var original = overrides[i].Key;
                    var row = ClipMap.FirstOrDefault(r => r.esko == original.name);
                    AnimationClip pick = null, firstPresent = null;
                    if (row.slots != null)
                        foreach (string slot in row.slots)
                        {
                            if (!clips.TryGetValue(slot, out AnimationClip candidate)) continue;
                            if (firstPresent == null) firstPresent = candidate;
                            if (MovesSkeleton(candidate, model))
                            {
                                pick = candidate;
                                break;
                            }
                        }
                    if (pick == null && firstPresent != null)
                    {
                        Debug.LogWarning($"[WildCreatureBuilder] {m.name}: no moving clip for {original.name}; '{firstPresent.name}' is a held pose.");
                        pick = firstPresent;
                    }
                    if (pick == null)
                        Debug.LogWarning($"[WildCreatureBuilder] {m.name}: nothing to play for {original.name}; it keeps the Esko clip.");
                    overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(original, pick);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(model);
            }
            aoc.ApplyOverrides(overrides);
            EditorUtility.SetDirty(aoc);
            AssetDatabase.SaveAssets();
            return aoc;
        }

        // True if the clip turns some bone of this skeleton by more than half a degree between its
        // first frame and its middle. False for a held pose and for a clip bound to another skeleton.
        private static bool MovesSkeleton(AnimationClip clip, GameObject model) =>
            clip.length >= 0.1f && ClipMotion.MaxBoneTurn(clip, model, 0f, clip.length * 0.5f) > ClipMotion.StillDegrees;

        // ------------------------------------------------------------------ eyes

        private static Material BuildEyeMaterial(Manifest m)
        {
            if (m.eyes.Count == 0) return null;
            EnsureFolder(EyeStyleFolder);
            string path = $"{EyeStyleFolder}/{m.name}.asset";
            var style = AssetDatabase.LoadAssetAtPath<EyeStyle>(path);
            if (style == null)
            {
                style = ScriptableObject.CreateInstance<EyeStyle>();
                AssetDatabase.CreateAsset(style, path);
            }
            var e = m.eyeStyle;
            Color iris = Hex(e.iris);
            style.Sclera = Hex(e.sclera);
            style.IrisOuter = iris;
            style.IrisInner = Color.Lerp(iris, Color.white, 0.25f);
            style.LimbalRing = Color.Lerp(iris, Color.black, 0.75f);
            style.Pupil = Hex(e.pupilColour);
            float irisDeg = Mathf.Clamp(e.irisSize * 70f, 10f, 80f);
            style.Iris = EyeShape.Round(irisDeg);
            float pupilDeg = Mathf.Clamp(e.pupilSize * irisDeg, 2f, irisDeg - 1f);
            var pupil = EyeShape.Round(pupilDeg);
            if (e.pupil == "slit") pupil.Aspect = 0.25f;
            else if (e.pupil == "bar") pupil.Aspect = 2.6f;
            style.PupilShape = pupil;
            style.EmissionStrength = 0f;
            var donor = StylizedEyeBuilder.LoadStyles().FirstOrDefault(s => s.name == CatchlightDonorStyle);
            if (donor != null)
                style.Catchlights = new List<EyeCatchlight>(donor.Catchlights);
            EditorUtility.SetDirty(style);
            AssetDatabase.SaveAssets();
            return StylizedEyeBuilder.Build(style);
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out Color c);
            return c;
        }

        // ------------------------------------------------------------------ prefab

        // The save system finds a runtime-spawned prefab by this id; it must be the prefab's own
        // GUID, written into the file (SaveableWiring.StampPrefabId explains why OnValidate is not enough).
        private static void StampPrefabId(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var entity = prefab != null ? prefab.GetComponent<SaveableEntity>() : null;
            if (entity == null) return;
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (SaveablePrefabFile.ReadPrefabId(path) == guid) return;
            var so = new SerializedObject(entity);
            so.FindProperty("prefabId").stringValue = guid;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SavePrefabAsset(prefab);
        }

        private static bool BuildPrefab(Manifest m, AnimatorOverrideController controller, Material eyeMaterial, string path)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(m.fbx);
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePrefab);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                root.name = m.name;
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                // Unity's fake-null: GetComponent returns a placeholder that == null but is not a C# null, so ?? never adds.
                var animator = root.GetComponent<Animator>();
                if (animator == null) animator = root.AddComponent<Animator>();
                EditorUtility.CopySerialized(template.GetComponent<Animator>(), animator);
                animator.avatar = AssetDatabase.LoadAllAssetsAtPath(m.fbx).OfType<Avatar>().FirstOrDefault();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                // NetworkObject and SaveableEntity are FRESH, never copied: a copied one carries the
                // template's identity (GlobalObjectIdHash, prefab id) and two prefabs would share it.
                root.AddComponent<NetworkObject>();
                CloneComponents(template, root);
                root.AddComponent<SaveableEntity>();

                Bounds b = BodyBounds(root, m.name);
                float run = Stride(m, "Run", "Trot", "Sprint", "HopFast");
                float walk = Stride(m, "Walk", "Hop");
                if (run <= 0f) run = Froude(b.size.y, 1.2f);
                if (walk <= 0f) walk = Froude(b.size.y, 0.3f);
                // A run measured slower than the walk is a mis-measure (feet that barely slide in a
                // bounding gait), not a slow animal: keep the walk, which the feet do confirm.
                if (walk >= run) run = walk * 2.2f;

                var box = root.GetComponent<BoxCollider>();
                box.center = b.center;
                box.size = new Vector3(b.size.x * 0.8f, b.size.y * 0.9f, b.size.z * 0.85f);

                var agent = root.GetComponent<NavMeshAgent>();
                agent.radius = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * 0.5f, 0.15f, 4f);
                agent.height = b.size.y;
                agent.baseOffset = 0f;
                agent.speed = run;

                SerializedFields.Edit(root.GetComponent<NavMeshAgentMotor>(), so =>
                    SerializedFields.SetFloat(so, "walkSpeedMultiplier", Mathf.Clamp(walk / run, 0.01f, 1f)));
                float multiplier = RunParam / run;
                SerializedFields.Edit(root.GetComponent<AgentAnimatorDriver>(), so =>
                {
                    SerializedFields.Set(so, "animator", animator);
                    SerializedFields.SetFloat(so, "animationSpeedMultiplier", multiplier);
                    SerializedFields.SetFloat(so, "walkAnimBoost", WalkParam / (walk * multiplier));
                    SerializedFields.SetFloat(so, "animatorSpeedScale", 1f);
                    SerializedFields.SetFloat(so, "measuredRunSpeed", (walk + run) * 0.5f);
                });
                var rig = root.GetComponent<RagdollRig>();
                if (rig != null)
                    SerializedFields.Edit(rig, so => SerializedFields.SetFloat(so, "totalMass",
                        70f * Mathf.Pow(b.size.y / TemplateHeight, 3f)));

                WireEyes(m, root, eyeMaterial);
                SaveablePolicy.Ensure(root, out _);

                EnsureFolder(PrefabFolder);
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
                Debug.Log($"[WildCreatureBuilder] {m.name}: {(ok ? "built" : "FAILED")} {path} — walk {walk:0.00} m/s, " +
                          $"run {run:0.00} m/s, {b.size.y:0.00} m tall, {m.eyes.Count} eyes");
                return ok;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void CloneComponents(GameObject template, GameObject root)
        {
            foreach (var source in template.GetComponents<Component>())
            {
                Type type = source.GetType();
                if (source is Transform || source is Animator || source is NetworkObject || source is SaveableEntity) continue;
                if (NotCloned.Contains(type.Name)) continue;
                var target = root.GetComponent(type);
                if (target == null) target = root.AddComponent(type);
                EditorUtility.CopySerialized(source, target);
            }
            // References into the template now point at the Esko asset: move each to the same-typed
            // component on the new root, or clear it if it pointed below the template's root.
            foreach (var component in root.GetComponents<Component>())
            {
                if (component is Transform) continue;
                var so = new SerializedObject(component);
                var it = so.GetIterator();
                bool changed = false;
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                    var target = it.objectReferenceValue;
                    if (AssetDatabase.GetAssetPath(target) != TemplatePrefab) continue;
                    UnityEngine.Object mapped = null;
                    if (target is GameObject go && go.transform.parent == null) mapped = root;
                    else if (target is Component c && c.transform.parent == null) mapped = root.GetComponent(c.GetType());
                    it.objectReferenceValue = mapped;
                    changed = true;
                }
                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void WireEyes(Manifest m, GameObject root, Material eyeMaterial)
        {
            if (eyeMaterial == null) return;
            var renderers = root.GetComponentsInChildren<Renderer>(true).ToDictionary(r => r.name, r => r);
            foreach (var eye in m.eyes)
            {
                if (!renderers.TryGetValue(eye.obj, out Renderer r))
                {
                    Debug.LogError($"[WildCreatureBuilder] {m.name}: eye '{eye.obj}' is not in the FBX.");
                    continue;
                }
                r.sharedMaterial = eyeMaterial;
                Vector3 gaze = r.transform.InverseTransformDirection(eye.gaze);
                Vector3 up = r.transform.InverseTransformDirection(Vector3.up);
                StylizedEyeBuilder.EnsureEyeMesh(r, gaze, up, $"{m.name}_{eye.obj}");
            }
            EyelidWiring.Ensure(root);
        }

        private static Bounds BodyBounds(GameObject root, string name)
        {
            var body = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == $"Mesh_{name}")
                       ?? root.GetComponentsInChildren<SkinnedMeshRenderer>(true).First();
            body.updateWhenOffscreen = false;
            Bounds world = body.bounds;
            return new Bounds(root.transform.InverseTransformPoint(world.center), world.size);
        }

        private static float Stride(Manifest m, params string[] slots)
        {
            foreach (string s in slots)
                if (m.stride.TryGetValue(s, out float v) && v >= MinimumStride) return v;
            return 0f;
        }

        // Dynamically similar gait speed for a body of this height: v = sqrt(froude * g * hipHeight).
        private static float Froude(float height, float froude) => Mathf.Sqrt(froude * 9.81f * height * 0.55f);

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ------------------------------------------------------------------ verification

        [MenuItem("Tools/SpaceGame/Creatures/Verify Wild Creatures")]
        public static void VerifyAll()
        {
            int failures = 0;
            foreach (var m in LoadManifests())
            {
                var problems = Verify(m);
                failures += problems.Count;
                Debug.Log(problems.Count == 0
                    ? $"[WildCreatureBuilder] {m.name}: OK"
                    : $"[WildCreatureBuilder] {m.name}: {string.Join("; ", problems)}");
            }
            Debug.Log($"[WildCreatureBuilder] verify: {failures} problem(s).");
        }

        public static List<string> VerifyByName(string name) =>
            Verify(LoadManifests().First(x => x.name == name));

        private static List<string> Verify(Manifest m)
        {
            var problems = new List<string>();
            string path = $"{PrefabFolder}/{m.name}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { problems.Add("no prefab"); return problems; }
            var animator = prefab.GetComponent<Animator>();
            if (!(animator.runtimeAnimatorController is AnimatorOverrideController aoc)) problems.Add("no override controller");
            else
            {
                var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                aoc.GetOverrides(overrides);
                foreach (var o in overrides.Where(o => o.Value == null)) problems.Add($"{o.Key.name} not overridden");
            }
            if (animator.avatar == null || !animator.avatar.isValid) problems.Add("avatar invalid");
            if (prefab.GetComponent<NetworkObject>() == null) problems.Add("no NetworkObject");
            if (!NetworkPrefabRegistrar.IsRegistered(path)) problems.Add("not in the network prefab list");
            var saveable = prefab.GetComponent<SaveableEntity>();
            if (saveable == null || string.IsNullOrEmpty(saveable.PrefabId)) problems.Add("no saveable prefab id");
            if (m.eyes.Count > 0 && prefab.GetComponent<EyeBlink>() == null) problems.Add("no EyeBlink");
            foreach (string mount in NotCloned)
                if (prefab.GetComponents<Component>().Any(c => c != null && c.GetType().Name == mount)) problems.Add($"has {mount}");
            if (prefab.GetComponents<Component>().Any(c => c == null)) problems.Add("missing script");

            // every clip the animator plays must move bones on THIS skeleton, or it bound to nothing
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var played = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                if (animator.runtimeAnimatorController is AnimatorOverrideController playing)
                    playing.GetOverrides(played);
                foreach (var clip in played.Select(p => p.Value).Where(c => c != null).Distinct())
                {
                    float moved = ClipMotion.MaxBoneTurn(clip, instance, 0f, clip.length * 0.5f);
                    if (clip.length > 0.1f && moved < ClipMotion.StillDegrees) problems.Add($"clip {clip.name} moves nothing");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
            return problems;
        }
    }
}
