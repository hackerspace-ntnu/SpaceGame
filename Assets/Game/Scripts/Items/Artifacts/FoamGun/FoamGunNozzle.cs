// The bell end of the foam gun: the shutter that irises open, the jet that comes out of it, and the
// loop that says it is running.
//
// Kept apart from FoamGunArtifact because none of it is a use. The artifact decides where foam goes
// and who pays for it; this is what a bystander sees and hears, and it runs on every machine off a
// single "is it spraying" flag — which is exactly the split Use()/Present() already draws through
// the whole item system.
using FMODUnity;
using SpaceGame.Audio;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>The moving, noisy half of the foam gun. Purely presentation.</summary>
    [DisallowMultipleComponent]
    public sealed class FoamGunNozzle : MonoBehaviour
    {
        [Header("Parts")]
        [Tooltip("Marker_Muzzle — where a dab leaves the bell. Falls back to this transform.")]
        [SerializeField] private Transform muzzle;

        [Tooltip("Mesh_FoamGun_Iris — the shutter. Its origin is already on the bell axis, so it " +
                 "turns about its own local Y and nothing else moves.")]
        [SerializeField] private Transform iris;

        [Tooltip("How far the shutter turns when the gun is running, in degrees.")]
        [SerializeField] private float irisOpenDegrees = 40f;

        [Tooltip("How fast it gets there, in degrees per second. Fast enough to read as a snap on " +
                 "the trigger rather than as a machine waking up.")]
        [SerializeField, Min(1f)] private float irisSpeed = 320f;

        [Header("Jet")]
        [Tooltip("The stream of foam leaving the bell. Emits on every machine while the trigger " +
                 "is down; it draws nothing that decides anything.")]
        [SerializeField] private ParticleSystem jet;

        [Header("Audio")]
        [Tooltip("A LOOP, not a one-shot. Started through LoopingEmitter so there is a handle to " +
                 "stop it with — Sfx.Play on a sustained event runs for the rest of the session.")]
        [SerializeField] private SfxId sprayLoop = SfxId.PortalSprayLoop;

        [SerializeField] private EventReference sprayLoopEvent;

        private readonly LoopingEmitter loop = new LoopingEmitter();

        /// <summary>
        /// The shutter's authored pose. Captured rather than assumed, because the export bakes no
        /// transforms: an FBX node carries whatever rotation the .blend gave it, and assigning
        /// localRotation outright would flatten the part to identity on the first sprayed frame.
        /// </summary>
        private Quaternion irisRest;

        private float irisAngle;
        private bool spraying;

        /// <summary>Where the stream is being thrown, so a peer's jet points at the same lump.</summary>
        private Vector3 aimPoint;

        /// <summary>Where a dab leaves the gun.</summary>
        public Vector3 MuzzlePosition => muzzle != null ? muzzle.position : transform.position;

        private void Awake()
        {
            if (iris != null) irisRest = iris.localRotation;
            aimPoint = MuzzlePosition + transform.forward;
        }

        private void OnEnable() => ApplyIris();

        // Reachable from both, because a loop cleaned up in only one of them leaks whenever the
        // game exits through the other.
        private void OnDisable()
        {
            SetSpraying(false);
            loop.Stop(false);
        }

        private void OnDestroy() => loop.Stop(false);

        /// <summary>Run or stop the jet, the shutter and the loop together. Safe to call twice.</summary>
        public void SetSpraying(bool on)
        {
            if (spraying == on) return;
            spraying = on;

            if (jet != null)
            {
                if (on) jet.Play(true);
                else jet.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            if (on)
            {
                if (sprayLoop != SfxId.None || !sprayLoopEvent.IsNull)
                    loop.Play(sprayLoop, gameObject, sprayLoopEvent);
            }
            else
            {
                loop.Stop();
            }
        }

        /// <summary>
        /// Where this tick's foam is going. Every machine is told the landing point in the use
        /// message, so a peer — whose copy of a remote player has an AimProvider with no camera
        /// behind it — still throws the stream the right way.
        /// </summary>
        public void AimAt(Vector3 point) => aimPoint = point;

        private void Update()
        {
            irisAngle = Mathf.MoveTowards(irisAngle, spraying ? irisOpenDegrees : 0f,
                                          irisSpeed * Time.deltaTime);
            ApplyIris();
        }

        /// <summary>
        /// Point the emitter after the look and the hold pose have both moved this frame — the
        /// muzzle rides the fist, so aiming it in Update would trail one frame behind the gun.
        /// </summary>
        private void LateUpdate()
        {
            if (jet == null || !spraying) return;

            Vector3 direction = aimPoint - MuzzlePosition;
            if (direction.sqrMagnitude < 1e-6f) return;

            jet.transform.rotation = Quaternion.LookRotation(direction);
        }

        private void ApplyIris()
        {
            if (iris == null) return;

            iris.localRotation = irisRest * Quaternion.Euler(0f, irisAngle, 0f);
        }
    }
}
