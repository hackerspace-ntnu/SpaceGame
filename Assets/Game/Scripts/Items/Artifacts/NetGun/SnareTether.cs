using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Gameplay.Ragdoll;

namespace SpaceGame.Items
{
    /// <summary>
    /// One netted creature, and everything being netted does to it.
    ///
    /// <para>
    /// Added at runtime, never authored — the same shape and reasoning as
    /// <see cref="LassoTether.Ensure"/>. Any creature can be netted at any time, and the
    /// alternative is a component every creature in the game carries for a case most never hit.
    /// </para>
    /// <para>
    /// <b>It exists on every machine, and so does the hold.</b> <c>SnareCatch.Capture</c> is
    /// reached everywhere — the deciding machine calls it from <c>SnareReceiver</c>'s own landing
    /// pass, the peers on hearing <c>NetMsg.Snared</c> — and going limp is presentation each of
    /// them performs for itself. Which machine is entitled to drive the body while it is limp is
    /// <c>RagdollRig.Drives</c>' question, not this one's; a watcher pins the hips to the
    /// replicated root and lets local physics have the rest. This component no longer writes a
    /// transform at all, so it has no authority split of its own to get wrong.
    /// </para>
    /// <para>
    /// <b>The brain is switched off now, and that is the adapter's job rather than this one's.</b>
    /// This used to hold a creature with a constraint and a speed cap precisely so that nothing was
    /// disabled: the version that did <c>agent.enabled = false</c> produced a statue on a string
    /// and restored the flag unconditionally, so a creature whose agent had been parked came back
    /// switched ON and walked off a world that was not loaded yet — and a component left disabled
    /// by a runtime effect is what a quit-time autosave captures. <c>AgentRagdoll.HoldDown</c> is
    /// what makes suspending safe: it records every flag before touching it and only ever switches
    /// back on what it itself switched off, which is the half the old attempt got wrong.
    /// </para>
    /// <para>
    /// The speed cap survives as the FALLBACK for a body that refuses to go down — a mount with
    /// somebody aboard, or a rig whose skeleton build kept no bones. That creature really is still
    /// on its feet, so hobbling it is the honest restraint, and it is better than the net doing
    /// nothing at all to something it visibly landed on.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // added in code, never by hand
    public sealed class SnareTether : MonoBehaviour
    {
        private SnareStruggle settings;
        private NavMeshAgent navAgent;
        private AgentRagdoll ragdoll;

        private bool bound;
        private float authoredSpeed;
        private bool cappedSpeed;

        /// <summary>Did the body actually go down? False means it is hobbled instead.</summary>
        private bool heldDown;

        /// <summary>The net that took hold. Only that net may let go again.</summary>
        private Transform binder;

        public bool IsBound => bound;

        /// <summary>
        /// What this creature weighs, for deciding how fast it drains the net.
        ///
        /// Uses <see cref="LassoTether.EstimateMassOf"/> rather than repeating the estimate: most
        /// creatures in this game are NavMesh agents with no Rigidbody at all, and a single shared
        /// default would have an ant tear a net exactly as fast as a six-legged habitat.
        /// </summary>
        public float Mass { get; private set; }

        public static SnareTether Ensure(GameObject creature)
        {
            if (creature == null) return null;

            return creature.TryGetComponent(out SnareTether existing)
                ? existing
                : creature.AddComponent<SnareTether>();
        }

        /// <summary>
        /// The ragdoll adapter for this creature, resolved on demand.
        ///
        /// From the PARENT for the reason <see cref="navAgent"/> is: <c>SnareCatch.Capture</c> binds
        /// whatever GameObject the capture query returned, which is the collider's object and not
        /// necessarily the root the adapter sits on. Not cached in an Awake, because this component
        /// is added at runtime and Unity does not raise Awake for an AddComponent outside play mode.
        /// </summary>
        private AgentRagdoll Ragdoll =>
            ragdoll != null ? ragdoll : ragdoll = GetComponentInParent<AgentRagdoll>();

