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
    /// rather than stacking up duplicates. It deliberately overwrites the SAME asset path so the
    /// prefab GUID survives -- deleting and recreating would silently null every serialized
    /// reference to the Nomad, including the network prefab list and the scene instance.
    ///
    /// The FBX is produced by
    /// `Assets/Game/Art/Models/_Source~/models/characters/nomad/nomad_export.py`, which curates
    /// one live character out of a .blend holding 30 copies of the same skeleton. Re-run that
    /// before this if the art changed.
    /// </summary>
    public static class NomadPrefabBuilder
    {
        private const string FbxPath = "Assets/Game/Art/Models/Characters/Nomad/nomad.fbx";
        private const string PrefabPath = "Assets/Game/Prefabs/Agents/Characters/Nomad.prefab";
        private const string ClothMaterialFolder = "Assets/Game/Art/Materials/Characters";
        private const string ScenePath = "Assets/Game/Scenes/World/persistentScene.unity";
        private const string AnimatorPath = "Assets/Game/Art/Animations/Player/AstronautArmature.controller";
        private const string FactionPath = "Assets/Game/ScriptableObjects/Factions/Core/NPCFaction.asset";
        private const string RelationshipsPath = "Assets/Game/ScriptableObjects/Factions/Core/GlobalRelationships.asset";

        // The ShipRV sits here in persistentScene, unrotated, at scale 2.
        private static readonly Vector3 ShipRvPosition = new Vector3(3789.8f, 99.7f, 1563.0f);

        // Placed off the ship's flank, far enough out to clear the doubled-scale hull.
        private static readonly Vector3 NomadOffset = new Vector3(6.5f, 0f, -3.0f);

        // How tall the Nomad should stand, sole to crown.
        private const float TargetHeight = 3.0f;

        // What the FBX itself measures, sole to crown: the toe bone sits at 0.014 and
        // HeadTop_End at 3.018 in the exported file's units.
        private const float ModelHeightUnits = 3.0f;

        private const float ModelScale = TargetHeight / ModelHeightUnits;

        // Capsule/agent dimensions derived from the height rather than restated, so changing
        // TargetHeight cannot leave the collider describing a differently-sized character.
        // The radius ratio is the one the 2 m build used (0.4 / 1.92).
        private const float BodyHeight = TargetHeight;
        private const float BodyRadius = TargetHeight * 0.208f;

        // The cape: the full-length cloak and its shoulder flap. These are the only meshes that
        // get the wind shader, and that assignment does two jobs -- ClothWind supplies the motion,
        // and it declares `Cull Off` in every pass, which is the only reason the cape is visible
        // from behind. A URP Lit material would render its back faces away.
        //
        // The list was previously five planes plus the neck scarf. Rendering each in isolation
        // showed three of them are a grey shoulder pad and two thigh pads, so they are ordinary
        // skinned geometry now; the ClothWind collar/hem span is measured against the cloak alone
        // and would stretch anything else against an anchor nowhere near it.
        //
        // These are OBJECT names, which is what Unity uses for the GameObject under the model
        // root, and they are set by nomad_fix_rig.py. The FBX's internal geometry names differ
        // and must not be used here.
        private static readonly string[] ClothMeshNames =
        {
            "Cloth_Cape_01", "Cloth_Cape_02",
        };

        // Flavour lines, drawn at random. RandomFromPredefinedPool shuffles a private cycle so the
        // nomad works through all of them before repeating.
        private static readonly string[] DialogLines =
        {
            "Sand's been restless since the storm.",
            "You're the first face I've seen in a week.",
            "Careful past the ridge. Something out there hums at night.",
            "Water first. Questions later.",
            "I trade in scrap, not promises.",
            "The old relay still sings, if you know where to listen.",
            "Keep your visor sealed after dark.",
            "Every wreck out here was somebody's ride home.",
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
            EnsureFolder(ClothMaterialFolder);

            // The agent components live on their own root rather than on the model, because the
            // model has to move relative to them: the artist's boots hang 0.59 model units below
            // the rig's foot plane, so a model placed straight at the origin stands buried to the
            // shin. See AlignSoleToRoot.
            var root = new GameObject("Nomad");

            GameObject saved;
            bool ok;
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                model.name = "Model";

                // Unpack so our edits live on a real prefab of our own rather than as overrides on
                // the model importer's prefab, which regenerates on reimport.
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely,
                                                   InteractionMode.AutomatedAction);

                model.transform.SetParent(root.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one * ModelScale;

                AlignSoleToRoot(root, model);

                ApplyClothMaterial(model);
                ConfigureAnimator(model);
                ConfigurePhysics(root);
                AddAgentStack(root);
                ConfigureDialog(root);
                ConfigurePerception(root);
                ConfigureHealth(root);
                ConfigureFaction(root);
                AddClothWind(root);

                saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out ok);
            }
            finally
            {
                // `new GameObject` lands in the open scene, so an exception partway through would
                // otherwise leave a half-built Nomad sitting in whatever scene the user had open.
                Object.DestroyImmediate(root);
            }

            if (!ok || saved == null)
            {
                Debug.LogError("[NomadPrefabBuilder] Failed to save the prefab.");
                return null;
            }

            saved = CorrectScaleAndSole(saved);

            AssetDatabase.SaveAssets();
            Debug.Log($"[NomadPrefabBuilder] Wrote {PrefabPath}");
            return saved;
        }

        // ------------------------------------------------------------------
        // Placement on the ground

        /// <summary>
        /// Slides the model up so its lowest geometry sits exactly on the root's origin.
        ///
        /// <para>
        /// The nomad's boots are modelled 0.59 units below the rig's toe bone. Nothing in the
        /// engine notices: the rest pose looks correct in the importer preview, and the
        /// NavMeshAgent plants the ROOT on the navmesh, which leaves the visible boots underground
        /// and the character standing in a hole. Measured off the mesh rather than hard-coded so a
        /// re-export with different proportions re-derives it.
        /// </para>
        /// </summary>
        private static void AlignSoleToRoot(GameObject root, GameObject model)
        {
            float lowest = float.MaxValue;
            bool found = false;

            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                if (mesh == null) continue;

                // The mesh's own bounds, walked corner by corner into root space. Renderer.bounds
                // would be simpler and is not trustworthy here -- for a SkinnedMeshRenderer that
                // has never been animated it reports whatever the importer last cached.
                Bounds bounds = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                        (i & 1) == 0 ? -1f : 1f,
                        (i & 2) == 0 ? -1f : 1f,
                        (i & 4) == 0 ? -1f : 1f));

                    float y = root.transform.InverseTransformPoint(
                        renderer.transform.TransformPoint(corner)).y;
                    if (y < lowest) lowest = y;
                    found = true;
                }
            }

            if (!found)
            {
                Debug.LogWarning("[NomadPrefabBuilder] No renderers to measure; the model may " +
                                 "stand in the ground.");
                return;
            }

            model.transform.localPosition += Vector3.up * -lowest;
            Debug.Log($"[NomadPrefabBuilder] Raised the model {-lowest:0.###} m so the soles meet " +
                      "the root origin.");
        }

        /// <summary>
        /// Sets the final scale and ground contact, measured on a real instance of the prefab.
        ///
        /// <para>
        /// Both numbers have to be measured here rather than derived up front, because the only
        /// honest measure of the character's size is what the renderers actually draw once the
        /// skeleton has posed them. Two things make the up-front estimate wrong:
        /// <c>mesh.bounds</c> on a SkinnedMeshRenderer describes the mesh in skinning space
        /// rather than where the bones put it, and the visible silhouette runs from the boot
        /// soles to the top of the helmet — well outside the toe and head BONES the skeleton
        /// measures 3.0 units between. Scaling by the skeleton produced a 3.68 m character.
        /// </para>
        /// </summary>
        private static GameObject CorrectScaleAndSole(GameObject prefab)
        {
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                probe.transform.position = Vector3.zero;
                probe.transform.rotation = Quaternion.identity;

                var animator = probe.GetComponentInChildren<Animator>();
                if (animator != null) animator.Rebind();

                var model = probe.transform.Find("Model");
                if (model == null) return prefab;

                if (!TryMeasure(probe, out float low, out float high)) return prefab;

                float rendered = high - low;
                if (rendered > 1e-3f)
                {
                    float factor = TargetHeight / rendered;
                    model.localScale *= factor;
                    Debug.Log($"[NomadPrefabBuilder] Drew {rendered:0.###} m; scaling by " +
                              $"{factor:0.####} to stand {TargetHeight:0.##} m tall.");
                }

                // Re-measure: the scale just moved everything, including the soles.
                if (TryMeasure(probe, out low, out high))
                {
                    model.localPosition += Vector3.up * -low;
                    Debug.Log($"[NomadPrefabBuilder] Final height {high - low:0.###} m, " +
                              $"soles set down by {-low:0.###} m.");
                }

                PrefabUtility.ApplyPrefabInstance(probe, InteractionMode.AutomatedAction);
                return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }

        private static bool TryMeasure(GameObject instance, out float low, out float high)
        {
            low = float.MaxValue;
            high = float.MinValue;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                low = Mathf.Min(low, renderer.bounds.min.y);
                high = Mathf.Max(high, renderer.bounds.max.y);
            }
            return low != float.MaxValue;
        }

        // ------------------------------------------------------------------
        // Materials

        /// <summary>
        /// Builds the wind material for one cape mesh.
        ///
        /// <para>
        /// One material per mesh, not one shared across the cape. ClothWind measures a vertex's
        /// freedom to blow as its distance from the collar along an axis in OBJECT space, and
        /// these two meshes do not share an object space: the cloak's local +Y points along
        /// world up while the shoulder flap's points along world down, and their local extents
        /// differ by a factor of twenty. A single anchor cannot describe both — it would pin one
        /// piece rigid and blow the other inside out.
        /// </para>
        /// </summary>
        private static Material EnsureClothMaterial(string meshName, ClothAnchor anchor)
        {
            var shader = Shader.Find("SpaceGame/ClothWind");
            if (shader == null)
            {
                Debug.LogError("[NomadPrefabBuilder] SpaceGame/ClothWind not found — the cape " +
                               "will not move, and will render single-sided because only this " +
                               "shader declares Cull Off. Check the shader compiled.");
                return null;
            }

            // "Cloth_Cape_01" -> ".../NomadCloth_Cape_01.mat"
            string leaf = meshName.StartsWith("Cloth_") ? meshName.Substring("Cloth_".Length) : meshName;
            string path = $"{ClothMaterialFolder}/NomadCloth_{leaf}.mat";

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
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

            // Measured off this mesh's own vertices every build rather than hard-coded. The
            // previous constants (origin 0.355, length -1.803) were measured against an older
            // export and survived a re-export that changed the cape's object space by ~90x; the
            // result was every vertex pinned at maximum displacement, which read in-game as the
            // cloak wrapping round the front of the character instead of hanging open.
            mat.SetFloat("_AnchorAxis", anchor.Axis);
            mat.SetFloat("_AnchorOrigin", anchor.Origin);
            mat.SetFloat("_FreeLength", anchor.FreeLength);
            mat.SetFloat("_Stiffness", 1.7f);

            // Every amplitude here is a DISTANCE IN METRES, so it has to be scaled to the piece
            // it is driving. The shoulder flap is 0.18 m long; at the cloak's _MaxStretch of
            // 0.5 m it was being thrown nearly three times its own length off the body, which is
            // what read in-game as a flap juddering back and forth and as loose bits floating
            // beside the character.
            float scale = Mathf.Clamp(anchor.WorldDrop / ReferenceCapeDrop, 0.05f, 1f);

            mat.SetFloat("_WindStrength", 0.14f * scale);
            mat.SetFloat("_Turbulence", 0.18f * scale);
            mat.SetFloat("_WaveSpeed", 2.2f);
            mat.SetFloat("_WaveLength", 1.6f);
            mat.SetFloat("_FlutterAmp", 0.07f * scale);
            mat.SetFloat("_FlutterFreq", 2.4f);
            mat.SetFloat("_FlutterSpeed", 5.0f);
            mat.SetFloat("_GustSpeed", 0.55f);
            mat.SetFloat("_GustAmount", 0.35f);
            mat.SetFloat("_MaxStretch", 0.22f * scale);
            mat.SetFloat("_Backlight", 0.6f);

            Debug.Log($"[NomadPrefabBuilder] {meshName}: {anchor.WorldDrop:0.##} m of cloth, " +
                      $"wind amplitudes scaled to {scale:0.##}x.");

            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Where a cape hangs from, in its own mesh's object space.</summary>
        private struct ClothAnchor
        {
            public float Axis;        // 0 = X, 1 = Y, 2 = Z, matching _AnchorAxis
            public float Origin;      // coordinate of the collar along that axis
            public float FreeLength;  // signed span from collar to hem
            public float WorldDrop;   // collar-to-hem distance in metres
        }

        // The wind amplitudes below are tuned for a cloak about this long. Shorter pieces get
        // them scaled down in proportion -- see EnsureClothMaterial.
        private const float ReferenceCapeDrop = 1.4f;

        /// <summary>
        /// Finds the axis the cape hangs along and where its collar and hem sit on it.
        ///
        /// <para>
        /// Works off real vertices rather than <c>mesh.bounds</c>, because the collar is a
        /// specific place on the mesh and a bounding box only knows its corners. The axis is
        /// whichever object-space axis lines up best with world vertical — which is not always
        /// +Y and not always positive, since these panels came in with the .blend's own
        /// orientations baked into their transforms.
        /// </para>
        /// </summary>
        private static bool TryMeasureClothAnchor(SkinnedMeshRenderer renderer, out ClothAnchor anchor)
        {
            anchor = default;

            var mesh = renderer.sharedMesh;
            if (mesh == null) return false;

            var t = renderer.transform;
            var axes = new[]
            {
                t.TransformDirection(Vector3.right).normalized,
                t.TransformDirection(Vector3.up).normalized,
                t.TransformDirection(Vector3.forward).normalized,
            };

            int best = 0;
            for (int i = 1; i < 3; i++)
                if (Mathf.Abs(axes[i].y) > Mathf.Abs(axes[best].y)) best = i;

            var verts = mesh.vertices;
            if (verts.Length == 0) return false;

            var toWorld = t.localToWorldMatrix;
            int top = 0, hem = 0;
            float topY = float.MinValue, hemY = float.MaxValue;
            for (int i = 0; i < verts.Length; i++)
            {
                float y = toWorld.MultiplyPoint3x4(verts[i]).y;
                if (y > topY) { topY = y; top = i; }
                if (y < hemY) { hemY = y; hem = i; }
            }

            float origin = verts[top][best];
            float length = verts[hem][best] - origin;

            // A panel with no vertical extent has no hang direction; the shader would divide by
            // that span. Leave it rigid rather than let it explode.
            if (Mathf.Abs(length) < 1e-6f)
            {
                Debug.LogWarning($"[NomadPrefabBuilder] '{renderer.name}' has no measurable drop " +
                                 "along any axis; leaving it unanimated.");
                return false;
            }

            anchor = new ClothAnchor
            {
                Axis = best,
                Origin = origin,
                FreeLength = length,
                WorldDrop = topY - hemY,
            };
            Debug.Log($"[NomadPrefabBuilder] {renderer.name}: hangs on " +
                      $"{"XYZ"[best]}, collar {origin:0.#####}, span {length:0.#####} " +
                      $"({topY - hemY:0.##} m of drop in world space).");
            return true;
        }

        /// <summary>
        /// Cape meshes get the wind shader. Everything else keeps the material the FBX arrived
        /// with -- the .blend paints the nomad across ~40 materials, and flattening them onto one
        /// body colour throws away the whole read of the character.
        /// </summary>
        private static void ApplyClothMaterial(GameObject model)
        {
            var wanted = new HashSet<string>(ClothMeshNames);
            var seen = new HashSet<string>();

            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!wanted.Contains(renderer.gameObject.name)) continue;
                seen.Add(renderer.gameObject.name);

                if (!TryMeasureClothAnchor(renderer, out ClothAnchor anchor)) continue;

                var cloth = EnsureClothMaterial(renderer.gameObject.name, anchor);
                if (cloth == null) continue;

                var mats = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                for (int i = 0; i < mats.Length; i++) mats[i] = cloth;
                renderer.sharedMaterials = mats;
            }

            foreach (var missing in wanted.Except(seen))
            {
                Debug.LogWarning($"[NomadPrefabBuilder] Cape mesh '{missing}' is not in the FBX. " +
                                 "It will not catch the wind AND will render single-sided, since " +
                                 "ClothWind is what supplies Cull Off. Check the name still " +
                                 "matches RENAMES in nomad_fix_rig.py.");
            }
        }

        // ------------------------------------------------------------------
        // Components

        private static void ConfigureAnimator(GameObject model)
        {
            var animator = GetOrAdd<Animator>(model);

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
            var capsule = GetOrAdd<CapsuleCollider>(root);
            capsule.height = BodyHeight;
            capsule.radius = BodyRadius;
            capsule.center = new Vector3(0f, BodyHeight * 0.5f, 0f);

            var body = GetOrAdd<Rigidbody>(root);
            // The NavMeshAgent owns movement, so physics must not also push this thing around.
            body.isKinematic = true;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            var agent = GetOrAdd<NavMeshAgent>(root);
            agent.radius = BodyRadius;
            agent.height = BodyHeight;
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
            // Order matters. AddComponent runs Awake immediately in the editor, and both
            // AgentController and AgentTargeting look their dependencies up there — so a
            // controller added before its movement modules, or targeting added before the
            // faction, warns and caches a null even though the finished prefab holds everything.
            // Dependencies therefore come first, and the two discoverers come last.
            string[] components =
            {
                "SpaceGame.Agents.NavMeshAgentMotor",
                "SpaceGame.Agents.AgentAnimatorDriver",
                "SpaceGame.Gameplay.HealthComponent",
                "SpaceGame.Agents.EntityFaction",
                "SpaceGame.Agents.PerceptionModule",
                "SpaceGame.Agents.WanderModule",
                "SpaceGame.Agents.IdleLookAroundModule",
                // Lets DialogInteraction stop him and turn him to face whoever is talking.
                "SpaceGame.Agents.InteractionFocusModule",
                "SpaceGame.Agents.AgentTargeting",
                "SpaceGame.Agents.AgentController",
                "SpaceGame.Agents.HealthReactionModule",
                "SpaceGame.Gameplay.DialogInteraction",
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

        /// <summary>
        /// Puts the nomad in random-pool mode with his own lines. Written through SerializedObject
        /// because these are private [SerializeField]s -- reaching them any other way means making
        /// gameplay fields public for the benefit of an editor script.
        /// </summary>
        private static void ConfigureDialog(GameObject root)
        {
            var dialog = FindComponent(root, "SpaceGame.Gameplay.DialogInteraction");
            if (dialog == null)
            {
                Debug.LogWarning("[NomadPrefabBuilder] No DialogInteraction; the Nomad will not talk.");
                return;
            }

            var so = new SerializedObject(dialog);

            // DialogMode.RandomFromPredefinedPool
            SetEnum(so, "dialogMode", 2);

            var pool = so.FindProperty("predefinedRandomPool");
            if (pool != null)
            {
                pool.arraySize = DialogLines.Length;
                for (int i = 0; i < DialogLines.Length; i++)
                    pool.GetArrayElementAtIndex(i).stringValue = DialogLines[i];
            }

            // SfxId.NpcMumbleFriendly
            SetEnum(so, "voiceId", 401);

            SetBool(so, "loopDialogLines", true);
            SetBool(so, "allowRestartAfterEnd", true);
            SetFloat(so, "popupDuration", 3.0f);
            SetFloat(so, "interactionFocusDuration", 3.0f);

            // Without a cooldown a held interact key walks the whole pool in a few frames.
            SetBool(so, "useDelayBetweenDialogues", true);
            SetFloat(so, "dialogueDelaySeconds", 1.5f);

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Sets the sight-blocking mask. Left at Nothing the module logs a warning every session
        /// and falls back to this same set, so writing it down just makes the prefab say what it
        /// means.
        /// </summary>
        private static void ConfigurePerception(GameObject root)
        {
            var perception = FindComponent(root, "SpaceGame.Agents.PerceptionModule");
            if (perception == null) return;

            int mask = 0;
            foreach (var layer in new[] { "Default", "Ground", "Interior" })
            {
                int index = LayerMask.NameToLayer(layer);
                if (index >= 0) mask |= 1 << index;
                else Debug.LogWarning($"[NomadPrefabBuilder] No '{layer}' layer in this project; " +
                                      "the Nomad's line of sight will ignore it.");
            }

            var so = new SerializedObject(perception);
            SetInt(so, "occlusionLayers", mask);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureHealth(GameObject root)
        {
            var health = FindComponent(root, "SpaceGame.Gameplay.HealthComponent");
            if (health == null)
            {
                Debug.LogWarning("[NomadPrefabBuilder] No HealthComponent; the Nomad cannot be hurt.");
                return;
            }

            var so = new SerializedObject(health);
            SetInt(so, "maxHealth", 100);
            SetInt(so, "currentHealth", 100);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureFaction(GameObject root)
        {
            var faction = FindComponent(root, "SpaceGame.Agents.EntityFaction");
            if (faction == null) return;

            var definition = AssetDatabase.LoadAssetAtPath<Object>(FactionPath);
            var table = AssetDatabase.LoadAssetAtPath<Object>(RelationshipsPath);
            if (definition == null || table == null)
            {
                Debug.LogWarning("[NomadPrefabBuilder] Faction assets missing; the Nomad will read " +
                                 "as factionless and nothing will treat him as friendly.");
                return;
            }

            var so = new SerializedObject(faction);
            SetObject(so, "faction", definition);
            SetObject(so, "relationshipTable", table);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------
        // Serialized-property helpers. Each warns rather than throwing, so a renamed field
        // downgrades to a default-valued prefab instead of aborting the whole build.

        private static SerializedProperty Find(SerializedObject so, string field)
        {
            var prop = so.FindProperty(field);
            if (prop == null)
                Debug.LogWarning($"[NomadPrefabBuilder] {so.targetObject.GetType().Name} has no " +
                                 $"serialized field '{field}' — was it renamed?");
            return prop;
        }

        /// <summary>
        /// Writes an enum field by its UNDERLYING value, not its ordinal.
        /// <para>
        /// `enumValueIndex` is a position in the enum's name list, which is only the same thing
        /// when the enum starts at 0 and has no gaps. SfxId does neither -- NpcMumbleFriendly is
        /// 401 -- so assigning through enumValueIndex there silently picks whatever sound happens
        /// to sit at that ordinal.
        /// </para>
        /// </summary>
        private static void SetEnum(SerializedObject so, string field, int value)
        {
            var p = Find(so, field);
            if (p != null) p.intValue = value;
        }

        private static void SetBool(SerializedObject so, string field, bool value)
        {
            var p = Find(so, field);
            if (p != null) p.boolValue = value;
        }

        private static void SetFloat(SerializedObject so, string field, float value)
        {
            var p = Find(so, field);
            if (p != null) p.floatValue = value;
        }

        private static void SetInt(SerializedObject so, string field, int value)
        {
            var p = Find(so, field);
            if (p != null) p.intValue = value;
        }

        private static void SetObject(SerializedObject so, string field, Object value)
        {
            var p = Find(so, field);
            if (p != null) p.objectReferenceValue = value;
        }

        private static Component FindComponent(GameObject go, string fullName)
        {
            var type = FindType(fullName);
            return type == null ? null : go.GetComponent(type);
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

        /// <summary>
        /// Fetches a component, adding it if absent.
        ///
        /// <para>
        /// Deliberately not `GetComponent&lt;T&gt;() ?? AddComponent&lt;T&gt;()`. `??` tests for real
        /// null, while a missing Unity component comes back as a managed wrapper around a null
        /// native pointer -- so the coalesce sees "not null", skips the AddComponent, and hands
        /// back a reference that throws MissingComponentException on first use. Only `!= null`
        /// runs Unity's overloaded equality, which is what actually knows the object is dead.
        /// </para>
        /// </summary>
        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
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
