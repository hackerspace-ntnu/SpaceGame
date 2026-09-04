using SpaceGame.Vehicles.Ornithopter;
using UnityEngine;

namespace SpaceGame.Gear.Wingsuit
{
    /// <summary>
    /// The stick, as pure functions: how a first-person player's mouse and keys become the four
    /// axes <see cref="OrnithopterFlightModel"/> expects, and how far the view leans with the bank.
    ///
    /// <para>
    /// Separate from the MonoBehaviour for the reason the flight model is: this is where "fly where
    /// you look" is actually defined, it is the part that can be wrong in a way nobody notices for
    /// a week, and checking it should not need a Rigidbody, a camera and a session.
    /// </para>
    /// </summary>
    public static class WingsuitControl
    {
        /// <summary>
        /// Move the commanded nose angle by a mouse movement, clamped to what the suit will hold.
        ///
        /// <para>
        /// The mouse is a POSITION control here, not a rate: it aims the nose and the nose stays
        /// where it was aimed, which is what makes the crosshair mean something. A self-centring
        /// pitch stick would drift the nose back to level whenever the player stopped moving the
        /// mouse, and "look where you are going" would become "hold the mouse still and sink".
        /// </para>
        /// </summary>
        public static float AimNose(float commandedPitch, float mouseDegrees, float maxPitch) =>
            Mathf.Clamp(commandedPitch + mouseDegrees, -maxPitch, maxPitch);

        /// <summary>
        /// The pitch stick that closes the gap between where the nose is aimed and where it is.
        ///
        /// <para>
        /// Deliberately an error term rather than the commanded angle itself. The model owns the
        /// pitch RATE and fades it with airspeed and with the stall, and handing it a position
        /// would throw all of that away — a stalled wing would snap its nose round as briskly as a
        /// fast one. Saturating a few degrees out keeps it feeling direct: within
        /// <paramref name="saturationDegrees"/> the response eases in, past it the stick is simply
        /// hard over.
        /// </para>
        /// </summary>
        public static float NoseStick(float commandedPitch, float currentPitch,
                                      float saturationDegrees)
        {
            float error = commandedPitch - currentPitch;
            if (saturationDegrees <= 0f) return Mathf.Sign(error);

            return Mathf.Clamp(error / saturationDegrees, -1f, 1f);
        }

        /// <summary>
        /// What the mouse's horizontal movement asks for: pushed by the movement, falling back to
        /// centre when the mouse stops.
        ///
        /// <para>
        /// A rate control, unlike the nose — the opposite choice, and for the opposite reason.
        /// Heading is a full circle with no resting angle to aim AT, so a position stick here would
        /// wind up without limit. The decay is what makes it read as "push and it swings, stop and
        /// it settles".
        /// </para>
        /// <para>
        /// Used for BOTH the bank and the flat rudder, deliberately. Turning a wing is banking it,
        /// and a mouse that only supplied a weak rudder meant the only real way to turn was A and D
        /// — which is not what "fly where you look" promises, and was the single most common
        /// complaint about how this steered. The mouse now rolls you into the turn the way it would
        /// in any flight game, and A/D still bank directly on top for anyone who wants them.
        /// </para>
        /// </summary>
        public static float Swing(float stick, float mouseDegrees, float decayPerSecond, float dt)
        {
            float pushed = Mathf.Clamp(stick + mouseDegrees, -1f, 1f);
            return Mathf.MoveTowards(pushed, 0f, decayPerSecond * dt);
        }

        /// <summary>
        /// The bank the wing is actually asked for: the mouse's swing and the strafe keys, together.
        ///
        /// Summed rather than one overriding the other, so holding A while pulling the mouse left
        /// banks harder rather than fighting itself, and clamped so the pair cannot exceed one
        /// stick's worth.
        /// </summary>
        public static float Bank(float swing, float strafe, float mouseShare) =>
            Mathf.Clamp(strafe + swing * mouseShare, -1f, 1f);

        /// <summary>
        /// Everything the pilot is asking of the wing this step.
        ///
        /// <para>
        /// <b>Flap is never positive.</b> This is the second of the two places "a wingsuit cannot
        /// climb under its own power" is enforced (the first is <see cref="WingsuitFlightConfig"/>,
        /// whose thrust is zero), and the belt and braces are deliberate: a config is a serialized
        /// field somebody can type a number into, and this is not. Negative flap is still allowed
        /// and is the tuck — arms in, area shed, dive.
        /// </para>
        /// </summary>
        public static OrnithopterFlightInput Stick(float noseStick, float rollStick,
                                                   float rudderStick, bool tucking) =>
            new OrnithopterFlightInput(
                pitch: Mathf.Clamp(noseStick, -1f, 1f),
                roll: Mathf.Clamp(rollStick, -1f, 1f),
                flap: tucking ? -1f : 0f,
                turn: Mathf.Clamp(rudderStick, -1f, 1f));

        /// <summary>
        /// How far the view leans into a bank, in degrees.
        ///
        /// <para>
        /// A fraction rather than the whole bank angle, and serialized all the way out to the
        /// prefab so it can be taken to zero: camera roll sells the turn and is also the single
        /// most reliable way to make somebody motion sick (GDC-L1-FEEL-0006 — dose it, and let the
        /// player have the dose).
        /// </para>
        /// </summary>
        public static float ViewRoll(float bankDegrees, float fraction) => bankDegrees * fraction;

        /// <summary>
        /// The state a glide starts in: flying along the player's LOOK direction, at the speed they
        /// already had.
        ///
        /// <para>
        /// The look direction rather than the velocity, and that is the whole rule. A player who
        /// double-taps in a fall is looking at the horizon they want to reach, and starting them on
        /// the flight path they were on — straight down — snapped the view to the ground and made
        /// the first second of every flight a recovery from a dive nobody asked for. The wing pack
        /// can read the velocity because it spawns a craft the pilot then aims; a wingsuit IS the
        /// aim, so it opens pointing where the eyes already are.
        /// </para>
        /// <para>
        /// **Speed is carried as a magnitude, so nothing is minted.** Rotating a fall onto the look
        /// direction converts vertical speed into horizontal, which is a real and deliberate gift —
        /// but it is the same joules, and altitude is untouched. What it buys is that the moment of
        /// deploy is legible: the horizon does not move, and the suit goes where you were pointing.
        /// </para>
        /// <para>
        /// <paramref name="minAirspeed"/> is a floor, not a target: stepping off a ledge at walking
        /// pace would otherwise start below the stall, where the wing makes nothing and the suit
        /// reads as broken on the one frame the player is watching hardest.
        /// </para>
        /// </summary>
        public static OrnithopterFlightState Deploy(Vector3 velocity, Vector3 lookDirection,
                                                    float speedCarry, float minAirspeed)
        {
            Vector3 look = lookDirection.sqrMagnitude > 1e-6f
                ? lookDirection.normalized
                : Vector3.forward;

            float speed = Mathf.Max(velocity.magnitude * speedCarry, minAirspeed);

            // Clamped just inside the model's own gamma limit: it stores the flight path as a
            // number of degrees and ±90 is the vertical it cannot express.
            float gamma = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(look.y, -1f, 1f)) * Mathf.Rad2Deg,
                                      -85f, 85f);

            // Launch sets Pitch = Gamma, so the wing opens at zero angle of attack along the line
            // the player is looking down. That is also what stops the view jumping: the camera is
            // slaved to -Pitch, and -Pitch is the pitch they already had.
            return OrnithopterFlightState.Launch(speed, FlightLaunch.HeadingOf(look), gamma);
        }
    }
}
