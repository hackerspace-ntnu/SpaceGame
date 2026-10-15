using System;
using System.Collections.Generic;
using System.Text;
using SpaceGame.Agents.Residents;
using SpaceGame.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using NavMesh = UnityEngine.AI.NavMesh;

namespace SpaceGame.World.NavMeshTools
{
    /// <summary>
    /// Answers "can residents walk everywhere in the settlements?" without playing the game.
    ///
    /// <para>
    /// Reports the world mesh's islands, then for every loaded <see cref="Settlement"/> checks that each
    /// <see cref="SettlementSpot"/>, <see cref="SettlementEntrance"/>, pen gate and <see cref="Ladder"/> end is
    /// on the mesh and reachable by a complete path from the settlement's
    /// <see cref="Settlement.WalkableHeart"/>, and that every narrow terrace stair flight carries mesh
    /// and can be walked end to end. Every failure is listed by hierarchy path and position.
    /// </para>
    ///
    /// <para>
    /// Runs in the editor with the chunk scenes open, or in Play mode. With no NavMesh live it puts
    /// the baked world mesh and its auto links on for the run and takes them off afterwards. In edit
    /// mode the scenes sit where they were saved, not where the streamer would place them; a mismatch
    /// shows up as everything off-mesh.
    /// </para>
    /// </summary>
    public static class SettlementNavMeshAudit
    {
        private const string ShowFindingsPref = "SpaceGame.NavMeshAudit.ShowFindings";

        // Metres from a point to the mesh that still counts as standing on it. The baked mesh sits up
        // to ~0.6 m above the ground (NavMeshSystem.md), so a spot on a floor needs the slack.
        private const float OnMeshRadius = 1.0f;
        // How far to look for the mesh when a point is off it, to say how far off it is.
        private const float NearestMeshRadius = 30f;
        private const float HeartRadius = 4f;
        // How far each side of a pen gate an NPC would stand to open it.
        private const float GateApproach = 1.5f;
        private const string GateNameFragment = "Gate";
        private const string NarrowStairsName = "Terrace_Stairs_Flight_Narrow";
        private const string RampChildName = "Ramp";
        // A flight whose walkable path is longer than this many times its ramp is a detour around it.
        private const float StairDetourFactor = 2f;
        private const float StairMidpointRadius = 0.75f;
        private const int IslandsListed = 12;
        private const int FailuresListedPerSettlement = 60;
        private const float SmallIslandArea = 10f;

        private enum Verdict { Ok, OffMesh, Unreachable }

        private readonly struct Finding
        {
            public readonly Vector3 position;
            public readonly Verdict verdict;
            public readonly string label;

            public Finding(Vector3 position, Verdict verdict, string label)
            {
                this.position = position;
                this.verdict = verdict;
                this.label = label;
            }
        }

        private static readonly List<Finding> Findings = new();

        [MenuItem("World/Streaming/Audit Settlement NavMesh", priority = 20)]
        private static void AuditMenu() => Debug.Log(Run());

        [MenuItem("World/Streaming/Show Settlement NavMesh Audit Findings", priority = 21)]
        private static void ToggleFindings() =>
            EditorPrefs.SetBool(ShowFindingsPref, !EditorPrefs.GetBool(ShowFindingsPref, true));

        [MenuItem("World/Streaming/Show Settlement NavMesh Audit Findings", validate = true)]
        private static bool ToggleFindingsChecked()
        {
            Menu.SetChecked("World/Streaming/Show Settlement NavMesh Audit Findings",
                            EditorPrefs.GetBool(ShowFindingsPref, true));
            return true;
        }

        [InitializeOnLoadMethod]
        private static void RegisterFindingsGizmos() => SceneView.duringSceneGui += DrawFindings;

        /// <summary>The full audit as a log: islands, then one section per loaded settlement.</summary>
        public static string Run()
        {
            Findings.Clear();
            var report = new StringBuilder("[SettlementNavMeshAudit]\n");

            using var mesh = LiveMesh.Acquire(out string error);
            if (error != null) return report.Append(error).ToString();

            report.AppendLine(mesh.Description);

            var graph = NavMeshGraph.FromActiveNavMesh(mesh.WeldTolerance);
            ReportIslands(graph, report);

            var settlements = UnityEngine.Object.FindObjectsByType<Settlement>(FindObjectsSortMode.None);
            Array.Sort(settlements, (a, b) => string.CompareOrdinal(a.name, b.name));
            if (settlements.Length == 0)
                report.AppendLine("No Settlement is loaded. Open the chunk scene that holds one, or enter Play mode.");

            var totals = new Tally();
            foreach (var settlement in settlements) totals.Add(AuditSettlement(settlement, graph, report));

            report.AppendLine($"TOTAL over {settlements.Length} settlement(s): {totals}");
            SceneView.RepaintAll();
            return report.ToString();
        }

