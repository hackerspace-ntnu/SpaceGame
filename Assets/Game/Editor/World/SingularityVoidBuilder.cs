// Builds the singularity's void: the completely white nowhere a swallowed body is put.
//
// WHY A SCENE AND NOT A ROOM IN THE WORLD. It is an interior, and this project already has an
// interior system that refcounts the scene, remembers where every occupant came from, keeps the
// exterior chunks under them pinned while they are away, and puts them back. Building a white box
// somewhere in the desert instead would mean re-answering all four of those badly.
//
// WHY EVERYTHING IS UNLIT WHITE. The brief is that there is no difference between the floor and the
// ceiling, and the only way to get that reliably is to take lighting out of the question entirely:
// a lit white surface is shaded by whatever is in the sky and comes out grey on one side. Every
// surface here is URP/Unlit at pure white, so the floor, the dome and the seam where they meet are
// the same colour to the last bit and no edge is visible anywhere. There is deliberately no
// directional light, no fog and no post volume — nothing that could tint one part of it.
//
// THE DOME IS A PLAIN SPHERE WITH CULLING OFF. Inverting the normals of a mesh to see it from the
// inside is the usual trick and it is unnecessary here: an unlit shader does not read normals, so
// switching the cull off is the whole of it.
//
// RE-RUNNABLE. It replaces the scene wholesale, so anything hand-added to it is lost — the same
// contract JetpackBuilder has with its prefab. Tune the constants here.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceGame.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SpaceGame.EditorTools
{
    /// <summary>Generates the white void scene, its material, and the InteriorScene that names it.</summary>
    public static class SingularityVoidBuilder
    {
        private const string ScenePath = "Assets/Game/Scenes/Interiors/SingularityVoid.unity";
        private const string InteriorPath = "Assets/Game/Resources/Interiors/Interior_SingularityVoid.asset";
        private const string MaterialPath = "Assets/Game/Art/Materials/World/VoidWhite.mat";
        private const string UnlitShader = "Universal Render Pipeline/Unlit";

        private const string WellPrefabPath =
            "Assets/Game/Prefabs/Items/Artifacts/Gadgets/SingularityWell.prefab";

        /// <summary>
        /// Half-width of the floor slab, metres. Large enough that a player walking for the few
        /// seconds a hold lasts cannot reach the edge — and the edge is invisible anyway.
        /// </summary>
        private const float FloorHalfWidth = 300f;

        /// <summary>Thickness of the slab. Only its top face is ever stood on.</summary>
        private const float FloorThickness = 2f;

        /// <summary>
        /// Radius of the white dome. Comfortably inside the floor's half-width, so the seam where
        /// the two meet is behind the dome wall rather than on the skyline.
        /// </summary>
        private const float DomeRadius = 200f;

        [MenuItem("Tools/SpaceGame/World/Build Singularity Void")]
        public static void Build()
        {
            Material white = EnsureWhiteMaterial();
            if (white == null) return;

            // SceneManager.CreateScene, not EditorSceneManager.NewScene. NewScene refuses outright
            // while an UNTITLED unsaved scene is open in the editor ("Cannot create a new scene
            // additively with an untitled scene unsaved") — which is the ordinary state of anybody's
            // editor after a play session. This route builds the scene beside whatever the user has
            // open and never touches it.
            Scene scene = SceneManager.CreateScene(SingularityVoid.SceneName);

            BuildContents(scene, white);
            ApplyRenderSettings(scene);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, removeScene: true);

            RegisterInBuildSettings();
            InteriorScene interior = EnsureInteriorAsset();
            WireWell(interior);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[SingularityVoid] Built {ScenePath}, registered it in Build Settings, and " +
                      $"wired {InteriorPath}.");
        }

        /// <summary>
        /// The floor, the dome and the anchor. Nothing else — every object added here is one more
        /// thing that could have an edge in it.
        /// </summary>
        private static void BuildContents(Scene scene, Material white)
        {
            var root = new GameObject("Void");
            SceneManager.MoveGameObjectToScene(root, scene);

            // A slab rather than a plane: a plane is single-sided and one-metre-per-unit-scaled,
            // and a body that clipped through it would fall out of the world forever. Its top face
            // sits exactly on y = 0.
            GameObject floor = Surface(PrimitiveType.Cube, "Floor", root.transform, white);
            floor.transform.localPosition = new Vector3(0f, -FloorThickness * 0.5f, 0f);
            floor.transform.localScale =
                new Vector3(FloorHalfWidth * 2f, FloorThickness, FloorHalfWidth * 2f);

            // The sky, the walls and the ceiling in one object, because from the inside they are
            // the same white and there is no reason for them to be three.
            GameObject dome = Surface(PrimitiveType.Sphere, "Dome", root.transform, white);
            dome.transform.localScale = Vector3.one * (DomeRadius * 2f);
            Object.DestroyImmediate(dome.GetComponent<Collider>());

            // Not on the floor: a body placed exactly on a surface can start the frame intersecting
            // it. A metre up costs nothing and lands them on their feet.
            var anchor = new GameObject("Anchor");
            anchor.transform.SetParent(root.transform, worldPositionStays: false);
            anchor.transform.localPosition = new Vector3(0f, 1f, 0f);
            anchor.AddComponent<InteriorAnchor>().SetAnchorId(SingularityVoid.AnchorId);
        }

        private static GameObject Surface(PrimitiveType type, string name, Transform parent,
                                          Material white)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, worldPositionStays: false);

            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = white;

            // Shadows are the one thing that could put a mark on a flat white surface, and the
            // occupant is standing on it.
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return part;
        }

        /// <summary>
        /// Flat white lighting, and nothing that could tint it.
        ///
        /// <para>
        /// Belt and braces rather than the mechanism: every surface in here is unlit, so none of
        /// this reaches them. It matters for what an occupant BRINGS — a player, a creature, a
        /// crate — which is lit normally and would otherwise stand in a black room lit by nothing.
        /// Flat white ambient from every direction is the closest thing to "no shading" a lit
        /// shader can be given.
        /// </para>
        /// </summary>
        private static void ApplyRenderSettings(Scene scene)
        {
            Scene previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
            RenderSettings.subtractiveShadowColor = Color.white;

            SceneManager.SetActiveScene(previous);
        }

        /// <summary>
        /// Add the scene to Build Settings, enabled.
        ///
        /// Without it `LoadSceneAsync` fails at runtime, and in a session it fails on the CLIENTS
        /// too — Netcode resolves a scene by a hash of its path, so a scene missing from one
        /// machine's build is a join that dies rather than an interior that is empty.
        /// </summary>
        private static void RegisterInBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();

            EditorBuildSettingsScene existing = scenes.FirstOrDefault(s => s.path == ScenePath);
            if (existing != null)
            {
                existing.enabled = true;
                EditorBuildSettings.scenes = scenes.ToArray();
                return;
            }

            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>The InteriorScene the well hands to InteriorManager.</summary>
        private static InteriorScene EnsureInteriorAsset()
        {
            var interior = AssetDatabase.LoadAssetAtPath<InteriorScene>(InteriorPath);

            if (interior == null)
            {
                interior = ScriptableObject.CreateInstance<InteriorScene>();
                Directory.CreateDirectory(Path.GetDirectoryName(InteriorPath));
                AssetDatabase.CreateAsset(interior, InteriorPath);
            }

            var serialized = new SerializedObject(interior);
            serialized.FindProperty("sceneName").stringValue = SingularityVoid.SceneName;
            serialized.FindProperty("spawnAnchorId").stringValue = SingularityVoid.AnchorId;
            serialized.FindProperty("sceneAsset").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(interior);
            return interior;
        }

        /// <summary>
        /// Point the thrown bottle at the room it sends bodies to.
        ///
        /// <para>
        /// A direct reference rather than a Resources load at the moment of the swallow, so the
        /// wiring is visible in the Inspector and a missing one is caught by a test rather than by
        /// a player watching a singularity eat nothing. Edited in place — this builder owns the
        /// scene and the asset, not the well.
        /// </para>
        /// </summary>
        private static void WireWell(InteriorScene interior)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(WellPrefabPath);
            if (root == null)
            {
                Debug.LogError($"[SingularityVoid] No prefab at {WellPrefabPath}, so nothing was " +
                               "wired to the void.");
                return;
            }

            try
            {
                var well = root.GetComponent<SpaceGame.Items.SingularityWell>();
                if (well == null)
                {
                    Debug.LogError("[SingularityVoid] The well prefab has no SingularityWell.");
                    return;
                }

                var serialized = new SerializedObject(well);
                serialized.FindProperty("voidInterior").objectReferenceValue = interior;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, WellPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Pure white, unlit, and two-sided so the dome is solid seen from the inside.
        ///
        /// Every field is written rather than left to the shader, because a `.mat` freezes the
        /// defaults it was born with — a material created from a URP/Unlit whose cull was never
        /// touched is a dome that is not there.
        /// </summary>
        private static Material EnsureWhiteMaterial()
        {
            Shader shader = Shader.Find(UnlitShader);
            if (shader == null)
            {
                Debug.LogError($"[SingularityVoid] Shader '{UnlitShader}' not found. Is the " +
                               "project still on URP?");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            material.SetFloat("_Surface", 0f);
            material.SetFloat("_ZWrite", 1f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetColor("_BaseColor", Color.white);
            material.renderQueue = (int)RenderQueue.Geometry;

            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");

            EditorUtility.SetDirty(material);
            return material;
        }
    }
}

