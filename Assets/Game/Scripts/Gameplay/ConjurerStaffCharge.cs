// The charge that gathers on the staff while the conjurer holds it up.
//
// ConjurerCastModule spawns this on the frame a cast begins, parented to the
// StaffTip bone -- the emitter staff.py puts above the turbine -- and destroys it
// when the bolt lands. Everything below is therefore presentation with a known,
// fixed lifetime: it never decides when to stop, and it must not touch anything
// the server owns.
//
// ---- it replaces ConjurerChestCharge, and it is doing a different job ---------
//
// The old effect lived on a ring in the creature's chest and its arcs ran INWARD,
// from the ring to the two palms hovering either side of it, then converged on the
// gap between them. That gap was the muzzle: the bolt came out of the creature and
// flew at the target, so the wind-up had to say "something is being loaded HERE".
//
// Nothing about this attack is like that. The staff is a conductor held as high as
// the arm reaches, and the bolt comes down out of the sky somewhere else entirely.
// So the arcs run the other way: they start on the turbine and, as the charge
// builds, more and more of them stop reaching back to the blades and shoot
// straight UP instead. That progression is the whole idea -- the effect's job is
// to tell the player, before anything is falling, that the answer is coming from
// above and not from the creature.
//
// ---- why it re-points arcs instead of spawning them ---------------------------
//
// LightningBoltEffect already draws a jagged, re-kinking discharge between two
// world points, which is exactly what an arc off a blade tip is. What it normally
// ALSO does is destroy itself after `duration` -- right for a strike that happens
// once, wrong for a four-second wind-up, which would churn a hundred instances
// through Instantiate and Destroy for no visual gain.
//
// So the arcs are authored with duration = 0, which that component reads as "do
// not self-destruct" (see its Update), and this script re-aims the same handful of
// them a few times a second. The snap to an unrelated shape is what sells it as
// electricity, and re-aiming produces exactly the same snap as respawning.
//
// ---- why the fan is a radius and not a bone -----------------------------------
//
// The blades are three swept ribbons inside ONE mesh on ONE bone, because they
// never move independently -- see staff.py on why that is one object and not four.
// There is therefore no per-blade-tip transform to hang an arc off, and adding
// three empty bones purely so this script could find them would put geometry in
// the FBX to serve a cosmetic. The turbine is a circle of known radius a known
// distance below the emitter, so the endpoints are sampled off that circle
// instead, which also lets them land anywhere along a blade rather than only at
// its tip.
using UnityEngine;

namespace SpaceGame.Gameplay
{
    public class ConjurerStaffCharge : MonoBehaviour
    {
        // There is deliberately no glowing core here.
        //
        // An emissive sphere that swelled at the emitter used to be the centrepiece of
        // this effect, inherited from the chest charge before it, where a ball growing
        // inside a ring was the whole picture. On the end of a staff it read as a blue
        // balloon stuck to a stick, and it hid the turbine -- which is the part that
        // actually tells the player what is happening, because it spins up.
        //
        // The light stays. A Light has no geometry, so it brightens the blades and the
        // arcs without drawing a shape of its own, which is the effect that was wanted
        // from the sphere in the first place.
        [Header("Parts")]
        [Tooltip("Arcs re-pointed between the emitter, the turbine and the sky. " +
                 "Authored with duration 0 so they persist instead of destroying " +
                 "themselves.")]
        [SerializeField] private LightningBoltEffect[] arcs;

        [SerializeField] private Light glow;

        [Header("Timing")]
        [Tooltip("Time from the start of the cast to the bolt landing. Must match " +
                 "ConjurerCastModule.castSeconds, which the builder derives from the " +
                 "Attack clip's fire frame.")]
        [SerializeField] private float chargeSeconds = 4f;

        [Tooltip("Seconds between re-rolls of which points each arc bridges.")]
        [SerializeField] private float restrikeInterval = 0.06f;

