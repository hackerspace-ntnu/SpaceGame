// Rosters spec §5.4. Hits and kills on a war-party member cost nothing when the attacker is the party's
// quarry or on the quarry's side — the same same-EntityFaction rule the hunt spill uses, which is the
// crew in the open world and the team in a versus match. Anyone else pays as usual.
namespace SpaceGame.Agents
{
    public static class SelfDefenceRules
    {
        /// <param name="quarrySide">The quarry's faction, or null when they are not in the game.</param>
        public static bool IsExempt(string quarryProfileId, FactionDefinition quarrySide,
                                    string attackerProfileId, FactionDefinition attackerSide)
        {
            if (string.IsNullOrEmpty(quarryProfileId)) return false;
            if (attackerProfileId == quarryProfileId) return true;

            return quarrySide != null && attackerSide == quarrySide;
        }
    }
}