        /// <summary>
        /// Take hold. False when another net already has this creature, and false when this net can
        /// do nothing to it at all.
        ///
        /// <para>
        /// <see cref="Ensure"/> returns whatever component is already here, so without that guard a
        /// second net rebinds the SAME tether: the first net's hold vanishes while it goes on
        /// drawing, and whichever net expires first frees the creature from both.
        /// </para>
        /// <para>
        /// Only one number is taken from <paramref name="struggleSettings"/> now — the hobble — and
        /// only on the path where the hold was refused. The rest went with the wander they
        /// described.
        /// </para>
        /// </summary>
        public bool Bind(Transform netAnchor, SnareStruggle struggleSettings)
        {
            if (bound && binder != netAnchor) return false;

            binder = netAnchor;
            settings = struggleSettings ?? new SnareStruggle();

            // A net on somebody riding an animal takes them off it first. Same call as
            // LassoTether.Bind, and the reason survives the rework intact even though the rope it
            // was written for is gone: a seated rider is PARENTED to the mount, so one that goes
            // limp in the saddle is dragged along by an animal that walks on regardless — which is
            // the same hazard AgentRagdoll.HasRider refuses a knockdown over, seen from the rider's
            // end instead of the mount's.
            //
            // Note which way round this runs. It unseats THIS body from whatever it is riding; it
            // does not take a rider off this body. A netted MOUNT still has its rider aboard when
            // the hold is asked for below, which is exactly why that hold can be refused.
            NpcPassenger.UnseatRider(gameObject);

            navAgent = GetComponentInParent<NavMeshAgent>();
            Mass = LassoTether.EstimateMassOf(gameObject);

            AgentRagdoll body = Ragdoll;

            // Asked, not assumed. HoldDown refuses a corpse, a mount with somebody aboard, and a
            // rig that declined to go limp — and a caller that treated a refusal as a hold would
            // leave the net drawing over a creature walking about underneath it.
            heldDown = body != null && body.HoldDown(this);
            if (!heldDown) CapSpeed();

            // Felled, or at least hobbled. Neither means this net does NOTHING to the creature, and
            // a capture recorded over one of those is the failure SnaredBody.Bind refuses for a
            // player: the net spends its shared pool holding something that walks away, and the
            // shooter pays for it. It is not a corner case — the ostrich, the desert crawler, the
            // vrescal and the dune rat all move on LeggedDriver and have no NavMeshAgent to cap.
            if (!heldDown && !cappedSpeed)
            {
                bound = false;
                binder = null;
                settings = null;
                return false;
            }

            bound = true;
            return true;
        }

        /// <summary>
        /// Slow the creature down, remembering what it was.
        ///
        /// <para>
        /// Recorded rather than assumed, for the reason <c>NavMeshAgentMotor.SuspendSelfDrive</c>
        /// gives about the enabled flag: the authored speed is not necessarily what the agent is
        /// carrying when the net lands, and restoring a guess is how a creature ends up permanently
        /// slower than its own prefab.
        /// </para>
        /// <para>
        /// Guarded against capping twice, which is the case that would make the hobble permanent:
        /// a second <see cref="Bind"/> from the same net would otherwise record the already-hobbled
        /// speed as the authored one, and each re-catch would cut the creature's speed again with
        /// no way back.
        /// </para>
        /// </summary>
        private void CapSpeed()
        {
            if (navAgent == null || cappedSpeed || settings == null) return;

            authoredSpeed = navAgent.speed;
            navAgent.speed = authoredSpeed * settings.HobbleSpeed;
            cappedSpeed = true;
        }

        /// <summary>
        /// Give the creature its speed back. Safe on an agent that has since been destroyed — the
        /// flag is cleared either way, so nothing tries to restore onto a dead reference later.
        /// </summary>
        private void RestoreSpeed()
        {
            if (navAgent != null && cappedSpeed) navAgent.speed = authoredSpeed;
            cappedSpeed = false;
        }

        /// <summary>
        /// Give up this net's claim on the body, if it ever had one.
        ///
        /// The creature only gets up once every claim is given up — <c>AgentRagdoll.ReleaseHold</c>
        /// counts them, so a net rotting off an animal something else is also holding does not let
        /// that animal up.
        /// </summary>
        private void LetBodyUp()
        {
            if (!heldDown) return;

            heldDown = false;

            AgentRagdoll body = Ragdoll;
            if (body != null) body.ReleaseHold(this);
        }

        public void Release(Transform netAnchor)
        {
            if (!bound || (netAnchor != null && netAnchor != binder)) return;

            LetBodyUp();
            RestoreSpeed();

            bound = false;
            binder = null;
        }

        /// <summary>
        /// Let go no matter which net asks. For teardown only — a chunk unloading under a net must
        /// not leave a creature limp or hobbled forever with nothing left alive to release it.
        ///
        /// <para>
        /// The two lines under the <see cref="Release"/> call are unreachable today —
        /// <see cref="CapSpeed"/> is only ever reached from <see cref="Bind"/>, which either sets
        /// <c>bound</c> or undoes the cap on the same pass — and they are kept anyway because of
        /// what the unreachable case would cost:
        /// <c>NavMeshAgent.speed</c> is a SERIALIZED field, so a hobble stranded by any future path
        /// that leaves this component capped-but-not-bound is captured by the quit-time autosave,
        /// and the world reloads with a creature that cannot move and nothing in the log to say
        /// why. That is the exact failure this design exists to avoid, so it is worth paying for
        /// twice. <c>SnaredBody.OnDisable</c> has no counterpart because it has no such state:
        /// its Bind fails outright when the hold does.
        /// </para>
        /// </summary>
        private void OnDisable()
        {
            if (bound) Release(binder);

            // See the remarks above: unreachable, kept for what it would cost if it ever were not.
            LetBodyUp();
            RestoreSpeed();
        }
    }
}
