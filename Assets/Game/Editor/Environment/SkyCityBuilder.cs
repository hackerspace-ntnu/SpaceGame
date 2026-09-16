// Builds the Sky City prefab from sky_city.fbx, colliders and all.
//
// The model comes out of Blender via
// Assets/Game/Art/Models/_Source~/models/vehicles/sky_city_export.py. Everything
// below that -- import settings, the collider set, the cull LODGroup, static
// flags and the prefab -- is generated here rather than hand-authored, for the
// same reason BuildingPrefabBuilder exists: a prefab wired by hand is a prefab
// nobody can rebuild after the model changes. 109 renderers, 282 k triangles.
//
// Re-running is safe and is the intended workflow. Re-export the FBX, run this,
// and the prefab is rebuilt in place against the new geometry.
//
// Re-run from: Tools > Environment > Build Sky City Prefab
//
// ---------------------------------------------------------------------------
// WHY THIS DOES NOT LOOK LIKE BuildingPrefabBuilder
//
// That script fits a collider per renderer from a name rule table, which works
// because its five buildings ship 89-167 renderers that are each one part. The
// sky city's PRIMARY STRUCTURE is different: keel, cage, decks, cradles, prow,
// stern gear, gantry, outriggers and climbs are each ONE merged mesh spanning
// the whole 125 m ship. A box over `Mesh_SkyCity_Decks` is a 22 x 112 m solid
// slab you could stand on in mid-air; a convex hull of it is worse.
//
// So collision comes from two places:
//
//   * WALKABLE VOLUMES, below -- five hand-placed boxes for the five merged
//     surfaces a player actually stands on or bumps into. These are the model's
//     own authoring constants, converted once (see AXES), not measured off
//     renderer bounds, because the bounds of a merged mesh are meaningless here.
//   * THE RULE TABLE -- for the kit parts, which ARE one renderer each and do
//     behave like the buildings: dwellings, crates, catwalks, gas bags.
//
// AXES -- read before touching a number in Walkables.
//
// The model is authored -Y forward, +X starboard, +Z up. `_exportlib` writes
// with Blender's default conversion, so a Blender point (bx, by, bz) arrives at
// Unity (bx, bz, -by): the bow at Blender y = -56 is Unity z = +56, and the
// promenade at Blender z = 0 is Unity y = 0. Every Walkable below is already in
// UNITY space with that conversion applied; the Blender source value is quoted
// beside it so the two can be checked against sky_city.py.
//
// SCALE -- the FBX imports at lossyScale 1, so these are true metres. The build
// asserts it, because BuildingPrefabBuilder's mesh children sit at localScale
// 100 and anything reading mesh.bounds there is wrong by that factor.
// ---------------------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Fit = SpaceGame.EditorTools.StaticPropBuilder.Fit;

namespace SpaceGame.EditorTools
{
    public static class SkyCityBuilder
    {
        private const string Fbx =
            "Assets/Game/Art/Models/Environment/Structures/sky_city.fbx";
        private const string Prefab =
            "Assets/Game/Prefabs/Environment/Structures/SkyCity.prefab";
        private const string RootName = "SkyCity";

        // Matches BuildingPrefabBuilder: the Vrescal's NavMeshAgent needs
        // 2.30 m of clear width, and generated gaps under it are reported.
        private const float AgentClearance = 2.30f;

        // Clutter below this gets no collider whatever the rule says. Barrels
        // at 1.03 m are walked past; crate stacks at 2.34 m read as cover.
        private const float ClutterIgnoreHeight = 1.20f;

        // One cull level, as on the buildings -- there are no decimated meshes
        // to hang a real chain off. See StaticPropBuilder.BuildLodGroup.
        private const float LodCullRatio = 0.02f;

        // -------------------------------------------------------------------
        // Walkable volumes for the merged structure meshes.
        //
        // The deck is ONE box spanning the full 22.4 m beam rather than two
        // promenade strips with the open spine between them. The spine really is
        // open lattice in the model, and leaving it open would drop a player
        // walking the middle straight through the ship -- there is no inboard
        // railing modelled to stop them. A continuous deck is the kinder lie and
        // the player stands on the keel's top chord, which is genuinely there.
        //
        // NOT COVERED, deliberately: the seven cantilevered timber platforms per
        // side that hang past the deck edge out to x = 14.8. Their positions are
        // seeded-random inside `decks()` and are not knowable here, and widening
        // this box to reach them would lay invisible floor across the gaps
        // BETWEEN them, which is worse than no floor at all. They are set
        // dressing over the void; a player who walks off the edge falls, which
        // is true of most of this ship's outboard edge anyway -- handrails are
        // modelled only on the aft apron.
        // -------------------------------------------------------------------
        private struct Walkable
        {
            public string Name;
            public Vector3 Centre;   // Unity space, metres
            public Vector3 Size;
            public string Note;
        }

