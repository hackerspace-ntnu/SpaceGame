// A scene that exists to prove the fog works.
//
// Eight volumes, no two alike, arranged so the questions you actually need answered are answerable
// by walking rather than by reasoning:
//
//   * Six of them sit in open bays around a ring, so each can be seen from outside at a distance,
//     from its own doorway, and from inside with your head in the middle of it. A volumetric that
//     is only ever checked from outside looks fine and falls apart the first time a player steps in.
//   * Two overlap in the middle of the plaza — one crimson, one azure — because blending is the one
//     behaviour that cannot be verified on a single volume. The overlap has to read as violet air,
//     not as two silhouettes taking turns.
//   * Pillars stand in front of several of them. Fog is computed at half resolution, so a hard
//     silhouette in front of it is exactly where a bad upsample shows itself as a halo.
//   * A cloud layer overhead, which is the same march at a completely different scale and the thing
//     that shows whether the horizon curves.
//
// Each bay has a lamp of its own inside it. That is not decoration: a light source inside a volume
// is what turns "coloured screen effect" into "air with something in it", and it is the part most
// likely to regress unnoticed.
using SpaceGame.Characters;
using SpaceGame.World;
using SpaceGame.World.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceGame.EditorTools.Environment
{
    public static class FogGallerySceneBuilder
    {
        private const string ScenePath = "Assets/Game/Scenes/Tests/FogGallery.unity";
        private const string SkyboxPath = "Assets/Game/Art/Materials/Environment/Skybox.mat";

        private const float RingRadius = 34f;
        private const int BayCount = 6;

        /// <summary>
        /// One bay's worth of authoring. Everything that differs between the six lives here, so
        /// adding a seventh look is a row in a table rather than another block of scene-building.
        /// </summary>
        private readonly struct Bay
        {
            public readonly string Name;
            public readonly FogShapeKind Shape;
            public readonly Vector3 Size;
            public readonly Color Color;
            public readonly Color Emission;
            public readonly Color Lamp;
            public readonly float Density;
            public readonly float Extinction;
            public readonly float NoiseScale;
            public readonly float Erosion;
            public readonly float Churn;
            public readonly float Tilt;

            public Bay(string name, FogShapeKind shape, Vector3 size, Color color, Color emission,
                       Color lamp, float density, float extinction, float noiseScale, float erosion,
                       float churn, float tilt)
            {
                Name = name;
                Shape = shape;
                Size = size;
                Color = color;
                Emission = emission;
                Lamp = lamp;
                Density = density;
                Extinction = extinction;
                NoiseScale = noiseScale;
                Erosion = erosion;
                Churn = churn;
                Tilt = tilt;
            }
        }

        private static readonly Bay[] Bays =
        {
            // Coolant venting into a cold room: pale, fast-moving, and faintly lit from within.
            new Bay("Coolant Vapour", FogShapeKind.Ellipsoid, new Vector3(9f, 5f, 9f),
                    new Color(0.55f, 0.85f, 0.92f), new Color(0.02f, 0.09f, 0.11f),
                    new Color(0.6f, 0.9f, 1f), 1.1f, 0.13f, 14f, 0.42f, 9f, 0f),

            // Marsh mist: wide, flat, and never reaching your knees. The ground layer's whole point
            // is that there is no ceiling on it to give the box away.
            new Bay("Marsh Mist", FogShapeKind.GroundLayer, new Vector3(11f, 3.5f, 11f),
                    new Color(0.70f, 0.86f, 0.66f), new Color(0.02f, 0.05f, 0.02f),
                    new Color(0.75f, 1f, 0.7f), 2.2f, 0.22f, 20f, 0.28f, 4f, 0f),

            // A vent column standing upright — the case that proves a cylinder is a real body and
            // not a billboard, because you can walk round it.
            new Bay("Ember Column", FogShapeKind.Cylinder, new Vector3(4.5f, 9f, 4.5f),
                    new Color(0.95f, 0.55f, 0.24f), new Color(0.16f, 0.05f, 0.01f),
                    new Color(1f, 0.6f, 0.25f), 1.3f, 0.17f, 10f, 0.5f, 12f, 0f),

            // A room full of still, heavy air. Low churn and low erosion, so it reads as something
            // that has been sitting there for a long time.
            new Bay("Chamber Haze", FogShapeKind.Box, new Vector3(9f, 4.5f, 9f),
                    new Color(0.35f, 0.42f, 0.68f), Color.black,
                    new Color(0.5f, 0.6f, 1f), 1.6f, 0.2f, 26f, 0.18f, 2.5f, 0f),

            // Spores: the one that glows on its own, to show emission is a separate channel from
            // albedo. Turn every light off and this is still visible.
            new Bay("Spore Bloom", FogShapeKind.Ellipsoid, new Vector3(8f, 6f, 8f),
                    new Color(0.85f, 0.35f, 0.80f), new Color(0.30f, 0.05f, 0.28f),
                    new Color(1f, 0.4f, 0.9f), 0.9f, 0.11f, 9f, 0.55f, 14f, 0f),

            // The same cylinder tipped over, blowing sideways out of the wall. Rotation is entirely
            // the transform's business, so this needs no shape of its own.
            new Bay("Sulphur Vent", FogShapeKind.Cylinder, new Vector3(3.5f, 10f, 3.5f),
                    new Color(0.90f, 0.86f, 0.32f), new Color(0.10f, 0.09f, 0.01f),
                    new Color(1f, 0.95f, 0.4f), 1.2f, 0.15f, 11f, 0.48f, 10f, 62f),
        };

        [MenuItem("SpaceGame/Environment/Build Fog Gallery Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[Fog] Leave play mode before building the gallery — " +
                               "EditorSceneManager cannot create scenes while it is running.");
                return;
            }

            // Additive, then closed again. A Single-mode new scene would throw away whatever the
            // person running this had open, unsaved changes and all — a builder that costs you your
            // work the first time you try it is not a tool anyone runs twice.
            //
            // The exception is an editor sitting on a single untitled scene, which Unity refuses to
            // create an additive scene alongside. That state has nothing in it worth protecting, so
            // it is replaced — after checking it is not dirty, which is the only way it could.
            Scene previousActive = SceneManager.GetActiveScene();

            bool untitled = SceneManager.sceneCount == 1 && string.IsNullOrEmpty(previousActive.path);
            if (untitled && previousActive.isDirty)
            {
                Debug.LogError("[Fog] The open untitled scene has unsaved changes. Save or discard " +
                               "it, then run this again.");
                return;
            }

            NewSceneMode mode = untitled ? NewSceneMode.Single : NewSceneMode.Additive;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);

            // New GameObjects land in the ACTIVE scene, and so do the ambient settings below.
            SceneManager.SetActiveScene(scene);

            Sky();
            Ground();

            for (int i = 0; i < BayCount; i++)
                BuildBay(Bays[i], i);

            BuildOverlapPair();
            BuildPillars();
            BuildCamera();

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);

            // Only unwind when there was something to unwind to. In Single mode the previous scene
            // is already gone and the new one IS the editor's open scene — closing it would leave
            // the editor with nothing.
            if (mode == NewSceneMode.Additive)
            {
                if (previousActive.IsValid())
                    SceneManager.SetActiveScene(previousActive);
                EditorSceneManager.CloseScene(scene, true);
            }

            // The scene is inert without the render features, and "I built the demo and it looks
            // like an empty room" is a worse first experience than one extra idempotent step.
            VolumetricSetup.Install();

            AssetDatabase.Refresh();
            Debug.Log("[Fog] Gallery written to " + ScenePath +
                      ". Open it and press Play — the camera is a free-fly spectator.");
        }

        private static void Sky()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 2.2f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.shadows = LightShadows.Soft;

            // Low enough that the fog is lit across its side rather than from straight above. A
            // volumetric at noon has almost no visible shading; the whole model shows at a raking
            // angle, which is also when a real bank of fog looks like anything. Not so low that the
            // bays are in their own shadow — the gallery has to read as a place, not as coloured
            // shapes floating in a void.
            sun.transform.rotation = Quaternion.Euler(34f, 150f, 0f);

            // Opposite the sun and much weaker: enough to keep the walls from reading as holes,
            // not enough to wash out the raking light the volumes are shaded by.
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.7f;
            fill.color = new Color(0.62f, 0.70f, 0.85f);
            fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(38f, -46f, 0f);

            var clouds = new GameObject("Cloud Layer").AddComponent<CloudLayer>();
            clouds.coverage = 0.62f;
            clouds.baseAltitude = 900f;
            clouds.topAltitude = 2400f;
            clouds.density = 1.6f;
            clouds.billowScale = 700f;
            clouds.weatherScale = 11000f;

            var skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
            if (skybox != null)
                RenderSettings.skybox = skybox;

            // Bright enough to see the room the fog is standing in.
            //
            // This is not decoration: `ambient` on every volume is multiplied by the sky's radiance,
            // so a scene with a dark ambient probe has fog that can only be lit by the sun and by
            // its own lamps. The gallery exists to show the fog, and fog in an unlit room shows
            // nothing.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.52f, 0.58f, 0.70f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.43f, 0.46f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.21f, 0.20f);

            // Setting the colours does not fill the ambient PROBE, and the probe is what both the
            // surfaces and FogVolumes.PushSkyLight read. Without this the scene saves with a black
            // probe and opens as a black room — which looks exactly like the fog being broken.
            DynamicGI.UpdateEnvironment();
        }

        private static void Ground()
        {
            Box("Plaza", new Vector3(0f, -0.5f, 0f), new Vector3(120f, 1f, 120f),
                new Color(0.52f, 0.50f, 0.47f));
        }

        private static void BuildBay(Bay bay, int index)
        {
            float angle = index / (float)BayCount * Mathf.PI * 2f;
            var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            Vector3 centre = outward * RingRadius;
            Quaternion facing = Quaternion.LookRotation(-outward, Vector3.up);

            var root = new GameObject(bay.Name);
            root.transform.SetPositionAndRotation(centre, facing);

            // Three walls and a floor: open toward the plaza, so every volume can be seen from
            // outside and then walked into without a door to fiddle with.
            var shell = new Color(0.60f, 0.58f, 0.55f);
            Wall(root.transform, "Back", new Vector3(0f, 5f, -8f), new Vector3(20f, 10f, 0.6f), shell);
            Wall(root.transform, "Left", new Vector3(-9.7f, 5f, 0f), new Vector3(0.6f, 10f, 16f), shell);
            Wall(root.transform, "Right", new Vector3(9.7f, 5f, 0f), new Vector3(0.6f, 10f, 16f), shell);
            Wall(root.transform, "Floor", new Vector3(0f, 0.05f, 0f), new Vector3(20f, 0.3f, 16f),
                 new Color(0.44f, 0.43f, 0.42f));

            var volumeObject = new GameObject("Fog");
            volumeObject.transform.SetParent(root.transform, false);
            volumeObject.transform.localPosition = new Vector3(0f, bay.Size.y * 0.75f, 0f);
            volumeObject.transform.localRotation = Quaternion.Euler(bay.Tilt, 0f, 0f);

            FogVolume volume = volumeObject.AddComponent<FogVolume>();
            volume.shape = bay.Shape;
            volume.size = bay.Size;
            volume.color = bay.Color;
            volume.emission = bay.Emission;
            volume.density = bay.Density;
            volume.extinction = bay.Extinction;
            volume.noiseScale = bay.NoiseScale;
            volume.erosion = bay.Erosion;
            volume.churn = bay.Churn;
            volume.churnScale = bay.NoiseScale * 1.6f;
            volume.windSpeed = 0.8f;
            volume.wind = new Vector3(Mathf.Cos(angle), -0.08f, Mathf.Sin(angle));

            // A ground layer sits ON the floor; every other shape is centred on its own body.
            if (bay.Shape == FogShapeKind.GroundLayer)
                volumeObject.transform.localPosition = new Vector3(0f, bay.Size.y, 0f);

            Lamp(root.transform, bay.Name + " Lamp", new Vector3(0f, 3.4f, -4f), bay.Lamp, 22f, 6f);
        }

        /// <summary>
        /// The blending demo: two volumes of different colours, overlapping by roughly half.
        ///
        /// <para>
        /// Deliberately in the open middle of the plaza with nothing else near it, because the thing
        /// being checked is subtle — the overlap should be a third colour that belongs to both, and
        /// it is easy to mistake a bad result for a good one when there is scenery to look at.
        /// </para>
        /// </summary>
        private static void BuildOverlapPair()
        {
            var root = new GameObject("Overlap Test");

            Overlap(root.transform, "Crimson", new Vector3(-4.5f, 5f, 0f),
                    new Color(0.92f, 0.24f, 0.22f), new Color(0.14f, 0.01f, 0.01f));

            Overlap(root.transform, "Azure", new Vector3(4.5f, 5f, 0f),
                    new Color(0.20f, 0.45f, 0.95f), new Color(0.01f, 0.04f, 0.16f));

            Lamp(root.transform, "Overlap Lamp", new Vector3(0f, 9f, 0f), Color.white, 30f, 5f);
        }

        private static void Overlap(Transform parent, string name, Vector3 position, Color color, Color emission)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);
            host.transform.localPosition = position;

            FogVolume volume = host.AddComponent<FogVolume>();
            volume.shape = FogShapeKind.Ellipsoid;
            volume.size = new Vector3(7f, 5f, 7f);
            volume.color = color;
            volume.emission = emission;
            volume.density = 0.9f;
            volume.extinction = 0.1f;
            volume.noiseScale = 13f;
            volume.erosion = 0.35f;
            volume.churn = 7f;
            volume.churnScale = 22f;
            volume.windSpeed = 0.6f;
        }

        /// <summary>
        /// Hard silhouettes standing in the fog. The march runs at half resolution, so an edge like
        /// this in front of a volume is where a bilinear upsample would show a bright halo — the one
        /// artefact of a reduced-resolution volumetric that a player notices without being told.
        /// </summary>
        private static void BuildPillars()
        {
            var root = new GameObject("Pillars");
            var stone = new Color(0.56f, 0.54f, 0.51f);

            for (int i = 0; i < 8; i++)
            {
                float angle = (i + 0.5f) / 8f * Mathf.PI * 2f;
                var position = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * 16f;

                GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pillar.name = "Pillar " + i;
                pillar.transform.SetParent(root.transform, false);
                pillar.transform.position = position + new Vector3(0f, 4f, 0f);
                pillar.transform.localScale = new Vector3(1.1f, 4f, 1.1f);
                Paint(pillar, stone);
            }
        }

        private static void BuildCamera()
        {
            // Free-fly rather than fixed, because every claim this scene exists to check is a claim
            // about a viewing angle: from outside, from the doorway, from inside, from above, and
            // from the moment of crossing the edge. SpectatorCamera already does exactly this, so it
            // is reused rather than reimplemented.
            var camera = new GameObject("Gallery Camera").AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(new Vector3(0f, 6f, -46f),
                                                    Quaternion.Euler(6f, 0f, 0f));
            camera.tag = "MainCamera";
            camera.farClipPlane = 3000f;
            camera.gameObject.AddComponent<AudioListener>();
            camera.gameObject.AddComponent<SpectatorCamera>();
        }

        private static void Lamp(Transform parent, string name, Vector3 localPosition, Color color,
                                 float range, float intensity)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);
            host.transform.localPosition = localPosition;

            Light light = host.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;

            // Without this the lamp lights the walls and leaves the air it is standing in flat.
            host.AddComponent<FogLight>();
        }

        private static void Wall(Transform parent, string name, Vector3 localPosition, Vector3 size,
                                 Color colour)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = size;
            Paint(box, colour);
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
            material.SetFloat("_Smoothness", 0.12f);
            target.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
