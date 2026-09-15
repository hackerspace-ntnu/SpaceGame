// Pure math for the bottled singularity: the throw's arc and the scatter its release is fanned out
// by. Kept free of scene and network state so it is unit-testable, and so the machine that DECIDES
// the throw and the machines that merely draw it provably agree — the same reason GravelBlastMath,
// DragonRocketFlight and NetGunFlight exist.
//
// NOTHING HERE ROLLS A NUMBER. The scatter is a closed-form function of the seed and of the
// direction being scattered, so every machine that knows those two knows the answer. A per-machine
// System.Random would diverge the moment two machines called it a different number of times, which
// is exactly what happens when one of them is a client that joined halfway through.
//
// The pull falloff and the outward fling are DELIBERATELY not here: they are RepulsorBlast's
// DistanceFalloff and DirectedFling, unchanged. A sphere with an inward sign for a second and a
// half and then one outward frame is the repulsor's cone with two dials turned, and a second copy
// of that trig is the drift this file would cause rather than prevent.
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The bottle's flight, and the seeded fan its release is thrown along.
    /// </summary>
    public static class SingularityMath
    {
        /// <summary>
        /// Where the thrown bottle is <paramref name="seconds"/> after it left the hand.
        ///
        /// <para>
        /// Closed form rather than an integrated Rigidbody, and that is load-bearing twice over.
        /// Every machine draws the same throw from the launch parameters alone, including one that
        /// joined mid-flight; and the bottle can stay OUT of <c>SaveablePolicy.NeedsSaving</c>,
        /// which opts in anything carrying a non-kinematic Rigidbody — see the note in
        /// <see cref="SingularityWell"/> about why a three-second effect must never reach a save
        /// file.
        /// </para>
        /// </summary>
        /// <param name="gravity">
        /// The world's own <c>Physics.gravity</c>, passed in rather than read here so this stays
        /// testable without a physics scene. It is -18 m/s² in this project, not -9.81, and taking
        /// it from the project setting is what keeps the drawn arc and the traced arc identical.
        /// </param>
        public static Vector3 FlightPoint(Vector3 origin, Vector3 velocity, Vector3 gravity,
                                          float seconds)
            => origin + velocity * seconds + 0.5f * (seconds * seconds) * gravity;

        /// <summary>
        /// How fast and which way the bottle is travelling at <paramref name="seconds"/>. The
        /// derivative of <see cref="FlightPoint"/>, so the nose points along the arc it is actually
        /// on rather than along the direction it was thrown.
        /// </summary>
        public static Vector3 FlightVelocity(Vector3 velocity, Vector3 gravity, float seconds)
            => velocity + gravity * seconds;

        /// <summary>
        /// Turn a release velocity off true by up to <paramref name="spreadDegrees"/>, in a way
        /// every machine agrees on.
        ///
        /// <para>
        /// The design asks for a scatter so the pile does not leave along one clean radial star,
        /// and asks for it to be identical everywhere. Both are satisfied by making the deflection
        /// a smooth function of the direction being deflected: two bodies leaving on nearly the
        /// same bearing are thrown nearly the same way, and the whole fan swirls one way or the
        /// other depending on the seed. <paramref name="lobes"/> is how many times the fan folds
        /// around the compass — 1 tips the whole burst to one side, 3 breaks it into three gusts.
        /// </para>
        /// <para>
        /// <b>Smooth on purpose, rather than hashed per body.</b> A hash of an object id would not
        /// survive the trip between machines at all, and a hash of a quantised position diverges
        /// hard across a bucket boundary — two machines whose float arithmetic differs in the last
        /// bit would then throw the same crate opposite ways. This degrades continuously instead: a
        /// direction that differs by a millionth of a degree is scattered to within a millionth of
        /// a degree.
        /// </para>
        /// </summary>
        /// <param name="velocity">The unscattered release velocity. Its magnitude is preserved.</param>
        public static Vector3 Scatter(int seed, Vector3 velocity, float spreadDegrees, float lobes)
        {
            float speed = velocity.magnitude;
            if (speed < 1e-4f || spreadDegrees <= 0f) return velocity;

            Vector3 dir = velocity / speed;

            // The azimuth is what the fan is folded around, so the swirl reads as a rotation of the
            // whole burst rather than as per-body noise.
            float azimuth = Mathf.Atan2(dir.z, dir.x);

            float yaw = spreadDegrees * Mathf.Sin(Phase(seed, 0) + azimuth * lobes);
            float pitch = spreadDegrees * Mathf.Sin(Phase(seed, 1) + azimuth * lobes);

            // Pitch about the horizontal axis across the throw. A body going straight up or down
            // has no such axis, and tilting it in a fixed world direction would be a bias rather
            // than a scatter — so it only gets the yaw, which for a vertical vector is nothing.
            Vector3 across = Vector3.Cross(Vector3.up, dir);
            Quaternion tilt = across.sqrMagnitude > 1e-6f
                ? Quaternion.AngleAxis(pitch, across.normalized)
                : Quaternion.identity;

            return Quaternion.AngleAxis(yaw, Vector3.up) * tilt * dir * speed;
        }

        /// <summary>
        /// A phase angle in [0, 2π) drawn from <paramref name="seed"/>.
        ///
        /// Integer avalanche arithmetic — the same on every machine and every platform, unlike
        /// anything that goes through a floating-point RNG. <paramref name="salt"/> is what lets one
        /// seed produce several independent phases without the caller having to carry several.
        /// </summary>
        private static float Phase(int seed, int salt)
        {
            uint hash = unchecked((uint)seed * 2654435761u + (uint)salt * 2246822519u);
            hash ^= hash >> 15;
            hash = unchecked(hash * 2246822519u);
            hash ^= hash >> 13;

            return hash / (float)uint.MaxValue * (2f * Mathf.PI);
        }
    }
}
