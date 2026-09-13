// Builds the nomad settlement: forty building prefabs sorted by size, eighteen
// shade-sail prefabs, and the sails hung off the buildings' walls.
//
// The models come out of two contact-sheet .blends via
// models/buildings/nomad_settlement_export.py and
// components/nomad_settlement/tents_export.py, one FBX per building and per
// sail. Everything below that -- import settings, colliders, LOD groups, static
// flags, which size folder a building lands in and where its sails hang -- is
// generated here. Forty buildings of 45-468 parts each is not a set anyone
// places colliders on twice by hand.
//
// Re-running is safe and is the intended workflow: re-export, run this, and
// every prefab is rebuilt in place against the new geometry. Prefabs are
// overwritten at the SAME path so their GUIDs survive; a building that changes
// size class is MOVED between folders rather than recreated, for the same
// reason.
//
// Re-run from: Tools > Environment > Build Nomad Settlement Prefabs
//
// ---------------------------------------------------------------------------
// SCALE -- read this before touching any measurement here.
//
// The kit these buildings are made of is authored well under human scale: its
// whole reference tower is 4.9 m tall on a 1.219 m storey, and the generator's
// own door cap is 1.05 m. Measured over the forty exported buildings, the doors
// come out 0.53-0.96 m tall. The astronaut is 2.00 m (the player capsule in
// Movement.cs; the skinned bounds read 2.86 m because the edit-mode animator
// pose has the arms up, and that pose is arbitrary -- do not measure off it).
// So a building as exported has a door an astronaut cannot walk through, and
// every shade sail, which IS authored at astronaut scale, is wider than the
// building it would hang on.
//
// Each building is therefore scaled so that ITS OWN SMALLEST DOOR clears the
// astronaut: factor = (2.00 + headroom) / smallest door height. Per building
// rather than one factor for the settlement, because the generator's doors vary
// two to one: a single factor set by the smallest door would give the others
// four-metre gateways, and one set by the largest would leave the small ones
// too short to enter. Scaling per building instead makes the door the constant
// -- the one part of a facade a player reads scale from -- and lets the kit's
// ring and box sizes vary between buildings, which reads as buildings built at
// different times rather than as an error.
//
// The sails are then scaled to the building they hang on: a sail's wall fixings
// sit THREE STOREYS up that building, measured off its own drums and blocks, and
// never below twice the size the sail was authored at. A sail is shade over a
// yard, not an awning over a window -- left at the size it came in, a 3.4 m sail
// on a building whose storeys are 5 m reads as a parasol nailed to the wall. What
// stops them growing for ever is the wall itself: a sail's fixing span is held to
// a share of the wall radius at the height it hangs at, because its wall edge is
// a straight line and a chord cannot be wider than the drum it crosses.
//
// Seating is done against the COLLIDERS, by firing a ray per fixing at the wall:
// the sail's wall plane goes on the surface the rays find, and the sail is tilted
// to the slope those rays describe, so its edge follows the wall's 6.4 deg batter
// instead of crossing it. Where the fixings still disagree -- a storey seam, an
// annex -- the sail seats on the SHALLOWEST of them, so hardware ends up in the
// masonry rather than a sail hanging off the building with daylight behind it.
//
// The same trap as BuildingPrefabBuilder applies to every measurement: the FBX
// root imports at lossyScale 1, but every mesh CHILD sits at localScale 100
// with the exporter's -90 deg X baked in. Everything here measures in WORLD
// space via Renderer.bounds and Transform.TransformPoint, never mesh.bounds.
// The instance is placed at the origin with identity rotation while it is
// measured, so world space and root-local space are the same thing.
//
// GEOMETRY THE SAIL PLACEMENT RELIES ON, measured off tents.blend:
//
//   * A Wall* sail is authored against a wall in Blender's y = 0 plane and
//     hangs away from it towards -y. Through the exporter's axis conversion
//     ((x, y, z) -> (-x, z, -y)) that arrives as: wall plane at LOCAL Z = 0,
//     sail reaching out along +Z, up is +Y. So seating one against a wall is a
//     yaw and a distance, nothing more.
//   * Its wall hardware is the `*WallFix*` parts. Their combined bounds give
//     both the span the wall has to carry and the height it is carried at --
//     measured per sail rather than written down here, because the ten sails
//     differ by more than a metre in both.
//
// Both are asserted at build time (SeatCheck), because a re-authored sail that
// silently moved its fixings would otherwise hang forty buildings' worth of
// cloth inside the masonry.
//
// WHAT IS NOT DONE, deliberately:
//
//   * No door avoidance. A sail's cloth starts at 2.4 m and the tallest door in
//     the set is 1.05 m, so a sail cannot cover one -- and WallPorch exists to
//     stand over a doorway. Its masts can land beside a door; that reads as a
//     porch, which is what it is.
//   * Sails get NO colliders. They are cloth over head height; a player walks
//     under them and their guy ropes are 60 mm of hemp. Colliding them would
//     add ~1500 colliders across the settlement to catch on.
// ---------------------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Fit = SpaceGame.EditorTools.StaticPropBuilder.Fit;

namespace SpaceGame.EditorTools
{
    public static class NomadSettlementBuilder
    {
        private const string ModelDir =
            "Assets/Game/Art/Models/Environment/Structures/NomadSettlement";
        private const string SailModelDir = ModelDir + "/Tents";

        private const string PrefabDir =
            "Assets/Game/Prefabs/Environment/Structures/NomadSettlement";
        private const string SailPrefabDir = PrefabDir + "/Tents";

        // The double-sided cloth copies go in the settlement's own material
        // folder, not the vehicle folder DoubleSidedMaterials defaults to.
        private const string SailMaterialDir = "Assets/Game/Art/Materials/Settlement";

        private const int BuildingCount = 40;

        private static readonly string[] SailNames =
        {
            // Freestanding -- shipped as prefabs so a level designer can place
            // them in a yard, but never auto-hung on a building.
            "QuadSmall", "QuadLarge", "Tri", "TriTall", "Penta", "HexLow", "Ribbon", "Kite",
            // Wall-mounted.
            "WallQuad", "WallTri", "WallStrip", "WallLean", "WallPorch",
            "WallCorner", "WallFan", "WallCanopyLong", "WallBillow", "WallSpur",
        };

        // WallCorner is the one wall sail that needs TWO walls at right angles
        // (TENTS.md): its fixings sit on both, so the x span this script
        // measures describes neither of them. It ships as a prefab and is left
        // out of the automatic hanging.
        private const string TwoWallSail = "WallCorner";

        // -------------------------------------------------------------------
        // Astronaut scale -- see the header.
        // -------------------------------------------------------------------

