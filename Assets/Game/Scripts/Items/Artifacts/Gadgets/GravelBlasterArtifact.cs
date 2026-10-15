using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Items
{
    /// <summary>
    /// A handmade spring-driven pipe shotgun that empties a fistful of gravel out of both barrels
    /// at once — and, one shot in ten, backfires and gives the holder the blast instead.
    ///
    /// <para>
    /// The shot itself is <see cref="PelletGunArtifact"/>'s: thirty pellets, a wide cone, and a
    /// hard taper past <c>fullDamageRange</c> that keeps it a corridor weapon whose long shots are
    /// grit in the eyes rather than a kill (GDC-L1-BAL-0002, GDC-L1-BAL-0004). What this class
    /// adds is the backfire — the other half of that price, and the reason a no-ammo,
    /// high-damage gun is a decision rather than a default (GDC-L1-DESIGN-0002). The shot's seed
    /// decides it (<see cref="GravelBlastMath.Backfires"/>), so every machine agrees which shots
    /// went wrong.
    /// </para>
    /// </summary>
    public class GravelBlasterArtifact : PelletGunArtifact
    {
        [Header("Backfire")]
        [Tooltip("One shot in this many blows back into the holder. 0 disables backfiring.")]
        [SerializeField] private int backfireChance = 10;

        [Tooltip("Damage the holder takes from their own gun.")]
        [SerializeField] private int backfireDamage = 25;

        [Tooltip("Speed of the backwards fling handed to the holder, m/s.")]
        [SerializeField] private float backfireKickSpeed = 9f;

        [Tooltip("Upward tilt of that fling, degrees. Keeps the kick from being pure slide.")]
        [SerializeField] private float backfireKickTilt = 35f;

        protected override bool Misfires => GravelBlastMath.Backfires(UseArg.B, backfireChance);

        /// <summary>
        /// The gun gives the holder the blast: damage, plus a backwards fling. The fling rides
        /// NetMsg.Flung on the HOLDER's relay because their body is owner-authoritative — a
        /// velocity written here on the server would be overwritten within a tick; FlungBody on
        /// the one machine that owns the body applies it (and brings its own shake and FOV kick).
        /// </summary>
        protected override void MisfireUse()
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

        protected override void MisfirePresent()
        {
            if (fx != null) fx.PlayBackfire();
            Sfx.Play(SfxId.ImpactExplosion, transform.position, GetInstanceID());
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            backfireChance = Mathf.Max(0, backfireChance);
            backfireDamage = Mathf.Max(0, backfireDamage);
        }
    }
}
