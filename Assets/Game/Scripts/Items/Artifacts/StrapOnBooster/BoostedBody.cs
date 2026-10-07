// The half of a booster that only one machine may run: moving the thing it is strapped to.
//
// Two rules meet here, and neither of them is negotiable.
//
// OWNERSHIP. A player's Rigidbody is the one body the server is NOT authoritative over — their
// NetworkTransform is owner-authoritative, so a force the server writes into it is overwritten by
// the owner's next state update, within a tick, with nothing in the console. So the server owns the
// FLAG (the booster exists, it is clamped here, it burns until this time) and the machine that owns
// the body reads that flag and does the pushing. For a crate, a creature or a parked hull that
// machine is the server; for a player on their own feet it is their client; for a mount handed to
// its rider it is the rider's. Network.Owns answers all four with one question.
//
// ORDER. PlayerMovement.FixedUpdate ASSIGNS horizontal velocity rather than adding to it, so a push
// applied before it runs is not reduced, it is deleted. The execution order below is load-bearing —
// it is the value LeashedBody and FlungBody use, for exactly this reason.
//
// And one thing this deliberately does NOT do: decide what a booster is worth. It applies whatever
// the boosters clamped to this body report, and they were the ones aimed.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles.Ornithopter;

namespace SpaceGame.Items
{
    /// <summary>
    /// Applies every booster strapped to this body, on the machine that drives it, after the body
    /// has moved itself.
    ///
    /// <para>
    /// Added on demand rather than authored on a prefab, because a booster can be stuck to anything
    /// with a surface and the alternative is a component on every crate in the world for a case
    /// almost none of them ever hit. Same shape and same reason as <c>LeashedBody.Ensure</c>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // added in code, never by hand
    [DefaultExecutionOrder(200)] // after PlayerMovement — see the note at the top of this file
    public sealed class BoostedBody : MonoBehaviour
    {
        private readonly List<BoosterMount> boosters = new();

        private Rigidbody body;
        private bool bodyResolved;
        private PlayerMovement movement;
        private bool movementResolved;

        // Who to bill, and until when. Kept on this component rather than looked up at the moment
        // of the crash, because by then the booster that caused it has usually burned out and let
        // go — most of a boosted ride happens after the motor stops.
        private BoosterImpactConfig impact;
        private Transform impactSource;
        private float impactUntil = float.NegativeInfinity;

        // Whether this machine is the one that actually pushed. A towed machine prices its own
        // arrivals — the ornithopter has carried that rule since long before this item existed —
        // so billing it again here would charge one crash twice.
        private bool billsImpacts;

        /// <summary>The boosted-body component for <paramref name="target"/>, creating one if needed.</summary>
        public static BoostedBody Ensure(GameObject target)
        {
            if (target == null) return null;

            return target.TryGetComponent(out BoostedBody existing)
                ? existing
                : target.AddComponent<BoostedBody>();
        }

        /// <summary>A booster has clamped on and lit. Idempotent.</summary>
        public void Attach(BoosterMount booster)
        {
            if (booster == null || boosters.Contains(booster)) return;

            boosters.Add(booster);
        }

        /// <summary>A booster has burnt out, been taken away, or gone with its scene.</summary>
        public void Detach(BoosterMount booster) => boosters.Remove(booster);

        /// <summary>
        /// This body's Rigidbody, resolved on demand. Not in <c>Awake</c>: this component is added
        /// at runtime, and Unity raises no <c>Awake</c> for an <c>AddComponent</c> outside play mode.
        /// </summary>
        private Rigidbody Body
        {
            get
            {
                if (bodyResolved) return body;

                body = GetComponent<Rigidbody>();
                bodyResolved = true;
                return body;
            }
        }

        /// <summary>
        /// The movement controller, when this body is a player. Its presence is the question being
        /// asked — see <see cref="Push"/> — rather than a convenience.
        /// </summary>
        private PlayerMovement Movement
        {
            get
            {
                if (movementResolved) return movement;

                movement = GetComponent<PlayerMovement>();
                movementResolved = true;
                return movement;
            }
        }

        private void FixedUpdate()
        {
            Prune();
            if (boosters.Count == 0) return;

            // Recorded on every machine, so a peer that later becomes the one that owns this body
            // still knows who to blame. Cheap, and it is the only state a booster leaves behind.
            impact = boosters[0].Impact;
            impactSource = boosters[0].Igniter;
            impactUntil = Time.time + boosters[0].ImpactWindowSeconds;

            // Everyone has one of these once a booster has been stuck to them; only the machine
            // that drives the body may move it. Elsewhere this is a replica whose pose is somebody
            // else's to publish.
            if (!Network.Owns(this)) return;

            Push();
        }