        private static readonly Walkable[] Walkables =
        {
            new Walkable
            {
                // Runs to z = -60.45, past the keel's own -56 stern frame. The
                // outboard deck strip is laid in random patches that overshoot
                // the frame by up to 4.4 m, so a collider stopping at the frame
                // leaves visibly plated deck you fall straight through.
                // Measured off the Decks renderer, not assumed.
                Name = "Collision_Deck",
                Centre = new Vector3(0f, -0.15f, -2.2f),
                Size = new Vector3(22.4f, 0.30f, 116.5f),
                Note = "promenade, both sides plus the spine; Blender z=0, y -56..60.4",
            },
            new Walkable
            {
                Name = "Collision_Keel",
                Centre = new Vector3(0f, -2.45f, 0f),
                Size = new Vector3(8.4f, 4.30f, 112f),
                Note = "girder body under the deck; Blender z -4.6..-0.3, x +/-4.2",
            },
            new Walkable
            {
                Name = "Collision_Prow",
                Centre = new Vector3(0f, -0.10f, 59.2f),
                Size = new Vector3(4.6f, 0.20f, 4.0f),
                Note = "bow lookout platform; Blender y=-59.2",
            },
            new Walkable
            {
                Name = "Collision_Gantry",
                Centre = new Vector3(0f, 19.61f, 0f),
                Size = new Vector3(2.6f, 0.18f, 100f),
                Note = "crown walkway; Blender z=19.7, y -50..50",
            },
            // The two end structures, each a stepped stack. One box per storey:
            // a single box over either would be a solid slab where the steps
            // cut back, and the roofs are walkable - both ladders to the crown
            // gantry start on them.
            new Walkable
            {
                Name = "Collision_BowCastle_0",
                Centre = new Vector3(0f, 2.5f, 51.5f),
                Size = new Vector3(17.6f, 5.4f, 14.0f),
                Note = "plated lower storey; Blender y -58.5..-44.5, z -0.2..5.2",
            },
            new Walkable
            {
                Name = "Collision_BowCastle_1",
                Centre = new Vector3(0f, 7.9f, 51.7f),
                Size = new Vector3(14.8f, 5.4f, 11.2f),
                Note = "second storey; Blender z 5.2..10.6",
            },
            new Walkable
            {
                Name = "Collision_BowCastle_2",
                Centre = new Vector3(0f, 13.55f, 51.7f),
                Size = new Vector3(10.8f, 5.9f, 7.6f),
                Note = "bridge storey, roof at y=16.5 carries the forward ladder",
            },
            new Walkable
            {
                Name = "Collision_SternBlock_0",
                Centre = new Vector3(0f, 3.4f, -49.5f),
                Size = new Vector3(16.0f, 7.2f, 10.0f),
                Note = "lower storey; Blender y 44.5..54.5, z -0.2..7.0",
            },
            new Walkable
            {
                Name = "Collision_SternBlock_1",
                Centre = new Vector3(0f, 9.25f, -49.3f),
                Size = new Vector3(12.4f, 4.5f, 7.6f),
                Note = "upper storey, roof at y=11.5 carries the aft ladder",
            },
        };

        // -------------------------------------------------------------------
        // Per-renderer rules. Ordered: the FIRST prefix match wins, so specific
        // names go above general ones. Every renderer is Mesh_SkyCity_*.
        // -------------------------------------------------------------------
        private struct Rule
        {
            public string Match;
            public Fit Fit;
            public string Note;
        }

