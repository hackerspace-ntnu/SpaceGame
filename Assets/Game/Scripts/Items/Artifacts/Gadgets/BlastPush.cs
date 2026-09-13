using System;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay.Ragdoll;

namespace SpaceGame.Items
{
    /// <summary>
    /// Hands a launch velocity to whatever kind of thing was caught in a blast.
    ///
    /// <para>
    /// Three kinds of target, three routes, and the split is a property of the TARGETS rather
    /// than of the weapon — which is exactly why it does not belong inside any one weapon:
    /// </para>
    /// <list type="bullet">
    ///   <item>A <b>player</b> is owner-authoritative. A velocity written here on the server is
    ///     overwritten within a tick and silently, so the shove travels as
    ///     <see cref="NetMsg.Flung"/> and the victim's own machine applies it
    ///     (<c>FlungBody</c>, which brings its own shake and FOV kick).</item>
    ///   <item>An <b>agent</b> — creature, mount, NPC — has a kinematic transform owned by its
    ///     motor, so forces never land on it at all. It can be knocked down or thrown as a leap,
    ///     and if it can do neither the blast moves it not at all.</item>
    ///   <item>Anything else with a <b>Rigidbody</b> takes a mass-scaled impulse.</item>
    /// </list>
    /// <para>
    /// Extracted because <see cref="RepulsorGauntletArtifact"/> and
    /// <see cref="SuckerPuncherArtifact"/> carried a copy each and the dragon bazooka's burst
    /// would have been a third. The two had already drifted — one clamps the mass scale to a
    /// serialized range and the other to a hard-coded pair; one gained ragdoll knockdown and the
    /// other did not — which is the failure this prevents: a fix to the kinematic-replica guard
    /// landing in one weapon and silently not the others.
    /// </para>
    /// <para>
    /// <b>The differences are parameters, not branches.</b> Both existing weapons keep the exact
    /// behaviour they had, which is the point — this is a de-duplication, not a retune. The one
    /// deliberate change is the degenerate-direction guard on the leap: a fling with no horizontal
    /// component used to reach <c>RequestLeap</c> with a zero vector and throw the creature
    /// nowhere in particular. It now does nothing instead. That case is reachable — the shared
    /// <see cref="RepulsorBlast.Launch"/> resolves a target directly overhead to straight up.
    /// </para>
    /// <para>
    /// Deliberately a plain static rather than a component. It is called from inside an
    /// <c>OverlapSphere</c> loop on the authority only, has no state of its own, and giving it a
    /// MonoBehaviour would put a serialized copy of the same tuning on every prefab.
    /// </para>
    /// </summary>
    public static class BlastPush
    {
        /// <summary>
        /// How hard a motor-driven creature is thrown, expressed as the leap it is asked for.
        ///
        /// <para>
        /// Both endpoints of each range are carried because the two original call sites disagreed
        /// about the floor, and both were right for their weapon: the Sucker Puncher wants a
        /// creature at the very edge of the wave to still hop (its range starts at 2 m), while the
        /// repulsor wants an edge hit to fade to nothing (its range starts at 0). Collapsing that
        /// to one rule would have quietly retuned a shipped weapon.
        /// </para>
        /// <para>
        /// A struct with no serialized fields, so adopting this changes no prefab and no save.
        /// </para>
        /// </summary>
        public readonly struct Leap
        {
            public readonly float MinDistance;
            public readonly float MaxDistance;
            public readonly float MinHeight;
            public readonly float MaxHeight;
            public readonly float Duration;

            public Leap(float minDistance, float maxDistance, float minHeight, float maxHeight,
                        float duration)
            {
                MinDistance = minDistance;
                MaxDistance = maxDistance;
                MinHeight = minHeight;
                MaxHeight = maxHeight;
                Duration = duration;
            }

            /// <summary>A leap that scales linearly from nothing at zero strength.</summary>
            public static Leap Proportional(float distance, float height, float duration)
                => new Leap(0f, distance, 0f, height, duration);
        }

