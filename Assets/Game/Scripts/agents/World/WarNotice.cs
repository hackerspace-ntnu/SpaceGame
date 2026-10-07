// What a hunted player is told about their war (rosters spec §5.5, GDC-L1-DESIGN-0006): a reckoning
// the player cannot perceive is no consequence at all. Phase 7's visor readout will say more; this is
// the minimum tell, through the one visor channel there is.
using SpaceGame.Presentation;

namespace SpaceGame.Agents
{
    /// <summary>Sent as an int over the wire. Append only.</summary>
    public enum WarNotice
    {
        Raised,
        Weakening,
        GaveUp,
    }

    public static class WarNoticeText
    {
        /// <summary>One message id per tribe, so "given up" replaces "a war party is coming" in place.</summary>
        public static (string Id, string Text, MessageSeverity Severity) For(WarNotice notice, FactionDefinition tribe)
        {
            string name = tribe != null ? tribe.factionName : "The tribe";
            string id = $"war:{(tribe != null ? tribe.ID : string.Empty)}";

            return notice switch
            {
                WarNotice.Raised    => (id, $"{name} have sent a war party after you.", MessageSeverity.Warning),
                WarNotice.Weakening => (id, $"{name}: their resolve is weakening.", MessageSeverity.Notice),
                _                   => (id, $"{name} have given up the war.", MessageSeverity.Notice),
            };
        }
    }
}
