// A placed storm ward: while it stands, any storm whose sand reaches its radius is held off -- it
// fades out, keeps drifting and ageing unseen, and fades back in when the ward is picked up, if it is
// still meant to be there. See StormSuppressors. Every pulse its ring rides up the mast and slams
// down, and the impact sends a shockwave out across the sand to show the ground it holds.
//
// Multiplayer: the ward is a spawned NetworkObject, so every machine has it and registers it, and
// every machine holds off the same storms with nothing sent. The pulse and its stroke
// (StormWardStroke) run on each machine's own timer and are purely cosmetic.
//
// Persistence: nothing of its own. The ward is a placed object (TransformSaveable), and the storms it
// holds off are saved like any other storm; a reloaded ward holds them off again. Where the pulse
// timer stood is not worth a save field -- a reloaded ward strikes one stroke after it appears.
using SpaceGame.Audio;
using SpaceGame.World.Weather;
using UnityEngine;

namespace SpaceGame.Items
{
    [DisallowMultipleComponent]
    public class StormWard : MonoBehaviour, IStormSuppressor
    {
        [Tooltip("Metres. A storm whose sand, feathered edge included, comes within this distance " +
                 "of the ward is held off for as long as the ward stands. The shockwave travels " +
                 "exactly this far, so what the player sees is the protected ground.")]
        [SerializeField, Min(1f)] private float wardRadius = 100f;

        [Tooltip("Seconds between pulses. Cosmetic: storms are held off continuously, not on the pulse.")]
        [SerializeField, Min(0.25f)] private float pulseInterval = 3f;

        [Tooltip("The part that strikes: the emitter ring, with the flash riding on it. Moved along the " +
                 "ward's up axis from wherever it rests. Optional; without it the pulse still fires.")]
        [SerializeField] private Transform head;

        [SerializeField] private StormWardStroke stroke = new StormWardStroke();

        [Tooltip("The expanding ring of dust. Its start speed is set from the radius and the " +
                 "particles' lifetime, so the two cannot disagree.")]
        [SerializeField] private ParticleSystem shockwave;

        [Tooltip("Played with the shockwave, at the emitter. Optional.")]
        [SerializeField] private ParticleSystem flash;

        [Tooltip("A sound FILE under StreamingAssets/Audio played on every pulse, on every machine. A " +
                 "file rather than an SfxId because no shipped FMOD event is a shockwave, and new " +
                 "events cannot be authored; SfxFile plays it through FMOD. Empty is silent.")]
        [SerializeField] private string pulseFile = "storm_ward_pulse.ogg";

        [SerializeField, Range(0f, 1f)] private float pulseVolume = 0.7f;

        [Tooltip("Metres inside which the pulse is at full volume.")]
        [SerializeField, Min(0f)] private float pulseFullVolumeRange = 6f;

        [Tooltip("Metres at which the pulse fades to nothing.")]
        [SerializeField, Min(1f)] private float pulseHearingRange = 70f;

        private static readonly Color GizmoColour = new Color(0.4f, 0.8f, 1f, 0.6f);

        private float untilPulse;
        private float sincePulse;
        private Vector3 headRest;

        public float WardRadius => wardRadius;

        public Vector3 SuppressionCentre => transform.position;
        public float SuppressionRadius => wardRadius;

        private void OnEnable() => StormSuppressors.Register(this);

        private void OnDisable() => StormSuppressors.Unregister(this);

        private void Awake()
        {
            // The first pulse gets a whole stroke, rather than firing from a ring at rest.
            untilPulse = stroke.Lead;
            sincePulse = stroke.Length;

            // In the ward's own space, so neither the FBX importer's parent scales nor wherever the
            // ward was set down can change how far the ring travels.
            if (head != null) headRest = transform.InverseTransformPoint(head.position);

            if (shockwave == null)
            {
                Debug.LogWarning($"[StormWard] {name} has no shockwave; it wards, unseen.", this);
                return;
            }

            ParticleSystem.MainModule main = shockwave.main;
            main.startSpeed = wardRadius / main.startLifetime.constantMax;
        }

        private void Update()
        {
            untilPulse -= Time.deltaTime;
            sincePulse += Time.deltaTime;
            if (untilPulse <= 0f)
            {
                untilPulse += pulseInterval;
                sincePulse = 0f;
                Pulse();
            }

            if (head != null)
                head.position = transform.TransformPoint(headRest) +
                                transform.up * stroke.Offset(untilPulse, sincePulse);
        }

        private void OnValidate() => pulseInterval = Mathf.Max(pulseInterval, stroke.Length);

        private void Pulse()
        {
            if (shockwave != null) shockwave.Play();
            if (flash != null) flash.Play();
            SfxFile.Play(pulseFile, transform.position, pulseVolume, pulseFullVolumeRange, pulseHearingRange);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = GizmoColour;
            Gizmos.DrawWireSphere(transform.position, wardRadius);
        }
    }
}
