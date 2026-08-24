using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Items
{
    /// <summary>
    /// A handmade spring-driven pipe shotgun that fires a cloud of gravel out of both barrels at
    /// once — and, one shot in ten, backfires and gives the holder the blast instead.
    ///
    /// The owner rolls one random seed into the use message; <see cref="GravelBlastMath"/> derives
    /// the pellet spread AND the backfire from that seed, so the damage the server bills and the
    /// spray every machine draws are provably the same shot. Backfire on the design side is the
    /// tradeoff that keeps a high-damage, no-ammo weapon from being the dominant option
    /// (GDC-L1-DESIGN-0002); the layered muzzle feedback is GDC-L1-FEEL-0004.
    /// </summary>
    public class GravelBlasterArtifact : ToolItem
    {
        /// <summary>
        /// Server. Pellet damage and the backfire's self-damage are shared world state, so exactly
        /// one machine may decide them.
        /// </summary>
        public override UseAuthority Authority => UseAuthority.Server;

        [Header("Blast")]
        [Tooltip("Gravel pellets per shot, across both barrels.")]
        [SerializeField] private int pelletCount = 14;

        [Tooltip("Half-angle of the spread cone, in degrees.")]
        [SerializeField] private float spreadAngle = 7f;

        [Tooltip("How far a pellet carries, in metres.")]
        [SerializeField] private float range = 35f;

        [Tooltip("Damage per pellet. A point-blank shot lands every pellet on one target.")]
        [SerializeField] private int pelletDamage = 9;

        [Tooltip("What the gravel can hurt. Triggers are always ignored.")]
        [SerializeField] private LayerMask damageMask = ~0;

        [Header("Backfire")]
        [Tooltip("One shot in this many blows back into the holder. 0 disables backfiring.")]
        [SerializeField] private int backfireChance = 10;

        [Tooltip("Damage the holder takes from their own gun.")]
        [SerializeField] private int backfireDamage = 25;

        [Tooltip("Speed of the backwards fling handed to the holder, m/s.")]
        [SerializeField] private float backfireKickSpeed = 9f;

        [Tooltip("Upward tilt of that fling, degrees. Keeps the kick from being pure slide.")]
        [SerializeField] private float backfireKickTilt = 35f;

        [Header("Effects")]
        [Tooltip("Where the blast leaves the pipes. Placed by GravelBlasterBuilder.")]
        [SerializeField] private Transform muzzle;

        [Tooltip("Tumbling rock chunks out of the muzzle. Cosmetic, every machine plays it.")]
        [SerializeField] private ParticleSystem gravelBurst;

        [Tooltip("Sand-coloured powder cloud at the muzzle.")]
        [SerializeField] private ParticleSystem muzzleDust;

        [Tooltip("Hot spring-steel sparks at the muzzle.")]
        [SerializeField] private ParticleSystem muzzleSparks;

        [Tooltip("Everything that leaves the BREECH instead when the gun backfires. Playing the parent plays its children.")]
        [SerializeField] private ParticleSystem backfireBurst;

        [Tooltip("Brief muzzle flash. Enabled by Present, cut by Update.")]
        [SerializeField] private Light muzzleFlash;

        [Tooltip("Seconds the muzzle flash stays lit.")]
        [SerializeField] private float flashSeconds = 0.08f;

        private float flashUntil = float.NegativeInfinity;

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

            Vector3[] pellets = GravelBlastMath.PelletDirections(
                UseArg.B, UseArg.R, pelletCount, spreadAngle);

            // Pellets are summed per target and billed once: NetDamage per pellet would be
            // fourteen messages, and a HealthComponent spans several colliders that must not
            // each collect the full count.
            var billed = new Dictionary<GameObject, int>();

            foreach (Vector3 dir in pellets)
            {
                if (!Physics.Raycast(UseArg.P, dir, out RaycastHit hit, range, damageMask,
                                     QueryTriggerInteraction.Ignore))
                    continue;

                // The holder cannot shoot themselves with the forward blast — the barrels start
                // outside their body, but the aim ray starts at their camera.
                if (owner != null && hit.collider.transform.IsChildOf(owner.transform)) continue;

                HealthComponent health = hit.collider.GetComponentInParent<HealthComponent>();
                GameObject target = health != null ? health.gameObject : hit.collider.gameObject;

                billed.TryGetValue(target, out int pelletsIn);
                billed[target] = pelletsIn + 1;
            }

            foreach (KeyValuePair<GameObject, int> entry in billed)
                NetDamage.Apply(entry.Key, entry.Value * pelletDamage,
                                owner != null ? owner.transform : transform);
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
        /// Every machine, immediately on the owner's: the same seed picks the same outcome, so
        /// the spray here and the damage on the server are one shot. `useSoundId` is already
        /// played by PlayUse; the backfire adds its own detonation on top.
        /// </summary>
        protected override void Present()
        {
            if (UseArg.P == Vector3.zero) return;

            if (GravelBlastMath.Backfires(UseArg.B, backfireChance))
            {
                if (backfireBurst != null) backfireBurst.Play(true);
                Sfx.Play(SfxId.ImpactExplosion, transform.position, GetInstanceID());
                return;
            }

            if (gravelBurst != null) gravelBurst.Play();
            if (muzzleDust != null) muzzleDust.Play();
            if (muzzleSparks != null) muzzleSparks.Play();

            if (muzzleFlash != null)
            {
                muzzleFlash.enabled = true;
                flashUntil = Time.time + flashSeconds;
            }
        }

        private void Update()
        {
            if (muzzleFlash != null && muzzleFlash.enabled && Time.time >= flashUntil)
                muzzleFlash.enabled = false;
        }

        private void OnDisable()
        {
            // Unequipping mid-flash would otherwise leave the light on for the next equip.
            if (muzzleFlash != null) muzzleFlash.enabled = false;
        }

        private void OnValidate()
        {
            pelletCount = Mathf.Max(0, pelletCount);
            pelletDamage = Mathf.Max(0, pelletDamage);
            backfireChance = Mathf.Max(0, backfireChance);
            backfireDamage = Mathf.Max(0, backfireDamage);
            range = Mathf.Max(0f, range);
        }
    }
}
