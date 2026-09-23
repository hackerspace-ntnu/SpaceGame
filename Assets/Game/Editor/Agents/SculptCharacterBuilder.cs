using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using SpaceGame.Core.Persistence.EditorTools;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Builds the three sculpt-base drifter NPCs -- Human, Alien and Crumpy -- from their
    /// imported FBX.
    ///
    /// <para>
    /// They are one model three ways: the same 24830-vertex sculpt pushed into three shapes, the
    /// same 52-bone Humanoid skeleton, the same object and material names. So they are one
    /// builder and one recipe type, for the reason <see cref="NomadPrefabBuilder"/> is one
    /// builder for nine nomads -- three copies of this wiring is three chances for one of them to
    /// drift out of step with its siblings.
    /// </para>
    ///
    /// <para>
    /// Authored as an editor command rather than hand-written prefab YAML because each rig
    /// carries 52 bones. Writing that hierarchy by hand means inventing 52 stable fileIDs and
    /// their parent links, and one wrong reference produces a prefab that opens with a broken
    /// skeleton and no error.
    /// </para>
    ///
    /// <para>
    /// Idempotent: running it twice overwrites each prefab at the SAME asset path so the GUID
    /// survives. Deleting and recreating would silently null every serialized reference to these
    /// characters, the network prefab list included.
    /// </para>
    ///
    /// <para>
    /// The FBX are exported by hand from the .blend files in
    /// <c>Assets/Game/Art/Models/_Source~/models/characters/sculpt_base/</c>.
    /// Re-export before running this if the art changed.
    /// </para>
    /// </summary>
    public static class SculptCharacterBuilder
    {
        /// <summary>One character built by this pipeline.</summary>
        public sealed class SculptRecipe
        {
            public string Name;
            public string FbxPath;
            public string TexturePath;
            public string PrefabPath;

            /// <summary>
            /// Which <see cref="StylizedEyeBuilder"/> style its eyes wear. The eye materials are
            /// shared across characters rather than baked per character, so giving a drifter a
            /// different look is changing this one string and rebuilding.
            /// </summary>
            public string EyeStyle;

            /// <summary>The flavour lines it says when talked to.</summary>
            public string[] DialogLines;
        }

        // Lower-case "agents" is the real folder on disk, beside Nomad.prefab. macOS resolves
        // either spelling to the same folder, so the capitalised form other builders use is a
        // latent bug rather than a visible one -- it would create a SECOND folder on a
        // case-sensitive filesystem, and AssetDatabase paths are compared as strings.
        private const string CharacterFolder = "Assets/Game/Prefabs/agents/Characters/Drifters";
        private const string ModelFolder = "Assets/Game/Art/Models/Characters";
        private const string MaterialFolder = "Assets/Game/Art/Materials/Characters";

        /// <summary>
        /// How the FBX names its eye material -- <c>alien_eyes</c>, <c>human_eyes</c>,
        /// <c>crumpy_eyes</c>. It is the only thing in the imported model that says which slot is an
        /// eye; see <see cref="ApplySkin"/>.
        /// </summary>
        private const string EyeMaterialSuffix = "_eyes";

        private const string ScenePath = "Assets/Game/Scenes/world/persistentScene.unity";

        /// <summary>The NpcWorldSim template these three walk in.</summary>
        private const string BandId = "drifters";

        /// <summary>
        /// The template whose errands and chatter the band is copied from. The sand nomads', not
        /// the original nomad caravan's: theirs are already written for people on foot rather than
        /// for a string of birds.
        /// </summary>
        private const string TemplateToCopyId = "sand-nomads";

        private const string AnimatorPath = "Assets/Game/Art/Animations/Player/AstronautArmature.controller";
        private const string WalkClipPath = "Assets/Game/Art/Animations/Player/walking.fbx";

        /// <summary>
        /// Peaceful newcomers: <c>defaultStance</c> Neutral and NOT ONE row in
        /// <c>GlobalRelationships.asset</c>, which is how the agent skill spells "peaceful until
        /// hurt". <see cref="ConfigureProvocation"/> is what hands them a target, and only after
        /// someone hits them. Adding a Hostile row here "for completeness" would make every
        /// drifter attack on sight.
        /// </summary>
        private const string FactionPath = "Assets/Game/ScriptableObjects/Factions/Core/DriftersFaction.asset";
        private const string RelationshipsPath = "Assets/Game/ScriptableObjects/Factions/Core/GlobalRelationships.asset";

        /// <summary>
        /// What every character here is scaled to stand. Matches the Nomad and the astronaut --
        /// this world's people are 3 m, and a 1.9 m sculpt dropped in unscaled reads as a child
        /// standing next to them.
        /// </summary>
        private const float TargetHeight = 3.0f;

        private const float BodyRadius = TargetHeight * 0.208f;

        // The clip's actor. Humanoid retargeting scales stride with the skeleton, so a character
        // this much bigger covers proportionally more ground per step.
        private const float ReferenceHumanHeight = 1.7f;

        // Forced by AstronautArmature.controller's own blend-tree sample positions, not picked.
        private const float WalkBlendSample = 4.0f;
        private const float RunBlendSample = 7.2f;

        /// <summary>Drifters amble; they are not patrolling anything.</summary>
        private const float WalkSpeed = 2.4f;

        /// <summary>
        /// Used when the walk clip's root motion has been stripped -- such a clip reports an
        /// averageSpeed of nearly zero, and dividing by that asks the animation to play at a few
        /// percent of rate.
        /// </summary>
        private const float FallbackClipSpeed = 1.35f;

        public static readonly SculptRecipe[] Drifters =
        {
            new SculptRecipe
            {
                Name = "Drifter_Human",
                FbxPath = ModelFolder + "/Human/human.fbx",
                TexturePath = ModelFolder + "/Human/Textures/human_BaseColor.png",
                PrefabPath = CharacterFolder + "/Drifter_Human.prefab",
                EyeStyle = "Ivory",
                DialogLines = new[]
                {
                    "Came down in the same storm you did, near enough.",
                    "Walk where the sand is firm. You learn that fast or you don't.",
                    "I don't want trouble. Neither do you, out here.",
                },
            },
            new SculptRecipe
            {
                Name = "Drifter_Alien",
                FbxPath = ModelFolder + "/Alien/alien.fbx",
                TexturePath = ModelFolder + "/Alien/Textures/alien_BaseColor.png",
                PrefabPath = CharacterFolder + "/Drifter_Alien.prefab",
                EyeStyle = "Amber",
                DialogLines = new[]
                {
                    "Your suit is loud. Everything out here hears it.",
                    "We were here before the wind changed.",
                    "Pass. I have no quarrel with you.",
                },
            },
            new SculptRecipe
            {
                Name = "Drifter_Crumpy",
                FbxPath = ModelFolder + "/Crumpy/crumpy.fbx",
                TexturePath = ModelFolder + "/Crumpy/Textures/crumpy_BaseColor.png",
                PrefabPath = CharacterFolder + "/Drifter_Crumpy.prefab",
                EyeStyle = "Ember",
                DialogLines = new[]
                {
                    "Hhh. You are very tall and very slow.",
                    "Dig where it is cool. Not where it is bright.",
                    "Leave me be and I leave you be.",
                },
            },
        };

        [MenuItem("Tools/SpaceGame/Agents/Build Drifter NPCs")]
        public static void BuildAll()
        {
            var built = new List<GameObject>();
            foreach (var recipe in Drifters)
            {
                var prefab = BuildPrefab(recipe);
                if (prefab != null) built.Add(prefab);
            }

            if (built.Count == 0)
            {
                Debug.LogError("[SculptCharacterBuilder] Nothing was built.");
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Runtime-spawned and carrying a NetworkObject, so all three project-wide wirings have
            // to be asked again. None is optional: an unregistered network prefab fails ONLY on
            // clients, a saveable without its savers reloads as prefab defaults, and a rebuild
            // writes the prefab wholesale so the ragdoll adapter has to be re-applied or the
            // character drops dead standing up.
            Debug.Log(NetworkPrefabRegistrar.Sync(out _, out _));
            SaveableWiring.TryWirePrefabs();
            RagdollWiring.WirePrefabs();

            Debug.Log($"[SculptCharacterBuilder] Built {built.Count} drifter NPCs into {CharacterFolder}.");
        }

        /// <summary>
        /// Puts the three of them in the world as one wandering band, by adding an
        /// <c>NpcGroupTemplate</c> to the <c>NpcWorldSim</c> in the persistent scene.
        ///
        /// <para>
        /// Without this the prefabs exist and nothing ever spawns them. The template is COPIED
        /// from the sand nomads' rather than built from nothing, so the band inherits a working
        /// set of errands and chatter instead of an empty task list that would leave it standing
        /// where it spawned.
        /// </para>
        ///
        /// <para>
        /// It starts near a <c>Camp</c> site rather than at a written-down coordinate: a hardcoded
        /// position is a number that is right until the terrain changes under it, and the site
        /// registry already knows where the ground is walkable.
        /// </para>
        /// </summary>
        [MenuItem("Tools/SpaceGame/Agents/Place Drifter Band")]
        public static void PlaceDrifterBand()
        {
            var prefabs = new List<GameObject>();
            foreach (var recipe in Drifters)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(recipe.PrefabPath);
                if (prefab != null) prefabs.Add(prefab);
            }

            if (prefabs.Count == 0)
            {
                Debug.LogError("[SculptCharacterBuilder] No drifter prefabs to place. Build them first.");
                return;
            }

            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool alreadyOpen = scene.IsValid() && scene.isLoaded;

            // Additive, and without asking to save whatever is open first: an additive open
            // discards nothing, and the open scene is usually dirty here anyway.
            if (!alreadyOpen)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            SpaceGame.Agents.NpcWorldSim sim = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                sim = go.GetComponentInChildren<SpaceGame.Agents.NpcWorldSim>(true);
                if (sim != null) break;
            }

            if (sim == null)
            {
                Debug.LogError($"[SculptCharacterBuilder] No NpcWorldSim in {ScenePath}; the " +
                               "drifters have nothing to spawn them.");
                if (!alreadyOpen) EditorSceneManager.CloseScene(scene, true);
                return;
            }

            var so = new SerializedObject(sim);
            var templates = so.FindProperty("templates");
            int existing = -1, source = -1;
            for (int i = 0; i < templates.arraySize; i++)
            {
                string id = templates.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue;
                if (id == BandId) existing = i;
                if (id == TemplateToCopyId) source = i;
            }

            SerializedProperty template;
            if (existing >= 0)
            {
                template = templates.GetArrayElementAtIndex(existing);
            }
            else if (source >= 0)
            {
                // DuplicateCommand inserts the copy right after the original.
                templates.GetArrayElementAtIndex(source).DuplicateCommand();
                template = templates.GetArrayElementAtIndex(source + 1);
            }
            else
            {
                Debug.LogError($"[SculptCharacterBuilder] No '{TemplateToCopyId}' template to copy " +
                               "the errands from; add the drifter band by hand.");
                if (!alreadyOpen) EditorSceneManager.CloseScene(scene, true);
                return;
            }

            template.FindPropertyRelative("id").stringValue = BandId;
            template.FindPropertyRelative("displayName").stringValue = "Drifters";
            template.FindPropertyRelative("tribe").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Object>(FactionPath);
            template.FindPropertyRelative("runtimeOnly").boolValue = false;
            template.FindPropertyRelative("bountyHunters").boolValue = false;
            template.FindPropertyRelative("useStartPosition").boolValue = false;
            template.FindPropertyRelative("startNearSite").intValue = (int)SpaceGame.World.SiteKind.Camp;

            // Match the record's pace to the members' walk, or the group visibly teleports
            // forward the moment it spawns.
            template.FindPropertyRelative("travelSpeed").floatValue = WalkSpeed;

            var members = template.FindPropertyRelative("members");
            members.arraySize = prefabs.Count;
            for (int i = 0; i < prefabs.Count; i++)
            {
                var member = members.GetArrayElementAtIndex(i);
                member.FindPropertyRelative("prefab").objectReferenceValue = prefabs[i];
                // The role is only read when prefab is empty, but a copied template brings the
                // donor's role along and a stale one is a confusing thing to read in the Inspector.
                member.FindPropertyRelative("isLeader").boolValue = i == 0;
                member.FindPropertyRelative("count").intValue = 1;
            }

            // Three people on foot, walking closer together than a string of pack animals.
            var formation = template.FindPropertyRelative("formation");
            formation.FindPropertyRelative("Lanes").intValue = 2;
            formation.FindPropertyRelative("RowSpacing").floatValue = 3.5f;
            formation.FindPropertyRelative("LaneSpacing").floatValue = 2.4f;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // Leave the editor as it was found. A world scene left open beside Bootstrap is
            // carried into the next Play, where the Bootstrapper reloads it on top of itself.
            if (!alreadyOpen) EditorSceneManager.CloseScene(scene, true);

            Debug.Log($"[SculptCharacterBuilder] {(existing >= 0 ? "Updated" : "Added")} the " +
                      $"'{BandId}' band in {ScenePath} with {prefabs.Count} member(s).");
        }

        /// <summary>
        /// Checks the three things about these characters that fail SILENTLY at runtime, on the
        /// built prefabs rather than on the intent that built them.
        ///
        /// <para>
        /// Worth a command of its own because none of them throws: a downgraded avatar leaves the
        /// character in bind pose with a clean console, a missing EntityFaction makes it invisible
        /// to every targeting module, and an unregistered network prefab works perfectly for the
        /// host and fails only on a client.
        /// </para>
        /// </summary>
        [MenuItem("Tools/SpaceGame/Agents/Verify Drifter NPCs")]
        public static void VerifyAll()
        {
            var problems = new List<string>();

            foreach (var recipe in Drifters)
            {
                var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(recipe.FbxPath);
                if (avatar == null || !avatar.isValid || !avatar.isHuman)
                    problems.Add($"{recipe.Name}: avatar valid={avatar?.isValid} human={avatar?.isHuman} " +
                                 "-- it would stand in bind pose with no error.");

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(recipe.PrefabPath);
                if (prefab == null)
                {
                    problems.Add($"{recipe.Name}: no prefab at {recipe.PrefabPath}.");
                    continue;
                }

                foreach (var required in new[]
                         {
                             "SpaceGame.Agents.AgentController",
                             "SpaceGame.Agents.EntityFaction",
                             "SpaceGame.Agents.AgentTargeting",
                             "SpaceGame.Agents.WanderModule",
                             "SpaceGame.Agents.ProvocationModule",
                             "SpaceGame.Agents.NavMeshAgentMotor",
                             "SpaceGame.Agents.AgentAnimatorDriver",
                             "SpaceGame.Gameplay.HealthComponent",
                             "Unity.Netcode.NetworkObject",
                             "SpaceGame.Core.Persistence.SaveableEntity",
                         })
                {
                    if (FindComponent(prefab, required) == null)
                        problems.Add($"{recipe.Name}: missing {required}.");
                }

                var faction = FindComponent(prefab, "SpaceGame.Agents.EntityFaction");
                if (faction != null)
                {
                    var so = new SerializedObject(faction);
                    if (Find(so, "faction")?.objectReferenceValue == null)
                        problems.Add($"{recipe.Name}: EntityFaction has no FactionDefinition.");
                    if (Find(so, "relationshipTable")?.objectReferenceValue == null)
                        problems.Add($"{recipe.Name}: EntityFaction has no relationship table.");
                }

                var animator = prefab.GetComponentInChildren<Animator>();
                if (animator == null || animator.runtimeAnimatorController == null)
                    problems.Add($"{recipe.Name}: no Animator controller.");

                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                    foreach (var material in renderer.sharedMaterials)
                        if (material == null)
                            problems.Add($"{recipe.Name}: {renderer.name} has an empty material slot.");
            }

            foreach (var offender in AgentNetworkWiring.Offenders())
                if (offender.Contains("Drifter_"))
                    problems.Add($"network wiring: {offender}");

            if (problems.Count == 0)
                Debug.Log($"[SculptCharacterBuilder] Verified {Drifters.Length} drifter NPCs: " +
                          "avatars are Humanoid, factions assigned, agent stack and savers present.");
            else
                Debug.LogError("[SculptCharacterBuilder] " + problems.Count + " problem(s):\n  " +
                               string.Join("\n  ", problems));
        }

        public static GameObject BuildPrefab(SculptRecipe recipe)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(recipe.FbxPath);
            if (fbx == null)
            {
                Debug.LogError($"[SculptCharacterBuilder] No FBX at {recipe.FbxPath}. Export it " +
                               "from the sculpt_base .blend first.");
                return null;
            }

            if (!EnsureHumanoidImport(recipe.FbxPath))
                return null;

            EnsureFolder(CharacterFolder);
            EnsureFolder(MaterialFolder);

            // The agent components live on their own root rather than on the model, because the
            // model has to move relative to them: the sculpt's soles do not sit on its origin,
            // so a model placed straight at the root stands buried. See AlignSoleToRoot.
            var root = new GameObject(recipe.Name);

            GameObject saved;
            bool ok;
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                model.name = "Model";

                // Unpack so our edits live on a real prefab of our own rather than as overrides
                // on the model importer's prefab, which regenerates on every reimport.
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely,
                                                   InteractionMode.AutomatedAction);

                model.transform.SetParent(root.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;

                AlignSoleToRoot(root, model);

                ApplySkin(model, recipe);
                ConfigureAnimator(model);
                ConfigurePhysics(root);
                AddAgentStack(root);
                ConfigureDialog(root, recipe);
                ConfigurePerception(root);
                ConfigureHealth(root);
                ConfigureFaction(root);
                ConfigureWander(root);
                ConfigureProvocation(root);
                ConfigureGait(root);

                // Every component this prefab needs must be added HERE. A rebuild overwrites the
                // asset wholesale, so anything added by hand in the Inspector is silently gone.
                AgentGroundConformWiring.Ensure(root);

                saved = PrefabUtility.SaveAsPrefabAsset(root, recipe.PrefabPath, out ok);
            }
            finally
            {
                // `new GameObject` lands in the open scene, so an exception partway through would
                // otherwise leave a half-built character sitting in whatever scene was open.
                Object.DestroyImmediate(root);
            }

            if (!ok || saved == null)
            {
                Debug.LogError($"[SculptCharacterBuilder] Failed to save {recipe.PrefabPath}.");
                return null;
            }

            saved = CorrectScaleAndSole(saved, recipe.PrefabPath);
            Debug.Log($"[SculptCharacterBuilder] Wrote {recipe.PrefabPath}");
            return saved;
        }

        // ------------------------------------------------------------------
        // Import

        /// <summary>
        /// Forces the FBX to import as Humanoid and confirms the generated avatar is actually
        /// human.
        ///
        /// <para>
        /// This check is the whole reason the method exists. When Unity cannot map a skeleton it
        /// does not fail the import -- it downgrades the avatar to <c>isHuman = false</c>,
        /// <c>isValid = true</c>, and the character then stands in bind pose for ever with a
        /// COMPLETELY CLEAN CONSOLE. The bone names here are Unity's own HumanBodyBones spellings
        /// precisely so the automatic mapping cannot be ambiguous.
        /// </para>
        /// </summary>
        private static bool EnsureHumanoidImport(string fbxPath)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[SculptCharacterBuilder] {fbxPath} has no ModelImporter.");
                return false;
            }

            if (importer.animationType != ModelImporterAnimationType.Human ||
                importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();
            }

            var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(fbxPath);
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError($"[SculptCharacterBuilder] {fbxPath} did not produce a Humanoid " +
                               $"avatar (valid={avatar?.isValid}, human={avatar?.isHuman}). The " +
                               "character would stand in bind pose with no error at runtime.");
                return false;
            }

            return true;
        }

        // ------------------------------------------------------------------
        // Placement on the ground

        /// <summary>
        /// Slides the model up so its lowest geometry sits exactly on the root's origin.
        ///
        /// <para>
        /// Measured off the mesh rather than hard-coded, so a re-export with different
        /// proportions re-derives it -- and the three characters need three different numbers:
        /// the crumpy's lowest vertex sits 32 mm ABOVE its origin while the human's sits 10 mm
        /// below. Nothing in the engine notices; the NavMeshAgent plants the ROOT on the navmesh
        /// and leaves the visible feet hovering or buried.
        /// </para>
        /// </summary>
        private static void AlignSoleToRoot(GameObject root, GameObject model)
        {
            float lowest = float.MaxValue;

            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                if (mesh == null) continue;

                // The mesh's own bounds, walked corner by corner into root space.
                // Renderer.bounds would be simpler and is not trustworthy here -- for a
                // SkinnedMeshRenderer that has never been animated it reports whatever the
                // importer last cached.
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
                }
            }

            if (lowest == float.MaxValue)
            {
                Debug.LogWarning("[SculptCharacterBuilder] No renderers to measure; the model " +
                                 "may stand in the ground.");
                return;
            }

            model.transform.localPosition += Vector3.up * -lowest;
        }

        /// <summary>
        /// Sets the final scale and ground contact, measured on a real instance of the prefab.
        ///
        /// <para>
        /// Measured here rather than derived up front because the only honest measure of a
        /// character's size is what the renderers actually draw once the skeleton has posed them:
        /// <c>mesh.bounds</c> on a SkinnedMeshRenderer describes the mesh in skinning space
        /// rather than where the bones put it.
        /// </para>
        /// </summary>
        private static GameObject CorrectScaleAndSole(GameObject prefab, string prefabPath)
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
                    Debug.Log($"[SculptCharacterBuilder] {prefab.name} drew {rendered:0.###} m; " +
                              $"scaling by {factor:0.####} to stand {TargetHeight:0.##} m tall.");
                }

                // Re-measure: the scale just moved everything, the soles included.
                if (TryMeasure(probe, out low, out high))
                    model.localPosition += Vector3.up * -low;

                PrefabUtility.ApplyPrefabInstance(probe, InteractionMode.AutomatedAction);
                return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
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
        // Look

        /// <summary>
        /// Puts the character's baked base-colour texture onto project materials.
        ///
        /// <para>
        /// The FBX cannot carry these: materials arrive as sub-assets regenerated on every
        /// reimport, so a colour set on one is lost the next time the art changes. Each character
        /// gets its own pair of .mat files instead, named for the character so the three cannot
        /// overwrite each other's.
        /// </para>
        ///
        /// <para>
        /// The eye spheres are a separate renderer with their own slot, and they do NOT take the
        /// body texture: the skin map has nothing painted where the eyes are, so a body material on
        /// an eye gives the character two beads of bare skin in its sockets. Which slot is an eye is
        /// read off the material the FBX itself arrived with -- Blender names it
        /// <c>&lt;character&gt;_eyes</c> -- rather than off the object name, which is the importer's
        /// <c>Sphere</c> / <c>Sphere.001</c> and says nothing.
        /// </para>
        ///
        /// <para>
        /// An eye also gets its UVs rebuilt -- see <see cref="StylizedEyeBuilder.EnsureEyeMesh"/>
        /// for what is wrong with the ones the FBX ships.
        /// </para>
        /// </summary>
        private static void ApplySkin(GameObject model, SculptRecipe recipe)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(recipe.TexturePath);
            if (texture == null)
            {
                Debug.LogWarning($"[SculptCharacterBuilder] No texture at {recipe.TexturePath}; " +
                                 $"{recipe.Name} keeps the FBX's own materials.");
                return;
            }

            var body = EnsureMaterial(recipe.Name, texture);
            var eyes = StylizedEyeBuilder.Load(recipe.EyeStyle);

            int eyeSlots = 0;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool isEye = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    bool eyeSlot = materials[i] != null &&
                                   materials[i].name.EndsWith(EyeMaterialSuffix,
                                                              System.StringComparison.OrdinalIgnoreCase);
                    isEye |= eyeSlot;
                    if (eyeSlot) eyeSlots++;
                    materials[i] = eyeSlot && eyes != null ? eyes : body;
                }

                renderer.sharedMaterials = materials;

                if (isEye)
                    UnwrapEye(model, renderer, recipe);
            }

            // Loud, because the failure it catches is silent: rename the material in the .blend and
            // every slot quietly falls through to the body material, which is the bug this whole
            // split exists to fix, and it looks like nothing happened.
            if (eyeSlots == 0)
                Debug.LogError($"[SculptCharacterBuilder] {recipe.Name}: no slot on the FBX uses a " +
                               $"material ending in '{EyeMaterialSuffix}', so the eyes wear the " +
                               "body skin. Check the material names in the source .blend.");
        }

        /// <summary>
        /// Re-unwraps one eye sphere around the direction the character actually looks.
        ///
        /// <para>
        /// The gaze is taken from the model root's own forward rather than written down as an axis:
        /// the importer's axis conversion decides what the eye's local space ends up being, and a
        /// hardcoded axis would be a number that is right until someone re-exports. It is then
        /// checked against the geometry -- the eyes sit in FRONT of the skull -- because the two
        /// answers come from different places and a disagreement means one of them is wrong.
        /// </para>
        /// </summary>
        private static void UnwrapEye(GameObject model, Renderer eye, SculptRecipe recipe)
        {
            Transform root = model.transform;
            Vector3 gaze = eye.transform.InverseTransformDirection(root.forward);
            Vector3 up = eye.transform.InverseTransformDirection(root.up);

            // The eye object's own origin, not renderer.bounds: a SkinnedMeshRenderer that has never
            // been animated reports whatever bounds the importer guessed. The sphere is centred on
            // its origin, which EnsureEyeMesh re-checks from the vertices anyway.
            Vector3 toEye = root.InverseTransformPoint(eye.transform.position);
            if (toEye.z <= 0f)
                Debug.LogError($"[SculptCharacterBuilder] {recipe.Name}: eye '{eye.name}' sits " +
                               $"{-toEye.z:F3} m BEHIND the model root's forward, so the root is not " +
                               "facing the way the character does and the pupils will end up in the " +
                               "side of its head.");

            string assetName = $"{recipe.Name}_{eye.name}";
            StylizedEyeBuilder.EnsureEyeMesh(eye, gaze, up, assetName);
        }

        private static Material EnsureMaterial(string characterName, Texture2D texture)
        {
            string path = $"{MaterialFolder}/{characterName}_Body.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.mainTexture = texture;
            material.SetFloat("_Smoothness", 0.15f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureAnimator(GameObject model)
        {
            var animator = GetOrAdd<Animator>(model);

            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AnimatorPath);
            if (controller != null)
                animator.runtimeAnimatorController = controller;
            else
                Debug.LogWarning($"[SculptCharacterBuilder] No animator controller at " +
                                 $"{AnimatorPath}; the character will stand in bind pose.");

            // The FBX imports as Humanoid, so the astronaut's mocap clips retarget onto this
            // 52-bone skeleton without a separate avatar.
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        }

        private static void ConfigurePhysics(GameObject root)
        {
            var capsule = GetOrAdd<CapsuleCollider>(root);
            capsule.height = TargetHeight;
            capsule.radius = BodyRadius;
            capsule.center = new Vector3(0f, TargetHeight * 0.5f, 0f);

            var body = GetOrAdd<Rigidbody>(root);
            // The NavMeshAgent owns movement, so physics must not also push this thing around.
            body.isKinematic = true;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            var agent = GetOrAdd<NavMeshAgent>(root);
            agent.radius = BodyRadius;
            agent.height = TargetHeight;
            agent.acceleration = 8f;
            agent.angularSpeed = 120f;
            agent.stoppingDistance = 0.5f;
            agent.autoBraking = true;
            // agent.speed is set by ConfigureGait, which owns the walk/run relationship.
        }

        /// <summary>
        /// The agent stack. Types are resolved by name so this compiles even while parts of the
        /// agent assembly are being edited.
        /// </summary>
        private static void AddAgentStack(GameObject root)
        {
            // Order matters. AddComponent runs Awake immediately in the editor, and both
            // AgentController and AgentTargeting look their dependencies up there -- so a
            // controller added before its movement modules, or targeting added before the
            // faction, warns and caches a null even though the finished prefab holds everything.
            // Dependencies therefore come first and the two discoverers come last.
            var components = new List<string>
            {
                "SpaceGame.Agents.NavMeshAgentMotor",
                "SpaceGame.Agents.AgentAnimatorDriver",
                "SpaceGame.Gameplay.HealthComponent",
                "SpaceGame.Agents.EntityFaction",
                "SpaceGame.Agents.PerceptionModule",
                "SpaceGame.Agents.WanderModule",
                "SpaceGame.Agents.IdleLookAroundModule",
                // Turns to face you when you walk up, before you press anything. Its Neutral
                // default is exactly what DriftersFaction is toward the player.
                "SpaceGame.Agents.WatchModule",
                "SpaceGame.Agents.AlertBroadcaster",
                "SpaceGame.Agents.AlertReceiverModule",
                // Ears: drifters spread out lose line of sight constantly, and the alert radius
                // alone leaves the far ones standing about while the near ones react.
                "SpaceGame.Agents.NoiseReceiverModule",
                // Notices a gun pointed at it, and shows what it thinks about it. Worth having
                // precisely because these are peaceful: the meter is the only thing that makes
                // "you are about to start something" legible before it starts.
                "SpaceGame.Agents.MenaceSensor",
                "SpaceGame.Agents.AggressionTelegraphModule",
                // Lets DialogInteraction stop it and turn it to face whoever is talking.
                "SpaceGame.Agents.InteractionFocusModule",
                // Chase before AgentTargeting: it reads sibling melee ranges to tighten its
                // stopping distance, and AgentTargeting widens acquisition to cover the longest
                // weapon it can find.
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
                // Required by SaveablePolicy for anything carrying an AgentTargeting. Without it
                // the character reloads having forgotten it was ever provoked.
                "SpaceGame.Core.Persistence.AgentStateSaveable",
                // AlertResponseSaveable and NoiseInvestigationSaveable are deliberately NOT
                // listed: SaveablePolicy adds a saver for each of the two modules above during
                // WireAll, the same way ProvocationSaveable already arrives here.
                "SpaceGame.World.Safety.UnderTerrainGuard",
            };

            foreach (var typeName in components)
                AddByName(root, typeName);
        }

        private static void ConfigureDialog(GameObject root, SculptRecipe recipe)
        {
            var dialog = FindComponent(root, "SpaceGame.Gameplay.DialogInteraction");
            if (dialog == null) return;

            var so = new SerializedObject(dialog);

            // DialogMode.RandomFromPredefinedPool
            SetEnum(so, "dialogMode", 2);

            var pool = so.FindProperty("predefinedRandomPool");
            if (pool != null)
            {
                pool.arraySize = recipe.DialogLines.Length;
                for (int i = 0; i < recipe.DialogLines.Length; i++)
                    pool.GetArrayElementAtIndex(i).stringValue = recipe.DialogLines[i];
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
        /// means. The angle and memory fields are left alone -- PerceptionModule already defaults
        /// them to <c>VisionBaseline</c>, which is what VisionBaselineTests checks.
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
                else Debug.LogWarning($"[SculptCharacterBuilder] No '{layer}' layer in this " +
                                      "project; line of sight will ignore it.");
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
                Debug.LogWarning("[SculptCharacterBuilder] No HealthComponent; the character " +
                                 "cannot be hurt.");
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
                Debug.LogWarning("[SculptCharacterBuilder] Faction assets missing; the character " +
                                 "reads as factionless and no targeting module can ever see it.");
                return;
            }

            var so = new SerializedObject(faction);
            SetObject(so, "faction", definition);
            SetObject(so, "relationshipTable", table);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureWander(GameObject root)
        {
            var wander = FindComponent(root, "SpaceGame.Agents.WanderModule");
            if (wander == null) return;

            var so = new SerializedObject(wander);
            SetBool(so, "limitWanderRadius", true);
            SetFloat(so, "wanderRadius", 18f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// What turns a peaceful character into one that fights back, and only after it is hit.
        /// <c>leashRange</c> stays at or under AgentTargeting's loseRange, or it gives up the
        /// chase and re-acquires in a loop.
        /// </summary>
        private static void ConfigureProvocation(GameObject root)
        {
            var provocation = FindComponent(root, "SpaceGame.Agents.ProvocationModule");
            if (provocation == null) return;

            var so = new SerializedObject(provocation);
            SetFloat(so, "leashRange", 30f);
            SetFloat(so, "calmDownDelay", 60f);
            SetInt(so, "damageThreshold", 1);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Ties the three numbers that decide whether the feet skate: how fast the body moves,
        /// which clip the blend tree picks, and how fast that clip plays.
        /// </summary>
        private static void ConfigureGait(GameObject root)
        {
            // What the animation wants: the clip's own ground speed, scaled up by how much bigger
            // this character is than the actor the clip was cut for.
            float clipSpeed = MeasureWalkClipSpeed();
            float strideSpeed = clipSpeed * (TargetHeight / ReferenceHumanHeight);

            // Forced by the blend tree's own sample positions, not picked.
            float runSpeed = WalkSpeed * (RunBlendSample / WalkBlendSample);

            // The agent's speed is the RUN, because the motor derives the walk from it by
            // multiplying down. Setting the walk here instead leaves it with no top gear.
            var agent = root.GetComponent<NavMeshAgent>();
            if (agent != null) agent.speed = runSpeed;

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
                Debug.LogWarning("[SculptCharacterBuilder] No AgentAnimatorDriver; the gait is " +
                                 "left at whatever the prefab had.");
                return;
            }

            // One scale covering both gaits. walkAnimBoost stays at 1 and does no work: it exists
            // to flatter a walk that travels slower than its clip, and this gait has no such gap.
            float toBlend = WalkBlendSample / Mathf.Max(0.01f, WalkSpeed);

            // Match the clip rate to the ground covered. Clamped so a bad stride measurement
            // shows up as a slightly-off gait rather than a frozen or blurred character.
            float playback = Mathf.Clamp(WalkSpeed / Mathf.Max(0.01f, strideSpeed), 0.5f, 2f);

            var driverSo = new SerializedObject(driver);
            SetFloat(driverSo, "animationSpeedMultiplier", toBlend);
            SetFloat(driverSo, "walkAnimBoost", 1f);
            SetFloat(driverSo, "animatorSpeedScale", playback);
            driverSo.ApplyModifiedPropertiesWithoutUndo();
        }

        private static float MeasureWalkClipSpeed()
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(WalkClipPath))
            {
                if (asset is AnimationClip clip && clip.averageSpeed.magnitude > 0.05f)
                    return clip.averageSpeed.magnitude;
            }
            return FallbackClipSpeed;
        }

        // ------------------------------------------------------------------
        // Editor plumbing

        private static SerializedProperty Find(SerializedObject so, string field)
        {
            return so.FindProperty(field);
        }

        private static void SetFloat(SerializedObject so, string field, float value)
        {
            var property = Find(so, field);
            if (property != null) property.floatValue = value;
        }

        private static void SetInt(SerializedObject so, string field, int value)
        {
            var property = Find(so, field);
            if (property != null) property.intValue = value;
        }

        private static void SetBool(SerializedObject so, string field, bool value)
        {
            var property = Find(so, field);
            if (property != null) property.boolValue = value;
        }

        /// <summary>
        /// Writes the enum's VALUE, not its index in the dropdown. <c>enumValueIndex</c> is the
        /// position in the list, which is only the same number for an enum that happens to start
        /// at zero and skip nothing -- <c>SfxId.NpcMumbleFriendly</c> is 401.
        /// </summary>
        private static void SetEnum(SerializedObject so, string field, int value)
        {
            var property = Find(so, field);
            if (property != null) property.intValue = value;
        }

        private static void SetObject(SerializedObject so, string field, Object value)
        {
            var property = Find(so, field);
            if (property != null) property.objectReferenceValue = value;
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
                Debug.LogWarning($"[SculptCharacterBuilder] No type {fullName}; skipped.");
                return;
            }

            // [RequireComponent] may already have added it -- a second AgentTargeting is a real
            // failure mode here.
            if (go.GetComponent(type) == null)
                go.AddComponent(type);
        }

        private static System.Type FindType(string fullName)
        {
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName);
                if (type != null) return type;
            }
            return null;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
