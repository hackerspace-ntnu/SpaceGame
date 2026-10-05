// Who lives in a settlement: what they say, what they are called and which kinds of people they are.
// A SettlementConfig with a culture fills its houses with residents; without one its characters are
// placed as plain NPCs. One culture serves every settlement of a people — the nomads, the colonists.
using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents.Expeditions;
using SpaceGame.World;

namespace SpaceGame.Agents.Residents
{
    /// <summary>A character prefab and the kinds of people it makes: the handyman fixes things, the warrior keeps watch.</summary>
    [Serializable]
    public struct CharacterProfile
    {
        public GameObject prefab;
        public ResidentArchetype[] suits;
    }

    [CreateAssetMenu(menuName = "SpaceGame/Residents/Culture", fileName = "Culture")]
    public sealed class SettlementCulture : ScriptableObject
    {
        [Tooltip("This culture's line table (<Culture>Lines.txt, tab-separated).")]
        public TextAsset lines;

        [Tooltip("Given names handed out to residents, in seeded order. A character prefab whose Resident already has a name keeps it.")]
        public string[] names = Array.Empty<string>();

        [Tooltip("The kinds of people this culture has. A job archetype is only handed out where a spot of its post exists.")]
        public ResidentArchetype[] archetypes = Array.Empty<ResidentArchetype>();

        [Tooltip("Which kinds of people each character prefab makes: a newcomer from that prefab is drawn from these archetypes " +
                 "when one is usable in the settlement, else from all of them.")]
        public CharacterProfile[] profiles = Array.Empty<CharacterProfile>();

        [Header("Share of the population (normalised)")]
        [Tooltip("Residents with a job spot they work at.")]
        [Min(0f)] public float stationedShare = 0.5f;
        [Tooltip("Residents who spend the day around the settlement.")]
        [Min(0f)] public float roamerShare = 0.35f;
        [Tooltip("Residents who leave on trips: hunting, scouting, foraging.")]
        [Min(0f)] public float outriderShare = 0.15f;

        [Tooltip("Of the whole population, the share who walk the perimeter on guard (in pairs, so the count is rounded to even). " +
                 "0 = none. Needs an archetype whose duty is Patrol.")]
        [Range(0f, 0.5f)] public float patrolShare = 0.15f;

        [Header("Free time")]
        [Tooltip("Free time is spent within this many game minutes' walk of where the resident is, unless nothing there is free. " +
                 "0 = anywhere. For a people whose buildings are joined by slow crossings (the colony's airlocks).")]
        [Min(0f)] public float freeTimeReachMinutes;

        [Tooltip("The Errand use an amble wanders to between spots, in a building that has some (room centres, tube middles). " +
                 "A building with none, or no use here, ambles to points out in the street.")]
        public SpotUse ambleUse;

        [Header("Expeditions")]
        [Tooltip("The bands this culture's settlements keep out in the world. Empty = they send none.")]
        public ExpeditionProfile expeditions;

        /// <summary>The archetypes the profile of <paramref name="prefab"/> suits; empty when it has none.</summary>
        public IReadOnlyCollection<ResidentArchetype> SuitsOf(GameObject prefab)
        {
            if (prefab != null)
                foreach (CharacterProfile profile in profiles)
                    if (profile.prefab == prefab && profile.suits != null) return profile.suits;
            return Array.Empty<ResidentArchetype>();
        }

        /// <summary>The target share of <paramref name="lifestyle"/>, normalised over the three.</summary>
        public float ShareOf(Lifestyle lifestyle)
        {
            float total = stationedShare + roamerShare + outriderShare;
            if (total <= 0f) return 0f;
            float share = lifestyle switch
            {
                Lifestyle.Stationed => stationedShare,
                Lifestyle.Roamer => roamerShare,
                _ => outriderShare,
            };
            return share / total;
        }
    }
}
