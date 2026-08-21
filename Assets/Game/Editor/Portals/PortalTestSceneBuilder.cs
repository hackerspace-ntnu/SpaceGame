// A scene that exists to prove the portals work.
//
// Two apertures on two walls, each with a visibly different room behind it, so
// "is the view right" is answerable by looking rather than by reasoning about a
// matrix. That matters more here than for most features: a portal that renders
// the wrong thing still renders SOMETHING plausible, and a subtly wrong transfer
// matrix looks fine standing still and only falls apart as you move.
//
// It also carries loose crates with PortalTraveller on them, because the
// traversal half has its own failure modes — a body that comes out stopped, or
// one that is shoved back through by the wall it just passed — and neither shows
// up unless something actually goes through.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Portals;

namespace SpaceGame.EditorTools.Portals
{
    public static class PortalTestSceneBuilder
    {
        private const string ScenePath = "Assets/Game/Scenes/Tests/PortalTest.unity";
        private const string PrefabFolder = "Assets/Game/Prefabs/Items/Artifacts/Portals";

        [MenuItem("SpaceGame/Portals/Build Portal Test Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[Portals] Leave play mode before building the test scene — " +
                               "EditorSceneManager cannot create scenes while it is running.");
                return;
            }

            // Additive, then closed again. A Single-mode new scene would throw
            // away whatever the person running this had open, unsaved changes
            // and all — a builder that costs you your work the first time you
            // try it is not a tool anyone runs twice.
            //
            // The exception is an editor sitting on a single untitled scene,
            // which Unity refuses to create an additive scene alongside. That
            // state has nothing in it worth protecting, so it is replaced —
            // after checking it is not dirty, which is the only way it could.
            Scene previousActive = SceneManager.GetActiveScene();

            bool untitled = SceneManager.sceneCount == 1 &&
                            string.IsNullOrEmpty(previousActive.path);

            if (untitled && previousActive.isDirty)
            {
                Debug.LogError("[Portals] The open untitled scene has unsaved changes. " +
                               "Save or discard it, then run this again.");
                return;
            }

            NewSceneMode mode = untitled ? NewSceneMode.Single : NewSceneMode.Additive;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);

            // New GameObjects land in the ACTIVE scene, and so do the ambient
            // lighting settings below, so the new scene has to be active while
            // it is being filled.
            SceneManager.SetActiveScene(scene);

            Light();
            Ground();

            // Room A — where the viewer stands. Warm crates, a wall across +Z.
            GameObject wallA = Box("Wall_A", new Vector3(0f, 3.5f, 7f), new Vector3(16f, 7f, 0.5f),
                                   new Color(0.55f, 0.52f, 0.48f));
            Box("Crate_A1", new Vector3(-3f, 0.5f, 3.5f), Vector3.one, new Color(0.75f, 0.35f, 0.18f));
            Box("Crate_A2", new Vector3(-3f, 1.5f, 3.5f), Vector3.one * 0.9f, new Color(0.72f, 0.30f, 0.14f));
            Box("Crate_A3", new Vector3(3.4f, 0.5f, 4.2f), Vector3.one, new Color(0.68f, 0.40f, 0.20f));

            // Room B — off to one side, deliberately nothing like room A. Cool
            // pillars of different heights, so what shows through the aperture
            // could not be mistaken for the room the viewer is standing in.
            GameObject wallB = Box("Wall_B", new Vector3(-14f, 3.5f, -6f), new Vector3(0.5f, 7f, 16f),
                                   new Color(0.42f, 0.46f, 0.55f));
            for (int i = 0; i < 5; i++)
            {
                Box($"Pillar_B{i}", new Vector3(-11.5f + i * 0.1f, 1.2f + i * 0.45f, -10f + i * 2.2f),
                    new Vector3(0.7f, 2.4f + i * 0.9f, 0.7f),
                    Color.Lerp(new Color(0.20f, 0.45f, 0.62f), new Color(0.35f, 0.72f, 0.78f), i / 4f));
            }
            Box("Floor_B", new Vector3(-11f, 0.02f, -6f), new Vector3(6f, 0.04f, 14f),
                new Color(0.22f, 0.28f, 0.34f));

