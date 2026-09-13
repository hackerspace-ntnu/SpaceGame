// Fires FootstepCameraShake from a procedurally-walked machine's actual footfalls.
//
// FootstepCameraShake was written to be driven by ANIMATION EVENTS baked into a walk clip at the
// frames the contact lands. A machine walked by LeggedLocomotion has no walk clip and no events:
// the gait decides when each foot swings and plants, at runtime, against real ground. So the
// footfall has to be watched rather than scheduled -- which is strictly better, because a
// procedural gait's cadence changes with speed and terrain and a baked event's cannot.
//
// The edge, not the state. LegState.Swinging is true through the swing and false through the
// stance, so a foot lands on the frame it goes from true to false, exactly once. Polling the state
// instead would shake every frame the foot was down.
//
// Deliberately independent of AgentController and of who owns the entity. This is presentation: a
// remote client still runs the locomotion (see IExternallyPosed) so it still sees the footfalls,
// and a walker stomping past should be felt on every machine, not only the one deciding where it
// goes. FootstepCameraShake's own distance falloff is what keeps that from being a nuisance.
using SpaceGame.Locomotion;
using UnityEngine;

namespace SpaceGame.Presentation
{
    [RequireComponent(typeof(FootstepCameraShake))]
    public class LeggedFootstepShake : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("The locomotion whose feet to watch. Auto-found on this object if empty.")]
        [SerializeField] private LeggedLocomotion locomotion;
        [SerializeField] private FootstepCameraShake shake;

        [Header("Strength")]
        [Tooltip("Shake strength at a standstill. A machine shifting its weight in place still puts " +
                 "a foot down, and on something this heavy that should still be felt.")]
        [Range(0f, 2f)]
        [SerializeField] private float restingStrength = 0.55f;
        [Tooltip("Shake strength at the machine's top speed. Scaled between the two by how fast it " +
                 "is actually travelling, so a stroll does not hit like a charge.")]
        [Range(0f, 2f)]
        [SerializeField] private float runningStrength = 1f;

        // One flag per leg, indexed the way the locomotion indexes them. Sized on first use rather
        // than in Awake: LeggedLocomotion discovers its legs during ITS Awake, and the order the two
        // run in is not ours to decide.
        private bool[] wasSwinging;

        private void Awake()
        {
            if (!locomotion) locomotion = GetComponent<LeggedLocomotion>();
            if (!shake) shake = GetComponent<FootstepCameraShake>();
        }

        private void LateUpdate()
        {
            if (!locomotion || !shake || !locomotion.IsReady) return;

            int count = locomotion.LegCount;
            if (wasSwinging == null || wasSwinging.Length != count)
            {
                wasSwinging = new bool[count];
                // Seed from the current state rather than from false, or every leg that happens to
                // be planted on the first frame reports a landing it did not make -- both feet
                // stomping at once the moment the creature streams in.
                for (int i = 0; i < count; i++)
                    if (locomotion.TryGetFoot(i, out _, out bool swinging))
                        wasSwinging[i] = swinging;
                return;
            }

            float strength = Mathf.Lerp(restingStrength, runningStrength, TravelFraction());

            for (int i = 0; i < count; i++)
            {
                if (!locomotion.TryGetFoot(i, out _, out bool swinging)) continue;
                if (wasSwinging[i] && !swinging) shake.OnFootPlant(strength);
                wasSwinging[i] = swinging;
            }
        }

        /// How hard the machine is working, 0..1. MeasuredVelocity rather than the commanded twist:
        /// the legs clamp what the driver asks for, so this reports the speed actually achieved.
        private float TravelFraction()
        {
            float max = locomotion.MaxSpeed;
            if (max <= 1e-4f) return 0f;
            return Mathf.Clamp01(locomotion.MeasuredVelocity.magnitude / max);
        }

        private void OnValidate()
        {
            restingStrength = Mathf.Max(0f, restingStrength);
            runningStrength = Mathf.Max(0f, runningStrength);
        }
    }
}
