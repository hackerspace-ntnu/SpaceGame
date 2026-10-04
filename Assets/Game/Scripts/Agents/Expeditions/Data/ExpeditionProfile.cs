// The bands one culture's settlements send out: the goals they draw from, how many warriors (and residents of
// other roles) they keep, and where a band musters. What a band says is in the culture's line table. A settlement runs bands when its
// culture names a profile and the profile is listed in the ExpeditionCatalog (the catalog index is what replicates).
using System;
using UnityEngine;
using SpaceGame.Agents.Residents;
using SpaceGame.World;

namespace SpaceGame.Agents.Expeditions
{
    /// <summary>How many residents with one role, besides its warriors, a settlement keeps.</summary>
    [Serializable]
    public struct RoleQuota
    {
        [Tooltip("The role kept. Name exactly one role, never Warrior: the warrior quota keeps those.")]
        public ExpeditionRole role;

        [Tooltip("Residents with the role a small settlement keeps: between x and y, more the more beds it has.")]
        public Vector2Int small;

        [Tooltip("Residents with the role a settlement of largeFromBeds beds or more keeps.")]
        [Min(0)] public int large;
    }

    [CreateAssetMenu(menuName = "SpaceGame/Expeditions/Profile", fileName = "Expeditions")]
    public sealed class ExpeditionProfile : ScriptableObject
    {
        [Tooltip("The culture whose settlements run these bands; it names this profile as its expeditions. A stand-in " +
                 "replicates its archetype as an index into this culture's archetypes.")]
        public SettlementCulture culture;

        [Tooltip("The kinds of band the rotation draws from, by weight.")]
        public ExpeditionGoal[] goals = Array.Empty<ExpeditionGoal>();

        [Header("Warrior quota")]
        [Tooltip("Warriors a small settlement keeps: between x and y. Half of them always stay home.")]
        public Vector2Int warriorQuotaSmall = new Vector2Int(6, 8);

        [Tooltip("Warriors a large settlement keeps. Half of them always stay home.")]
        [Min(0)] public int warriorQuotaLarge = 12;

        [Tooltip("Beds from which a settlement counts as large.")]
        [Min(0)] public int largeFromBeds = 40;

        [Header("Home")]
        [Tooltip("The SpotUse of the place a band musters at before it walks out; its +Z points out through the gate.")]
        public SpotUse musterUse;

        [Header("Role quotas")]
        [Tooltip("Residents of other roles a settlement keeps, so a band can still be raised while the last one rests. Same bed " +
                 "bands as the warrior quota (largeFromBeds). Tools/SpaceGame/Expeditions/Apply Role Quotas recasts residents to reach them.")]
        public RoleQuota[] roleQuotas = Array.Empty<RoleQuota>();

        private void OnValidate()
        {
            foreach (string problem in ExpeditionValidation.Problems(this))
                Debug.LogWarning($"[ExpeditionProfile] {name}: {problem}", this);
        }
    }
}