        private static readonly Rule[] Rules =
        {
            // Merged primary structure -- handled by Walkables above, never by
            // a fitted box. Listed explicitly rather than left to the fallback
            // so that adding a new merged part fails loudly as an unmatched
            // name instead of silently acquiring a ship-sized collider.
            new Rule { Match = "Mesh_SkyCity_Decks", Fit = Fit.None, Note = "-> Collision_Deck" },
            new Rule { Match = "Mesh_SkyCity_Keel", Fit = Fit.None, Note = "-> Collision_Keel" },
            new Rule { Match = "Mesh_SkyCity_Prow", Fit = Fit.None, Note = "-> Collision_Prow" },
            new Rule { Match = "Mesh_SkyCity_Gantry", Fit = Fit.None, Note = "-> Collision_Gantry" },
            new Rule { Match = "Mesh_SkyCity_SternGear", Fit = Fit.None, Note = "pylons, ducts, rudder - 26 m of mostly empty air" },
            new Rule { Match = "Mesh_SkyCity_BowCastle", Fit = Fit.None, Note = "-> Collision_BowCastle_*" },
            new Rule { Match = "Mesh_SkyCity_SternBlock", Fit = Fit.None, Note = "-> Collision_SternBlock_*" },
            new Rule { Match = "Mesh_SkyCity_Cage", Fit = Fit.None, Note = "overhead ring frames" },
            new Rule { Match = "Mesh_SkyCity_Cradles", Fit = Fit.None, Note = "bag saddles, overhead" },
            new Rule { Match = "Mesh_SkyCity_Outriggers", Fit = Fit.None, Note = "sail booms and rigging" },
            new Rule { Match = "Mesh_SkyCity_Climbs", Fit = Fit.None, Note = "ladders, non-blocking" },

            // Walkable kit parts -- one renderer each, top face only.
            new Rule { Match = "Mesh_SkyCity_Walk", Fit = Fit.Surface, Note = "hanging walkway" },
            new Rule { Match = "Mesh_SkyCity_Under", Fit = Fit.Surface, Note = "under-keel walkway" },
            new Rule { Match = "Mesh_SkyCity_Gangway", Fit = Fit.Surface, Note = "aft apron gangway" },

            // Solid things to bump into.
            new Rule { Match = "Mesh_SkyCity_Home", Fit = Fit.Box, Note = "dwelling" },
            new Rule { Match = "Mesh_SkyCity_Dome", Fit = Fit.Box, Note = "sensor dome" },
            new Rule { Match = "Mesh_SkyCity_Dish", Fit = Fit.Box, Note = "dish mast" },
            new Rule { Match = "Mesh_SkyCity_Beacon", Fit = Fit.Box, Note = "forward beacon" },
            new Rule { Match = "Mesh_SkyCity_Stow", Fit = Fit.Box, Note = "crates and barrels, height-gated" },

            // The gas bags get real hulls: they are the biggest things aboard,
            // an ovoid fills about half its own box, and a player on the gantry
            // walks within 2 m of the top of one.
            new Rule { Match = "Mesh_SkyCity_Bag", Fit = Fit.Convex, Note = "gas envelope" },

            // Cloth, light and greeble -- walked under or past.
            new Rule { Match = "Mesh_SkyCity_Sail", Fit = Fit.None, Note = "sailcloth, diagonal bounds" },
            new Rule { Match = "Mesh_SkyCity_Shade", Fit = Fit.None, Note = "market awning, walk under" },
            new Rule { Match = "Mesh_SkyCity_Flag", Fit = Fit.None, Note = "pennant" },
            new Rule { Match = "Mesh_SkyCity_Lamp", Fit = Fit.None, Note = "lantern" },
            new Rule { Match = "Mesh_SkyCity_Flood", Fit = Fit.None, Note = "floodlight" },
            new Rule { Match = "Mesh_SkyCity_Boarding", Fit = Fit.None, Note = "ladder, non-blocking" },
        };

        // Anything unmatched gets nothing and is NAMED in the report, so a
        // renamed or new part shows up instead of quietly losing its collider.
        private const Fit Fallback = Fit.None;

        [MenuItem("Tools/Environment/Build Sky City Prefab")]
        public static void Build()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (source == null)
            {
                Debug.LogError($"No FBX at {Fbx}. Run sky_city_export.py first.");
                return;
            }

            StaticPropBuilder.ConfigureImporter(Fbx);
            source = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);

            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            root.name = RootName;
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;

            var report = new System.Text.StringBuilder();
            report.AppendLine("SkyCityBuilder");

