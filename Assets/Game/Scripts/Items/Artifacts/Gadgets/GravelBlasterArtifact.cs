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
    /// A handmade spring-driven pipe shotgun that empties a fistful of gravel out of both barrels
    /// at once — and, one shot in ten, backfires and gives the holder the blast instead.
    ///
    /// The owner rolls one random seed into the use message; <see cref="GravelBlastMath"/> derives
    /// the pellet spread AND the backfire from that seed, so the damage the server bills and the
    /// spray every machine draws are provably the same shot. Both sides walk it through the one
    /// <see cref="GravelShotTrace"/>, and the presentation is <see cref="GravelBlastFx"/>'s.
    ///
    /// <para>
    /// The shot reaches a long way for a scattergun and throws a great many pellets, and neither
    /// makes it the answer to everything: <see cref="GravelBlastMath.DamageFalloff"/> tapers a
    /// pellet's damage past <see cref="fullDamageRange"/>, so the weapon stays a corridor weapon
    /// whose long shots are grit in the eyes rather than a kill (GDC-L1-BAL-0002,
    /// GDC-L1-BAL-0004). Backfire is the other half of that price and the reason a no-ammo,
    /// high-damage gun is a decision rather than a default (GDC-L1-DESIGN-0002).
    /// </para>
    /// </summary>
    public class GravelBlasterArtifact : ToolItem
    {
        /// <summary>
        /// Server. Pellet damage, the shove it lands with, and the backfire's self-damage are all
        /// shared world state, so exactly one machine may decide them.
        /// </summary>
        public override UseAuthority Authority => UseAuthority.Server;

        [Header("Blast")]
        [Tooltip("Gravel pellets per shot, across both barrels. Every one of them is traced, billed " +
                 "and drawn as its own streak, so this is a visible number rather than an abstract " +
                 "damage multiplier.")]
        [SerializeField] private int pelletCount = 30;

        [Tooltip("Half-angle of the spread cone, in degrees.")]
        [SerializeField] private float spreadAngle = 9f;

        [Tooltip("How far a pellet carries, in metres. Long on purpose — what stops a distant shot " +
                 "being a good shot is the falloff below, not a wall the gravel stops at.")]
        [SerializeField] private float range = 70f;

        [Tooltip("Damage per pellet at point-blank range. A shot that lands every pellet on one " +
                 "target is worth pelletCount times this.")]
        [SerializeField] private int pelletDamage = 5;

        [Tooltip("Metres over which a pellet keeps its full damage. Past this it tapers to " +
                 "farDamageFraction at maximum range.")]
        [SerializeField] private float fullDamageRange = 15f;

        [Tooltip("What a pellet is worth at maximum range, as a fraction of pelletDamage.")]
        [SerializeField, Range(0f, 1f)] private float farDamageFraction = 0.25f;

        [Tooltip("What the gravel can hurt. Triggers are always ignored.")]
        [SerializeField] private LayerMask damageMask = ~0;

        [Header("Knockback")]
        [Tooltip("Speed a target takes when EVERY pellet lands on it, m/s; a partial hit is " +
                 "proportional. This is what makes a point-blank shot read as a hit by a truck " +
                 "rather than as a number going down.")]
        [SerializeField] private float fullHitKickSpeed = 13f;

        [Tooltip("Upward tilt of that shove, degrees. Load-bearing on a player: PlayerMovement " +
                 "never deletes vertical velocity, and the rise is what keeps the horizontal half " +
                 "alive long enough to be felt.")]
        [SerializeField] private float kickTilt = 18f;

        [Tooltip("How far a point-blank shot staggers a creature, metres. Creatures are moved by " +
                 "their motors, so the only thing a blast can do to one is ask it to leap.")]
        [SerializeField] private float staggerDistance = 3.5f;

        [SerializeField] private float staggerHeight = 0.9f;
        [SerializeField] private float staggerSeconds = 0.4f;

        [Tooltip("Impulse scaling reference for loose items: a body this heavy takes the full kick.")]
        [SerializeField] private float itemMassReference = 14f;

        [Tooltip("Bounds on that mass scaling, so a crate is not immovable and a tin can does not " +
                 "leave the chunk.")]
        [SerializeField] private Vector2 itemMassScaleRange = new Vector2(0.3f, 1.6f);

        [Header("Backfire")]
        [Tooltip("One shot in this many blows back into the holder. 0 disables backfiring.")]
        [SerializeField] private int backfireChance = 10;

        [Tooltip("Damage the holder takes from their own gun.")]
        [SerializeField] private int backfireDamage = 25;

        [Tooltip("Speed of the backwards fling handed to the holder, m/s.")]
        [SerializeField] private float backfireKickSpeed = 9f;

        [Tooltip("Upward tilt of that fling, degrees. Keeps the kick from being pure slide.")]
        [SerializeField] private float backfireKickTilt = 35f;

        [Header("Presentation")]
        [Tooltip("Everything the shot looks and sounds like. On this prefab, added by the builder.")]
        [SerializeField] private GravelBlastFx fx;

        [Tooltip("The body of the report, layered under useSoundId. Both must be DIFFERENT source " +
                 "keys or the catalog dedupes the second away and the shot collapses to one thin " +
                 "layer (GDC-L1-FEEL-0004).")]
        [SerializeField] private SfxId reportId = SfxId.ImpactExplosion;

        [Tooltip("Played once per shot that hit something soft.")]
        [SerializeField] private SfxId fleshImpactId = SfxId.ImpactFlesh;

        [Tooltip("Played once per shot that hit anything else.")]
        [SerializeField] private SfxId hardImpactId = SfxId.ImpactProjectile;

        [Tooltip("Recoil shove handed to the holder on a clean shot, m/s. Small on purpose: this " +
                 "is a jolt that sells the discharge, not the repulsor's movement tool.")]
        [SerializeField] private float recoilSpeed = 4.5f;

        [Tooltip("Upward fraction mixed into that shove.")]
        [SerializeField] private float recoilUpwardBias = 0.12f;

        [Tooltip("FOV punch on the holder, degrees. The camera does the job hitstop would, which " +
                 "this codebase rules out on purpose — Time.timeScale on a host stalls the " +
                 "authoritative simulation for everyone else (GDC-L1-FEEL-0005).")]
        [SerializeField] private float fovKick = 7f;

        [SerializeField] private float fovKickSeconds = 0.18f;

        /// <summary>
        /// The traced shot, reused rather than allocated: thirty pellets is thirty raycasts on
        /// every machine watching, and the authority walks the same list a frame later.
        /// </summary>
        private readonly List<GravelShotTrace.Pellet> pellets = new List<GravelShotTrace.Pellet>();

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

        /// <summary>
        /// Owner-side, before the request leaves — the only machine whose aim is honest. The seed
        /// travels too: every machine must agree whether THIS shot backfires, and a re-roll per
        /// machine would have the server billing a backfire while the peers draw a clean shot.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            if (aimProvider == null) return;

            Ray ray = aimProvider.GetAimRay();
            arg.P = ray.origin;
            arg.R = Quaternion.LookRotation(ray.direction);
            arg.B = Random.Range(int.MinValue, int.MaxValue);
        }

        /// <summary>Authority only: what the shot does to the world — or to the holder.</summary>
        protected override void Use()
        {
            // Default-struct guard: a use that never went through OnRequestUse (no aim provider)
            // carries a zero origin, and tracing pellets from the world origin would spray a spot
            // nobody is standing in.
            if (UseArg.P == Vector3.zero) return;

            if (GravelBlastMath.Backfires(UseArg.B, backfireChance))
            {
                Backfire();
                return;
            }

            if (pelletDamage <= 0 || pelletCount <= 0) return;

            TraceShot();

            // Pellets are summed per target and billed once: NetDamage per pellet would be thirty
            // messages, and a HealthComponent spans several colliders that must not each collect
            // the full count.
            billed.Clear();
            billedColliders.Clear();

            foreach (GravelShotTrace.Pellet pellet in pellets)
            {
                if (!pellet.Hit) continue;

                float damage = pelletDamage * GravelBlastMath.DamageFalloff(
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
        /// Throw a target back by however much of the shot it caught.
        ///
        /// <para>
        /// Priced off the DAMAGE it took rather than off the pellet count, so distance thins the
        /// shove exactly as it thins the wound and a shot at seventy metres does not launch what
        /// it barely scratched. <see cref="BlastPush"/> owns the three routes a shove can take —
        /// a player's own machine applies it, a creature leaps, anything else takes an impulse.
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
        /// The gun gives the holder the blast: damage, plus a backwards fling. The fling rides
        /// NetMsg.Flung on the HOLDER's relay because their body is owner-authoritative — a
        /// velocity written here on the server would be overwritten within a tick; FlungBody on
        /// the one machine that owns the body applies it (and brings its own shake and FOV kick).
        /// </summary>
        private void Backfire()
        {
            if (owner == null) return;

            NetDamage.Apply(owner, backfireDamage, transform);

            Vector3 aimDir = UseArg.R * Vector3.forward;
            var fling = new NetArg
            {
                P = GravelBlastMath.BackfireVelocity(aimDir, backfireKickSpeed, backfireKickTilt),
            };
            NetMessaging.NetSendTo(owner, NetMsg.Flung, fling, NetTo.All);
        }

        /// <summary>
        /// Every machine, immediately on the owner's: the same seed picks the same outcome, so the
        /// spray here and the damage on the server are one shot. `useSoundId` is already played by
        /// PlayUse; everything else is layered on top of it.
        /// </summary>
        protected override void Present()
        {
            if (UseArg.P == Vector3.zero) return;

            if (GravelBlastMath.Backfires(UseArg.B, backfireChance))
            {
                if (fx != null) fx.PlayBackfire();
                Sfx.Play(SfxId.ImpactExplosion, transform.position, GetInstanceID());
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

            foreach (GravelShotTrace.Pellet pellet in pellets)
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
            GravelShotTrace.Trace(UseArg.P, UseArg.R, UseArg.B, pelletCount, spreadAngle, range,
                                  damageMask, owner != null ? owner.transform : null, pellets);
        }

        /// <summary>
        /// One impact layer per KIND of thing the shot hit, not per pellet: the catalog dedupes on
        /// (id, sourceKey), so thirty calls would collapse to one anyway — and the two kinds are
        /// what the player actually needs to hear, since "did I hit something alive" is the
        /// question a spread weapon at range leaves open.
        /// </summary>
        private void PlayImpactAudio()
        {
            Sfx.Play(reportId, transform.position, default, transform.GetInstanceID());

            bool fleshPlayed = false;
            bool hardPlayed = false;

            foreach (GravelShotTrace.Pellet pellet in pellets)
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

        private void OnValidate()
        {
            pelletCount = Mathf.Max(0, pelletCount);
            pelletDamage = Mathf.Max(0, pelletDamage);
            backfireChance = Mathf.Max(0, backfireChance);
            backfireDamage = Mathf.Max(0, backfireDamage);
            range = Mathf.Max(0f, range);
            fullDamageRange = Mathf.Clamp(fullDamageRange, 0f, range);
            fullHitKickSpeed = Mathf.Max(0f, fullHitKickSpeed);
            itemMassScaleRange.x = Mathf.Max(0.01f, itemMassScaleRange.x);
            itemMassScaleRange.y = Mathf.Max(itemMassScaleRange.y, itemMassScaleRange.x);
        }
    }
}