        /// <summary>The player capsule's height in Movement.cs, in metres.</summary>
        private const float AstronautHeight = 2.0f;

        /// <summary>Clear air above the astronaut's head in the smallest doorway.</summary>
        private const float DoorHeadroom = 0.25f;

        /// <summary>
        /// A sane band for the per-building factor. Outside it the export changed
        /// shape and the scale is being derived from something that is not a door.
        /// </summary>
        private const float MinBuildingScale = 1.5f;
        private const float MaxBuildingScale = 6f;

        private const string DoorPart = "Door_Door_Frame";

        /// <summary>
        /// How far up a building a sail hangs, in storeys. Two: one storey put the
        /// cloth barely over head height on a building five storeys tall and read as
        /// an awning over a window rather than shade over a yard.
        /// </summary>
        private const float StoreysUnderSail = 3f;

        /// <summary>
        /// …but never so wide that it wraps the wall it hangs on. A sail's fixing
        /// span is held to this share of the wall radius AT the height it hangs at --
        /// not the building's overall radius, which on a tapered drum two storeys up
        /// is a wall that is no longer there. Sized off the footprint instead, the
        /// sails came out too wide to seat anywhere and seventeen buildings shipped
        /// bare.
        /// </summary>
        private const float SailWidthShare = 0.95f;

        /// <summary>
        /// Clamps, so a building with an unreadable storey cannot produce a doll's
        /// tent or one that swallows the settlement. The floor is above 1: a sail
        /// that will not fit a wall at this size is passed over for a smaller sail,
        /// never shrunk below the size it was authored at.
        /// </summary>
        private const float MinSailScale = 2f;
        private const float MaxSailScale = 9f;

        // -------------------------------------------------------------------
        // Size classes
        //
        // Measured AFTER the per-building astronaut scaling above, so these are
        // the sizes a player actually walks up to. Neither axis alone sorts them
        // the way the eye does -- B08 is narrow and the tallest thing in the
        // settlement, B26 is wide and squat -- so a building is large if it is
        // big EITHER way, and small only if it is small BOTH ways.
        //
        // Read as multiples of the astronaut: small is under five and a half of
        // him across and eight and a half tall, large is over ten across or
        // fourteen tall. Measured on the scaled set, that splits 7 / 21 / 12.
        // -------------------------------------------------------------------
        private const float SmallFootprint = 11f;
        private const float SmallHeight = 17f;
        private const float LargeFootprint = 20f;
        private const float LargeHeight = 28f;

        private static readonly string[] SizeFolders = { "Small", "Medium", "Large" };

        // How many sails a building of each class asks for, before the wall runs
        // out of room. Twice the 1-5 the first pass hung: a hut gets two to four,
        // a landmark eight to ten. What a building actually carries is whatever
        // fits round it without two sails sharing a heading, and the build report
        // prints that rather than this.
        private static readonly Vector2Int[] SailsPerClass =
        {
            new Vector2Int(2, 4),    // Small
            new Vector2Int(6, 6),    // Medium
            new Vector2Int(8, 10),   // Large
        };

        // -------------------------------------------------------------------
        // Sail seating
        // -------------------------------------------------------------------

        // The wall is profiled at exactly each sail's own fixing height, by
        // slicing the structural triangles with that horizontal plane. A drum
        // tapers 6.4 deg per storey, so the radius three metres up is not the
        // radius at the door, and seating a sail against the wrong one either
        // buries it or hangs it in the air -- the first version sampled a band
        // and picked up the wide skirt under a tapered drum, which left two
        // buildings' fixings a third of a metre off the wall.
        //
        // Slicing rather than binning vertices is what makes the height exact: a
        // generated drum carries vertices only at its two ends, so a band narrow
        // enough to mean "at the fixings" contains no vertices at all.
        private const int YawBuckets = 180;   // 2 deg

        // The wall has to beat the sail's half span by this much before the
        // sail is hung on it, so the fixings bite into masonry rather than
        // landing on the silhouette's edge.
        private const float WallMargin = 1.05f;

        // How far past the measured wall surface a seated sail is pushed, so its
        // hardware is buried in the masonry rather than resting exactly on the
        // skin of it. Covers the 2 deg the wall profile is quantised to.
        private const float SeatBite = 0.02f;

        // The seating pass that follows fires a ray per fixing at the wall and puts
        // the sail's wall plane ON the surface it finds, so a sail meets the
        // building at its EDGE and not somewhere inside it. Three passes because
        // moving the sail changes the heading each fixing looks along; they
        // converge in two.
        private const int SeatPasses = 8;

        // How far outside the building a seating ray starts, past its own radius.
        private const float RayStandoff = 5f;

        // The most the closing pass may pull a sail in, past what the rays asked
        // for. Beyond this the heading is simply wrong and pulling further would
        // bury the sail rather than seat it.
        private const float MaxPull = 2.5f;

        // The most a sail may lean to follow a wall, as metres out per metre up.
        // The kit's own batter is 0.11 (6.4 deg); past a quarter the fit has found
        // something that is not one wall.
        private const float MaxBatter = 0.25f;

        // Clear air between neighbouring sails, as an angle about the building.
        private const float SailGap = 12f * Mathf.Deg2Rad;

        // How far round the building to step when a heading had no wall to hang
        // anything on, and how many headings to try before giving up.
        private const float SearchStep = 25f * Mathf.Deg2Rad;
        private const int SearchAttempts = 128;

        // A sail's fixings must sit in its own z = 0 plane (see the header).
        // Anything past this is a re-authored sail, not a placement bug.
        private const float SeatTolerance = 0.3f;

        private const float LodCullRatio = 0.02f;

        // -------------------------------------------------------------------
        // Collision
        //
        // Ordered, first match wins, matched against the part name with its unit
        // prefix stripped (`B07_A2_Drum3` -> `Drum3`). The generator's naming is
        // mechanical, which is what makes one table cover all forty buildings
        // where BuildingPrefabBuilder needs a hand-written table per model.
        //
        // Every structural part is a convex solid by construction -- a drum is a
        // 32-sided truncated cone, a block is a bevelled box, a roof is a slab --
        // so a convex hull is not an approximation here, it is the shape. That
        // is what makes an accurate collider cheap: 3-16 hulls per building
        // instead of a mesh collider over 45-468 parts.
        //
        // Ring/Arc must be tested BEFORE the roof-slab rule: roof slabs are
        // `R00_*` and bands are `Ring00_*`, and both start with R.
        // -------------------------------------------------------------------
        private struct Rule
        {
            public string Match;
            public Fit Fit;
            public string Note;
        }

