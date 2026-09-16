// A tribe's people: who it fields in each role, what they carry, and what a war party sent after a
// player looks like at each step of a war.
//
// Design: docs/superpowers/specs/2026-09-16-rosters-and-war-parties-design.md §3.
using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    public enum RosterRole
    {
        Scout,
        Warrior,
        Rider,
        Trader,
        Elder,
    }

    [Serializable]
    public class RosterMember
    {
        public RosterRole role;

        [Tooltip("What to spawn. For a Rider this is the MOUNT prefab carrying an NpcPassenger — the " +
                 "same convention as a caravan template. The mount keeps its own faction; the tribe " +
                 "is applied to the rider it seats.")]
        public GameObject prefab;

        [Min(0f)]
        public float weight = 1f;
    }

    [Serializable]
    public class RoleCount
    {
        public RosterRole role;

        [Min(1)]
        public int count = 1;
    }

    [Serializable]
    public class WarPartyTier
    {
        public RoleCount[] roles = Array.Empty<RoleCount>();
    }

    [CreateAssetMenu(menuName = "Factions/Roster")]
    public class FactionRoster : ScriptableObject
    {
        [Tooltip("The tribe this roster fields. Its FactionDefinition.roster must point back here.")]
        public FactionDefinition faction;

        public RosterMember[] members = Array.Empty<RosterMember>();

        [Tooltip("Weapons a member may draw at spawn. Baked into each nomad's NpcRandomLoadout by " +
                 "NomadPrefabBuilder; a test fails if the two differ.")]
        public InventoryItem[] handItems = Array.Empty<InventoryItem>();

        [Tooltip("Shouted by a war party on first sight of the player it is hunting.")]
        public DialogPool hostileLines;

        [Tooltip("War-party composition per escalation tier. Each party beaten raises the next one tier.")]
        public WarPartyTier[] warPartyTiers = Array.Empty<WarPartyTier>();

        private static readonly List<GameObject> CandidateBuffer = new();
        private static readonly List<float> WeightBuffer = new();

        public int MaxTier => Mathf.Max(0, warPartyTiers.Length - 1);

        public WarPartyTier TierAt(int tier) =>
            warPartyTiers.Length == 0 ? null : warPartyTiers[Mathf.Clamp(tier, 0, warPartyTiers.Length - 1)];

        /// <summary>
        /// A prefab for <paramref name="role"/>, the same one every time for the same seed and index.
        /// Never substitutes another role: an empty role is an authoring error, logged, and null.
        /// </summary>
        public GameObject Draw(RosterRole role, int seed, int index)
        {
            CandidateBuffer.Clear();
            WeightBuffer.Clear();

            foreach (RosterMember member in members)
            {
                if (member == null || member.prefab == null || member.role != role) continue;

                CandidateBuffer.Add(member.prefab);
                WeightBuffer.Add(member.weight);
            }

            int pick = RosterDraw.PickWeighted(WeightBuffer, RosterDraw.Roll01(seed, index));
            if (pick >= 0) return CandidateBuffer[pick];

            Debug.LogError($"[FactionRoster] {name} has no {role} members with a positive weight; " +
                           "nothing was drawn.", this);
            return null;
        }

        /// <summary>Warns and never blocks: a half-filled slot is unfinished, not broken (CONTENT-0004).</summary>
        private void OnValidate()
        {
            foreach (string problem in RosterValidation.Problems(this))
                Debug.LogWarning($"[FactionRoster] {name}: {problem}", this);
        }
    }
}
