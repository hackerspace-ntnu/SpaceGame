using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Gameplay;

namespace SpaceGame.Weapons
{
    /// <summary>
    /// What the orb is actually for.
    ///
    /// <para>
    /// Drifting, it spits bolts at the ground and at whatever it floats past — that is
    /// <see cref="BallLightningBoltTargeting"/>, and it is only ever a light show. This is the same
    /// gesture made real: when something that can bleed comes within reach, the orb earths itself
    /// through every such body at once and is gone. It is a weapon that never has to be aimed at
    /// anything, which is the point of firing a slow wandering ball instead of a bullet.
    /// </para>
    /// <para>
    /// <b>The arc is the orb's own bolt.</b> Nothing new is drawn. The discharge borrows the
    /// shader's single direct bolt through <see cref="BallLightningBoltTargeting.StrikeAt"/> and
    /// whips it between the bodies it is killing, fast enough that the eye reads several
    /// simultaneous arcs rather than one being dragged around. Drawing a LineRenderer instead would
    /// have put a second, differently shaded lightning in the same frame as the first.
    /// </para>
    /// <para>
    /// <b>Who bills.</b> Every machine runs its own copy of the shot and exactly one may charge the
    /// target for it — see <see cref="Projectile.Cosmetic"/>. The arc, the flash and the report
    /// deliberately run on all of them, the same split <see cref="Projectile.OnImpact"/> already
    /// uses: gating the spectacle on authority would leave the kill silent and invisible for
    /// everyone except whoever resolved it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class BallLightningDischarge : MonoBehaviour
    {
        private enum Phase
        {
            /// <summary>Drifting, sweeping for something to earth through.</summary>
            Drifting,

            /// <summary>Emptying itself into the bodies it found.</summary>
            Arcing,

            /// <summary>Spent. The projectile reads this and destroys itself.</summary>
            Done
        }

        [Header("Trigger")]
        [Tooltip("How close a damageable body has to come, in metres, before the orb earths through it. This is the weapon's whole feel: too small and it behaves like a bullet that has to be aimed, too large and it goes off across the room.")]
        [SerializeField] private float dischargeRadius = 6f;

        [Tooltip("What the discharge can reach. Triggers are always ignored, and anything without health or IDamageable above it is skipped whatever the mask says.")]
        [SerializeField] private LayerMask damageMask = ~0;

        [Tooltip("Seconds between proximity sweeps. Cheap — one non-allocating overlap — but there is no reason to run it every frame on something moving this slowly.")]
        [SerializeField] private float scanInterval = 0.05f;

        [Header("Damage")]
        [Tooltip("Dealt to every body caught, each exactly once. Whole points — NetDamage discards anything that rounds to zero.")]
        [SerializeField] private int damage = 100;

        [Header("Arc")]
        [Tooltip("The orb's bolt targeting. Left empty it is found on this object or below; without it the discharge still kills, it just does it invisibly.")]
        [SerializeField] private BallLightningBoltTargeting boltTargeting;

        [Tooltip("How long the arc is held before the orb goes out, in seconds. Long enough to read as a discharge, short enough that it is clearly not a beam.")]
        [SerializeField] private float arcDuration = 0.45f;

        [Tooltip("How many times a second the bolt jumps to another of the bodies being earthed through. Below about ten it reads as one arc being moved rather than several at once.")]
        [SerializeField] private float arcSwitchRate = 26f;

        [Header("Flash")]
        [Tooltip("Colour of the burst thrown off at the moment it discharges.")]
        [ColorUsage(true, true)]
        [SerializeField] private Color flashColor = new Color(0.65f, 0.85f, 1.6f);

        [Tooltip("Peak brightness of that burst, faded to nothing across its life.")]
        [SerializeField] private float flashIntensity = 220f;

        [Tooltip("How far the burst reaches, in metres.")]
        [SerializeField] private float flashRange = 22f;

        [Tooltip("Seconds the burst takes to fall to nothing. It outlives the orb on purpose, so the light does not vanish with the thing that cast it.")]
        [SerializeField] private float flashDuration = 0.35f;

        private readonly List<GameObject> victims = new List<GameObject>();
        private readonly List<Vector3> victimPoints = new List<Vector3>();

        private Phase phase = Phase.Drifting;
        private float nextScanTime;
        private float arcEndTime;

        /// <summary>Whether the orb is currently emptying itself, and so should hold still.</summary>
        public bool IsDischarging => phase == Phase.Arcing;

        /// <summary>Whether the orb has finished discharging and should now be destroyed.</summary>
        public bool Spent => phase == Phase.Done;

        private void Awake()
        {
            if (boltTargeting == null)
            {
                boltTargeting = GetComponentInChildren<BallLightningBoltTargeting>();
            }
        }

        /// <summary>
        /// Driven by the projectile once it has launched, rather than running off its own Update,
        /// so that an orb still charging in the barrel cannot discharge into whatever is standing
        /// next to the person holding it.
        /// </summary>
        /// <param name="source">
        /// Whoever fired, for kill credit and provocation. Also the root the sweep refuses to bill,
        /// so the orb cannot kill its own author.
        /// </param>
        /// <param name="bill">Whether this copy of the shot is the one allowed to deal the damage.</param>
        public void Tick(Transform source, bool bill)
        {
            switch (phase)
            {
                case Phase.Drifting:
                    Sweep(source, bill);
                    break;

                case Phase.Arcing:
                    DriveArc();
                    break;
            }
        }

        private void Sweep(Transform source, bool bill)
        {
            if (Time.time < nextScanTime) return;

            nextScanTime = Time.time + Mathf.Max(0.01f, scanInterval);

            if (RadiusDamage.Collect(transform.position, dischargeRadius, damageMask, source, victims) == 0)
            {
                return;
            }

            Discharge(source, bill);
        }

        private void Discharge(Transform source, bool bill)
        {
            // All of them, in the same instant, and only from the copy that owns the shot.
            if (bill)
            {
                for (int i = 0; i < victims.Count; i++)
                {
                    NetDamage.Apply(victims[i], damage, source);
                }
            }

            // Snapshot now. The bodies are being killed as the arc is drawn, and a corpse may be
            // despawned — or dragged off by a ragdoll — before the arc ends.
            victimPoints.Clear();
            for (int i = 0; i < victims.Count; i++)
            {
                victimPoints.Add(victims[i] != null ? victims[i].transform.position : transform.position);
            }

            phase = Phase.Arcing;
            arcEndTime = Time.time + Mathf.Max(0.05f, arcDuration);

            Sfx.Play(SfxId.WeaponBallLightningArc, transform.position, GetInstanceID());
            SpawnFlash();
        }

        private void DriveArc()
        {
            if (Time.time >= arcEndTime || victimPoints.Count == 0)
            {
                phase = Phase.Done;
                return;
            }

            if (boltTargeting == null) return;

            int index = Mathf.FloorToInt(Time.time * Mathf.Max(1f, arcSwitchRate)) % victimPoints.Count;

            // Follow anything still alive and moving; a body that has gone keeps the point it died on.
            if (victims[index] != null)
            {
                victimPoints[index] = victims[index].transform.position;
            }

            boltTargeting.StrikeAt(victimPoints[index]);
        }

        /// <summary>
        /// Unparented on purpose: the projectile is destroyed the moment the arc ends, and a light
        /// hanging off it would be taken with it half way through its fade.
        /// </summary>
        private void SpawnFlash()
        {
            GameObject flashObject = new GameObject("BallLightningDischargeFlash");
            flashObject.transform.position = transform.position;

            Light flash = flashObject.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = flashColor;
            flash.intensity = flashIntensity;
            flash.range = flashRange;
            flash.shadows = LightShadows.None;

            flashObject.AddComponent<BallLightningFlash>().Begin(flashIntensity, Mathf.Max(0.01f, flashDuration));
        }

        private void OnValidate()
        {
            dischargeRadius = Mathf.Max(0f, dischargeRadius);
            damage = Mathf.Max(0, damage);
            arcDuration = Mathf.Max(0.05f, arcDuration);
            flashDuration = Mathf.Max(0.01f, flashDuration);
            arcSwitchRate = Mathf.Max(1f, arcSwitchRate);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, dischargeRadius);
        }
    }
}
