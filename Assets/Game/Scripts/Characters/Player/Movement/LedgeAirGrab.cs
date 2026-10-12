// Catching a ledge in mid-air: hold Jump through a jump or a fall, and the first ledge that comes within
// arms' reach is climbed (GDC-L1-FEEL-0003 — holding forgives the timing a press would demand).
//
// A separate layer on LedgeClimber, on purpose: the wall, obstacle and grapple climbs are the predictable
// core, and this is an add-on. Disable or remove this component and nothing else changes. A double tap of
// Space (the back gear) always wins over a grab: BodyEquipmentController cancels the climb and opens it.
using UnityEngine;
using PlayerInputManager = SpaceGame.Core.PlayerInputManager;

namespace SpaceGame.Characters
{
    /// <summary>Grabs a ledge in mid-air while Jump is held, through <see cref="LedgeClimber"/>.</summary>
    [DefaultExecutionOrder(160)]
    [RequireComponent(typeof(LedgeClimber), typeof(PlayerMovement))]
    public class LedgeAirGrab : MonoBehaviour
    {
        [Tooltip("Lowest lip grabbed, metres above the feet. Anything lower is a landing.")]
        [SerializeField, Min(0f)] private float minHeight = 0.2f;

        [Tooltip("Highest lip grabbed, metres above the feet: arms' reach, with no jump on top.")]
        [SerializeField, Min(0.1f)] private float airReach = 3.3f;

        [Tooltip("Seconds after a jump leaves the ground before a grab is allowed, so the press that " +
                 "jumped is not also a grab.")]
        [SerializeField, Min(0f)] private float afterJumpDelay = 0.15f;

        private LedgeClimber climber;
        private PlayerMovement movement;
        private PlayerInputManager inputs;
        private float lastJumpTime = float.NegativeInfinity;

        private void Awake()
        {
            climber = GetComponent<LedgeClimber>();
            movement = GetComponent<PlayerMovement>();
        }

        private void Start()
        {
            inputs = GetComponent<PlayerController>().Input;
            movement.OnJumped += OnJumped;
        }

        private void OnDestroy()
        {
            if (movement != null) movement.OnJumped -= OnJumped;
        }

        private void OnJumped() => lastJumpTime = Time.time;

        private void FixedUpdate()
        {
            if (inputs == null || !inputs.JumpHeld) return;
            if (movement.IsOnGround || movement.IsTethered) return;
            if (Time.time - lastJumpTime < afterJumpDelay) return;

            climber.TryClimbFrom(transform.forward, minHeight, airReach);
        }
    }
}
