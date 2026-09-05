using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Where a thrown loop goes: the arc, and nothing else.
    ///
    /// <para>
    /// Static and pure so the one thing this item cannot afford to get wrong is testable without a
    /// scene, a player or a frame. It also has to give the same answer on every machine — the throw
    /// is drawn by <c>Present</c> everywhere and only the thrower decides what it caught, so an arc
    /// that differed by a machine would have two players watching the rope pass two different sides
    /// of the same animal.
    /// </para>
    ///
    /// <para>
    /// <b>Loft is flight time, not extra upward speed.</b> This is the whole reason the file exists.
    /// The throw used to solve a correct ballistic arc to the aimed point and then add
    /// <c>throwArcHeight</c> straight onto the vertical component of the answer, which is not a
    /// loftier throw at the same target — it is a throw at a different target. At the prefab's own
    /// numbers the loop passed <b>1.6 m over</b> an animal aimed at from 12 m and <b>4.0 m over</b>
    /// one at 30 m, and crossed the target's own altitude a flat 13 m behind it every time
    /// (<c>2·arc·speed/g</c>, which is why the miss distance did not vary with range). The catch
    /// radius is between 0.22 m and 0.8 m. So aiming at a creature was a reliable way to miss it,
    /// and the item taught players to aim at its feet.
    /// </para>
    /// <para>
    /// A projectile cannot be both loftier and still land on the same spot without spending longer
    /// in the air. So the apex is chosen first and the flight time falls out of it, rather than the
    /// flight time being fixed by a muzzle speed and the loft bolted on afterwards where there is
    /// no freedom left to put it.
    /// </para>
    /// </summary>
    public static class LassoThrow
    {
        /// <summary>Below this the arc is degenerate and the quadratic below stops being one.</summary>
        private const float MinApex = 0.05f;

        /// <summary>
        /// How high this throw arcs, given how far it is going.
        ///
        /// <para>
        /// Ramped with range rather than constant, because a flick across a camp and a rope wound
        /// up across a canyon are not the same gesture and should not fly the same way. A short
        /// throw stays flat and quick — loft on it reads as the rope being lobbed rather than
        /// thrown — and a long one has to arc or it cannot reach at a speed the eye can follow.
        /// </para>
        /// </summary>
        public static float ApexFor(float distance, float maxRange, float minApex, float maxApex)
        {
            float t = maxRange > 0.01f ? Mathf.Clamp01(distance / maxRange) : 0f;
            return Mathf.Max(Mathf.Lerp(minApex, maxApex, t), MinApex);
        }

        /// <summary>
        /// The launch velocity that puts a loop through <paramref name="target"/>.
        ///
        /// <para>
        /// <paramref name="apex"/> is the peak height in metres above whichever end is higher, so
        /// it stays honest when throwing uphill: a throw at something above eye level still clears
        /// it rather than arriving on the way up. <paramref name="maxHorizontalSpeed"/> is a rail
        /// rather than the thing that sets the pace — when a throw would need to travel faster than
        /// the arm can plausibly send it, the flight is lengthened and the arc re-solved for the
        /// new time, which keeps the loop landing on the aimed point instead of short of it.
        /// </para>
        /// </summary>
        public static Vector3 SolveVelocity(Vector3 start, Vector3 target, float gravity, float apex,
                                            float maxHorizontalSpeed, out float flightTime)
        {
            float g = Mathf.Max(gravity, 0.01f);

            Vector3 delta = target - start;
            Vector3 flat = new Vector3(delta.x, 0f, delta.z);
            float distance = flat.magnitude;
            float rise = delta.y;

            // Peak above the START, so an uphill throw clears the target rather than reaching it on
            // the way up. Downhill the rise is negative and this is just the apex.
            float peak = Mathf.Max(rise, 0f) + Mathf.Max(apex, MinApex);

            float vy = Mathf.Sqrt(2f * g * peak);

            // The LATER root of `rise = vy·t − ½g·t²`: the loop arrives on the way DOWN, which is
            // what drops it over an animal rather than punching it up from underneath. The
            // discriminant is 2g(peak − rise) and `peak` is built from `rise` above, so it can
            // never go negative — the Max is for float noise alone.
            flightTime = (vy + Mathf.Sqrt(Mathf.Max(vy * vy - 2f * g * rise, 0f))) / g;

            // Too fast to be a throw: spend longer in the air instead, and re-solve the vertical
            // for that longer flight so the arc still passes through the same point. Solving it
            // again is the whole discipline of this file — adding to `vy` here would put the miss
            // straight back, just at long range only.
            if (maxHorizontalSpeed > 0.01f && distance / flightTime > maxHorizontalSpeed)
            {
                flightTime = distance / maxHorizontalSpeed;
                vy = rise / flightTime + 0.5f * g * flightTime;
            }

            Vector3 direction = distance > 1e-4f ? flat / distance : Vector3.zero;
            return direction * (distance / flightTime) + Vector3.up * vy;
        }

        /// <summary>
        /// Where a loop launched at <paramref name="velocity"/> is after <paramref name="time"/>.
        ///
        /// The flight itself is integrated frame by frame so the rope has something to trail
        /// behind; this closed form is what the aim preview draws and what the tests measure, and
        /// the two agreeing is the point of having it.
        /// </summary>
        public static Vector3 PointAt(Vector3 start, Vector3 velocity, float gravity, float time) =>
            start + velocity * time + Vector3.down * (0.5f * Mathf.Max(gravity, 0.01f) * time * time);
    }
}