        private static void ReportIslands(NavMeshGraph graph, StringBuilder report)
        {
            float total = 0f;
            int small = 0;
            for (int i = 0; i < graph.IslandCount; i++)
            {
                total += graph.IslandArea(i);
                if (graph.IslandArea(i) < SmallIslandArea) small++;
            }

            report.AppendLine($"NavMesh: {graph.TriangleCount} triangles, {graph.IslandCount} islands " +
                              $"({small} under {SmallIslandArea:0} m²), {total:0} m² walkable, " +
                              $"{graph.BoundaryEdges.Count} boundary edges");
            report.AppendLine("Islands only count mesh joined across shared edges; auto links and off-mesh " +
                              "links are not merged in, reachability below does follow them.");

            for (int i = 0; i < Mathf.Min(IslandsListed, graph.IslandCount); i++)
            {
                Vector3 c = graph.IslandCentre(i);
                report.AppendLine($"  island {i}: {graph.IslandArea(i):0} m² around ({c.x:0}, {c.y:0}, {c.z:0})");
            }
        }

        private sealed class Tally
        {
            public int targets, ok, offMesh, unreachable, flights, flightsWithoutMesh, flightsNotWalkable;

            public void Add(Tally other)
            {
                targets += other.targets;
                ok += other.ok;
                offMesh += other.offMesh;
                unreachable += other.unreachable;
                flights += other.flights;
                flightsWithoutMesh += other.flightsWithoutMesh;
                flightsNotWalkable += other.flightsNotWalkable;
            }

            public override string ToString() =>
                $"{targets} targets: {ok} ok, {offMesh} off-mesh, {unreachable} unreachable; " +
                $"{flights} narrow flights: {flightsWithoutMesh} with no mesh mid-ramp, " +
                $"{flightsNotWalkable} not walkable end to end";
        }

        private readonly struct Target
        {
            public readonly string kind;
            public readonly Transform owner;
            public readonly float reach;
            public readonly Vector3[] points;

            public Target(string kind, Transform owner, float reach, params Vector3[] points)
            {
                this.kind = kind;
                this.owner = owner;
                this.reach = reach;
                this.points = points;
            }
        }

        private static Tally AuditSettlement(Settlement settlement, NavMeshGraph graph, StringBuilder report)
        {
            var tally = new Tally();
            Vector3 heart = settlement.WalkableHeart;
            bool heartOnMesh = NavMesh.SamplePosition(heart, out NavMeshHit heartHit, HeartRadius, NavMesh.AllAreas);

            report.AppendLine($"--- {Path(settlement.transform)} at ({settlement.transform.position.x:0}, " +
                              $"{settlement.transform.position.z:0}); walkable heart ({heart.x:0}, {heart.y:0}, {heart.z:0})");
            if (!heartOnMesh)
                report.AppendLine($"  WALKABLE HEART IS NOT ON THE NAVMESH (nothing within {HeartRadius:0} m): " +
                                  "reachability cannot be judged, only on-mesh is.");
            else
                report.AppendLine($"  heart on island {graph.IslandNear(heartHit.position, OnMeshRadius)}");

            var failures = new List<string>();
            foreach (var target in GatherTargets(settlement))
            {
                tally.targets++;
                Verdict verdict = Judge(target, heartOnMesh ? heartHit.position : (Vector3?)null, graph,
                                        out Vector3 where, out string detail);
                switch (verdict)
                {
                    case Verdict.Ok: tally.ok++; continue;
                    case Verdict.OffMesh: tally.offMesh++; break;
                    default: tally.unreachable++; break;
                }

                string line = $"  {verdict.ToString().ToUpperInvariant()} {target.kind} {Path(target.owner)} " +
                              $"({where.x:0.0}, {where.y:0.0}, {where.z:0.0}) {detail}";
                failures.Add(line);
                Findings.Add(new Finding(where, verdict, target.kind));
            }

            AuditNarrowStairs(settlement, graph, tally, failures);

            report.AppendLine($"  {tally}");
            for (int i = 0; i < Mathf.Min(FailuresListedPerSettlement, failures.Count); i++)
                report.AppendLine(failures[i]);
            if (failures.Count > FailuresListedPerSettlement)
                report.AppendLine($"  ... and {failures.Count - FailuresListedPerSettlement} more");
            return tally;
        }

