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
        /// Velocity to hand a flung body: horizontally away from the blast origin, tilted upward.
        /// The tilt is load-bearing — see <see cref="Launch"/> for why the RISE, not the resulting
        /// clearance, is what keeps CarryMomentum holding the horizontal half.
        /// Speed falls off toward the cone edge; edge hits may dip under the CarryMomentum floor
        /// (~sprint speed) by design — an edge hit is a puff, not a launch.
        ///
        /// <para>
        /// <paramref name="edgeFalloff"/> has no default on purpose: every caller serializes its
        /// own, and a default here would be a second value to tune that silently wins whenever
        /// somebody forgets to pass the field.
        /// </para>
        /// </summary>
        public static Vector3 FlingVelocity(Vector3 origin, Vector3 aimDir, Vector3 targetPos,
                                            float charge, float radius,
                                            float minSpeed, float maxSpeed, float upwardTiltDeg,
                                            float edgeFalloff)
        {
            Vector3 flat = Vector3.ProjectOnPlane(targetPos - origin, Vector3.up);
            Vector3 dir = flat.sqrMagnitude > 1e-4f
                ? flat.normalized
                : Vector3.ProjectOnPlane(aimDir, Vector3.up).normalized;

            float t = Mathf.Clamp01((targetPos - origin).magnitude / Mathf.Max(radius, 0.01f));
            float speed = Mathf.Lerp(minSpeed, maxSpeed, charge) * Mathf.Lerp(1f, edgeFalloff, t);

            return Launch(dir, upwardTiltDeg, speed);
        }
    }
}
