using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// One turning room: its floor and the wall that rides on it, with the room's one entrance in it. It has no
    /// stops: it swings after whatever angle <see cref="Follow"/> last named, and stays there.
    ///
    /// <para>
    /// It moves on the physics clock and by writing its own transform, which is what the
    /// <c>WalkerPlatformCarrier</c> beside it reads to carry anybody standing on the floor. Its colliders are convex
    /// pieces under a kinematic body, so a wall that swings across a tunnel mouth is solid the whole way.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorkshopTurntable : MonoBehaviour
    {
        [Tooltip("The axis it turns about, in its own frame. Its sign is what makes a positive angle turn the way " +
                 "the model counts its angles.")]
        [SerializeField] private Vector3 localAxis = Vector3.up;

        [Tooltip("Degrees a second it swings after the angle it is told to follow. Faster than any wheel turns a " +
                 "room, so on the machine that decides the angle it is simply there.")]
        [SerializeField, Min(0.1f)] private float followDegreesPerSecond = 40f;

        private Quaternion rest;
        private bool restKnown;
        private float angle;
        private float target;

        /// <summary>Degrees turned from the authored pose, right now. Not wrapped: it counts turns.</summary>
        public float Angle => angle;

        /// <summary>Still on its way to the angle it was last given.</summary>
        public bool IsTurning => !Mathf.Approximately(angle, target);

        /// <summary>
        /// Head for an angle. Not eased: whoever turns the wheel eases it. <paramref name="instant"/> lands there
        /// at once: the room was already there before this machine was looking (a late joiner, a loaded save).
        /// </summary>
        public void Follow(float degrees, bool instant)
        {
            target = degrees;
            if (!instant) return;

            angle = target;
            Pose();
        }

        private void Awake() => RememberRest();

        private void FixedUpdate() => Step(Time.fixedDeltaTime);

        /// <summary>One physics step toward where it was last sent. Public so a test can take it.</summary>
        public void Step(float deltaTime)
        {
            if (!IsTurning) return;

            angle = Mathf.MoveTowards(angle, target, followDegreesPerSecond * deltaTime);
            Pose();
        }

        private void Pose()
        {
            RememberRest();
            transform.localRotation = rest * Quaternion.AngleAxis(angle, localAxis);
        }

        // Lazy as well as in Awake: an instant Follow can arrive from a restore before this object has woken.
        private void RememberRest()
        {
            if (restKnown) return;
            rest = transform.localRotation;
            restKnown = true;
        }
    }
}
