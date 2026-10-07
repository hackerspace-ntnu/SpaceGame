// Wires a TrackBelts onto a tracked hull whose links are separate meshes baked in place round each
// loop (the dune barges' Mesh_TrackAssembly_<Unit>_Link_<Kind>_<nnn>) and whose wheels hang off spin
// bones (Bone_Wheel_<Unit><L|R>_<Kind><n>, dune_barge_rig.py). Everything is read from the model:
//
//   * a belt per track unit and side of the hull, the side judged by where the part sits, not by the
//     L/R in a bone's name (the rig names sides from Blender's view);
//   * the slots in link-number order, checked to close into one even loop, and turned so the ground
//     run heads for the rear -- forward motion then advances the belt;
//   * each slot's frame from its link's mesh centre and the loop's tangent there;
//   * each wheel's radius from its own meshes, its axle the hull's x; a spin bone that is no disc (a
//     roller on its bracket) is left still.
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public static class TrackBeltWiring
    {
        private static readonly Regex Link = new Regex(@"^Mesh_TrackAssembly_(?<unit>[A-Za-z]+)_Link_[A-Za-z]+_(?<n>\d+)");
        private static readonly Regex Wheel = new Regex(@"^Bone_Wheel_(?<unit>[A-Za-z]+?)[LR]_");
        /// <summary>A gap between two slots more than this many times the median is a break in the loop, not one belt.</summary>
        private const float MaxGapRatio = 1.6f;
        /// <summary>A spin bone whose parts reach less than this fraction as far one way across the axle as the other is no disc.</summary>
        private const float MinRoundness = 0.85f;

        /// <summary>
        /// Adds TrackBelts to <paramref name="root"/> (at the origin, unrotated), each belt driven by the
        /// contacts named <c>TrackContact_&lt;Unit&gt;_&lt;L|R&gt;_Front/Rear</c> among <paramref name="contacts"/>.
        /// Returns null, having logged why, when a track does not read as one closed loop.
        /// </summary>
        public static TrackBelts AddTrackBelts(GameObject root, Transform[] contacts)
        {
            var belts = new Dictionary<string, TrackBelts.Belt>();
            foreach (IGrouping<string, MeshFilter> track in LinksByTrack(root))
            {
                TrackBelts.Belt belt = BuildBelt(root, track.Key, track.ToList(), contacts);
                if (belt == null) return null;
                belts.Add(track.Key, belt);
            }
            if (belts.Count == 0)
            {
                Debug.LogError($"[TrackBeltWiring] {root.name}: no Mesh_TrackAssembly_*_Link_* meshes; nothing to animate.");
                return null;
            }

            AddWheels(root, belts);
            var component = root.AddComponent<TrackBelts>();
            component.Configure(belts.Values.ToArray());
            return component;
        }

        private static string Side(GameObject root, Vector3 world) => root.transform.InverseTransformPoint(world).x < 0f ? "L" : "R";

        private static Vector3 MeshCentre(MeshFilter filter) => filter.transform.TransformPoint(filter.sharedMesh.bounds.center);

        private static IEnumerable<IGrouping<string, MeshFilter>> LinksByTrack(GameObject root) =>
            root.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null && Link.IsMatch(f.name))
                .GroupBy(f => $"{Link.Match(f.name).Groups["unit"].Value}_{Side(root, MeshCentre(f))}")
                .OrderBy(g => g.Key);

        private static TrackBelts.Belt BuildBelt(GameObject root, string key, List<MeshFilter> filters, Transform[] contacts)
        {
            Transform parent = filters[0].transform.parent;
            if (filters.Any(f => f.transform.parent != parent))
            {
                Debug.LogError($"[TrackBeltWiring] {root.name}: track {key}'s links hang off more than one bone.");
                return null;
            }
            Transform front = contacts.FirstOrDefault(c => c.name == $"TrackContact_{key}_Front");
            Transform rear = contacts.FirstOrDefault(c => c.name == $"TrackContact_{key}_Rear");
            if (front == null || rear == null)
            {
                Debug.LogError($"[TrackBeltWiring] {root.name}: no TrackContact_{key}_Front/Rear to read track {key}'s speed from.");
                return null;
            }

            filters = filters.OrderBy(f => int.Parse(Link.Match(f.name).Groups["n"].Value)).ToList();
            List<Vector3> world = filters.Select(MeshCentre).ToList();
            if (GroundRunHeadsForward(root, world))
            {
                filters.Reverse();
                world.Reverse();
            }
            int count = filters.Count;
            float[] gaps = Enumerable.Range(0, count).Select(i => Vector3.Distance(world[i], world[(i + 1) % count])).ToArray();
            float median = gaps.OrderBy(g => g).ElementAt(count / 2);
            int broken = System.Array.FindIndex(gaps, g => g > median * MaxGapRatio);
            if (broken >= 0)
            {
                Debug.LogError($"[TrackBeltWiring] {root.name}: track {key} breaks after {filters[broken].name} " +
                               $"({gaps[broken]:F2} m against a {median:F2} m pitch); were its links renumbered?");
                return null;
            }

            Vector3 axle = parent.InverseTransformDirection(root.transform.right).normalized;
            var slots = world.Select(parent.InverseTransformPoint).ToArray();
            var frames = new Quaternion[count];
            for (int k = 0; k < count; k++)
            {
                Vector3 tangent = (slots[(k + 1) % count] - slots[(k + count - 1) % count]).normalized;
                frames[k] = Quaternion.LookRotation(tangent, Vector3.Cross(tangent, axle));
            }

            var belt = new TrackBelts.Belt
            {
                left = key.EndsWith("_L"),
                frontContact = front,
                rearContact = rear,
                pitch = gaps.Sum() / count,
                slotPositions = slots,
                slotRotations = frames,
                links = filters.Select(f => f.transform).ToArray(),
                linkOffsetPositions = new Vector3[count],
                linkOffsetRotations = new Quaternion[count],
            };
            for (int i = 0; i < count; i++)
            {
                Quaternion toSlot = Quaternion.Inverse(frames[i]);
                belt.linkOffsetPositions[i] = toSlot * (belt.links[i].localPosition - slots[i]);
                belt.linkOffsetRotations[i] = toSlot * belt.links[i].localRotation;
            }
            return belt;
        }

        /// <summary>
        /// The loop's winding seen from the hull's +x side (z along, y up): a ground run heading for the
        /// rear winds clockwise, a negative shoelace area.
        /// </summary>
        private static bool GroundRunHeadsForward(GameObject root, List<Vector3> world)
        {
            float area = 0f;
            for (int i = 0; i < world.Count; i++)
            {
                Vector3 a = root.transform.InverseTransformPoint(world[i]);
                Vector3 b = root.transform.InverseTransformPoint(world[(i + 1) % world.Count]);
                area += a.z * b.y - b.z * a.y;
            }
            return area > 0f;
        }

        /// <summary>Each spin bone goes to the belt of its unit on its side, with its rest pose, axle and radius.</summary>
        private static void AddWheels(GameObject root, Dictionary<string, TrackBelts.Belt> belts)
        {
            var wheels = belts.Keys.ToDictionary(k => k, k => new List<Transform>());
            foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
            {
                Match match = Wheel.Match(bone.name);
                if (!match.Success) continue;
                string key = $"{match.Groups["unit"].Value}_{Side(root, bone.position)}";
                if (!wheels.TryGetValue(key, out List<Transform> track))
                {
                    Debug.LogWarning($"[TrackBeltWiring] {root.name}: wheel {bone.name} has no track {key} to turn with; it stays still.");
                    continue;
                }
                // A roller on its bracket (the return rollers) would swing the bracket round like a lever.
                if (IsRound(root, bone)) track.Add(bone);
            }

            foreach (KeyValuePair<string, TrackBelts.Belt> pair in belts)
            {
                TrackBelts.Belt belt = pair.Value;
                List<Transform> bones = wheels[pair.Key];
                belt.wheels = bones.ToArray();
                belt.wheelRestRotations = bones.Select(w => w.localRotation).ToArray();
                belt.wheelAxles = bones.Select(w => w.parent.InverseTransformDirection(root.transform.right).normalized).ToArray();
                belt.wheelRadii = bones.Select(w => Radius(root, w)).ToArray();
            }
        }

        /// <summary>The farthest any of the wheel's own vertices sits from its axle, in world metres.</summary>
        private static float Radius(GameObject root, Transform wheel) =>
            WheelVertices(root, wheel).Max(v => Vector3.ProjectOnPlane(v, root.transform.right).magnitude);

        /// <summary>Whether the wheel's parts reach about as far up and down as fore and aft of its axle: a disc, not a bracket.</summary>
        private static bool IsRound(GameObject root, Transform wheel)
        {
            List<Vector3> vertices = WheelVertices(root, wheel).ToList();
            if (vertices.Count == 0) return false;
            float high = vertices.Max(v => Mathf.Abs(Vector3.Dot(v, root.transform.up)));
            float along = vertices.Max(v => Mathf.Abs(Vector3.Dot(v, root.transform.forward)));
            return Mathf.Min(high, along) >= MinRoundness * Mathf.Max(high, along);
        }

        /// <summary>The wheel's own vertices in world space, relative to its pivot.</summary>
        private static IEnumerable<Vector3> WheelVertices(GameObject root, Transform wheel) =>
            wheel.GetComponentsInChildren<MeshFilter>(true)
                 .Where(f => f.sharedMesh != null)
                 .SelectMany(f => f.sharedMesh.vertices.Select(v => f.transform.TransformPoint(v) - wheel.position));
    }
}
