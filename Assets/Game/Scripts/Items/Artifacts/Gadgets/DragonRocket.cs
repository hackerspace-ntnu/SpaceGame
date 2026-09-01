using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Items
{
    /// <summary>
    /// One rocket in flight: a lacquered firework that corkscrews toward where the player aimed,
    /// trailing red smoke and fire, and bursts into a brood of whelps when it lands.
    ///
    /// <para>
    /// <b>Never a network prefab.</b> Every machine instantiates its own copy locally from the
    /// seed in the use message — see <see cref="DragonRocketFlight"/> for why that is the same
    /// rocket on all of them — and exactly one of those copies, the authority's, is allowed to
    /// hurt anything. Replicating a projectile's transform instead would put a round trip inside
    /// the one thing this item is about watching, and registering it in the prefab list would be
    /// the mistake the artifact skill warns about: only what <c>GameServices.World.Spawn</c> is
    /// handed belongs there.
    /// </para>
    /// <para>
    /// <b>Flight advances in fixed substeps, not in frame-sized ones.</b> The path is closed-form
    /// and therefore frame-rate independent, but the IMPACT TEST is not: it sweeps the chord
    /// between two sampled points, and sampling a curve at 144 Hz gives a different polyline —
    /// and eventually a different first hit — than sampling it at 30 Hz. Stepping a fixed
    /// <see cref="StepSeconds"/> makes every machine walk the identical polyline, so the burst
    /// happens in the same place on all of them and the server's explosion is the one the peers
    /// watched.
    /// </para>
    /// <para>
    /// <b>Persistence:</b> nothing here is saved, deliberately. A rocket lives about two seconds;
    /// a shot restored mid-flight after a quit would be a bug, not a feature, and the charges left
    /// in the launcher ride <c>ItemState</c> on the hotbar slot like every other artifact's.
    /// </para>
    /// </summary>
    public class DragonRocket : MonoBehaviour
    {
        /// <summary>
        /// Simulation step. 120 Hz: fine enough that the chord across the tightest swerve stays
        /// well under the hit radius (so the rocket cannot tunnel through a wall it should have
        /// clipped), coarse enough that a two-second flight is a couple of hundred sweeps.
        /// </summary>
        private const float StepSeconds = 1f / 120f;

        /// <summary>
        /// Ceiling on catch-up steps in one frame. Without it a hitch — a scene load, a
        /// breakpoint — hands this an accumulated second and the rocket sweeps a hundred and
        /// twenty casts in a single frame, which is itself a hitch. Dropping time instead makes a
        /// badly-stuttering machine's rocket lag its own flight rather than freeze the game; the
        /// authority's copy decides the damage regardless.
        /// </summary>
        private const int MaxStepsPerFrame = 12;

        [Header("Flight")]
        [Tooltip("Forward speed along the aim ray, m/s. The wander is added on top, so the rocket " +
                 "covers ground slightly faster than this. Deliberately slow: the flight IS the " +
                 "effect, and a fast rocket resolves before anyone can watch it misbehave.")]
        [SerializeField] private float speed = 15f;

        [Tooltip("Scale of the stray off the aim ray, in metres. This is the headline number of " +
                 "the whole weapon — it is how much 'like a bug' the flight reads as. It is a " +
                 "SCALE, not a cap: harmonic jitter and the two independent lateral axes let a " +
                 "peak reach about 1.8x this, and a typical mid-flight stray is well under it.")]
        [SerializeField] private float wanderAmplitude = 5.4f;

        [Tooltip("Metres per second this rocket leans off the aim, in a direction of its own. " +
                 "The swerve alone always comes back to the crosshair; THIS is what lets an " +
                 "individual shot genuinely go somewhere else. Averages to zero across shots, so " +
                 "the launcher still shoots where it is pointed even though no single rocket does.")]
        [SerializeField] private float driftRate = 2.4f;

        [Tooltip("Seconds the swerve takes to ease in. The rocket leaves the dragon's mouth " +
                 "straight so the player can see their aim honoured before it misbehaves.")]
        [SerializeField] private float settleSeconds = 0.3f;

        [Tooltip("Swerves per second of the broadest harmonic. Higher reads as a fizzing bottle " +
                 "rocket, lower as a lazy loop.")]
        [SerializeField] private float wanderFrequency = 1.55f;

        [Tooltip("Seconds before an un-hit rocket bursts on its own. A wanderer that never lands " +
                 "must still resolve, or the shot simply disappears.")]
        [SerializeField] private float lifetime = 4.2f;

        [Tooltip("Radius of the sweep that decides a hit. Aim forgiveness AND the rocket's own " +
                 "size — this one is a 0.6 m firework, not a dart (GDC-L1-FEEL-0003).")]
        [SerializeField] private float hitRadius = 0.5f;

        [Tooltip("What the rocket can hit. Triggers are always ignored.")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Blast")]
        [SerializeField] private float blastRadius = 7.5f;
        [SerializeField] private int blastDamage = 45;
        [SerializeField] private float flingSpeed = 22f;
        [SerializeField] private float upwardTilt = 30f;
        [Tooltip("Fraction of the radius that takes undiminished force.")]
        [SerializeField, Range(0f, 1f)] private float coreFraction = 0.25f;
        [Tooltip("Launch strength at the rim relative to the centre.")]
        [SerializeField, Range(0f, 1f)] private float edgeFalloff = 0.3f;
        [Tooltip("Impulse scaling reference for loose items: a body this heavy takes full speed.")]
        [SerializeField] private float itemMassReference = 12f;
        [SerializeField] private Vector2 itemMassScaleRange = new Vector2(0.3f, 1.6f);

        [Header("Burst")]
        [Tooltip("The whelp rocket spawned by the burst. A DragonRocket prefab like this one, at " +
                 "half the size. Leave empty on the whelp itself.")]
        [SerializeField] private DragonRocket whelpPrefab;

        [Tooltip("Whelps per burst.")]
        [SerializeField] private int whelpCount = 4;

        [Tooltip("Half-angle of the cone the whelps scatter into, degrees.")]
        [SerializeField] private float whelpSpread = 52f;

        [Tooltip("How many times a burst may burst again. 1 means the hero bursts into whelps " +
                 "and the whelps do not burst further. This is a hard stop, not a tuning knob: " +
                 "four whelps each making four is sixteen, and the frame after that is 64.")]
        [SerializeField] private int maxGenerations = 1;

        [Header("Effects")]
        [Tooltip("Red smoke laid down along the flight. Detached on death so the trail hangs in " +
                 "the sky after the rocket is gone — the whole point of a red trail.")]
        [SerializeField] private ParticleSystem trail;

        [Tooltip("Fire wrapped around the rocket. Detached with the trail.")]
        [SerializeField] private ParticleSystem flame;

        [Tooltip("Gold embers shed along the flight — the detail that reads as a FIREWORK rather " +
                 "than as an exhaust plume. Detached with the trail so they fall out of the sky " +
                 "after the rocket is gone.")]
        [SerializeField] private ParticleSystem embers;

        [Tooltip("Soft additive glow travelling with the rocket, under the flame. Detached too.")]
        [SerializeField] private ParticleSystem halo;

        [Tooltip("The burst. Playing the parent plays its children.")]
        [SerializeField] private ParticleSystem burst;

        [Tooltip("RepulsorShockwave-shader material for the ground ring, shared with the " +
                 "repulsor and the Sucker Puncher.")]
        [SerializeField] private Material ringMaterial;

        [SerializeField] private float ringDuration = 0.5f;

        [Tooltip("Lights the rocket from inside while it flies.")]
        [SerializeField] private Light glow;

        /// <summary>
        /// True for a copy that exists only so somebody can watch it.
        ///
        /// The same flag, under the same name and for the same reason, as
        /// <see cref="SpaceGame.Agents.AgentProjectile.Cosmetic"/>: whenever more than one machine
        /// puts a copy of the same shot in the air, exactly one may bill the target, because
        /// <see cref="NetDamage"/> honours a request from every client that asks. Everything
        /// visible — the flight, the trail, the burst, the whelps — runs on a cosmetic copy too.
        /// That IS the point of it being in the air.
        /// </summary>
        public bool Cosmetic { get; private set; }

        private GameObject shooter;
        private Transform shooterRoot;
        private Vector3 origin;
        private Quaternion aim;
        private int seed;
        private int generation;

        private float elapsed;
        private float accumulator;
        private Vector3 previous;
        private bool spent;

        /// <summary>
        /// Put the rocket in the air. Called by whoever instantiated it, on every machine.
        /// </summary>
        /// <param name="cosmetic">See <see cref="Cosmetic"/>. False on exactly one machine.</param>
        /// <param name="fromGeneration">0 for a launcher's shot; a burst passes its own plus one.</param>
        public void Launch(GameObject firedBy, Vector3 from, Quaternion along, int flightSeed,
                           bool cosmetic, int fromGeneration = 0)
        {
            shooter = firedBy;
            shooterRoot = firedBy != null ? firedBy.transform.root : null;
            origin = from;
            aim = along;
            seed = flightSeed;
            Cosmetic = cosmetic;
            generation = fromGeneration;

            elapsed = 0f;
            accumulator = 0f;
            previous = from;
            spent = false;

            transform.SetPositionAndRotation(from, along);

            foreach (ParticleSystem effect in Streams())
                if (effect != null) effect.Play(withChildren: true);
        }

        private void Update()
        {
            if (spent) return;

            accumulator += Time.deltaTime;
            int steps = 0;

            while (accumulator >= StepSeconds && steps < MaxStepsPerFrame)
            {
                accumulator -= StepSeconds;
                steps++;

                elapsed += StepSeconds;
                if (Step()) return;
            }

            // Time this frame could not afford is dropped rather than carried, so a machine that
            // stutters does not spend the next second sprinting the rocket to catch up.
            if (accumulator >= StepSeconds) accumulator = 0f;
        }

        /// <summary>
        /// Advance one fixed step. Returns true if the rocket resolved and this object is gone.
        /// </summary>
        private bool Step()
        {
            Vector3 next = DragonRocketFlight.PositionAt(origin, aim, seed, elapsed, speed,
                                                         wanderAmplitude, settleSeconds,
                                                         wanderFrequency, driftRate);

            Vector3 travel = next - previous;
            float distance = travel.magnitude;

            if (distance > 1e-5f &&
                Physics.SphereCast(previous, hitRadius, travel / distance, out RaycastHit hit,
                                   distance, hitMask, QueryTriggerInteraction.Ignore) &&
                !IsShooter(hit.collider))
            {
                Explode(hit.point, hit.normal);
                return true;
            }

            previous = next;
            transform.position = next;

            Vector3 heading = DragonRocketFlight.VelocityAt(aim, seed, elapsed, speed,
                                                            wanderAmplitude, settleSeconds,
                                                            wanderFrequency, driftRate);
            if (heading.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(heading);

            if (elapsed >= lifetime)
            {
                // An air burst has no surface to bounce the ring off, so the wave is oriented
                // against the flight instead. Nothing else about the burst changes: a rocket that
                // simply vanished when its fuse ran out would be the one case where the weapon
                // does nothing at all, and the player would read it as the gun having failed.
                Explode(transform.position, -heading.normalized);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Is this the shooter's own body? The rocket leaves a muzzle already in front of them,
        /// but the first swerve can bring it back across their shoulder — and a firework that
        /// kills the person who fired it every third shot is not a tradeoff, it is a bug.
        /// </summary>
        private bool IsShooter(Collider other)
        {
            if (shooterRoot == null || other == null) return false;
            return other.transform.IsChildOf(shooterRoot);
        }

        /// <summary>
        /// The rocket resolves: damage and physics on the authority, spectacle everywhere, and a
        /// brood of whelps on every machine.
        /// </summary>
        private void Explode(Vector3 at, Vector3 normal)
        {
            spent = true;

            if (!Cosmetic) ApplyBlast(at);

            PresentBurst(at);
            SpawnWhelps(at, normal);

            // The trail and everything riding with it outlive the rocket. Detached rather than
            // destroyed with it, because a red smoke trail that vanishes at the instant of the
            // bang erases the one thing the shot leaves behind.
            foreach (ParticleSystem effect in Streams())
                Detach(effect);

            Destroy(gameObject);
        }

        /// <summary>Authority only: what the blast does to the world.</summary>
        private void ApplyBlast(Vector3 at)
        {
            var seen = new HashSet<GameObject>();
            if (shooterRoot != null) seen.Add(shooterRoot.gameObject);

            foreach (Collider caught in Physics.OverlapSphere(at, blastRadius, ~0,
                                                              QueryTriggerInteraction.Ignore))
            {
                GameObject root = caught.transform.root.gameObject;
                if (!seen.Add(root)) continue;

                // charge = 1 and min = max = flingSpeed: this blast has no charge to vary, and
                // RepulsorBlast is shared with the gauntlet and the punch, which do.
                Vector3 fling = RepulsorBlast.FlingVelocity(at, Vector3.up, caught.bounds.center,
                                                            1f, blastRadius, flingSpeed,
                                                            flingSpeed, upwardTilt, coreFraction,
                                                            edgeFalloff);

                // No knock hook: this blast flings but does not ragdoll. The gauntlet's knockdown
                // is a deliberate extra it pays for with a serialized downed duration, and adding
                // one here would be inventing a second thing to tune.
                BlastPush.Apply(caught, root, fling, flingSpeed,
                                BlastPush.Leap.Proportional(9f, 2.4f, 0.55f),
                                itemMassReference, itemMassScaleRange);

                if (blastDamage > 0)
                    NetDamage.Apply(root, blastDamage,
                                    shooter != null ? shooter.transform : transform);
            }
        }

        /// <summary>Every machine, cosmetic copies included.</summary>
        private void PresentBurst(Vector3 at)
        {
            if (burst != null)
            {
                burst.transform.SetParent(null, worldPositionStays: true);
                burst.transform.position = at;
                burst.Play(withChildren: true);
                SelfDestruct(burst);
            }

            if (glow != null) glow.enabled = false;

            RepulsorBlastRing.Spawn(at, blastRadius, ringDuration, ringMaterial);
            Sfx.Play(SfxId.ImpactExplosion, at, default, GetInstanceID());
        }

        /// <summary>
        /// The brood. Spawned on every machine from seeds derived off this rocket's own, because
        /// nothing is sent about a burst — a re-roll per machine would have four peers watching
        /// four different sets of whelps. Their cosmetic flag is inherited, so the authority's
        /// whelps are the only ones that can hurt anybody.
        /// </summary>
        private void SpawnWhelps(Vector3 at, Vector3 normal)
        {
            if (whelpPrefab == null || whelpCount <= 0 || generation >= maxGenerations) return;

            // Away from the surface it struck, so a burst against a wall throws its brood back
            // into the room rather than into the wall.
            Vector3 axis = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            Vector3[] dirs = DragonRocketFlight.BurstDirections(seed, axis, whelpCount,
                                                                whelpSpread);

            for (int i = 0; i < dirs.Length; i++)
            {
                // Started clear of the blast's own centre, or the whelp's first sweep hits the
                // same wall its parent just did and the brood dies on the frame it is born.
                Vector3 from = at + dirs[i] * hitRadius * 2f;
                DragonRocket whelp = Instantiate(whelpPrefab, from,
                                                 Quaternion.LookRotation(dirs[i]));
                whelp.Launch(shooter, from, Quaternion.LookRotation(dirs[i]),
                             DragonRocketFlight.ChildSeed(seed, i), Cosmetic, generation + 1);
            }
        }

        /// <summary>
        /// The effects that run for the whole flight and outlive it.
        ///
        /// One list, so starting them and cutting them loose cannot disagree about which is
        /// which — the failure that leaves a trail still parented to a destroyed rocket, or an
        /// ember stream nobody ever started.
        /// </summary>
        private IEnumerable<ParticleSystem> Streams()
        {
            yield return trail;
            yield return flame;
            yield return embers;
            yield return halo;
        }

        /// <summary>
        /// Cut a running effect loose so it can finish in the air on its own.
        /// </summary>
        private static void Detach(ParticleSystem effect)
        {
            if (effect == null) return;

            effect.transform.SetParent(null, worldPositionStays: true);
            effect.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmitting);
            SelfDestruct(effect);
        }

        /// <summary>
        /// Have an orphaned system clean itself up once its last particle dies.
        ///
        /// Set here rather than authored on the prefab: with `Destroy` baked into the asset, the
        /// system would take the rocket's whole GameObject with it the moment it was stopped for
        /// any other reason.
        /// </summary>
        private static void SelfDestruct(ParticleSystem effect)
        {
            var main = effect.main;
            main.stopAction = ParticleSystemStopAction.Destroy;
        }

        private void OnValidate()
        {
            speed = Mathf.Max(0.1f, speed);
            wanderAmplitude = Mathf.Max(0f, wanderAmplitude);
            driftRate = Mathf.Max(0f, driftRate);
            settleSeconds = Mathf.Max(0f, settleSeconds);
            wanderFrequency = Mathf.Max(0.01f, wanderFrequency);
            lifetime = Mathf.Max(0.1f, lifetime);
            hitRadius = Mathf.Max(0.01f, hitRadius);
            blastRadius = Mathf.Max(0.1f, blastRadius);
            blastDamage = Mathf.Max(0, blastDamage);
            whelpCount = Mathf.Max(0, whelpCount);
            maxGenerations = Mathf.Max(0, maxGenerations);
            itemMassScaleRange.x = Mathf.Max(0.01f, itemMassScaleRange.x);
            itemMassScaleRange.y = Mathf.Max(itemMassScaleRange.y, itemMassScaleRange.x);
        }
    }
}
