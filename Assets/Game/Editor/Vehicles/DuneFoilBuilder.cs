// Builds Assets/Prefabs/agents/vehicle/DuneFoil.prefab from
// Assets/Art/Models/Vehicles/DuneFoil/dune_foil_rig.fbx, and the Wind prefab that drives it.
//
// The FBX already carries the rig — pivots, named parts, subdivided sails — because
// Assets/Art/Models/_Source~/models/vehicles/dune_foil_rig.py put them there. What this script adds is everything
// Unity-side: components on the right nodes, colliders, sail areas and lever arms measured off
// the meshes, the rope stations, the running rigging, and the sailcloth material.
//
// Everything geometric is measured at build time rather than hardcoded, so re-running
// dune_foil_rig.py after changing the model and rebuilding here still lands in the right place.
//
// Re-run from: Tools ▸ Vehicles ▸ Build Dune Foil Prefab
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Vehicles.DuneFoil;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public static class DuneFoilBuilder
    {
        // The model is authored bow-along-+Y in Blender, and the FBX exporter maps Blender +Y onto
        // Unity -Z — so an untouched import faces backwards, with the bow at negative Z and the foil
        // strut aft. Everything downstream reads "forward" as +Z, and sail lever arms take their
        // sign from it, so the model is yawed here before a single measurement is taken.
        private static readonly Quaternion ModelYaw = Quaternion.Euler(0f, 180f, 0f);

        private const string ModelPath = "Assets/Art/Models/Vehicles/DuneFoil/dune_foil_rig.fbx";
        private const string PrefabPath = "Assets/Prefabs/agents/vehicle/DuneFoil.prefab";
        private const string WindPrefabPath = "Assets/Prefabs/environment/Wind.prefab";
        private const string MaterialFolder = "Assets/Art/Models/Vehicles/DuneFoil/Materials";
        private const string SailMaterialPath = MaterialFolder + "/SailCloth_Foil.mat";
        private const string RopeMaterialPath = MaterialFolder + "/Rope_Foil.mat";

        // The four sails, and how each is rigged. Node names come from dune_foil_rig.py.
        //
        // `invert` exists because the two wing sails mirror each other: the same positive sheet
        // angle swings one outboard and the other inboard, so one of the pair has to be flipped.
        private readonly struct SailSpec
        {
            public readonly string Group;
            public readonly string Label;
            public readonly float MinSheet;
            public readonly float MaxSheet;
            public readonly bool Invert;

            public SailSpec(string group, string label, float minSheet, float maxSheet, bool invert)
            {
                Group = group; Label = label; MinSheet = minSheet; MaxSheet = maxSheet; Invert = invert;
            }
        }

        private static readonly SailSpec[] Sails =
        {
            // The main sheets furthest out — it is the sail that does the work on a reach, and the
            // full 90 degrees matters. Stopped at 80 the main is still making lateral force even
            // fully eased, and since it has six times the jib's area it then dominates the yaw at
            // every trim: the craft rounds up and can never bear away. Letting the sheet run right
            // out lets the main flag, the jib takes over, and the bow falls off the wind. That is
            // both how a real boat is steered on sail balance and what makes the rope a two-way
            // control rather than a throttle.
            new SailSpec("Main",  "Main",          5f, 90f, false),
            // A jib is sheeted much closer; let it out past about 45 degrees and it just flogs.
            new SailSpec("Jib",   "Jib",           3f, 45f, false),
            // The wing sails feather rather than sheet, so they travel a narrow range.
            new SailSpec("WingS", "WingStarboard", 2f, 35f, false),
            new SailSpec("WingP", "WingPort",      2f, 35f, true),
        };

        [MenuItem("Tools/Vehicles/Build Dune Foil Prefab")]
        public static void Build()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (source == null)
            {
                Debug.LogError($"[DuneFoilBuilder] No model at {ModelPath}. Run " +
                               "Assets/Art/Models/_Source~/models/vehicles/dune_foil_rig.py first:\n" +
                               "  blender --background Assets/Art/Models/_Source~/models/vehicles/dune_foil.blend " +
                               "--python Assets/Art/Models/_Source~/models/vehicles/dune_foil_rig.py");
                return;
            }

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);

            GameObject root = new GameObject("DuneFoil");
            model.transform.SetParent(root.transform, false);
            // Compose, never assign. The imported root carries the FBX importer's own Blender
            // Z-up -> Unity Y-up correction; overwriting it lays the whole craft on its side.
            model.transform.localRotation = ModelYaw * model.transform.localRotation;
            model.name = "Model";

            StripSceneJunk(model);

            Transform foilRoot = FindDeep(model.transform, "FOIL_Root") ?? model.transform;
            Transform hull = FindDeep(foilRoot, "Hull");
            Transform foilStrut = FindDeep(foilRoot, "Foil_Strut");

            if (hull == null || foilStrut == null)
            {
                Debug.LogError("[DuneFoilBuilder] The model is missing Hull or Foil_Strut. It was " +
                               "probably exported from the original blend rather than the rig.");
                Object.DestroyImmediate(root);
                return;
            }

            Material sailMaterial = EnsureSailMaterial();
            ApplySailMaterial(foilRoot, sailMaterial);

            SailRig rig = root.AddComponent<SailRig>();
            FoilLift foil = root.AddComponent<FoilLift>();
            DuneFoilLocomotion locomotion = root.AddComponent<DuneFoilLocomotion>();

            // The foil is the centre of lateral resistance. Every sail's steering lever is measured
            // from here, so it has to come off the model rather than a guess.
            float foilZ = MeasureBounds(foilStrut).center.z;
            float strutLength = MeasureStrutLength(foilRoot, foilStrut);

            List<SailSurface> surfaces = BuildSails(foilRoot, rig, foilZ, out SailSurface main,
                                                    out SailSurface jib);
            rig.Bind(surfaces, main, jib);

            // Place the centre of lateral resistance for a rig that balances.
            //
            // Taken literally, the modelled strut sits aft of every sail, so every sail would bear
            // the bow away and nothing could ever luff up — the craft would be unsteerable. Real
            // designers do not take it literally either: they place the CLR a small "lead" ahead of
            // the rig's combined centre of effort and tune from there. Doing the same puts the main
            // aft of it and the jib forward, which is what makes trimming one against the other a
            // helm.
            RebalanceLevers(surfaces, main, jib, foilRoot);

            foil.MaxRideHeight = strutLength;

            // Take-off has to sit below the speed the craft can reach while still ploughing, or it
            // can never get onto the foil at all: hull drag caps it just short, and the drag only
            // falls once it is up. 5 m/s leaves the craft stuck in light air and foiling in a real
            // sailing breeze, which is the transition the design is built around.
            foil.TakeoffSpeed = 5f;

            float deckLevel = BuildColliders(root, hull, foilStrut, out BoxCollider carryVolume);
            BuildStations(foilRoot, rig, main, jib, deckLevel);
            BuildRigging(root, foilRoot, surfaces);
            BuildBoardingRamp(root, hull, foil, deckLevel);

            // Bound explicitly to the deck. Left empty, WalkerPlatformCarrier builds its own volume
            // from every renderer on the craft — which on a foiler means the mast overhead and the
            // thirteen-metre strut below, so the box reaches well past the hull in all three axes and
            // "carried" comes to include anybody standing on the sand alongside. Walking up to a
            // parked craft then dragged the player about with the hull.
            WalkerPlatformCarrier carrier = root.AddComponent<WalkerPlatformCarrier>();
            var carrierSo = new SerializedObject(carrier);
            carrierSo.FindProperty("carryVolume").objectReferenceValue = carryVolume;
            carrierSo.ApplyModifiedPropertiesWithoutUndo();

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            EnsureWindPrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[DuneFoilBuilder] Built {PrefabPath}: {surfaces.Count} sails, " +
                      $"strut {strutLength:F2} m, CLR at z {foilZ:F2}. Wind at {WindPrefabPath}.");
        }

        // ---------------------------------------------------------------- sails

        private static List<SailSurface> BuildSails(Transform foilRoot, SailRig rig, float foilZ,
                                                    out SailSurface main, out SailSurface jib)
        {
            List<SailSurface> surfaces = new List<SailSurface>();
            main = null;
            jib = null;

            foreach (SailSpec spec in Sails)
            {
                Transform rake = FindDeep(foilRoot, spec.Group + "_Rake");
                Transform yaw = FindDeep(foilRoot, spec.Group + "_Yaw");
                Transform cloth = FindDeep(foilRoot, "Sail_" + spec.Group);
                Transform clew = FindDeep(foilRoot, "Anchor_" + spec.Group + "_Clew");

                if (rake == null || yaw == null || cloth == null)
                {
                    Debug.LogWarning($"[DuneFoilBuilder] Sail '{spec.Group}' is missing rig nodes " +
                                     "and was skipped.");
                    continue;
                }

                // The component goes on the rake pivot, so one sail is one object in the hierarchy.
                SailSurface sail = rake.gameObject.AddComponent<SailSurface>();

                float area = MeasureSailArea(cloth);
                float lever = MeasureBounds(cloth).center.z - foilZ;

                sail.Bind(rake, yaw, cloth.gameObject, clew, area, lever);
                sail.ConfigureSheet(spec.MinSheet, spec.MaxSheet, spec.Invert);

                // Ship it moored: sails furled.
                //
                // With sails set, a craft dropped into the world simply leaves. It catches the wind
                // on the frame the scene loads and sails off — measured at 293 m from the player
                // spawn within seconds of pressing play, foiling, with the gangway stowed and no way
                // to reach it. Furled, it sits on the sand with the gangway down until somebody
                // boards and hoists, which is also how a real craft is left.
                sail.SetHoisted(false);

                rake.name = spec.Label + "_Rig";
                surfaces.Add(sail);

                if (spec.Group == "Main") main = sail;
                if (spec.Group == "Jib") jib = sail;
            }

            return surfaces;
        }

        /// <summary>
        /// Move the centre of lateral resistance so the rig actually steers, then re-measure every
        /// lever arm against it. See the comment at the call site for why this is not cheating.
        /// </summary>
        private static void RebalanceLevers(List<SailSurface> surfaces, SailSurface main,
                                            SailSurface jib, Transform foilRoot)
        {
            if (surfaces.Count == 0) return;

            // Area-weighted centre of effort of the whole rig, in the craft's local Z.
            float totalArea = 0f;
            float weighted = 0f;
            Dictionary<SailSurface, float> centres = new Dictionary<SailSurface, float>();

            foreach (SailSurface sail in surfaces)
            {
                Transform cloth = FindDeep(foilRoot, "Sail_" + SailGroupOf(sail, foilRoot));
                if (cloth == null) continue;
                float z = MeasureBounds(cloth).center.z;
                centres[sail] = z;
                weighted += z * sail.Area;
                totalArea += sail.Area;
            }

            if (totalArea <= 0f || centres.Count == 0) return;

            float centreOfEffort = weighted / totalArea;

            // Where to put the resistance, between the two sails that steer.
            //
            // Not at the area-weighted centre of effort: the main carries most of the area, so the
            // CE sits practically on top of it and the main would be left with a few centimetres of
            // lever and no steering authority at all. Instead the CLR goes a fixed fraction of the
            // way from the main toward the jib. That leaves the main a modest arm and the jib a long
            // one, which is the right balance — the main has three times the area, so equal torque
            // needs a much shorter arm on it.
            float clr;
            if (main != null && jib != null && centres.ContainsKey(main) && centres.ContainsKey(jib))
            {
                // Tuned so the two sails have comparable authority (area x lever). At 0.25 the main
                // out-torqued the jib roughly 2:1 and no amount of jib could answer it.
                const float mainShare = 0.18f;
                clr = Mathf.Lerp(centres[main], centres[jib], mainShare);
            }
            else
            {
                // No recognisable main/jib pair — fall back to a small lead ahead of the CE, which
                // at least gives the craft mild weather helm rather than none.
                clr = centreOfEffort + 1.2f;
            }

            foreach (KeyValuePair<SailSurface, float> pair in centres)
                pair.Key.LeverArm = pair.Value - clr;

            if (main != null && jib != null && main.LeverArm * jib.LeverArm >= 0f)
            {
                Debug.LogWarning("[DuneFoilBuilder] Main and jib ended up on the same side of the " +
                                 "centre of resistance, so sail-balance steering will not work. " +
                                 "Check the sail positions in the model.");
            }
        }

        private static string SailGroupOf(SailSurface sail, Transform foilRoot)
        {
            foreach (SailSpec spec in Sails)
            {
                Transform rake = FindDeep(foilRoot, spec.Label + "_Rig")
                              ?? FindDeep(foilRoot, spec.Group + "_Rake");
                if (rake != null && rake == sail.transform) return spec.Group;
            }
            return string.Empty;
        }

        /// <summary>
        /// Sail area, summed off the actual triangles. A sail is a flat cut panel, so its mesh area
        /// is its sail area — no need to approximate from a bounding box.
        /// </summary>
        private static float MeasureSailArea(Transform cloth)
        {
            float area = 0f;
            foreach (MeshFilter mf in cloth.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null) continue;

                Vector3[] verts = mesh.vertices;
                int[] tris = mesh.triangles;
                Vector3 scale = mf.transform.lossyScale;

                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    Vector3 a = Vector3.Scale(verts[tris[i]], scale);
                    Vector3 b = Vector3.Scale(verts[tris[i + 1]], scale);
                    Vector3 c = Vector3.Scale(verts[tris[i + 2]], scale);
                    area += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                }
            }
            // The mesh is a single surface; both faces of it are the same sail.
            return Mathf.Max(area, 1f);
        }

        private static float MeasureStrutLength(Transform foilRoot, Transform foilStrut)
        {
            Bounds strut = MeasureBounds(foilStrut);
            // Root sits at the hull bottom, so how far the foil hangs below the origin is exactly
            // how far the craft can climb before the foil tip surfaces.
            float below = foilRoot.position.y - strut.min.y;
            return Mathf.Max(1f, below);
        }

        // ------------------------------------------------------------ colliders

        /// <summary>
        /// Minimum horizontal footprint, m², for a hull part to count as something to stand on.
        /// Filters the cleats, bollards and bulwark strakes out of the deck plates.
        /// </summary>
        private const float DeckPlateMinFootprint = 2.5f;

        /// <summary>
        /// Builds collision, and the deck the player stands on.
        ///
        /// The deck is measured from the actual deck plates, one box per plate at that plate's own
        /// height. It is emphatically NOT derived from the hull's overall height: the tallest thing
        /// on this hull is a winch, so topping the deck out at the hull's max Y puts an invisible
        /// floor 2.4 m above the planks, standing the player in mid-air with every control they are
        /// supposed to reach hanging below their feet. That is the whole reason the craft could
        /// neither be boarded nor sailed.
        /// </summary>
        /// <returns>World Y of the walking surface, for the stations and the gangway to sit on.</returns>
        private static float BuildColliders(GameObject root, Transform hull, Transform foilStrut,
                                            out BoxCollider carryVolume)
        {
            GameObject collision = new GameObject("Collision");
            collision.transform.SetParent(root.transform, false);

            Bounds hullBounds = MeasureBounds(hull);
            Vector3 local = root.transform.InverseTransformPoint(hullBounds.center);

            // --- the deck -----------------------------------------------------
            // One flat surface at the dominant plate's height, spanning all of them.
            //
            // Not a box per plate: the plates sit at 1.96, 2.16, 2.29 and 2.37, and a Rigidbody
            // capsule with no step offset cannot climb a 0.4 m ledge any more than it could climb
            // the rungs. Per-plate boxes would strand the player on whichever plate they landed on.
            // A single level costs a little clipping into the two small raised platforms and buys a
            // deck that can actually be walked from end to end.
            List<Bounds> plates = FindDeckPlates(hull);
            float deckLevel;
            Bounds deckSpan;

            if (plates.Count == 0)
            {
                Debug.LogWarning("[DuneFoilBuilder] No deck plates found; falling back to 55% of " +
                                 "hull height. Check DeckPlateMinFootprint.");
                deckLevel = hullBounds.min.y + hullBounds.size.y * 0.55f;
                deckSpan = new Bounds(
                    new Vector3(hullBounds.center.x, deckLevel, hullBounds.center.z),
                    new Vector3(hullBounds.size.x * 0.62f, 0.1f, hullBounds.size.z * 0.86f));
            }
            else
            {
                // The dominant plate is the one you actually walk on; here it is 27.8 m² against
                // 3–4 m² for the rest.
                Bounds dominant = plates[0];
                foreach (Bounds p in plates)
                    if (p.size.x * p.size.z > dominant.size.x * dominant.size.z) dominant = p;

                deckLevel = dominant.max.y;
                deckSpan = Encapsulate(plates);
            }

            AddDeckBox(collision.transform, root, "COL_Deck", deckSpan, deckLevel);

            // --- hull body, strictly below the deck ---------------------------
            // Topping this out above the walking surface would put the player inside it.
            GameObject body = new GameObject("COL_Hull");
            body.transform.SetParent(collision.transform, false);
            BoxCollider bodyBox = body.AddComponent<BoxCollider>();
            float bodyTop = deckLevel - 0.05f;
            float bodyHeight = Mathf.Max(0.2f, bodyTop - hullBounds.min.y);
            bodyBox.size = new Vector3(hullBounds.size.x * 0.9f, bodyHeight, hullBounds.size.z * 0.98f);
            bodyBox.center = root.transform.InverseTransformPoint(
                new Vector3(hullBounds.center.x, bodyTop - bodyHeight * 0.5f, hullBounds.center.z));

            // --- rails at the deck edge ---------------------------------------
            // Sized off the deck, not the hull: at speed this deck is 13 m up.
            //
            // The starboard rail is split around the gangway. A continuous rail there would be a
            // wall across the top of the ramp — you would walk all the way up and be stopped by the
            // handrail you were climbing to get behind.
            float gapCentre = BoardingZ(hullBounds);
            float gapHalf = BoardingGapWidth * 0.5f;

            foreach (int side in new[] { -1, 1 })
            {
                float x = side < 0 ? deckSpan.min.x - 0.09f : deckSpan.max.x + 0.09f;
                string baseName = side < 0 ? "COL_RailPort" : "COL_RailStarboard";

                if (side < 0)
                {
                    AddRail(collision.transform, root, baseName, x, deckLevel,
                            deckSpan.min.z, deckSpan.max.z);
                    continue;
                }

                AddRail(collision.transform, root, baseName + "_Aft", x, deckLevel,
                        deckSpan.min.z, gapCentre - gapHalf);
                AddRail(collision.transform, root, baseName + "_Fwd", x, deckLevel,
                        gapCentre + gapHalf, deckSpan.max.z);
            }

            // --- the strut ------------------------------------------------------
            GameObject strut = new GameObject("COL_Strut");
            strut.transform.SetParent(collision.transform, false);
            Bounds sb = MeasureBounds(foilStrut);
            BoxCollider strutBox = strut.AddComponent<BoxCollider>();
            strutBox.size = sb.size;
            strutBox.center = root.transform.InverseTransformPoint(sb.center);

            // --- carry volume ---------------------------------------------------
            // Head height over the deck and no further. Deliberately not the whole craft: this is
            // "who is standing on the planks", and anything looser than that carries bystanders.
            // Starts just below the deck so a capsule resting on it is inside, not straddling the lip.
            GameObject carry = new GameObject("COL_CarryVolume");
            carry.transform.SetParent(collision.transform, false);
            carryVolume = carry.AddComponent<BoxCollider>();
            carryVolume.isTrigger = true;
            const float carryHeight = 2.6f;
            carryVolume.size = new Vector3(deckSpan.size.x, carryHeight, deckSpan.size.z);
            carryVolume.center = root.transform.InverseTransformPoint(
                new Vector3(deckSpan.center.x, deckLevel - 0.3f + carryHeight * 0.5f, deckSpan.center.z));

            Debug.Log($"[DuneFoilBuilder] Deck: {plates.Count} plate(s), walking surface at " +
                      $"y {deckLevel:F2} (hull top is {hullBounds.max.y:F2}).");
            return deckLevel;
        }

        /// <summary>
        /// Hull parts big enough and flat enough to stand on. Each returned bounds is a real plate,
        /// so the deck follows the model's steps rather than flattening them.
        /// </summary>
        private static List<Bounds> FindDeckPlates(Transform hull)
        {
            List<Bounds> plates = new List<Bounds>();
            Bounds hullBounds = MeasureBounds(hull);

            foreach (Transform child in hull)
            {
                // Stations carry their own winch meshes and are not floor.
                if (child.name.StartsWith("Station_")) continue;
                if (child.name == "Rigging") continue;

                Renderer[] renderers = child.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;

                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

                if (b.size.x * b.size.z < DeckPlateMinFootprint) continue;
                // A plate is wide and flat. The hull shell is the same footprint but metres deep.
                if (b.size.y > 1.0f) continue;
                // Must be in the upper half of the hull — the underside pods are large and flat too.
                if (b.max.y < hullBounds.min.y + hullBounds.size.y * 0.35f) continue;

                plates.Add(b);
            }
            return plates;
        }

        private static void AddDeckBox(Transform parent, GameObject root, string name, Bounds span,
                                       float surfaceY)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            BoxCollider box = go.AddComponent<BoxCollider>();
            const float thickness = 0.3f;
            box.size = new Vector3(span.size.x, thickness, span.size.z);
            box.center = root.transform.InverseTransformPoint(
                new Vector3(span.center.x, surfaceY - thickness * 0.5f, span.center.z));
        }

        /// <summary>Width of the gap in the starboard rail, and of the gangway itself.</summary>
        private const float BoardingGapWidth = 2.2f;

        /// <summary>
        /// Where along the hull the player boards. One place, used by both the gangway and the gap
        /// in the rail, so the two cannot drift apart.
        /// </summary>
        private static float BoardingZ(Bounds hullBounds)
        {
            return hullBounds.center.z - hullBounds.size.z * 0.10f;
        }

        private static void AddRail(Transform parent, GameObject root, string name, float x,
                                    float deckLevel, float zFrom, float zTo)
        {
            float length = zTo - zFrom;
            if (length <= 0.1f) return;

            GameObject rail = new GameObject(name);
            rail.transform.SetParent(parent, false);
            BoxCollider rb = rail.AddComponent<BoxCollider>();
            rb.size = new Vector3(0.18f, 1.1f, length);
            rb.center = root.transform.InverseTransformPoint(
                new Vector3(x, deckLevel + 0.55f, (zFrom + zTo) * 0.5f));
        }

        private static Bounds Encapsulate(List<Bounds> list)
        {
            Bounds b = list[0];
            for (int i = 1; i < list.Count; i++) b.Encapsulate(list[i]);
            return b;
        }

        // ------------------------------------------------------------- stations

        private static readonly string[] StationNodes =
        {
            "Station_Hoist", "Station_MainSheet", "Station_JibSheet", "Station_MastRake",
        };

        private static void BuildStations(Transform foilRoot, SailRig rig, SailSurface main,
                                          SailSurface jib, float deckLevel)
        {
            // Size the handles from how far apart the winches actually are.
            //
            // A fixed radius does not work: these four sit between 1.3 m and 6 m apart along the
            // deck, so a radius chosen to make the lonely ones easy to hit makes the close pair
            // overlap — and then aiming at the far one of the pair just hits the near one, and a
            // control the player is looking straight at silently refuses.
            float nearest = float.MaxValue;
            List<Vector3> spots = new List<Vector3>();
            foreach (string n in StationNodes)
            {
                Transform t = FindDeep(foilRoot, n);
                if (t != null) spots.Add(t.position);
            }
            for (int i = 0; i < spots.Count; i++)
                for (int j = i + 1; j < spots.Count; j++)
                    nearest = Mathf.Min(nearest, Vector3.Distance(spots[i], spots[j]));

            // Comfortably inside half the gap, so neighbours never touch.
            float radius = spots.Count < 2 ? 0.7f : Mathf.Clamp(nearest * 0.40f, 0.30f, 0.70f);

            AddStation(foilRoot, "Station_Hoist", DuneFoilRiggingStation.StationFunction.Hoist,
                       rig, null, deckLevel, radius);
            AddStation(foilRoot, "Station_MainSheet", DuneFoilRiggingStation.StationFunction.Sheet,
                       rig, main, deckLevel, radius);
            AddStation(foilRoot, "Station_JibSheet", DuneFoilRiggingStation.StationFunction.Sheet,
                       rig, jib, deckLevel, radius);
            AddStation(foilRoot, "Station_MastRake", DuneFoilRiggingStation.StationFunction.MastRake,
                       rig, main, deckLevel, radius);

            Debug.Log($"[DuneFoilBuilder] Stations: closest pair {nearest:F2} m apart -> " +
                      $"handle radius {radius:F2} m.");
        }

        /// <summary>
        /// Makes one winch player-operable.
        ///
        /// The interaction collider is a separate child sitting at chest height above the deck,
        /// rather than a sphere wrapped around the winch mesh. The winches are modelled at and below
        /// deck level, so a collider centred on the mesh is half-buried in the deck and the player
        /// has to find the sliver of it that pokes through. A handle at a known height above the
        /// walking surface is something you can just look at.
        /// </summary>
        private static void AddStation(Transform foilRoot, string nodeName,
                                       DuneFoilRiggingStation.StationFunction function,
                                       SailRig rig, SailSurface sail, float deckLevel,
                                       float handleRadius)
        {
            const float handleHeight = 1.0f;      // above the deck: roughly chest height

            Transform node = FindDeep(foilRoot, nodeName);
            if (node == null)
            {
                Debug.LogWarning($"[DuneFoilBuilder] No '{nodeName}' node in the model; that " +
                                 "control will be missing from the prefab.");
                return;
            }

            DuneFoilRiggingStation station = node.gameObject.AddComponent<DuneFoilRiggingStation>();
            station.Bind(function, rig, sail);

            GameObject handle = new GameObject("Handle");
            handle.transform.SetParent(node, false);
            // Keep the winch's own x/z, lift it to a height the player can see and aim at.
            Vector3 world = new Vector3(node.position.x, deckLevel + handleHeight, node.position.z);
            handle.transform.position = world;

            SphereCollider hit = handle.AddComponent<SphereCollider>();
            hit.radius = handleRadius;
            hit.isTrigger = false;

            // Interactor resolves IInteractable by GetComponent then GetComponentInParent, so the
            // collider on this child finds the station on the node above it.
        }

        // -------------------------------------------------------------- rigging

        private static void BuildRigging(GameObject root, Transform foilRoot,
                                         List<SailSurface> surfaces)
        {
            Material ropeMaterial = EnsureRopeMaterial();

            GameObject ropes = new GameObject("Rigging");
            ropes.transform.SetParent(root.transform, false);

            // A sheet from each sail's clew to the winch that works it, so the rope visibly answers
            // the station the player is standing at.
            AddSheet(ropes.transform, foilRoot, "MainSheet", "Main", surfaces, ropeMaterial);
            AddSheet(ropes.transform, foilRoot, "JibSheet", "Jib", surfaces, ropeMaterial);
        }

        private static void AddSheet(Transform parent, Transform foilRoot, string anchorName,
                                     string group, List<SailSurface> surfaces, Material material)
        {
            Transform deckAnchor = FindDeep(foilRoot, "Anchor_" + anchorName + "_Deck");
            Transform clew = FindDeep(foilRoot, "Anchor_" + group + "_Clew");
            if (deckAnchor == null || clew == null) return;

            SailSurface sail = surfaces.FirstOrDefault(s => s.Clew == clew);

            GameObject rope = new GameObject("Rope_" + anchorName);
            rope.transform.SetParent(parent, false);

            LineRenderer line = rope.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.textureMode = LineTextureMode.Tile;

            RiggingRope r = rope.AddComponent<RiggingRope>();
            r.Bind(clew, deckAnchor, sail, sagMetres: 1.4f);
        }

        // -------------------------------------------------------------- boarding

        /// <summary>
        /// A gangway from the sand up to the deck.
        ///
        /// A ladder of rungs does not work here. The player is a Rigidbody capsule with no step
        /// offset — it cannot climb a stack of ledges, so the original rungs were scenery. A single
        /// continuous slope shallow enough to walk up is the only thing this movement code can
        /// actually board, so that is what this builds.
        ///
        /// <see cref="BoardingRamp"/> stows it once the craft lifts onto its foil.
        /// </summary>
        private static void BuildBoardingRamp(GameObject root, Transform hull, FoilLift foil,
                                              float deckLevel)
        {
            const float rampAngleDegrees = 28f;   // comfortably walkable
            const float rampThickness = 0.25f;
            float rampWidth = BoardingGapWidth - 0.4f;   // fits through the gap in the rail

            Bounds hullBounds = MeasureBounds(hull);

            GameObject holder = new GameObject("Boarding");
            holder.transform.SetParent(root.transform, false);

            // Top lands just inboard of the starboard deck edge; bottom reaches the sand, which is
            // where the root sits when the craft is parked.
            float groundY = root.transform.position.y;
            float rise = Mathf.Max(0.5f, deckLevel - groundY);
            float run = rise / Mathf.Tan(rampAngleDegrees * Mathf.Deg2Rad);
            float z = BoardingZ(hullBounds);

            Vector3 top = new Vector3(hullBounds.max.x - 0.4f, deckLevel, z);
            Vector3 bottom = new Vector3(top.x + run, groundY, z);

            GameObject ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "Gangway";
            ramp.transform.SetParent(holder.transform, false);

            Vector3 along = top - bottom;
            ramp.transform.position = (top + bottom) * 0.5f;
            ramp.transform.rotation = Quaternion.FromToRotation(Vector3.right, along.normalized);
            ramp.transform.localScale = new Vector3(along.magnitude, rampThickness, rampWidth);

            // Primitives arrive on URP's default Lit material, which would put a sixth material on
            // a craft that was deliberately reduced to five. Reuse the hull's dark wood instead.
            Material wood = FindModelMaterial(hull, "Mat_Foil_Wood_Dark");
            if (wood != null) ramp.GetComponent<MeshRenderer>().sharedMaterial = wood;

            BoardingRamp control = holder.AddComponent<BoardingRamp>();
            control.Bind(foil, ramp);

            Debug.Log($"[DuneFoilBuilder] Gangway: rise {rise:F2} m over {run:F2} m at " +
                      $"{rampAngleDegrees}deg, from y {groundY:F2} to deck y {deckLevel:F2}.");
        }

        /// <summary>Pull a material off the imported model by name, so nothing new is introduced.</summary>
        private static Material FindModelMaterial(Transform node, string name)
        {
            foreach (Renderer r in node.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (m != null && m.name == name) return m;
            return null;
        }

        // ------------------------------------------------------------- materials

        private static Material EnsureSailMaterial()
        {
            EnsureFolder(MaterialFolder);

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(SailMaterialPath);
            Shader shader = Shader.Find("SpaceGame/SailCloth");
            if (shader == null)
            {
                Debug.LogWarning("[DuneFoilBuilder] SpaceGame/SailCloth not found; sails will keep " +
                                 "their imported material and will not billow.");
                return null;
            }

            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, SailMaterialPath);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", new Color(0.90f, 0.71f, 0.46f, 1f));
            mat.SetFloat("_BillowDepth", 1.35f);
            mat.SetFloat("_FlutterAmp", 0.45f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material EnsureRopeMaterial()
        {
            EnsureFolder(MaterialFolder);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(RopeMaterialPath);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                             ?? Shader.Find("Sprites/Default");
                mat = new Material(shader);
                mat.color = new Color(0.30f, 0.24f, 0.16f, 1f);
                AssetDatabase.CreateAsset(mat, RopeMaterialPath);
            }
            return mat;
        }

        /// <summary>Point every sail's cloth at the billowing material; spars keep the wood.</summary>
        private static void ApplySailMaterial(Transform foilRoot, Material sailMaterial)
        {
            if (sailMaterial == null) return;

            foreach (SailSpec spec in Sails)
            {
                Transform cloth = FindDeep(foilRoot, "Sail_" + spec.Group);
                if (cloth == null) continue;

                foreach (MeshRenderer r in cloth.GetComponentsInChildren<MeshRenderer>(true))
                {
                    // Only the cloth panels. Battens under the same node stay wooden.
                    if (!r.name.Contains("_Cloth_")) continue;
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = sailMaterial;
                    r.sharedMaterials = mats;
                }
            }
        }

        // ------------------------------------------------------------------ wind

        private static void EnsureWindPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(WindPrefabPath) != null) return;

            EnsureFolder("Assets/Prefabs/environment");

            GameObject wind = new GameObject("Wind");
            wind.AddComponent<WindField>();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(WindPrefabPath));
            PrefabUtility.SaveAsPrefabAsset(wind, WindPrefabPath);
            Object.DestroyImmediate(wind);
        }

        // ----------------------------------------------------------------- utils

        private static void StripSceneJunk(GameObject model)
        {
            // An enabled camera or light inside a vehicle prefab hijacks rendering. The rig script
            // strips them, but a re-export from a different file might not.
            foreach (Camera c in model.GetComponentsInChildren<Camera>(true))
                Object.DestroyImmediate(c.gameObject);
            foreach (Light l in model.GetComponentsInChildren<Light>(true))
                Object.DestroyImmediate(l.gameObject);
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            foreach (Transform child in parent)
            {
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>World-space bounds of every renderer under a node.</summary>
        private static Bounds MeasureBounds(Transform node)
        {
            Renderer[] renderers = node.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(node.position, Vector3.one);

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
