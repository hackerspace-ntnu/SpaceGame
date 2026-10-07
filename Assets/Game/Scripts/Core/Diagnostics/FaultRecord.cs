// One fault, as it happened: what threw, where, and whether it has been switched off.
//
// A struct with no Unity object references on purpose. Records outlive the objects they describe —
// the ledger is read minutes later, from a chat command, after the GameObject is gone — so holding
// a Component here would either resurrect a destroyed reference or print "null" where the useful
// information used to be. The names are copied at the moment of the fault instead.
namespace SpaceGame.Diagnostics
{
    public readonly struct FaultRecord
    {
        /// <summary>Where in the code, e.g. "AgentController.Movement". Stable, never interpolated with an id.</summary>
        public readonly string Site;

        /// <summary>The GameObject or component name at the moment of the fault.</summary>
        public readonly string Owner;

        /// <summary>The exception, already formatted. Never null; "unknown" when nothing was supplied.</summary>
        public readonly string Detail;

        /// <summary>How many times this (owner, site) pair has thrown inside the current window.</summary>
        public readonly int Count;

        /// <summary>True on the record that tripped quarantine — the one that switched the thing off.</summary>
        public readonly bool Quarantined;

        /// <summary>Seconds since startup, as supplied by the caller. Tests pass their own clock.</summary>
        public readonly float Time;

        public FaultRecord(string site, string owner, string detail, int count, bool quarantined, float time)
        {
            Site = string.IsNullOrEmpty(site) ? "unknown" : site;
            Owner = string.IsNullOrEmpty(owner) ? "unknown" : owner;
            Detail = string.IsNullOrEmpty(detail) ? "unknown" : detail;
            Count = count;
            Quarantined = quarantined;
            Time = time;
        }

        public override string ToString() =>
            $"[{Time:F1}s] {Owner} · {Site} · x{Count}{(Quarantined ? " · QUARANTINED" : string.Empty)} · {Detail}";
    }
}
