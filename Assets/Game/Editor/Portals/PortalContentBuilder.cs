// Builds every Unity-side asset the portal gun needs, from the shaders and the
// imported FBX.
//
// Written as a builder rather than authored by hand for the reason the rest of
// this project's *Builder scripts exist: the wiring is long, exact, and easy to
// get subtly wrong — which material slot the fluid is, where the muzzle sits,
// which quad is the aperture and which the halo — and a script says all of that
// out loud where a .prefab file says it in GUIDs.
//
// It is deliberately RE-RUNNABLE and deliberately NON-DESTRUCTIVE about the
// things a person is likely to have tuned. Materials and prefabs it already
// made are updated in place; the grip pose, which only looks right once someone
// has held it, is written once and then left alone. Note the project's own
// warning about builder scripts (see memory on peaceful creatures): a builder
// that overwrites a prefab wholesale eats hand-added components, so this one
// edits the existing prefab contents instead of rebuilding them.
using System.IO;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Portals;

namespace SpaceGame.EditorTools.Portals
{
    public static class PortalContentBuilder
    {
        private const string MaterialFolder = "Assets/Game/Art/Materials/Portal";
        private const string PrefabFolder   = "Assets/Game/Prefabs/Items/Artifacts/Portals";
        private const string ItemFolder     = "Assets/Game/Resources/Items/Artifacts";
        private const string GunFbx         = "Assets/Game/Art/Models/Items/portal_gun.fbx";

        // Named to match the FBX's own material slots, so the remap below is a
        // substitution rather than a rename — and so PortalGunItem, which finds
        // the two fluids by material name, keeps working against either.
        //
        // The names still say Orange and Blue because they are the identifiers
        // baked into the exported mesh, not a claim about colour: both are now
        // filled with yellow, matching the apertures they charge. Renaming them
        // means re-exporting the model from Blender and re-pointing the remap,
        // which is churn for no gain — nothing reads these but the remap table.
        private const string PrimaryFluidName   = "Mat_Emissive_Portal_Orange";
        private const string SecondaryFluidName = "Mat_Emissive_Portal_Blue";

        /// <summary>Seconds an aperture the gun opens stays open before it irises shut.</summary>
        private const float PortalLifetime = 20f;

        // Both apertures are YELLOW. An earlier pass had one orange and one blue,
        // and two saturated complementary colours across the same screen read as
        // clip art rather than as one piece of technology. They are told apart by
        // VALUE instead: the primary is a deep saturated gold, the secondary a
        // pale lemon. Same hue family, unmistakably different at a glance.
        private static readonly Color Gold  = new Color(1.00f, 0.76f, 0.10f);
        private static readonly Color Lemon = new Color(1.00f, 0.91f, 0.54f);

        // The hot core both apertures burn to at the rim. Shared, because a
        // discharge that bright is nearly white whatever colour it started as.
        private static readonly Color Hot   = new Color(1.00f, 0.96f, 0.72f);

        // The deep end of the same hue, for the throat of the aperture and the
        // shadowed half of anything graded into portal colour.
        private static readonly Color Deep  = new Color(0.34f, 0.16f, 0.01f);

