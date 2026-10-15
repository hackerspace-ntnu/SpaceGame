using System;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;

namespace SpaceGame.Gameplay.Status
{
    /// <summary>
    /// Who stasis lets through: every player, and the members of the factions listed.
    ///
    /// <para>
    /// One rule for both things that lay stasis on contact — the sprite hamlet's dome and the
    /// Oathbranch's bolt — because they must agree. The dome lets the hamlet's own walk through its
    /// skin; the bolt flies through the keepers running in to strike a frozen witch. A bolt that
    /// froze a keeper in the line of fire would turn the fight's one verb against the people the
    /// player brought to finish it.
    /// </para>
    /// <para>
    /// A list rather than a faction relationship: the hamlets are NEUTRAL to the crew in the global
    /// table, and making them allied to pass a bolt would change how every other system reads them.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class StasisPass
    {
        [Tooltip("Factions whose members stasis passes by. Players always pass.")]
        [SerializeField] private FactionDefinition[] factions = Array.Empty<FactionDefinition>();

        /// <summary>Does stasis let <paramref name="body"/> through?</summary>
        public bool Passes(Component body)
        {
            if (body == null) return false;
            if (body.GetComponentInParent<PlayerIdentity>() != null) return true;

            EntityFaction faction = body.GetComponentInParent<EntityFaction>();
            if (faction == null || faction.Faction == null) return false;

            foreach (FactionDefinition member in factions)
                if (member == faction.Faction) return true;

            return false;
        }
    }
}
