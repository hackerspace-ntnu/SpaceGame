// Builds the jetpack prefab, its flame material, its smoke and its inventory item from the two
// FBXs jetpack_export.py writes.
//
// Re-runnable, and it OWNS the prefab: SaveAsPrefabAsset replaces it wholesale, so anything added
// by hand is stripped on the next run. That is the trap the wing pack fell into — it lost its
// NetworkObject, its PickupableItem and both savers exactly that way, with no error anywhere — so
// everything the prefab needs is added here or it is not on the prefab at all.
using System.Linq;
using SpaceGame.Items;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class JetpackBuilder
    {
        private const string ItemModelPath = "Assets/Game/Art/Models/Items/jetpack.fbx";
        private const string WornModelPath = "Assets/Game/Art/Models/Items/jetpack_worn.fbx";
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Equipment/Jetpack.prefab";
        private const string ItemPath = "Assets/Game/Resources/Items/Artifacts/Jetpack.asset";
        private const string FlamePath = "Assets/Game/Art/Materials/Items/JetFlame.mat";
        private const string FlameShader = "SpaceGame/Effects/JetFlame";
        private const string SmokePath = "Assets/Game/Art/Materials/Items/JetSmoke.mat";
        private const string SmokeShader = "SpaceGame/Effects/JetSmoke";
        private const string FlameMeshPath = "Assets/Game/Art/Models/Generated/JetFlameCone.asset";

        /// <summary>
        /// How big the carried pair is drawn in the hand, metres.
        ///
        /// <para>
        /// The measured longest axis of <c>Coll_Jetpack_Item</c> (0.674, printed by
        /// jetpack_export.py) times <see cref="SizeScale"/>. The measurement stays visible in the
        /// arithmetic on purpose, so a re-export that changes the model shows up here as a
        /// disagreement rather than being absorbed into a round number.
        /// </para>
        /// </summary>
        private const float HoldSize = 0.674f * SizeScale;

        /// <summary>
        /// How much bigger than modelled the pack is worn and carried. Two, asked for directly
        /// (2026-09-07: "the jetpack must be twice as big").
        ///
        /// <para>
        /// Applied HERE rather than in the .blend, and that is deliberate: <c>jetpack.blend</c> is
        /// hand-built and the user keeps editing it, so a scale baked into the model is a change
        /// that has to survive their next save. <c>WornSeat</c> and <c>ItemGrip</c> both size a
        /// model to a number, so this costs one multiply and touches nothing they own.
        /// </para>
        /// </summary>
        private const float SizeScale = 2f;

        /// <summary>
        /// How much room it takes on a pack face, metres — and deliberately far larger than the
        /// model.
        ///
        /// <para>
        /// This is the one number on the jetpack that is a DESIGN decision rather than a
        /// measurement, and it was asked for directly: the pack should cost about what the
        /// ornithopter costs (the wing pack's 1.82). Two motors, two fuel tanks and their
        /// plumbing are awkward cargo, and a machine that flies you anywhere ought to be paid for
        /// in the space it takes to carry. ItemFootprint documents that <c>packSize</c> exists for
        /// exactly this: half the item prefabs authored it deliberately and the rest fall back to
        /// <c>holdSize</c>.
        /// </para>
        /// </summary>
        private const float PackSize = 1.8f;

        /// <summary>
        /// The span of the worn pair across the wearer, metres — the measured longest axis of
        /// <c>Coll_Jetpack_Worn</c> (1.105, its pods at ±0.4462) times <see cref="SizeScale"/>.
        ///
        /// <para>
        /// The pods sit at HALF the lash rail's tip span, not on the tips (2026-09-07: the motors
        /// missed the rig). <see cref="SizeScale"/> doubles everything, spacing included, so pods
        /// authored on the tips stood 3.99 m across a wearer and the pack read as two lamps on a
        /// pole either side of the body. Halving lives in <c>jetpack_mirror.py</c> because it is a
        /// placement, not a size: <c>WornSeat</c> scales the whole model to this number, so
        /// shrinking it here would only shrink the pods with it and change nothing about the gap.
        /// </para>
        ///
        /// <para>
        /// Pinned rather than left at zero so a re-export that changed the model shows up as a
        /// number disagreeing with the exporter's printout rather than as pods that quietly drift
        /// off the back.
        /// </para>
        /// </summary>
        private const float WornSize = 1.105f * SizeScale;

        [MenuItem("Tools/SpaceGame/Items/Build Jetpack")]
        public static void Build()
        {
            GameObject carried = AssetDatabase.LoadAssetAtPath<GameObject>(ItemModelPath);
            if (carried == null)
            {
                Debug.LogError($"[Jetpack] No model at {ItemModelPath}. Run " +
                               "_Source~/models/gear/jetpack_export.py first.");
                return;
            }

            var root = new GameObject("Jetpack");

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(carried);
            visual.name = "Model";
            visual.transform.SetParent(root.transform, false);

            // The standard −90° X that puts Blender's up on Unity's +Y. It belongs here and NOT on
            // the worn model below: an `_exportlib` FBX arrives with the conversion already on its
            // nodes, so a second one is only correct for a model that is placed by its own bounds
            // (this one, carried in a hand) and is wrong for one authored in the wearer's frame.
            visual.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            Material flame = EnsureFlameMaterial();

            ExtractModelMaterials(ItemModelPath);
            ExtractModelMaterials(WornModelPath);

            AddWornModel(root);
            EnableTipEmission(root);
            BuildFlames(root, flame);

            JetpackNozzles nozzles = AddNozzles(root);
            AddItem(root, nozzles);
            AddFit(root);

            ItemGrip grip = root.AddComponent<ItemGrip>();
            var gripSo = new SerializedObject(grip);
            SetFloat(gripSo, "holdSize", HoldSize);
            SetFloat(gripSo, "packSize", PackSize);
            gripSo.ApplyModifiedPropertiesWithoutUndo();

            AddIfPresent(root, "SpaceGame.Items.PickupableItem");

            // Without a NetworkObject a dropped jetpack exists only on the host and never survives
            // a reload — and it must be registered in DefaultNetworkPrefabs.asset as well, which is
            // Tools ▸ SpaceGame ▸ Multiplayer ▸ Sync Network Prefabs.
            Unity.Netcode.NetworkObject netObject = root.AddComponent<Unity.Netcode.NetworkObject>();
            netObject.SynchronizeTransform = true;

            // Body, collider measured off the model, sizing, and the netcode that lets another
            // machine watch it be shoved about. One shared block.
            ItemWorldPresence.Apply(root);

            // prefabId and instanceId are left blank on purpose — SaveableEntity.OnValidate stamps
            // them, and a hand-written id is how two prefabs end up sharing one.
            root.AddComponent<SpaceGame.Core.Persistence.SaveableEntity>();
            root.AddComponent<SpaceGame.Core.Persistence.TransformSaveable>();

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            BuildInventoryItem(saved);
            VerifyPods(saved);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Jetpack] Built {PrefabPath}, {FlamePath} and {ItemPath}. " +
                      "Run Sync Network Prefabs, then Generate All Item Icons.");
        }

        /// <summary>
        /// Instantiate what was just saved and make <c>JetpackNozzles</c> resolve it for real.
        ///
        /// <para>
        /// Every binding in that component is a NAME in a hand-built model that keeps being
        /// re-arranged, and it resolves them in <c>Awake</c> — which does not run in the editor, so
        /// a re-export that renamed a part would ship a pack whose pods never move and whose
        /// flames never light, with a clean console, and would only be found by flying one. Calling
        /// <c>Resolve</c> here is the whole point of it being public.
        /// </para>
        /// <para>
        /// The check runs on the SAVED asset rather than on the in-memory root, so it sees exactly
        /// what the game will load — the difference that catches anything <c>SaveAsPrefabAsset</c>
        /// drops on the way out.
        /// </para>
        /// <para>
        /// It also flies the pack for a moment <b>in the form it is actually used in</b> — worn,
        /// throttle open — and asserts that smoke comes out. The pack shipped without a single
        /// puff because <c>WornVisual</c> read the smoke system as a third model and switched it
        /// off the instant the pack went on a back; the flames were fine, nothing logged, and the
        /// only way to see it was to fly one. A resolved binding is not the same as a working
        /// effect, so both are checked.
        /// </para>
        /// </summary>
        private static void VerifyPods(GameObject saved)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(saved);

            try
            {
                var nozzles = instance.GetComponent<JetpackNozzles>();
                if (nozzles == null)
                {
                    Debug.LogError("[Jetpack] The saved prefab has no JetpackNozzles.");
                    return;
                }

                nozzles.Resolve();

                // Two models on the item, two pods each, two flames a pod.
                if (nozzles.PodCount != 4 || nozzles.FlameCount != 8)
                {
                    Debug.LogError($"[Jetpack] The saved prefab resolves {nozzles.PodCount} pod(s) " +
                                   $"and {nozzles.FlameCount} flame(s); expected 4 and 8. The part " +
                                   "names in the model and the role names in JetpackNozzles have " +
                                   "diverged.");
                    return;
                }

                Debug.Log("[Jetpack] Verified: 4 pods, 8 flames resolved off the saved prefab.");
                VerifySmoke(instance, nozzles);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Fly the saved pack for a few frames, worn and at full throttle, and assert that the
        /// nozzles actually throw smoke.
        ///
        /// <para>
        /// Worn rather than carried, because worn is the only form that ever flies and it is the
        /// form the smoke was missing from — a check run on the carried pack would have passed
        /// throughout. The frames are driven by hand through <c>JetpackNozzles.Tick</c>: the
        /// editor never calls <c>LateUpdate</c>, and <c>Time.deltaTime</c> is zero here, so the
        /// component has to be handed a step it can spend.
        /// </para>
        /// </summary>
        private static void VerifySmoke(GameObject instance, JetpackNozzles nozzles)
        {
            WornVisual.SetForm(instance, WornVisual.Form.Worn);

            var smoke = instance.GetComponentInChildren<ParticleSystem>(true);
            if (smoke == null)
            {
                Debug.LogError("[Jetpack] The saved prefab has no smoke system at all.");
                return;
            }

            if (!smoke.gameObject.activeInHierarchy)
            {
                Debug.LogError("[Jetpack] The smoke system is switched OFF on a worn pack, so the " +
                               "pack flies without a trail however hot it gets. WornVisual hides " +
                               "every top-level child that is not the shown model — see its note " +
                               "on effects.");
                return;
            }

            smoke.Clear();
            nozzles.SetWearer(instance.transform);
            nozzles.Throttle = 1f;
            nozzles.Heat = 0f;

            for (int i = 0; i < SmokeVerifyFrames; i++) nozzles.Tick(SmokeVerifyStep);

            if (smoke.particleCount > 0)
            {
                Debug.Log($"[Jetpack] Verified: {smoke.particleCount} puff(s) off a worn pack at " +
                          "full throttle.");
                return;
            }

            Debug.LogError("[Jetpack] A worn pack at full throttle emits NO smoke. The system is " +
                           "active, so the emit path itself is broken — check the flame cones the " +
                           "puffs are thrown off and JetpackNozzles.smokePerSecond.");
        }

        /// <summary>How many frames <see cref="VerifySmoke"/> flies the pack for, and how long each
        /// one is. A tenth of a second is plenty at any sane rate, and short enough that the debt
        /// carried between frames is exercised rather than stepped over.</summary>
        private const int SmokeVerifyFrames = 6;
        private const float SmokeVerifyStep = 1f / 60f;

        /// <summary>
        /// The worn pair, as the <c>WornVisual</c> child the swap looks for by name.
        ///
        /// <para>
        /// <b>No rotation, unlike the carried model above.</b> An `_exportlib` FBX arrives ALREADY
        /// converted — every mesh node carries the Blender-to-Unity position and its own −90 X,
        /// with the vertices left in Blender's axes — so a −90 X here would be a SECOND conversion.
        /// The worn pair is placed by the lash rail through <c>WornSeat</c>, in the wearer's own
        /// frame, and any turn at all lands it off the bar. This is the mistake that put the
        /// wingsuit's wings at the waist pointing backwards, and it still looked plausible.
        /// </para>
        /// <para>
        /// Shipped switched off, which is load-bearing rather than tidy: <c>WornSeat</c> scales a
        /// worn item so its measured size matches the fit, and <c>ItemBounds</c> measures only the
        /// renderers that are on. A visible 2 m worn pair would make the CARRIED pack measure two
        /// metres across and scale it to a sliver in the hand.
        /// </para>
        /// </summary>
        private static void AddWornModel(GameObject root)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(WornModelPath);
            if (model == null)
            {
                Debug.LogError($"[Jetpack] No worn model at {WornModelPath}. Run " +
                               "_Source~/models/gear/jetpack_export.py first. Without it the pack " +
                               "is worn as the two motors clamped together.");
                return;
            }

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
            visual.name = WornVisual.ChildName;
            visual.transform.SetParent(root.transform, false);
            visual.SetActive(false);
        }

        /// <summary>
        /// Pull the FBX's embedded materials out into real project assets.
        ///
        /// <para>
        /// <b>An embedded material cannot be edited, and that silently defeated the heat glow.</b>
        /// The pods ship with the authored <c>Material.002</c>-<c>Material.012</c> living inside
        /// the FBX (<c>materialLocation = InPrefab</c>), where they are imported sub-assets:
        /// <c>EnableKeyword</c> on one appears to work, dirties nothing, and is gone on the next
        /// import - so the tips simply never went red, with a clean console.
        /// </para>
        /// <para>
        /// Extracting preserves the look exactly - the same colours, in a file that can now hold a
        /// keyword - and the importer is switched to search for them by name, so a re-export binds
        /// back to these rather than making fresh embedded copies. The materials are NOT recoloured
        /// to the palette here: that is the look the user chose, and jetpack_BUILD.md records it as
        /// a deliberate gap rather than an oversight.
        /// </para>
        /// </summary>
        private static void ExtractModelMaterials(string modelPath)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null) return;
            if (importer.materialLocation == ModelImporterMaterialLocation.External) return;

            string folder = System.IO.Path.GetDirectoryName(modelPath) + "/" +
                            System.IO.Path.GetFileNameWithoutExtension(modelPath) + "_Materials";
            System.IO.Directory.CreateDirectory(folder);

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            {
                if (asset is not Material material) continue;

                string target = AssetDatabase.GenerateUniqueAssetPath(
                    folder + "/" + material.name + ".mat");
                string error = AssetDatabase.ExtractAsset(material, target);

                if (!string.IsNullOrEmpty(error))
                    Debug.LogWarning("[Jetpack] Could not extract '" + material.name + "' from " +
                                     modelPath + ": " + error);
            }

            importer.materialLocation = ModelImporterMaterialLocation.External;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Build the four plume cones - one per exhaust, on both models.
        ///
        /// <para>
        /// <b>The exhaust meshes are not plumes and cannot be used as one.</b> They measure
        /// 5.9 x 2.6 x 5.9 cm: flat DISCS at the nozzle mouth, whose longest axis is a tie between
        /// the two across the disc and whose real outflow direction is the shortest one, the disc
        /// normal. Generating the flame removes that guess instead of making it more cleverly, and
        /// it gives the shader a frame it can rely on - a unit cone, base at y = 0 radius 1, tip at
        /// y = 1 radius 0, so every number in <c>JetFlame.shader</c> is a fraction.
        /// </para>
        /// <para>
        /// <b>Parented to the MODEL ROOT, not to the exhaust</b>, and that is not a stylistic
        /// choice. The mirror in <c>jetpack_mirror.py</c> arrives as a NEGATIVE, non-uniform
        /// <c>lossyScale</c> on every left-hand part (-2.57, -3.02, -2.57 on an exhaust), so a cone
        /// hung under one would come out mirrored and squashed by a different amount on each axis.
        /// The model root is a clean identity scale. It still vectors with the nozzle, because
        /// <c>JetpackNozzles</c> takes everything matching its role names - the flames included -
        /// and swings the lot about the pod's measured gimbal.
        /// </para>
        /// </summary>
        private static void BuildFlames(GameObject root, Material flame)
        {
            if (flame == null) return;

            Mesh cone = EnsureFlameMesh();
            int built = 0;

            foreach (Transform model in root.transform)
            {
                foreach (Renderer exhaust in model.GetComponentsInChildren<Renderer>(true))
                {
                    if (!exhaust.name.Contains("ExhaustInner") &&
                        !exhaust.name.Contains("ExhaustOuter")) continue;

                    Transform housing = NearestHousing(model, exhaust.transform);
                    if (housing == null) continue;

                    var go = new GameObject(JetpackNozzles.FlamePrefix + exhaust.name);
                    go.transform.SetParent(model, false);

                    go.AddComponent<MeshFilter>().sharedMesh = cone;
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = flame;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;

                    Place(go.transform, exhaust, housing);
                    built++;
                }
            }

            // Two models on the item - the carried pair and the worn pair - and two exhausts on
            // each pod, so eight. Four would mean only one form got them, which is the shape of
            // failure where a pack flames in the hand and not on the back.
            if (built != 8)
                Debug.LogWarning("[Jetpack] Built " + built + " flame(s); expected 8 (four per " +
                                 "model). Check the exhaust names against jetpack_export.py.");
        }

        /// <summary>
        /// Aim one cone: rooted at the disc, pointing the way the exhaust actually blows.
        ///
        /// <para>
        /// The outflow is the disc's NORMAL, taken as its mesh's thinnest local axis and then
        /// pointed away from the pod's own housing. Neither half is assumed: the thinnest axis is
        /// measured off the mesh, and the sign comes from a second part of the same pod, because a
        /// hand-built model can be authored either way round and a flame growing back up into the
        /// tank is the failure that would result.
        /// </para>
        /// <para>
        /// Sized in WORLD metres and then divided back through the parent's scale, because the
        /// disc's own scale is non-uniform and mirrored - reading <c>mesh.bounds</c> alone would
        /// give a flame two and a half to three times the wrong size, differently on each axis.
        /// </para>
        /// </summary>
        private static void Place(Transform flame, Renderer exhaust, Transform housing)
        {
            var filter = exhaust.GetComponent<MeshFilter>();
            Vector3 mesh = filter != null && filter.sharedMesh != null
                ? filter.sharedMesh.bounds.size
                : Vector3.one;

            int thinnest = mesh.x <= mesh.y && mesh.x <= mesh.z ? 0 : mesh.y <= mesh.z ? 1 : 2;
            Vector3 localNormal = thinnest == 0 ? Vector3.right
                                : thinnest == 1 ? Vector3.up
                                                : Vector3.forward;

            Vector3 outflow = exhaust.transform.TransformDirection(localNormal).normalized;
            if (Vector3.Dot(outflow, exhaust.bounds.center - housing.position) < 0f)
                outflow = -outflow;

            // The disc across its two wide axes, brought into world metres through the part's own
            // scale. Halved for the radius, then trimmed so the fire sits inside the nozzle lip
            // rather than flaring out past it.
            Vector3 scale = exhaust.transform.lossyScale;
            float across = Mathf.Max(
                mesh[(thinnest + 1) % 3] * Mathf.Abs(scale[(thinnest + 1) % 3]),
                mesh[(thinnest + 2) % 3] * Mathf.Abs(scale[(thinnest + 2) % 3]));
            float radius = 0.5f * across * FlameWidthShare;

            // Half the disc's thickness along the outflow, so the cone starts at the nozzle's
            // outer face. Rooted at the disc's centre the first few centimetres of every flame sit
            // INSIDE the nozzle geometry, which at this scale is most of the visible root.
            float lip = 0.5f * mesh[thinnest] * Mathf.Abs(scale[thinnest]);
            flame.position = exhaust.bounds.center + outflow * lip;

            // LookRotation puts the cone's +Z down the outflow; the extra pitch turns that into +Y,
            // which is the axis the unit cone - and therefore the shader - is authored along.
            flame.rotation = Quaternion.LookRotation(outflow) * Quaternion.Euler(90f, 0f, 0f);

            // The parent is the model root at identity scale, so this is world size directly. Kept
            // as a division anyway: the day somebody parents this elsewhere it stays correct.
            Vector3 parent = flame.parent != null ? flame.parent.lossyScale : Vector3.one;
            flame.localScale = new Vector3(radius / Mathf.Abs(parent.x),
                                           radius * FlameLengthPerRadius / Mathf.Abs(parent.y),
                                           radius / Mathf.Abs(parent.z));
        }

        /// <summary>
        /// The housing on the same pod as this exhaust - the pod's body, and therefore which way is
        /// "back up into the machine". Nearest rather than name-matched, so it keeps working if the
        /// side suffixes are ever renamed.
        /// </summary>
        private static Transform NearestHousing(Transform model, Transform exhaust)
        {
            Transform best = null;
            float bestDistance = float.MaxValue;

            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
            {
                if (!part.name.Contains("Housing")) continue;

                float distance = Vector3.SqrMagnitude(part.position - exhaust.position);
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = part;
            }

            return best;
        }

        /// <summary>
        /// How wide the flame is against the nozzle it comes out of. One, so the fire fills the
        /// nozzle mouth exactly: the cone narrows away from the base anyway, so a base at the full
        /// bore still reads as something coming out of the lip rather than as a cylinder stuck on
        /// the end - and a plume two and a half times as long off a pinched root reads as a
        /// needle. Above 1 it would flare past the hardware and the root would float.
        /// </summary>
        private const float FlameWidthShare = 1.0f;

        /// <summary>
        /// How long the flame is against its own radius, at full throttle.
        ///
        /// Seven against a pod that is now twice its modelled size puts the four flames at roughly
        /// a metre each - two and a half times the 40 cm the first cut drew, which the user asked
        /// for by eye. The restrained version was legible but never dramatic: at 40 cm the plume
        /// was shorter than the pack, so a full-thrust burn and a levitate looked like the same
        /// machine from any distance. It is deliberately a rocket now.
        ///
        /// This is a LENGTH-PER-RADIUS, so it does not move alone: <see cref="FlameWidthShare"/>
        /// widened the base at the same time, and the two together are the 2.5x.
        /// </summary>
        private const float FlameLengthPerRadius = 7.0f;

        /// <summary>
        /// The unit cone every flame is drawn on: base at y = 0 radius 1, tip at y = 1 radius 0.
        ///
        /// Normalized on purpose - it is the contract <c>JetFlame.shader</c> reads object space
        /// against, which is what lets that shader hold no measurements at all. Saved as one shared
        /// asset rather than four meshes built at runtime.
        /// </summary>
        private static Mesh EnsureFlameMesh()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(FlameMeshPath);
            if (existing != null) return existing;

            const int sides = 12;
            var vertices = new Vector3[sides + 1];
            var triangles = new int[sides * 3];

            vertices[0] = new Vector3(0f, 1f, 0f);   // the tip

            for (int i = 0; i < sides; i++)
            {
                float angle = i / (float)sides * Mathf.PI * 2f;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = 1 + i;
                triangles[i * 3 + 2] = 1 + (i + 1) % sides;
            }

            var mesh = new Mesh { name = "JetFlameCone" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FlameMeshPath));
            AssetDatabase.CreateAsset(mesh, FlameMeshPath);

            return mesh;
        }

        /// <summary>
        /// Turn emission ON, at black, on the four nozzle cones' materials.
        ///
        /// <para>
        /// <b>A MaterialPropertyBlock cannot enable a shader keyword</b>, and URP/Lit ignores
        /// <c>_EmissionColor</c> entirely without <c>_EMISSION</c>. So the glow that
        /// <c>JetpackNozzles</c> drives at runtime has to be armed here, at author time, and armed
        /// at BLACK so nothing about the look the user chose changes until the pack is actually
        /// hot. Without this the tips simply never go red, with a clean console — which is the
        /// exact shape of failure this project keeps paying for.
        /// </para>
        /// </summary>
        private static void EnableTipEmission(GameObject root)
        {
            int armed = 0;

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.name.Contains("NozzleInnerCone") &&
                    !renderer.name.Contains("NozzleOuterCone")) continue;

                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || !material.HasProperty("_EmissionColor")) continue;

                    material.EnableKeyword("_EMISSION");
                    material.globalIlluminationFlags =
                        MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    material.SetColor("_EmissionColor", Color.black);
                    EditorUtility.SetDirty(material);
                    armed++;
                }
            }

            if (armed == 0)
                Debug.LogWarning("[Jetpack] No nozzle cone material could be armed for emission. " +
                                 "The tips will not glow when the pack overheats — check the part " +
                                 "names against jetpack_export.py.");
        }

        /// <summary>
        /// The flame material. One shared asset for all four exhausts — every per-flame number
        /// (throttle, heat, and the measured plume frame) travels in a property block, so nothing
        /// here is ever instanced.
        /// </summary>
        private static Material EnsureFlameMaterial()
        {
            var shader = Shader.Find(FlameShader);
            if (shader == null)
            {
                Debug.LogError($"[Jetpack] Shader '{FlameShader}' not found. The exhausts will " +
                               "keep the authored grey and there will be no flame.");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(FlamePath);
            if (material == null)
            {
                material = new Material(shader);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FlamePath));
                AssetDatabase.CreateAsset(material, FlamePath);
            }
            else
            {
                material.shader = shader;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// The pods component and the one smoke system it emits through.
        ///
        /// <para>
        /// The system is built PLAYING with its emission DISABLED — the shipped manual-emit shape
        /// (see <c>GravelBlastFx</c>). A system that is not playing ignores <c>Emit</c>; a system
        /// with emission enabled smokes constantly; and without <c>AlwaysSimulate</c> the puffs
        /// freeze the moment the wearer walks off screen, which for a pack worn on somebody's back
        /// is most of the time.
        /// </para>
        /// <para>
        /// <b>World simulation space</b>, because smoke is meant to be left behind. Local space
        /// would drag every puff along with the flying player, which reads as steam pinned to the
        /// pack rather than as a trail.
        /// </para>
        /// </summary>
        private static JetpackNozzles AddNozzles(GameObject root)
        {
            var smokeObject = new GameObject("Smoke");
            smokeObject.transform.SetParent(root.transform, false);

            var smoke = smokeObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = smoke.main;
            main.duration = 5f;
            main.loop = true;
            main.playOnAwake = true;

            // A range rather than a number on both of these, and that is what makes a stack of
            // flat quads read as one body of smoke: puffs born together must not grow and die
            // together, or the trail pulses.
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 3.4f);

            // Zero, because JetpackNozzles hands every puff its own velocity down the nozzle it
            // came out of. A start speed here would add a second, undirected push on top.
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.70f);

            // Thin on its own and thick where several overlap. A puff opaque enough to read alone
            // stacks into a flat grey slab, which is the opposite of the depth being asked for.
            main.startColor = new Color(0.34f, 0.33f, 0.31f, 0.42f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.04f;   // smoke rises, slightly
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // Local, not Shape: the pack is scaled by WornSeat to fit the wearer, and a Hierarchy
            // scaling mode would scale the puffs with it — smoke that changes size depending on
            // whose back the pack is on.
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            // Four nozzles at up to ~50 puffs a second each, living up to 3.4 s, is about 680 in
            // the air at an overheat. A cap below that silently thins the trail exactly when it is
            // trying to warn you.
            main.maxParticles = 900;

            ParticleSystem.EmissionModule emission = smoke.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = smoke.shape;
            shape.enabled = false;

            // Stylized: a few flat quads that grow and fade, not a volumetric plume. One curve
            // each rather than gradients over lifetime with keys nobody will ever tune.
            // The puff is thrown out hard and then stops, which is what makes the trail sit in the
            // air behind the player instead of streaking away from them.
            ParticleSystem.LimitVelocityOverLifetimeModule drag = smoke.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.dampen = 0.12f;
            drag.limit = new ParticleSystem.MinMaxCurve(1.2f);

            ParticleSystem.SizeOverLifetimeModule size = smoke.sizeOverLifetime;
            size.enabled = true;
            // Born small and ending near two metres across. The growth is most of the volume:
            // four columns of expanding puffs overlapping each other is what stands in for a
            // volumetric plume here, since nothing in this project renders one for real.
            size.size = new ParticleSystem.MinMaxCurve(
                1f, AnimationCurve.EaseInOut(0f, 0.30f, 1f, 3.4f));

            ParticleSystem.ColorOverLifetimeModule fade = smoke.colorOverLifetime;
            fade.enabled = true;
            fade.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.18f),
                    new GradientAlphaKey(0f, 1f),
                },
            });

            // A script-created ParticleSystem's renderer has a NULL material and draws NOTHING,
            // silently — no warning, no magenta, just no smoke. That is exactly how this shipped
            // the first time.
            var smokeRenderer = smokeObject.GetComponent<ParticleSystemRenderer>();
            smokeRenderer.sharedMaterial = EnsureSmokeMaterial();
            smokeRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            smokeRenderer.alignment = ParticleSystemRenderSpace.View;
            smokeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            smokeRenderer.receiveShadows = false;
            smokeRenderer.sortingFudge = 0f;

            JetpackNozzles nozzles = root.AddComponent<JetpackNozzles>();
            var so = new SerializedObject(nozzles);
            Set(so, "smoke", smoke);
            so.ApplyModifiedPropertiesWithoutUndo();

            return nozzles;
        }

        /// <summary>
        /// The smoke material. Its own shader rather than a Unity default, for the reason every
        /// other surface in this project has one: there is no art in the repo to point a particle
        /// at, and a soft-smoke texture would be the only image file the whole item needed.
        /// </summary>
        private static Material EnsureSmokeMaterial()
        {
            var shader = Shader.Find(SmokeShader);
            if (shader == null)
            {
                Debug.LogError($"[Jetpack] Shader '{SmokeShader}' not found. The smoke renderer " +
                               "will have no material and draw nothing at all, silently.");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(SmokePath);
            if (material == null)
            {
                material = new Material(shader);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SmokePath));
                AssetDatabase.CreateAsset(material, SmokePath);
            }
            else
            {
                material.shader = shader;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void AddItem(GameObject root, JetpackNozzles nozzles)
        {
            JetpackItem item = root.AddComponent<JetpackItem>();
            var so = new SerializedObject(item);
            Set(so, "nozzles", nozzles);

            // Unlimited: the pack is equipment, not a consumable. Its limit is heat, and heat is
            // not a use count. −1 is UsableItem's sentinel.
            SetInt(so, "maxUses", -1);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Where the worn pack sits on the torso.
        ///
        /// <para>
        /// <b>Rail-anchored, unlike the wingsuit.</b> The pods are authored 1.785 m apart to land
        /// on the expedition rig's lash-rail tips, which is exactly what back gear normally gets:
        /// <c>BackpackController.GearMount</c> hands out the rail and <c>WornSeat</c> puts the
        /// item's origin on it, so <c>anchorToBone</c> stays off and the local offset below is
        /// only the fallback for a back with no pack on it.
        /// </para>
        /// <para>
        /// <c>holdsArmsOut</c> is deliberately NOT set. It exists for the wingsuit alone, whose
        /// membranes fold into the ribs with the arms down; a pair of pods on a bar stands clear
        /// of the arms and reads in the ordinary idle.
        /// </para>
        /// </summary>
        private static void AddFit(GameObject root)
        {
            WornFit fit = root.AddComponent<WornFit>();
            var so = new SerializedObject(fit);

            // Behind the spine and a little up, so a pack on a rig-less back still reads as worn
            // rather than as growing out of the chest. Superseded by the rail whenever there is one.
            SetVector(so, "localPosition", new Vector3(0f, 0.06f, -0.20f));
            SetVector(so, "localEuler", Vector3.zero);
            SetBool(so, "anchorToBone", false);
            SetFloat(so, "size", WornSize);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildInventoryItem(GameObject prefab)
        {
            InventoryItem item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
            bool isNew = item == null;
            if (isNew)
                item = ScriptableObject.CreateInstance<InventoryItem>();

            item.itemName = "Jetpack";
            item.itemPrefab = prefab;

            // The torso slot, which it now shares with the wing pack and the wingsuit — one of the
            // three, never two. No rule is needed: there is a single slot and all three want it.
            item.equipKind = EquipKind.Back;

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ItemPath));
            if (isNew)
                AssetDatabase.CreateAsset(item, ItemPath);
            else
                EditorUtility.SetDirty(item);
        }

        private static void AddIfPresent(GameObject go, string typeName)
        {
            System.Type t = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(typeName))
                .FirstOrDefault(x => x != null);
            if (t != null) go.AddComponent(t);
            else Debug.LogWarning($"[Jetpack] No type '{typeName}'; skipped.");
        }

        private static void Set(SerializedObject so, string field, Object value)
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

        private static void SetVector(SerializedObject so, string field, Vector3 value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.vector3Value = value;
        }

        private static void SetBool(SerializedObject so, string field, bool value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.boolValue = value;
        }

        private static SerializedProperty Find(SerializedObject so, string field)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
                Debug.LogWarning($"[Jetpack] {so.targetObject.GetType().Name} has no serialized " +
                                 $"field '{field}'; left at its default.");
            return p;
        }
    }
}
