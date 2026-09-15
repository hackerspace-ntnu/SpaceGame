// Shakes the player camera when a heavy walker plants a foot.
//
// Driven by ANIMATION EVENTS baked into the walk clip at the exact frames the
// contact lands, not by a timer and not by a collider. The frames come from
// measuring the foot's lowest point across the cycle (see _Source~/contacts.py
// beside the model), so the jolt is locked to the footfall no matter what the
// animator's playback speed is set to.
//
// Range matters here. An eighteen-metre walker is visible long before it is
// felt, and a camera that rattles every 1.2 seconds because something is
// stomping around on the far side of the map is a bug, not atmosphere. So the
// shake falls off with distance and stops entirely past maxRange.
using FirstGearGames.SmoothCameraShaker;
using UnityEngine;

namespace SpaceGame.Presentation
{
    public class FootstepCameraShake : MonoBehaviour
    {
        [Header("Shake")]
        [SerializeField] private ShakeData shakeData;

        [Header("Falloff (metres)")]
        [Tooltip("Inside this distance the step shakes at full strength.")]
        [SerializeField] private float fullStrengthRange = 15f;
        [Tooltip("Past this distance the step is not felt at all.")]
        [SerializeField] private float maxRange = 70f;

        [Header("Safety")]
        [Tooltip("Ignore a second footfall closer together than this. Guards against " +
                 "an animation event firing twice across a loop seam.")]
        [SerializeField] private float minInterval = 0.2f;

        private float _nextAllowed;

        /// Animation-event target. Unity binds events by name and allows exactly one
        /// float argument, so this must stay a single non-overloaded method -- adding
        /// an overload makes the binding ambiguous and Unity drops the event silently.
        /// `strength` scales the step: use it for a lighter first contact or a heavier
        /// landing without authoring a second ShakeData.
        public void OnFootPlant(float strength)
        {
            if (shakeData == null || strength <= 0f) return;
            if (Time.time < _nextAllowed) return;

            // No shaker in the scene means Shake() silently returns null, so bail here
            // instead: it also gives us the listener position for the falloff.
            CameraShaker shaker = CameraShakerHandler.DefaultCameraShaker;
            if (shaker == null) return;

            float distance = Vector3.Distance(shaker.transform.position, transform.position);
            if (distance > maxRange) return;

            float scale = strength;
            if (distance > fullStrengthRange)
                scale *= 1f - Mathf.InverseLerp(fullStrengthRange, maxRange, distance);
            if (scale <= 0.01f) return;

            _nextAllowed = Time.time + minInterval;

            ShakerInstance instance = CameraShakerHandler.Shake(shakeData);
            if (instance != null && !Mathf.Approximately(scale, 1f))
                instance.MultiplyMagnitude(scale, 0f);   // moveRate 0 == apply instantly
        }
    }
}