        private static IEnumerable<Target> GatherTargets(Settlement settlement)
        {
            // A spot is stood at the way residents stand at it, so the audit gives it their snap radius.
            float standReach = ResidentTuning.Instance.standSnapRadius;
            foreach (var spot in settlement.GetComponentsInChildren<SettlementSpot>(true))
                yield return new Target("spot", spot.transform, standReach, spot.Position);

            foreach (var entrance in settlement.GetComponentsInChildren<SettlementEntrance>(true))
                yield return new Target("entrance", entrance.transform, ResidentTuning.Instance.doorThresholdSnap, entrance.transform.position);

            foreach (var ladder in settlement.GetComponentsInChildren<Ladder>(true))
            {
                if (!ladder.IsValid) continue;
                yield return new Target("ladder foot", ladder.transform, OnMeshRadius, ladder.Foot);
                yield return new Target("ladder top", ladder.transform, OnMeshRadius, ladder.ExitPoint);
            }

            foreach (var gate in settlement.GetComponentsInChildren<Transform>(true))
            {
                if (!IsGate(gate)) continue;
                Vector3 p = gate.position;
                yield return new Target("pen gate", gate, OnMeshRadius,
                    p + gate.forward * GateApproach, p - gate.forward * GateApproach,
                    p + gate.right * GateApproach, p - gate.right * GateApproach);
            }
        }

        /// <summary>The outermost transform whose name says gate: the parts inside a gate are not gates.</summary>
        private static bool IsGate(Transform t) =>
            NameSaysGate(t) && (t.parent == null || !NameSaysGate(t.parent));