        [Header("The turbine")]
        [Tooltip("Radius of the fan in metres, and how far below the emitter its " +
                 "plane sits. staff.py's FAN_R1 and the gap from HUB_Z to TOP_Z, " +
                 "converted by the model's import scale.")]
        [SerializeField] private float fanRadius = 1.2f;
        [SerializeField] private float fanDrop = 1.4f;

        [Header("The sky")]
        [Tooltip("How far up the skyward arcs reach at full charge, in metres. The " +
                 "arcs that go up are what say the strike is coming from above.")]
        [SerializeField] private float skyReach = 14f;

        [Tooltip("Fraction of the arcs already pointing skyward at the very start. " +
                 "The rest convert as the charge builds, so the effect turns from " +
                 "a spinning-up rotor into a column reaching for the clouds.")]
        [SerializeField] [Range(0f, 1f)] private float skyAtStart = 0.1f;

        [Header("Growth")]
        [SerializeField] private float startIntensity = 0.6f;
        [SerializeField] private float endIntensity = 6f;

        private float _elapsed;
        private float _sinceRestrike;

        /// <summary>
        /// Charge progress, 0 to 1. Drives how much of the effect has turned skyward
        /// as well as how bright it is.
        /// </summary>
        private float Progress =>
            chargeSeconds > 0f ? Mathf.Clamp01(_elapsed / chargeSeconds) : 1f;

        private void Awake()
        {
            // Struck once up front so the first frame draws arcs rather than a set of
            // zero-length lines at the origin.
            Restrike();
            Apply(0f);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            _sinceRestrike += Time.deltaTime;

            if (_sinceRestrike >= restrikeInterval)
            {
                _sinceRestrike = 0f;
                Restrike();
            }

            Apply(Progress);
        }

        /// Brighten. Eased rather than linear so the last second escalates visibly --
        /// that acceleration is the part the player reads as "this is about to go off".
        private void Apply(float t)
        {
            if (glow == null) return;
            glow.intensity = Mathf.Lerp(startIntensity, endIntensity, t * t);
        }

        private void Restrike()
        {
            if (arcs == null) return;

            float skyward = Mathf.Lerp(skyAtStart, 1f, Progress);

            for (int i = 0; i < arcs.Length; i++)
            {
                LightningBoltEffect arc = arcs[i];
                if (arc == null) continue;

                GetEndpoints(i, skyward, out Vector3 from, out Vector3 to);
                arc.Strike(from, to);
            }
        }

        /// Both ends of one arc.
        ///
        /// Everything is anchored on this transform, which is parented to the StaffTip
        /// bone, so all of it rides the staff through the raise and the tremble for
        /// free -- and stays correct when the arm lifts the emitter ten metres.
        ///
        /// `transform.up` rather than `Vector3.up` for the fan plane: the staff leans
        /// eleven degrees while it charges and straightens at the strike, and the
        /// turbine is perpendicular to the SHAFT, not to the world.
        private void GetEndpoints(int index, float skyward, out Vector3 from, out Vector3 to)
        {
            from = transform.position;

            // Deterministic per index rather than random, so a given arc keeps its job
            // between re-strikes instead of flickering between blade and sky every
            // frame. Only its endpoint jitters.
            bool up = (index + 0.5f) / Mathf.Max(1, arcs.Length) <= skyward;

            if (up)
            {
                // Straight up, into the sky, with a widening scatter: the higher the
                // charge the further and the more spread out these reach.
                to = from
                     + Vector3.up * (skyReach * Mathf.Lerp(0.25f, 1f, Progress))
                     + Random.insideUnitSphere * (fanRadius * 1.5f);
                return;
            }

            // Down onto the turbine: a point anywhere on the fan disc, so the arc lands
            // along a blade rather than always at its tip.
            float a = Random.value * Mathf.PI * 2f;
            float r = Mathf.Sqrt(Random.value) * fanRadius;
            to = from
                 - transform.up * fanDrop
                 + transform.right * (Mathf.Cos(a) * r)
                 + transform.forward * (Mathf.Sin(a) * r);
        }
    }
}