        /// <summary>
        /// One step of every booster on this body.
        ///
        /// <para>
        /// The three branches are the three kinds of thing a booster can be stuck to, and the order
        /// matters. A machine that integrates its own motion is ASKED, never pushed — its Rigidbody
        /// is not what moves it, and a force written there is discarded without a word. That branch
        /// is also what catches a mounted rider, whose body is kinematic and parented into a seat:
        /// the vehicle above them is the thing that is going anywhere. A player on their own feet
        /// gets a velocity write, because their controller assigns velocity and would delete a
        /// force. Everything else takes a force at the clamp, which is what makes an off-centre
        /// booster spin a crate instead of sliding it — and that is the point of the item.
        /// </para>
        /// </summary>
        private void Push()
        {
            billsImpacts = false;

            ITowable tow = GetComponentInParent<ITowable>();
            if (tow != null)
            {
                // One ask, however many boosters: the machine decides for itself what a push is
                // worth and what its own gait, airframe or NavMesh will take. The combined axis is
                // the honest reading of two boosters pointing different ways.
                //
                // The ACCELERATION, never a point to be pulled towards. A booster has no
                // destination, so the anchor this used to invent — 60 m out along the thrust — was
                // a distance the machine was free to read literally, and a NavMesh creature did:
                // it moved the full 60 m every physics step and was out of the world before the
                // flame was drawn. See ITowable.RequestThrust.
                Vector3 combined = CombinedThrust();
                if (combined.sqrMagnitude < 1e-6f) return;

                tow.RequestThrust(combined);
                return;
            }

            Rigidbody rigidbody = Body;
            if (rigidbody == null || rigidbody.isKinematic) return;

            billsImpacts = true;

            if (Movement != null)
            {
                // A player. PlayerMovement assigns velocity every step, so this adds to the value
                // it has just written and then latches it — without the latch, air control lerps
                // the horizontal half back to walking speed inside a fifth of a second and the
                // launch is gone before it has left the ground.
                rigidbody.linearVelocity += CombinedThrust() * Time.fixedDeltaTime;
                Movement.CarryMomentum();
                return;
            }

            // A force rather than an acceleration, scaled by the target's own mass. That leaves the
            // linear result mass-independent — a crate and a hull leave at the same rate — while
            // the torque about the centre of mass scales with the body the same way its inertia
            // does, so an off-centre clamp spins a heavy thing exactly as readily as a light one.
            foreach (BoosterMount booster in boosters)
            {
                rigidbody.AddForceAtPosition(booster.ThrustDirection * (booster.Acceleration * rigidbody.mass),
                                             booster.AttachPoint, ForceMode.Force);
            }
        }

        /// <summary>Every booster's push, summed, as an acceleration in world space.</summary>
        private Vector3 CombinedThrust()
        {
            var thrust = Vector3.zero;

            foreach (BoosterMount booster in boosters)
                thrust += booster.ThrustDirection * booster.Acceleration;

            return thrust;
        }

        /// <summary>
        /// Arriving somewhere, hard.
        ///
        /// <para>
        /// Billed on the machine that was doing the pushing, which is also the machine whose copy
        /// of this body actually collided with anything: a remote replica is kinematic and reports
        /// contacts nobody should be charged for. <c>NetDamage.Apply</c> takes it from there — it
        /// lands directly on the server and becomes a request from anywhere else.
        /// </para>
        /// <para>
        /// Closing speed is read off the live Rigidbody inside the callback, the way the jetpack
        /// reads it, and carries the same limitation: the solver has already begun eating the
        /// velocity by the time this runs, so the very hardest hits under-report. That is why the
        /// safe threshold is generous rather than tight.
        /// </para>
        /// <para>
        /// The window closes on the first hit it charges for. A crate that arrives at a rock face
        /// and then tumbles down it is one crash, not eleven.
        /// </para>
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            if (!billsImpacts || impact == null || Time.time > impactUntil) return;
            if (collision.contactCount == 0 || !Network.Owns(this)) return;

            Rigidbody rigidbody = Body;
            if (rigidbody == null) return;

            float closing = OrnithopterCrash.ClosingSpeed(rigidbody.linearVelocity,
                                                          collision.GetContact(0).normal);
            int damage = OrnithopterCrash.ImpactDamage(closing, impact);
            if (damage <= 0) return;

            impactUntil = float.NegativeInfinity;
            NetDamage.Apply(gameObject, damage, impactSource);
        }

        /// <summary>
        /// Drop boosters that have gone without saying so — destroyed with their scene, or taken
        /// away by a despawn that beat their own teardown.
        /// </summary>
        private void Prune()
        {
            for (int i = boosters.Count - 1; i >= 0; i--)
                if (boosters[i] == null || !boosters[i].IsBurning) boosters.RemoveAt(i);
        }
    }
}
