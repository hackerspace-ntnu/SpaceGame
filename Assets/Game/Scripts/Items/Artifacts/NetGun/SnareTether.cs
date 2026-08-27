using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Core;

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
    /// <b>It exists only on the machine that simulates the creature.</b> On a peer the replica is
    /// kinematic on purpose and its transform is somebody else's to publish; a second copy of this
    /// fighting a NetworkTransform is a creature that visibly stutters between two answers.
    /// <see cref="Network.Simulates"/> is the right question here — where
    /// <see cref="LassoTether"/> deliberately asks <see cref="Network.Owns"/> instead — because
    /// what this moves is an AI creature, which the server simulates. A netted PLAYER is
    /// owner-authoritative and is somebody else's problem entirely; see <c>SnaredBody</c>.
    /// </para>
    /// <para>
    /// <b>The AI is never switched off.</b> This holds the creature with a constraint and a speed
    /// cap rather than by disabling its motor, for two separate reasons.
    /// <see cref="LassoTether"/> documents the first: the version that did
    /// <c>agent.enabled = false</c> produced a statue on a string, and restored the flag
    /// unconditionally, so a creature whose agent had been parked came back switched ON and walked
    /// off a world that was not loaded yet. The second is persistence — a runtime effect that
    /// leaves a component disabled is exactly what a quit-time autosave captures, and the world
    /// reloads with a creature that cannot move and nothing in the log to say why.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // added in code, never by hand
    public sealed class SnareTether : MonoBehaviour
    {
        private Transform anchor;
        private SnareStruggle settings;
        private NavMeshAgent navAgent;
        private Rigidbody body;

        private bool bound;
        private float authoredSpeed;
        private bool cappedSpeed;
        private float thrashPhase;

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

        /// <summary>Which way it is pulling right now, for the net to be dragged by.</summary>
        public Vector3 StrugglePull { get; private set; }

        public static SnareTether Ensure(GameObject creature)
        {
            if (creature == null) return null;

            return creature.TryGetComponent(out SnareTether existing)
                ? existing
                : creature.AddComponent<SnareTether>();
        }

        /// <summary>
        /// Take hold. False when another net already has this creature.
        ///
        /// <see cref="Ensure"/> returns whatever component is already here, so without this a
        /// second net rebinds the SAME tether: the first net's constraint vanishes while it goes on
        /// drawing, and whichever net expires first frees the creature from both.
        /// </summary>
        public bool Bind(Transform netAnchor, SnareStruggle struggleSettings)
        {
            if (bound && binder != netAnchor) return false;

            binder = netAnchor;
            anchor = netAnchor;
            settings = struggleSettings ?? new SnareStruggle();

            // A net on somebody riding an animal takes them off it first: a seated rider's
            // transform belongs to the mount, so the net would go taut against an animal that
            // walks on regardless. Same call and same reason as LassoTether.Bind.
            NpcPassenger.UnseatRider(gameObject);

            navAgent = GetComponentInParent<NavMeshAgent>();
            body = GetComponentInParent<Rigidbody>();
            Mass = LassoTether.EstimateMassOf(gameObject);

            CapSpeed();

            bound = true;
            return true;
        }

        /// <summary>
        /// Slow the creature down, remembering what it was.
        ///
        /// <para>
        /// Recorded rather than assumed for the reason <c>NavMeshAgentMotor.SuspendSelfDrive</c>
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
            if (navAgent == null || cappedSpeed) return;

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

        public void Release(Transform netAnchor)
        {
            if (!bound || (netAnchor != null && netAnchor != binder)) return;

            RestoreSpeed();

            bound = false;
            anchor = null;
            binder = null;
            StrugglePull = Vector3.zero;
        }

        /// <summary>
        /// Let go no matter which net asks. For teardown only — a chunk unloading under a net must
        /// not leave a creature hobbled forever with nothing left alive to release it.
        /// </summary>
        private void OnDisable()
        {
            if (bound) Release(binder);

            // Belt and braces, and the belt is not enough on its own. Release refuses when the net
            // addressing it is not the one that took hold, so any path that leaves this component
            // capped-but-not-bound would strand the hobble — and NavMeshAgent.speed is a SERIALIZED
            // field, so a quit-time autosave captures it and the world reloads with a creature that
            // cannot move and nothing in the log to say why. That is the exact failure this whole
            // design exists to avoid, so it is worth paying for twice.
            RestoreSpeed();
        }

        /// <summary>
        /// Advance one step without a physics frame. The seam the EditMode tests use, and public
        /// for the reason <see cref="LassoedBody.Step"/> is: the tests compile into
        /// Assembly-CSharp-Editor, which cannot see internals of Assembly-CSharp.
        /// </summary>
        public void Step(float deltaTime)
        {
            if (!bound || anchor == null) return;

            // Every machine has one of these once a net has landed; only the one that simulates
            // this creature may move it. Offline — and in an EditMode test with no NetworkManager —
            // this answers true, so the seam needs no weakening to be testable.
            if (!Network.Simulates(this)) return;

            thrashPhase += deltaTime * settings.ThrashFrequency * Mathf.PI * 2f;

            Vector3 fromAnchor = transform.position - anchor.position;
            fromAnchor.y = 0f;

            float distance = fromAnchor.magnitude;
            Vector3 away = distance < 0.001f ? Vector3.zero : fromAnchor / distance;

            // Retreat plus thrash, the same blend LassoTether.FixedUpdate makes and for the same
            // reason: a pure retreat is a straight line away from the net, which reads as a spring
            // rather than as an animal. What sells it is the creature throwing its weight ACROSS
            // its own tether and being turned by it.
            Vector3 sideways = Vector3.Cross(away, Vector3.up);
            StrugglePull = away == Vector3.zero
                ? Vector3.zero
                : Vector3.Lerp(away, sideways * Mathf.Sin(thrashPhase), settings.ThrashShare).normalized;

            if (distance <= settings.ShuffleRadius) return;

            // Pulled back along the RADIAL direction, not the thrashed one. The thrash is what the
            // net is dragged by; using it here as well would slide the creature around the rim of
            // its own shuffle circle every substep, which is jitter rather than struggle.
            //
            // Position error given back as a POSITION, never folded into velocity. Velocity added
            // to close a gap is still there on the next step, which is how a solver gains energy —
            // the rule LassoedBody.Step states.
            Vector3 pulledBack = anchor.position + away * settings.ShuffleRadius;
            pulledBack.y = transform.position.y;

            if (body != null && !body.isKinematic) body.position = pulledBack;
            else transform.position = pulledBack;
        }

        private void FixedUpdate() => Step(Time.fixedDeltaTime);
    }
}
