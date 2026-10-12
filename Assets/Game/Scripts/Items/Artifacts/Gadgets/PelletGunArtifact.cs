using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Items
{
    /// <summary>
    /// A hitscan gun: every press traces a fan of pellets from the holder's eye, bills whatever
    /// they hit on the server, and draws the same fan on every machine. Used as-is by the basic gun
    /// (one pellet, no spread, a long reach) and extended by <see cref="GravelBlasterArtifact"/>
    /// (thirty pellets, a short taper, and a backfire).
    ///
    /// The owner rolls one random seed into the use message; <see cref="PelletShotMath"/> derives
    /// the spread from that seed, so the damage the server bills and the spray every machine draws
    /// are provably the same shot. Both sides walk it through the one
    /// <see cref="PelletShotTrace"/>, and the presentation is <see cref="PelletGunFx"/>'s.
    ///
    /// <para>
    /// Hitscan because a shot must be acknowledged the frame it is fired (GDC-L1-FEEL-0002): the
    /// tracer and the impact are drawn at once, rather than a slow bullet the player has to lead.
    /// What sets one pellet gun apart from another is the falloff, not the reach —
    /// <see cref="PelletShotMath.DamageFalloff"/> tapers a pellet's damage past
    /// <see cref="fullDamageRange"/>, so each gun is strong somewhere and weak elsewhere rather
    /// than one being a strictly better number (GDC-L1-BAL-0004).
    /// </para>
    /// </summary>
    public class PelletGunArtifact : ToolItem
    {
        /// <summary>
        /// Server. Pellet damage and the shove it lands with are shared world state, so exactly one
        /// machine may decide them.
        /// </summary>
        public override UseAuthority Authority => UseAuthority.Server;

        [Header("Shot")]
        [Tooltip("Pellets per shot. Every one of them is traced, billed and drawn as its own " +
                 "streak, so this is a visible number rather than an abstract damage multiplier.")]
        [SerializeField] private int pelletCount = 30;

        [Tooltip("Half-angle of the spread cone, in degrees.")]
        [SerializeField] private float spreadAngle = 9f;

        [Tooltip("How far a pellet carries, in metres. What stops a distant shot being a good shot " +
                 "is the falloff below, not a wall the pellet stops at.")]
        [SerializeField] private float range = 70f;

        [Tooltip("Damage per pellet at point-blank range. A shot that lands every pellet on one " +
                 "target is worth pelletCount times this.")]
        [SerializeField] private int pelletDamage = 5;

        [Tooltip("Metres over which a pellet keeps its full damage. Past this it tapers to " +
                 "farDamageFraction at maximum range.")]
        [SerializeField] private float fullDamageRange = 15f;

        [Tooltip("What a pellet is worth at maximum range, as a fraction of pelletDamage.")]
        [SerializeField, Range(0f, 1f)] private float farDamageFraction = 0.25f;

        [Tooltip("Seconds after a shot before the next press fires. 0 = as fast as the trigger is " +
                 "pulled. On a gun with a delay, leave useSoundId at None and put the report in " +
                 "reportId: PlayUse sounds useSoundId for every press, including the ones this " +
                 "turns away.")]
        [SerializeField] private float refireSeconds;

        [Tooltip("What the pellets can hurt. Triggers are always ignored.")]
        [SerializeField] private LayerMask damageMask = ~0;

        [Tooltip("Metres a shot is heard over. Anything with a NoiseReceiverModule inside it " +
                 "reacts — guards investigate, wildlife bolts, and MenaceSensor counts the holder " +
                 "as having just fired. 0 = silent to AI.")]
        [SerializeField] private float gunshotNoiseRadius = 40f;

        [Header("Knockback")]
        [Tooltip("Speed a target takes when EVERY pellet lands on it, m/s; a partial hit is " +
                 "proportional. This is what makes a hit read as a hit rather than as a number " +
                 "going down.")]
        [SerializeField] private float fullHitKickSpeed = 13f;

        [Tooltip("Upward tilt of that shove, degrees. Load-bearing on a player: PlayerMovement " +
                 "never deletes vertical velocity, and the rise is what keeps the horizontal half " +
                 "alive long enough to be felt.")]
        [SerializeField] private float kickTilt = 18f;

        [Tooltip("How far a full hit staggers a creature, metres. Creatures are moved by their " +
                 "motors, so the only thing a shot can do to one is ask it to leap.")]
        [SerializeField] private float staggerDistance = 3.5f;

        [SerializeField] private float staggerHeight = 0.9f;
        [SerializeField] private float staggerSeconds = 0.4f;

        [Tooltip("Impulse scaling reference for loose items: a body this heavy takes the full kick.")]
        [SerializeField] private float itemMassReference = 14f;

        [Tooltip("Bounds on that mass scaling, so a crate is not immovable and a tin can does not " +
                 "leave the chunk.")]
        [SerializeField] private Vector2 itemMassScaleRange = new Vector2(0.3f, 1.6f);

        [Header("Presentation")]
        [Tooltip("Everything the shot looks and sounds like. On this prefab, added by the builder.")]
        [SerializeField] protected PelletGunFx fx;

        [Tooltip("The body of the report, layered under useSoundId. Both must be DIFFERENT source " +
                 "keys or the catalog dedupes the second away and the shot collapses to one thin " +
                 "layer (GDC-L1-FEEL-0004).")]
        [SerializeField] private SfxId reportId = SfxId.ImpactExplosion;

        [Tooltip("Played once per shot that hit something soft.")]
        [SerializeField] private SfxId fleshImpactId = SfxId.ImpactFlesh;

        [Tooltip("Played once per shot that hit anything else.")]
        [SerializeField] private SfxId hardImpactId = SfxId.ImpactProjectile;

        [Tooltip("Recoil shove handed to the holder on a clean shot, m/s. Small on purpose: this " +
                 "is a jolt that sells the discharge, not a movement tool.")]
        [SerializeField] private float recoilSpeed = 4.5f;

        [Tooltip("Upward fraction mixed into that shove.")]
        [SerializeField] private float recoilUpwardBias = 0.12f;

        [Tooltip("FOV punch on the holder, degrees. The camera does the job hitstop would, which " +
                 "this codebase rules out on purpose — Time.timeScale on a host stalls the " +
                 "authoritative simulation for everyone else (GDC-L1-FEEL-0005).")]
        [SerializeField] private float fovKick = 7f;

        [SerializeField] private float fovKickSeconds = 0.18f;

        /// <summary>
        /// The traced shot, reused rather than allocated: a pellet is a raycast on every machine
        /// watching, and the authority walks the same list a frame later.
        /// </summary>
        private readonly List<PelletShotTrace.Pellet> pellets = new List<PelletShotTrace.Pellet>();

        /// <summary>Damage owed per target, summed across pellets before anything is billed.</summary>
        private readonly Dictionary<GameObject, float> billed = new Dictionary<GameObject, float>();

        /// <summary>One representative collider per billed target, for the shove's Rigidbody.</summary>
        private readonly Dictionary<GameObject, Collider> billedColliders =
            new Dictionary<GameObject, Collider>();

        /// <summary>Targets already made to flinch by the shot being presented.</summary>
        private readonly HashSet<GameObject> flinched = new HashSet<GameObject>();

        private PlayerLook look;
        private float fovKickUntil = float.NegativeInfinity;
        private bool fovKickArmed;
        private float nextShotTime = float.NegativeInfinity;

        /// <summary>
        /// Owner-side, before the request leaves — the only machine whose aim is honest. The seed
        /// travels too: every machine must agree where THIS shot's pellets go, and a re-roll per
        /// machine would have the server billing one spray while the peers draw another.
        ///
        /// <para>
        /// An NPC has no aim provider; its <see cref="EntityEquipmentController"/> has already put
        /// the barrel and the direction to its target in <c>P</c> and <c>R</c>, which are left
        /// alone. It still needs a seed — before this was rolled for it, an NPC's shot carried
        /// seed zero, which <see cref="GravelBlastMath.Backfires"/> reads as a backfire, and every
        /// gravel blaster an NPC fired went off in its own face.
        /// </para>
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            // Too soon after the last shot: a zero origin is the "no shot" both halves already
            // refuse (see Use), so the press is dropped on every machine at once.
            if (Time.time < nextShotTime)
            {
                arg.P = Vector3.zero;
                return;
            }

            nextShotTime = Time.time + refireSeconds;

            if (aimProvider != null)
            {
                Ray ray = aimProvider.GetAimRay();
                arg.P = ray.origin;
                arg.R = Quaternion.LookRotation(ray.direction);
            }

            arg.B = Random.Range(int.MinValue, int.MaxValue);
        }

        /// <summary>
        /// Does this shot go wrong instead of leaving the barrel? Decided from <c>UseArg</c> alone,
        /// so the authority and every watching machine agree. The gravel blaster's backfire.
        /// </summary>
        protected virtual bool Misfires => false;

        /// <summary>Authority: what a misfire does to the world. See <see cref="Misfires"/>.</summary>
        protected virtual void MisfireUse() { }

        /// <summary>Every machine: what a misfire looks and sounds like. See <see cref="Misfires"/>.</summary>
        protected virtual void MisfirePresent() { }

        /// <summary>Authority only: what the shot does to the world — or to the holder.</summary>
        protected override void Use()
        {
            // Default-struct guard: a use that never went through OnRequestUse (no aim provider)
            // carries a zero origin, and tracing pellets from the world origin would spray a spot
            // nobody is standing in. It is also how a press inside refireSeconds arrives.
            if (UseArg.P == Vector3.zero) return;

            ReportGunshot();

            if (Misfires)
            {
                MisfireUse();
                return;
            }

            if (pelletDamage <= 0 || pelletCount <= 0) return;

            TraceShot();

            // Pellets are summed per target and billed once: NetDamage per pellet would be thirty
            // messages, and a HealthComponent spans several colliders that must not each collect
            // the full count.
            billed.Clear();
            billedColliders.Clear();

            foreach (PelletShotTrace.Pellet pellet in pellets)
            {
                if (!pellet.Hit) continue;

                float damage = pelletDamage * PelletShotMath.DamageFalloff(
                    pellet.Distance, fullDamageRange, range, farDamageFraction);

                billed.TryGetValue(pellet.Target, out float owed);
                billed[pellet.Target] = owed + damage;
                billedColliders[pellet.Target] = pellet.Collider;
            }

            Vector3 aimDir = UseArg.R * Vector3.forward;
            foreach (KeyValuePair<GameObject, float> entry in billed)
            {
                int damage = Mathf.RoundToInt(entry.Value);
                if (damage > 0)
                    NetDamage.Apply(entry.Key, damage, owner != null ? owner.transform : transform);

                Shove(entry.Key, aimDir, entry.Value);
            }
        }

        /// <summary>
        /// Tell the world a gun went off here, so AI can react to it. Authority only — a creature
        /// ticks on the machine that owns it, so a noise emitted on a peer would be heard by a copy
        /// that cannot act on it. The instigator is the holder, not the gun: a receiver that aggros
        /// on gunfire targets whoever it is handed.
        /// </summary>
        private void ReportGunshot()
        {
            if (gunshotNoiseRadius <= 0f) return;

            Transform shooter = owner != null ? owner.transform : transform;
            Noise.Emit(NoiseType.Gunshot, transform.position, gunshotNoiseRadius, shooter, shooter);
        }

        /// <summary>
        /// Throw a target back by however much of the shot it caught.
        ///
        /// <para>
        /// Priced off the DAMAGE it took rather than off the pellet count, so distance thins the
        /// shove exactly as it thins the wound and a long shot does not launch what it barely
        /// scratched. <see cref="BlastPush"/> owns the three routes a shove can take — a player's
        /// own machine applies it, a creature leaps, anything else takes an impulse.
        /// </para>
        /// </summary>
        private void Shove(GameObject target, Vector3 aimDir, float damageDealt)
        {
            float fullHit = pelletCount * pelletDamage;
            if (fullHit <= 0f || fullHitKickSpeed <= 0f) return;

            float strength = Mathf.Clamp01(damageDealt / fullHit);
            float rad = kickTilt * Mathf.Deg2Rad;
            Vector3 velocity = (aimDir.normalized * Mathf.Cos(rad) + Vector3.up * Mathf.Sin(rad))
                               * (fullHitKickSpeed * strength);

            billedColliders.TryGetValue(target, out Collider collider);
            BlastPush.Apply(collider, target, velocity, fullHitKickSpeed,
                            BlastPush.Leap.Proportional(staggerDistance, staggerHeight, staggerSeconds),
                            itemMassReference, itemMassScaleRange);
        }

        /// <summary>
        /// Every machine, immediately on the owner's: the same seed picks the same spray, so what
        /// is drawn here and the damage on the server are one shot. `useSoundId` is already played
        /// by PlayUse; everything else is layered on top of it.
        /// </summary>
        protected override void Present()
        {
            if (UseArg.P == Vector3.zero) return;

            if (Misfires)
            {
                MisfirePresent();
                return;
            }

            TraceShot();

            bool firstPerson = OwnerIsLocal();
            Vector3 aimDir = UseArg.R * Vector3.forward;
            if (fx != null) fx.PlayShot(UseArg.P, aimDir, pellets, firstPerson);

            PlayImpactAudio();

            FlinchTheHit();

            if (firstPerson) KickHolder(aimDir);
        }

        /// <summary>
        /// Make everything alive that was hit react, once each. Animator triggers do not
        /// replicate, so each machine runs its own off the same trace the server billed — and the
        /// per-target set is what keeps a target that caught twenty pellets from being asked to
        /// flinch twenty times.
        /// </summary>
        private void FlinchTheHit()
        {
            flinched.Clear();

            foreach (PelletShotTrace.Pellet pellet in pellets)
            {
                if (!pellet.IsFlesh || !flinched.Add(pellet.Target)) continue;
                pellet.Target.GetComponentInChildren<AgentAnimatorDriver>()?.TriggerHurt();
            }
        }

        /// <summary>
        /// The trace both halves of the shot walk. Fills <see cref="pellets"/> from the use
        /// message, which is the same on every machine.
        /// </summary>
        private void TraceShot()
        {
            PelletShotTrace.Trace(UseArg.P, UseArg.R, UseArg.B, pelletCount, spreadAngle, range,
                                  damageMask, owner != null ? owner.transform : null, pellets);
        }

        /// <summary>
        /// One impact layer per KIND of thing the shot hit, not per pellet: the catalog dedupes on
        /// (id, sourceKey), so thirty calls would collapse to one anyway — and the two kinds are
        /// what the player actually needs to hear, since "did I hit something alive" is the
        /// question a shot at range leaves open.
        /// </summary>
        private void PlayImpactAudio()
        {
            Sfx.Play(reportId, transform.position, default, transform.GetInstanceID());

            bool fleshPlayed = false;
            bool hardPlayed = false;

            foreach (PelletShotTrace.Pellet pellet in pellets)
            {
                if (!pellet.Hit) continue;

                if (pellet.IsFlesh && !fleshPlayed)
                {
                    Sfx.Play(fleshImpactId, pellet.Point, GetInstanceID());
                    fleshPlayed = true;
                }
                else if (!pellet.IsFlesh && !hardPlayed)
                {
                    Sfx.Play(hardImpactId, pellet.Point, GetInstanceID() + 1);
                    hardPlayed = true;
                }

                if (fleshPlayed && hardPlayed) return;
            }
        }

        /// <summary>
        /// The recoil, on the holder's own machine only — their body is owner-authoritative, so
        /// this is the one place a velocity written on it survives.
        /// </summary>
        private void KickHolder(Vector3 aimDir)
        {
            if (look != null && fovKick > 0f)
            {
                look.SetFovOffset(fovKick);
                fovKickUntil = Time.time + fovKickSeconds;
                fovKickArmed = true;
            }

            if (recoilSpeed <= 0f || owner == null) return;

            var movement = owner.GetComponent<PlayerMovement>();
            var body = owner.GetComponent<Rigidbody>();
            if (movement == null || body == null) return;

            Vector3 back = (-Vector3.ProjectOnPlane(aimDir, Vector3.up).normalized
                            + Vector3.up * recoilUpwardBias).normalized;
            movement.EnsureMovableBody();
            if (body.isKinematic) return;

            body.linearVelocity += back * recoilSpeed;
            movement.CarryMomentum();
        }

        private void Update()
        {
            if (fovKickArmed && Time.time >= fovKickUntil)
            {
                fovKickArmed = false;
                if (look != null) look.SetFovOffset(0f);
            }
        }

        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);
            look = holder != null ? holder.GetComponent<PlayerLook>() : null;
        }

        public override void OnUnequipped(GameObject holder)
        {
            // Unequipping mid-kick would otherwise strand the holder's view wide open.
            ClearFovKick();
            look = null;
            base.OnUnequipped(holder);
        }

        private void OnDisable() => ClearFovKick();

        private void ClearFovKick()
        {
            if (look != null) look.SetFovOffset(0f);
            fovKickArmed = false;
        }

        protected virtual void OnValidate()
        {
            pelletCount = Mathf.Max(0, pelletCount);
            pelletDamage = Mathf.Max(0, pelletDamage);
            range = Mathf.Max(0f, range);
            fullDamageRange = Mathf.Clamp(fullDamageRange, 0f, range);
            refireSeconds = Mathf.Max(0f, refireSeconds);
            gunshotNoiseRadius = Mathf.Max(0f, gunshotNoiseRadius);
            fullHitKickSpeed = Mathf.Max(0f, fullHitKickSpeed);
            itemMassScaleRange.x = Mathf.Max(0.01f, itemMassScaleRange.x);
            itemMassScaleRange.y = Mathf.Max(itemMassScaleRange.y, itemMassScaleRange.x);
        }
    }
}
