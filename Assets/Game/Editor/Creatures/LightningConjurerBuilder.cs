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
// ---- the legs are IK, not animation ------------------------------------
//
// This creature does NOT walk on its baked Walk clip. It walks on the project's
// procedural IK locomotion (Assets/Game/Scripts/Locomotion), through
// ConjurerLocomotion + ConjurerDriver, exactly as the ostrich, the horse, the crab
// and the humanoid robot do.
//
// That was a deliberate swap and it cost a re-rig. The baked walk was never
// foot-locked -- _Source~/stride.py measures the planted foot sliding across a
// range of 6.6 to 11.5 m/s about its mean, which is the skating you cannot tune
// out of a clip that has no idea where the ground is. LeggedLocomotion chooses a
// foothold against the real ground each step, so the feet stay where they are put,
// on terrain, on slopes, at any speed the driver asks for.
//
// The rig had to change to be discoverable at all: WalkerRig finds limbs by the
// names Coxa_/Hip_/Knee_/Ankle_/Foot_ and measures every hinge off a modelled pin.
// _Source~/walkerize.py does that conversion and verifies it before saving. The
// cold-start order is now rig.py -> walkerize.py -> anim.py -> export.py.
//
// The Idle clip is still used, and still comes from anim.py. It drives the halo,
// the eyelid, the floating arms and the hover -- everything that is NOT a leg. The
// Walk clip is still imported but nothing plays it; it is kept because it is the
// one-line fallback if the IK ever has to be backed out.
//
// Re-run from: Tools > Creatures > Build Lightning Conjurer
using FirstGearGames.SmoothCameraShaker;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Creatures;
using SpaceGame.Creatures.Conjurer;
using SpaceGame.Locomotion;

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
        private const string FactionDir = "Assets/Game/ScriptableObjects/Factions/Core";
        private const string RobotFactionPath = FactionDir + "/RobotFaction.asset";
        private const string RelationshipsPath = FactionDir + "/GlobalRelationships.asset";

        /// How close a player must come before the creature wakes up, in metres.
        ///
        /// This is the whole of the brief's first half and it is a SMALL number for a
        /// creature 18.1 m tall -- the player is inside the thing's own footprint before it
        /// reacts, close enough to look up at it. That is the intended effect: it reads as
        /// something inert that you walk up to and disturb, not as a sentry with a picket
        /// line. Raise it if the creature should notice you across a clearing instead.
        private const float ActivationRange = 10f;

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

        /// Ground speed at which the Walk clip's feet skated least, in m/s.
        ///
        /// HISTORICAL. Nothing reads it any more: the legs are solved by
        /// ConjurerLocomotion, which derives its own top speed from the measured
        /// stride and cadence rather than from a clip. Kept because it is the
        /// number that made the case for the swap -- _Source~/stride.py reports the
        /// planted foot's instantaneous speed varying from 6.6 to 11.5 m/s about
        /// this mean, and that spread IS the skating. It is also the sanity check
        /// on the IK: LeggedLocomotion.MaxSpeed should land in the same
        /// neighbourhood, because it is the same legs at the same cadence.
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

        // Frame ranges match the actions authored in anim.py. Both are cycles whose
        // last frame duplicates the first, so they loop without a seam. Slowed from
        // 40/90 to 72/120 frames when the creature doubled in size.
        private static readonly Clip[] Clips =
        {
            new Clip("Idle", "ConjurerRig|Idle", 1, 120),
            new Clip("Walk", "ConjurerRig|Walk", 1, 73),
        };

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

        /// Run just this creature's rig checks, headless, and write the result to
        /// Temp/headless_tests.txt.
        ///
        /// The Test Runner window carries some six hundred tests and these five are needles in it.
        /// More to the point, the thing they check -- whether the FBX round trip preserved a rig
        /// the IK can actually bind to -- is the question you want answered right after a re-export
        /// and right before wondering why the creature is standing in the ground.
        [MenuItem("Tools/Creatures/Verify Lightning Conjurer Rig")]
        private static void VerifyRig() => HeadlessTestRunner.RunEditMode(".*ConjurerRigDiscovery.*");

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

            // A state with no MOTION is the failure worth catching -- that is what an FBX
            // whose clips did not import produces, and it is invisible in the inspector until
            // something plays it.
            //
            // Deliberately NOT checking parameters. This controller is meant to have none:
            // there is no locomotion blend tree to drive and no AgentAnimatorDriver to write
            // SpeedX/SpeedY, because the legs are solved by ConjurerLocomotion. Requiring a
            // parameter here rejected a perfectly good controller.
            AnimatorState[] states = controller.layers[0].stateMachine.states
                .Select(c => c.state).ToArray();
            if (states.Length == 0 || states.Any(st => st.motion == null))
            {
                Debug.LogError("[LightningConjurer] Controller has a state with no motion - " +
                               "not reporting success. The FBX's clips are missing; re-run " +
                               "_Source~/walkerize.py (it retargets and verifies the actions) " +
                               "and re-export.");
                return;
            }

            Debug.Log($"[LightningConjurer] Built. Height {TargetHeight:0.00} m " +
                      $"(scale {Scale:0.0000}); legs solved by ConjurerLocomotion " +
                      $"(the old baked walk measured {StrideSpeed:0.00} m/s).");
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
                // No animation events. The footfall shake is driven off the gait's own
                // swing-to-stance edge now (LeggedFootstepShake), because a procedural
                // gait's cadence changes with speed and terrain and a baked event's
                // cannot. Leaving the events here as well would double-fire.
                events = new AnimationEvent[0],
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

            // SaveAndReimport does not guarantee the clips are queryable by the time
            // it returns. Without this the very next LoadAllAssetsAtPath can come
            // back with no AnimationClips at all, the blend tree gets no motions,
            // and the build finishes "successfully" with an empty controller --
            // which is exactly what happened on the second run of this builder.
            AssetDatabase.ImportAsset(
                Fbx, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        private static AnimationClip FindClip(string name)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(Fbx)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == name);
            if (clip == null)
            {
                string found = string.Join(", ", AssetDatabase.LoadAllAssetsAtPath(Fbx)
                    .OfType<AnimationClip>().Select(c => c.name));
                throw new System.InvalidOperationException(
                    $"[LightningConjurer] Clip '{name}' missing from the FBX. " +
                    $"Clips present: [{(found.Length == 0 ? "none" : found)}]. " +
                    "Building on would produce an animator with no motion. An FBX that " +
                    "imports with NO clips at all usually means an action in the .blend is " +
                    "keying bones that no longer exist -- the exporter skips those silently. " +
                    "Re-run _Source~/walkerize.py, which retargets and verifies them.");
            }
            return clip;
        }

        /// One state, one clip, no parameters.
        ///
        /// There is no locomotion blend tree any more and no AgentAnimatorDriver to
        /// drive one. The legs belong to ConjurerLocomotion, which poses them in
        /// LateUpdate -- after the Animator has written its pass -- so anything this
        /// controller had to say about them would be overwritten every frame anyway.
        ///
        /// What the Animator still owns is everything that is NOT a leg: the halo's
        /// turn, the eyelid, the floating arms, the spine and the hover. That is the
        /// whole of the Idle clip, and losing it to go procedural would have meant
        /// reimplementing it as a component for no gain.
        ///
        /// The one clip has to keep playing while the creature walks, which is why
        /// there is no transition and no Walk state: Idle is not an idle POSE here,
        /// it is the creature's ambient life.
        private static AnimatorController BuildController()
        {
            EnsureFolder(ControllerDir);
            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller =
                AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            AnimatorStateMachine root = controller.layers[0].stateMachine;
            AnimatorState idle = root.AddState("Idle");
            idle.motion = FindClip("Idle");
            root.defaultState = idle;

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

            // Kinematic, gravity off. ConjurerLocomotion writes the body transform directly
            // (invariant I4 -- it is the single owner of the pose), so a dynamic body would
            // fight it every frame and win. The Rigidbody is here anyway because without one
            // every collider on this object is a STATIC collider, and moving static colliders
            // makes PhysX rebuild its broadphase every frame.
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            WireLocomotion(root);
            WireBrain(root);

            // Footstep camera shake. No longer driven by animation events -- see
            // LeggedFootstepShake, which watches the gait's own swing-to-stance edge.
            var footstep = root.AddComponent<Presentation.FootstepCameraShake>();
            var shake = AssetDatabase.LoadAssetAtPath<ShakeData>(ShakeDataPath);
            if (shake != null) SetField(footstep, "shakeData", shake);
            root.AddComponent<Presentation.LeggedFootstepShake>();

            // TEMPORARY -- remove once the creature walks. Prints the acquisition chain in order
            // once a second, so the first line that reads wrong IS the fault.
            root.AddComponent<ConjurerDebugReadout>();

            // Save support, decided by the POLICY rather than by a list written out here.
            //
            // AgentController implements IPersistentEntity, so this creature is save-eligible with
            // no extra opt-in -- but the savers still have to be present or it reloads at its
            // authored position with its gait mid-stride. They go in the BUILDER because this
            // script overwrites the prefab wholesale on every re-run, which is exactly how the
            // Golem lost its SaveableEntity.
            //
            // SaveablePolicy.Ensure is the same call Tools > Save System > Wire Saveable Prefabs
            // makes, and the same one PersistenceProbe asserts against. Naming the components here
            // instead -- which is what this did first -- means the builder holds a second opinion
            // about which savers this prefab needs, and the moment the policy learns about a new
            // one the two disagree and the persistence sweep fails. Asking the policy cannot drift.
            if (SaveablePolicy.Ensure(root, out string savers))
                Debug.Log($"[LightningConjurer] Save wiring added: {savers}");

            // The savers are on the prefab now, but its prefabId is NOT: that lives in the asset
            // file, and SaveAsPrefabAsset below replaces the file wholesale, so every rebuild
            // blanks it. Only Tools > Save System > Wire Saveable Prefabs can stamp it back, and
            // it is deliberately not called from here because it sweeps every prefab in the
            // project -- far more than building one creature should touch. So: say so, every time,
            // rather than leaving it to be remembered.
            Debug.LogWarning("[LightningConjurer] Rebuilt prefab needs its save id re-stamped. " +
                             "Run Tools > Save System > Wire Saveable Prefabs, or SaveWiringOnDisk" +
                             "Tests will fail and the creature will be dropped on load in a build.");

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        /// The IK walker: ConjurerLocomotion solves the legs, ConjurerDriver commands them.
        ///
        /// Almost every number here is a consequence of ONE fact -- this creature is 18.1 m
        /// tall, six times the player. A biped's numbers do not scale linearly with height,
        /// and the two places that bite are cadence and lookahead:
        ///
        ///   * Step frequency in nature falls off roughly as 1/sqrt(length), which is the
        ///     reasoning already written into anim.py's WALK note when the cycle was slowed
        ///     to 2.4 s. stepDuration carries that decision now.
        ///   * Every distance the DRIVER works in -- stop distance, corner radius, NavMesh
        ///     sample distance -- is a distance this machine crosses in a fraction of a
        ///     stride. Left at the humanoid's defaults it grinds against corners it is
        ///     already standing on.
        private static void WireLocomotion(GameObject root)
        {
            // WalkerRig's OWN discovery, not a lookup by name.
            //
            // This used to be `model.Find("ConjurerRig")`, which failed for a boring reason --
            // Transform.Find searches direct children only, and the armature is not one -- but
            // it was the wrong question either way. A name says nothing about whether the rig
            // is discoverable; FindArmature picks whichever transform holds the most limb
            // ROOTS, which is exactly the answer LeggedLocomotion will reach at runtime.
            //
            // So this asks the runtime's question at build time. If it comes up short here, it
            // would have come up short in play, and the difference is an error naming the cause
            // rather than a creature standing inert in a scene with a clean console.
            Transform armature = WalkerRig.FindArmature(root.transform);
            int legs = WalkerRig.Build(armature, root.transform).Count;
            if (legs != 2)
            {
                Debug.LogError(
                    $"[LightningConjurer] Discovered {legs} limb(s) under " +
                    $"'{(armature != null ? armature.name : "<nothing>")}', expected 2 legs. " +
                    "The rig is not in the walker convention (Coxa_/Hip_/Knee_/Ankle_/Foot_ " +
                    "plus a *Pin* mesh per joint). Re-run _Source~/walkerize.py and re-export.",
                    root);
            }

            ConjurerLocomotion loco = root.AddComponent<ConjurerLocomotion>();
            var so = new SerializedObject(loco);
            SetProp(so, "armatureRoot", armature);
            SetProp(so, "body", root.transform);

            // Joint travel. The coxa is NOT the stride on a biped -- the feet sit under the
            // hips, so a yaw arc buys almost nothing. Its job is holding a planted foot still
            // while the body turns over it, and 30 degrees is plenty for that.
            SetFloat(so, "yawRange", 30f);
            SetFloat(so, "hipRange", 45f);
            SetFloat(so, "kneeRange", 60f);
            SetFloat(so, "ankleRange", 45f);
            SetFloat(so, "rollRange", 25f);

            // 1.1 s of swing, against the baked clip's ~1.0 s at a 2.4 s cycle. Deliberately
            // ponderous: a giant that steps at a human cadence reads as a toy.
            SetFloat(so, "stepDuration", 1.1f);
            SetFloat(so, "stepClearance", 0.08f);    // fraction of reach; ~0.9 m on this leg
            SetFloat(so, "obstacleClearance", 1.5f);

            // Measure the ride height rather than guessing it. The rig's origin sits at foot
            // level, so a hand-authored number here launches the machine on frame one.
            SetBool(so, "autoCalibrateRideHeight", true);
            SetFloat(so, "heightSmooth", 5f);

            SetFloat(so, "fallGravity", 20f);
            SetFloat(so, "maxFallSpeed", 40f);
            // Scaled off the machine, not the default 0.25 m. A foot on a leg this long sits
            // further from its idealised contact than a person's does, and too tight a
            // tolerance reports a planted foot as stranded in the air -- which reads as the
            // creature falling through the world the moment it steps onto anything uneven.
            SetFloat(so, "footGroundTolerance", 0.6f);

            // Every layer. Left unset this serialises as 0, and a walker whose ground mask
            // matches nothing finds no ground at all: it never snaps down, never places a
            // foothold, and stands perfectly still looking exactly like a rig fault. The
            // machine's own colliders are rejected by WalkerGround regardless of mask.
            SetInt(so, "groundMask", ~0);
            SetFloat(so, "rayStartAbove", 6f);
            SetFloat(so, "rayLength", 120f);
            SetBool(so, "snapToGroundOnStart", true);
            so.ApplyModifiedPropertiesWithoutUndo();

            ConjurerDriver driver = root.AddComponent<ConjurerDriver>();
            var dso = new SerializedObject(driver);
            // Clamped to what the legs can actually carry, so this is a ceiling rather than a
            // speed. The old baked walk measured 8.99 m/s at this cadence and stride, which is
            // the neighbourhood ConjurerLocomotion.MaxSpeed should come out in.
            SetFloat(dso, "moveSpeed", 9f);
            SetFloat(dso, "turnSpeed", 20f);
            SetFloat(dso, "acceleration", 1.2f);     // low: this thing has mass
            // All four sized to the machine. A stride is metres long, so a 1 m corner radius
            // is a corner it is already past by the time it notices.
            SetFloat(dso, "defaultStopDistance", 12f);
            SetFloat(dso, "turnInPlaceAngle", 45f);
            SetFloat(dso, "cornerArriveRadius", 10f);
            SetFloat(dso, "navMeshSampleDistance", 25f);
            SetBool(dso, "autoWalk", false);         // stands still until something provokes it
            dso.ApplyModifiedPropertiesWithoutUndo();
        }

        /// Stand still; wake when a player comes inside ActivationRange; then follow.
        ///
        /// Composed, not coded. There is no conjurer-specific brain class and there should not
        /// be one -- this is three stock components and a priority number:
        ///
        ///   EntityFaction    makes it visible to targeting at all. Without it the creature
        ///                    can never acquire anything, silently.
        ///   AgentTargeting   owns WHO. Every module reads its answer, which is what stops a
        ///                    creature chasing one entity while facing another.
        ///   ChaseModule      owns how to get there, at Reactive priority.
        ///
        /// The standing still is the ABSENCE of a module. There is deliberately no
        /// WanderModule and no PatrolModule: with nothing at Fallback priority,
        /// AgentController.EvaluateModules falls off the end of the ladder and returns
        /// MoveIntent.Idle, and ConjurerDriver holds position. Adding a wander module later is
        /// what would break the brief, not what would complete it.
        private static void WireBrain(GameObject root)
        {
            var faction = AssetDatabase.LoadAssetAtPath<FactionDefinition>(RobotFactionPath);
            var table = AssetDatabase.LoadAssetAtPath<FactionRelationshipTable>(RelationshipsPath);
            if (faction == null || table == null)
            {
                Debug.LogError("[LightningConjurer] Faction assets missing; the creature will " +
                               "never acquire a target. Expected " + RobotFactionPath + " and " +
                               RelationshipsPath + ".");
            }

            // RobotFaction is already Hostile toward PlayerFaction in GlobalRelationships.asset,
            // so no new row is needed and none should be added -- that table is global, and a
            // row added here changes every robot in the game.
            var entityFaction = root.AddComponent<EntityFaction>();
            SetField(entityFaction, "faction", faction);
            SetField(entityFaction, "relationshipTable", table);

            // Added explicitly rather than left to AgentController's Awake, because the ranges
            // below are the entire behaviour and an auto-added component would carry defaults
            // (35 m acquisition) that are nothing like the brief.
            var targeting = root.AddComponent<AgentTargeting>();
            var tso = new SerializedObject(targeting);
            SetEnum(tso, "relationship", (int)FactionRelationship.Hostile);
            SetFloat(tso, "acquisitionRange", ActivationRange);
            // Above acquisition so a player hovering exactly on the line does not flip the
            // creature between chasing and inert every frame.
            SetFloat(tso, "loseRange", ActivationRange * 1.4f);
            // Distance alone decides, which is what "comes within 10 metres" means. With line
            // of sight required, walking up behind its own leg would leave it inert.
            SetBool(tso, "requireLineOfSightToAcquire", false);
            SetFloat(tso, "proximityAcquireRange", ActivationRange);
            tso.ApplyModifiedPropertiesWithoutUndo();

            var chase = root.AddComponent<ChaseModule>();
            var cso = new SerializedObject(chase);
            // Set EXPLICITLY. Unity does not call Reset() for AddComponent, so a module added
            // from a script keeps the serialized default of Fallback (0) -- which here would
            // leave the one module that makes this creature move sitting at the bottom of the
            // ladder for no reason.
            SetInt(cso, "priority", ModulePriority.Reactive);
            // Sized against the ACQUISITION RANGE, not just against the creature.
            //
            // This was 8 m, reasoned from the creature's size alone -- an 18 m robot stopping at
            // ChaseModule's default 1.3 m would put a foot on the player. True, and useless: with
            // ActivationRange at 10 m it left a two-metre chase band. The creature noticed you at
            // 10 m, took two steps, decided it had arrived, and stood there -- which from the
            // outside is indistinguishable from never having reacted at all.
            //
            // The floor that actually matters is the capsule: its radius is BlenderBodyWidth/2
            // (~2.4 m), so anything under that walks the body through the player. 4 m clears it
            // with margin and leaves a real chase band -- 4 m out to the 15 m lose range.
            SetFloat(cso, "chaseStopDistance", 4f);
            SetFloat(cso, "chaseSpeedMultiplier", 1f);
            cso.ApplyModifiedPropertiesWithoutUndo();

            var agent = root.AddComponent<AgentController>();
            var aso = new SerializedObject(agent);
            // The driver IS the motor: LeggedDriver implements IMovementMotor.
            SetProp(aso, "MotorComponent", root.GetComponent<ConjurerDriver>());
            // Left null on purpose. AgentAnimatorDriver drives a locomotion blend tree, and
            // this creature has no locomotion clips -- its legs are IK and its Animator only
            // plays the ambient Idle.
            SetProp(aso, "animatorDriver", null);
            SetFloat(aso, "nearbyAgentScanRadius", 0f);   // no flocking; skips the neighbour scan
            aso.ApplyModifiedPropertiesWithoutUndo();
        }

        // Private [SerializeField] fields are not reachable from an editor script any other
        // way, and making them public purely so this could set them would widen the runtime API
        // for a build-time convenience. A missing name warns loudly rather than silently doing
        // nothing -- a typo here is a tuning value that never lands.
        private static SerializedProperty Find(SerializedObject so, string field)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
                Debug.LogWarning($"[LightningConjurer] {so.targetObject.GetType().Name} has no " +
                                 $"serialized field '{field}'; it was renamed or removed.");
            return p;
        }

        private static void SetProp(SerializedObject so, string field, Object value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.objectReferenceValue = value;
        }

        private static void SetFloat(SerializedObject so, string field, float value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.floatValue = value;
        }

        private static void SetInt(SerializedObject so, string field, int value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.intValue = value;
        }

        private static void SetBool(SerializedObject so, string field, bool value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.boolValue = value;
        }

        private static void SetEnum(SerializedObject so, string field, int value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.enumValueIndex = value;
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
