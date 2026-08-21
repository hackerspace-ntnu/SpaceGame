// Opt-in for the motors that keep moving after nobody ticks them.
//
// Most motors are inert the moment AgentController stops calling Tick: a RigidbodyMotor only
// writes the body from a rider input it was handed, a LeggedDriver notices the missing command
// and idles. A NavMeshAgent does not. It owns transform.position for as long as it is enabled and
// keeps walking the path it was last given, so a client that merely stops deciding still has a
// creature striding off across the sand while the server's NetworkTransform yanks it back every
// tick — which is precisely the desync jitter that switching the brain off was meant to end. It
// is worse than a fight, in fact: with updatePosition on, the agent rewrites the replicated
// position from its own internal one every frame, so the remote copy stops tracking the server at
// all and stands where it last pathed.
//
// Two methods rather than one taking a bool, because they are not symmetric — Suspend records
// what it switched off so Resume can put exactly that back, and a caller passing the wrong bool
// twice must not be able to invent an "enabled" state the motor never had.
namespace SpaceGame.Agents
{
    public interface ISelfDrivingMotor
    {
        /// <summary>
        /// Stop driving the transform until further notice: this machine is only watching.
        /// Must be idempotent — the controller calls it once per transition, but a transition can
        /// be re-entered by an ownership change on the same frame as a spawn.
        /// </summary>
        void SuspendSelfDrive();

        /// <summary>Undo <see cref="SuspendSelfDrive"/>. Idempotent, and a no-op if never suspended.</summary>
        void ResumeSelfDrive();
    }
}