        [MenuItem("SpaceGame/Portals/Build Portal Gun Content")]
        public static void Build()
        {
            EnsureFolder(MaterialFolder);
            EnsureFolder(PrefabFolder);

            Material surfacePrimary   = BuildSurfaceMaterial("PortalSurface_Primary", Gold);
            Material surfaceSecondary = BuildSurfaceMaterial("PortalSurface_Secondary", Lemon);
            Material rimPrimary       = BuildRimMaterial("PortalRim_Primary", Gold);
            Material rimSecondary     = BuildRimMaterial("PortalRim_Secondary", Lemon);
            // The reservoir columns run from z 0.092 to 0.240 in the gun mesh's
            // own space — the numbers portal_gun.py builds them at. The blob is
            // a unit sphere, so it keeps the centred default.
            Material fluidPrimary   = BuildFluidMaterial(PrimaryFluidName, Gold, 0.092f, 0.240f);
            Material fluidSecondary = BuildFluidMaterial(SecondaryFluidName, Lemon, 0.092f, 0.240f);
            Material blob           = BuildFluidMaterial("PortalBlob", Gold);

            RemapGunMaterials(fluidPrimary, fluidSecondary);

            // One prefab per colour: the aperture's tint is baked into its
            // materials rather than set per instance, because a screen-space
            // material with a property block is the one case where Unity's
            // batching quietly drops the override.
            GameObject portalPrimary   = BuildPortalPrefab("PortalPrimary", surfacePrimary, rimPrimary);
            GameObject portalSecondary = BuildPortalPrefab("PortalSecondary", surfaceSecondary, rimSecondary);

            GameObject projectile = BuildProjectilePrefab(blob);
            GameObject gun = BuildGunPrefab(portalPrimary, projectile, fluidPrimary, fluidSecondary);

            BuildItemAsset(gun);
            AddTravellerToPlayers();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Portals] Content built. Primary aperture: " +
                      AssetDatabase.GetAssetPath(portalPrimary) +
                      " · secondary: " + AssetDatabase.GetAssetPath(portalSecondary) +
                      " · gun: " + AssetDatabase.GetAssetPath(gun));
        }

        // ── Materials ──────────────────────────────────────────────────────────

        private static Material BuildSurfaceMaterial(string name, Color colour)
        {
            Material material = EnsureMaterial(name, "SpaceGame/Portal/PortalSurface");
            material.SetColor("_Colour", colour);
            material.SetColor("_DeepColour", Deep);
            material.SetColor("_HotColour", Hot);
            material.SetFloat("_ViewTint", 0.62f);
            material.SetFloat("_EdgeGlow", 3.2f);
            material.SetFloat("_EdgeWidth", 0.22f);
            material.SetFloat("_Throat", 0.45f);
            material.SetFloat("_Swirl", 2.1f);
            material.SetFloat("_Energy", 0.45f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material BuildRimMaterial(string name, Color colour)
        {
            Material material = EnsureMaterial(name, "SpaceGame/Portal/PortalRim");
            material.SetColor("_Colour", colour);
            material.SetColor("_HotColour", Hot);
            material.SetFloat("_Intensity", 3.2f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material BuildFluidMaterial(string name, Color colour,
                                                   float fillMin = -0.5f, float fillMax = 0.5f)
        {
            Material material = EnsureMaterial(name, "SpaceGame/Portal/PortalFluid");
            material.SetColor("_ColourDeep", Color.Lerp(Deep, colour, 0.35f));
            material.SetColor("_ColourBright", Color.Lerp(colour, Hot, 0.55f));
            material.SetColor("_ColourVapour", Deep * 0.35f);

            // The fill axis is the reservoir's own long axis. The gun's meshes
            // come from Blender, where up is +Z, and the FBX keeps that in the
            // mesh's local space — the Y-up conversion lives on the root's
            // rotation, not in the vertices.
            material.SetVector("_FillAxis", new Vector4(0f, 0f, 1f, 0f));
            material.SetFloat("_FillMin", fillMin);
            material.SetFloat("_FillMax", fillMax);
            material.SetFloat("_Emission", 2.0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private const string ShaderFolder = "Assets/Game/Art/Shaders/Portal";

        /// <summary>
        /// The portal shader with this name.
        ///
        /// Loaded from its own path first, and only then looked up by name.
        /// Shader.Find answers from the runtime's registered shader list, which
        /// is not populated for a shader imported earlier in the same editor
        /// session — so a builder run straight after adding the shaders would
        /// silently fall back and produce unlit magenta portals.
        /// </summary>
        private static Shader FindPortalShader(string shaderName)
        {
            string file = shaderName.Substring(shaderName.LastIndexOf('/') + 1);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>($"{ShaderFolder}/{file}.shader");
            return shader != null ? shader : Shader.Find(shaderName);
        }

        private static Material EnsureMaterial(string name, string shaderName)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Shader shader = FindPortalShader(shaderName);

            if (shader == null)
            {
                // Almost always a compile error in the shader, which Unity
                // reports separately and which would otherwise show up here as
                // a magenta portal with no explanation.
                Debug.LogError($"[Portals] Shader '{shaderName}' not found. " +
                               "Check the console for shader compilation errors.");
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                if (existing.shader != shader) existing.shader = shader;
                return existing;
            }

            var material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// Point the gun FBX's two fluid slots at the animated materials.
        ///
        /// Through the importer's remap table rather than by assigning materials
        /// on a prefab instance: the FBX is re-exported from Blender whenever the
        /// model changes, and a prefab-level override is lost on reimport while a
        /// remap survives it.
        /// </summary>
        private static void RemapGunMaterials(Material primary, Material secondary)
        {
            var importer = AssetImporter.GetAtPath(GunFbx) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[Portals] {GunFbx} is not imported yet; " +
                                 "run this again once Unity has finished importing it.");
                return;
            }

            // Only reimport when something actually changed. SaveAndReimport is
            // synchronous and invalidates every asset loaded from this file, so
            // calling it unconditionally in the middle of a build makes the next
            // LoadAssetAtPath of the FBX — and of the prefab that instances it —
            // come back null for the rest of the run. That is exactly how a
            // re-run of this builder reported the model as missing and skipped
            // rebuilding the gun.
            var primaryId = new AssetImporter.SourceAssetIdentifier(typeof(Material), PrimaryFluidName);
            var secondaryId = new AssetImporter.SourceAssetIdentifier(typeof(Material), SecondaryFluidName);

            bool changed = false;
            System.Collections.Generic.Dictionary<AssetImporter.SourceAssetIdentifier, Object> map =
                importer.GetExternalObjectMap();

            if (!map.TryGetValue(primaryId, out Object mappedPrimary) || mappedPrimary != primary)
            {
                importer.AddRemap(primaryId, primary);
                changed = true;
            }

            if (!map.TryGetValue(secondaryId, out Object mappedSecondary) || mappedSecondary != secondary)
            {
                importer.AddRemap(secondaryId, secondary);
                changed = true;
            }

            if (changed) importer.SaveAndReimport();
        }

        // ── The aperture ───────────────────────────────────────────────────────

        private static GameObject BuildPortalPrefab(string name, Material surface, Material rim)
        {
            string path = $"{PrefabFolder}/{name}.prefab";
            GameObject root = LoadOrCreateRoot(path, name);

            Portal portal = Ensure<Portal>(root);

            Transform surfaceQuad = EnsureQuad(root.transform, "Surface", surface);
            Transform rimQuad = EnsureQuad(root.transform, "Rim", rim);

            // The halo sits a centimetre in front so it is never z-fighting with
            // the aperture it surrounds, and behind nothing else — it writes no
            // depth, so it cannot occlude the view through the hole.
            rimQuad.localPosition = new Vector3(0f, 0f, 0.01f);

            Transform volume = EnsureChild(root.transform, "TravellerVolume");
            BoxCollider box = Ensure<BoxCollider>(volume.gameObject);
            box.isTrigger = true;

            var serialized = new SerializedObject(portal);
            serialized.FindProperty("surfaceRenderer").objectReferenceValue =
                surfaceQuad.GetComponent<MeshRenderer>();
            serialized.FindProperty("rimRenderer").objectReferenceValue =
                rimQuad.GetComponent<MeshRenderer>();
            serialized.FindProperty("travellerVolume").objectReferenceValue = box;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return SaveRoot(root, path);
        }

        private static Transform EnsureQuad(Transform parent, string name, Material material)
        {
            Transform child = parent.Find(name);
            if (child == null)
            {
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = name;

                // A primitive arrives with a collider. On a portal surface that
                // is a solid pane across the opening, which stops everything the
                // portal exists to let through.
                Object.DestroyImmediate(quad.GetComponent<Collider>());

                quad.transform.SetParent(parent, false);
                child = quad.transform;
            }

            var renderer = child.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return child;
        }

        // ── The blob ───────────────────────────────────────────────────────────

        private static GameObject BuildProjectilePrefab(Material blobMaterial)
        {
            string path = $"{PrefabFolder}/PortalBlob.prefab";
            GameObject root = LoadOrCreateRoot(path, "PortalBlob");

            PortalProjectile projectile = Ensure<PortalProjectile>(root);

            Transform body = root.transform.Find("Body");
            if (body == null)
            {
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = "Body";
                Object.DestroyImmediate(sphere.GetComponent<Collider>());
                sphere.transform.SetParent(root.transform, false);
                sphere.transform.localScale = Vector3.one * 0.16f;
                body = sphere.transform;
            }

            var renderer = body.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = blobMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Transform lightChild = EnsureChild(root.transform, "Glow");
            Light light = Ensure<Light>(lightChild.gameObject);
            light.type = LightType.Point;
            light.range = 4f;
            light.intensity = 3f;
            light.shadows = LightShadows.None;

            var serialized = new SerializedObject(projectile);
            serialized.FindProperty("blobRenderer").objectReferenceValue = renderer;
            serialized.FindProperty("blobLight").objectReferenceValue = light;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return SaveRoot(root, path);
        }

        // ── The gun ────────────────────────────────────────────────────────────

        private static GameObject BuildGunPrefab(GameObject portalPrefab, GameObject projectilePrefab,
                                                 Material fluidPrimary, Material fluidSecondary)
        {
            string path = $"{PrefabFolder}/PortalGun.prefab";
            GameObject root = LoadOrCreateRoot(path, "PortalGun");

            Transform model = root.transform.Find("Model");
            if (model == null)
            {
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(GunFbx);
                if (fbx == null)
                {
                    Debug.LogError($"[Portals] {GunFbx} is missing. Export it from " +
                                   "components/props/portal_gun_export.py first.");
                    return SaveRoot(root, path);
                }

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                instance.name = "Model";
                instance.transform.SetParent(root.transform, false);

                // The FBX root's own rotation is left exactly as imported. It
                // carries the Blender-to-Unity conversion, and forcing it to
                // identity lays the gun on its back — the fastest way to spot
                // that having happened is bounds with Y and Z swapped.
                model = instance.transform;
            }

            // The markers exported alongside the mesh exist only to carry two
            // coordinates across the FBX. Turned into plain transforms on the
            // prefab root, they become the muzzle and the grip; left as meshes,
            // they would be two 4 mm cubes floating in the model.
            Transform muzzle = AdoptMarker(root.transform, model, "Marker_Muzzle", "Muzzle");
            Transform grip = AdoptMarker(root.transform, model, "Marker_Grip", "GripPoint");

            var item = Ensure<PortalGunItem>(root);
            var itemGrip = root.GetComponent<ItemGrip>();
            bool gripIsNew = itemGrip == null;
            if (gripIsNew) itemGrip = root.AddComponent<ItemGrip>();

            // Explicitly the gun mesh: GetComponentInChildren returns whichever
            // renderer Unity reaches first, and the marker cubes are siblings of
            // it in the same FBX.
            Renderer bodyRenderer = null;
            foreach (MeshRenderer candidate in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (candidate.name.StartsWith("Marker_")) continue;
                bodyRenderer = candidate;
                break;
            }

            var serializedItem = new SerializedObject(item);
            serializedItem.FindProperty("muzzle").objectReferenceValue = muzzle;
            serializedItem.FindProperty("portalPrefab").objectReferenceValue =
                portalPrefab.GetComponent<Portal>();
            serializedItem.FindProperty("projectilePrefab").objectReferenceValue =
                projectilePrefab.GetComponent<PortalProjectile>();
            serializedItem.FindProperty("bodyRenderer").objectReferenceValue = bodyRenderer;
            serializedItem.FindProperty("primaryMaterialName").stringValue = PrimaryFluidName;
            serializedItem.FindProperty("secondaryMaterialName").stringValue = SecondaryFluidName;
            serializedItem.FindProperty("portalLifetime").floatValue = PortalLifetime;
            serializedItem.ApplyModifiedPropertiesWithoutUndo();

            // Written once. How a thing sits in a hand is judged by looking at
            // it, so re-running the builder must not undo the look.
            if (gripIsNew)
            {
                var serializedGrip = new SerializedObject(itemGrip);
                serializedGrip.FindProperty("gripPoint").objectReferenceValue = grip;
                serializedGrip.FindProperty("holdSize").floatValue = 0.42f;
                serializedGrip.ApplyModifiedPropertiesWithoutUndo();
            }

            // Keep the two fluid slots pointed at the animated materials even if
            // the FBX was imported before the remap existed.
            if (bodyRenderer != null)
            {
                Material[] materials = bodyRenderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    if (materials[i].name.Contains(PrimaryFluidName)) materials[i] = fluidPrimary;
                    else if (materials[i].name.Contains(SecondaryFluidName)) materials[i] = fluidSecondary;
                }
                bodyRenderer.sharedMaterials = materials;
            }

            return SaveRoot(root, path);
        }

        /// <summary>
        /// Turn an exported marker mesh into a bare transform on the prefab root.
        ///
        /// Reparented to the ROOT, not left under the model: the model carries
        /// the FBX's Blender-axis rotation, so a transform under it points along
        /// Blender's axes. On the root, +Z is the way the horn points, which is
        /// what the projectile and the grip pose both assume.
        /// </summary>
        private static Transform AdoptMarker(Transform root, Transform model,
                                             string markerName, string wantedName)
        {
            Transform existing = root.Find(wantedName);

            Transform marker = FindDeep(model, markerName);
            if (marker != null)
            {
                Vector3 local = root.InverseTransformPoint(marker.position);

                if (existing == null) existing = EnsureChild(root, wantedName);
                existing.localPosition = local;
                existing.localRotation = Quaternion.identity;

                // Hidden, not deleted. The model is a prefab instance of the
                // FBX and Unity forbids removing a child of one — and keeping
                // the link is what lets a re-export from Blender reach the
                // prefab at all. A disabled renderer is also skipped by
                // EquipItemSocket's bounds measurement, so the 4 mm cube cannot
                // quietly influence how large the gun is held.
                var markerRenderer = marker.GetComponent<MeshRenderer>();
                if (markerRenderer != null) markerRenderer.enabled = false;
            }
            else if (existing == null)
            {
                // Model exported without markers — fall back to the root rather
                // than leaving a null reference that fails at fire time.
                existing = EnsureChild(root, wantedName);
            }

            return existing;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name) return parent;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindDeep(parent.GetChild(i), name);
                if (found != null) return found;
            }

            return null;
        }

        // ── Item registration and the player ───────────────────────────────────

        private static void BuildItemAsset(GameObject gunPrefab)
        {
            EnsureFolder(ItemFolder);
            string path = $"{ItemFolder}/PortalGun.asset";

            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(path);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<InventoryItem>();
                AssetDatabase.CreateAsset(item, path);
            }

            item.itemName = "Portal Gun";
            item.itemPrefab = gunPrefab;
            EditorUtility.SetDirty(item);

            // Nothing else to do: RegistryLoader picks every InventoryItem out of
            // Resources/Items at startup, so being in this folder IS being
            // registered.
        }

        /// <summary>
        /// Give both player prefabs a traveller, tracked from the middle of their
        /// capsule rather than from between their feet.
        ///
        /// The tracked point decides when a crossing counts. At the origin — which
        /// for these rigs is the soles — a wall portal only fires once the feet
        /// are through it, so the player's whole body pushes into the wall first.
        /// </summary>
        private static void AddTravellerToPlayers()
        {
            string[] prefabs =
            {
                "Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab",
                "Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab",
            };

            foreach (string path in prefabs)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) continue;

                try
                {
                    if (root.GetComponent<PortalTraveller>() == null)
                        root.AddComponent<PlayerPortalTraveller>();

                    var traveller = root.GetComponent<PlayerPortalTraveller>();
                    if (traveller == null) continue;

                    var serialized = new SerializedObject(traveller);
                    SerializedProperty tracked = serialized.FindProperty("trackedPoint");

                    if (tracked.objectReferenceValue == null)
                    {
                        Transform point = root.transform.Find("PortalTrackPoint");
                        if (point == null)
                        {
                            point = new GameObject("PortalTrackPoint").transform;
                            point.SetParent(root.transform, false);

                            var capsule = root.GetComponentInChildren<CapsuleCollider>();
                            point.localPosition = capsule != null
                                ? root.transform.InverseTransformPoint(
                                      capsule.transform.TransformPoint(capsule.center))
                                : new Vector3(0f, 1f, 0f);
                        }

                        tracked.objectReferenceValue = point;
                    }

                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        // ── Small helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Every root currently open through <see cref="PrefabUtility.LoadPrefabContents"/>.
        ///
        /// Tracked explicitly because there is no way to ask a GameObject which
        /// it is: prefab contents live in a hidden scene and answer false to
        /// both IsPartOfPrefabAsset and IsPersistent, so disposing of them by
        /// inspection means calling DestroyImmediate on a preview scene root and
        /// leaking the scene it belongs to.
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<int> LoadedContents =
            new System.Collections.Generic.HashSet<int>();

        private static GameObject LoadOrCreateRoot(string path, string name)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing == null) return new GameObject(name);

            // Edited through LoadPrefabContents rather than rebuilt, so anything
            // added to the prefab by hand since the last run survives — the
            // failure mode this project has already been bitten by with builder
            // scripts that overwrite prefabs wholesale.
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            LoadedContents.Add(contents.GetInstanceID());
            return contents;
        }

        private static GameObject SaveRoot(GameObject root, string path)
        {
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);

            if (LoadedContents.Remove(root.GetInstanceID()))
                PrefabUtility.UnloadPrefabContents(root);
            else
                Object.DestroyImmediate(root);

            return saved;
        }

        /// <summary>
        /// The component of type <typeparamref name="T"/> on <paramref name="target"/>, added if
        /// it is not there yet.
        ///
        /// Written out rather than using <c>GetComponent&lt;T&gt;() ?? AddComponent&lt;T&gt;()</c>,
        /// which does not work and does not look like it does not work: a
        /// missing component comes back as Unity's fake null, an object that
        /// compares equal to null through the overloaded == but is a real
        /// reference as far as ?? is concerned. So ?? keeps the destroyed stub
        /// and the AddComponent never runs, and the first property set on it
        /// throws MissingComponentException from somewhere else entirely.
        /// </summary>
        private static T Ensure<T>(GameObject target) where T : Component
        {
            T existing = target.GetComponent<T>();
            return existing != null ? existing : target.AddComponent<T>();
        }

        private static Transform EnsureChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) return child;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
