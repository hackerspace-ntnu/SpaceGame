// The one field a Settlement component carries. Buildings/decorations/characters are simple
// prefab+count lists (no min/max range) so the same asset behaves identically wherever it's used —
// determinism comes from the placing GameObject's position, not from anything stored here, so this
// config can be dropped onto many settlements and still give each one its own layout.
using System;
using UnityEngine;

namespace SpaceGame.World
{
    [CreateAssetMenu(fileName = "SettlementConfig", menuName = "Settlement/Settlement Config")]
    public class SettlementConfig : ScriptableObject
    {
        [Serializable]
        public struct SpawnEntry
        {
            public GameObject prefab;
            [Min(0)] public int count;
        }

        [Header("Buildings")]
        public SpawnEntry[] buildings = Array.Empty<SpawnEntry>();

        [Header("Decorations")]
        public SpawnEntry[] decorations = Array.Empty<SpawnEntry>();

        [Header("Characters")]
        public SpawnEntry[] characters = Array.Empty<SpawnEntry>();

        [Header("Layout")]
        [Tooltip("How far from the settlement's center buildings/decorations/characters may scatter.")]
        [Min(1f)] public float radius = 60f;
        [Tooltip("Buildings only: extra gap enforced between any two buildings, on top of their own mesh footprints. Decorations and characters use their own flat spacing below instead, since they don't need building-sized clearance.")]
        [Min(0f)] public float minSpacing = 4f;
        [Tooltip("Flat gap kept between two decorations (e.g. tents), regardless of their mesh size. Decorations only avoid each other this way, plus real building geometry -- never a building's footprint circle -- so a handful of large canopies can still cluster into a small camp.")]
        [Min(0f)] public float decorationSpacing = 1.5f;
        [Tooltip("Flat gap kept between two characters, regardless of their mesh size. Characters only avoid each other this way, plus real building geometry -- never a building's or decoration's footprint circle -- so a crowd can stand close together.")]
        [Min(0f)] public float characterSpacing = 0.6f;

        [Header("Terrain Shaping")]
        [Tooltip("Flat margin kept beyond each building's own footprint before the ground is allowed to blend away.")]
        [Min(0f)] public float flattenPadding = 1.5f;
        [Tooltip("Distance over which flattened ground blends back into the natural terrain.")]
        [Min(0.1f)] public float blendDistance = 6f;
        [Tooltip("Max height (metres) of the gentle seeded rises/dips added away from buildings.")]
        [Min(0f)] public float ambientNoiseAmplitude = 0.6f;
        [Tooltip("Frequency of the ambient terrain noise; smaller values = broader, gentler undulation.")]
        [Min(0.001f)] public float ambientNoiseScale = 0.05f;
    }
}
