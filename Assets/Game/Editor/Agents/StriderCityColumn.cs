// The cards the walking city's vehicles are dealt into its column with (ColumnDeal).
//
// Followers take slots in spawn order, so a template listed by kind marched as a parade (the user,
// 2026-10-04) and an order shuffled once here was the same column in every world (the user, 2026-10-05:
// "like drawing from a card deck"). So the order is dealt at spawn from the group's per-world seed, and
// this measures what the deal needs to know of each vehicle without loading a prefab at runtime:
//
//   - every carrier (a house or a barge) rides in the first CityCarrierSlots, the ground the city levels
//     at a stop (StriderCityBuilder.CityLevelGround is sized to them);
//   - no house, crawler, crab or barge sits beside its own kind in a row or nose to tail behind it in a
//     lane — the monowheel scouts (8 of 17) go anywhere, since spacing out that many would itself make
//     a pattern;
//   - every barge clears every other member at their slots (a barge is longer than a row is deep),
//     measured from each prefab's renderers.
//
// StriderCityTemplateTests re-measures the written cards and deals the column for many seeds.
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public static class StriderCityColumn
    {
        /// <summary>The card WireStriderCity writes for a city member built from <paramref name="prefab"/>.</summary>
        public static ColumnCard Card(GameObject prefab, bool shuffled) => new ColumnCard
        {
            shuffled = shuffled,
            // Empty, as the template serializes a null, for the kindless scouts.
            kind = RosterAuthoring.CityKind(prefab) ?? string.Empty,
            withinFirstSlots = RosterAuthoring.IsCityCarrier(prefab) ? RosterAuthoring.CityCarrierSlots : 0,
            keepsClear = RosterAuthoring.IsCityBarge(prefab),
            footprint = Footprint(prefab),
            slotTolerance = new SerializedObject(prefab.GetComponent<FormationModule>()).FindProperty("slotTolerance").floatValue,
        };

        /// <summary>
        /// A prefab's renderers seen from above, in its own space: x right, y forward. Particle systems
        /// are left out — a dust cloud is not the hull, and its unsimulated bounds say nothing.
        /// </summary>
        private static Rect Footprint(GameObject prefab)
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
    }
}
