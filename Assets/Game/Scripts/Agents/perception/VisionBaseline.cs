// The least any agent sees: how wide, how far, and how long it remembers. One definition, read by
// the component defaults (PerceptionModule, AgentTargeting, TargetingProfile), by the wiring pass
// that raises authored prefabs and profiles to it, which fails a
// creature that falls below it.
//
// Why it exists: every agent used to run on its component's defaults -- a 110 degree cone, 35 m,
// five or six seconds of memory -- which nobody had chosen, and a player was noticed only when he
// stood right in front of an NPC. A creature may see MORE than this (prey animals are wider); one
// that should see less is exempted by name in the wiring pass, with its reason.
namespace SpaceGame.Agents
{
    public static class VisionBaseline
    {
        /// <summary>Degrees. Half of that either side of the root's forward.</summary>
        public const float MinFieldOfView = 180f;

        /// <summary>Metres at which a new target can be scored.</summary>
        public const float MinAcquisitionRange = 80f;

        /// <summary>Metres at which a held target is dropped. Above acquisition, for hysteresis.</summary>
        public const float MinLoseRange = 110f;

        /// <summary>Seconds an unseen target and its last-known position are remembered.</summary>
        public const float MinMemory = 12f;
    }
}