        private static readonly Rule[] Rules =
        {
            new Rule { Match = "Ring", Fit = Fit.None, Note = "storey band, 0.05 m proud" },
            new Rule { Match = "Arc", Fit = Fit.None, Note = "half band" },

            new Rule { Match = "Drum", Fit = Fit.Convex, Note = "coned storey" },
            new Rule { Match = "Blk", Fit = Fit.Convex, Note = "rect storey block" },
            new Rule { Match = "Foundation", Fit = Fit.Convex, Note = "foundation slab" },
            new Rule { Match = "Crown_TowerBody_RoofCap", Fit = Fit.Convex, Note = "roof cap" },
            new Rule { Match = "Crown_RoofDeck_Ringwall", Fit = Fit.Convex, Note = "roof parapet" },

            // Roof furniture and the vent cowls: on top of a cap nothing can
            // stand on, and small enough that a hull each is pure cost.
            new Rule { Match = "Crown", Fit = Fit.None, Note = "roof furniture" },

            new Rule { Match = "R", Fit = Fit.Convex, Note = "roof slab" },
        };

        // `B07_A2_Drum3` and `B07_T_Crown_...` -- main body, annex or the tower
        // of a hybrid. Three unit prefixes, one regex.
        private static readonly Regex UnitPrefix = new Regex(@"^B\d+_(?:M|A\d+|T)_");

        /// <summary>One sail prefab, and what it needs from a wall.</summary>
        private sealed class Sail
        {
            public string Name;
            public GameObject Prefab;
            public bool HangsOnOneWall;
            public float HalfWidth;   // half the span of the wall fixings
            public float FixTop;      // the highest point of the wall hardware
            public float Reach;       // how far the sail stands off the wall

            /// <summary>
            /// Every wall fixing as (sideways offset, height) in the sail's own frame.
            /// Seating is checked against these one by one rather than against the
            /// group: the two fixings of a sail sit up to 0.6 m apart in height, and
            /// the wall they hang on is a cone, so they do not meet the same radius.
            /// </summary>
            public Vector2[] Fixings;
        }