        /// <summary>
        /// Push <paramref name="root"/> with <paramref name="velocity"/>.
        ///
        /// <paramref name="referenceSpeed"/> is the authored peak the weapon launches at, and it
        /// is what <paramref name="velocity"/> is measured against to price the creature leap — so
        /// retuning a weapon's fling retunes its knockback on creatures with it, rather than
        /// leaving the two to drift apart.
        /// </summary>
        /// <param name="collider">
        /// The collider that was caught. Needed for <c>attachedRigidbody</c>: a loose body's
        /// Rigidbody frequently is not on the transform root the rest of this reasons about.
        /// </param>
        /// <param name="massScaleRange">
        /// Clamp on the mass compensation, so a paperclip is not launched into orbit and a
        /// shipping container is not immovable.
        /// </param>
        /// <param name="knock">
        /// Optional: put the victim on the ground as well. Supplied by weapons that knock down and
        /// left null by those that do not, because a knockdown carries a duration the weapon owns
        /// and a message only it should be composing.
        ///
        /// <para>
        /// For a player it happens IN ADDITION to the fling, and neither replaces the other: the
        /// fling carries the BODY, applied by the machine that owns it, while the knockdown
        /// carries the POSE on every machine — bone transforms do not replicate, so a ragdoll is
        /// not something one machine can run for another. For a creature it happens INSTEAD of the
        /// leap, and only when the creature can be knocked down at all: a mount with somebody on
        /// it must not go limp, because a rider is parented to the seat and would be dragged
        /// through the ground with it.
        /// </para>
        /// </param>
        public static void Apply(Collider collider, GameObject root, Vector3 velocity,
                                 float referenceSpeed, in Leap leap,
                                 float itemMassReference, Vector2 massScaleRange,
                                 Action<GameObject, Vector3> knock = null)
        {
            if (root == null) return;

            if (root.GetComponent<PlayerMovement>() != null)
            {
                NetMessaging.NetSendTo(root, NetMsg.Flung, new NetArg { P = velocity }, NetTo.All);
                knock?.Invoke(root, velocity);
                return;
            }

            if (root.GetComponentInChildren<AgentController>() != null)
            {
                PushAgent(root, velocity, referenceSpeed, leap, knock);
                return;
            }

            Rigidbody body = collider != null ? collider.attachedRigidbody : null;
            if (body == null) return;

            if (body.isKinematic)
            {
                // Only un-kinematic a body this machine simulates — a kinematic replica is
                // kinematic on purpose (the LassoTether guard).
                if (!Network.Simulates(body)) return;
                body.isKinematic = false;
            }

            float massScale = Mathf.Clamp(itemMassReference / Mathf.Max(body.mass, 0.1f),
                                          massScaleRange.x, massScaleRange.y);
            body.AddForce(velocity * massScale, ForceMode.VelocityChange);
        }

        /// <summary>
        /// A creature's transform belongs to its motor and forces never land on it, so there are
        /// only two things that can be done to one: take the body away from the motor and let it
        /// fall, or throw it as a leap.
        ///
        /// Ragdoll wins where the weapon offers it, and the leap is the fallback rather than the
        /// other way round — but only for a creature that answers <c>CanBeKnockedDown</c>, which
        /// is what keeps a ridden mount from going limp underneath its rider.
        /// </summary>
        private static void PushAgent(GameObject root, Vector3 velocity, float referenceSpeed,
                                      in Leap leap, Action<GameObject, Vector3> knock)
        {
            if (knock != null)
            {
                var ragdoll = root.GetComponentInChildren<AgentRagdoll>();
                if (ragdoll != null && ragdoll.CanBeKnockedDown)
                {
                    knock(root, velocity);
                    return;
                }
            }

            if (root.GetComponentInChildren<IMountLeapMotor>() == null) return;

            // A blast with no horizontal component resolves to no direction at all. See the class
            // remarks: this used to leap along a zero vector.
            Vector3 away = Vector3.ProjectOnPlane(velocity, Vector3.up);
            if (away.sqrMagnitude < 1e-6f) return;

            float strength = Mathf.Clamp01(velocity.magnitude / Mathf.Max(referenceSpeed, 0.01f));
            float distance = Mathf.Lerp(leap.MinDistance, leap.MaxDistance, strength);
            float height = Mathf.Lerp(leap.MinHeight, leap.MaxHeight, strength);

            // Sent rather than applied, even though we are the authority and the motor is right
            // here. A ridden mount is owned by its RIDER, so a leap written on this machine is
            // overwritten by their next state update within a tick — the blast simply did nothing
            // to a mount a client was on, while working perfectly on one the host was riding.
            //
            // IsLeapAvailable is deliberately NOT checked here either. It is a property of the
            // motor on the machine that will actually run the leap, and this machine's copy of a
            // client-owned mount is not that machine.
            NetMessaging.NetSendTo(root, NetMsg.Leap, new NetArg
            {
                P = away.normalized * distance,
                A = Mathf.RoundToInt(height * 100f),
                B = Mathf.RoundToInt(leap.Duration * 1000f),
            }, NetTo.All);
        }
    }
}
