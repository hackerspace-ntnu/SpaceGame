// What a foam dab does to the frame it lands in.
//
// Kept apart from FoamGunNozzle because the two answer different questions. The nozzle is the BELL:
// the iris, the jet leaving it, and the loop that says it is running — all driven off one "is it
// spraying" flag. This is the IMPACT: the gob that bursts where a dab landed, the blast that
// punches out of the muzzle when the trigger goes down, and the kick that puts both into the
// player's hands. The split is the same one GravelBlastFx draws for the gravel blaster, and for the
// same reason: a bell that has to be told about landing points is a bell that has stopped being a
// bell.
//
// EVERY MACHINE RUNS ALL OF IT — it is reached from Present(), never from Use(). The shake is the
// exception, and it is not a special case so much as the ordinary meaning of "present it here":
// a camera kick is only real on the machine that owns the eye being kicked.
//
// WHY THE SHAKE IS FENCED THE WAY IT IS (GDC-L1-FEEL-0006). Screenshake is the most over-applied
// juice technique there is, and a HELD trigger is exactly the case the principle warns about — an
// unbounded sustained shake stops being force and starts being an unreadable frame. So:
//
//   • The press gets a real kick. The hold gets a small one, re-struck on an interval rather than
//     stacked every tick, so a five-second spray does not integrate into a seizure.
//   • Everything is multiplied by GameSettings.CameraShakeIntensity, which is the game's
//     shippable-off accessibility dial. CameraShakerHandler does NOT apply it for you — only the
//     arrival rig does, by hand, and this does the same.
//   • It fires only where the holder's own view camera is live. A remote player's copy has its
//     camera switched off, which makes "is this camera alive" the honest local test — and it
//     avoids Camera.main, which in this project is never the player's camera.
using FirstGearGames.SmoothCameraShaker;
using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Core;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The violent half of the foam gun: the gob that bursts where a dab lands, the muzzle blast on
    /// the press, and the camera kick behind both. Purely presentation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FoamSprayFx : MonoBehaviour
    {
        [Header("Emitters")]
        [Tooltip("The gob that bursts where a dab lands. EMITTED INTO, never played: one system " +
                 "serves every impact, and it is moved to each landing point in turn.")]
        [SerializeField] private ParticleSystem splat;

        [Tooltip("The flat puff that spreads across the surface under a gob. A CHILD of the gob " +
                 "system, so moving that one moves this one — but wired separately, because " +
                 "ParticleSystem.Emit reaches one system and never its children.")]
        [SerializeField] private ParticleSystem splatRing;

        [Tooltip("The pressure wave off the bell when the trigger goes down. Played with its " +
                 "children, so anything hung under it goes off with it.")]
        [SerializeField] private ParticleSystem blast;

        [Tooltip("Foam gobs thrown per landed dab. The dab rate is high, so this is per-dab spend " +
                 "the frame budget notices — see GDC-L1-PERF-0004.")]
        [SerializeField, Min(0)] private int splatCount = 90;

        [Tooltip("Puffs of the flat spread per landed dab. Fewer than the gobs: they are much " +
                 "bigger, and this is the layer that turns into a white wall at close range.")]
        [SerializeField, Min(0)] private int splatRingCount = 20;

        [Tooltip("One dab landing. It lives here rather than on the artifact because the impact " +
                 "is an ARRIVAL: it has to wait out the spray's flight time with the gob it " +
                 "belongs to, and a sound played from somewhere else would fire on the press.")]
        [SerializeField] private SfxId impactSound = SfxId.PortalPaintSplat;

        [Header("Camera kick")]
        [Tooltip("The kick asset, seeded from the shared damage shake by the builder and this " +
                 "gun's own from then on.")]
        [SerializeField] private ShakeData sprayShake;

        [Tooltip("How hard the trigger going down hits, before the player's intensity setting.")]
        [SerializeField, Range(0f, 2f)] private float pressMagnitude = 0.5f;

        [Tooltip("How hard the sustained rumble hits while the trigger is held. Deliberately a " +
                 "fraction of the press: a held jet is a texture, not an impact.")]
        [SerializeField, Range(0f, 2f)] private float holdMagnitude = 0.14f;

        [Tooltip("Seconds between rumbles while the trigger is held. The shakes are re-struck " +
                 "rather than stacked; a shorter interval than the asset's own duration would " +
                 "pile them into an unreadable frame.")]
        [SerializeField, Min(0.05f)] private float holdInterval = 0.22f;

        /// <summary>
        /// Who is holding this, so the kick can ask whether their eye is on this machine. Set by
        /// the artifact on equip, because an item does not otherwise know whose hand it is in.
        /// </summary>
        private AimProvider viewer;

        private float nextRumbleAt;

        /// <summary>One dab of foam still crossing the room, and when it gets there.</summary>
        private struct Arrival
        {
            public Vector3 Point;
            public Vector3 Normal;
            public float DueAt;
        }

        /// <summary>
        /// The dabs in flight. A fixed array rather than a queue: this is per-frame presentation
        /// code on an item every player may be holding, and it must not allocate.
        /// </summary>
        private readonly Arrival[] Pending = new Arrival[32];

        private int pendingCount;

        /// <summary>
        /// Whose view this gun is presenting through. Null on every machine but the holder's, and
        /// null again the moment it is put away.
        /// </summary>
        public void SetViewer(AimProvider provider)
        {
            viewer = provider;
            nextRumbleAt = 0f;
        }

        /// <summary>The trigger went down: the blast off the bell, and a real kick behind it.</summary>
        /// <param name="direction">Where the jet is pointing, so the wave leaves along it.</param>
        public void Press(Vector3 direction)
        {
            if (blast != null)
            {
                if (direction.sqrMagnitude > 1e-6f)
                    blast.transform.rotation = Quaternion.LookRotation(direction.normalized);

                blast.Play(true);
            }

            Kick(pressMagnitude);
            nextRumbleAt = Time.time + holdInterval;
        }

        /// <summary>
        /// One tick of a held trigger. The rumble is on its own clock rather than the tick's, so
        /// the hold rate and the shake rate are two separate decisions.
        /// </summary>
        public void Hold()
        {
            if (Time.time < nextRumbleAt) return;

            nextRumbleAt = Time.time + holdInterval;
            Kick(holdMagnitude);
        }

        /// <summary>Trigger up. Nothing to stop — every effect here is a burst.</summary>
        public void Release() => nextRumbleAt = 0f;

        /// <summary>
        /// A dab landed here: throw a gob of foam back out of it, along the surface it stuck to.
        ///
        /// <para>
        /// The system is MOVED to the landing point rather than the particles being placed there,
        /// so its shape module does the spreading. That is free because it simulates in world
        /// space: gobs already in the air from the last dab do not move with it.
        /// </para>
        /// </summary>
        /// <param name="delaySeconds">
        /// The spray's flight time to this point. The impact is what ARRIVAL looks like, so it
        /// waits exactly as long as <see cref="FoamBlob"/> waits before it starts to swell —
        /// burst it on the press and the gob goes off at the far wall while the jet is still
        /// leaving the muzzle.
        /// </param>
        public void Splat(Vector3 point, Vector3 normal, float delaySeconds)
        {
            if (splat == null || normal.sqrMagnitude < 1e-6f) return;

            if (delaySeconds <= 0f)
            {
                Burst(point, normal);
                return;
            }

            // Dropped rather than grown. The queue only has to cover the dabs in flight at once —
            // fifteen a second over well under a second — and a spray that somehow outran it is
            // one that must not also start allocating every frame (GDC-L1-PERF-0004).
            if (pendingCount >= Pending.Length) return;

            Pending[pendingCount++] = new Arrival
            {
                Point = point,
                Normal = normal,
                DueAt = Time.time + delaySeconds,
            };
        }

        /// <summary>The gob, the flat spread and the sound, all at the instant the foam gets there.</summary>
        private void Burst(Vector3 point, Vector3 normal)
        {
            // The ring rides the gob system as a child, so one move places both.
            splat.transform.SetPositionAndRotation(point, Quaternion.LookRotation(normal));

            if (splatCount > 0) splat.Emit(splatCount);
            if (splatRing != null && splatRingCount > 0) splatRing.Emit(splatRingCount);

            if (impactSound != SfxId.None) Sfx.Play(impactSound, point);
        }

        /// <summary>
        /// Fire the arrivals whose foam has landed. Swap-removed, so the order the gobs go off in
        /// is not the order they were sprayed — which nothing can see, and which keeps this O(n)
        /// on a list that is a handful of entries long.
        /// </summary>
        private void Update()
        {
            for (int i = pendingCount - 1; i >= 0; i--)
            {
                if (Time.time < Pending[i].DueAt) continue;

                Burst(Pending[i].Point, Pending[i].Normal);
                Pending[i] = Pending[--pendingCount];
            }
        }

        /// <summary>
        /// The kick, dosed and fenced. See the file header for why every clause here is load-bearing.
        /// </summary>
        private void Kick(float magnitude)
        {
            if (sprayShake == null || magnitude <= 0f) return;

            // Not our eye: a peer's copy of this gun has a holder whose camera is switched off.
            if (viewer == null || viewer.ViewCamera == null || !viewer.ViewCamera.isActiveAndEnabled)
                return;

            float scaled = magnitude * GameSettings.CameraShakeIntensity;
            if (scaled <= 0f) return;

            // A null instance means no CameraShaker is live in the scene — nothing to scale.
            ShakerInstance instance = CameraShakerHandler.Shake(sprayShake);
            instance?.MultiplyMagnitude(scaled, 0f); // 0 rate = applied on the first frame
        }

        /// <summary>
        /// Putting the gun away drops whatever was still in the air with it. Left in place, those
        /// arrivals would go off across the map the next time it was drawn.
        /// </summary>
        private void OnDisable()
        {
            Release();
            pendingCount = 0;
        }
    }
}
