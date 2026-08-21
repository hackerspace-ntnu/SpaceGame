// A two-leaf door that every machine in the session agrees about.
//
// It used to be a plain MonoBehaviour with no netcode: the leaves swung on the machine that pressed
// E and stayed shut for everybody else. That is worse than it looks, because IsOpen below is what
// SandstormShelter reads to decide whether the inside of a ship is safe — so two players standing in
// the same hull disagreed about whether they were being sanded, with nothing on screen to explain it.
//
// All of the netcode now lives in NetLatch, which owns the state and the protocol. What is left here
// is the only part that is actually about doors: where the leaves point, and what it sounds like.
using FMODUnity;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Persistence;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Handles door interaction — rotates left and right door children in opposite directions.
    ///
    /// <para>
    /// <see cref="IPersistentEntity"/> because a door has none of the components
    /// <c>SaveablePolicy.NeedsSaving</c> otherwise looks for — no health, no NavMeshAgent, no
    /// dynamic body — so without the marker it would never be given a <c>SaveableEntity</c> and
    /// <c>DoorSaveable</c> would never run. Whether the hatch is shut is world state a player
    /// changed, which is exactly what the marker is for.
    /// </para>
    /// </summary>
    public class DoorInteraction : MonoBehaviour, IInteractable, ILatchHost, IPersistentEntity
    {
        [SerializeField]
        private float _rotationSpeed = 2f;

        [SerializeField]
        private Transform _leftDoor;

        [SerializeField]
        private Transform _rightDoor;

        [Header("Audio")]
        [SerializeField] private SfxId openId = SfxId.InteractDoorOpen;
        [SerializeField] private EventReference openSound;
        [SerializeField] private SfxId closeId = SfxId.InteractDoorClose;
        [SerializeField] private EventReference closeSound;

        /// <summary>How far each leaf swings. Mirrored, so the pair opens outwards.</summary>
        private const float SwingDegrees = 90f;

        private NetLatch latch;

        /// <summary>
        /// Whether the door is standing open. Read by SandstormShelter: shutting the hatch is what
        /// makes the inside of the ship safe, so something outside this class has to be able to ask.
        /// True from the moment the swing starts rather than when it finishes — the player has
        /// committed by then, and delaying the consequence by the animation reads as a bug.
        ///
        /// It is the LATCH's state, which means it is the server's answer and not this machine's
        /// opinion. That is the whole fix: shelter is decided by the same door for everyone.
        /// </summary>
        public bool IsOpen => latch != null && latch.IsOn;

        /// <summary>One door, one latch. See ILatchHost for why this has to answer before Awake.</summary>
        public int LatchCount => 1;

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// A door that was left open must come back open, and not only because it looks wrong shut:
        /// <c>SandstormShelter</c> reads <see cref="IsOpen"/> to decide whether a hull is safe, so a
        /// hatch the player sealed against a storm would be sand-exposed again on the first reload.
        ///
        /// Goes through the latch rather than the leaves, because the latch is what everything else
        /// in the session reads — moving the transforms alone would restore the picture of a door
        /// and none of its meaning.
        /// </summary>
        public void RestoreOpen(bool open) => latch?.Restore(open);

        // The authored, shut pose of each leaf, and the pose each is swinging between.
        //
        // LOCAL rotations, where the original stored world ones, and the change is load-bearing
        // twice over. A door lives on a ship that drives away: a world-space target goes stale the
        // moment the hull turns, and the leaf fights its own parent for the rest of the swing. And
        // the targets are now absolute — "the shut pose, times ninety degrees" rather than "wherever
        // you are now, times ninety degrees" — which is what lets the same state be applied more
        // than once without the door walking further open each time. NetLatch guarantees it will not
        // be, but a fixture whose correctness depends on never being told twice is a trap.
        private Quaternion _leftShut;
        private Quaternion _rightShut;
        private Quaternion _leftFrom, _leftTo;
        private Quaternion _rightFrom, _rightTo;

        private float _rotationTime;
        private bool _isRotating;

        private void Awake()
        {
            // Find the door children if not assigned
            if (_leftDoor == null)
                _leftDoor = transform.Find("LeftDoors");
            if (_rightDoor == null)
                _rightDoor = transform.Find("RightDoors");

            // In Awake and not Start: OnEnable subscribes the latch, and a state announcement can
            // arrive before the first Start runs. Capturing the shut pose afterwards would mean
            // answering that announcement against an identity rotation and slamming both leaves into
            // the hull.
            if (_leftDoor != null) _leftShut = _leftTo = _leftFrom = _leftDoor.localRotation;
            if (_rightDoor != null) _rightShut = _rightTo = _rightFrom = _rightDoor.localRotation;

            latch = new NetLatch(this, ApplySwing, canChange: () => !_isRotating);
        }

        // Null-conditional so a latch that failed to construct — a wiring mistake NetLatch throws
        // on — costs one loud error in Awake rather than one per frame forever after.
        private void OnEnable() => latch?.Enable();

        private void OnDisable() => latch?.Disable();

        private void Update()
        {
            if (!_isRotating) return;

            _rotationTime += Time.deltaTime * _rotationSpeed;
            float k = Mathf.Clamp01(_rotationTime);

            if (_leftDoor != null)
                _leftDoor.localRotation = Quaternion.Slerp(_leftFrom, _leftTo, k);
            if (_rightDoor != null)
                _rightDoor.localRotation = Quaternion.Slerp(_rightFrom, _rightTo, k);

            if (k >= 1f) _isRotating = false;
        }

        /// <summary>
        /// Whether the door will take a press right now. Delegated to the latch so the crosshair,
        /// the key and the server's re-check are all reading the same sentence — a prompt that
        /// lights up and then refuses the press is the failure Interactor.IsAvailable exists to
        /// prevent, and it can only stay prevented if there is one answer to give.
        /// </summary>
        public bool CanInteract() => latch != null && latch.Accepts(latch.Next);

        /// <summary>
        /// Interacts with the door — rotates left and right doors in opposite directions.
        ///
        /// Nothing swings from this call. It asks the server, which decides and tells every machine
        /// including this one; offline that round trip collapses into the same frame.
        /// </summary>
        public void Interact(Interactor interactor)
        {
            if (!CanInteract()) return;

            latch.Toggle();
        }

        /// <summary>
        /// Put the leaves where the session says they are. Called on every machine by the latch.
        /// </summary>
        private void ApplySwing(bool open, bool instant)
        {
            _leftTo = open ? _leftShut * Quaternion.Euler(0f, -SwingDegrees, 0f) : _leftShut;
            _rightTo = open ? _rightShut * Quaternion.Euler(0f, SwingDegrees, 0f) : _rightShut;

            if (instant)
            {
                // A door that was already open before this machine arrived. Land in the pose rather
                // than swinging into it, and stay silent: a joiner should not hear every door in the
                // world clunk at them the moment they spawn.
                if (_leftDoor != null) _leftDoor.localRotation = _leftTo;
                if (_rightDoor != null) _rightDoor.localRotation = _rightTo;
                _isRotating = false;
                return;
            }

            // Played at the leaf that actually swings where there is one, so a wide double door is
            // heard at the door rather than at the pivot its logic happens to sit on.
            Vector3 soundAt = _leftDoor != null ? _leftDoor.position : transform.position;
            Sfx.Play(open ? openId : closeId, soundAt,
                     open ? openSound : closeSound, GetInstanceID());

            _leftFrom = _leftDoor != null ? _leftDoor.localRotation : _leftShut;
            _rightFrom = _rightDoor != null ? _rightDoor.localRotation : _rightShut;

            _rotationTime = 0f;
            _isRotating = true;
        }
    }
}
