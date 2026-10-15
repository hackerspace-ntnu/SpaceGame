// A hand-built outpost and the people who live at it: drop this on an empty GameObject, assign the outpost
// prefab and the character prefabs, press Generate (or Generate + Bake World NavMesh) in the inspector.
//
// It is a Settlement whose whole configuration is on the component, so everything a settlement does it does
// here: the people are residents with a day, a home and speech (when a culture is set), they sit on the
// outpost's seats, climb its ladders and carry its props, and the settlement's places, plans and saves are
// the ordinary ones. Turn the GameObject to turn the outpost; the characters it lists are always the ones
// that move in (an outpost has no beds to cap them: they sleep where they stand, like any camp).
using System;
using UnityEngine;
using SpaceGame.Agents.Residents;

namespace SpaceGame.World
{
    public sealed class Outpost : Settlement
    {
        [Tooltip("The outpost itself: one prefab from Prefabs/Environment/Structures/Outpost, or any hand-built arrangement.")]
        [SerializeField] private GameObject outpostPrefab;

        [Tooltip("The characters who live here: a prefab and how many of it. They are tied to this outpost -- its seats, ladders, " +
                 "work posts and props are theirs. A character prefab made for a role only moves in where the outpost has that role's post.")]
        [SerializeField] private SettlementConfig.SpawnEntry[] residents = Array.Empty<SettlementConfig.SpawnEntry>();

        [Tooltip("Who the residents are as a people: lines, names, archetypes. Empty = plain NPCs with no day, home or speech.")]
        [SerializeField] private SettlementCulture culture;

        [Header("Ground")]
        [Tooltip("How far past the outpost's outermost wall its residents may stand when they are placed, in metres.")]
        [SerializeField, Min(0f)] private float outskirts = 8f;

        [Tooltip("Flat ground kept beyond the outpost's own footprint before the terrain blends back, in metres.")]
        [SerializeField, Min(0f)] private float flattenPadding = 2f;

        [Tooltip("Distance over which the flattened ground blends back into the natural terrain, in metres.")]
        [SerializeField, Min(0.1f)] private float blendDistance = 8f;

        private SettlementConfig built;

        public GameObject OutpostPrefab => outpostPrefab;

        // The settlement machinery reads one SettlementConfig; this component is its source, so it is made from the fields
        // and thrown away when they change. Never saved: nothing outlives the component that owns it.
        protected override SettlementConfig Config
        {
            get
            {
                if (outpostPrefab == null) return null;
                if (built == null) built = Build();
                return built;
            }
        }

        protected override Quaternion GetSpawnRotation(Vector2 localXZ, ref SettlementPlacementUtil.SeededRng rng) =>
            localXZ.sqrMagnitude < 0.01f
                ? Quaternion.Euler(0f, transform.eulerAngles.y, 0f)
                : base.GetSpawnRotation(localXZ, ref rng);

        private SettlementConfig Build()
        {
            var config = ScriptableObject.CreateInstance<SettlementConfig>();
            config.hideFlags = HideFlags.HideAndDontSave;
            config.name = name + " outpost";
            config.buildings = new[] { new SettlementConfig.SpawnEntry { prefab = outpostPrefab, count = 1, spawnChance = 1f } };
            config.characters = residents;
            config.culture = culture;
            config.outskirts = outskirts;
            config.flattenPadding = flattenPadding;
            config.blendDistance = blendDistance;
            return config;
        }

        private void OnValidate() => Forget();

        private void OnDestroy() => Forget();

        private void Forget()
        {
            if (built == null) return;

            if (Application.isPlaying) Destroy(built);
            else DestroyImmediate(built);
            built = null;
        }
    }
}
