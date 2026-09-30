using System.Collections.Generic;
using FirstGearGames.SmoothCameraShaker;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Everything the gravel blaster THROWS AT THE SENSES, on every machine: the muzzle blast, one
    /// visible streak per pellet, an impact where each one lands, the pressure wave off the
    /// barrels, and a camera kick dosed by distance.
    ///
    /// <para>
    /// Split out of <see cref="GravelBlasterArtifact"/> so the artifact keeps only the shot's
    /// authority and arithmetic. The layering is GDC-L1-FEEL-0004 taken at its word — the same
    /// press lands on sight (flash, wave, thirty tracers, impact puffs), on hearing (the
    /// report, plus a separate impact layer that reports what was hit) and on the camera
    /// (GDC-L1-FEEL-0006, attenuated with distance and capped). It is amplification of a real
    /// event, not decoration: every streak is a pellet the server actually traced, and an impact
    /// puff marks a place damage was actually billed.
    /// </para>
    /// <para>
    /// The per-pellet effects are ONE particle system emitting many particles rather than one
    /// system (or object) per pellet — thirty spawned GameObjects a shot is how a weapon that
    /// looks good in a screenshot becomes a frame spike in a firefight (GDC-L1-PERF-0004). The
    /// emitters are world-space, so gravel already in the air keeps its arc when the gun swings.
    /// </para>
    /// </summary>
    public class GravelBlastFx : MonoBehaviour
    {
        [Header("Muzzle")]
        [Tooltip("Where the blast leaves the pipes. Placed by GravelBlasterBuilder.")]
        [SerializeField] private Transform muzzle;

        [Tooltip("Tumbling rock chunks out of the muzzle.")]
        [SerializeField] private ParticleSystem gravelBurst;

        [Tooltip("Sand-coloured powder cloud at the muzzle.")]
        [SerializeField] private ParticleSystem muzzleDust;

        [Tooltip("Hot spring-steel sparks at the muzzle.")]
        [SerializeField] private ParticleSystem muzzleSparks;

        [Tooltip("The slow plume that hangs off the barrels after the shot has gone.")]
        [SerializeField] private ParticleSystem muzzleSmoke;

        [Tooltip("Brief muzzle flash. Enabled by PlayShot, cut by Update.")]
        [SerializeField] private Light muzzleFlash;

        [Tooltip("Seconds the muzzle flash stays lit.")]
        [SerializeField] private float flashSeconds = 0.07f;

        [Header("Pellets")]
        [Tooltip("One stretched streak per pellet, emitted along the traced direction.")]
        [SerializeField] private ParticleSystem pelletTracers;

        [Tooltip("How fast a streak flies, m/s. Its lifetime is set so it dies exactly where its " +
                 "pellet landed — the spray you watch is the shot the server billed, not an " +
                 "impression of it.")]
        [SerializeField] private float tracerSpeed = 165f;

        [Tooltip("Seconds a streak lingers past its impact, so the line is still readable when the " +
                 "puff appears at the end of it.")]
        [SerializeField] private float tracerLinger = 0.04f;

        [Header("Impacts")]
        [Tooltip("Sparks struck off whatever the gravel hits.")]
        [SerializeField] private ParticleSystem impactSparks;

        [Tooltip("Dust punched out of the surface.")]
        [SerializeField] private ParticleSystem impactDust;

        [Tooltip("Chips and fragments knocked loose.")]
        [SerializeField] private ParticleSystem impactDebris;

        [Tooltip("Particles emitted per impact, in order: sparks, dust, debris. Carried here rather " +
                 "than as each system's own burst because these systems are emitted into by hand " +
                 "and never played — a burst would go off at the gun the moment it was equipped.")]
        [SerializeField] private Vector3Int impactCounts = new Vector3Int(9, 5, 4);

        [Tooltip("Impact dust is tinted this on a living target — the only thing that tells a hit " +
                 "on a creature apart from a hit on a rock at a glance.")]
        [SerializeField] private Color fleshTint = new Color(0.55f, 0.11f, 0.10f);

        [Tooltip("How many of the shot's impacts are drawn, in trace order. A cap rather than a " +
                 "promise: thirty pellets into one wall is thirty puffs inside a square metre, " +
                 "which costs a great deal and reads as one puff anyway.")]
        [SerializeField] private int maxImpactsDrawn = 14;

        [Header("Blast wave")]
        [Tooltip("The pressure wave off the barrels: one big, fast, short-lived sheet that gives " +
                 "the discharge a SHAPE, which thirty thin streaks on their own do not.")]
        [SerializeField] private ParticleSystem blastWave;

        [Header("Backfire")]
        [Tooltip("Everything that leaves the BREECH instead when the gun backfires. Playing the " +
                 "parent plays its children.")]
        [SerializeField] private ParticleSystem backfireBurst;

        [Header("Camera")]
        [SerializeField] private ShakeData blastShake;

        [Tooltip("Only cameras within this range of the muzzle shake at all.")]
        [SerializeField] private float shakeRadius = 22f;

        [Tooltip("Shake magnitude at the muzzle, faded to nothing at shakeRadius.")]
        [SerializeField] private float shakeMagnitude = 1.15f;

        [Tooltip("Extra magnitude when the gun goes off in your own hands rather than someone " +
                 "else's, since the report is at the camera rather than across the street.")]
        [SerializeField] private float firstPersonShake = 1.5f;

        private float flashUntil = float.NegativeInfinity;

        /// <summary>
        /// Draw one shot. <paramref name="pellets"/> is the trace the authority billed, so nothing
        /// here has to be guessed or re-rolled.
        /// </summary>
        public void PlayShot(Vector3 origin, Vector3 aimDir,
                             IReadOnlyList<GravelShotTrace.Pellet> pellets, bool firstPerson)
        {
            Vector3 muzzlePoint = muzzle != null ? muzzle.position : origin;

            PlayAimed(gravelBurst, aimDir);
            PlayAimed(muzzleDust, aimDir);
            PlayAimed(muzzleSparks, aimDir);
            PlayAimed(muzzleSmoke, aimDir);
            PlayAimed(blastWave, aimDir);

            if (muzzleFlash != null)
            {
                muzzleFlash.enabled = true;
                flashUntil = Time.time + flashSeconds;
            }

            EmitTracers(muzzlePoint, pellets);
            EmitImpacts(pellets);
            Shake(muzzlePoint, firstPerson);
        }

        /// <summary>The gun failing in the holder's face. No tracers: nothing left the barrels.</summary>
        public void PlayBackfire()
        {
            if (backfireBurst != null) backfireBurst.Play(true);
        }

        /// <summary>
        /// One streak per pellet, each living exactly as long as its pellet's flight so the line
        /// ends on the surface the pellet struck.
        /// </summary>
        private void EmitTracers(Vector3 muzzlePoint, IReadOnlyList<GravelShotTrace.Pellet> pellets)
        {
            if (pelletTracers == null || pellets == null || tracerSpeed <= 0f) return;

            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };

            for (int i = 0; i < pellets.Count; i++)
            {
                // Aimed from the MUZZLE at the point the pellet reached, rather than along the
                // traced direction: the trace starts at the holder's camera, which is behind and
                // above the barrels, so a streak that merely copies its direction leaves the gun
                // parallel to the shot and lands beside what was hit.
                Vector3 flight = pellets[i].Point - muzzlePoint;
                float distance = flight.magnitude;
                if (distance < 1e-3f) continue;

                emit.position = muzzlePoint;
                emit.velocity = flight / distance * tracerSpeed;
                emit.startLifetime = distance / tracerSpeed + tracerLinger;
                pelletTracers.Emit(emit, 1);
            }
        }

        /// <summary>
        /// A puff, chips and sparks where the gravel landed. Emitted into shared world-space
        /// systems at the hit point — one system, many particles.
        /// </summary>
        private void EmitImpacts(IReadOnlyList<GravelShotTrace.Pellet> pellets)
        {
            if (pellets == null) return;

            int drawn = 0;
            for (int i = 0; i < pellets.Count && drawn < maxImpactsDrawn; i++)
            {
                GravelShotTrace.Pellet pellet = pellets[i];
                if (!pellet.Hit) continue;
                drawn++;

                // Away from the surface, biased along the pellet so a grazing hit sprays forward
                // the way a real ricochet does rather than straight back out of the wall.
                Vector3 spray = (pellet.Normal - pellet.Direction * 0.35f).normalized;

                EmitAt(impactSparks, impactCounts.x, pellet.Point, spray, null);
                EmitAt(impactDust, impactCounts.y, pellet.Point, spray,
                       pellet.IsFlesh ? fleshTint : (Color?)null);
                EmitAt(impactDebris, impactCounts.z, pellet.Point, spray, null);
            }
        }

        /// <summary>
        /// Spray <paramref name="count"/> particles out of one shared system at a world point,
        /// pointed along <paramref name="direction"/>.
        ///
        /// <para>
        /// The system is MOVED to the point rather than the particles being placed at it, because
        /// that is what lets the shape module do the spreading — and it is free: these systems
        /// simulate in world space, so particles already in the air from the last pellet do not
        /// move with it.
        /// </para>
        /// </summary>
        private static void EmitAt(ParticleSystem system, int count, Vector3 point,
                                   Vector3 direction, Color? tint)
        {
            if (system == null || count <= 0 || direction.sqrMagnitude < 1e-6f) return;

            system.transform.SetPositionAndRotation(point, Quaternion.LookRotation(direction));

            var emit = new ParticleSystem.EmitParams();
            if (tint.HasValue) emit.startColor = tint.Value;
            system.Emit(emit, count);
        }

        /// <summary>
        /// Point a burst down the shot and fire it. The muzzle systems are children of the gun, so
        /// only their ROTATION is written — moving them into world space would strand them behind
        /// the barrels for every shot after the first.
        /// </summary>
        private static void PlayAimed(ParticleSystem burst, Vector3 direction)
        {
            if (burst == null) return;

            if (direction.sqrMagnitude > 1e-6f)
                burst.transform.rotation = Quaternion.LookRotation(direction.normalized);
            burst.Play(true);
        }

        /// <summary>
        /// Camera kick, faded out with distance and capped — the dose GDC-L1-FEEL-0006 asks for.
        /// A blast heard from across a settlement should register, not punch the frame as hard as
        /// one going off in your own hands.
        /// </summary>
        private void Shake(Vector3 origin, bool firstPerson)
        {
            if (blastShake == null || Camera.main == null) return;

            float distance = Vector3.Distance(Camera.main.transform.position, origin);
            if (distance >= shakeRadius) return;

            float magnitude = shakeMagnitude * (1f - distance / Mathf.Max(shakeRadius, 0.01f));
            if (firstPerson) magnitude = firstPersonShake;

            // A null instance means no CameraShaker is live in the scene — nothing to scale.
            ShakerInstance instance = CameraShakerHandler.Shake(blastShake);
            instance?.MultiplyMagnitude(magnitude, 0f); // 0 rate = applied on the first frame
        }

        private void Update()
        {
            if (muzzleFlash != null && muzzleFlash.enabled && Time.time >= flashUntil)
                muzzleFlash.enabled = false;
        }

        /// <summary>Unequipping mid-flash would otherwise leave the light on for the next equip.</summary>
        private void OnDisable()
        {
            if (muzzleFlash != null) muzzleFlash.enabled = false;
            flashUntil = float.NegativeInfinity;
        }

        private void OnValidate()
        {
            tracerSpeed = Mathf.Max(1f, tracerSpeed);
            tracerLinger = Mathf.Max(0f, tracerLinger);
            maxImpactsDrawn = Mathf.Max(0, maxImpactsDrawn);
            impactCounts = Vector3Int.Max(impactCounts, Vector3Int.zero);
            shakeRadius = Mathf.Max(0.01f, shakeRadius);
            shakeMagnitude = Mathf.Max(0f, shakeMagnitude);
        }
    }
}
