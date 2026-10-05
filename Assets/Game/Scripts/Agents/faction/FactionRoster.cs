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
                 "the shipped nomad prefabs; a test fails if the two differ.")]
        public InventoryItem[] handItems = Array.Empty<InventoryItem>();

        [Tooltip("Shouted by a war party on first sight of the player it is hunting.")]
        public DialogPool hostileLines;

        [Tooltip("War-party composition per escalation tier. Each party beaten raises the next one tier.")]
        public WarPartyTier[] warPartyTiers = Array.Empty<WarPartyTier>();

        private static readonly List<GameObject> CandidateBuffer = new();
        private static readonly List<float> WeightBuffer = new();
        private static readonly List<(double key, int candidate)> RoundBuffer = new();
        private static readonly int RoleKinds = Enum.GetValues(typeof(RosterRole)).Length;

        public int MaxTier => Mathf.Max(0, warPartyTiers.Length - 1);

        public WarPartyTier TierAt(int tier) =>
            warPartyTiers.Length == 0 ? null : warPartyTiers[Mathf.Clamp(tier, 0, warPartyTiers.Length - 1)];

        /// <summary>
        /// The prefab dealt as the <paramref name="dealt"/>-th <paramref name="role"/> of the group with
        /// <paramref name="seed"/>, the same one every time. The role's members are a deck: each round
        /// deals every member with a positive weight once, in an order shuffled per round, so no member
        /// comes twice before every one has come once. A weight only orders the round — a heavier member
        /// tends to come earlier (first with the odds of its weight), so a small group sees it more.
        /// Never substitutes another role: an empty role is an authoring error, logged, and null.
        /// </summary>
        public GameObject Deal(RosterRole role, int seed, int dealt)
        {
            CandidateBuffer.Clear();
            WeightBuffer.Clear();

            foreach (RosterMember member in members)
            {
                if (member == null || member.prefab == null || member.role != role || member.weight <= 0f) continue;

                CandidateBuffer.Add(member.prefab);
                WeightBuffer.Add(member.weight);
            }

            if (CandidateBuffer.Count == 0)
            {
                Debug.LogError($"[FactionRoster] {name} has no {role} members with a positive weight; " +
                               "nothing was drawn.", this);
                return null;
            }

            // A weighted shuffle (Efraimidis-Spirakis): each member's key is roll^(1/weight), and the
            // round deals them highest key first.
            int round = Mathf.Max(0, dealt) / CandidateBuffer.Count;
            int roundSeed = (int)RosterDraw.Hash(seed, round * RoleKinds + (int)role);
            RoundBuffer.Clear();
            for (int i = 0; i < CandidateBuffer.Count; i++)
                RoundBuffer.Add((Math.Pow(RosterDraw.Roll01(roundSeed, i), 1d / WeightBuffer[i]), i));
            RoundBuffer.Sort((a, b) => b.key != a.key ? b.key.CompareTo(a.key) : a.candidate.CompareTo(b.candidate));

            return CandidateBuffer[RoundBuffer[Mathf.Max(0, dealt) % CandidateBuffer.Count].candidate];
        }

        /// <summary>Warns and never blocks: a half-filled slot is unfinished, not broken (CONTENT-0004).</summary>
        private void OnValidate()
        {
            foreach (string problem in RosterValidation.Problems(this))
                Debug.LogWarning($"[FactionRoster] {name}: {problem}", this);
        }
    }
}
