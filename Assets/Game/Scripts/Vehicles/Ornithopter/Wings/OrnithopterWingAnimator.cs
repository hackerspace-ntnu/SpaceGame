using UnityEngine;

namespace SpaceGame.Vehicles.Ornithopter
{
    /// <summary>
    /// Poses the ornithopter's 30-bone rig from the flight state. This is the whole articulation:
    /// the beat, the spread, the twist that makes a flap propel rather than just wave, the tail
    /// boom that pitches and the tail fan that turns.
    ///
    /// <b>Every rotation is a delta on the rest pose</b> that <see cref="OrnithopterWingRig"/>
    /// cached, applied on the axis the model's build record specifies:
    ///
    /// <code>
    /// Motion            Bone                  Axis      Per-side sign?
    /// Wing beat         Bone_Shoulder_L/R     local X    No
    /// Wing sweep        Bone_Arm_L/R          local Z    Yes
    /// Digit splay       Bone_Digit_*          local Z    Yes
    /// Digit twist       Bone_Digit_*          local Y    No
    /// Gear spin         Bone_Gear_L/R         local Y    either
    /// Tail fan splay    Bone_TailDigit_1..5   local Z    n/a
    /// Boom pitch        Bone_Boom_1/2         local X    n/a
    /// </code>
    ///
    /// The rule behind that table, and the reason the signs are not uniform: the two wing bones
    /// point outboard in OPPOSITE directions, so their local X and Y are already mirrored and the
    /// same angle on both sides comes out symmetric. Local Z points up on both sides and is not
    /// mirrored, so anything about Z needs an explicit per-side sign. This caused two real bugs
    /// during the model build, which is why it is written down rather than left to be rediscovered.
    ///
    /// <b>Nothing here is filtered at beat frequency.</b> The flap, the gear spin and the twist
    /// are all driven from <see cref="IOrnithopterFlightState.FlapPhase"/> directly. Smoothing
    /// anything that moves at the beat rate detunes it from the thing it is supposed to be
    /// showing — the lesson from the ostrich gait, where a filtered stride made the feet skate.
    /// Only quantities that genuinely change slowly (bank, pitch trim) are damped.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class OrnithopterWingAnimator : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Root of the imported armature. Leave empty to search this hierarchy for " +
                 "Bone_Root's parent chain.")]
        [SerializeField] private Transform armatureRoot;

        [Header("Beat")]
        [Tooltip("How far the shoulders swing above and below rest at full effort, degrees.")]
        [SerializeField, Range(0f, 80f)] private float flapAmplitude = 38f;

        [Tooltip("Amplitude the wings still move with while gliding, as a fraction of the above. " +
                 "A gliding bird's wings are not rigid — they breathe with the air.")]
        [SerializeField, Range(0f, 1f)] private float glideFlapFraction = 0.12f;

        [Tooltip("Degrees the drive wheels turn per beat. Purely visual, but it is what ties the " +
                 "mechanism to the wings instead of leaving it spinning decoratively.")]
        [SerializeField] private float gearDegreesPerBeat = 360f;

        [Tooltip("How far the crank throw leads the gear, degrees.")]
        [SerializeField] private float crankThrow = 25f;

        [Header("Spread")]
        [Tooltip("Digit splay when the wings are fully open, degrees. This is the fan angle the " +
                 "membrane is stretched across.")]
        [SerializeField, Range(0f, 90f)] private float splayOpen = 42f;

        [Tooltip("Digit splay when folded. Negative folds the digits back along the arm.")]
        [SerializeField, Range(-90f, 30f)] private float splayFolded = -34f;

        [Tooltip("How far the arms sweep forward as the wings open, degrees.")]
        [SerializeField] private float sweepOpen = 18f;

        [Tooltip("Arm sweep when folded — swept back against the fuselage.")]
        [SerializeField] private float sweepFolded = -46f;

        [Header("Twist")]
        [Tooltip("Digit twist at the bottom of the downstroke, degrees. This is what angles the " +
                 "membrane so a beat pushes air BACKWARDS and the craft forwards. Zero here and " +
                 "the wings flap without propelling anything.")]
        [SerializeField, Range(0f, 45f)] private float downstrokeTwist = 20f;

        [Tooltip("Twist on the upstroke, degrees. Negative feathers the wing so the recovery " +
                 "stroke slices rather than pushing the craft back down.")]
        [SerializeField, Range(-45f, 10f)] private float upstrokeTwist = -12f;

        [Tooltip("Extra twist the outer digits take over the inner ones. A real wing washes out " +
                 "toward the tip; a wing that twists uniformly reads as a stiff board.")]
        [SerializeField, Range(0f, 2f)] private float twistWashout = 0.6f;

        [Header("Roll")]
        [Tooltip("Differential twist between the wings per degree of bank. The wing on the inside " +
                 "of the turn twists down and the outer one up, which is how a bird banks.")]
        [SerializeField, Range(0f, 1f)] private float rollTwistPerDegree = 0.22f;

        [Header("Tail")]
        [Tooltip("Boom pitch at full stick, degrees. The back wing — this is what points the nose.")]
        [SerializeField, Range(0f, 45f)] private float boomPitchRange = 22f;

        [Tooltip("Tail fan splay when closed, degrees.")]
        [SerializeField, Range(-45f, 45f)] private float tailSplayClosed = 4f;

        [Tooltip("Tail fan splay when fully fanned, degrees. The fan opens to turn and to brake.")]
        [SerializeField, Range(0f, 80f)] private float tailSplayOpen = 34f;

        [Tooltip("How far the fan twists asymmetrically with turn input, degrees.")]
        [SerializeField, Range(0f, 40f)] private float tailYawSplay = 16f;

        [Header("Damping")]
        [Tooltip("Response of the SLOW channels only — bank, pitch trim, tail fan. Never applied " +
                 "to anything running at beat frequency.")]
        [SerializeField, Min(0.01f)] private float trimResponse = 8f;

        private readonly OrnithopterWingRig rig = new OrnithopterWingRig();
        private IOrnithopterFlightState flight;

        // Damped copies of the slow channels. Deliberately separate from the raw state so it is
        // impossible to accidentally route the beat through them.
        private float smoothedBank;
        private float smoothedPitch;
        private float smoothedTurn;

        public OrnithopterWingRig Rig => rig;
        public bool IsInitialised => rig.IsBuilt;

        private void Awake()
        {
            Initialise(GetComponent<IOrnithopterFlightState>());
        }

        /// <summary>
        /// Bind the rig and the flight source. Called from Awake with whatever motor sits on this
        /// GameObject; also callable directly so a test can drive the wings from a stub.
        /// </summary>
        public void Initialise(IOrnithopterFlightState flightSource)
        {
            flight = flightSource;
            if (!rig.IsBuilt)
                rig.Build(armatureRoot != null ? armatureRoot : transform);
        }

        private void LateUpdate() => Tick(Time.deltaTime);

        /// <summary>
        /// Pose the rig for one frame. Takes its timestep rather than reading <c>Time.deltaTime</c>
        /// so the articulation can be driven outside play mode — by a test, or by an editor
        /// preview. Reading the clock internally made the damped channels dead in edit mode, where
        /// <c>Time.deltaTime</c> is zero, so bank and pitch could not be exercised at all.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (flight == null || !rig.IsBuilt)
                return;

            // A zero or negative step snaps rather than freezing. Without this the smoothing
            // filter multiplies by exp(0) = 1 and the slow channels never leave their start value.
            float k = deltaTime > 0f ? 1f - Mathf.Exp(-trimResponse * deltaTime) : 1f;
            smoothedBank = Mathf.Lerp(smoothedBank, flight.BankAngle, k);
            smoothedPitch = Mathf.Lerp(smoothedPitch, flight.PitchInput, k);
            smoothedTurn = Mathf.Lerp(smoothedTurn, flight.TurnInput, k);

            PoseWing(rig.Right);
            PoseWing(rig.Left);
            PoseTail();
        }

        private void PoseWing(OrnithopterWingRig.Wing w)
        {
            float phase = flight.FlapPhase;                       // unfiltered, by design
            float spread = Mathf.Clamp01(flight.WingSpread);
            float effort = Mathf.Clamp01(flight.FlapEffort);

            // ── Beat ──────────────────────────────────────────────────────────────────
            // A full sine about the rest pose. Amplitude never reaches zero: gliding wings still
            // breathe, they just do it gently.
            float beat = Mathf.Sin(phase * 2f * Mathf.PI);
            float amplitude = BeatAmplitude(effort) * spread;
            // Local X, and deliberately NO per-side sign — the mirrored bone axes do that for us.
            w.Shoulder.localRotation = w.ShoulderRest * Quaternion.Euler(beat * amplitude, 0f, 0f);

            // The drivetrain turns with the beat, so the visible mechanism agrees with the wings.
            float gearAngle = phase * gearDegreesPerBeat;
            w.Gear.localRotation = w.GearRest * Quaternion.Euler(0f, gearAngle, 0f);
            w.Crank.localRotation = w.CrankRest
                * Quaternion.Euler(0f, Mathf.Sin(phase * 2f * Mathf.PI) * crankThrow, 0f);

            // ── Sweep — local Z, per-side sign ────────────────────────────────────────
            float sweep = Mathf.Lerp(sweepFolded, sweepOpen, spread);
            w.Arm.localRotation = w.ArmRest * Quaternion.Euler(0f, 0f, sweep * w.Sign);

            // ── Digits: splay about Z (signed), twist about Y (unsigned) ──────────────
            float splay = Mathf.Lerp(splayFolded, splayOpen, spread);

            // Twist follows the beat: bite on the way down, feather on the way up. Using the
            // beat's own sign rather than a second oscillator keeps the two exactly in step —
            // a twist that lags the stroke looks like the wing is made of rubber.
            float strokeTwist = beat >= 0f
                ? Mathf.Lerp(0f, downstrokeTwist, beat)
                : Mathf.Lerp(0f, upstrokeTwist, -beat);
            strokeTwist *= effort * spread;

            // Banking twists the two wings against each other. The per-side multiply is what makes
            // this ANTI-symmetric where the beat is symmetric: because local Y is already mirrored
            // between the wings, the same number on both sides raises both leading edges, and it
            // takes opposite numbers to raise one and drop the other.
            float rollTwist = -smoothedBank * rollTwistPerDegree * w.Sign;

            for (int i = 0; i < OrnithopterWingRig.DigitCount; i++)
            {
                // Digit 1 is the leading spar carrying the tip; 5 is innermost. Fan them out so
                // the membrane opens as a fan rather than a rigid triangle.
                float across = i / (float)(OrnithopterWingRig.DigitCount - 1);
                float digitSplay = splay * Mathf.Lerp(1f, 0.35f, across);

                // Washout: the tip twists more than the root.
                float washout = 1f + twistWashout * (1f - across);
                float twist = (strokeTwist + rollTwist) * washout;

                w.Digits[i].localRotation = w.DigitRest[i]
                    * Quaternion.Euler(0f, twist, digitSplay * w.Sign);
            }
        }

        /// <summary>Beat amplitude: a floor while gliding, rising to full at full effort.</summary>
        private float BeatAmplitude(float effort)
        {
            return flapAmplitude * Mathf.Lerp(glideFlapFraction, 1f, effort);
        }

        private void PoseTail()
        {
            // Boom pitch — the back wing. Split across both segments so the boom curves rather
            // than hinging at one joint.
            float pitch = smoothedPitch * boomPitchRange;
            rig.Boom1.localRotation = rig.Boom1Rest * Quaternion.Euler(pitch * 0.6f, 0f, 0f);
            rig.Boom2.localRotation = rig.Boom2Rest * Quaternion.Euler(pitch * 0.4f, 0f, 0f);

            // The fan opens when the pilot asks for a turn or hauls the nose up to brake, and
            // closes when running straight and fast — same as a bird's tail.
            float demand = Mathf.Clamp01(Mathf.Abs(smoothedTurn) + Mathf.Max(0f, smoothedPitch));
            float splay = Mathf.Lerp(tailSplayClosed, tailSplayOpen, demand);

            for (int i = 0; i < OrnithopterWingRig.DigitCount; i++)
            {
                // The fan is symmetric about its middle spar, so the outer feathers on the inside
                // of the turn open further — that asymmetry is what actually yaws the machine.
                float across = i / (float)(OrnithopterWingRig.DigitCount - 1);   // 0..1
                float fromCentre = (across - 0.5f) * 2f;                          // -1..1
                float asymmetric = fromCentre * smoothedTurn * tailYawSplay;

                rig.TailDigits[i].localRotation = rig.TailDigitRest[i]
                    * Quaternion.Euler(0f, 0f, splay * fromCentre + asymmetric);
            }
        }

        /// <summary>Fold the wings and centre everything — the pose to spawn and despawn in.</summary>
        public void SnapToFolded()
        {
            if (!rig.IsBuilt) return;
            smoothedBank = smoothedPitch = smoothedTurn = 0f;
            rig.ResetToRest();
        }
    }
}
