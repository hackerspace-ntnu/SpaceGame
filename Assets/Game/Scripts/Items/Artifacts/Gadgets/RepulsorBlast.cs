using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Pure math for the repulsor gauntlet's blast — kept free of scene and network state so it is
    /// unit-testable and so the authority and the cosmetic sweep provably agree on the same cone.
    /// </summary>
    public static class RepulsorBlast
    {
        /// <summary>Charge in [minCharge, 1] from seconds held. A tap still puffs.</summary>
        public static float ChargeFrom(float heldSeconds, float chargeTime, float minCharge)
            => Mathf.Max(minCharge, Mathf.Clamp01(heldSeconds / Mathf.Max(chargeTime, 0.01f)));

        /// <summary>Is the point inside the blast cone? Point-blank always counts.</summary>
        public static bool InCone(Vector3 origin, Vector3 aimDir, Vector3 point,
                                  float radius, float halfAngleDeg)
        {
            Vector3 to = point - origin;
            if (to.sqrMagnitude > radius * radius) return false;
            if (to.sqrMagnitude < 1e-4f) return true;
            return Vector3.Angle(aimDir, to) <= halfAngleDeg;
        }

        /// <summary>
        /// A launch velocity: `flatDir` flattened onto the ground plane, tilted up by
        /// `upwardTiltDeg`, at `speed`.
        ///
        /// The tilt is load-bearing, not flavour. PlayerMovement never touches vertical velocity,
        /// so the up-component survives unconditionally — and while it is still climbing,
        /// PlayerMovement counts the body as in flight and CarryMomentum protects the horizontal
        /// half. Note it does NOT un-ground the victim: the ground probe keeps answering
        /// "grounded" for the first ~0.6 m, which is why the rise itself is the signal
        /// (PlayerMovement.ShouldEndCarry).
        ///
        /// Extracted so the repulsor's radial blast and the Sucker Puncher's straight-line punch
        /// launch bodies by the same rule. They differ only in where the direction comes from —
        /// away from a blast origin for one, along the fist for the other — and that difference
        /// belongs at the call site, not in two copies of this trig.
        /// </summary>
        public static Vector3 Launch(Vector3 flatDir, float upwardTiltDeg, float speed)
        {
            Vector3 dir = Vector3.ProjectOnPlane(flatDir, Vector3.up);
            if (dir.sqrMagnitude < 1e-6f) return Vector3.up * speed;

            float rad = upwardTiltDeg * Mathf.Deg2Rad;
            return (dir.normalized * Mathf.Cos(rad) + Vector3.up * Mathf.Sin(rad)) * speed;
        }

        /// <summary>
        /// How much of the authored speed survives at <paramref name="distance"/> from the blast
        /// origin: full inside the core, then falling to <paramref name="edgeFalloff"/> at the rim.
        ///
        /// <para>
        /// The core exists because falloff measured from the origin outward makes almost every hit
        /// a weak one. With a 13 m radius and no core, a body standing halfway out — the ordinary
        /// case, not the edge case — already keeps only two thirds of the speed, which for a player
        /// victim lands close enough to sprint speed that CarryMomentum hands the fling straight
        /// back to the movement lerp. The blast then reads as a shove no matter how the peak is
        /// tuned. Inside the core the number the designer authored is the number the victim gets;
        /// outside it, distance still matters (GDC-L1-FEEL-0007 — tune for the sensation of a
        /// blast, not for an inverse-square field).
        /// </para>
        /// </summary>
        /// <param name="coreFraction">
        /// Fraction of the radius that takes undiminished force. 0 falls off from the origin, which
        /// is right for a wave centred on the point of CONTACT (the Sucker Puncher) and wrong for
        /// one centred on the caster's own chest.
        /// </param>
        public static float DistanceFalloff(float distance, float radius, float coreFraction,
                                            float edgeFalloff)
        {
            float core = Mathf.Clamp01(coreFraction) * Mathf.Max(radius, 0.01f);
            float outer = Mathf.Max(radius, 0.01f) - core;
            if (outer <= 1e-4f) return 1f;

            float t = Mathf.Clamp01((distance - core) / outer);
            return Mathf.Lerp(1f, edgeFalloff, t);
        }

        /// <summary>
        /// The horizontal direction a blast throws a body.
        ///
        /// <para>
        /// Two blasts want two different answers and this is the dial between them. A DETONATION —
        /// a rocket, a fist landing on a chest — is centred on the point of contact, and everything
        /// around it should be thrown away from that point: <paramref name="aimBias"/> 0. A
        /// directed blast is not that shape at all. Its origin is the caster's own chest, so a
        /// purely radial push throws the body standing beside them sideways and the one behind
        /// their shoulder backwards, and the weapon reads as a bomb the player is standing on top
        /// of rather than something they aimed. Biasing toward the aim throws the whole cone the
        /// one way the player pointed, which is the sensation the weapon is actually promising
        /// (GDC-L1-FEEL-0007 — the radial field is the physically honest answer and the wrong one).
        /// </para>
        ///
        /// <para>
        /// It is a blend and not a switch because the radial component is what stops a crowd from
        /// being compressed into a single stack: at bias 1 every body in the cone leaves along the
        /// exact same vector and they land on top of each other. Keeping a minority of radial
        /// spreads the fan out while the aim still decides where the fan goes.
        /// </para>
        /// </summary>
        /// <param name="aimBias">0 = straight away from the origin, 1 = straight down the aim.</param>
        public static Vector3 PushDirection(Vector3 origin, Vector3 aimDir, Vector3 targetPos,
                                            float aimBias)
        {
            Vector3 aimFlat = Vector3.ProjectOnPlane(aimDir, Vector3.up);
            Vector3 radialFlat = Vector3.ProjectOnPlane(targetPos - origin, Vector3.up);

            // A body standing exactly on the origin has no radial direction to offer, and an aim
            // straight up or down has no flat one. Either way the other term is the whole answer.
            if (radialFlat.sqrMagnitude < 1e-4f) return aimFlat.sqrMagnitude > 1e-6f
                ? aimFlat.normalized
                : Vector3.zero;
            if (aimFlat.sqrMagnitude < 1e-6f) return radialFlat.normalized;

            Vector3 blended = Vector3.Lerp(radialFlat.normalized, aimFlat.normalized,
                                           Mathf.Clamp01(aimBias));

            // Lerping between two opposed unit vectors passes through zero — which happens whenever
            // a body is directly BEHIND the caster at bias 0.5. Fall back to the aim there rather
            // than to a normalize of nothing.
            return blended.sqrMagnitude > 1e-6f ? blended.normalized : aimFlat.normalized;
        }

        /// <summary>
        /// Velocity to hand a body caught in a DIRECTED blast: <see cref="PushDirection"/> at
        /// <paramref name="aimBias"/>, tilted upward by <see cref="Launch"/>, at
        /// <paramref name="speed"/> less the <see cref="DistanceFalloff"/> for its distance.
        ///
        /// <para>
        /// <see cref="FlingVelocity"/> is this same function pinned to aimBias 0 — the two are one
        /// code path on purpose, so a detonation and a directed blast can never drift apart on
        /// falloff or tilt while differing on the one thing they are supposed to differ on.
        /// </para>
        /// </summary>
        public static Vector3 DirectedFling(Vector3 origin, Vector3 aimDir, Vector3 targetPos,
                                            float radius, float speed, float upwardTiltDeg,
                                            float coreFraction, float edgeFalloff, float aimBias)
        {
            Vector3 dir = PushDirection(origin, aimDir, targetPos, aimBias);
            float scaled = speed * DistanceFalloff((targetPos - origin).magnitude, radius,
                                                   coreFraction, edgeFalloff);
            return Launch(dir, upwardTiltDeg, scaled);
        }

        /// <summary>
        /// Velocity to hand a flung body: horizontally away from the blast origin, tilted upward.
        /// The tilt is load-bearing — see <see cref="Launch"/> for why the RISE, not the resulting
        /// clearance, is what keeps CarryMomentum holding the horizontal half.
        /// Speed is undiminished inside the core and falls off past it; edge hits may dip under the
        /// CarryMomentum floor (~sprint speed) by design — an edge hit is a puff, not a launch.
        ///
        /// <para>
        /// <paramref name="coreFraction"/> and <paramref name="edgeFalloff"/> have no defaults on
        /// purpose: every caller serializes its own, and a default here would be a second value to
        /// tune that silently wins whenever somebody forgets to pass the field.
        /// </para>
        /// </summary>
        public static Vector3 FlingVelocity(Vector3 origin, Vector3 aimDir, Vector3 targetPos,
                                            float charge, float radius,
                                            float minSpeed, float maxSpeed, float upwardTiltDeg,
                                            float coreFraction, float edgeFalloff)
            => DirectedFling(origin, aimDir, targetPos, radius,
                             Mathf.Lerp(minSpeed, maxSpeed, charge), upwardTiltDeg,
                             coreFraction, edgeFalloff, aimBias: 0f);
    }
}
