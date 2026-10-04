// The order the walking city's vehicles take their formation slots in.
//
// Followers take slots in spawn order, which is template order, so a template listed by kind (the
// houses, then the crawlers, the crabs, the barges and the scouts in blocks) marched as a parade of
// matching pairs. The user wanted it mixed (2026-10-04). So the order is a seeded shuffle, the first
// one found that keeps three rules:
//
//   - every carrier (a house or a barge) rides in the first CarrierSlots, the ground the city levels
//     at a stop (StriderCityBuilder.CityLevelGround is sized to them);
//   - no member with a kind sits beside its own kind in a row or nose to tail behind it in a lane —
//     kindless members (the monowheel scouts, 8 of 17) go anywhere, since spacing out that many
//     would itself make a pattern;
//   - every barge clears every other member at their slots (a barge is longer than a row is deep).
//
// The seed is a constant, so a re-run writes the same column. StriderCityTemplateTests re-checks the
// written template with the same measures.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public static class StriderCityColumn
    {
        private const int ShuffleSeed = 20261004;

        /// <summary>Shuffles tried before giving up; a valid column turns up within a few hundred.</summary>
        private const int ShuffleAttempts = 20000;

        /// <summary>One member of the column at its slot, seen from above: x right of the leader, y ahead of it.</summary>
        public readonly struct Placed
        {
            public Placed(string name, bool barge, Rect area, float tolerance)
            {
                Name = name;
                Barge = barge;
                Area = area;
                Tolerance = tolerance;
            }

            public string Name { get; }
            public bool Barge { get; }
            public Rect Area { get; }
            /// <summary>How far off its slot the member may park (its FormationModule's slotTolerance).</summary>
            public float Tolerance { get; }
        }

        /// <summary>
        /// The followers in the order they should take their slots behind <paramref name="leader"/>.
        /// <paramref name="kind"/> names what may not sit beside or behind its own kind (null: anywhere).
        /// False, with an error logged, when no shuffle keeps the rules.
        /// </summary>
        public static bool TryOrder(GameObject leader, IReadOnlyList<GameObject> followers, Func<GameObject, string> kind,
                                    Func<GameObject, bool> isCarrier, Func<GameObject, bool> isBarge,
                                    FormationShape shape, int carrierSlots, out List<GameObject> order)
        {
            var measured = new Dictionary<GameObject, (Rect footprint, float tolerance)>();
            foreach (GameObject prefab in followers.Append(leader).Distinct())
                measured[prefab] = (Footprint(prefab), SlotTolerance(prefab));

            var random = new System.Random(ShuffleSeed);
            order = followers.ToList();
            for (int attempt = 0; attempt < ShuffleAttempts; attempt++)
            {
                Shuffle(order, random);
                if (order.Select((prefab, slot) => !isCarrier(prefab) || slot < carrierSlots).All(ok => ok)
                    && FirstSameKindNeighbours(order.Select(kind).ToList(), shape.Lanes) == null
                    && FirstOverlap(Place(leader, order, isBarge, measured, shape), shape) == null)
                    return true;
            }

            Debug.LogError($"[StriderCityColumn] No order of the city's {followers.Count} followers in {ShuffleAttempts} shuffles " +
                           $"keeps its carriers in the first {carrierSlots} slots, no kind beside or behind its own, and every " +
                           "barge clear of its neighbours. Widen the formation or raise the carrier slots.");
            order = null;
            return false;
        }

        /// <summary>
        /// The first pair of followers of one kind side by side in a row or one behind the other in a
        /// lane, as "a and b", or null. <paramref name="kinds"/> lists each follower's kind in slot order
        /// (null: no kind); slot i is row i / lanes, lane i % lanes, as FormationMath.SlotOffset lays it out.
        /// </summary>
        public static string FirstSameKindNeighbours(IReadOnlyList<string> kinds, int lanes)
        {
            for (int slot = 0; slot < kinds.Count; slot++)
            {
                if (kinds[slot] == null) continue;
                bool besideInRow = slot % lanes > 0 && kinds[slot - 1] == kinds[slot];
                bool behindInLane = slot >= lanes && kinds[slot - lanes] == kinds[slot];
                if (besideInRow || behindInLane)
                    return $"{kinds[slot]} in slot {slot} and slot {(besideInRow ? slot - 1 : slot - lanes)}";
            }
            return null;
        }

        /// <summary>The leader at the origin and each follower at its slot, with its footprint round it.</summary>
        public static List<Placed> Place(GameObject leader, IReadOnlyList<GameObject> followers, Func<GameObject, bool> isBarge,
                                         FormationShape shape)
        {
            var measured = followers.Append(leader).Distinct().ToDictionary(p => p, p => (Footprint(p), SlotTolerance(p)));
            return Place(leader, followers, isBarge, measured, shape);
        }

        private static List<Placed> Place(GameObject leader, IReadOnlyList<GameObject> followers, Func<GameObject, bool> isBarge,
                                          Dictionary<GameObject, (Rect footprint, float tolerance)> measured, FormationShape shape)
        {
            var column = new List<Placed> { At(leader, "leader", Vector2.zero) };
            for (int slot = 0; slot < followers.Count; slot++)
                // SlotOffset's y is metres behind; the column's is metres ahead.
                column.Add(At(followers[slot], $"slot {slot}", FormationMath.SlotOffset(slot, shape) * new Vector2(1f, -1f)));
            return column;

            Placed At(GameObject prefab, string where, Vector2 slot)
            {
                (Rect footprint, float tolerance) = measured[prefab];
                return new Placed($"{prefab.name} ({where})", isBarge(prefab),
                                  new Rect(footprint.position + slot, footprint.size), tolerance);
            }
        }

        /// <summary>
        /// The first barge and member that can touch, as a description, or null. Two members clear
        /// each other when they are apart side to side or nose to tail by their jitter and drift at
        /// their worst, or by both parked as far off their slots as their slotTolerance lets them.
        /// Only pairs with a barge are judged; the rest of the column is the walking spike's.
        /// </summary>
        public static string FirstOverlap(IReadOnlyList<Placed> column, FormationShape shape)
        {
            float sideSlop = 2f * (shape.LateralJitter + shape.DriftAmplitude);
            float lengthSlop = 2f * (shape.LongitudinalJitter + shape.DriftAmplitude);
            for (int a = 0; a < column.Count; a++)
            for (int b = a + 1; b < column.Count; b++)
            {
                if (!column[a].Barge && !column[b].Barge) continue;
                Rect p = column[a].Area, q = column[b].Area;
                float side = Mathf.Max(p.xMin - q.xMax, q.xMin - p.xMax);
                float length = Mathf.Max(p.yMin - q.yMax, q.yMin - p.yMax);
                float parked = column[a].Tolerance + column[b].Tolerance;
                if (side < Mathf.Max(sideSlop, parked) && length < Mathf.Max(lengthSlop, parked))
                    return $"{column[a].Name} and {column[b].Name} overlap: {side:F1} m apart side to side, {length:F1} m nose to tail";
            }
            return null;
        }

        /// <summary>
        /// A prefab's renderers seen from above, in its own space: x right, y forward. Particle systems
        /// are left out — a dust cloud is not the hull, and its unsimulated bounds say nothing.
        /// </summary>
        public static Rect Footprint(GameObject prefab)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(prefab));
            try
            {
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                foreach (Renderer renderer in contents.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer is ParticleSystemRenderer) continue;
                    Bounds world = renderer.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = new Vector3((i & 1) == 0 ? world.min.x : world.max.x, (i & 2) == 0 ? world.min.y : world.max.y,
                                                 (i & 4) == 0 ? world.min.z : world.max.z);
                        Vector3 p = contents.transform.InverseTransformPoint(corner);
                        minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                        minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                    }
                }
                return Rect.MinMaxRect(minX, minZ, maxX, maxZ);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        private static float SlotTolerance(GameObject prefab) =>
            new SerializedObject(prefab.GetComponent<FormationModule>()).FindProperty("slotTolerance").floatValue;

        private static void Shuffle(List<GameObject> list, System.Random random)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
