// What a Settlement is made of. Buildings/decorations/characters are prefab lists where `count` is
// the most that can appear and `spawnChance` is each one's own odds of appearing — determinism
// comes from the placing GameObject's position, not from anything stored here, so this config can
// be dropped onto many settlements and still give each one its own layout. Every distance is in
// metres (Unity units); a Settlement's own size multiplier scales the counts.
using System;
using UnityEngine;
using SpaceGame.Agents.Residents;

namespace SpaceGame.World
{
    [CreateAssetMenu(fileName = "SettlementConfig", menuName = "Settlement/Settlement Config")]
    public class SettlementConfig : ScriptableObject
    {
        // A class, not a struct, so the field initializer below is what an asset saved before
        // spawnChance existed deserializes to -- a struct would load it as 0 and spawn nothing.
        [Serializable]
        public class SpawnEntry
        {
            public GameObject prefab;
            [Tooltip("The most of this prefab that can appear.")]
            [Min(0)] public int count;
            [Tooltip("Chance each of the `count` copies appears, rolled separately per copy off the settlement's seed. 1 = always.")]
            [Range(0f, 1f)] public float spawnChance = 1f;
        }

        [Header("Buildings")]
        public SpawnEntry[] buildings = Array.Empty<SpawnEntry>();

        [Header("Decorations")]
        public SpawnEntry[] decorations = Array.Empty<SpawnEntry>();

        // One named character a settlement always has exactly one of: a quest giver, an elder.
        [Serializable]
        public class SpecialCharacter
        {
            public GameObject prefab;
            [Tooltip("Who this character is. Empty = handed out like anyone else's.")]
            public ResidentArchetype archetype;
        }

        [Header("Characters")]
        [Tooltip("The characters residents are drawn from, and the most of each. With dwellings, their beds decide how many move in (the rest of this list stays out); without any, every copy rolled here appears.")]
        public SpawnEntry[] characters = Array.Empty<SpawnEntry>();
        [Tooltip("Always spawned, exactly one each, before anyone else — they get the first beds.")]
        public SpecialCharacter[] specialCharacters = Array.Empty<SpecialCharacter>();
        [Tooltip("Who the characters are as a people: lines, names, archetypes. Empty = the characters are placed as plain NPCs, with no day, home or speech.")]
        public SettlementCulture culture;

        [Header("Planned layout")]
        [Tooltip("Lays the settlement out along a grown street network, buildings lining the streets, the ground cut into terraces with walls and stairs. Empty = the buildings grow as a cluster instead.")]
        public SettlementStreetStyle streets;

        [Header("Layout (metres)")]
        [Tooltip("Smallest wall-to-wall distance between any two buildings, in metres. 0 lets buildings touch.")]
        [Min(0f)] public float minBuildingGap = 1f;
        [Tooltip("Largest wall-to-wall distance a new building leaves to the neighbour it is built beside, in metres. Each building picks a gap between min and max, so the settlement grows outward from its first building and its size comes from how many buildings it has -- there is no radius to fit them into.")]
        [Min(0f)] public float maxBuildingGap = 4f;
        [Tooltip("How far past the outermost building's wall decorations and characters may spread, in metres.")]
        [Min(0f)] public float outskirts = 10f;
        [Tooltip("Centre-to-centre distance kept between two decorations (e.g. tents), in metres, regardless of their mesh size. Decorations only avoid each other this way, plus real building geometry, so a handful of large canopies can still cluster into a small camp.")]
        [Min(0f)] public float decorationSpacing = 1.5f;
        [Tooltip("Centre-to-centre distance kept between two characters, in metres. Characters stand anywhere on the NavMesh inside the settlement -- ground, courtyards, walkable roofs.")]
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

        private void OnValidate()
        {
            maxBuildingGap = Mathf.Max(maxBuildingGap, minBuildingGap);
        }
    }
}
