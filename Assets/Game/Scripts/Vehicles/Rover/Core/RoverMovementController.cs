using UnityEngine;

namespace SpaceGame.Vehicles
{
    /// <summary>
    /// Handles the movement logic for the rover.
    /// Separate from control logic for better separation of concerns.
    ///
    /// <para>
    /// Deliberately free of netcode, and the separation above is what makes that possible: this is
    /// a motor, driven entirely by the direction it is handed, so "who is allowed to decide where
    /// this rover goes" is a question one layer up. <see cref="RoverController"/> answers it, and
    /// only calls <see cref="UpdateMovement"/> on a machine entitled to move the thing.
    /// </para>
    /// <para>
    /// The same split the dune foiler uses against its own simulation assembly, and it is worth
    /// keeping: a motor with an authority check in it is a motor that cannot be unit-tested,
    /// replayed, or driven by anything but a live session.
    /// </para>
    /// </summary>
    public class RoverMovementController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 10f;
        [SerializeField] private float rotationSpeed = 45f;
        [SerializeField] private float acceleration = 5f;

        private Vector3 targetDirection;
        private float currentSpeed;

        public void SetTargetDirection(Vector3 direction)
        {
            if (direction.sqrMagnitude > 0.001f)
            {
                targetDirection = direction.normalized;
                return;
            }

            targetDirection = Vector3.zero;
        }

        public void UpdateMovement(Transform roverTransform)
        {
            if (roverTransform == null)
                return;

            // Accelerate toward target speed
            float targetSpeed = targetDirection.sqrMagnitude > 0.001f ? moveSpeed : 0f;
            currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, acceleration * Time.deltaTime);

            // Move rover forward
            Vector3 movement = roverTransform.forward * currentSpeed * Time.deltaTime;
            roverTransform.position += movement;

            // Rotate toward target direction
            if (targetDirection.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(targetDirection, Vector3.up);
                roverTransform.rotation = Quaternion.RotateTowards(
                    roverTransform.rotation,
                    targetRotation,
                    rotationSpeed * Time.deltaTime
                );
            }
        }

        public void SetMoveSpeed(float speed) => moveSpeed = speed;
        public void SetRotationSpeed(float speed) => rotationSpeed = speed;
        public float GetCurrentSpeed() => currentSpeed;
    }
}
