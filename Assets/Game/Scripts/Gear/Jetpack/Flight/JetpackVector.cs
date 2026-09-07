using UnityEngine;

namespace SpaceGame.Gear.Jetpack
{
    /// <summary>
    /// Where the nozzles are pointing, as a deflection from straight down.
    ///
    /// <para>
    /// Two angles rather than a direction vector, because they are what the hardware actually
    /// does: <see cref="Pitch"/> is the gimbal yoke swinging the nozzle fore and aft, and
    /// <see cref="Roll"/> is the pair rolling to throw the exhaust sideways. Keeping them as
    /// angles is what lets the rate limit be one honest number in degrees per second instead of
    /// a slerp whose speed depends on where it started.
    /// </para>
    /// <para>
    /// Sign convention, in the wearer's own frame: positive <see cref="Pitch"/> pushes the player
    /// FORWARD, positive <see cref="Roll"/> pushes them RIGHT. The nozzles themselves rake the
    /// opposite way, which is the joke of a vectoring nozzle and is not modelled here — the parts
    /// are rotated to match by <c>JetpackNozzles</c>.
    /// </para>
    /// </summary>
    public struct JetNozzle
    {
        /// <summary>Fore-and-aft deflection, degrees. Positive pushes the player forward.</summary>
        public float Pitch;

        /// <summary>Sideways deflection, degrees. Positive pushes the player right.</summary>
        public float Roll;

        /// <summary>Straight down, thrust straight up. Where a levitating pack settles.</summary>
        public static JetNozzle Vertical => default;

        /// <summary>How far off vertical, degrees. What <c>MaxDeflectionDegrees</c> clamps.</summary>
        public readonly float Magnitude => Mathf.Sqrt(Pitch * Pitch + Roll * Roll);

        /// <summary>
        /// The deflection as a rotation. Applied to <see cref="Vector3.up"/> it is the thrust
        /// direction in the wearer's frame; it is also exactly what a pod's parts are rotated by,
        /// and what goes on the wire.
        /// </summary>
        public readonly Quaternion Rotation => Quaternion.Euler(Pitch, 0f, -Roll);

        /// <summary>Unit thrust direction in the wearer's frame.</summary>
        public readonly Vector3 LocalThrust => Rotation * Vector3.up;

        /// <summary>
        /// Rebuild a deflection from a rotation that came off the wire.
        ///
        /// <para>
        /// <b>The two angles are NOT recovered symmetrically, and assuming they were is a bug this
        /// already had.</b> Unity composes <c>Euler(x, y, z)</c> as Z then X then Y, so
        /// <see cref="Rotation"/> sends up to
        /// <c>(sin roll, cos roll · cos pitch, cos roll · sin pitch)</c>. The pitch falls out of
        /// <c>atan2(z, y)</c> because the cosine cancels; the roll does not, and reading it the
        /// same way returns a value inflated by the pitch — 17.25° came back as 18.71° with a
        /// pitch of 23.5° on it, which is small enough to look like rounding and large enough to
        /// put a peer's pods visibly off the owner's.
        /// </para>
        /// </summary>
        public static JetNozzle FromRotation(Quaternion rotation)
        {
            Vector3 up = rotation * Vector3.up;

            return new JetNozzle
            {
                Pitch = Mathf.Atan2(up.z, up.y) * Mathf.Rad2Deg,
                Roll = Mathf.Asin(Mathf.Clamp(up.x, -1f, 1f)) * Mathf.Rad2Deg,
            };
        }
    }