            Vector3 ls = root.transform.lossyScale;
            if (Mathf.Abs(ls.x - 1f) > 0.001f || Mathf.Abs(ls.y - 1f) > 0.001f ||
                Mathf.Abs(ls.z - 1f) > 0.001f)
            {
                Debug.LogWarning(
                    $"{RootName} imported at lossyScale {ls:F4}, not 1. Every " +
                    "collider extent in SkyCityBuilder assumes metres at " +
                    "scale 1 - re-measure before trusting this prefab.");
            }

            int boxes = 0, surfaces = 0, convex = 0, skipped = 0;
            var unmatched = new List<string>();
            var tooNarrow = new List<string>();

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                Fit fit = Resolve(r.name, out bool matched);
                if (!matched) unmatched.Add(r.name);

                if (fit != Fit.None && fit != Fit.Surface &&
                    r.bounds.size.y < ClutterIgnoreHeight)
                {
                    fit = Fit.None;
                }

                switch (fit)
                {
                    case Fit.Box:
                        StaticPropBuilder.AddBox(r, false);
                        boxes++;
                        break;
                    case Fit.Surface:
                        StaticPropBuilder.AddBox(r, true);
                        surfaces++;
                        break;
                    case Fit.Convex:
                        if (StaticPropBuilder.AddConvex(r)) convex++;
                        else { StaticPropBuilder.AddBox(r, false); boxes++; }
                        break;
                    default:
                        skipped++;
                        break;
                }

                if (fit == Fit.Surface)
                {
                    Bounds bb = r.bounds;
                    float narrow = Mathf.Min(bb.size.x, bb.size.z);
                    if (narrow < AgentClearance)
                        tooNarrow.Add($"{r.name} ({narrow:F2} m)");
                }
            }

            foreach (Walkable w in Walkables) AddWalkable(root, w);

            StaticPropBuilder.BuildLodGroup(root, renderers, LodCullRatio);
            StaticPropBuilder.MarkStatic(root);

            StaticPropBuilder.EnsureFolder(
                System.IO.Path.GetDirectoryName(Prefab).Replace('\\', '/'));
            PrefabUtility.SaveAsPrefabAsset(root, Prefab);

            int tris = renderers
                .Select(r => r.GetComponent<MeshFilter>())
                .Where(mf => mf != null && mf.sharedMesh != null)
                .Sum(mf => mf.sharedMesh.triangles.Length / 3);
            Bounds world = WorldBounds(renderers);

            Object.DestroyImmediate(root);

            report.AppendLine(
                $"  {renderers.Length} renderers, {tris} tris, lossyScale {ls:F3}");
            report.AppendLine(
                $"  world size {world.size.x:F2} x {world.size.y:F2} x " +
                $"{world.size.z:F2} m, centre {world.center:F2}");
            report.AppendLine(
                $"  colliders: {boxes} box + {surfaces} surface + {convex} convex " +
                $"+ {Walkables.Length} authored = " +
                $"{boxes + surfaces + convex + Walkables.Length} " +
                $"({skipped} renderers left uncollided)");
            if (unmatched.Count > 0)
            {
                report.AppendLine(
                    $"  UNMATCHED renderer names ({unmatched.Count}) - these got " +
                    $"no collider by fallback, not by decision: " +
                    string.Join(", ", unmatched.Take(8)) +
                    (unmatched.Count > 8 ? ", ..." : ""));
            }
            if (tooNarrow.Count > 0)
            {
                report.AppendLine(
                    $"  walkways under the {AgentClearance:F2} m agent clearance " +
                    $"({tooNarrow.Count}): " + string.Join(", ", tooNarrow.Take(8)) +
                    (tooNarrow.Count > 8 ? ", ..." : "") +
                    " - passable by the player, not by a Vrescal.");
            }
            report.AppendLine($"  saved {Prefab}");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(report.ToString());
        }

        private static void AddWalkable(GameObject root, Walkable w)
        {
            var go = new GameObject(w.Name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = w.Centre;
            var bc = go.AddComponent<BoxCollider>();
            bc.size = w.Size;
        }

        private static Fit Resolve(string name, out bool matched)
        {
            for (int i = 0; i < Rules.Length; i++)
            {
                if (name.StartsWith(Rules[i].Match, System.StringComparison.Ordinal))
                {
                    matched = true;
                    return Rules[i].Fit;
                }
            }
            matched = false;
            return Fallback;
        }

        private static Bounds WorldBounds(Renderer[] renderers)
        {
            if (renderers.Length == 0) return new Bounds();
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }
    }
}
