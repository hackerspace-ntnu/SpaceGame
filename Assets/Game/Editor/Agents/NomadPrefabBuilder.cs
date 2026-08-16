using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Builds the Nomad NPC prefab from the imported FBX and drops one into the persistent
    /// scene beside the ShipRV.
    ///
    /// This is authored as an editor command rather than hand-written prefab YAML because the
    /// nomad rig carries 65 mixamorig bones. Writing that hierarchy by hand means inventing 65
    /// stable fileIDs and their parent links, and a single wrong reference produces a prefab
    /// that opens with a broken skeleton and no error. Unity's own API does it correctly.
    ///
    /// Idempotent: running it twice overwrites the prefab and moves the existing scene instance
    /// rather than stacking up duplicates.
    /// </summary>
    public static class NomadPrefabBuilder
    {
        private const string FbxPath = "Assets/Game/Art/Models/Characters/Nomad/nomad.fbx";
        private const string PrefabPath = "Assets/Game/Prefabs/Agents/Characters/Nomad.prefab";
        private const string ClothMaterialPath = "Assets/Game/Art/Materials/Characters/NomadCloth.mat";
        private const string BodyMaterialPath = "Assets/Game/Art/Materials/Characters/NomadBody.mat";
        private const string ScenePath = "Assets/Game/Scenes/World/persistentScene.unity";
        private const string AnimatorPath = "Assets/Game/Art/Animations/Player/AstronautArmature.controller";

        // The ShipRV sits here in persistentScene, unrotated, at scale 2.
        private static readonly Vector3 ShipRvPosition = new Vector3(3789.8f, 99.7f, 1563.0f);

        // Placed off the ship's flank, far enough out to clear the doubled-scale hull.
        private static readonly Vector3 NomadOffset = new Vector3(6.5f, 0f, -3.0f);

        // The FBX measures 3.12 units head to foot. Human agents in this project are built
        // around a 2 m NavMesh capsule, so scale to land at roughly 2 m.
        private const float ModelScale = 0.64f;

        // The cape: five tattered cloth panels plus the shoulder scarf. In the Blender source
        // these were unparented cloth-sim planes; the export binds them to mixamorig:Spine2 so
        // they ride with the torso, and this shader then supplies the motion.
        //
        // These are OBJECT names, which is what Unity uses for the GameObject under the model
        // root. The FBX's internal geometry names differ (Plane.003-.006, Mesh.0xx) and must
        // not be used here.
        private static readonly string[] ClothMeshNames =
        {
            "Plane", "Plane.007", "Plane.008", "Plane.009", "Plane.010",
            "Scarf Remeshed.001",
        };

        [MenuItem("Tools/SpaceGame/Agents/Build Nomad NPC")]
        public static void BuildAndPlace()
        {
            var prefab = BuildPrefab();
            if (prefab == null) return;

            PlaceInPersistentScene(prefab);

            Debug.Log("[NomadPrefabBuilder] Done. Run " +
                      "Tools > SpaceGame > Multiplayer > Sync Network Prefabs so clients can " +
                      "spawn the Nomad.");
        }

        [MenuItem("Tools/SpaceGame/Agents/Build Nomad NPC (prefab only)")]
        public static void BuildPrefabOnly()
        {
            BuildPrefab();
        }

        public static GameObject BuildPrefab()
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbx == null)
            {
                Debug.LogError($"[NomadPrefabBuilder] No FBX at {FbxPath}.");
                return null;
            }

            EnsureFolder("Assets/Game/Prefabs/Agents/Characters");
            EnsureFolder("Assets/Game/Art/Materials/Characters");

            var clothMaterial = EnsureClothMaterial();
            var bodyMaterial = EnsureBodyMaterial();

            // Work on an instance, then save it over the prefab path.
            var root = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            root.name = "Nomad";

            // Unpack so the agent components live on a real prefab of our own rather than as
            // overrides on the model importer's prefab, which regenerates on reimport.
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);

            root.transform.localScale = Vector3.one * ModelScale;

            ApplyMaterials(root, clothMaterial, bodyMaterial);
            ConfigureAnimator(root);
            ConfigurePhysics(root);
            AddAgentStack(root);
            AddClothWind(root);

            var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            Object.DestroyImmediate(root);

            if (!ok || saved == null)
            {
                Debug.LogError("[NomadPrefabBuilder] Failed to save the prefab.");
                return null;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[NomadPrefabBuilder] Wrote {PrefabPath}");
            return saved;
        }

        // ------------------------------------------------------------------
        // Materials

        private static Material EnsureClothMaterial()
        {
            var shader = Shader.Find("SpaceGame/ClothWind");
            if (shader == null)
            {
                Debug.LogError("[NomadPrefabBuilder] SpaceGame/ClothWind not found — the cape " +
                               "will not move. Check the shader compiled.");
                return null;
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(ClothMaterialPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, ClothMaterialPath);
            }
            else
            {
                mat.shader = shader;
            }

            mat.SetColor("_BaseColor", new Color(0.44f, 0.35f, 0.26f));
            mat.SetFloat("_Smoothness", 0.10f);

            // The shoulder scarf carries no UVs, so a weave sampled from UV0 would read as
            // noise on it. The cape panels do have UVs, but the weave is a subtle touch and
            // not worth splitting into a second material — leave it off for the whole garment.
            mat.SetFloat("_WeaveDepth", 0f);

            // Measured from the shipped FBX's own vertex data, in the file's coordinate frame
            // (Y up), so these are the real collar and hem heights rather than estimates:
            //   cape collar  Y = +0.355
            //   cape hem     Y = -1.447
            // The hem lies below the collar, hence the negative span.
            mat.SetFloat("_AnchorAxis", 1f);        // Y
            mat.SetFloat("_AnchorOrigin", 0.355f);  // collar, where the cape is stitched on
            mat.SetFloat("_FreeLength", -1.803f);   // collar -> hem
            mat.SetFloat("_Stiffness", 1.7f);

            mat.SetFloat("_WindStrength", 0.22f);
            mat.SetFloat("_Turbulence", 0.30f);
            mat.SetFloat("_WaveSpeed", 2.2f);
            mat.SetFloat("_WaveLength", 1.6f);
            mat.SetFloat("_FlutterAmp", 0.12f);
            mat.SetFloat("_FlutterFreq", 2.4f);
            mat.SetFloat("_FlutterSpeed", 5.0f);
            mat.SetFloat("_GustSpeed", 0.55f);
            mat.SetFloat("_GustAmount", 0.45f);
            mat.SetFloat("_MaxStretch", 0.5f);
            mat.SetFloat("_Backlight", 0.6f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material EnsureBodyMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(BodyMaterialPath);
            if (mat == null)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                mat = new Material(lit);
                AssetDatabase.CreateAsset(mat, BodyMaterialPath);
            }

            mat.SetColor("_BaseColor", new Color(0.30f, 0.27f, 0.24f));
            mat.SetFloat("_Smoothness", 0.25f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Cloth meshes get the wind shader; everything else gets the plain body material.
        /// </summary>
        private static void ApplyMaterials(GameObject root, Material cloth, Material body)
        {
            var clothSet = new HashSet<string>(ClothMeshNames);

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                bool isCloth = clothSet.Contains(r.gameObject.name);
                var chosen = isCloth ? cloth : body;
                if (chosen == null) continue;

                var mats = new Material[r.sharedMaterials.Length == 0 ? 1 : r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = chosen;
                r.sharedMaterials = mats;
            }
        }

        // ------------------------------------------------------------------
        // Components

        private static void ConfigureAnimator(GameObject root)
        {
            var animator = root.GetComponent<Animator>() ?? root.AddComponent<Animator>();

            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AnimatorPath);
            if (controller != null)
                animator.runtimeAnimatorController = controller;
            else
                Debug.LogWarning($"[NomadPrefabBuilder] No animator controller at {AnimatorPath}; " +
                                 "the Nomad will stand in bind pose.");

            // The FBX imports as Humanoid (animationType 3), so the Astronaut's mixamo clips
            // retarget onto this skeleton without a separate avatar.
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        }

        private static void ConfigurePhysics(GameObject root)
        {
            var capsule = root.GetComponent<CapsuleCollider>() ?? root.AddComponent<CapsuleCollider>();
            capsule.height = 2.0f;
            capsule.radius = 0.4f;
            capsule.center = new Vector3(0f, 1.0f, 0f);

            var body = root.GetComponent<Rigidbody>() ?? root.AddComponent<Rigidbody>();
            // The NavMeshAgent owns movement, so physics must not also push this thing around.
            body.isKinematic = true;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            var agent = root.GetComponent<NavMeshAgent>() ?? root.AddComponent<NavMeshAgent>();
            agent.radius = 0.4f;
            agent.height = 2.0f;
            agent.speed = 2.2f;              // a walking nomad, not a patrol robot
            agent.acceleration = 8f;
            agent.angularSpeed = 120f;
            agent.stoppingDistance = 0.5f;
            agent.autoBraking = true;
        }

        /// <summary>
        /// The agent stack, matching what PatrolRobot and DuneRat carry. Types are resolved by
        /// name so this compiles even while parts of the agent assembly are being edited.
        /// </summary>
        private static void AddAgentStack(GameObject root)
        {
            // Order matters only in that AgentController self-discovers modules in Awake, so
            // everything it should find must exist on the object by then — which it does,
            // since these are all added before the prefab is saved.
            string[] components =
            {
                "SpaceGame.Agents.NavMeshAgentMotor",
                "SpaceGame.Agents.AgentAnimatorDriver",
                "SpaceGame.Agents.AgentController",
                "SpaceGame.Agents.AgentTargeting",
                "SpaceGame.Gameplay.HealthComponent",
                "SpaceGame.Agents.HealthReactionModule",
                "SpaceGame.Agents.PerceptionModule",
                "SpaceGame.Agents.WanderModule",
                "SpaceGame.Agents.IdleLookAroundModule",
                "SpaceGame.Agents.EntityFaction",
                "SpaceGame.World.SceneTracked",
                "Unity.Netcode.NetworkObject",
                "SpaceGame.Core.ClientNetworkTransform",
                "SpaceGame.Core.NetRelay",
                "SpaceGame.Core.NetAuthority",
                "SpaceGame.Gameplay.NetworkedHealthComponent",
                "SpaceGame.Core.Persistence.SaveableEntity",
                "SpaceGame.Core.Persistence.TransformSaveable",
                "SpaceGame.Core.Persistence.HealthSaveable",
                "SpaceGame.World.Safety.UnderTerrainGuard",
            };

            foreach (var typeName in components)
                AddByName(root, typeName);
        }

        private static void AddByName(GameObject go, string fullName)
        {
            var type = FindType(fullName);
            if (type == null)
            {
                Debug.LogWarning($"[NomadPrefabBuilder] Type not found, skipping: {fullName}. " +
                                 "Add it by hand if the Nomad needs it.");
                return;
            }

            if (go.GetComponent(type) == null)
                go.AddComponent(type);
        }

        private static System.Type FindType(string fullName)
        {
            // Try the plain lookup first, then sweep loaded assemblies, then fall back to a
            // short-name match so a moved namespace does not silently drop the component.
            var t = System.Type.GetType(fullName);
            if (t != null) return t;

            var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                t = asm.GetType(fullName);
                if (t != null) return t;
            }

            // Last resort: match on the short name, so a component that moved namespace is
            // still found rather than silently dropped from the prefab.
            string shortName = fullName.Substring(fullName.LastIndexOf('.') + 1);
            foreach (var asm in assemblies)
            {
                System.Type[] candidates;
                try
                {
                    candidates = asm.GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException e)
                {
                    // An assembly with an unresolvable reference still reports the types it
                    // did manage to load; the nulls are the ones that failed.
                    candidates = e.Types;
                }
                catch
                {
                    continue;
                }

                foreach (var candidate in candidates)
                {
                    if (candidate != null &&
                        candidate.Name == shortName &&
                        typeof(Component).IsAssignableFrom(candidate))
                        return candidate;
                }
            }

            return null;
        }

        private static void AddClothWind(GameObject root)
        {
            if (root.GetComponent<ClothWindDriver>() == null)
                root.AddComponent<ClothWindDriver>();
        }

        // ------------------------------------------------------------------
        // Scene placement

        private static void PlaceInPersistentScene(GameObject prefab)
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool alreadyOpen = scene.IsValid() && scene.isLoaded;

            if (!alreadyOpen)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    Debug.LogWarning("[NomadPrefabBuilder] Scene placement cancelled.");
                    return;
                }
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            }

            // Reuse an existing Nomad rather than stacking duplicates on repeat runs.
            var existing = scene.GetRootGameObjects()
                                .FirstOrDefault(g => g.name == "Nomad");

            GameObject instance;
            if (existing != null)
            {
                instance = existing;
            }
            else
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = "Nomad";
            }

            Vector3 target = ShipRvPosition + NomadOffset;

            // Drop onto whatever ground is actually under that spot; the ShipRV's own Y is the
            // hull, not the terrain, so using it directly would bury or float the nomad. The
            // terrain for this chunk may not be streamed in at edit time, in which case the
            // raycast finds nothing and the ship's own Y is the best estimate we have — the
            // NavMeshAgent and UnderTerrainGuard settle him on first play either way.
            if (Physics.Raycast(target + Vector3.up * 200f, Vector3.down,
                                out RaycastHit hit, 500f))
            {
                target.y = hit.point.y;
            }
            else
            {
                Debug.LogWarning("[NomadPrefabBuilder] No ground under the spawn point — the " +
                                 "terrain chunk is probably not loaded. Placing at the ShipRV's " +
                                 "height; check him in play mode.");
            }

            instance.transform.position = target;
            // Face back toward the ship, so he reads as standing beside it rather than
            // wandering off.
            Vector3 toShip = ShipRvPosition - target;
            toShip.y = 0f;
            if (toShip.sqrMagnitude > 1e-4f)
                instance.transform.rotation = Quaternion.LookRotation(toShip.normalized, Vector3.up);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[NomadPrefabBuilder] Placed Nomad at {target} in {ScenePath}");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