    /// <summary>
    /// The steering, as pure arithmetic: what the player asked for, and how far the nozzles have
    /// got toward delivering it.
    ///
    /// <para>
    /// <b>The rate limit is the design.</b> Thrust follows <see cref="Advance"/>'s output, never
    /// <see cref="Command"/>'s — so pressing W does not move the player forward, it starts the
    /// nozzles swinging, and the push arrives as they get there. That single lag is where the
    /// whole skill of the jetpack lives: you fly arcs rather than corners, you set up a turn
    /// before you need it, and stopping is a thing you plan (<c>GDC-L1-FEEL-0008</c> — commitment
    /// chosen deliberately, and <c>GDC-L1-DESIGN-0005</c>, depth out of one rule rather than out
    /// of added mechanics).
    /// </para>
    /// <para>
    /// It does not break <c>GDC-L1-FEEL-0002</c>: the input is HEARD instantly and the nozzles
    /// start moving on the frame of the press, with the flames and the pods showing it. What is
    /// deliberate is the resolution time, not the acknowledgement.
    /// </para>
    /// </summary>
    public static class JetpackVector
    {
        /// <summary>
        /// Where the pilot is asking the nozzles to point.
        ///
        /// <para>
        /// The keys pick a DIRECTION in the wearer's horizontal plane and the look picks how hard
        /// to commit to it. Looking down while asking for forward rakes the nozzles further over,
        /// which trades climb for speed; looking up backs them off toward vertical and the same
        /// key climbs instead. That is why the same W feels different depending on where you are
        /// pointed, which is what "the direction is not strictly based on the keys" asks for.
        /// </para>
        /// <para>
        /// The look is a MULTIPLIER on a key demand rather than a term added to it, deliberately:
        /// added, looking down would drift a hands-off levitate forwards, and a player who lets go
        /// of everything must come to a stop or the machine cannot be parked.
        /// </para>
        /// </summary>
        /// <param name="move">Movement input: x strafes, y is forward.</param>
        /// <param name="lookPitchDegrees">The player's look pitch in PlayerLook's own convention —
        /// positive looks DOWN.</param>
        public static JetNozzle Command(Vector2 move, float lookPitchDegrees, JetpackConfig cfg)
        {
            if (cfg == null) return JetNozzle.Vertical;

            float demand = Mathf.Clamp01(move.magnitude);
            if (demand <= 1e-4f) return JetNozzle.Vertical;

            // Looking straight down (+90) commits fully and then some; straight up backs off.
            // Clamped at zero so an extreme upward look parks the nozzles rather than reversing
            // them — a key that pushes you BACKWARDS because of where your head is would be
            // unreadable.
            float look = 1f + cfg.LookPitchShare * (lookPitchDegrees / 90f);

            // Full stick is deliberately NOT full deflection. The look is a multiplier, so if the
            // keys alone already saturated the clamp there would be nothing left for it to scale
            // and where the player was pointed would mean nothing at exactly the moment they were
            // asking for the most — which is the whole feature. NeutralDeflectionShare is the room
            // left for it.
            float commit = demand * cfg.NeutralDeflectionShare * Mathf.Max(0f, look);
            float magnitude = cfg.MaxDeflectionDegrees * Mathf.Clamp01(commit);

            Vector2 direction = move / Mathf.Max(move.magnitude, 1e-4f);

            return new JetNozzle
            {
                Pitch = magnitude * direction.y,
                Roll = magnitude * direction.x,
            };
        }

        /// <summary>
        /// Swing the nozzles toward the command at the config's rate, and never past its
        /// deflection limit.
        ///
        /// <para>
        /// Moved as a two-dimensional vector rather than one axis at a time, because a nozzle
        /// swings through an arc: per-axis it would travel the diagonal at 1.41x the stated rate,
        /// so a diagonal input would be quicker to answer than a straight one for no reason a
        /// player could ever see.
        /// </para>
        /// </summary>
        public static JetNozzle Advance(JetNozzle current, JetNozzle command, JetpackConfig cfg,
                                        float dt)
        {
            if (cfg == null || dt <= 0f) return current;

            Vector2 from = new Vector2(current.Roll, current.Pitch);
            Vector2 to = new Vector2(command.Roll, command.Pitch);

            if (to.magnitude > cfg.MaxDeflectionDegrees)
                to = to.normalized * cfg.MaxDeflectionDegrees;

            Vector2 next = Vector2.MoveTowards(from, to, cfg.VectorRateDegreesPerSecond * dt);

            return new JetNozzle { Roll = next.x, Pitch = next.y };
        }

        /// <summary>
        /// The world-space direction the exhaust is pushing the player, from the nozzles and the
        /// body's heading.
        ///
        /// <para>
        /// The body's yaw is the only part of its rotation that goes in. The capsule never pitches
        /// or rolls — three metres of upright collider that the ground probe, the crouch and the
        /// head look all assume stands up — so composing its full rotation would fold in a lean
        /// that is only ever drawn, never simulated.
        /// </para>
        /// </summary>
        public static Vector3 WorldThrust(JetNozzle nozzle, float headingDegrees) =>
            Quaternion.Euler(0f, headingDegrees, 0f) * nozzle.LocalThrust;
    }
}
