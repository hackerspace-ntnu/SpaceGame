// The one place two entities are turned into a single answer about each other.
//
// Three layers, most specific first, and each one lives on a different object so none of them has
// to know about the others (design §3):
//
//   1. GRUDGE   — this agent, about that individual. A creature you shot is your enemy whatever
//                 its faction thinks of yours, and only yours: its neighbours are unaffected
//                 until an AlertBroadcaster tells them.
//   2. GOODWILL — this faction, about that PLAYER. A tribe you have been shooting turns on you
//                 while leaving your crewmate alone — unless they are standing next to you, which
//                 is the one place the per-player rule bends (FactionGoodwillLedger.BandForEntity).
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
        /// What <paramref name="self"/>'s faction currently thinks of the PLAYER behind
        /// <paramref name="other"/>, or null if goodwill has nothing to say about this pair.
        ///
        /// <para>
        /// Null is the common answer and means "no opinion — ask the table": there is no ledger
        /// (offline test, arena), this faction keeps none (Clankers, Outlaws), or the other side is
        /// not a player. Only the two extreme bands override the authored stance; Friendly opens
        /// trade and dialogue without changing who shoots whom, which is why it is not here.
        /// </para>
        /// <para>
        /// Asked in BOTH directions, so a nomad hunting you and your own visor agree about it. The
        /// ledger answers the same row either way round.
        /// </para>
        /// </summary>
        private static FactionRelationship? ResolveGoodwill(EntityFaction self, EntityFaction other)
        {
            FactionGoodwillLedger ledger = FactionGoodwillLedger.Instance;
            if (ledger == null) return null;

            // Which side is the tribe and which is the player. A tribe keeps no opinion of another
            // tribe here — that is the table's business — so exactly one side may be tracked.
            if (ledger.Tracks(self.Faction) && !ledger.Tracks(other.Faction))
                return FromBand(ledger.BandForEntity(self.Faction, other));

            if (ledger.Tracks(other.Faction) && !ledger.Tracks(self.Faction))
                return FromBand(ledger.BandForEntity(other.Faction, self));

            return null;
        }

        /// <summary>
        /// The two bands that change who shoots whom. Everything between them leaves the authored
        /// stance alone.
        /// </summary>
        private static FactionRelationship? FromBand(GoodwillBand band)
        {
            if (GoodwillMath.IsHostile(band)) return FactionRelationship.Hostile;
            if (band == GoodwillBand.Allied) return FactionRelationship.Allied;

            return null;
        }
    }
}