        private static bool NameSaysGate(Transform t) =>
            t.name.IndexOf(GateNameFragment, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// A target with several points (the two sides of a gate) passes when any one does; the verdict
        /// and position reported on failure are the nearest to passing.
        /// </summary>
        private static Verdict Judge(Target target, Vector3? heart, NavMeshGraph graph, out Vector3 where,
                                     out string detail)
        {
            Verdict best = Verdict.OffMesh;
            where = target.points[0];
            detail = "";
            float bestMeshDistance = float.MaxValue;

            foreach (Vector3 point in target.points)
            {
                if (!NavMesh.SamplePosition(point, out NavMeshHit hit, target.reach, NavMesh.AllAreas))
                {
                    float distance = NavMesh.SamplePosition(point, out NavMeshHit far, NearestMeshRadius, NavMesh.AllAreas)
                        ? Vector3.Distance(point, far.position) : float.MaxValue;
                    if (best == Verdict.OffMesh && distance < bestMeshDistance)
                    {
                        bestMeshDistance = distance;
                        where = point;
                        detail = distance == float.MaxValue
                            ? $"no mesh within {NearestMeshRadius:0} m"
                            : $"nearest mesh {distance:0.0} m away";
                    }
                    continue;
                }

                if (heart == null || NavMeshReach.CanWalk(heart.Value, hit.position)) { where = point; return Verdict.Ok; }

                best = Verdict.Unreachable;
                where = hit.position;
                detail = $"on island {graph.IslandNear(hit.position, target.reach)} " +
                         $"({graph.IslandArea(Mathf.Max(0, graph.IslandNear(hit.position, target.reach))):0} m²), " +
                         "no complete path from the heart";
            }

            return best;
        }

        private static void AuditNarrowStairs(Settlement settlement, NavMeshGraph graph, Tally tally,
                                              List<string> failures)
        {
            var path = new NavMeshPath();
            foreach (var flight in settlement.GetComponentsInChildren<Transform>(true))
            {
                if (!flight.name.StartsWith(NarrowStairsName, StringComparison.Ordinal)) continue;

                var ramp = flight.Find(RampChildName) ?? FindDeep(flight, RampChildName);
                var box = ramp != null ? ramp.GetComponent<BoxCollider>() : null;
                if (box == null)
                {
                    failures.Add($"  STAIRS {Path(flight)} has no '{RampChildName}' box to measure");
                    continue;
                }

                tally.flights++;
                Vector3 a = ramp.TransformPoint(box.center + new Vector3(0f, box.size.y * 0.5f, -box.size.z * 0.5f));
                Vector3 b = ramp.TransformPoint(box.center + new Vector3(0f, box.size.y * 0.5f, box.size.z * 0.5f));
                Vector3 top = a.y >= b.y ? a : b, bottom = a.y >= b.y ? b : a;
                Vector3 mid = (top + bottom) * 0.5f;

                if (!NavMesh.SamplePosition(mid, out _, StairMidpointRadius, NavMesh.AllAreas))
                {
                    tally.flightsWithoutMesh++;
                    failures.Add($"  STAIRS no mesh mid-ramp {Path(flight)} ({mid.x:0.0}, {mid.y:0.0}, {mid.z:0.0})");
                    Findings.Add(new Finding(mid, Verdict.OffMesh, "stairs"));
                    continue;
                }

                bool walkable = NavMesh.SamplePosition(top, out NavMeshHit topHit, OnMeshRadius, NavMesh.AllAreas)
                    && NavMesh.SamplePosition(bottom, out NavMeshHit bottomHit, OnMeshRadius, NavMesh.AllAreas)
                    && NavMesh.CalculatePath(topHit.position, bottomHit.position, NavMesh.AllAreas, path)
                    && path.status == NavMeshPathStatus.PathComplete
                    && PathLength(path) <= Vector3.Distance(top, bottom) * StairDetourFactor;
                if (walkable) continue;

                tally.flightsNotWalkable++;
                failures.Add($"  STAIRS mesh mid-ramp but not walkable end to end {Path(flight)} " +
                             $"({mid.x:0.0}, {mid.y:0.0}, {mid.z:0.0})");
                Findings.Add(new Finding(mid, Verdict.Unreachable, "stairs"));
            }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static float PathLength(NavMeshPath path)
        {
            float length = 0f;
            var corners = path.corners;
            for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
            return length;
        }

        private static string Path(Transform t)
        {
            var parts = new Stack<string>();
            for (; t != null; t = t.parent) parts.Push(t.name);
            return string.Join("/", parts);
        }

        private static void DrawFindings(SceneView view)
        {
            if (!EditorPrefs.GetBool(ShowFindingsPref, true) || Findings.Count == 0) return;

            foreach (var finding in Findings)
            {
                Handles.color = finding.verdict == Verdict.OffMesh ? Color.red : new Color(1f, 0.55f, 0f);
                Handles.DrawWireDisc(finding.position, Vector3.up, OnMeshRadius);
                Handles.DrawLine(finding.position, finding.position + Vector3.up * 3f);
                Handles.Label(finding.position + Vector3.up * 3f, $"{finding.verdict}: {finding.label}");
            }
        }

        /// <summary>
        /// A NavMesh to audit against. When none is live (edit mode) this adds the baked world mesh and
        /// its auto links, and removes them on dispose; when one is, it leaves it alone.
        /// </summary>
        private sealed class LiveMesh : IDisposable
        {
            private NavMeshDataInstance data;
            private readonly List<NavMeshLinkInstance> links = new();

            public string Description { get; private set; }
            public float WeldTolerance { get; private set; }

            public static LiveMesh Acquire(out string error)
            {
                var mesh = new LiveMesh();
                var asset = AssetDatabase.LoadAssetAtPath<WorldNavMeshAsset>(WorldNavMeshBaker.AssetPath);
                mesh.WeldTolerance = (asset != null ? asset.linkSettings : new WorldNavMeshLinkSettings()).weldTolerance;
                error = null;

                if (NavMeshGraph.AnyNavMeshLive())
                {
                    mesh.Description = "Using the live NavMesh (Play mode, or another tool put one on).";
                    return mesh;
                }

                if (asset == null || asset.bakedData == null)
                {
                    error = $"No NavMesh is live and {WorldNavMeshBaker.AssetPath} has no baked mesh.";
                    return mesh;
                }

                mesh.data = NavMesh.AddNavMeshData(asset.bakedData);
                asset.AddAutoLinks(mesh.links);
                mesh.Description = $"Using {WorldNavMeshBaker.AssetPath} (baked {asset.bakedAtUtc}) with " +
                                   $"{mesh.links.Count} auto links, put on the NavMesh for this run.";
                return mesh;
            }

            public void Dispose()
            {
                foreach (var link in links) NavMesh.RemoveLink(link);
                links.Clear();
                if (data.valid) data.Remove();
            }
        }
    }
}
