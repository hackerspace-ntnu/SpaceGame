// The one place two entities are turned into a single answer about each other.
//
// Three layers, most specific first, and each one lives on a different object so none of them has
// to know about the others (design §3):
//
//   1. GRUDGE   — this agent, about that individual. A creature you shot is your enemy whatever
//                 its faction thinks of yours, and only yours: its neighbours are unaffected
//                 until an AlertBroadcaster tells them.
//   2. GOODWILL — this faction, about that PLAYER. A tribe you have been shooting turns on you
//                 while leaving your crewmate alone. Phase 3; the hook is here and answers null.
//   3. STANCE   — this faction, about that faction. The authored table, plus each side's
//                 defaultStance for pairs nobody wrote a row for.
//
// The order is the point. A grudge is about two individuals and cannot be expressed as a faction
// fact, so it has to beat one; goodwill is about one player and cannot be expressed as a stance,
// so it has to beat that. Asking the table first and patching the answer afterwards is the same
// logic written in the order that gets it wrong.
//
// Everything that hunts goes through EntityFaction.GetRelationshipWith, which is the only caller
// of this. Nothing else may reach past it to FactionRelationshipTable.Get — a second answer to
// "are we enemies" is free to drift from the first, and the bug that produces is an agent that
// chases what it will not shoot.
using UnityEngine;

namespace SpaceGame.Agents
{
    public static class FactionRelations
    {
        /// <summary>
        /// What <paramref name="self"/> thinks of <paramref name="other"/> right now.
        ///
        /// <para>
        /// Asymmetric by design, and that is not a defect: the agent you just shot is hostile to
        /// you while you may be perfectly neutral toward it. Only the stance layer is guaranteed
        /// to agree in both directions.
        /// </para>
        /// </summary>
        public static FactionRelationship Resolve(EntityFaction self, EntityFaction other)
        {
            if (self == null || other == null)
                return FactionRelationship.Neutral;

            if (HoldsGrudgeAgainst(self, other))
                return FactionRelationship.Hostile;

            FactionRelationship? goodwill = ResolveGoodwill(self, other);
            if (goodwill.HasValue)
                return goodwill.Value;

            return self.RelationshipTable != null
                ? self.RelationshipTable.Get(self.Faction, other.Faction)
                : FactionRelationship.Neutral;
        }

        /// <summary>
        /// Has <paramref name="self"/> been provoked by <paramref name="other"/> personally?
        ///
        /// <para>
        /// Compared at the ROOT on both sides. A grudge is against a body, and the thing that hurt
        /// you is routinely reported as one of its parts — a weapon's muzzle, a limb, a rider in a
        /// saddle — so comparing the transforms as handed over would let the same attacker walk
        /// away from its own grudge by being reported through a different child.
        /// </para>
        /// </summary>
        private static bool HoldsGrudgeAgainst(EntityFaction self, EntityFaction other)
        {
            if (!self.TryGetComponent(out ProvocationModule provocation) || !provocation.IsProvoked)
                return false;

            Transform aggressor = provocation.Aggressor;
            return aggressor != null && aggressor.root == other.transform.root;
        }

        /// <summary>
        /// What this faction's goodwill meter toward <paramref name="other"/>'s player says, or
        /// null if goodwill has nothing to say about this pair.
        ///
        /// <para>
        /// Phase 3 fills this in from <c>FactionGoodwillLedger</c>. Until then it answers null,
        /// which means "no opinion — ask the table", so the layer is inert rather than absent:
        /// every caller already goes through it and nothing has to be rewired later.
        /// </para>
        /// </summary>
        private static FactionRelationship? ResolveGoodwill(EntityFaction self, EntityFaction other)
        {
            return null;
        }
    }
}
