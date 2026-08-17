// Builds usable prefabs for the five exported buildings, from the FBXs up.
//
// The models come out of Blender via the *_export.py scripts beside each
// .blend. Everything below that -- import settings, the collider set, LOD
// groups, static flags and the prefab itself -- is generated here rather than
// hand-authored, for the same reason GolemBuilder and VrescalBuilder exist:
// a prefab wired by hand is a prefab nobody can rebuild after the model
// changes. These five are 300-380 k triangles across 89-167 renderers each;
// nobody is placing those colliders twice by hand.
//
// Re-running is safe and is the intended workflow. Re-export an FBX, run this,
// and that building's prefab is rebuilt in place against the new geometry.
//
// Re-run from: Tools > Environment > Build Building Prefabs
//
// ---------------------------------------------------------------------------
// Measured in-engine on 2026-08-16, not taken from the source table. Every
// number in this file derives from these, so re-measure if a model is
// re-exported:
//
//   model                 lossyScale  world size (m)        renderers  tris
//   relay_outpost         1.0         22.27 x 19.77 x 19.43   89       303 486
//   lattice_outpost       1.0         22.35 x 52.00 x 26.10  157       341 034
//   refinery_tower        1.0         87.17 x 76.66 x 47.07  167       378 288
//   hulk_settlement       1.0         94.20 x 66.00 x 30.16  133       355 652
//   mining_rig_derelict   1.0         25.07 x 53.97 x 21.40  146       307 412
//
// SCALE -- read this before touching any collider code.
//
// The ROOT imports at lossyScale 1.0, so world-space sizes above are true
// metres. But every mesh CHILD sits at localScale 100 with a -90 deg X rotation
// baked in by the Blender export, and the shared meshes are authored in
// centi-units to match (a 0.97 m walkway tile has mesh.bounds.size 0.010, and
// its local Y is the world's Z).
//
// So the project's familiar "FBXs import at lossyScale 100" trap IS present
// here -- one level below where you would look for it. Anything that reads
// mesh.bounds and writes it onto a collider is wrong by a factor of 100 and by
// a 90 degree rotation; the first version of this file did exactly that and
// gave every part a 25 m Z extent. AddBox therefore works in world space and
// converts back. See the comment there.
//
// Each build prints the root lossyScale so a future export that puts a factor
// back on the root shows up immediately.
//
// Buried bases are intentional and are not corrected: refinery_tower reaches
// 1.66 m below origin and hulk_settlement 6.00 m. Both are modelled dug in, so
// dropping them at terrain height is correct.
// ---------------------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class BuildingPrefabBuilder
    {
        private const string ModelDir =
            "Assets/Game/Art/Models/Environment/Structures";
        private const string OutpostFbxDir = ModelDir + "/Outpost";
        private const string IndustrialFbxDir = ModelDir + "/Industrial";

        private const string RelayFbx = OutpostFbxDir + "/relay_outpost.fbx";
        private const string LatticeFbx = OutpostFbxDir + "/lattice_outpost.fbx";
        private const string RefineryFbx = IndustrialFbxDir + "/refinery_tower.fbx";
        private const string HulkFbx = IndustrialFbxDir + "/hulk_settlement.fbx";
        private const string MiningRigFbx = IndustrialFbxDir + "/mining_rig_derelict.fbx";

        private const string PrefabDir =
            "Assets/Game/Prefabs/Environment/Structures";
        private const string OutpostPrefabDir = PrefabDir + "/Outpost";
        private const string IndustrialPrefabDir = PrefabDir + "/Industrial";

        private const string RelayPrefab = OutpostPrefabDir + "/RelayOutpost.prefab";
        private const string LatticePrefab = OutpostPrefabDir + "/LatticeOutpost.prefab";
        private const string RefineryPrefab = IndustrialPrefabDir + "/RefineryTower.prefab";
        private const string HulkPrefab = IndustrialPrefabDir + "/HulkSettlement.prefab";
        private const string MiningRigPrefab = IndustrialPrefabDir + "/MiningRigDerelict.prefab";

        // -------------------------------------------------------------------
        // Clearance budget
        //
        // The largest thing that walks here is the Vrescal. Its NavMeshAgent is
        // built at radius 1.05 m / height 3.85 m (VrescalBuilder.cs), so it
        // needs 2.10 m of clear width. The task brief quotes 1.15 m / 2.30 m;
        // the stricter of the two is used throughout so the result is safe
        // under either figure, and so a future re-tune of the creature does not
        // silently invalidate these colliders.
        //
        // This only governs colliders THIS script generates. It cannot widen a
        // walkway that is modelled narrow -- see the report printed at the end
        // of a build, which lists every generated gap under budget.
        // -------------------------------------------------------------------
        private const float AgentRadius = 1.15f;
        private const float AgentClearance = 2.30f;

        // Yard clutter below this height is skipped entirely. Measured: crates
        // and drums sit at 0.44-1.03 m, stools at 0.51 m, cable runs at 0.39 m.
        // A collider on each would add ~40 bodies per outpost to trip over and
        // would carve the navmesh into confetti, for props the player walks
        // past. Anything taller than this (tanks at 1.89 m, crate stacks at
        // 2.34 m) does get one -- those read as cover.
        private const float ClutterIgnoreHeight = 1.20f;

        // Screen-relative height at which each LOD hands over. Only two levels
        // exist because there is exactly one mesh per renderer and no decimated
        // variants were authored -- see BuildLodGroup for why this is a cull
        // group rather than a real LOD chain.
        private const float LodCullRatio = 0.02f;

        /// <summary>How a matched renderer is turned into collision.</summary>
        private enum Fit
        {
            /// <summary>No collider at all.</summary>
            None = 0,
            /// <summary>Axis-aligned box over the renderer bounds.</summary>
            Box = 1,
            /// <summary>Box flattened to the top face -- walkable surface only.</summary>
            Surface = 2,
            /// <summary>Convex MeshCollider -- silhouette actually matters.</summary>
            Convex = 3,
        }

        private struct Rule
        {
            public string Match;   // matched against the renderer name, ordinal
            public Fit Fit;
            public string Note;
        }

        private struct Building
        {
            public string Fbx;
            public string Prefab;
            public string Name;
            public Rule[] Rules;
            public Fit Fallback;
            public bool AmbientMotion;
        }

        // -------------------------------------------------------------------
        // Per-model rules.
        //
        // These are ordered: the FIRST match wins, so specific prefixes go
        // above general ones. The naming schemes genuinely differ per model
        // (relay_outpost uses Roof_/Wall_/Yard_, lattice_outpost uses
        // DeckPlate_/Rail_/Mast_, the three industrial models use Mesh_*), so
        // there is no single global table -- a shared list would silently stop
        // matching the moment one exporter changed its prefix.
        // -------------------------------------------------------------------

        private static readonly Rule[] RelayRules =
        {
            // The hull. One box over the block is the whole building's solidity.
            new Rule { Match = "Outpost_Block", Fit = Fit.Box,
                       Note = "13.6 x 5.1 x 10.6 m hull" },
            // Yawed 18 deg, so a world-aligned box overshoots its footprint by
            // 3.85 m and would swallow part of the roof walkway beside it.
            new Rule { Match = "Outpost_Cupola", Fit = Fit.Convex, Note = "roof cupola, yawed 18 deg" },

            // Roof walkway: 9 tiles of 0.97 m. Surface-only so the player stands
            // on them without the 0.11 m slab edge catching a step.
            new Rule { Match = "Roof_Walk", Fit = Fit.Surface, Note = "roof walkway tiles" },

            // Ladders are climbed by walking into them in this project (there is
            // no climb system), so they must not block the roof approach.
            new Rule { Match = "Roof_Ladder", Fit = Fit.None, Note = "ladder, non-blocking" },
            new Rule { Match = "Mast_Ladder", Fit = Fit.None, Note = "ladder, non-blocking" },

            // Door and its frame read as solid wall; there is no door logic here.
            new Rule { Match = "Wall_Door", Fit = Fit.Box, Note = "door panel" },
            new Rule { Match = "Mesh_Outpost_Stoop", Fit = Fit.Surface, Note = "entry stoop" },

            // Yard furniture tall enough to be cover.
            new Rule { Match = "Yard_Tank", Fit = Fit.Box, Note = "1.89 m tank" },
            new Rule { Match = "Yard_CrateStack", Fit = Fit.Box, Note = "2.34 m crate stack" },
            new Rule { Match = "Yard_ToolRack", Fit = Fit.Box, Note = "1.72 m rack" },
            new Rule { Match = "Yard_Shelf", Fit = Fit.Box, Note = "1.55 m shelf" },
            new Rule { Match = "Yard_Bottles", Fit = Fit.Box, Note = "1.53 m bottle rack" },

            // Everything else in the yard, and every wall greeble, is decoration.
            new Rule { Match = "Yard_", Fit = Fit.None, Note = "yard clutter under 1.2 m" },
            new Rule { Match = "Wall_", Fit = Fit.None, Note = "wall greeble, inside the hull box" },
            new Rule { Match = "Roof_", Fit = Fit.None, Note = "roof greeble" },
            new Rule { Match = "Cupola_", Fit = Fit.None, Note = "cupola greeble" },
            new Rule { Match = "Mast_", Fit = Fit.None, Note = "mast greeble" },
            new Rule { Match = "Mesh_Outpost_", Fit = Fit.None, Note = "trim" },
        };

        private static readonly Rule[] LatticeRules =
        {
            // 52 m mast on a 6.3 m splayed foot. The foot is the only thing at
            // ground level besides a ladder, so it is the only ground blocker.
            new Rule { Match = "Mast_Splay", Fit = Fit.Convex, Note = "splayed foot, 12.2 m across" },
            new Rule { Match = "Mast_Bay", Fit = Fit.Box, Note = "mast bay" },
            new Rule { Match = "Mast_Collar", Fit = Fit.Box, Note = "mast collar" },

            // Deck_Lower is one 20.24 x 2.85 x 16.44 m slab -- the platform the
            // 36 DeckPlate_* tiles sit on. Colliding the slab and skipping the
            // tiles turns 37 colliders into 1 with no loss: the tiles are 0.06-
            // 0.11 m thick veneer on top of it.
            new Rule { Match = "Deck_Lower", Fit = Fit.Box, Note = "main deck slab" },
            new Rule { Match = "DeckPlate_", Fit = Fit.None, Note = "veneer on Deck_Lower" },

            new Rule { Match = "Ladder_", Fit = Fit.None, Note = "ladder, non-blocking" },
            new Rule { Match = "Stair_Lower", Fit = Fit.Surface, Note = "ground stair" },
            new Rule { Match = "Plant_Catwalk", Fit = Fit.Surface, Note = "catwalk" },

            new Rule { Match = "Cab_", Fit = Fit.Box, Note = "cab volume" },
            new Rule { Match = "Block_", Fit = Fit.Box, Note = "station block" },

            // Rails are waist-high guard rails around the deck. Left solid they
            // would fence the deck off from anything wider than the gap between
            // posts; the deck slab already stops a fall in the only sense this
            // project models.
            new Rule { Match = "Rail_", Fit = Fit.None, Note = "guard rail" },
            new Rule { Match = "Riser_", Fit = Fit.None, Note = "pipe/cable riser" },
            new Rule { Match = "Deck_Flood", Fit = Fit.None, Note = "floodlight" },
            new Rule { Match = "Mast_DeckBrace", Fit = Fit.None, Note = "brace, above head height" },
        };

        // The three industrial models share the Mesh_* scheme, but their part
        // vocabularies differ enough that they still get separate tables.

        private static readonly Rule[] RefineryRules =
        {
            // Podium is the 26.6 x 9.8 x 22.6 m base everything stands on.
            new Rule { Match = "Mesh_Refinery_Podium", Fit = Fit.Box, Note = "base podium" },
            new Rule { Match = "Mesh_Outrigger_Deck", Fit = Fit.Box, Note = "outrigger deck slab" },
            new Rule { Match = "Mesh_Outrigger_Col", Fit = Fit.Box, Note = "outrigger column" },
            new Rule { Match = "Mesh_Outrigger_Portal", Fit = Fit.Box, Note = "portal frame" },

            // Legs are raked and splayed -- a box over a raked leg swallows the
            // gap a creature walks through, so these get convex hulls.
            new Rule { Match = "Mesh_Leg_", Fit = Fit.Convex, Note = "raked/splayed leg" },

            new Rule { Match = "Mesh_Bay_", Fit = Fit.Box, Note = "tower bay" },
            new Rule { Match = "Mesh_Tank_Ground", Fit = Fit.Box, Note = "ground tank" },
            new Rule { Match = "Mesh_Flare_Stack", Fit = Fit.Box, Note = "flare stack" },
            new Rule { Match = "Mesh_Deck_Tank", Fit = Fit.Box, Note = "deck tank" },
            new Rule { Match = "Mesh_Deck_Module", Fit = Fit.Box, Note = "deck module" },
            new Rule { Match = "Mesh_Deck_Pod", Fit = Fit.Box, Note = "deck pod" },
            new Rule { Match = "Mesh_Capsule_", Fit = Fit.Box, Note = "capsule volume" },

            new Rule { Match = "Mesh_Conveyor_", Fit = Fit.Convex, Note = "conveyor run" },
            new Rule { Match = "Mesh_Anchor_", Fit = Fit.Box, Note = "guy anchor" },

            // Deck plates are 0.97 m veneer tiles on the podium, as on the
            // lattice: the podium box already carries them.
            new Rule { Match = "Mesh_Deck_Plate_", Fit = Fit.None, Note = "veneer on podium" },

            new Rule { Match = "Mesh_Walk_", Fit = Fit.Surface, Note = "tower walkway ring" },
            new Rule { Match = "Mesh_Balcony", Fit = Fit.Surface, Note = "balcony" },
            new Rule { Match = "Mesh_Stair", Fit = Fit.Surface, Note = "stair flight" },

            new Rule { Match = "Mesh_Ladder", Fit = Fit.None, Note = "ladder, non-blocking" },
            new Rule { Match = "Mesh_Rail", Fit = Fit.None, Note = "guard rail" },
            new Rule { Match = "Mesh_Pipe_", Fit = Fit.None, Note = "pipework" },
            new Rule { Match = "Mesh_Lamp_", Fit = Fit.None, Note = "lamp" },
            new Rule { Match = "Mesh_Flood_", Fit = Fit.None, Note = "floodlight" },
            new Rule { Match = "Mesh_Vent", Fit = Fit.None, Note = "vent" },
            new Rule { Match = "Mesh_Antenna", Fit = Fit.None, Note = "antenna" },
            new Rule { Match = "Mesh_Derrick_", Fit = Fit.None, Note = "derrick greeble" },
            new Rule { Match = "Mesh_Refinery_", Fit = Fit.None, Note = "crown greeble" },
        };

        private static readonly Rule[] HulkRules =
        {
            // Ten stacked hull blocks, 8 m tall each, are the settlement's mass.
            new Rule { Match = "Mesh_Block_", Fit = Fit.Box, Note = "hull block" },
            new Rule { Match = "Mesh_Hulk_CrownDeck", Fit = Fit.Box, Note = "crown deck" },
            new Rule { Match = "Mesh_Hulk_BoomSaddle", Fit = Fit.Box, Note = "boom saddle" },

            new Rule { Match = "Mesh_Shanty_", Fit = Fit.Box, Note = "shanty volume" },
            new Rule { Match = "Mesh_Stack_", Fit = Fit.Box, Note = "smoke stack" },
            // Roof pods and the tower gear are yawed 8-24 deg off the hull axis.
            // Boxed, each overshoots its own footprint by 2.0-2.2 m and starts
            // fencing off the walkway that runs past it.
            new Rule { Match = "Mesh_Roof_", Fit = Fit.Convex, Note = "roof structure, yawed" },
            new Rule { Match = "Mesh_Tower_", Fit = Fit.Convex, Note = "tower gear, yawed" },

            new Rule { Match = "Mesh_Boom_", Fit = Fit.Convex, Note = "crane boom" },
            new Rule { Match = "Mesh_Conveyor_", Fit = Fit.Convex, Note = "conveyor run" },
            new Rule { Match = "Mesh_Strut", Fit = Fit.Convex, Note = "strut" },

            new Rule { Match = "Mesh_Deck_", Fit = Fit.None, Note = "veneer tiles on blocks" },
            new Rule { Match = "Mesh_Walk_", Fit = Fit.Surface, Note = "walkway" },
            new Rule { Match = "Mesh_Stair", Fit = Fit.Surface, Note = "stair flight" },
            new Rule { Match = "Mesh_Door", Fit = Fit.None, Note = "door panel, flush with block" },

            new Rule { Match = "Mesh_Ladder", Fit = Fit.None, Note = "ladder, non-blocking" },
            new Rule { Match = "Mesh_Rail", Fit = Fit.None, Note = "guard rail" },
            new Rule { Match = "Mesh_Pipe_", Fit = Fit.None, Note = "pipework" },
            new Rule { Match = "Mesh_Lamp_", Fit = Fit.None, Note = "lamp" },
            new Rule { Match = "Mesh_Flood_", Fit = Fit.None, Note = "floodlight" },
            new Rule { Match = "Mesh_Vent", Fit = Fit.None, Note = "vent" },
            new Rule { Match = "Mesh_Antenna", Fit = Fit.None, Note = "antenna" },
        };

        private static readonly Rule[] MiningRigRules =
        {
            new Rule { Match = "Mesh_L0_", Fit = Fit.Box, Note = "base storey, 19.5 x 8.6 x 18.2 m" },
            new Rule { Match = "Mesh_L1_", Fit = Fit.Box, Note = "storey 1" },
            new Rule { Match = "Mesh_L2_", Fit = Fit.Box, Note = "storey 2" },
            new Rule { Match = "Mesh_L3_", Fit = Fit.Box, Note = "storey 3" },
            new Rule { Match = "Mesh_L4_", Fit = Fit.Box, Note = "storey 4" },
            new Rule { Match = "Mesh_Crown_", Fit = Fit.Box, Note = "crown machine house" },
            // Mesh_Stack_Cluster is yawed on all three axes (271/295/47), so a
            // world-aligned box is 1.4 m wider than the part.
            new Rule { Match = "Mesh_Stack_", Fit = Fit.Convex, Note = "stack, compound rotation" },

            new Rule { Match = "Mesh_Deck_Hatch", Fit = Fit.None, Note = "hatch, animated" },
            new Rule { Match = "Mesh_Deck_", Fit = Fit.None, Note = "crown deck veneer" },
            new Rule { Match = "Mesh_Walk_", Fit = Fit.Surface, Note = "walkway ring" },
            new Rule { Match = "Mesh_Balcony", Fit = Fit.Surface, Note = "balcony" },
            new Rule { Match = "Mesh_Stair", Fit = Fit.Surface, Note = "stair flight" },

            new Rule { Match = "Mesh_Ladder", Fit = Fit.None, Note = "ladder, non-blocking" },
            new Rule { Match = "Mesh_Rail", Fit = Fit.None, Note = "guard rail" },
            new Rule { Match = "Mesh_Cable", Fit = Fit.None, Note = "slack cable, animated" },
            new Rule { Match = "Mesh_Pipe", Fit = Fit.None, Note = "pipework" },
            new Rule { Match = "Mesh_Lamp_", Fit = Fit.None, Note = "lamp" },
            new Rule { Match = "Mesh_Flood_", Fit = Fit.None, Note = "floodlight" },
            new Rule { Match = "Mesh_Vent", Fit = Fit.None, Note = "vent" },
            new Rule { Match = "Mesh_Cowl", Fit = Fit.None, Note = "cowl" },
            new Rule { Match = "Mesh_Patch_", Fit = Fit.None, Note = "hull patch decal" },
            new Rule { Match = "Mesh_Mark_", Fit = Fit.None, Note = "painted marking" },
            new Rule { Match = "Mesh_Win_", Fit = Fit.None, Note = "window" },
            new Rule { Match = "Mesh_Door_", Fit = Fit.None, Note = "door panel" },
        };

        private static readonly Building[] Buildings =
        {
            new Building { Fbx = RelayFbx,     Prefab = RelayPrefab,     Name = "RelayOutpost",
                           Rules = RelayRules,     Fallback = Fit.None },
            new Building { Fbx = LatticeFbx,   Prefab = LatticePrefab,   Name = "LatticeOutpost",
                           Rules = LatticeRules,   Fallback = Fit.None },
            new Building { Fbx = RefineryFbx,  Prefab = RefineryPrefab,  Name = "RefineryTower",
                           Rules = RefineryRules,  Fallback = Fit.None },
            new Building { Fbx = HulkFbx,      Prefab = HulkPrefab,      Name = "HulkSettlement",
                           Rules = HulkRules,      Fallback = Fit.None },
            new Building { Fbx = MiningRigFbx, Prefab = MiningRigPrefab, Name = "MiningRigDerelict",
                           Rules = MiningRigRules, Fallback = Fit.None, AmbientMotion = true },
        };

        [MenuItem("Tools/Environment/Build Building Prefabs")]
        public static void BuildAll()
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("BuildingPrefabBuilder");

            for (int i = 0; i < Buildings.Length; i++)
                BuildOne(Buildings[i], report);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(report.ToString());
        }

        private static void BuildOne(Building b, System.Text.StringBuilder report)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(b.Fbx);
            if (source == null)
            {
                Debug.LogError($"No FBX at {b.Fbx}. Run its *_export.py first.");
                report.AppendLine($"  {b.Name}: SKIPPED, no FBX");
                return;
            }

            ConfigureImporter(b.Fbx);
            source = AssetDatabase.LoadAssetAtPath<GameObject>(b.Fbx);

            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            root.name = b.Name;
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;

            // Guard the assumption every collider extent below rests on. At
            // lossyScale 1 the numbers are metres; at anything else they are not.
            Vector3 ls = root.transform.lossyScale;
            if (Mathf.Abs(ls.x - 1f) > 0.001f || Mathf.Abs(ls.y - 1f) > 0.001f ||
                Mathf.Abs(ls.z - 1f) > 0.001f)
            {
                Debug.LogWarning(
                    $"{b.Name} imported at lossyScale {ls:F4}, not 1. Collider " +
                    "sizes in BuildingPrefabBuilder assume metres at scale 1 — " +
                    "re-measure before trusting this prefab.");
            }

            int boxes = 0, surfaces = 0, convex = 0, skipped = 0;
            var tooNarrow = new List<string>();

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                Fit fit = Resolve(r.name, b.Rules, b.Fallback);

                // Small clutter never gets a collider whatever the rule says --
                // this is the height gate, applied after the name match so a
                // rule cannot accidentally re-add a 0.44 m crate.
                if (fit != Fit.None && r.bounds.size.y < ClutterIgnoreHeight &&
                    fit != Fit.Surface)
                {
                    fit = Fit.None;
                }

                switch (fit)
                {
                    case Fit.Box:
                        AddBox(r, false);
                        boxes++;
                        break;
                    case Fit.Surface:
                        AddBox(r, true);
                        surfaces++;
                        break;
                    case Fit.Convex:
                        if (AddConvex(r)) convex++;
                        else { AddBox(r, false); boxes++; }
                        break;
                    default:
                        skipped++;
                        break;
                }

                // Flag any collider narrower than the agent budget in both
                // horizontal axes -- that is a gap nothing large can path through.
                if (fit == Fit.Surface)
                {
                    Bounds bb = r.bounds;
                    float narrow = Mathf.Min(bb.size.x, bb.size.z);
                    if (narrow < AgentClearance)
                        tooNarrow.Add($"{r.name} ({narrow:F2} m)");
                }
            }

            BuildLodGroup(root, renderers);
            MarkStatic(root);

            if (b.AmbientMotion) WireAmbientMotion(root, report, b.Name);

            EnsureFolder(System.IO.Path.GetDirectoryName(b.Prefab).Replace('\\', '/'));
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, b.Prefab);

            int tris = renderers
                .Select(r => r.GetComponent<MeshFilter>())
                .Where(mf => mf != null && mf.sharedMesh != null)
                .Sum(mf => mf.sharedMesh.triangles.Length / 3);

            Object.DestroyImmediate(root);

            report.AppendLine($"  {b.Name}: {boxes} box + {surfaces} surface + " +
                              $"{convex} convex = {boxes + surfaces + convex} colliders " +
                              $"({skipped} renderers left uncollided), {tris} tris, " +
                              $"lossyScale {ls:F3}");

            if (tooNarrow.Count > 0)
            {
                report.AppendLine($"    walkways under the {AgentClearance:F2} m " +
                                  $"clearance budget ({tooNarrow.Count}): " +
                                  string.Join(", ", tooNarrow.Take(6)) +
                                  (tooNarrow.Count > 6 ? ", …" : ""));
            }
        }

        private static Fit Resolve(string name, Rule[] rules, Fit fallback)
        {
            for (int i = 0; i < rules.Length; i++)
            {
                if (name.StartsWith(rules[i].Match, System.StringComparison.Ordinal))
                    return rules[i].Fit;
            }
            return fallback;
        }

        // -------------------------------------------------------------------
        // Colliders
        // -------------------------------------------------------------------

        // Boxes are derived from Renderer.bounds (world, axis-aligned) and then
        // converted into the collider's own local space.
        //
        // Using mesh.bounds directly here is WRONG on these models and was the
        // first version's bug. Measured: the FBX root sits at scale 1, but every
        // mesh child sits at localScale 100 with a -90 deg X rotation baked in
        // by the Blender export. So the shared mesh is authored in centi-units
        // (a 0.97 m walkway tile has mesh.bounds.size 0.010) and its local Y is
        // the world's Z. Copying mesh.bounds onto a BoxCollider produced a
        // constant 25 m Z extent on every part and flattened the wrong axis.
        //
        // Going through world space and back is immune to both: whatever
        // rotation and scale the child carries, InverseTransform* undoes exactly
        // it. The cost is that a rotated part gets a world-aligned box slightly
        // larger than the part itself -- acceptable for buildings, and the
        // Convex fit exists for the cases where it is not (raked legs, booms).
        private static void AddBox(Renderer r, bool surfaceOnly)
        {
            Bounds world = r.bounds;
            Transform t = r.transform;

            // World-space box, expressed in the collider's local axes. Scale is
            // divided out via lossyScale rather than InverseTransformVector so
            // the extent stays axis-aligned instead of being re-rotated.
            Vector3 ls = t.lossyScale;
            Vector3 safeScale = new Vector3(
                Mathf.Approximately(ls.x, 0f) ? 1f : ls.x,
                Mathf.Approximately(ls.y, 0f) ? 1f : ls.y,
                Mathf.Approximately(ls.z, 0f) ? 1f : ls.z);

            Vector3 worldCenter = world.center;
            Vector3 worldSize = world.size;

            if (surfaceOnly)
            {
                // Walkway and deck meshes include their guard rails, so a
                // Mesh_Walk_* is ~2.9 m tall for a surface ~0.1 m thick.
                // Colliding the full bounds would put an invisible ceiling over
                // the walkway and box the player in at chest height. Keep the
                // top face and give it a slab thickness instead. This is done in
                // WORLD Y, which is the only axis that means "up" regardless of
                // how the child is rotated.
                const float SlabThickness = 0.25f;
                float top = world.max.y;
                worldCenter = new Vector3(worldCenter.x,
                                          top - (SlabThickness * 0.5f),
                                          worldCenter.z);
                worldSize = new Vector3(worldSize.x, SlabThickness, worldSize.z);
            }

            var box = r.gameObject.AddComponent<BoxCollider>();

            // Undo rotation for the centre, and scale for the extent.
            Vector3 localCenter = t.InverseTransformPoint(worldCenter);
            Quaternion inv = Quaternion.Inverse(t.rotation);
            Vector3 rotatedSize = inv * worldSize;
            Vector3 localSize = new Vector3(
                Mathf.Abs(rotatedSize.x) / Mathf.Abs(safeScale.x),
                Mathf.Abs(rotatedSize.y) / Mathf.Abs(safeScale.y),
                Mathf.Abs(rotatedSize.z) / Mathf.Abs(safeScale.z));

            box.center = localCenter;
            box.size = localSize;
        }

        // Convex hulls are capped at Unity's 255-face limit by the cooker, so
        // they are cheap; they exist only where a box genuinely lies about the
        // shape (raked legs, crane booms, conveyor runs).
        private static bool AddConvex(Renderer r)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;
            if (!mf.sharedMesh.isReadable) return false;

            var mc = r.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = true;
            return true;
        }

        // -------------------------------------------------------------------
        // LODs
        //
        // These models ship ONE mesh per part and no decimated variants -- the
        // exporters never generated any. A real LOD chain therefore cannot be
        // built here without authoring geometry, which is the art side's call
        // and not something this script should invent.
        //
        // What IS built is a single-level LODGroup acting as a cull group: one
        // LOD0 covering every renderer, culled at 2% screen height. That still
        // buys the thing these buildings most need -- 89-167 renderers dropping
        // out in one test instead of being frustum-culled individually -- and
        // it gives a level designer a real LODGroup to hang decimated meshes off
        // later without rebuilding the prefab.
        //
        // Unity's own mesh LOD generation (generateMeshLods) is left off: it is
        // per-import, it would triple asset size on 300 k-triangle FBXs, and it
        // decimates hard-surface panelling badly.
        // -------------------------------------------------------------------
        private static void BuildLodGroup(GameObject root, Renderer[] renderers)
        {
            LODGroup group = root.GetComponent<LODGroup>();
            if (group == null) group = root.AddComponent<LODGroup>();

            var lods = new LOD[1];
            lods[0] = new LOD(LodCullRatio, renderers);
            group.SetLODs(lods);
            group.RecalculateBounds();
        }

        // -------------------------------------------------------------------
        // Static flags, layers and navigation
        //
        // Layer: everything stays on Default (0), deliberately.
        //
        //   The obvious move is to put decks and walkways on Ground (7). It
        //   buys nothing here and risks harm. Both ground probes in this
        //   project -- Movement.IsGrounded and HoverGroundSensor -- default
        //   their masks to ~0, i.e. every layer, so a deck is already walkable
        //   on Default. And the vehicle-climbs-the-building failure is not a
        //   layer problem: it was ground probes hitting a rider's dynamic
        //   Rigidbody, fixed in the probes themselves by rejecting non-kinematic
        //   attachedRigidbody hits. Moving buildings to Ground would not have
        //   prevented it and would silently change what PerceptionModule treats
        //   as sight-blocking (its fallback mask is Default|Ground|Interior, so
        //   both are occluders either way).
        //
        // Navigation: marked navmesh-static rather than given NavMeshObstacles.
        //   These are immovable scene geometry, so baking them is strictly
        //   cheaper than carving every frame. NavMeshObstacle is for things that
        //   move or appear at runtime, which none of these do.
        //
        // Lightmapping: ContributeGI is deliberately NOT set. generateSecondaryUV
        //   is 0 on all five imports, so there are no lightmap UVs; flagging
        //   ContributeGI without them produces a bake with overlapping charts
        //   and black splotches. Turning UV generation on is a slow, one-way
        //   import change on 300-380 k-triangle meshes, so it is left to whoever
        //   decides these buildings should be lightmapped. See the report.
        //
        // Netcode: no NetworkObject, deliberately.
        //   These are static scene-placed geometry. They never move, never
        //   spawn at runtime and have no replicated state -- every client builds
        //   an identical copy from the same chunk scene, so there is nothing to
        //   synchronise. Adding NetworkObject would cost a spawn message and a
        //   registry entry per building for zero behaviour.
        //
        //   Note the standing trap if that ever changes: an unregistered network
        //   prefab fails ONLY on clients, so a solo playtest as host will never
        //   reveal it. Anything given a NetworkObject here must also be
        //   registered in Assets/DefaultNetworkPrefabs.asset.
        //
        //   SceneTracked is likewise not added -- that is for entities that move
        //   between chunks (vehicles, NPCs). A building belongs to exactly one
        //   chunk for its whole life.
        //
        //   The mining rig's ambient motion is deliberately client-local and
        //   unsynchronised: it is decorative, driven from Time.time, and
        //   randomises its own phase per instance. Replicating a spinning fan
        //   would be pure bandwidth for something no gameplay reads.
        // -------------------------------------------------------------------
        private static void MarkStatic(GameObject root)
        {
            var flags = StaticEditorFlags.BatchingStatic
                      | StaticEditorFlags.OccluderStatic
                      | StaticEditorFlags.OccludeeStatic
                      | StaticEditorFlags.NavigationStatic
                      | StaticEditorFlags.ReflectionProbeStatic;

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
        }

        // -------------------------------------------------------------------
        // Ambient motion (mining_rig_derelict only)
        //
        // Measured: Arm_MiningRig holds 12 bones, each with exactly one mesh
        // parented under it and NOTHING skinned to it -- the FBX imports with
        // zero SkinnedMeshRenderers and no Animator. They are prop handles, not
        // deformation bones.
        //
        // So this binds them to StructureAmbientMotion rather than authoring an
        // Animator and six clips. Reasons, in order: the FBX carries no clips at
        // all, so they would have to be authored from nothing; a clip plays in
        // lockstep across instances while a procedural driver gets per-instance
        // phase free; and two spinning fans plus a panning light do not justify
        // an AnimatorController in a project that already has one per creature.
        // -------------------------------------------------------------------
        private static void WireAmbientMotion(GameObject root,
                                              System.Text.StringBuilder report,
                                              string name)
        {
            var motion = root.GetComponent<StructureAmbientMotion>();
            if (motion == null) motion = root.AddComponent<StructureAmbientMotion>();

            var handles = new List<StructureAmbientMotion.Handle>();
            Transform[] all = root.GetComponentsInChildren<Transform>(true);

            foreach (Transform t in all)
            {
                string n = t.name;
                if (!n.StartsWith("Bone_", System.StringComparison.Ordinal)) continue;

                // Rates are chosen to read as "derelict but not dead": slow
                // enough that nothing looks powered, fast enough to catch the
                // eye. All rotate about local Y, which is the bone's own axis
                // out of Blender.
                if (n.StartsWith("Bone_VentFan", System.StringComparison.Ordinal))
                {
                    // 48 deg/s -- a lazy free-wheeling fan, not a driven one.
                    handles.Add(Handle(t, StructureAmbientMotion.MotionKind.Spin,
                                       48f, 0f, handles.Count * 0.13f));
                }
                else if (n.StartsWith("Bone_FloodSweep", System.StringComparison.Ordinal))
                {
                    // +/-35 deg at 0.055 Hz: one full sweep every ~18 s.
                    handles.Add(Handle(t, StructureAmbientMotion.MotionKind.Sweep,
                                       35f, 0.055f, handles.Count * 0.31f));
                }
                else if (n.StartsWith("Bone_RoofHatch", System.StringComparison.Ordinal))
                {
                    // A hatch banging in the wind: small, slow, offset so it is
                    // never fully shut.
                    handles.Add(Handle(t, StructureAmbientMotion.MotionKind.Rock,
                                       12f, 0.09f, 0.2f));
                }
                else if (n.StartsWith("Bone_StackFlue", System.StringComparison.Ordinal))
                {
                    handles.Add(Handle(t, StructureAmbientMotion.MotionKind.Rock,
                                       4f, 0.13f, 0.5f));
                }
                else if (n.StartsWith("Bone_CableSway", System.StringComparison.Ordinal))
                {
                    // Six cables on staggered phases so the run ripples rather
                    // than swinging as one rigid bar.
                    handles.Add(Handle(t, StructureAmbientMotion.MotionKind.Rock,
                                       6f, 0.17f, handles.Count * 0.17f));
                }
            }

            motion.SetHandles(handles.ToArray());
            report.AppendLine($"    {name}: {handles.Count} ambient bone handles bound");

            if (handles.Count == 0)
            {
                Debug.LogWarning(
                    "No Bone_* transforms found on mining_rig_derelict — the rig " +
                    "was renamed or optimiseGameObjects stripped it. The building " +
                    "is still valid, just motionless.");
            }
        }

        private static StructureAmbientMotion.Handle Handle(
            Transform t, StructureAmbientMotion.MotionKind kind,
            float amount, float frequency, float phase)
        {
            return new StructureAmbientMotion.Handle
            {
                target = t,
                kind = kind,
                axis = Vector3.up,
                amount = amount,
                frequency = frequency,
                phase = phase,
            };
        }

        // -------------------------------------------------------------------
        // Model import
        // -------------------------------------------------------------------

        private static void ConfigureImporter(string fbx)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(fbx);
            if (importer == null) return;

            bool dirty = false;

            // Read/write is required for the convex MeshColliders above: a
            // non-readable mesh cannot be cooked into one, and the failure is a
            // silent null sharedMesh rather than an error.
            if (!importer.isReadable) { importer.isReadable = true; dirty = true; }

            // These arrive at scale 1 and must stay there -- every collider
            // extent in this file is a true metre.
            if (!importer.useFileScale) { importer.useFileScale = true; dirty = true; }
            if (!Mathf.Approximately(importer.globalScale, 1f))
            {
                importer.globalScale = 1f;
                dirty = true;
            }

            // Four of the five have no rig and no clips; the fifth has a rig of
            // prop handles and still no clips. Importing animation on any of
            // them yields nothing but an Animator this script would have to
            // strip. The bones must survive as transforms, though, so the
            // mining rig keeps its hierarchy.
            if (importer.importAnimation) { importer.importAnimation = false; dirty = true; }
            if (importer.optimizeGameObjects)
            {
                importer.optimizeGameObjects = false;
                dirty = true;
            }

            // generateSecondaryUV is left alone on purpose -- see MarkStatic.

            if (dirty)
            {
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            var parts = new List<string>(path.Split('/'));
            string built = parts[0];
            for (int i = 1; i < parts.Count; i++)
            {
                string next = built + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(built, parts[i]);
                built = next;
            }
        }
    }
}