        [MenuItem("Tools/Environment/Build Nomad Settlement Prefabs")]
        public static void BuildAll()
        {
            var report = new StringBuilder();
            report.AppendLine("NomadSettlementBuilder");

            // The tuning this run used, printed so a report can be read back
            // against the numbers that produced it rather than against whatever
            // the file says now.
            report.AppendLine($"  astronaut {AstronautHeight:F2} m + {DoorHeadroom:F2} headroom · " +
                              $"sails {StoreysUnderSail:F1} storeys up, " +
                              $"{MinSailScale:F2}-{MaxSailScale:F2}x, " +
                              $"width share {SailWidthShare:F2}");

            StaticPropBuilder.EnsureFolder(SailPrefabDir);
            foreach (string folder in SizeFolders)
                StaticPropBuilder.EnsureFolder(PrefabDir + "/" + folder);

            List<Sail> sails = BuildSails(report);
            List<Sail> hangable = sails
                .Where(s => s.HangsOnOneWall)
                .OrderByDescending(s => s.HalfWidth)
                .ToList();

            if (hangable.Count == 0)
                Debug.LogError("No wall sails built — every building will come out bare.");

            for (int n = 1; n <= BuildingCount; n++)
                BuildBuilding(n, hangable, report);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Re-read every prefab off disk and measure it. A build log says what the
        /// builder believed; this says what shipped, which is the only version worth
        /// trusting -- see ShipPartItemBuilder for the same pattern.
        /// </summary>
        [MenuItem("Tools/Environment/Verify Nomad Settlement Prefabs")]
        public static void VerifyAll()
        {
            var report = new StringBuilder();
            report.AppendLine("NomadSettlementBuilder — verify off disk");

            int cloth = 0, clothCulled = 0, sailColliders = 0, sailPrefabs = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { SailPrefabDir }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(
                    AssetDatabase.GUIDToAssetPath(guid));
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                sailPrefabs++;
                sailColliders += instance.GetComponentsInChildren<Collider>(true).Length;

                foreach (Renderer r in instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name.IndexOf("Canopy", System.StringComparison.Ordinal) < 0) continue;
                    cloth++;
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null || !m.HasProperty("_Cull") ||
                            !Mathf.Approximately(m.GetFloat("_Cull"), 0f))
                            clothCulled++;
                    }
                }
                Object.DestroyImmediate(instance);
            }

            report.AppendLine($"  sails: {sailPrefabs} prefabs, {sailColliders} colliders, " +
                              $"{cloth} cloth parts, {clothCulled} still back-face culled");

            int buildings = 0, hulls = 0, concave = 0, hung = 0, bare = 0;
            int anchors = 0, floating = 0, buried = 0, mirroredCulled = 0;
            float minDoor = float.MaxValue, minScale = float.MaxValue, maxScale = 0f;
            float minFix = float.MaxValue, maxFix = 0f, minCloth = float.MaxValue, maxCloth = 0f;
            float worstGap = 0f;

            foreach (string folder in SizeFolders)
            {
                string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir + "/" + folder });
                report.AppendLine($"  {folder}: {guids.Length} buildings");

                foreach (string guid in guids)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(
                        AssetDatabase.GUIDToAssetPath(guid));
                    var root = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                    root.transform.position = Vector3.zero;
                    root.transform.rotation = Quaternion.identity;
                    Physics.SyncTransforms();
                    buildings++;

                    var sails = new List<Transform>();
                    foreach (Transform t in root.transform)
                        if (t.name.StartsWith("NomadSail_", System.StringComparison.Ordinal))
                            sails.Add(t);
                    hung += sails.Count;
                    if (sails.Count == 0) bare++;

                    Collider[] cols = root.GetComponentsInChildren<Collider>(true)
                        .Where(c => !sails.Any(s => c.transform.IsChildOf(s))).ToArray();
                    hulls += cols.Length;
                    concave += cols.OfType<MeshCollider>().Count(m => !m.convex);

                    foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (sails.Any(s => r.transform.IsChildOf(s))) continue;
                        if (r.name.IndexOf(DoorPart, System.StringComparison.Ordinal) >= 0)
                            minDoor = Mathf.Min(minDoor, r.bounds.size.y);
                        if (r.transform.localToWorldMatrix.determinant >= 0f) continue;
                        foreach (Material m in r.sharedMaterials)
                        {
                            if (m == null || !m.HasProperty("_Cull") ||
                                !Mathf.Approximately(m.GetFloat("_Cull"), 0f))
                                mirroredCulled++;
                        }
                    }

                    foreach (Transform sail in sails)
                    {
                        minScale = Mathf.Min(minScale, sail.localScale.x);
                        maxScale = Mathf.Max(maxScale, sail.localScale.x);

                        Renderer[] fixings = sail.GetComponentsInChildren<Renderer>(true)
                            .Where(r => r.name.IndexOf("WallFix", System.StringComparison.Ordinal) >= 0)
                            .ToArray();
                        if (fixings.Length == 0) continue;

                        Bounds seat = fixings[0].bounds;
                        for (int i = 1; i < fixings.Length; i++) seat.Encapsulate(fixings[i].bounds);
                        minFix = Mathf.Min(minFix, seat.max.y);
                        maxFix = Mathf.Max(maxFix, seat.max.y);

                        Renderer[] canopies = sail.GetComponentsInChildren<Renderer>(true)
                            .Where(r => r.name.IndexOf("Canopy", System.StringComparison.Ordinal) >= 0)
                            .ToArray();
                        if (canopies.Length > 0)
                        {
                            Bounds cb = canopies[0].bounds;
                            for (int i = 1; i < canopies.Length; i++) cb.Encapsulate(canopies[i].bounds);
                            float span = Mathf.Max(cb.size.x, cb.size.z);
                            minCloth = Mathf.Min(minCloth, span);
                            maxCloth = Mathf.Max(maxCloth, span);
                        }

                        // A fixing is seated when a point just outside its wall plane
                        // is outside the building and one just inside it is inside.
                        Vector3 outward = sail.forward;
                        foreach (Renderer r in fixings)
                        {
                            Vector3 local = sail.InverseTransformPoint(r.bounds.center);
                            Vector3 point = sail.TransformPoint(new Vector3(local.x, local.y, 0f));
                            anchors++;

                            Vector3 outside = point + (outward * ProbeStep);
                            Vector3 inside = point - (outward * ProbeStep);
                            bool clearOutside = cols.All(c =>
                                Vector3.Distance(c.ClosestPoint(outside), outside) > 0.001f);
                            bool solidInside = cols.Any(c =>
                                Vector3.Distance(c.ClosestPoint(inside), inside) <= 0.001f);

                            if (!solidInside)
                            {
                                floating++;
                                float gap = float.MaxValue;
                                foreach (Collider c in cols)
                                    gap = Mathf.Min(gap, Vector3.Distance(c.ClosestPoint(point), point));
                                worstGap = Mathf.Max(worstGap, gap);
                            }
                            else if (!clearOutside)
                            {
                                buried++;
                            }
                        }
                    }

                    Object.DestroyImmediate(root);
                }
            }

            report.AppendLine($"  buildings {buildings}, bare {bare}, sails hung {hung}");
            report.AppendLine($"  colliders {hulls} hulls, concave {concave}; " +
                              $"shortest door {minDoor:F2} m against {AstronautHeight:F2} m");
            report.AppendLine($"  sail scale {minScale:F2}-{maxScale:F2}x, fixings " +
                              $"{minFix:F1}-{maxFix:F1} m up, cloth {minCloth:F1}-{maxCloth:F1} m across");
            report.AppendLine($"  fixings {anchors}: on the wall {anchors - floating - buried}, " +
                              $"sunk in {buried}, off it {floating} (worst {worstGap:F2} m)");
            report.AppendLine($"  mirrored parts still back-face culled: {mirroredCulled}");

            Debug.Log(report.ToString());
        }

        /// <summary>How far either side of a wall plane the verify probes.</summary>
        private const float ProbeStep = 0.15f;

        // -------------------------------------------------------------------
        // Sails
        // -------------------------------------------------------------------

        private static List<Sail> BuildSails(StringBuilder report)
        {
            var built = new List<Sail>();

            foreach (string name in SailNames)
            {
                string fbx = $"{SailModelDir}/nomad_sail_{Snake(name)}.fbx";
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                if (source == null)
                {
                    Debug.LogError($"No FBX at {fbx}. Run tents_export.py first.");
                    report.AppendLine($"  sail {name}: SKIPPED, no FBX");
                    continue;
                }

                StaticPropBuilder.ConfigureImporter(fbx);
                source = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);

                GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source);
                root.name = "NomadSail_" + name;
                root.transform.position = Vector3.zero;
                root.transform.rotation = Quaternion.identity;

                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    Debug.LogError($"{fbx} imported with no renderers — the export dropped " +
                                   "every mesh. Check tents_export.py's object filter.");
                    Object.DestroyImmediate(root);
                    continue;
                }

                // The canopies are single-sided membranes: tents.blend ships
                // them as one quad grid, not the solidified shell TENTS.md
                // describes. Back-face culled, a sail is invisible from
                // underneath -- which is the side a player stands on. Only the
                // cloth is swapped; the poles and anchors are real solids.
                int cloth = 0;
                foreach (Renderer r in renderers)
                {
                    if (r.name.IndexOf("Canopy", System.StringComparison.Ordinal) < 0) continue;
                    DoubleSidedMaterials.Apply(r.transform, SailMaterialDir);
                    cloth++;
                }

                var sail = new Sail
                {
                    Name = name,
                    HangsOnOneWall = name.StartsWith("Wall", System.StringComparison.Ordinal)
                                     && name != TwoWallSail,
                };

                if (sail.HangsOnOneWall)
                    MeasureSeat(sail, renderers);

                StaticPropBuilder.BuildLodGroup(root, renderers, LodCullRatio);
                StaticPropBuilder.MarkStatic(root);

                string path = $"{SailPrefabDir}/{root.name}.prefab";
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);

                sail.Prefab = saved;
                built.Add(sail);

                report.AppendLine($"  sail {name}: {renderers.Length} parts, {cloth} cloth " +
                                  (sail.HangsOnOneWall
                                      ? $"— {sail.HalfWidth * 2f:F2} m wide at {sail.FixTop:F2} m, " +
                                        $"reaches {sail.Reach:F2} m"
                                      : "— freestanding"));
            }

            return built;
        }

        /// <summary>
        /// Measure the wall hardware, and refuse to trust a sail whose fixings
        /// are not in its own z = 0 plane -- see the header.
        /// </summary>
        private static void MeasureSeat(Sail sail, Renderer[] renderers)
        {
            var fixings = renderers
                .Where(r => r.name.IndexOf("WallFix", System.StringComparison.Ordinal) >= 0)
                .ToArray();

            if (fixings.Length == 0)
            {
                sail.HangsOnOneWall = false;
                Debug.LogError($"{sail.Name} has no *WallFix* parts — it cannot be seated " +
                               "against a wall. Left out of the hanging pool.");
                return;
            }

            Bounds seat = fixings[0].bounds;
            for (int i = 1; i < fixings.Length; i++) seat.Encapsulate(fixings[i].bounds);

            Bounds whole = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) whole.Encapsulate(renderers[i].bounds);

            sail.HalfWidth = Mathf.Abs(seat.center.x) + seat.extents.x;
            sail.FixTop = seat.max.y;
            sail.Reach = whole.max.z;
            // Highest first: the topmost fixing meets the narrowest wall on a
            // coned drum, so it is the honest first guess for the seat.
            sail.Fixings = fixings
                .Select(f => new Vector2(f.bounds.center.x, f.bounds.center.y))
                .OrderByDescending(f => f.y)
                .ToArray();

            if (Mathf.Abs(seat.center.z) > SeatTolerance)
            {
                sail.HangsOnOneWall = false;
                Debug.LogError($"{sail.Name}'s fixings sit at z {seat.center.z:F2}, not on the " +
                               "wall plane at z 0. The sail was re-authored; re-read its " +
                               "orientation before hanging it. Left out of the hanging pool.");
                return;
            }

            // The sail must reach OUT of the wall, not back through it.
            if (whole.max.z <= 0f)
            {
                sail.HangsOnOneWall = false;
                Debug.LogError($"{sail.Name} lies entirely behind its wall plane " +
                               $"(z {whole.min.z:F2}..{whole.max.z:F2}). Its export turned it " +
                               "round. Left out of the hanging pool.");
            }
        }

        // -------------------------------------------------------------------
        // Buildings
        // -------------------------------------------------------------------

        private static void BuildBuilding(int n, List<Sail> sails, StringBuilder report)
        {
            string fbx = $"{ModelDir}/nomad_building_{n:00}.fbx";
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (source == null)
            {
                Debug.LogError($"No FBX at {fbx}. Run nomad_settlement_export.py first.");
                report.AppendLine($"  building {n:00}: SKIPPED, no FBX");
                return;
            }

            StaticPropBuilder.ConfigureImporter(fbx);
            source = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);

            string name = $"NomadBuilding_{n:00}";
            var root = new GameObject(name);

            // The model is a child so it can carry the astronaut scale on its
            // own: the sails hang off the ROOT at 1.0, because they are already
            // authored at that scale.
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);

            // Guard the assumption every measurement below rests on.
            Vector3 ls = model.transform.lossyScale;
            if (Mathf.Abs(ls.x - 1f) > 0.001f || Mathf.Abs(ls.y - 1f) > 0.001f ||
                Mathf.Abs(ls.z - 1f) > 0.001f)
            {
                Debug.LogWarning($"{name} imported at lossyScale {ls:F4}, not 1. The door " +
                                 "measurement below assumes metres at scale 1.");
            }

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Debug.LogError($"{fbx} imported with no renderers — the export dropped every " +
                               "mesh. Check nomad_settlement_export.py's object filter.");
                Object.DestroyImmediate(root);
                report.AppendLine($"  building {n:00}: SKIPPED, no renderers");
                return;
            }

            float door = SmallestDoor(renderers, name);
            float scale = AstronautScale(door, name);
            model.transform.localScale = Vector3.one * scale;

            var structural = new List<Renderer>();
            int convex = 0, skipped = 0;

            foreach (Renderer r in renderers)
            {
                string part = UnitPrefix.Replace(r.name, string.Empty);
                if (Resolve(part) != Fit.Convex)
                {
                    skipped++;
                    continue;
                }

                // A hull needs a readable mesh. The importer is configured for
                // that above, so falling back to a box means something stripped
                // Read/Write -- take the box rather than ship a hole.
                if (StaticPropBuilder.AddConvex(r)) convex++;
                else StaticPropBuilder.AddBox(r, false);
                structural.Add(r);
            }

            int mirrored = RenderMirroredPartsDoubleSided(renderers);

            if (structural.Count == 0)
            {
                Debug.LogError($"{name}: no structural part matched a collision rule. The " +
                               "generator's naming changed — nothing would collide and no " +
                               "sail could be seated.");
            }

            Bounds whole = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) whole.Encapsulate(renderers[i].bounds);

            float footprint = Mathf.Max(whole.size.x, whole.size.z);
            float height = whole.max.y;
            int sizeClass = Classify(footprint, height);

            StaticPropBuilder.BuildLodGroup(root, renderers, LodCullRatio);
            StaticPropBuilder.MarkStatic(root);

            // Sails last: they are nested prefab instances, and marking the
            // building static first keeps the flags off them as overrides.
            float storey = Storey(structural);
            int hung = HangSails(root, structural, sails, sizeClass, n, storey);

            string path = $"{PrefabDir}/{SizeFolders[sizeClass]}/{name}.prefab";
            KeepGuid(name, path);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            report.AppendLine($"  {name}: {SizeFolders[sizeClass]} " +
                              $"({footprint:F1} x {height:F1} m at {scale:F2}x, door " +
                              $"{door * scale:F2} m, storey {storey:F2} m), {convex} hulls " +
                              $"({skipped} parts left uncollided, {mirrored} double-sided), " +
                              $"{hung} sails");
        }

        /// <summary>
        /// The shortest doorway on the building, unscaled. The smallest one is what
        /// sets the scale: every door on the building has to clear the astronaut,
        /// not just the main one, and a fused building's annexes carry their own.
        /// </summary>
        private static float SmallestDoor(Renderer[] renderers, string name)
        {
            float door = float.MaxValue;
            foreach (Renderer r in renderers)
            {
                if (r.name.IndexOf(DoorPart, System.StringComparison.Ordinal) < 0) continue;
                door = Mathf.Min(door, r.bounds.size.y);
            }

            if (door < float.MaxValue) return door;

            Debug.LogError($"{name} has no {DoorPart} — the generator names a doorway " +
                           "differently now, and nothing is left to scale the building by. " +
                           "Shipped unscaled.");
            return 0f;
        }

        private static float AstronautScale(float door, string name)
        {
            if (door <= 0f) return 1f;

            float scale = (AstronautHeight + DoorHeadroom) / door;
            if (scale >= MinBuildingScale && scale <= MaxBuildingScale) return scale;

            Debug.LogWarning($"{name}'s smallest door is {door:F2} m, which wants a " +
                             $"{scale:F2}x building. That is outside {MinBuildingScale:F1}-" +
                             $"{MaxBuildingScale:F1}x, so the part measured is probably not a " +
                             "doorway any more. Clamped.");
            return Mathf.Clamp(scale, MinBuildingScale, MaxBuildingScale);
        }

        /// <summary>
        /// 36 kit parts carry a negative scale axis and every copy inherits it, so
        /// 2538 parts over the settlement arrive with inverted winding: Unity culls
        /// their front faces and you see through them. The export deliberately does
        /// not repair this -- doing it in Blender scattered three buildings' parts
        /// across the map, see nomad_settlement_export.py -- so it is fixed here, on
        /// the parts that actually need it, found by the sign of the determinant
        /// rather than by name.
        /// </summary>
        private static int RenderMirroredPartsDoubleSided(Renderer[] renderers)
        {
            int mirrored = 0;
            foreach (Renderer r in renderers)
            {
                if (r.transform.localToWorldMatrix.determinant >= 0f) continue;
                DoubleSidedMaterials.Apply(r.transform, SailMaterialDir);
                mirrored++;
            }
            return mirrored;
        }

        private static Fit Resolve(string part)
        {
            for (int i = 0; i < Rules.Length; i++)
            {
                if (part.StartsWith(Rules[i].Match, System.StringComparison.Ordinal))
                    return Rules[i].Fit;
            }
            return Fit.None;
        }

        private static int Classify(float footprint, float height)
        {
            if (footprint >= LargeFootprint || height >= LargeHeight) return 2;
            if (footprint < SmallFootprint && height < SmallHeight) return 0;
            return 1;
        }

        /// <summary>
        /// Move an existing prefab of this name into <paramref name="path"/>'s folder
        /// rather than leaving a stale copy behind in another size folder. Moving
        /// keeps the GUID, so anything already placing this building keeps working;
        /// deleting and recreating would null every reference to it.
        /// </summary>
        private static void KeepGuid(string name, string path)
        {
            foreach (string folder in SizeFolders)
            {
                string other = $"{PrefabDir}/{folder}/{name}.prefab";
                if (other == path) continue;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(other) == null) continue;

                string error = AssetDatabase.MoveAsset(other, path);
                if (!string.IsNullOrEmpty(error))
                    Debug.LogError($"{name} changed size class but could not move: {error}");
            }
        }

        // -------------------------------------------------------------------
        // Hanging the sails
        // -------------------------------------------------------------------

        private static int HangSails(GameObject root, List<Renderer> structural,
                                     List<Sail> sails, int sizeClass, int seed, float storey)
        {
            if (sails.Count == 0 || structural.Count == 0) return 0;

            Collider[] colliders = structural
                .Select(r => r.GetComponent<Collider>())
                .Where(c => c != null)
                .ToArray();

            Vector2Int band = SailsPerClass[sizeClass];
            int wanted = new Roll(seed).Range(band.x, band.y + 1);

            Bounds body = structural[0].bounds;
            for (int i = 1; i < structural.Count; i++) body.Encapsulate(structural[i].bounds);

            // A short building cannot carry a sail three storeys up, so the target
            // drops to whatever wall it does have.
            float hangHeight = Mathf.Min(StoreysUnderSail * storey, body.max.y - storey * 0.5f);
            float radius = Typical(Profile(structural, hangHeight));
            float outerRadius = Mathf.Max(body.extents.x, body.extents.z);

            // Full size first. A building too narrow to carry anything that big
            // gets a second pass at the authored size rather than shipping bare --
            // five of the forty did exactly that when the floor was the only rule.
            int hung = Hang(MinSailScale);
            if (hung == 0) hung = Hang(1f);
            return hung;

            int Hang(float floor)
            {
                var scales = new float[sails.Count];
                for (int i = 0; i < sails.Count; i++)
                    scales[i] = SailScale(hangHeight, radius, sails[i], floor);

                // One wall profile per height any scaled fixing sits at, built once
                // and shared by every heading the search tries.
                var profiles = new Dictionary<int, float[]>();
                for (int i = 0; i < sails.Count; i++)
                {
                    foreach (Vector2 fixing in sails[i].Fixings)
                    {
                        int key = Mathf.RoundToInt(fixing.y * scales[i] * 100f);
                        if (!profiles.ContainsKey(key))
                            profiles[key] = Profile(structural, fixing.y * scales[i]);
                    }
                }

                // Each building starts its sails at its own heading and prefers its
                // own sail, so forty buildings do not all wear the same one facing
                // the same way.
                var roll = new Roll(seed);
                roll.Range(band.x, band.y + 1);          // the count, already drawn
                float yaw = roll.Unit() * Mathf.PI * 2f;
                int firstChoice = roll.Range(0, sails.Count);

                var taken = new List<Vector2>();         // (heading, half span)
                int placedCount = 0;

                for (int attempt = 0; attempt < SearchAttempts && placedCount < wanted; attempt++)
                {
                    bool placed = false;

                    for (int i = 0; i < sails.Count && !placed; i++)
                    {
                        int pick = (firstChoice + placedCount + i) % sails.Count;
                        Sail sail = sails[pick];
                        float scale = scales[pick];

                        if (!Seat(sail, scale, profiles, yaw, taken,
                                  out float distance, out float halfSpan))
                            continue;

                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(sail.Prefab);
                        Vector3 direction = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                        instance.transform.SetParent(root.transform, false);
                        instance.transform.localScale = Vector3.one * scale;
                        instance.transform.localPosition = direction * distance;
                        instance.transform.localRotation =
                            Quaternion.LookRotation(direction, Vector3.up);

                        Edge(instance.transform, sail, scale, colliders, yaw, outerRadius,
                             ref distance);

                        taken.Add(new Vector2(yaw, halfSpan));
                        yaw += halfSpan * 2f + SailGap;
                        placedCount++;
                        placed = true;
                    }

                    if (!placed) yaw += SearchStep;
                }

                return placedCount;
            }
        }

        /// <summary>
        /// One storey of this building, in metres: the typical height of its drums
        /// and blocks, which are one storey each by construction. The median rather
        /// than the mean, because the ground block and the crown are not storeys and
        /// a tower carries eight of the real thing against one of each of those.
        /// </summary>
        private static float Storey(List<Renderer> structural)
        {
            var heights = new List<float>();

            foreach (Renderer r in structural)
            {
                string part = UnitPrefix.Replace(r.name, string.Empty);
                if (!part.StartsWith("Drum", System.StringComparison.Ordinal) &&
                    !part.StartsWith("Blk", System.StringComparison.Ordinal))
                    continue;
                heights.Add(r.bounds.size.y);
            }

            if (heights.Count == 0) return 0f;

            heights.Sort();
            return heights[heights.Count / 2];
        }

        /// <summary>
        /// How much to grow a sail for this building: enough to hang its fixings at
        /// <paramref name="target"/>, but never so much that its fixing span passes
        /// <see cref="SailWidthShare"/> of the wall radius up there. The narrow sails
        /// take the height; the long ones are held by the width, which is the right
        /// way round -- a 7 m awning on a 10 m building is already most of a facade.
        /// </summary>
        private static float SailScale(float target, float radius, Sail sail, float floor)
        {
            if (target <= 0f || sail.FixTop <= 0f) return 1f;

            float byHeight = target / sail.FixTop;
            float byWidth = sail.HalfWidth > 0f && radius > 0f
                ? SailWidthShare * radius / sail.HalfWidth
                : byHeight;

            return Mathf.Clamp(Mathf.Min(byHeight, byWidth), floor, MaxSailScale);
        }

        /// <summary>
        /// The wall radius a sail can count on at one height: the median of the
        /// headings that have a wall at all. The median rather than the largest,
        /// because one annex sticking out is not a wall the other three quarters of
        /// the building can hang anything on.
        /// </summary>
        private static float Typical(float[] profile)
        {
            var walls = profile.Where(r => r > 0f).OrderBy(r => r).ToList();
            return walls.Count == 0 ? 0f : walls[walls.Count / 2];
        }

        /// <summary>
        /// Push a seated sail the last few centimetres into the masonry, against the
        /// building's own colliders rather than against the profile that chose the
        /// heading. The profile is a 2 deg silhouette of the source triangles; the
        /// colliders are cooked convex hulls of the same parts, and the two disagree
        /// by up to half a metre on a drum whose two fixings sit a metre apart in
        /// height. This closes that, and only ever moves a sail INWARD -- it cannot
        /// lift one off a wall it was correctly seated against.
        /// </summary>
        private static void Edge(Transform sail, Sail spec, float scale, Collider[] colliders,
                                 float yaw, float radius, ref float distance)
        {
            Vector3 direction = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));

            for (int pass = 0; pass < SeatPasses; pass++)
            {
                // Physics reads the poses it was last given, not the transforms as
                // they are now: everything here was created and moved inside one
                // editor call, so without this every ray below answers about where
                // the building was before it was scaled.
                Physics.SyncTransforms();

                // How far out the wall is at each fixing, at that fixing's own
                // height and its own heading.
                var heights = new List<float>();
                var reaches = new List<float>();

                foreach (Vector2 unscaled in spec.Fixings)
                {
                    Vector2 fixing = unscaled * scale;
                    Vector3 anchor = sail.TransformPoint(new Vector3(unscaled.x, unscaled.y, 0f));

                    float heading = Mathf.Atan2(anchor.x, anchor.z);
                    float wall = Surface(colliders, heading, anchor.y, radius);
                    if (wall <= Mathf.Abs(fixing.x)) continue;

                    // The reach the sail's own axis needs for THIS fixing to land on
                    // the wall, and the height it needs it at.
                    heights.Add(anchor.y);
                    reaches.Add(Mathf.Sqrt((wall * wall) - (fixing.x * fixing.x)));
                }

                int found = heights.Count;
                if (found == 0) return;

                // Fit reach against height. The slope IS the wall's batter -- the kit
                // builds every drum on one 6.4 deg angle -- and a sail whose wall edge
                // stays vertical against it can only ever touch at one height.
                float sumY = 0f, sumR = 0f, sumYY = 0f, sumYR = 0f;
                for (int i = 0; i < found; i++)
                {
                    sumY += heights[i];
                    sumR += reaches[i];
                    sumYY += heights[i] * heights[i];
                    sumYR += heights[i] * reaches[i];
                }

                float slope = 0f;
                float denominator = (found * sumYY) - (sumY * sumY);
                if (found > 1 && Mathf.Abs(denominator) > 1e-4f)
                    slope = ((found * sumYR) - (sumY * sumR)) / denominator;

                slope = Mathf.Clamp(slope, -MaxBatter, MaxBatter);

                // Then slide that line in until NO fixing is left outside the wall.
                // A fixing sunk into masonry is a fixing: it is 0.2 m of hook, and
                // the wall is where it belongs. One hanging in the air is a hole
                // between the sail and the building, and that is what reads as
                // wrong from outside, so the shallowest wall wins.
                float seat = float.MaxValue;
                for (int i = 0; i < found; i++)
                    seat = Mathf.Min(seat, reaches[i] - (slope * heights[i]));

                distance = seat - SeatBite;

                // Rebuild the sail's axes so its own "up" leans with the wall.
                Vector3 up = (Vector3.up + (direction * slope)).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
                Vector3 forward = Vector3.Cross(right, up).normalized;
                if (Vector3.Dot(forward, direction) < 0f) forward = -forward;

                sail.localRotation = Quaternion.LookRotation(forward, up);
                sail.localPosition = direction * distance;
            }

            // Nothing may be left hanging. A ray can miss the wall a fixing needs --
            // a recess between two annexes, the seam where one drum steps in over
            // another -- and that fixing then never got a say in the seat above. The
            // colliders answer for every point, so the last word is theirs: pull the
            // sail in until the furthest-out fixing touches masonry.
            float pulled = 0f;

            for (int pass = 0; pass < SeatPasses; pass++)
            {
                Physics.SyncTransforms();

                float excess = 0f;
                foreach (Vector2 unscaled in spec.Fixings)
                {
                    Vector3 point = sail.TransformPoint(new Vector3(unscaled.x, unscaled.y, 0f));
                    float nearest = float.MaxValue;

                    foreach (Collider c in colliders)
                        nearest = Mathf.Min(nearest, Vector3.Distance(c.ClosestPoint(point), point));

                    if (nearest < float.MaxValue) excess = Mathf.Max(excess, nearest);
                }

                if (excess <= SeatBite) return;

                float step = Mathf.Min(excess, MaxPull - pulled);
                if (step <= 0f) return;

                pulled += step;
                distance -= step;
                sail.localPosition = direction * distance;
            }
        }

        /// <summary>
        /// The building's outer surface along one heading at one height, by firing a
        /// ray at it from outside. The colliders are the authority on where the wall
        /// IS -- the triangle profile that chose the heading is a 2 deg silhouette
        /// and puts a sail up to half a metre off a facade it meets at an angle.
        /// Returns 0 when the ray finds no wall at that height.
        /// </summary>
        private static float Surface(Collider[] colliders, float heading, float height,
                                     float radius)
        {
            Vector3 outward = new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading));
            float start = radius + RayStandoff;
            var ray = new Ray((outward * start) + (Vector3.up * height), -outward);

            float hit = 0f;
            RaycastHit[] hits = Physics.RaycastAll(ray, start, ~0, QueryTriggerInteraction.Ignore);

            // Nearest hit that belongs to THIS building: the builder works in the
            // open scene, which holds whatever the level designer left in it.
            float nearest = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                if (System.Array.IndexOf(colliders, hits[i].collider) < 0) continue;
                if (hits[i].distance >= nearest) continue;
                nearest = hits[i].distance;
                hit = start - hits[i].distance;
            }

            return nearest == float.MaxValue ? 0f : hit;
        }

        /// <summary>
        /// Can this sail hang on this heading, and how far out does its wall plane
        /// sit? The wall edge is a chord across the facade, so the sail is pulled in
        /// until EVERY fixing is at or inside the wall it is nailed to, each tested
        /// against the wall radius at its own height and its own heading. Testing the
        /// group instead left one fixing of a pair up to 1.2 m out in the air: the
        /// two sit at different heights, and a coned wall is a different width at
        /// each of them.
        /// </summary>
        private static bool Seat(Sail sail, float scale, Dictionary<int, float[]> profiles,
                                 float yaw, List<Vector2> taken,
                                 out float distance, out float halfSpan)
        {
            distance = 0f;
            halfSpan = 0f;

            float halfWidth = sail.HalfWidth * scale;
            float[] top = profiles[Mathf.RoundToInt(sail.Fixings[0].y * scale * 100f)];
            float radius = top[Bucket(yaw)];
            if (radius <= halfWidth * WallMargin) return false;

            // First guess from the topmost fixing, then tightened per fixing. The
            // heading of a fixing depends on how far out the sail sits and the
            // distance depends on the headings, so it is solved by iteration.
            distance = Mathf.Sqrt((radius * radius) - (halfWidth * halfWidth));

            for (int pass = 0; pass < 3; pass++)
            {
                foreach (Vector2 unscaled in sail.Fixings)
                {
                    Vector2 fixing = unscaled * scale;
                    float[] profile = profiles[Mathf.RoundToInt(fixing.y * 100f)];
                    float heading = yaw + Mathf.Atan2(fixing.x, distance);
                    float wall = profile[Bucket(heading)];

                    if (wall <= Mathf.Abs(fixing.x) * WallMargin) return false;

                    float reach = Mathf.Sqrt((wall * wall) - (fixing.x * fixing.x));
                    if (reach < distance) distance = reach;
                }
            }

            distance -= SeatBite;
            if (distance <= 0f) return false;

            halfSpan = Mathf.Atan2(halfWidth, distance);

            for (int i = 0; i < taken.Count; i++)
            {
                float gap = Mathf.Abs(Mathf.DeltaAngle(taken[i].x * Mathf.Rad2Deg,
                                                       yaw * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                if (gap < halfSpan + taken[i].y + SailGap) return false;
            }

            return true;
        }

        /// <summary>
        /// The outermost wall surface per heading at one height, in root-local metres,
        /// from the structural triangles sliced by that horizontal plane. Built from
        /// geometry rather than renderer bounds because a building's annexes make its
        /// bounds a box round a lumpy silhouette, and a sail seated on that box hangs
        /// in the air over every notch.
        /// </summary>
        private static float[] Profile(List<Renderer> structural, float height)
        {
            var profile = new float[YawBuckets];
            bool any = false;

            foreach (Renderer r in structural)
            {
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                if (!filter.sharedMesh.isReadable) continue;

                Vector3[] vertices = filter.sharedMesh.vertices;
                int[] triangles = filter.sharedMesh.triangles;
                Transform t = r.transform;

                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    Vector3 a = t.TransformPoint(vertices[triangles[i]]);
                    Vector3 b = t.TransformPoint(vertices[triangles[i + 1]]);
                    Vector3 c = t.TransformPoint(vertices[triangles[i + 2]]);

                    any |= Cut(profile, a, b, height);
                    any |= Cut(profile, b, c, height);
                    any |= Cut(profile, c, a, height);
                }
            }

            return any ? Close(profile) : profile;
        }

        /// <summary>Where an edge crosses the plane, into the profile.</summary>
        private static bool Cut(float[] profile, Vector3 p, Vector3 q, float height)
        {
            if ((p.y < height && q.y < height) || (p.y > height && q.y > height)) return false;
            if (Mathf.Approximately(p.y, q.y)) return false;

            Vector3 hit = Vector3.LerpUnclamped(p, q, (height - p.y) / (q.y - p.y));
            int bucket = Bucket(Mathf.Atan2(hit.x, hit.z));
            float d = new Vector2(hit.x, hit.z).magnitude;
            if (d > profile[bucket]) profile[bucket] = d;
            return true;
        }

        /// <summary>
        /// Fill the headings no edge happened to cross. A 32-sided drum crosses the
        /// plane 32 times and the profile has 180 buckets, so most of it is holes --
        /// left as zeroes they read as "no wall here" and no sail is ever hung. Each
        /// hole takes the SMALLER of its two neighbouring measurements, so filling in
        /// can only seat a sail closer to the wall, never further out.
        /// </summary>
        private static float[] Close(float[] profile)
        {
            var closed = new float[YawBuckets];

            for (int i = 0; i < YawBuckets; i++)
            {
                if (profile[i] > 0f) { closed[i] = profile[i]; continue; }

                float back = 0f, forward = 0f;
                for (int step = 1; step < YawBuckets && back <= 0f; step++)
                    back = profile[((i - step) % YawBuckets + YawBuckets) % YawBuckets];
                for (int step = 1; step < YawBuckets && forward <= 0f; step++)
                    forward = profile[(i + step) % YawBuckets];

                closed[i] = Mathf.Min(back, forward);
            }

            return closed;
        }

        private static int Bucket(float yaw)
        {
            float turns = yaw / (Mathf.PI * 2f);
            int b = Mathf.FloorToInt((turns - Mathf.Floor(turns)) * YawBuckets);
            return Mathf.Clamp(b, 0, YawBuckets - 1);
        }

        // -------------------------------------------------------------------

        /// <summary>
        /// A named, reproducible dice roll. Written out rather than using
        /// System.Random so that re-running this on another machine or another
        /// runtime hangs every sail in exactly the same place -- the same reason
        /// the settlement generator has one SEED constant.
        /// </summary>
        private struct Roll
        {
            private uint state;

            public Roll(int seed)
            {
                state = (uint)seed * 2654435761u + 1u;
            }

            private uint Next()
            {
                state = (state * 1664525u) + 1013904223u;
                return state;
            }

            /// <summary>Inclusive low, exclusive high.</summary>
            public int Range(int low, int high)
            {
                if (high <= low) return low;
                return low + (int)(Next() % (uint)(high - low));
            }

            public float Unit()
            {
                return (Next() >> 8) / (float)(1 << 24);
            }
        }

        /// <summary>`WallCanopyLong` -> `wall_canopy_long`, matching tents_export.py.</summary>
        private static string Snake(string name)
        {
            return Regex.Replace(name, "(?<!^)(?=[A-Z])", "_").ToLowerInvariant();
        }
    }
}
