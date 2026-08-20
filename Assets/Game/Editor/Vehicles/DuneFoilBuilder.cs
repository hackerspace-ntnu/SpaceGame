// Builds Assets/Game/Prefabs/agents/vehicle/DuneFoil.prefab from
// Assets/Game/Art/Models/Vehicles/DuneFoil/dune_foil_rig.fbx, and the Wind prefab that drives it.
//
// The FBX already carries the rig — pivots, named parts, subdivided sails — because
// Assets/Game/Art/Models/_Source~/models/vehicles/dune_foil_rig.py put them there. What this script adds is everything
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

        private const string ModelPath = "Assets/Game/Art/Models/Vehicles/DuneFoil/dune_foil_rig.fbx";
        // Both paths were left behind by the domain-first restructure, and both fail silently
        // rather than loudly: SaveAsPrefabAsset happily creates the folder it is given, so a
        // rebuild wrote a brand-new prefab at the old path while the world instance in
        // persistentScene went on referencing the real one, untouched. The build log said it had
        // succeeded. Anything here that names a folder is worth re-checking against disk.
        private const string PrefabPath =
            "Assets/Game/Prefabs/Agents/Vehicles/Ground/DuneFoil.prefab";
        private const string WindPrefabPath = "Assets/Game/Prefabs/Environment/Sky/Wind.prefab";
        private const string MaterialFolder = "Assets/Game/Art/Models/Vehicles/DuneFoil/Materials";
        private const string SailMaterialPath = MaterialFolder + "/SailCloth_Foil.mat";
        private const string RopeMaterialPath = MaterialFolder + "/Rope_Foil.mat";

        // The four sails, and how each is rigged. Node names come from dune_foil_rig.py.
        //
        // Only two of them have a winch. The wing panels are part of the main's sail plan and are
        // trimmed with it — see the sheet slaving in BuildSails — so the craft has exactly the
        // three controls it is meant to have: the helm, the main (sheet and post cant), and the
        // jib. A sail with no control that still makes force is a rig that fights the player for
        // reasons they can neither see nor change.
        private readonly struct SailSpec
        {
            public readonly string Group;
            public readonly string Label;
            public readonly float MinSheet;
            public readonly float MaxSheet;

            public SailSpec(string group, string label, float minSheet, float maxSheet)
            {
                Group = group; Label = label; MinSheet = minSheet; MaxSheet = maxSheet;
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
            new SailSpec("Main",  "Main",          5f, 90f),
            // A jib is sheeted much closer; let it out past about 45 degrees and it just flogs.
            new SailSpec("Jib",   "Jib",           3f, 45f),
            // The wing sails feather rather than sheet, so they travel a narrow range.
            new SailSpec("WingS", "WingStarboard", 2f, 35f),
            new SailSpec("WingP", "WingPort",      2f, 35f),
        };

        /// <summary>How far the main's post lays down at full lean, degrees.</summary>
        private const float MaxCantAngle = 32f;

        /// <summary>
        /// Righting moment the rig's own weight makes at full lean, per square metre of sail.
        ///
        /// Tuned so that leaning the main's post right into the wind removes roughly half the
        /// heel it would otherwise carry in a good breeze. Much less and the control is a
        /// decoration; much more and leaning the post becomes a way to sail the craft on its
        /// other ear, which is neither useful nor what a canting rig does.
        /// </summary>
        private const float CantRightingPerSquareMetre = 70f;

        [MenuItem("Tools/Vehicles/Build Dune Foil Prefab")]
        public static void Build()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (source == null)
            {
                Debug.LogError($"[DuneFoilBuilder] No model at {ModelPath}. Run " +
                               "Assets/Game/Art/Models/_Source~/models/vehicles/dune_foil_rig.py first:\n" +
                               "  blender --background Assets/Game/Art/Models/_Source~/models/vehicles/dune_foil.blend " +
                               "--python Assets/Game/Art/Models/_Source~/models/vehicles/dune_foil_rig.py");
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
            FoilRudder rudder = root.AddComponent<FoilRudder>();
            DuneFoilLocomotion locomotion = root.AddComponent<DuneFoilLocomotion>();

            // The foil is the centre of lateral resistance. Every sail's steering lever is measured
            // from here, so it has to come off the model rather than a guess.
            float foilZ = MeasureBounds(foilStrut).center.z;
            float strutLength = MeasureStrutLength(foilRoot, foilStrut);

            // The wheel turns the strut. Its own node is the pivot — that is where the strut meets
            // the hull — so all this has to do is hand FoilRudder the transform and tell it how
            // long the craft is.
            //
            // The wheelbase is the hull, NOT the blade's lever arm off the pivot. Taken literally
            // the blade sits 3.6 m aft of its own pivot, which would put an eighteen-metre hull on
            // a ten-metre turning circle at half lock — a craft that pirouettes. The hull resists
            // sliding sideways along its whole length and that is what actually sets the circle.
            Bounds hullBounds = MeasureBounds(hull);
            rudder.Bind(foilStrut, hullBounds.size.z);

            List<SailSurface> surfaces = BuildSails(root.transform, foilRoot, rig, foilZ,
                                                    out SailSurface main, out SailSurface jib);
            rig.Bind(surfaces, main, jib);

            // The wing panels answer the main's winch. Done after the rig is bound so the whole
            // set exists to point at.
            BindWingsToMain(surfaces, main, jib);

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
            BuildHelm(foilRoot, locomotion, deckLevel);
            BuildRigging(root, foilRoot, surfaces);
            BuildBoardingRamp(root, hull, foil, deckLevel);

            // Bound explicitly to the deck. Left empty, WalkerPlatformCarrier builds its own volume
            // from every renderer on the craft — which on a foiler means the mast overhead and the
            // thirteen-metre strut below, so the box reaches well past the hull in all three axes and
            // "carried" comes to include anybody standing on the sand alongside. Walking up to a
            // parked craft then dragged the player about with the hull.
            WalkerPlatformCarrier carrier = root.AddComponent<WalkerPlatformCarrier>();
            carrier.BindCarryVolume(carryVolume);

            // Looking at the hull and pressing E puts you on the deck. The gangway is still there and
            // still walkable; this is the way aboard that does not depend on being on the one side of
            // an eighteen-metre hull the plank happens to be on.
            BoxCollider deckCollider = null;
            foreach (BoxCollider box in root.GetComponentsInChildren<BoxCollider>(true))
                if (box.name == "COL_Deck") deckCollider = box;

            DeckBoarding boarding = root.AddComponent<DeckBoarding>();
            boarding.Bind(deckCollider, carryVolume, foil);

            // The sails are shipped set, so the hull has to be held or the craft leaves its berth
            // on the frame the scene loads. Released the moment somebody stands on the deck.
            DuneFoilMooring mooring = root.AddComponent<DuneFoilMooring>();
            mooring.Bind(locomotion, carrier);

            // Where the wind is, how fast the craft is going, and whether the sails are drawing.
            // Drawn only for somebody standing in the carry volume; see DuneFoilHUD for why a
            // wind indicator is not optional on a vessel steered by an invisible force.
            DuneFoilHUD hud = root.AddComponent<DuneFoilHUD>();
            hud.Bind(locomotion, rig, carryVolume);

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

        private static List<SailSurface> BuildSails(Transform craft, Transform foilRoot, SailRig rig,
                                                    float foilZ, out SailSurface main,
                                                    out SailSurface jib)
        {
            List<SailSurface> surfaces = new List<SailSurface>();
            main = null;
            jib = null;

            foreach (SailSpec spec in Sails)
            {
                Transform cantPivot = FindDeep(foilRoot, spec.Group + "_Rake");
                Transform spar = FindDeep(foilRoot, spec.Group + "_Yaw");
                Transform cloth = FindDeep(foilRoot, "Sail_" + spec.Group);
                Transform clew = FindDeep(foilRoot, "Anchor_" + spec.Group + "_Clew");
                Transform post = FindDeep(foilRoot, spec.Group + "_Post");

                if (cantPivot == null || spar == null || cloth == null)
                {
                    Debug.LogWarning($"[DuneFoilBuilder] Sail '{spec.Group}' is missing rig nodes " +
                                     "and was skipped.");
                    continue;
                }

                // The component goes on the cant pivot, so one sail is one object in the hierarchy.
                SailSurface sail = cantPivot.gameObject.AddComponent<SailSurface>();

                float area = MeasureSailArea(cloth);
                Bounds clothBounds = MeasureBounds(cloth);
                float lever = clothBounds.center.z - foilZ;

                sail.Bind(cantPivot, spar, cloth.gameObject, clew, craft, area, lever);
                sail.ConfigureSheet(spec.MinSheet, spec.MaxSheet);

                Vector3 sparAxis = MeasureSparAxis(spar, post);
                float centreHeight = Mathf.Max(0.5f, clothBounds.center.y - cantPivot.position.y);
                sail.ConfigurePost(sparAxis, centreHeight, area * CantRightingPerSquareMetre,
                                   MaxCantAngle);

                Debug.Log($"[DuneFoilBuilder] Sail '{spec.Group}': {area:F1} m², lever " +
                          $"{lever:F2} m, centre of effort {centreHeight:F2} m up the post, " +
                          $"spar axis {sparAxis}.");

                // Ship it with the sails SET, so the craft is ready to sail the moment you step
                // aboard rather than making you find the hoist station first.
                //
                // What used to stop this: with sails set, a craft dropped into the world simply
                // leaves. It catches the wind on the frame the scene loads and sails off — measured
                // at 293 m from the player spawn within seconds of pressing play, foiling, with the
                // gangway stowed and no way to reach it. That is now handled at the other end, by
                // DuneFoilMooring holding the hull still until somebody is actually on the deck, so
                // the rig can be left drawing without the craft running away from its berth.
                sail.SetHoisted(true);

                cantPivot.name = spec.Label + "_Rig";
                surfaces.Add(sail);

                if (spec.Group == "Main") main = sail;
                if (spec.Group == "Jib") jib = sail;
            }

            return surfaces;
        }

        /// <summary>
        /// Which of the spar pivot's own axes the post actually runs along.
        ///
        /// Worth measuring rather than assuming, and the shipped rig is the argument: the sail
        /// trim used to turn the spar pivot about its local Y, on the stated reasoning that
        /// "Blender +Z exports to Unity +Y". That holds for the imported ROOT, which carries the
        /// axis correction, and is false for every node under it — those keep Blender-native local
        /// axes. So the trim control was swinging the whole mast a hundred and twenty degrees off
        /// its own line instead of rotating the sail about it. Turning about the post's real
        /// direction moves the post by five degrees, which is the rounding on a curved yard.
        ///
        /// Snapped to the nearest local axis when it is close to one, because the rig script fitted
        /// that axis over the whole bowed spar and a two-point measurement here should not
        /// second-guess it. Falls back to the measured direction otherwise.
        /// </summary>
        private static Vector3 MeasureSparAxis(Transform spar, Transform post)
        {
            if (post == null) return Vector3.forward;

            Vector3 direction = MeasureBounds(post).center - spar.position;
            if (direction.sqrMagnitude < 1e-6f) return Vector3.forward;

            Vector3 local = spar.InverseTransformDirection(direction).normalized;

            Vector3[] candidates =
            {
                Vector3.right, Vector3.left, Vector3.up, Vector3.down,
                Vector3.forward, Vector3.back,
            };

            Vector3 best = local;
            float bestAngle = 15f;      // only snap when it is genuinely the same axis
            foreach (Vector3 candidate in candidates)
            {
                float angle = Vector3.Angle(local, candidate);
                if (angle < bestAngle) { bestAngle = angle; best = candidate; }
            }
            return best;
        }

        /// <summary>
        /// Put the wing panels on the main's sheet.
        ///
        /// They have no winch of their own and they never did, which meant two sails on the craft
        /// were making force at whatever trim they were serialised with and biasing the helm for
        /// reasons the player could neither see nor change. Slaving them is also what the model
        /// depicts: they flank the main at half a metre of lever, panels of one sail plan rather
        /// than sails in their own right.
        /// </summary>
        private static void BindWingsToMain(List<SailSurface> surfaces, SailSurface main,
                                            SailSurface jib)
        {
            if (main == null) return;

            List<SailSurface> slaves = new List<SailSurface>();
            foreach (SailSurface sail in surfaces)
                if (sail != null && sail != main && sail != jib) slaves.Add(sail);

            main.BindSheetSlaves(slaves);

            // Put them where the main is now, so the craft starts coherently trimmed rather than
            // waiting for the first touch of the winch to pull them into line.
            main.SetSheet(main.SheetOut);

            Debug.Log($"[DuneFoilBuilder] Main sheet also works {slaves.Count} wing panel(s).");
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
            // Hung off the strut NODE rather than off the collision root, because the strut is
            // steerable now: the wheel swings it about the vertical and the collider has to go
            // with it, or the craft turns while its collision stands still.
            //
            // Parented keeping the world transform and then squared up to the world, so the box
            // is axis-aligned with the measured bounds. Every scale on this model is 1, so the
            // box stays undistorted as the parent turns.
            GameObject strut = new GameObject("COL_Strut");
            Bounds sb = MeasureBounds(foilStrut);
            strut.transform.SetParent(foilStrut, true);
            strut.transform.position = sb.center;
            strut.transform.rotation = Quaternion.identity;
            BoxCollider strutBox = strut.AddComponent<BoxCollider>();
            strutBox.size = sb.size;
            strutBox.center = Vector3.zero;

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

        /// <summary>
        /// One colour per control, so four winches on one deck stop being four grey spheres.
        /// The grip mesh built on each handle wears this, and it is the only way to tell the
        /// halyard from the jib sheet from across the deck rather than by aiming at it.
        /// </summary>
        private static readonly Dictionary<string, Color> StationColours =
            new Dictionary<string, Color>
            {
                { "Station_Hoist",     new Color(0.95f, 0.78f, 0.25f) },   // amber: halyards
                { "Station_MainSheet", new Color(0.30f, 0.72f, 0.95f) },   // blue: the main
                { "Station_JibSheet",  new Color(0.35f, 0.85f, 0.45f) },   // green: the jib
                { "Station_MastRake",  new Color(0.85f, 0.40f, 0.85f) },   // violet: the mast cant
                { "Station_Helm",      new Color(0.95f, 0.35f, 0.30f) },   // red: the wheel
            };

        private static void BuildStations(Transform foilRoot, SailRig rig, SailSurface main,
                                          SailSurface jib, float deckLevel)
        {
            // Size each handle against its OWN nearest neighbour, not against the closest pair
            // on the whole deck.
            //
            // The old rule took one global radius from the tightest pair and gave it to all four.
            // That pair is Hoist and JibSheet, 1.50 m apart, so every handle became a 0.60 m
            // sphere — leaving 30 cm of clear air between those two at chest height. Both answer
            // left click; on the jib that hauls in, and on the hoist it used to strike the entire
            // rig instantly. A crosshair a few degrees off while trimming the jib and the sails
            // simply vanished mid-passage. The halyard is no longer destructive, but the aiming
            // problem was real on its own and this is the half of the fix that belongs here.
            // Measured where the handles END UP, not where the winches are.
            //
            // AddStation lifts every handle to one common deck height, so two winches that sit
            // 1.50 m apart on a curved deck — most of it a height difference — leave handles only
            // 1.33 m apart once they are levelled. Sizing off the nodes quietly overstates the
            // room available and hands back the crowding this is meant to remove.
            List<Vector3> spots = new List<Vector3>();
            foreach (string n in StationNodes)
            {
                Transform t = FindDeep(foilRoot, n);
                spots.Add(t != null
                    ? new Vector3(t.position.x, deckLevel + HandleHeight, t.position.z)
                    : Vector3.positiveInfinity);
            }

            float[] radii = new float[StationNodes.Length];
            for (int i = 0; i < StationNodes.Length; i++)
            {
                if (float.IsInfinity(spots[i].x)) { radii[i] = 0.45f; continue; }

                float nearest = float.MaxValue;
                for (int j = 0; j < StationNodes.Length; j++)
                {
                    if (i == j || float.IsInfinity(spots[j].x)) continue;
                    nearest = Mathf.Min(nearest, Vector3.Distance(spots[i], spots[j]));
                }

                // A third of the gap rather than 40%, so neighbouring spheres are separated by at
                // least as much air as they occupy. Two handles that nearly touch are two handles
                // the player will confuse.
                radii[i] = nearest == float.MaxValue
                    ? 0.55f
                    : Mathf.Clamp(nearest * 0.33f, 0.26f, 0.55f);
            }

            // Four handles, three controls plus the halyards: the main's sheet, the main's post
            // cant, the jib's sheet, and the hoist that depowers the whole rig. The wing panels
            // ride on the main's sheet, so nothing on the craft is unreachable.
            var functions = new[]
            {
                DuneFoilRiggingStation.StationFunction.Hoist,
                DuneFoilRiggingStation.StationFunction.Sheet,
                DuneFoilRiggingStation.StationFunction.Sheet,
                DuneFoilRiggingStation.StationFunction.MastCant,
            };
            var sails = new[] { null, main, jib, main };

            for (int i = 0; i < StationNodes.Length; i++)
            {
                AddStation(foilRoot, StationNodes[i], functions[i], rig, sails[i], deckLevel,
                           radii[i]);
                Debug.Log($"[DuneFoilBuilder] {StationNodes[i]}: handle radius {radii[i]:F2} m.");
            }
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
        /// <summary>Above the deck: roughly chest height. Every handle is levelled to this.</summary>
        private const float HandleHeight = 1.0f;

        private static void AddStation(Transform foilRoot, string nodeName,
                                       DuneFoilRiggingStation.StationFunction function,
                                       SailRig rig, SailSurface sail, float deckLevel,
                                       float handleRadius)
        {
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
            Vector3 world = new Vector3(node.position.x, deckLevel + HandleHeight, node.position.z);
            handle.transform.position = world;

            SphereCollider hit = handle.AddComponent<SphereCollider>();
            hit.radius = handleRadius;
            hit.isTrigger = false;

            // Interactor resolves IInteractable by GetComponent then GetComponentInParent, so the
            // collider on this child finds the station on the node above it.

            AddGrip(handle.transform, nodeName, handleRadius);
        }

        /// <summary>
        /// A visible, colour-coded grip on the handle.
        ///
        /// Until now the interaction volume was an invisible sphere floating a metre above a winch
        /// that is itself modelled at and below deck level. There was nothing to look at, nothing
        /// to distinguish one control from another, and no way to learn the rig except by pressing
        /// buttons and watching what happened — which on the hoist meant losing every sail. The
        /// grip is small, so it does not clutter a deck that is also a walking surface, but it is
        /// lit and coloured so it reads instantly from the far end of the hull.
        /// </summary>
        private static void AddGrip(Transform handle, string nodeName, float handleRadius)
        {
            if (!StationColours.TryGetValue(nodeName, out Color colour))
                colour = new Color(0.85f, 0.85f, 0.85f);

            // Scaled off the handle so a tight-packed pair gets small grips and a lonely control
            // gets a big one — the grip always looks like the thing you can actually hit.
            float shaft = Mathf.Clamp(handleRadius * 0.45f, 0.17f, 0.24f);

            GameObject grip = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            grip.name = "Grip";
            grip.transform.SetParent(handle, false);
            grip.transform.localPosition = Vector3.zero;

            // Upright in WORLD space, and scaled UNIFORMLY, both for the same reason.
            //
            // Everything under the imported model inherits the FBX importer's Blender Z-up to
            // Unity Y-up correction, so this node's local axes are not the world's. A capsule
            // stretched along its local Y therefore lay flat on the planks — the first attempt
            // produced a 0.43 m disc 0.14 m thick, which from a standing player reads as a coin
            // someone dropped rather than as a control. Uniform scale cannot be skewed by that
            // rotation at all, and a capsule is already 2:1, so this stands up on its own.
            grip.transform.rotation = Quaternion.identity;
            grip.transform.localScale = Vector3.one * shaft;

            // No collider: the sphere above is the interaction volume, and a second collider here
            // would be one more thing for the player's own capsule to catch on while walking the
            // deck. Interactor hits the sphere; this is purely something to look at.
            Object.DestroyImmediate(grip.GetComponent<Collider>());

            grip.GetComponent<MeshRenderer>().sharedMaterial = EnsureGripMaterial(nodeName, colour);
        }

        private const string GripMaterialFolder =
            "Assets/Game/Art/Models/Vehicles/DuneFoil/Materials";

        /// <summary>
        /// Load the grip material or create it. Reused rather than recreated, so rebuilding the
        /// prefab does not orphan a fresh material asset every time and leave the old ones behind
        /// referenced by nothing.
        /// </summary>
        private static Material EnsureGripMaterial(string nodeName, Color colour)
        {
            System.IO.Directory.CreateDirectory(GripMaterialFolder);
            string path = $"{GripMaterialFolder}/Mat_Grip_{nodeName}.mat";

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = material == null;
            if (isNew) material = new Material(Shader.Find("Universal Render Pipeline/Lit"));

            material.color = colour;
            material.SetFloat("_Smoothness", 0.35f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", colour * 0.55f);

            if (isNew) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);

            return material;
        }

        // ----------------------------------------------------------------- helm

        /// <summary>
        /// Makes the ship's wheel steerable: E takes the helm, A and D turn it.
        ///
        /// The craft was designed with no rudder — heading came only from trimming main against
        /// jib — which is an honest model of a sand yacht and close to unusable as a control. The
        /// wheel comes in from the shared component library through dune_foil_rig.py, on a
        /// Station_Helm node whose origin is the wheel's hub with the pedestal hanging below it.
        /// All this has to do is seat that node at standing height and wire the pieces up.
        /// </summary>
        /// <summary>How far abaft the wheel the helmsman stands, metres.</summary>
        private const float HelmStanceOffset = 0.85f;

        private static void BuildHelm(Transform foilRoot, DuneFoilLocomotion locomotion,
                                      float deckLevel)
        {
            Transform node = FindDeep(foilRoot, "Station_Helm");
            if (node == null)
            {
                Debug.LogWarning("[DuneFoilBuilder] No 'Station_Helm' node in the model, so the " +
                                 "craft has no wheel and can only be steered by sail balance. " +
                                 "Re-export the rig with dune_foil_rig.py.");
                return;
            }

            Transform wheel = FindDeep(node, "Helm_Wheel");

            // Seat the assembly on the deck. The node IS the hub and the pedestal is modelled
            // hanging below it, so one Y places the whole thing with its foot on the planks.
            const float hubHeight = 1.02f;      // matches the pedestal in ships_wheel.py
            node.position = new Vector3(node.position.x, deckLevel + hubHeight, node.position.z);

            // Where the helmsman stands: on the planks, a pace abaft the wheel, facing forward.
            // Their feet are held here for as long as they have the helm — see the pin in
            // DuneFoilHelm.LateUpdate for why a heeling deck makes that necessary.
            GameObject stance = new GameObject("HelmStance");
            stance.transform.SetParent(node, true);
            stance.transform.position = new Vector3(node.position.x,
                                                    deckLevel,
                                                    node.position.z - HelmStanceOffset);
            stance.transform.rotation = Quaternion.identity;

            DuneFoilHelm helm = node.gameObject.AddComponent<DuneFoilHelm>();
            helm.Bind(locomotion, wheel, stance.transform);

            // The wheel itself is what you look at and press E on, so the interaction volume goes
            // on the wheel rather than on the pedestal — aiming at a post to steer would be the
            // same "where do I click" problem the rope stations had.
            GameObject handle = new GameObject("Handle");
            handle.transform.SetParent(node, false);
            handle.transform.localPosition = Vector3.zero;

            SphereCollider hit = handle.AddComponent<SphereCollider>();
            hit.radius = 0.46f;                 // a little inside the 0.62 m rim
            hit.isTrigger = false;

            // No coloured grip here, unlike the rope stations. A ship's wheel already announces
            // what it is and where to stand; a marker pinned to the hub would either be swallowed
            // by the boss or sit still while the wheel turned past it.

            Debug.Log($"[DuneFoilBuilder] Helm seated at y {node.position.y:F2} " +
                      $"(deck {deckLevel:F2} + {hubHeight:F2}); wheel " +
                      $"{(wheel != null ? "bound" : "MISSING")}, spin axis " +
                      $"{DuneFoilHelm.MeasureSpinAxis(wheel)}.");
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
