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
using System.Linq;
using Unity.Netcode;
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

        // The list NetworkManager actually reads. NOT Assets/DefaultNetworkPrefabs.asset, which
        // Netcode regenerates on its own and which nothing consults.
        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

        /// <summary>The ground layer DropItemPhysics settles a dropped item against.</summary>
        private const int GroundLayerMask = 128;

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

        /// <summary>
        /// Seconds a ground splat lasts. The APERTURE outlives it by forty times over, on purpose:
        /// the paint that became a portal is the thing the player made, and the paint that merely
        /// spattered around it is exhaust.
        /// </summary>
        private const float SplatLife = 0.5f;

        /// <summary>The hose's numbers, mirroring PortalGunItem's serialized defaults.
        ///
        /// These MUST match, and not approximately. The droplets are an ordinary ParticleSystem
        /// under Unity's own gravity, and PortalJet integrates the same parabola in C# to decide
        /// where the paint lands. Same start speed, same gravity modifier, same lifetime means the
        /// stream you watch and the stream that paints are the one curve. Drift them apart and the
        /// paint lands somewhere the player never saw the water go.</summary>
        private const float JetSpeed = 13f;
        private const float JetGravity = 1f;
        private const float JetFlightTime = 1.6f;

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
            Material paint          = BuildGooMaterial("PortalGoo");
            Material splatMaterial  = BuildSplatMaterial("PortalSplat", Gold);

            RemapGunMaterials(fluidPrimary, fluidSecondary);

            // One prefab per colour: the aperture's tint is baked into its
            // materials rather than set per instance, because a screen-space
            // material with a property block is the one case where Unity's
            // batching quietly drops the override.
            GameObject portalPrimary   = BuildPortalPrefab("PortalPrimary", surfacePrimary, rimPrimary);
            GameObject portalSecondary = BuildPortalPrefab("PortalSecondary", surfaceSecondary, rimSecondary);

            GameObject splat = BuildSplatPrefab(splatMaterial);
            GameObject gun = BuildGunPrefab(portalPrimary, paint, splat, fluidPrimary, fluidSecondary);

            InventoryItem item = BuildItemAsset(gun);
            WireItemIntoPickup(gun, item);
            RegisterNetworkPrefab(gun);
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
            material.SetFloat("_EdgeGlow", 3.2f);
            material.SetFloat("_EdgeWidth", 0.22f);
            material.SetFloat("_Throat", 0.45f);
            material.SetFloat("_Swirl", 2.1f);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// The gunk in flight.
        ///
        /// Deliberately UNTINTED: the droplets take their colour from the particle system's start
        /// colour, so one material serves both barrels and the gun switches hue by setting the
        /// system rather than by instancing a second material per barrel.
        /// </summary>
        private static Material BuildGooMaterial(string name)
        {
            Material material = EnsureMaterial(name, "SpaceGame/Portal/PortalGoo");
            material.SetFloat("_Glossiness", 42f);
            material.SetFloat("_SpecStrength", 1.6f);
            material.SetFloat("_Fresnel", 1.5f);
            material.SetFloat("_ShadeDepth", 0.45f);
            material.SetFloat("_Cutoff", 0.5f);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>The coat of paint a landing leaves. Per-splat values are set at runtime.</summary>
        private static Material BuildSplatMaterial(string name, Color colour)
        {
            Material material = EnsureMaterial(name, "SpaceGame/Portal/PortalSplat");
            material.SetColor("_Colour", colour);
            material.SetColor("_HotColour", Hot);
            material.SetFloat("_Lobes", 7f);
            material.SetFloat("_Spread", 0.52f);
            material.SetFloat("_LobeSize", 0.24f);
            material.SetFloat("_CoreSize", 0.34f);
            material.SetFloat("_Smooth", 0.13f);
            // Half a second, start to gone. The splash spreads in the first 90 ms and spends the
            // rest of its life drying off. See the header of PortalSplat.cs.
            material.SetFloat("_Spread01", 0.09f);
            material.SetFloat("_Life", SplatLife);
            material.SetFloat("_Fade", 0.28f);
            material.SetFloat("_Sheen", 1.1f);
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

        // ── The jet ────────────────────────────────────────────────────────────

        /// <summary>
        /// The stream of paint that comes out of the nozzle while the trigger is down.
        ///
        /// Built here rather than authored by hand for the reason the rest of this file exists:
        /// a particle system is thirty settings, of which four matter, and a script says which
        /// four out loud. The lifetime is deliberately RANGE divided by SPEED, so the droplets
        /// die about where the paint lands however either is retuned — a jet whose particles
        /// outlive their own reach is a jet that sprays through walls.
        ///
        /// Play On Awake is off on purpose: PortalGunItem.SetJet starts and stops it, and a
        /// system that begins emitting the moment the gun is equipped paints the floor.
        /// </summary>
        private static ParticleSystem BuildJet(Transform muzzle, Material paintMaterial)
        {
            Transform child = EnsureChild(muzzle, "Jet");
            var jet = Ensure<ParticleSystem>(child.gameObject);

            ParticleSystem.MainModule main = jet.main;
            main.loop = true;
            main.playOnAwake = false;

            // The three numbers that have to agree with PortalJet, exactly. See the constants.
            main.startSpeed = JetSpeed;
            main.gravityModifier = JetGravity;
            main.startLifetime = JetFlightTime;

            // Big droplets, and a wide spread of sizes. Uniform particles read as a spray can; a
            // hose throws fat gobs with fine spatter between them, and the size variation is most
            // of what says "thick" before a single pixel is shaded.
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.30f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1400;

            // VAST amounts. This is a hose, and a hose that emits ninety particles a second looks
            // like a leak. The cost is affordable precisely because PortalGoo is a cheap opaque
            // fragment rather than a blended one — overdraw is resolved by the depth buffer.
            ParticleSystem.EmissionModule emission = jet.emission;
            emission.rateOverTime = 420f;

            ParticleSystem.ShapeModule shape = jet.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6.5f;
            shape.radius = 0.05f;

            // A little randomness on each droplet's speed is what breaks the stream into gobs
            // instead of a smooth tube of particles.
            ParticleSystem.VelocityOverLifetimeModule velocity = jet.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;

            // ALL THREE AXES, and all three in the same MinMaxCurve mode. Unity requires it and
            // enforces it by throwing "Particle Velocity curves must all be in the same mode" the
            // moment the system plays — not when the curves are assigned, which is why setting x
            // and y and leaving z on its default Constant built a perfectly clean prefab that took
            // the game down on the first trigger pull.
            //
            // Small, and symmetric about zero on purpose: this is what breaks the stream into
            // separate gobs instead of a smooth tube of particles, and a mean of zero is what keeps
            // the cloud centred on the parabola PortalJet actually paints along.
            velocity.x = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
            velocity.y = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

            // Droplets swell slightly as they fly, the way a thrown blob of liquid does as it comes
            // apart, then hold. A curve rather than a constant so the growth eases off.
            ParticleSystem.SizeOverLifetimeModule size = jet.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f, AnimationCurve.EaseInOut(0f, 0.75f, 1f, 1.15f));

            // Collision OFF, deliberately. Where the paint lands was settled by the shooter and
            // travelled in the message; a particle that decided anything would put two machines a
            // frame apart at two different answers. These are decoration that happens to follow
            // the same parabola.
            ParticleSystem.CollisionModule collision = jet.collision;
            collision.enabled = false;

            var renderer = jet.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = paintMaterial;

            // Billboards, not stretched: PortalGoo reconstructs a sphere normal from the quad's UV,
            // and stretching the quad would shear that normal into an ellipsoid lit from the wrong
            // direction.
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;

            // The droplets are opaque and cast real shadows, which is the last thing that stops a
            // stream of paint reading as a stream of light.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;

            return jet;
        }

        /// <summary>
        /// One quad, carrying PortalSplat and its shader. Everything about how a splash looks is in
        /// the shader; this is only the surface it is drawn on.
        /// </summary>
        private static GameObject BuildSplatPrefab(Material splatMaterial)
        {
            string path = $"{PrefabFolder}/PortalSplat.prefab";
            GameObject root = LoadOrCreateRoot(path, "PortalSplat");

            var splat = Ensure<PortalSplat>(root);

            Transform quad = root.transform.Find("Quad");
            if (quad == null)
            {
                GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
                plane.name = "Quad";
                Object.DestroyImmediate(plane.GetComponent<Collider>());
                plane.transform.SetParent(root.transform, false);
                quad = plane.transform;
            }

            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = splatMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var serialized = new SerializedObject(splat);
            serialized.FindProperty("quad").objectReferenceValue = renderer;

            // Written from the same constant the material's _Life gets. The component destroys the
            // GameObject on this timer and the shader fades on that one; if they disagree the splat
            // either vanishes mid-fade or sits invisible for the difference.
            serialized.FindProperty("life").floatValue = SplatLife;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return SaveRoot(root, path);
        }

        /// <summary>The light at the nozzle, lit only while paint is coming out of it.</summary>
        private static Light BuildMuzzleLight(Transform muzzle)
        {
            Transform child = EnsureChild(muzzle, "Muzzle Light");
            Light light = Ensure<Light>(child.gameObject);

            light.type = LightType.Point;
            light.range = 3f;
            light.intensity = 4f;
            light.shadows = LightShadows.None;

            // Off in the prefab. SetJet switches it on, and a gun that glows in the inventory is
            // a light source nobody asked for.
            light.enabled = false;

            return light;
        }

        // ── The gun ────────────────────────────────────────────────────────────

        private static GameObject BuildGunPrefab(GameObject portalPrefab, Material paintMaterial,
                                                 GameObject splatPrefab,
                                                 Material fluidPrimary, Material fluidSecondary)
        {
            string path = $"{PrefabFolder}/PortalGun.prefab";
            GameObject root = LoadOrCreateRoot(path, "PortalGun");

            // Before anything else, and before the early return below: a gun that cannot be
            // dropped is broken whether or not its model imported.
            EnsureWorldPresence(root);

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
            serializedItem.FindProperty("jet").objectReferenceValue = BuildJet(muzzle, paintMaterial);
            serializedItem.FindProperty("muzzleLight").objectReferenceValue = BuildMuzzleLight(muzzle);
            serializedItem.FindProperty("nozzle").objectReferenceValue = muzzle;
            serializedItem.FindProperty("splat").objectReferenceValue =
                splatPrefab.GetComponent<PortalSplat>();
            serializedItem.FindProperty("bodyRenderer").objectReferenceValue = bodyRenderer;
            serializedItem.FindProperty("primaryMaterialName").stringValue = PrimaryFluidName;
            serializedItem.FindProperty("secondaryMaterialName").stringValue = SecondaryFluidName;
            serializedItem.FindProperty("portalLifetime").floatValue = PortalLifetime;

            // Written, not left to the field defaults. The prefab is older than the hose and still
            // carried the hitscan gun's 55 m/s, so the paint was being traced along one parabola
            // while the droplets flew down another — the exact drift the constants above exist to
            // prevent, sitting in serialized data where a code default cannot reach it.
            serializedItem.FindProperty("jetSpeed").floatValue = JetSpeed;
            serializedItem.FindProperty("jetGravity").floatValue = JetGravity;
            serializedItem.FindProperty("jetFlightTime").floatValue = JetFlightTime;
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

        private static InventoryItem BuildItemAsset(GameObject gunPrefab)
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

            // Nothing else to do for the REGISTRY: RegistryLoader picks every
            // InventoryItem out of Resources/Items at startup, so being in this
            // folder IS being registered there. The network prefab list is a
            // separate list and is handled by RegisterNetworkPrefab.
            return item;
        }

        // ── The gun as an object in the world ──────────────────────────────────

        /// <summary>
        /// The components that let the gun exist in the world rather than only in a hand: spawned
        /// over the network, thrown, picked back up, and saved where it came to rest.
        ///
        /// The gun shipped without any of them, which is invisible for as long as it is only ever
        /// equipped out of the hotbar and then fails the instant somebody drops it —
        /// PlayerDropService routes through GameServices.World.Spawn, which refuses a prefab with
        /// no NetworkObject and says so: "Prefab 'PortalGun' has no NetworkObject, so it will only
        /// ever exist on the server." PickupableItem is itself a NetworkBehaviour, so it cannot be
        /// added without one either — and without PickupableItem a dropped gun is scenery that
        /// cannot be picked back up.
        ///
        /// Mirrors LightningSpell.prefab component for component; LaserStaffBuilder writes the
        /// same block out for a staff.
        /// </summary>
        private static void EnsureWorldPresence(GameObject root)
        {
            NetworkObject netObject = Ensure<NetworkObject>(root);
            netObject.SynchronizeTransform = true;

            // Roughly the gun's half-size at the 0.42 m it is held at. ItemGrip.keepColliders is
            // off, so this is disabled while held and only has to be right for the thing lying in
            // the sand.
            SphereCollider sphere = Ensure<SphereCollider>(root);
            sphere.radius = 0.18f;
            sphere.center = Vector3.zero;

            Rigidbody body = Ensure<Rigidbody>(root);
            body.isKinematic = true;
            body.useGravity = true;

            EnsureInternal(root, "SpaceGame.Items.PickupableItem");

            DropItemPhysics drop = Ensure<DropItemPhysics>(root);
            var serializedDrop = new SerializedObject(drop);
            serializedDrop.FindProperty("rb").objectReferenceValue = body;
            serializedDrop.FindProperty("groundLayer").intValue = GroundLayerMask;
            serializedDrop.ApplyModifiedPropertiesWithoutUndo();

            Ensure<SpaceGame.Core.NetRelay>(root);

            // prefabId and instanceId are left blank on purpose — SaveableEntity.OnValidate
            // stamps them, and a hand-written id is how two prefabs end up sharing one.
            Ensure<SpaceGame.Core.Persistence.SaveableEntity>(root);
            Ensure<SpaceGame.Core.Persistence.TransformSaveable>(root);
        }

        /// <summary>
        /// Point the gun prefab's pickup at its own item asset.
        ///
        /// Done after the prefab is saved rather than inside the build: the item asset references
        /// the prefab and the prefab references the item, so one of the two links can only be made
        /// once both files exist.
        /// </summary>
        private static void WireItemIntoPickup(GameObject gunPrefab, InventoryItem item)
        {
            if (gunPrefab == null || item == null) return;

            // By type NAME. PickupableItem is internal to Assembly-CSharp and cannot be named
            // from an editor assembly at all.
            Component pickup = gunPrefab.GetComponents<Component>()
                .FirstOrDefault(c => c != null &&
                                     c.GetType().FullName == "SpaceGame.Items.PickupableItem");

            if (pickup == null)
            {
                Debug.LogError("[Portals] PickupableItem missing from the gun prefab; " +
                               "a dropped gun could not be picked back up.");
                return;
            }

            var serialized = new SerializedObject(pickup);
            SerializedProperty slot = serialized.FindProperty("item");
            if (slot == null || slot.objectReferenceValue == item) return;

            slot.objectReferenceValue = item;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SavePrefabAsset(gunPrefab);
        }

        /// <summary>
        /// Add the gun to the list NetworkManager actually reads.
        ///
        /// An unregistered item prefab fails on CLIENTS ONLY — the host instantiates its own copy
        /// and never consults the list — so solo playtesting cannot find this mistake.
        ///
        /// Only the GUN belongs here. The apertures and the blob are instantiated locally by every
        /// machine out of Present(), which is exactly what a network prefab must not be.
        /// </summary>
        private static void RegisterNetworkPrefab(GameObject prefab)
        {
            if (prefab == null) return;

            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (list == null)
            {
                Debug.LogError($"[Portals] No network prefab list at {NetworkPrefabsPath}.");
                return;
            }

            if (list.Contains(prefab)) return;

            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
            Debug.Log("[Portals] Registered PortalGun as a network prefab.");
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

        /// <summary>
        /// <see cref="Ensure{T}"/> for a component this assembly cannot name.
        ///
        /// PickupableItem is internal to Assembly-CSharp, so an editor script reaches it the way
        /// the inspector does — by type name, off an assembly it can name.
        /// </summary>
        private static Component EnsureInternal(GameObject target, string typeName)
        {
            System.Type type = typeof(ItemGrip).Assembly.GetType(typeName);
            if (type == null)
            {
                Debug.LogError($"[Portals] Type '{typeName}' not found.");
                return null;
            }

            Component existing = target.GetComponent(type);
            return existing != null ? existing : target.AddComponent(type);
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
