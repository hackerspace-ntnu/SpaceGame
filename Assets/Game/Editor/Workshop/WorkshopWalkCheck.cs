// Can the player walk the workshop's web of rooms? Asked of the geometry, not assumed.
//
// A turning room has one entrance and a wheel in its middle. The layout says which tunnels each room can face and
// how far it turns to face each. For every such mouth this poses the rooms of an open workshop, bakes a NavMesh for
// a body the PLAYER's size over the collision round that tunnel, and asks two things: with the room's entrance
// turned to the tunnel, can the player walk between the wheel and the tunnel; and with it turned away, is the way
// really shut. Then the web is searched over the mouths that passed, from the rooms as they are modelled: a room
// can be entered only by the tunnel its entrance faces, and is turned only from inside.
//
// Nothing is kept: the rooms are put back, no asset is written, the scene is not dirtied.
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public static class WorkshopWalkCheck
    {
        public const string ReportPath = "Temp/workshop_walk_check.txt";

        // The player's capsule (PlayerCharacter.prefab: radius 0.5, 3 m tall), and what a rigidbody capsule rolls over.
        private const float PlayerRadius = 0.5f;
        private const float PlayerHeight = 3f;
        private const float PlayerClimb = 0.3f;
        private const float PlayerSlope = 50f;
        private const float VoxelsPerRadius = 2.5f;

        private const float BoundsMargin = 9f;
        private const float BoundsHeight = 9f;
        private const float SnapAcross = 2.5f;
        private const float SnapLevel = 1.2f;
        private const float FacingSlack = 1f;
        private const float HalfTurn = 180f;

        // The walls: rays level across every tunnel from eye, chest and shin height, this many round and this far.
        // A tunnel is under 60 m long; a ray that meets nothing in that has left the caves.
        private static readonly float[] WallHeights = { 0.6f, 1.5f, 2.6f };
        private const int WallRays = 48;
        private const float WallRayLength = 60f;
        private const float WallStep = 1.5f;
        private const float WallClearOfRoom = 5.2f;
        private const float WallClearOfPit = 19.5f;
        private const float WallHallReach = 6f;
        private const float WallRoomRing = 2.6f;

        // The layout's names for the two places that are not turning rooms.
        private const string FinalId = "Final";
        private const string LaneTunnel = "Lane";
        private const string FinalRoom = "Vault";

        // Prefab space: where a visitor first stands, in the ship lane.
        private static readonly Vector3 LaneStart = new(24f, 0.2f, 0f);

        /// <summary>A turning room as the check needs it.</summary>
        private sealed class Room
        {
            public WorkshopLayout.Wheel layout;
            public WorkshopTurntable table;
            public Quaternion rest;
            public Vector3 axis;

            /// <summary>Per port, in the layout's order: the player walks between the wheel and that tunnel.</summary>
            public bool[] open;
        }

        [MenuItem("Tools/SpaceGame/Workshop/Check The Player Can Walk The Puzzle")]
        private static void RunMenu() => Debug.Log("[Workshop] " + Run());

        /// <summary>Runs the check on the workshop in the open scenes and writes <see cref="ReportPath"/>. Returns its last lines.</summary>
        public static string Run()
        {
            if (EditorApplication.isPlaying) return "exit Play mode first";

            Workshop workshop = Object.FindFirstObjectByType<Workshop>();
            if (workshop == null) return "no workshop in the open scenes: open Assets/Game/Scenes/Tests/WorkshopTest.unity";
            if (!System.IO.File.Exists(WorkshopPrefabBuilder.LayoutPath)) return "no layout file";

            WorkshopLayout layout = WorkshopLayout.Parse(System.IO.File.ReadAllText(WorkshopPrefabBuilder.LayoutPath));
            Transform root = workshop.transform;
            Dictionary<string, Room> rooms = RoomsOf(workshop, layout);
            if (rooms == null) return "the workshop's wheels do not match the layout: rebuild the prefab";

            var report = new StringBuilder();
            int mouths = 0, walked = 0, leaks = 0;
            var hallWalks = new HashSet<string>();      // the tunnels the engine hall can be walked into from
            try
            {
                foreach (WorkshopLayout.Tunnel tunnel in Tunnels(layout))
                {
                    Vector3 hub = root.TransformPoint(WorkshopLayout.V(tunnel.pos));
                    report.AppendLine($"== tunnel {tunnel.id}: {string.Join(", ", tunnel.rooms)}");

                    // Every room on it turned to it: each wheel must reach the tunnel.
                    foreach (Room room in rooms.Values) Turn(room, AwayFromAll(room));
                    foreach (string id in tunnel.rooms)
                        if (rooms.TryGetValue(id, out Room member)) Turn(member, DegreesTo(member, tunnel.id));
                    HashSet<string> reached = Reached(root, layout, rooms, tunnel, hub);

                    // Every room on it turned away: no wheel may reach it.
                    foreach (string id in tunnel.rooms)
                        if (rooms.TryGetValue(id, out Room member)) Turn(member, AwayFromAll(member));
                    HashSet<string> leaked = Reached(root, layout, rooms, tunnel, hub);

                    foreach (string id in tunnel.rooms)
                    {
                        bool isRoom = rooms.TryGetValue(id, out Room member);
                        bool open = reached.Contains(id);
                        if (id == FinalId && open) hallWalks.Add(tunnel.id);
                        // The lane is the one way in that is meant to stay open: with the main room turned away the
                        // lane stair, the gallery over the wall and a pit stair still lead down to its floor.
                        bool overTheWall = tunnel.id == LaneTunnel && leaked.Contains(id);
                        bool shut = !isRoom || overTheWall || !leaked.Contains(id);
                        if (isRoom)
                        {
                            mouths++;
                            if (open) walked++;
                            if (!shut) leaks++;
                            member.open[PortOf(member, tunnel.id)] = open;
                        }
                        string verdict = open ? "walks" : "DOES NOT WALK";
                        if (!shut) verdict += "   LEAKS when turned away";
                        if (overTheWall) verdict += "   (turned away: still reached over the gallery, as meant)";
                        report.AppendLine("   " + id.PadRight(6) + " " + verdict);
                    }
                }
            }
            finally
            {
                foreach (Room room in rooms.Values) room.table.transform.localRotation = room.rest;
                Physics.SyncTransforms();
            }

            int holes, wallRays;
            try
            {
                // With the main room turned to rock, so the one way out of the mountain (the lane) is shut.
                if (rooms.TryGetValue(WorkshopLayout.MainWheelId, out Room main)) Turn(main, AwayFromAll(main));
                Physics.SyncTransforms();
                holes = Holes(root, layout, rooms, report, out wallRays);
            }
            finally
            {
                foreach (Room room in rooms.Values) room.table.transform.localRotation = room.rest;
                Physics.SyncTransforms();
            }

            string solved = Solve(layout, rooms, hallWalks);
            string walls = holes == 0 ? $"The walls hold ({wallRays} rays)." : $"HOLES IN THE WALLS: {holes} of {wallRays} rays get out.";
            string summary = $"{walked} of {mouths} mouths walk for a {PlayerHeight:0} m player, {leaks} leak when turned away. {walls} {solved}";
            report.AppendLine().AppendLine(summary);
            System.IO.File.WriteAllText(ReportPath, report.ToString());
            return summary + $" Full report: {ReportPath}";
        }

        // ── The model's word ─────────────────────────────────────────────────

        private static Dictionary<string, Room> RoomsOf(Workshop workshop, WorkshopLayout layout)
        {
            var wheels = workshop.GetComponentsInChildren<WorkshopWheel>(true).ToDictionary(w => w.Id);
            var rooms = new Dictionary<string, Room>();
            foreach (WorkshopLayout.Wheel entry in layout.wheels)
            {
                if (!wheels.TryGetValue(entry.id, out WorkshopWheel wheel)) return null;

                var table = new SerializedObject(wheel).FindProperty("room").objectReferenceValue as WorkshopTurntable;
                if (table == null) return null;

                rooms[entry.id] = new Room
                {
                    layout = entry,
                    table = table,
                    rest = table.transform.localRotation,
                    axis = new SerializedObject(table).FindProperty("localAxis").vector3Value,
                    open = new bool[entry.ports.Length],
                };
            }
            return rooms;
        }

        /// <summary>The layout's tunnels, and the ship lane as one more: only the main room has a mouth on it.</summary>
        private static IEnumerable<WorkshopLayout.Tunnel> Tunnels(WorkshopLayout layout)
        {
            foreach (WorkshopLayout.Tunnel tunnel in layout.tunnels) yield return tunnel;
            yield return new WorkshopLayout.Tunnel
            {
                id = LaneTunnel, pos = new[] { LaneStart.x, LaneStart.y, LaneStart.z }, rooms = new[] { WorkshopLayout.MainWheelId },
            };
        }

        private static int PortOf(Room room, string tunnel) => System.Array.FindIndex(room.layout.ports, p => p.tunnel == tunnel);

        private static float DegreesTo(Room room, string tunnel) => room.layout.ports[PortOf(room, tunnel)].degrees;

        /// <summary>The angle furthest from every tunnel the room can face: its entrance on blank rock.</summary>
        private static float AwayFromAll(Room room)
        {
            float best = 0f, clear = -1f;
            for (float degrees = 0f; degrees < 2f * HalfTurn; degrees += FacingSlack)
            {
                float nearest = room.layout.ports.Min(p => Mathf.Abs(Mathf.DeltaAngle(degrees, p.degrees)));
                if (nearest <= clear) continue;
                (best, clear) = (degrees, nearest);
            }
            return best;
        }

        private static void Turn(Room room, float degrees) =>
            room.table.transform.localRotation = room.rest * Quaternion.AngleAxis(degrees, room.axis);

        // ── What holds a player in ───────────────────────────────────────────

        /// <summary>
        /// Rays level across the tunnels and the small rooms, against what a player bumps into. One that meets
        /// nothing has found a wall with no collider: the model draws clay there and the player walks through it.
        /// </summary>
        private static int Holes(Transform root, WorkshopLayout layout, Dictionary<string, Room> rooms, StringBuilder report, out int cast)
        {
            var eyes = new List<(string where, Vector3 at)>();
            foreach (WorkshopLayout.Tunnel tunnel in layout.tunnels)
            {
                Vector3 hub = WorkshopLayout.V(tunnel.pos);
                eyes.Add((tunnel.id, hub));
                foreach (string id in tunnel.rooms)
                {
                    bool isRoom = rooms.TryGetValue(id, out Room member);
                    Vector3 end = isRoom ? WorkshopLayout.V(member.layout.centre) : StandOf(layout, rooms, id);
                    float length = Vector3.Distance(hub, end);
                    float walkable = id == FinalId ? Mathf.Min(length, WallHallReach)
                        : length - (id == WorkshopLayout.MainWheelId ? WallClearOfPit : WallClearOfRoom);
                    for (float along = WallStep; along < walkable; along += WallStep)
                        eyes.Add(($"{tunnel.id} to {id}", Vector3.Lerp(hub, end, along / length)));
                }
            }
            foreach (Room room in rooms.Values)
            {
                if (room.layout.id == WorkshopLayout.MainWheelId) continue;
                Vector3 centre = WorkshopLayout.V(room.layout.centre);
                for (int k = 0; k < 6; k++)
                    eyes.Add((room.layout.id, centre + Quaternion.Euler(0f, k * 60f, 0f) * Vector3.forward * WallRoomRing));
            }

            int holes = 0;
            cast = 0;
            report.AppendLine("== walls");
            foreach ((string where, Vector3 local) in eyes)
            {
                Vector3 foot = root.TransformPoint(new Vector3(local.x, 0f, local.z));
                int here = 0;
                Vector3 worst = Vector3.zero;
                foreach (float height in WallHeights)
                {
                    Vector3 eye = foot + (Vector3.up * height);
                    // An eye that is not in open air over a floor is the check's own mistake, not a hole.
                    if (Physics.CheckSphere(eye, 0.3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (!Physics.Raycast(eye, Vector3.down, height + 1f, ~0, QueryTriggerInteraction.Ignore)) continue;

                    for (int k = 0; k < WallRays; k++)
                    {
                        Vector3 way = Quaternion.Euler(0f, k * 360f / WallRays, 0f) * Vector3.forward;
                        cast++;
                        if (Physics.Raycast(eye, way, WallRayLength, ~0, QueryTriggerInteraction.Ignore)) continue;
                        here++;
                        worst = way;
                    }
                }
                if (here == 0) continue;
                holes += here;
                Vector3 at = root.InverseTransformPoint(foot);
                report.AppendLine("   " + where.PadRight(14) + $" at ({at.x:0.0}, {at.z:0.0}): {here} rays out, one toward ({worst.x:0.00}, {worst.z:0.00})");
            }
            if (holes == 0) report.AppendLine("   none");
            return holes;
        }

        // ── What can be walked ───────────────────────────────────────────────

        /// <summary>Which of a tunnel's rooms the player can walk to from the tunnel, as the rooms now stand.</summary>
        private static HashSet<string> Reached(Transform root, WorkshopLayout layout, Dictionary<string, Room> rooms,
                                               WorkshopLayout.Tunnel tunnel, Vector3 hub)
        {
            Physics.SyncTransforms();

            var stands = new Dictionary<string, Vector3>();
            var bounds = new Bounds(hub, Vector3.zero);
            foreach (string id in tunnel.rooms)
            {
                Vector3 stand = root.TransformPoint(StandOf(layout, rooms, id));
                stands[id] = stand;
                bounds.Encapsulate(stand);
            }
            // The main room is as wide as the whole pit, and the way from the lane to its wheel may go over the wall.
            if (tunnel.rooms.Contains(WorkshopLayout.MainWheelId)) bounds.Encapsulate(root.position);
            bounds.Expand(new Vector3(2f * BoundsMargin, 0f, 2f * BoundsMargin));
            bounds.SetMinMax(new Vector3(bounds.min.x, root.position.y - 1f, bounds.min.z),
                             new Vector3(bounds.max.x, root.position.y + BoundsHeight, bounds.max.z));

            GearDoor[] gearDoors = root.GetComponentsInChildren<GearDoor>(true);
            var sources = new List<NavMeshBuildSource>();
            foreach (Collider collider in Physics.OverlapBox(bounds.center, bounds.extents))
            {
                // Triggers and whatever walks are out; a turned wall is IN (doorsShut): that is the question. A gear
                // door's leaf is out: its lock is a puzzle of its own (GearDoor.md), walked here as if it stood open.
                if (!NavMeshSources.IsBakeable(collider, ~0, doorsShut: true)) continue;
                if (System.Array.Exists(gearDoors, door => door.IsLeafPart(collider.transform))) continue;
                if (!collider.transform.IsChildOf(root) && collider.GetComponentInParent<Rigidbody>() != null) continue;
                if (NavMeshSources.TryColliderToSource(collider, out NavMeshBuildSource source)) sources.Add(source);
            }

            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = PlayerRadius;
            settings.agentHeight = PlayerHeight;
            settings.agentClimb = PlayerClimb;
            settings.agentSlope = PlayerSlope;
            settings.overrideVoxelSize = true;
            settings.voxelSize = PlayerRadius / VoxelsPerRadius;
            settings.minRegionArea = 0.5f;

            var reached = new HashSet<string>();
            NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            NavMeshDataInstance instance = NavMesh.AddNavMeshData(data);
            try
            {
                if (!Snap(hub, out Vector3 from)) return reached;
                foreach ((string id, Vector3 stand) in stands)
                    if (Snap(stand, out Vector3 to) && NavMeshReach.CanWalk(from, to)) reached.Add(id);
            }
            finally
            {
                instance.Remove();
                Object.DestroyImmediate(data);
            }
            return reached;
        }

        /// <summary>Where a room is entered to: beside its wheel; for the engine hall, its floor.</summary>
        private static Vector3 StandOf(WorkshopLayout layout, Dictionary<string, Room> rooms, string id)
        {
            if (rooms.TryGetValue(id, out Room room)) return WorkshopLayout.V(room.layout.stance);

            WorkshopLayout.Room hall = layout.rooms.First(r => r.name == FinalRoom);
            return WorkshopLayout.V(hall.pos);
        }

        /// <summary>The mesh at a point: across by <see cref="SnapAcross"/>, never onto another level.</summary>
        private static bool Snap(Vector3 point, out Vector3 on)
        {
            on = point;
            for (float reach = 0.5f; reach <= SnapAcross + SnapLevel; reach += 0.5f)
            {
                if (!NavMesh.SamplePosition(point, out NavMeshHit hit, reach, NavMesh.AllAreas)) continue;
                Vector3 off = hit.position - point;
                if (Mathf.Abs(off.y) > SnapLevel || new Vector2(off.x, off.z).magnitude > SnapAcross) continue;
                on = hit.position;
                return true;
            }
            return false;
        }

        // ── The web ──────────────────────────────────────────────────────────

        /// <summary>
        /// One player, from the lane, with every room as modelled: its entrance on the port at zero degrees. Standing
        /// in a room they may turn it to any tunnel it can face; they walk a tunnel into any room whose entrance is
        /// on it. Breadth first, so the route named is the one through the fewest rooms.
        /// </summary>
        private static string Solve(WorkshopLayout layout, Dictionary<string, Room> rooms, HashSet<string> hallWalks)
        {
            string[] ids = rooms.Keys.OrderBy(id => id).ToArray();
            var members = Tunnels(layout).ToDictionary(t => t.id, t => t.rooms);
            string[] start = ids.Select(id => rooms[id].layout.ports
                .OrderBy(p => Mathf.Abs(Mathf.DeltaAngle(0f, p.degrees))).First().tunnel).ToArray();

            string Key(string at, string[] facing) => at + "|" + string.Join(",", facing);

            var queue = new Queue<(string at, string[] facing, string route)>();
            var seen = new HashSet<string>();
            var entered = new HashSet<string>();
            string shortest = null;

            // In the lane, the main room open to it: walking in is the first move.
            queue.Enqueue((LaneTunnel, start, string.Empty));
            seen.Add(Key(LaneTunnel, start));

            while (queue.Count > 0)
            {
                (string at, string[] facing, string route) = queue.Dequeue();
                bool inRoom = rooms.ContainsKey(at);

                // From a tunnel there is one tunnel to be in; from a room, any it can face and walk out to.
                IEnumerable<string> ways = inRoom
                    ? rooms[at].layout.ports.Where((p, i) => rooms[at].open[i]).Select(p => p.tunnel)
                    : new[] { at };

                foreach (string tunnel in ways)
                {
                    string[] next = facing;
                    if (inRoom)
                    {
                        next = (string[])facing.Clone();
                        next[System.Array.IndexOf(ids, at)] = tunnel;
                    }

                    foreach (string other in members[tunnel])
                    {
                        if (other == at) continue;
                        if (other == FinalId)
                        {
                            // A hall you can see but not walk into is not reached: its tunnel once pinched shut.
                            if (hallWalks.Contains(tunnel)) shortest ??= route + " > engine hall";
                            continue;
                        }

                        int slot = System.Array.IndexOf(ids, other);
                        if (next[slot] != tunnel || !rooms[other].open[PortOf(rooms[other], tunnel)]) continue;
                        if (!seen.Add(Key(other, next))) continue;

                        entered.Add(other);
                        queue.Enqueue((other, next, route.Length == 0 ? other : route + " > " + other));
                    }
                }
            }

            string[] missed = ids.Where(id => !entered.Contains(id)).ToArray();
            string all = missed.Length == 0
                ? $"All {ids.Length} rooms can be entered"
                : $"NEVER ENTERED: {string.Join(", ", missed)}";
            return shortest != null
                ? $"SOLVABLE on foot: {shortest}. {all}."
                : $"UNSOLVABLE for a walking player: the engine hall is never reached. {all}.";
        }
    }
}
