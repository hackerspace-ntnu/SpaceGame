// Builds every Unity-side asset the Lightning Conjurer needs, from the exported FBX up.
//
// The FBX comes out of Blender via the rig/anim/export scripts kept beside the
// model in Assets/Game/Art/Models/Creatures/Robotic/LightningConjurer/_Source~/.
// Everything below that -- import settings, animation clips, the animator
// controller, the prefab, and the test-scene instance -- is generated here rather
// than hand-authored, for the same reason GolemBuilder and VrescalBuilder exist:
// a prefab wired by hand is a prefab nobody can rebuild after the model changes.
//
// Re-running is safe and is the intended workflow. Re-export the FBX, run this,
// and the controller, prefab and scene instance are rebuilt in place.
//
// Re-run from: Tools > Creatures > Build Lightning Conjurer
using FirstGearGames.SmoothCameraShaker;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceGame.EditorTools
{
    public static class LightningConjurerBuilder
    {
        private const string ModelDir =
            "Assets/Game/Art/Models/Creatures/Robotic/LightningConjurer";
        private const string Fbx = ModelDir + "/LightningConjurer.fbx";
        private const string ControllerDir = "Assets/Game/Art/Animations/Creatures";
        private const string ControllerPath = ControllerDir + "/LightningConjurer.controller";
        private const string PrefabDir = "Assets/Game/Prefabs/Agents/creatures";
        private const string PrefabPath = PrefabDir + "/LightningConjurer.prefab";
        private const string ScenePath = "Assets/Game/Scenes/Tests/Marius test scene.unity";
        private const string InstanceName = "LightningConjurer";
        private const string MaterialDir = "Assets/Game/Art/Materials/Palette";
        private const string ShakeDir = "Assets/Game/ScriptableObjects/Shake";
        private const string ShakeDataPath = ShakeDir + "/ConjurerFootstepShake.asset";

        /// One URP material per palette entry the model uses.
        ///
        /// These have to exist Unity-side because FBX material export is lossy: it
        /// carries a base colour and nothing else. Metallic, smoothness and above
        /// all EMISSION do not survive the trip, and the palette's own "Emissive"
        /// materials sit at emission strength 0 in palette.blend anyway - there the
        /// category records intent and hue, not glow. So the glow is authored here.
        ///
        /// Colours are the palette hex written straight as hex/255, matching
        /// DuneRat.mat (which stores 0.905882 for the #E7B345 of Mat_Hide_Sand_Pale
        /// rather than its linearised 0.799). Consistency with the project's
        /// existing materials matters more here than colour-space theory.
        private readonly struct Pal
        {
            public readonly string Name;
            public readonly int Hex;
            public readonly float Metallic, Roughness, Emission;
            public Pal(string name, int hex, float metallic, float roughness, float emission = 0f)
            {
                Name = name; Hex = hex; Metallic = metallic;
                Roughness = roughness; Emission = emission;
            }
            public Color Colour => new Color(((Hex >> 16) & 0xFF) / 255f,
                                             ((Hex >> 8) & 0xFF) / 255f,
                                             (Hex & 0xFF) / 255f, 1f);
        }

        private static readonly Pal[] Palette =
        {
            new Pal("Mat_Metal_Steel_Dark",      0x3A3E42, 1.00f, 0.45f),
            new Pal("Mat_Metal_Steel_Worn",      0x7A7D80, 1.00f, 0.55f),
            new Pal("Mat_Metal_Brass_Tarnished", 0x9C7B3F, 1.00f, 0.45f),
            new Pal("Mat_Metal_Chrome_Scuffed",  0xC9CDD2, 1.00f, 0.22f),
            new Pal("Mat_Metal_Copper_Oxide",    0x4E8C7A, 0.80f, 0.60f),
            new Pal("Mat_Neutral_Slate_Dark",    0x1F2736, 0.00f, 0.70f),
            new Pal("Mat_Neutral_Black_Matte",   0x272727, 0.00f, 0.55f),
            new Pal("Mat_Paint_White_Arctic",    0xD6DAD9, 0.35f, 0.58f),
            // The iris, the palm emitters and the halo share this one material, so
            // its intensity is a compromise: the halo is a big surface and blows
            // out long before a surface the size of the iris does. 2.0 reads as a
            // lit crystal on the halo while still carrying the eye.
            new Pal("Mat_Emissive_Portal_Blue",  0x2FB8FF, 0.00f, 0.15f, 2.0f),
        };

        // ---- Geometry, in the .blend's own units (Z up, model faces +X) --------
        // Measured off the source meshes; see the rig table in rig.py.
        private const float BlenderFloor = 2.757f;   // lowest point of both feet
        private const float BlenderTop = 37.49f;     // top of Eyelid, i.e. the body
        private const float BodyX = 0.19f;           // body centre line
        private const float BodyY = -0.06f;
        private const float BlenderBodyWidth = 9.3f;  // the head/body sphere across

        // The player model (AstronautArmature) is 3.019 m to the top of the head;
        // the brief was three times that, then doubled again to six.
        private const float PlayerHeight = 3.019f;
        private const float TargetHeight = PlayerHeight * 6f;

        /// Metres per Blender unit. Applied via ModelImporter.globalScale, NOT by
        /// scaling the armature: see ConfigureImporter.
        private static float Scale => TargetHeight / (BlenderTop - BlenderFloor);

        /// Ground speed at which the Walk clip's feet skate least, in m/s.
        ///
        /// MEASURED, not derived. A closed form over the thigh swing alone ignores
        /// the knee: the shin flexes through swing and carries the contact further
        /// back than the hip angle by itself accounts for. stride.py samples the planted
        /// foot's actual backward velocity across the stance frames and reports
        /// the mean, which is this number. Re-run it after ANY change to SW, KN or
        /// the cycle length in anim.py.
        ///
        /// Unlike the golem's clips this walk is NOT foot-locked -- there is no IK
        /// pinning a contact -- so the instantaneous speed varies over stance
        /// (measured range 6.6 to 11.5 m/s about this mean). Matching the mean
        /// minimises the skating; it does not eliminate it.
        ///
        /// This is the number to put in AgentAnimatorDriver.animatorSpeedScale as
        /// `groundSpeed / StrideSpeed` once this creature gets a motor.
        private const float StrideSpeed = 8.99f;

        private readonly struct Clip
        {
            public readonly string Name, Take;
            public readonly int First, Last;
            public Clip(string name, string take, int first, int last)
            {
                Name = name; Take = take; First = first; Last = last;
            }
        }

        private const float Fps = 30f;

        /// Frames where a foot's lowest point reaches the ground in the Walk clip,
        /// measured by _Source~/contacts.py rather than eyeballed. The two sit
        /// exactly 36 frames apart, which is half of the 72-frame cycle -- the
        /// check that the gait is actually symmetric.
        private static readonly int[] FootPlantFrames = { 7, 43 };

        // Frame ranges match the actions authored in anim.py. Both are cycles whose
        // last frame duplicates the first, so they loop without a seam. Slowed from
        // 40/90 to 72/120 frames when the creature doubled in size.
        private static readonly Clip[] Clips =
        {
            new Clip("Idle", "ConjurerRig|Idle", 1, 120),
            new Clip("Walk", "ConjurerRig|Walk", 1, 73),
        };

        /// One event per footfall, at the frame the contact actually lands.
        /// AnimationEvent.time is seconds from the clip start, and the clip starts at
        /// frame 1, hence (frame - 1) / fps.
        private static AnimationEvent[] FootPlantEvents()
        {
            return FootPlantFrames.Select(f => new AnimationEvent
            {
                time = (f - 1) / Fps,
                functionName = "OnFootPlant",
                floatParameter = 1f,
            }).ToArray();
        }

        /// A heavier, shorter shake than DamageShake: a footfall is a single vertical
        /// jolt that dies quickly, not the sustained rattle of taking a hit.
        private static void BuildShakeData()
        {
            EnsureFolder(ShakeDir);
            if (AssetDatabase.LoadAssetAtPath<ShakeData>(ShakeDataPath) != null) return;

            ShakeData data = ScriptableObject.CreateInstance<ShakeData>();
            AssetDatabase.CreateAsset(data, ShakeDataPath);

            var so = new SerializedObject(data);
            void F(string n, float v) { so.FindProperty(n).floatValue = v; }
            F("_totalDuration", 0.45f);
            F("_fadeInDuration", 0f);
            F("_fadeOutDuration", 0.35f);
            F("_magnitude", 0.8f);
            F("_magnitudeNoise", 0.15f);
            F("_roughness", 12f);
            F("_roughnessNoise", 0.2f);
            // Mostly a vertical thump, with a little roll so it does not read as a
            // pure elevator drop.
            so.FindProperty("_positionalInfluence").vector3Value = new Vector3(0.25f, 1f, 0.25f);
            so.FindProperty("_rotationalInfluence").vector3Value = new Vector3(0.3f, 0f, 0.6f);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }

        private static void SetField(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("Tools/Creatures/Build Lightning Conjurer")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Fbx) == null)
            {
                Debug.LogError($"[LightningConjurer] No FBX at {Fbx}. " +
                               "Re-export it from the .blend first.");
                return;
            }

            BuildMaterials();
            BuildShakeData();
            ConfigureImporter();
            AnimatorController controller = BuildController();
            GameObject prefab = BuildPrefab(controller);
            AddToTestScene(prefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[LightningConjurer] Built. Height {TargetHeight:0.00} m " +
                      $"(scale {Scale:0.0000}), stride speed {StrideSpeed:0.00} m/s.");
        }

        /// Creates or updates a URP material per palette entry, in a shared folder
        /// so a later model using the same palette entry reuses the asset rather
        /// than minting a second copy of the same grey.
        private static void BuildMaterials()
        {
            EnsureFolder(MaterialDir);
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Debug.LogError("[LightningConjurer] URP Lit shader not found.");
                return;
            }

            foreach (Pal p in Palette)
            {
                string path = $"{MaterialDir}/{p.Name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool isNew = mat == null;
                if (isNew) mat = new Material(lit);
                else mat.shader = lit;

                mat.SetColor("_BaseColor", p.Colour);
                mat.SetFloat("_Metallic", p.Metallic);
                mat.SetFloat("_Smoothness", 1f - p.Roughness);   // URP is smoothness, palette is roughness

                if (p.Emission > 0f)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    mat.SetColor("_EmissionColor", p.Colour * p.Emission);
                }
                else
                {
                    mat.DisableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                    mat.SetColor("_EmissionColor", Color.black);
                }

                if (isNew) AssetDatabase.CreateAsset(mat, path);
                else EditorUtility.SetDirty(mat);
            }
            AssetDatabase.SaveAssets();
        }

        private static void ConfigureImporter()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(Fbx);

            // Generic, not Humanoid. The conjurer is a two-legged sphere with two
            // detached, free-floating arms and no torso or spine to speak of;
            // there is no humanoid bone map that survives contact with it.
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importNormals = ModelImporterNormals.Import;

            // The FBX is written in Blender's own Z-up axes and Unity is asked to
            // bake the Z-up -> Y-up conversion into the data.
            //
            // This is load-bearing. Every part of this model is a rigid mesh
            // bone-parented to the skeleton rather than skinned, and for that kind
            // of rig Unity discards the armature node's own transform. Putting the
            // conversion (or the metre scale) on the armature in Blender therefore
            // survives in the animation curves but vanishes from the bind pose --
            // the creature stands correctly only while a clip is playing and
            // collapses the moment one stops. GolemBuilder hit exactly this and
            // documents it; the export script leaves the armature at identity for
            // the same reason.
            importer.bakeAxisConversion = true;

            // Metre scale belongs here, for the same reason: globalScale is applied
            // to the bind pose and the curves alike. Unit conversion stays ON and
            // the scale factor rides on top of it -- the combination the rest of the
            // project's models use (ostrich_rigged imports at globalScale 0.13742
            // with useFileUnits 1).
            importer.useFileScale = true;
            importer.globalScale = Scale;

            // 52 separate parts bone-parented to the skeleton, so they exist as real
            // child transforms. Optimising the hierarchy away would delete the very
            // transforms the clips animate and the creature would import as a
            // motionless pile of components.
            importer.optimizeGameObjects = false;
            importer.optimizeBones = false;

            importer.clipAnimations = Clips.Select(c => new ModelImporterClipAnimation
            {
                name = c.Name,
                takeName = c.Take,
                events = c.Name == "Walk" ? FootPlantEvents() : new AnimationEvent[0],
                firstFrame = c.First,
                lastFrame = c.Last,
                loopTime = true,
                loopPose = true,
                wrapMode = WrapMode.Loop,
                keepOriginalPositionY = true,
                keepOriginalPositionXZ = true,
                keepOriginalOrientation = true,
                lockRootRotation = true,
                lockRootHeightY = true,
                lockRootPositionXZ = true,
            }).ToArray();

            // Point every material slot in the FBX at the authored URP asset. The
            // key is the material NAME as Blender wrote it, which is the palette
            // name because the .blend links its materials straight from
            // palette.blend rather than making local copies.
            int remapped = 0;
            foreach (Pal p in Palette)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/{p.Name}.mat");
                if (mat == null) continue;
                importer.AddRemap(
                    new AssetImporter.SourceAssetIdentifier(typeof(Material), p.Name), mat);
                remapped++;
            }
            Debug.Log($"[LightningConjurer] Remapped {remapped} materials.");

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
        }

        private static AnimationClip FindClip(string name)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(Fbx)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == name);
            if (clip == null)
                Debug.LogError($"[LightningConjurer] Clip '{name}' missing from the FBX.");
            return clip;
        }

        private static AnimatorController BuildController()
        {
            EnsureFolder(ControllerDir);
            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller =
                AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            // These names are AgentAnimatorDriver's, verbatim, misspellings and all:
            // it calls SetFloat/SetBool on them unconditionally and a parameter it
            // cannot find is a warning every frame. They are here so this creature
            // can be dropped onto an AgentController later without a second pass.
            controller.AddParameter("SpeedX", AnimatorControllerParameterType.Float);
            controller.AddParameter("SpeedY", AnimatorControllerParameterType.Float);
            controller.AddParameter("FallSpeed", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsImmobalized", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsAiming", AnimatorControllerParameterType.Bool);

            var tree = new BlendTree
            {
                name = "Locomotion",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "SpeedY",
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.AddChild(FindClip("Idle"), 0f);
            tree.AddChild(FindClip("Walk"), StrideSpeed);

            AnimatorStateMachine root = controller.layers[0].stateMachine;
            AnimatorState locomotion = root.AddState("Locomotion");
            locomotion.motion = tree;
            root.defaultState = locomotion;

            // There is no motor on this prefab yet, so nothing writes SpeedY at
            // runtime. Defaulting it to the stride speed means dropping the prefab
            // in a scene and pressing play shows the walk cycle rather than a
            // creature standing still. An AgentAnimatorDriver overwrites this every
            // frame once one is attached, so it costs nothing later.
            AnimatorControllerParameter[] ps = controller.parameters;
            foreach (AnimatorControllerParameter p in ps)
            {
                if (p.name == "SpeedY") p.defaultFloat = StrideSpeed;
                if (p.name == "IsGrounded") p.defaultBool = true;
            }
            controller.parameters = ps;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static GameObject BuildPrefab(AnimatorController controller)
        {
            EnsureFolder(PrefabDir);
            var root = new GameObject(InstanceName);

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            model.transform.SetParent(root.transform, false);

            // The model is built facing Blender +X, which lands on Unity +X. Yaw the
            // model child so the prefab ROOT's forward (+Z) is the creature's
            // forward -- that is the axis every motor and facing module works in.
            //
            // This lives on a child rather than being baked into the mesh data
            // because baking it would mean rotating the artist's geometry in the
            // .blend, and because a visible -90 on a transform is something anyone
            // can find and correct later.
            model.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);

            // Setting localRotation from script leaves m_LocalEulerAnglesHint at zero.
            // The quaternion is what renders, so the model looks right either way, but
            // the Inspector reads the hint to decide which of the equivalent Euler
            // triples to show -- leave it and the rotation field can read (0,0,0) on a
            // transform that is visibly yawed, which is exactly the kind of thing
            // someone later "fixes" by dragging it back.
            var hint = new SerializedObject(model.transform);
            hint.FindProperty("m_LocalEulerAnglesHint").vector3Value = new Vector3(0f, -90f, 0f);
            hint.ApplyModifiedPropertiesWithoutUndo();

            // Drop the body's centre-bottom onto the prefab origin. Blender (x,y,z)
            // imports as Unity (x, z, -y) once bakeAxisConversion has run.
            var footInModel = new Vector3(BodyX, BlenderFloor, -BodyY) * Scale;
            model.transform.localPosition = -(model.transform.localRotation * footInModel);

            Animator animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;

            // The motor owns movement, never the clip.
            animator.applyRootMotion = false;

            // Required for this rig specifically. It is 52 bone-parented renderers
            // rather than one skinned mesh, so Unity culls it against bind-pose
            // bounds that do not follow the animation; with the default culling mode
            // it freezes mid-stride whenever it thinks it is off screen.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = TargetHeight;
            capsule.radius = BlenderBodyWidth * Scale * 0.5f;   // tracks the model, not a magic number
            capsule.center = new Vector3(0f, TargetHeight * 0.5f, 0f);

            // Footstep camera shake, driven by animation events on the Walk clip.
            var footstep = root.AddComponent<Presentation.FootstepCameraShake>();
            var shake = AssetDatabase.LoadAssetAtPath<ShakeData>(ShakeDataPath);
            if (shake != null) SetField(footstep, "shakeData", shake);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        private static void AddToTestScene(GameObject prefab)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[LightningConjurer] Scene save declined; prefab " +
                                 "built but not added to the test scene.");
                return;
            }

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.isLoaded)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // Idempotent: replace a previous instance rather than stacking copies.
            GameObject existing = scene.GetRootGameObjects()
                .FirstOrDefault(g => g.name == InstanceName);
            if (existing != null) Object.DestroyImmediate(existing);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = InstanceName;

            // Clear of the Artifact at the origin, well inside the ground plane,
            // and facing +Z towards MovementCamera.
            instance.transform.SetPositionAndRotation(new Vector3(8f, 0f, 0f),
                                                      Quaternion.identity);

            // Without a CameraShaker in the scene the footstep events fire into
            // nothing: CameraShakerHandler.Shake returns null when there is no
            // default shaker, silently. The real player camera prefab
            // ("Assets/Game/Prefabs/Camera/3rd person.prefab") already carries one,
            // but this test scene has plain cameras, so give one a shaker here.
            GameObject[] roots = scene.GetRootGameObjects();
            Camera cam = roots.Select(g => g.GetComponentInChildren<Camera>(true))
                              .FirstOrDefault(c => c != null && c.name == "MovementCamera")
                        ?? roots.Select(g => g.GetComponentInChildren<Camera>(true))
                                .FirstOrDefault(c => c != null);
            if (cam != null && cam.GetComponent<CameraShaker>() == null)
            {
                cam.gameObject.AddComponent<CameraShaker>();
                Debug.Log($"[LightningConjurer] Added a CameraShaker to '{cam.name}' " +
                          "so the footstep shake is visible in the test scene.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string[] parts = path.Split('/');
            string built = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = built + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(built, parts[i]);
                built = next;
            }
        }
    }
}