            // The two apertures, each set into its own wall and facing the room.
            // Centre at 1.85 m: the aperture is 3.4 m tall now, so anything
            // lower puts its bottom edge through the floor.
            Portal primary = Place("PortalPrimary", new Vector3(0f, 1.85f, 7f - 0.26f),
                                  Quaternion.LookRotation(Vector3.back, Vector3.up),
                                  wallA.GetComponent<Collider>(), PortalPair.Primary);

            Portal secondary = Place("PortalSecondary", new Vector3(-14f + 0.26f, 1.85f, -6f),
                                Quaternion.LookRotation(Vector3.right, Vector3.up),
                                wallB.GetComponent<Collider>(), PortalPair.Secondary);

            Portal.Link(primary, secondary);

            // Surfaces that a strict fit would have refused: a sphere, a
            // tilted slab and a small crate. All three are portalable now, and
            // they are here so that stays true.
            GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rock.name = "Rock_Curved";
            rock.transform.position = new Vector3(6.5f, 1.6f, 3.5f);
            rock.transform.localScale = Vector3.one * 3.2f;
            Paint(rock, new Color(0.40f, 0.37f, 0.33f));

            GameObject slab = Box("Slab_Tilted", new Vector3(-6.5f, 1.8f, 3.0f),
                                  new Vector3(3.5f, 3.5f, 0.4f), new Color(0.46f, 0.44f, 0.40f));
            slab.transform.rotation = Quaternion.Euler(18f, 24f, 7f);

            Box("Crate_Small", new Vector3(2.0f, 0.6f, 1.5f), Vector3.one * 1.2f,
                new Color(0.60f, 0.45f, 0.25f));

            // Loose bodies, so the traversal half is testable by shoving one in.
            for (int i = 0; i < 3; i++)
            {
                GameObject crate = Box($"Traveller_{i}", new Vector3(-1f + i, 0.4f, 4.5f),
                                       Vector3.one * 0.8f, new Color(0.85f, 0.78f, 0.35f));
                Rigidbody body = crate.AddComponent<Rigidbody>();
                body.mass = 8f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                // Only the first keeps an authored traveller: the other two
                // prove the auto-add path in Portal.OnTriggerEnter works.
                if (i == 0) crate.AddComponent<PortalTraveller>();
            }

            // Placed where the aperture fills a good part of the frame, since
            // this camera is what the verification screenshots look through.
            var camera = new GameObject("Test Camera").AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(new Vector3(0f, 1.7f, 0.2f),
                                                    Quaternion.Euler(2f, 0f, 0f));
            camera.tag = "MainCamera";

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);

            // Only unwind when there was something to unwind to. In Single mode
            // the previous scene is already gone and the new one IS the editor's
            // open scene — closing it would leave the editor with nothing.
            if (mode == NewSceneMode.Additive)
            {
                if (previousActive.IsValid()) SceneManager.SetActiveScene(previousActive);
                EditorSceneManager.CloseScene(scene, true);
            }

            AssetDatabase.Refresh();

            Debug.Log("[Portals] Test scene written to " + ScenePath);
        }

        private static Portal Place(string prefabName, Vector3 position, Quaternion rotation,
                                    Collider host, int index)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{prefabName}.prefab");
            if (prefab == null)
            {
                Debug.LogError($"[Portals] {prefabName}.prefab is missing — run " +
                               "SpaceGame/Portals/Build Portal Gun Content first.");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Portal portal = instance.GetComponent<Portal>();

            // Through Place rather than by setting the transform, so the scene is
            // built by the same call the gun makes at runtime. A test scene posed
            // some other way would not be testing what actually ships.
            portal.Place(position, rotation, host, index);
            return portal;
        }

        private static void Light()
        {
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(48f, 35f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.38f, 0.42f, 0.50f);
            RenderSettings.ambientEquatorColor = new Color(0.28f, 0.29f, 0.31f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.15f, 0.14f);
        }

        private static void Ground()
        {
            Box("Ground", new Vector3(-5f, -0.25f, 0f), new Vector3(40f, 0.5f, 40f),
                new Color(0.34f, 0.33f, 0.31f));
        }

        private static GameObject Box(string name, Vector3 position, Vector3 size, Color colour)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.position = position;
            box.transform.localScale = size;
            Paint(box, colour);
            return box;
        }

        private static void Paint(GameObject target, Color colour)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", 0.15f);
            target.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
