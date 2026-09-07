// Everything drawn between the canister's mouth and whatever it has hold of.
//
// WHY NOTHING HERE MOVES THE CAPTIVE. The design asks for the target to slide toward the mouth, and
// this deliberately does not do it by writing the body's transform. A player's body is
// owner-authoritative, so a pull written on any other machine is undone within a tick and silently;
// a creature's position belongs to its NavMeshAgent or its LeggedLocomotion, both of which
// overwrite from their own solve on the next frame. Either way the write is a fight the drawing
// loses, on the machine that matters, with a clean console. So the pull is SHOWN — the draught, the
// dust streaming up it, and the fold-in at the end — and the body itself is left to whoever owns
// it. The moment it visibly goes in is real: PulledBody.Folded fires on every machine while the
// body is still there to be seen, which is the whole reason NetMsg.Contained is announced before
// the despawn.
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The suction beam and its dust, on every machine.
    ///
    /// <para>
    /// Told once a frame where the mouth is and where the draught ends, by
    /// <see cref="VacuumCanisterArtifact"/>, which traces the shared aim ray. Every machine traces
    /// the same ray, so a peer's beam ends where the server is actually pulling rather than
    /// wherever the owner last reported a hit.
    /// </para>
    /// <para>
    /// <b>What it deliberately does not show is progress.</b> The contest's fill level lives on the
    /// authority (<c>ContainmentPull.Progress</c>) and no message carries it, so a client drawing
    /// something in has no honest number to draw. Rather than paint a second, differently-wrong
    /// meter on every machine, the draught only shows that it HAS hold of something — the fold-in
    /// is what says the contest was won.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VacuumDraught : MonoBehaviour
    {
        [Header("Beam")]
        [Tooltip("The draught itself, drawn from the mouth to whatever it reaches. Optional.")]
        [SerializeField] private LineRenderer beam;

        [Tooltip("How wide the draught is drawn when it has hold of a body, in metres.")]
        [SerializeField, Min(0f)] private float gripWidth = 0.22f;

        [Tooltip("How wide it is drawn when it reaches nothing but ground or air. Thinner, so " +
                 "having hold of something reads at a glance.")]
        [SerializeField, Min(0f)] private float idleWidth = 0.08f;

        [Tooltip("Seconds for the draught to reach full width when it opens.")]
        [SerializeField, Min(0.001f)] private float riseSeconds = 0.07f;

        [Tooltip("Seconds for it to die back once the trigger is up.")]
        [SerializeField, Min(0.001f)] private float fallSeconds = 0.16f;

        [Header("Dust")]
        [Tooltip("Grit and air pulled up the draught. One system, emitted into by hand — see the " +
                 "class summary. Optional.")]
        [SerializeField] private ParticleSystem dust;

        [Tooltip("Motes emitted per second while the draught is running.")]
        [SerializeField, Min(0f)] private float dustPerSecond = 60f;

        [Tooltip("How far off the draught's line a mote may start, in metres. Zero is a laser; a " +
                 "little spread is air being dragged in from around the target.")]
        [SerializeField, Min(0f)] private float dustSpread = 0.35f;

        [Tooltip("Seconds a mote takes to travel the whole draught. Its speed is derived from " +
                 "this and the distance, so the stream looks the same close up and at full reach.")]
        [SerializeField, Min(0.05f)] private float dustTravelSeconds = 0.28f;

        [Header("Fold and uncork")]
        [Tooltip("Motes thrown in one go when a captive goes in or comes out. Not folded into the " +
                 "steady rate: the whole point is that it is not spread over time.")]
        [SerializeField, Min(0)] private int burstCount = 90;

        [Tooltip("How fast a burst's motes travel, in metres per second.")]
        [SerializeField, Min(0f)] private float burstSpeed = 6f;

        /// <summary>0 out, 1 at full width. Eased so the draught does not pop on and off.</summary>
        private float intensity;

        private bool flowing;
        private bool gripping;

        private Vector3 mouthPoint;
        private Vector3 endPoint;

        /// <summary>Motes earned but not yet emitted, so the rate is per second rather than per frame.</summary>
        private float dustCarry;

        private void Awake() => PrepareDust();

        /// <summary>
        /// Where the draught runs, this frame.
        ///
        /// Pushed rather than pulled so this component knows nothing about the aim, the trace or
        /// who owns the canister — it draws what it is handed.
        /// </summary>
        /// <param name="mouth">Where the draught leaves the canister.</param>
        /// <param name="end">Where it reaches: the traced hit, or the end of its range.</param>
        /// <param name="running">Is the canister drawing at all?</param>
        /// <param name="onBody">Has it reached something that could be drawn in?</param>
        public void Aim(Vector3 mouth, Vector3 end, bool running, bool onBody)
        {
            mouthPoint = mouth;
            endPoint = end;
            flowing = running;
            gripping = onBody;
        }

        /// <summary>Put the draught out now. For an unequip or a disable, which may be the last frame.</summary>
        public void Clear()
        {
            flowing = false;
            gripping = false;
            intensity = 0f;
            dustCarry = 0f;

            if (beam != null && beam.enabled) beam.enabled = false;
        }

        /// <summary>
        /// A captive went in, or came out, at <paramref name="at"/>.
        ///
        /// <paramref name="inward"/> is the whole difference: a fold-in throws its motes at the
        /// mouth, an uncork throws them away from it. Both are played on every machine, because
        /// both are announced to every machine while there is still something to see.
        /// </summary>
        public void Burst(Vector3 at, bool inward)
        {
            if (dust == null || burstCount <= 0) return;

            Vector3 span = mouthPoint - at;
            Vector3 direction = span.sqrMagnitude > 1e-6f
                ? span.normalized
                : Vector3.up;

            Emit(burstCount, at, (inward ? direction : -direction) * burstSpeed, dustSpread);
        }

        private void Update()
        {
            intensity = Mathf.MoveTowards(intensity, flowing ? 1f : 0f,
                                          Time.deltaTime / (flowing ? riseSeconds : fallSeconds));

            DrawBeam();
            StreamDust();
        }

        private void DrawBeam()
        {
            if (beam == null) return;

            bool visible = intensity > 0.001f;
            if (beam.enabled != visible) beam.enabled = visible;
            if (!visible) return;

            beam.useWorldSpace = true;
            beam.positionCount = 2;
            beam.SetPosition(0, mouthPoint);
            beam.SetPosition(1, endPoint);
            beam.widthMultiplier = Mathf.Lerp(0f, gripping ? gripWidth : idleWidth, intensity);
        }

        /// <summary>
        /// Feed the steady stream up the draught.
        ///
        /// The carry is what keeps <see cref="dustPerSecond"/> a rate rather than a frame budget: a
        /// fractional per-frame count would floor to zero at sixty frames a second and emit nothing
        /// at all.
        /// </summary>
        private void StreamDust()
        {
            if (dust == null || !flowing || dustPerSecond <= 0f)
            {
                dustCarry = 0f;
                return;
            }

            dustCarry += dustPerSecond * Time.deltaTime;

            int motes = Mathf.FloorToInt(dustCarry);
            if (motes <= 0) return;

            dustCarry -= motes;

            Vector3 span = mouthPoint - endPoint;
            float distance = span.magnitude;
            if (distance < 1e-3f) return;

            Vector3 velocity = span / distance * (distance / dustTravelSeconds);

            Emit(motes, endPoint, velocity, dustSpread);
        }

        /// <summary>
        /// Put <paramref name="count"/> motes into the world at <paramref name="from"/>.
        ///
        /// One system emitted into by hand rather than a system per target: a particle system moved
        /// and replayed per event clears whatever is still in the air, and one per captive would be
        /// an allocation on a path that runs sixty times a second.
        /// </summary>
        private void Emit(int count, Vector3 from, Vector3 velocity, float spread)
        {
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };

            for (int i = 0; i < count; i++)
            {
                emit.position = from + Random.insideUnitSphere * spread;
                emit.velocity = velocity;
                dust.Emit(emit, 1);
            }
        }

        /// <summary>
        /// Put the dust system into the one state hand emission works in.
        ///
        /// <para>
        /// Set here rather than left to the prefab because all three are structural rather than
        /// authored: a system simulating in LOCAL space would drag every mote along with the
        /// canister as the holder turns, a system with its own emission enabled would spray a
        /// second, uncommanded stream, and a system that is not PLAYING silently drops every
        /// <c>Emit</c> it is handed. The look — colour, size, lifetime, gravity — stays entirely on
        /// the prefab.
        /// </para>
        /// </summary>
        private void PrepareDust()
        {
            if (dust == null) return;

            ParticleSystem.MainModule main = dust.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;

            ParticleSystem.EmissionModule emission = dust.emission;
            emission.enabled = false;

            dust.Play();
        }

        private void OnDisable() => Clear();
    }
}
