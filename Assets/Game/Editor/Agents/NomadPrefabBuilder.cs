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

        // The walking staff he carries and fights with. Built by
        // Assets/Game/Art/Models/_Source~/components/props/walking_staff.py; the FBX holds the
        // Coll_Staff_Nomad variation alone.
        private const string StaffFbxPath = "Assets/Game/Art/Models/Weapons/WalkingStaff/walking_staff.fbx";

        // The clip whose stride the walk is matched to, and the rig height it was authored for.
        // Mixamo clips are cut for a roughly 1.7 m actor; Unity's Humanoid retargeting scales the
        // stride to the target skeleton, so the ground speed this animation actually wants is the
        // clip's own speed times how much bigger the Nomad is.
        private const string WalkClipPath = "Assets/Game/Art/Animations/Player/walking.fbx";
        private const float ReferenceHumanHeight = 1.7f;

        // Where the forward walk and run clips sit on the AstronautArmature "Move" blend tree
        // (2-D freeform: walk at y = 4, run at y = 7.2). Feeding SpeedY exactly these is what puts
        // the Nomad on one clip or the other with nothing bleeding in between.
        private const float WalkBlendSample = 4.0f;
        private const float RunBlendSample = 7.2f;

        // Ground speed at cruise — the one number to change if he should cover ground differently.
        // Everything else about the gait is derived from it.
        //
        // He is a 3 m character, so this is slower in body-lengths than it reads in metres: 2.6 m/s
        // on a 3 m frame is the same gait as roughly 1.5 m/s on a person.
        private const float WalkSpeed = 2.6f;

        // Fallback stride speed if the walk clip turns out to be authored in place (root motion
        // stripped), which makes averageSpeed useless. An ordinary human walk.
        private const float FallbackClipSpeed = 1.35f;

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
                ConfigureWatch(root);
                AttachStaff(model);
                ConfigureCombat(root);
                ConfigureProvocation(root);
                ConfigureGait(root);
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

            // After the body has been sized, never before: the staff hangs inside the hierarchy
            // CorrectScaleAndSole rescales, so it has to be measured against the finished character.
            saved = CorrectStaff(saved);

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
                if (IsHeldProp(renderer.transform)) continue;
                low = Mathf.Min(low, renderer.bounds.min.y);
                high = Mathf.Max(high, renderer.bounds.max.y);
            }
            return low != float.MaxValue;
        }

        /// <summary>
        /// True for anything hanging off a hand rather than being part of the body.
        ///
        /// <para>
        /// This exists because <see cref="CorrectScaleAndSole"/> sizes the character by the extent
        /// of everything it renders, and the staff is nearly two metres of that extent held out at
        /// chest height. Counted in, the Nomad "measures" far taller than he is and gets scaled
        /// down to compensate — a character who shrinks every time he is given something to carry,
        /// with no error anywhere to say why.
        /// </para>
        /// </summary>
        private static bool IsHeldProp(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
                if (p.name == StaffObjectName)
                    return true;
            return false;
        }

        private const string StaffObjectName = "WalkingStaff";

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
                // Turns to face you when you walk up, before you press anything. Reused rather
                // than written: WatchModule already is "stop and face whoever is inside this
                // radius", and its Neutral default is exactly what NPCFaction is toward the
                // player. See ConfigureWatch.
                "SpaceGame.Agents.WatchModule",
                // Lets DialogInteraction stop him and turn him to face whoever is talking.
                "SpaceGame.Agents.InteractionFocusModule",
                // He is peaceful, so these two never claim a frame — both do nothing at all
                // without a target, and NPCFaction is Neutral toward the player so AgentTargeting
                // never acquires one. ProvocationModule is what hands him a target, and only
                // after someone hits him. See ConfigureProvocation.
                //
                // CloseCombat before Chase, and both before AgentTargeting, for the same
                // Awake-ordering reason as the rest of this list: Chase reads sibling melee ranges
                // to tighten its stopping distance, and AgentTargeting widens its acquisition
                // range to cover the longest weapon it can find.
                "SpaceGame.Agents.CloseCombatModule",
                "SpaceGame.Agents.ChaseModule",
                "SpaceGame.Agents.AgentTargeting",
                "SpaceGame.Agents.ProvocationModule",
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
                // Required by SaveablePolicy for anything carrying an AgentTargeting, which is
                // every agent here. Without it the creature reloads having forgotten who it was
                // fighting — which now includes forgetting that it was provoked at all.
                "SpaceGame.Core.Persistence.AgentStateSaveable",
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
        // Weapon

        /// <summary>
        /// Parents the walking staff to the right hand, sized to the hand that holds it.
        ///
        /// <para>
        /// A visual-only FBX rather than one of the gun prefabs from
        /// <c>Prefabs/Items/Artifacts/Guns</c>: those carry NetworkObject, PickupableItem and
        /// DropItemPhysics, because they are things lying in the world waiting to be picked up.
        /// Parenting one to a bone nests a NetworkObject inside the Nomad's own and hangs a
        /// droppable physics item off an NPC. An equipped visual must be inert.
        /// </para>
        ///
        /// <para>
        /// The staff's origin is its GRIP, not its butt — see walking_staff_BUILD.md — so the
        /// local position is zero and the hand does not need an offset looked up per model.
        /// </para>
        /// </summary>
        private static void AttachStaff(GameObject model)
        {
            var staffFbx = AssetDatabase.LoadAssetAtPath<GameObject>(StaffFbxPath);
            if (staffFbx == null)
            {
                Debug.LogWarning($"[NomadPrefabBuilder] No staff FBX at {StaffFbxPath}; the Nomad " +
                                 "will fight bare-handed. Run walking_staff_export.py.");
                return;
            }

            Transform hand = FindBone(model.transform, "RightHand");
            if (hand == null)
            {
                Debug.LogWarning("[NomadPrefabBuilder] No right-hand bone found; the staff is not " +
                                 "attached. Check the mixamorig bone names in the FBX.");
                return;
            }

            var staff = (GameObject)PrefabUtility.InstantiatePrefab(staffFbx);
            PrefabUtility.UnpackPrefabInstance(staff, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);
            staff.name = StaffObjectName;
            staff.transform.SetParent(hand, false);
            staff.transform.localPosition = Vector3.zero;

            // Scale and orientation are deliberately NOT set here — see CorrectStaff, which runs
            // after the prefab has been saved and re-measured. Doing it now produces a staff that
            // is silently wrong twice over: CorrectScaleAndSole multiplies the whole Model's scale
            // to fit the character to 3 m, and the staff hangs inside that hierarchy, so any size
            // chosen here is multiplied by a factor that has not been computed yet.
        }

        // How much of the character's height the staff spans. A walking staff comes to roughly
        // chest height on its owner; below that it reads as a cane, above it as a spear.
        private const float StaffLengthFraction = 0.62f;

        /// <summary>
        /// Stands the staff up in the fist and sizes it against the finished character.
        ///
        /// <para>
        /// Runs on a real instance of the SAVED prefab, for the same reason
        /// <see cref="CorrectScaleAndSole"/> does: the only honest measure of how big anything is
        /// here is what the renderers actually draw once the skeleton has posed them and every
        /// scale factor in the chain has been applied. The staff sits under the hand, under the
        /// armature, under the Model — a Blender FBX arrives with a factor of 100 on it, and the
        /// model root carries its own measured fit-to-3-metres factor on top.
        /// </para>
        ///
        /// <para>
        /// The orientation is COMPUTED rather than dialled in as Euler angles. The staff is
        /// modelled along +Z in Blender and the exporter's Z-up to Y-up conversion lands that on
        /// local +Y; the hand bone it hangs off has its own arbitrary rest orientation. Guessing an
        /// Euler triple that reconciles the two produced a staff lying diagonally across the
        /// character — measuring the shaft's actual world direction and rotating it onto vertical
        /// cannot be wrong in the same way, and survives a re-export that changes either.
        /// </para>
        /// </summary>
        private static GameObject CorrectStaff(GameObject prefab)
        {
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                probe.transform.position = Vector3.zero;
                probe.transform.rotation = Quaternion.identity;

                var animator = probe.GetComponentInChildren<Animator>();
                if (animator != null) animator.Rebind();

                Transform staff = FindBone(probe.transform, StaffObjectName);
                if (staff == null) return prefab;

                // Stand the shaft upright: crown above the fist, butt hanging toward the ground,
                // which is how a staff is carried when it is not being swung.
                if (!TryFindShaftAxis(staff, out Vector3 crownward))
                {
                    Debug.LogWarning("[NomadPrefabBuilder] Could not find the staff's long axis; " +
                                     "leaving its orientation alone.");
                    return prefab;
                }

                staff.rotation = Quaternion.FromToRotation(staff.TransformDirection(crownward),
                                                           Vector3.up) * staff.rotation;
                staff.localPosition = Vector3.zero;

                if (!TryMeasureStaff(staff, out Bounds bounds) || bounds.size.y <= 1e-4f)
                {
                    Debug.LogWarning("[NomadPrefabBuilder] Could not measure the staff; it keeps " +
                                     "the FBX's own scale. Check it by hand in the prefab.");
                    return prefab;
                }

                // Vertical extent is the true length now that the shaft has been stood up. Taking
                // the largest axis of an un-oriented box would measure the diagonal instead.
                float target = TargetHeight * StaffLengthFraction;
                float factor = target / bounds.size.y;
                staff.localScale *= factor;

                Debug.Log($"[NomadPrefabBuilder] Staff drew {bounds.size.y:0.###} m; scaled by " +
                          $"{factor:0.####} to {target:0.##} m and stood upright in the right hand.");

                PrefabUtility.ApplyPrefabInstance(probe, InteractionMode.AutomatedAction);
                return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }

        /// <summary>
        /// The staff's own long axis, in its local space, pointing from the grip toward the crown.
        ///
        /// <para>
        /// Derived from the geometry rather than assumed, because assuming it was wrong: the staff
        /// is modelled along +Z in Blender and the exporter's Z-up to Y-up conversion was expected
        /// to land that on local +Y. It does not, and standing the wrong axis up produced a staff
        /// scaled to 42 m across — the length check passed, because the code had just forced the
        /// axis it measured to the target and the real shaft was left to grow with it.
        /// </para>
        ///
        /// <para>
        /// Two things are read off the mesh. The LONGEST local extent is the shaft. Its SIGN comes
        /// from the origin being the grip: the butt is 1.32 m from it and the crown only 0.30 m, so
        /// whichever end is farther away is the butt, and the crown is the other way.
        /// </para>
        /// </summary>
        private static bool TryFindShaftAxis(Transform staff, out Vector3 crownward)
        {
            crownward = Vector3.up;

            bool any = false;
            Bounds local = default;
            foreach (var renderer in staff.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                if (mesh == null) continue;

                // Mesh corners walked into the staff's own space, so the result is independent of
                // however the hand bone happens to be oriented this frame.
                Matrix4x4 toStaff = staff.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                Bounds mb = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3(
                        (i & 1) == 0 ? -1f : 1f,
                        (i & 2) == 0 ? -1f : 1f,
                        (i & 4) == 0 ? -1f : 1f));
                    Vector3 p = toStaff.MultiplyPoint3x4(corner);
                    if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
                    else local.Encapsulate(p);
                }
            }

            if (!any) return false;

            int axis = 0;
            if (local.size.y > local.size.x) axis = 1;
            if (local.size.z > local.size[axis]) axis = 2;

            Vector3 dir = Vector3.zero;
            dir[axis] = Mathf.Abs(local.max[axis]) > Mathf.Abs(local.min[axis]) ? -1f : 1f;
            crownward = dir;
            return true;
        }

        private static bool TryMeasureStaff(Transform staff, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var renderer in staff.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return any;
        }

        /// <summary>
        /// Finds a bone by suffix, so `mixamorig:RightHand` and a re-exported `RightHand` both
        /// resolve. Matching on the full name is what breaks silently when a rig is re-exported
        /// under a different namespace prefix.
        /// </summary>
        private static Transform FindBone(Transform root, string suffix)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n == suffix || n.EndsWith(":" + suffix) || n.EndsWith("_" + suffix))
                    return t;
            }
            return null;
        }

        /// <summary>
        /// Makes him notice you and turn to face you when you walk up.
        ///
        /// <para>
        /// The player's Interactor casts 5 m, so that is what "conversation distance" means here.
        /// The radius is set a little wider than that on purpose: turning to face you exactly as
        /// the interact prompt appears reads as the world reacting to a UI event, whereas being
        /// already turned by the time you can talk reads as him having seen you coming.
        /// </para>
        ///
        /// <para>
        /// Priority is set explicitly rather than left to WatchModule's own <c>Reset</c>, which
        /// Unity does not call for a component added from a script. The serialized default is
        /// Fallback (0) — the same band as WanderModule — and two modules tied at the same priority
        /// resolve by component order, so he would have gone on wandering past you about half the
        /// time depending on nothing anyone could see. Ambient (10) also puts him below Chase (20),
        /// so a provoked Nomad does not stop to politely face the person he is fighting.
        /// </para>
        /// </summary>
        private static void ConfigureWatch(GameObject root)
        {
            var watch = FindComponent(root, "SpaceGame.Agents.WatchModule");
            if (watch == null)
            {
                Debug.LogWarning("[NomadPrefabBuilder] No WatchModule; he will ignore you until " +
                                 "you actually interact with him.");
                return;
            }

            var so = new SerializedObject(watch);
            SetInt(so, "priority", 10);                 // ModulePriority.Ambient
            SetEnum(so, "requiredRelationship", 0);     // FactionRelationship.Neutral
            SetFloat(so, "detectRadius", InteractorCastDistance + 1f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // What the player's Interactor casts, from Interactor._castDistance. If that changes, the
        // Nomad's noticing distance should follow it rather than drifting apart silently.
        private const float InteractorCastDistance = 5f;

        // ------------------------------------------------------------------
        // Temperament and combat

        /// <summary>
        /// What he does once provoked: close the distance and swing the staff.
        ///
        /// <para>
        /// Both modules are inert until something hands AgentTargeting a target, which only
        /// ProvocationModule ever does for him. There is no "peaceful mode" to leave — a module
        /// with no target returns no intent, and the frame falls through to WanderModule.
        /// </para>
        /// </summary>
        private static void ConfigureCombat(GameObject root)
        {
            var melee = FindComponent(root, "SpaceGame.Agents.CloseCombatModule");
            if (melee != null)
            {
                var so = new SerializedObject(melee);
                // Reach is the staff plus the arm, measured off the model rather than guessed:
                // the staff is 62% of a 3 m character and he swings it from the shoulder.
                SetFloat(so, "attackRange", TargetHeight * StaffLengthFraction + 0.9f);
                SetFloat(so, "attackCooldown", 1.1f);
                // A stick swung hard by someone who lives outdoors. Less than the Golem's 45 —
                // this is a warning, not an execution.
                SetInt(so, "attackDamage", 18);
                // How long he stays rooted after a swing so Chase cannot walk him out of his own
                // animation. Short, because the swing itself is short: this must not outlast the
                // clip, or the attack ends with him standing still waiting for a timer.
                SetFloat(so, "attackCommitDuration", 0.32f);
                // The trigger AstronautArmature actually declares, misspelling and all.
                SetString(so, "attackAnimTrigger", "Meele");
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var chase = FindComponent(root, "SpaceGame.Agents.ChaseModule");
            if (chase != null)
            {
                var so = new SerializedObject(chase);
                // Inside swing range with daylight to spare. ChaseModule tightens this itself
                // against the melee range at Awake; setting it here means the prefab says what it
                // means rather than relying on that.
                SetFloat(so, "chaseStopDistance", 1.6f);
                // 1.0, not the 1.3 default. ChaseModule already asks for isRunning, which gets him
                // the full agent speed; multiplying on top of that would push him past the run
                // clip's blend sample and he would skate toward you.
                SetFloat(so, "chaseSpeedMultiplier", 1f);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// Peaceful until hurt. The leash, not a fixed timer, is what decides when he forgives.
        /// </summary>
        private static void ConfigureProvocation(GameObject root)
        {
            var provocation = FindComponent(root, "SpaceGame.Agents.ProvocationModule");
            if (provocation == null)
            {
                Debug.LogWarning("[NomadPrefabBuilder] No ProvocationModule; the Nomad will stand " +
                                 "and take hits without ever fighting back.");
                return;
            }

            var so = new SerializedObject(provocation);
            // Shorter than the Golem's 45 m. He is a person with a stick, not a territorial
            // animal: back off across the camp and he lets it go, and at walk speed he could
            // never close a 45 m leash anyway.
            SetFloat(so, "leashRange", 30f);
            SetFloat(so, "calmDownDelay", 60f);
            SetInt(so, "damageThreshold", 1);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------
        // Gait

        /// <summary>
        /// Sets the walk, the run he uses when provoked, and the rate the clips play at.
        ///
        /// <para>
        /// Three separate things decide how a character reads, and they are easy to confuse because
        /// two of them look like "animation speed" and are not:
        /// </para>
        /// <list type="bullet">
        /// <item><b>Which clip plays.</b> AgentAnimatorDriver feeds SpeedY as velocity times
        /// <c>animationSpeedMultiplier * walkAnimBoost</c>. The blend tree picks a clip from that
        /// number; it does not change how fast the clip runs.</item>
        /// <item><b>How fast he travels.</b> NavMeshAgent speed, scaled by the motor's walk
        /// multiplier when the intent is a walk.</item>
        /// <item><b>How fast the clips play.</b> <c>animatorSpeedScale</c>, and it is GLOBAL — it
        /// scales the attack and every other one-shot along with the walk. That is the trap: this
        /// was set to 0.63 to stop a slow walk from skating, and it put the staff swing into slow
        /// motion as a side effect.</item>
        /// </list>
        ///
        /// <para>
        /// Because the third is global, the walk speed and the animation rate cannot be tuned
        /// independently — matched feet require <c>rate = groundSpeed / strideSpeed</c> exactly, and
        /// any other value skates. So they move TOGETHER: walking faster raises the rate, which
        /// speeds the attack up as well. Wanting a faster walk and a faster swing is therefore one
        /// change, not two, and <see cref="WalkSpeed"/> is the single knob for both.
        /// </para>
        ///
        /// <para>
        /// The run comes back for exactly one reason: ChaseModule asks for it, and with no run gear
        /// a provoked Nomad closed at walking pace. Its speed is not chosen freely — it is fixed by
        /// the blend tree, which samples the run clip at 7.2 against the walk's 4.0, so the run must
        /// be 1.8x the walk or the tree lands between clips and he shuffles.
        /// </para>
        /// </summary>
        private static void ConfigureGait(GameObject root)
        {
            // What the animation wants: the clip's own ground speed, scaled up by how much bigger
            // the Nomad is than the actor the clip was cut for. Humanoid retargeting scales stride
            // with the skeleton, so a 3 m character covers proportionally more ground per step.
            float clipSpeed = MeasureWalkClipSpeed();
            float strideSpeed = clipSpeed * (TargetHeight / ReferenceHumanHeight);

            // Forced by the blend tree's own sample positions, not picked.
            float runSpeed = WalkSpeed * (RunBlendSample / WalkBlendSample);

            // The agent's speed is the RUN, because the motor derives the walk from it by
            // multiplying down. Setting the walk here instead is what left him with no top gear.
            var agent = root.GetComponent<NavMeshAgent>();
            if (agent != null)
                agent.speed = runSpeed;

            var motor = FindComponent(root, "SpaceGame.Agents.NavMeshAgentMotor");
            if (motor != null)
            {
                var so = new SerializedObject(motor);
                SetFloat(so, "walkSpeedMultiplier", WalkSpeed / runSpeed);
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var driver = FindComponent(root, "SpaceGame.Agents.AgentAnimatorDriver");
            if (driver == null)
            {
                Debug.LogWarning("[NomadPrefabBuilder] No AgentAnimatorDriver; the gait is left " +
                                 "at whatever the prefab had.");
                return;
            }

            // One scale covering both gaits. walkAnimBoost stays at 1 and does no work: it exists to
            // flatter a walk that travels slower than its clip, and this gait has no such gap. Left
            // above 1 it would put SpeedY past the run sample the moment he walked.
            float toBlend = WalkBlendSample / Mathf.Max(0.01f, WalkSpeed);

            // Match the clip rate to the ground covered. Clamped so a bad stride measurement shows
            // up as a slightly-off gait rather than a frozen or blurred character.
            float playback = Mathf.Clamp(WalkSpeed / Mathf.Max(0.01f, strideSpeed), 0.5f, 2f);

            var driverSo = new SerializedObject(driver);
            SetFloat(driverSo, "animationSpeedMultiplier", toBlend);
            SetFloat(driverSo, "walkAnimBoost", 1f);
            SetFloat(driverSo, "animatorSpeedScale", playback);
            driverSo.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[NomadPrefabBuilder] Gait: walks {WalkSpeed:0.##} m/s, runs " +
                      $"{runSpeed:0.##} m/s when provoked. Stride wants {strideSpeed:0.##} m/s, so " +
                      $"every clip — the staff swing included — plays at {playback:0.###}x. SpeedY " +
                      $"reaches the tree as {WalkSpeed * toBlend:0.##} walking and " +
                      $"{runSpeed * toBlend:0.##} running (samples are {WalkBlendSample:0.##} and " +
                      $"{RunBlendSample:0.##}).");
        }

        /// <summary>
        /// The forward walk clip's authored ground speed, in metres per second.
        ///
        /// <para>
        /// Returns <see cref="FallbackClipSpeed"/> when the clip is authored in place — a clip with
        /// its root motion stripped reports an averageSpeed of nearly zero, and dividing by that
        /// would ask the animation to play at a few percent of rate.
        /// </para>
        /// </summary>
        private static float MeasureWalkClipSpeed()
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(WalkClipPath)
                                    .OfType<AnimationClip>()
                                    .FirstOrDefault(c => !c.name.StartsWith("__preview"));
            if (clip == null)
            {
                Debug.LogWarning($"[NomadPrefabBuilder] No AnimationClip in {WalkClipPath}; " +
                                 $"assuming a {FallbackClipSpeed:0.##} m/s stride.");
                return FallbackClipSpeed;
            }

            // Horizontal only: a walk cycle bobs vertically, and counting that as travel would
            // overstate the stride and make the clip play too slowly.
            Vector3 v = clip.averageSpeed;
            float speed = new Vector2(v.x, v.z).magnitude;

            if (speed < 0.05f)
            {
                Debug.Log($"[NomadPrefabBuilder] '{clip.name}' is authored in place " +
                          $"(averageSpeed {speed:0.###} m/s), so the stride cannot be measured " +
                          $"from it. Assuming {FallbackClipSpeed:0.##} m/s.");
                return FallbackClipSpeed;
            }

            return speed;
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

        private static void SetString(SerializedObject so, string field, string value)
        {
            var p = Find(so, field);
            if (p != null) p.stringValue = value;
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
