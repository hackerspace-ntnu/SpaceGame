// Click a lever -> rotate the handle -> fire OnPulled.
// Wire OnPulled to anything (enable a hidden door, play sounds, trigger an InteriorPortal, etc.).
//
// This used to be a plain MonoBehaviour with no netcode, which made it the worst of the fixtures: a
// door at least announces itself by being visibly shut, but a lever's whole point is the UnityEvent
// on the end of it, and that event ran on the puller's machine alone. One player opened the hidden
// door and walked through a wall nobody else had; nothing on any screen said why.
//
// The state and the protocol live in NetLatch — see that file for the shape and why it is shared
// with DoorInteraction. What a lever adds is one thing: it is the case where the latch may be
// ONE-WAY, because a one-shot lever is exactly "a latch that can only travel forwards".
using System.Collections;
using FMODUnity;
using UnityEngine;
using UnityEngine.Events;
using SpaceGame.Audio;
using SpaceGame.Persistence;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// A lever, and whatever its pull is wired to.
    ///
    /// <para>
    /// <see cref="IPersistentEntity"/> because a lever qualifies for saving under none of
    /// <c>SaveablePolicy.NeedsSaving</c>'s other tests, and a one-shot lever that re-arms on every
    /// load is a hidden door that can be opened twice — or a cutscene that plays again.
    /// </para>
    /// </summary>
    public class LeverInteraction : MonoBehaviour, IInteractable, ILatchHost, IPersistentEntity
    {
        [Header("Visuals")]
        [Tooltip("The transform that rotates when pulled. If null, uses this GameObject.")]
        [SerializeField] private Transform handle;

        [Tooltip("Local euler offset applied to the handle when pulled.")]
        [SerializeField] private Vector3 pulledLocalEuler = new Vector3(60f, 0f, 0f);

        [SerializeField] private float animDuration = 0.6f;

        [Header("Behavior")]
        [Tooltip("If true, the lever can only be pulled once.")]
        [SerializeField] private bool oneShot = true;

        [Header("Events")]
        [Tooltip("Fires when the handle has finished animating to the pulled position.")]
        [SerializeField] private UnityEvent onPulled;

        [Tooltip("Fire OnPulled for a player who joins after this lever was already pulled.\n\n" +
                 "Leave this ON when the event describes STATE — enabling a hidden door, unlocking " +
                 "a gate, revealing a bridge. A joiner who never runs it is standing in a world " +
                 "nobody else is in, and nothing will ever tell them.\n\n" +
                 "Turn it OFF only when the event is a one-shot EFFECT — a portal, a cutscene, a " +
                 "sound — which would otherwise fire in a joiner's face for something that happened " +
                 "before they arrived.")]
        [SerializeField] private bool replayOnJoin = true;

        [Header("Audio")]
        [SerializeField] private SfxId pullId = SfxId.InteractLever;
        [SerializeField] private EventReference pullSound;

        private NetLatch latch;
        private Coroutine swing;
        private bool busy;
        private Quaternion restRotation;

        /// <summary>One lever, one latch. See ILatchHost for why this has to answer before Awake.</summary>
        public int LatchCount => 1;

        /// <summary>Whether the handle is standing in its pulled position, session-wide.</summary>
        public bool IsPulled => latch != null && latch.IsOn;

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// The restored pull travels through the latch, which is what makes a one-shot lever stay
        /// spent: <c>NetLatch.Accepts</c> refuses a one-way latch that is already on, so
        /// <see cref="CanInteract"/> goes false the moment the state lands and the lever cannot be
        /// pulled a second time in the loaded session.
        ///
        /// Whether <see cref="onPulled"/> runs again is the same question a late joiner asks, and it
        /// gets the same answer: <c>replayOnJoin</c>. A lever whose event describes STATE — a hidden
        /// door enabled, a gate unlocked — must re-run it, because nothing else in a freshly loaded
        /// world will. A lever whose event is a one-shot EFFECT — a cutscene, a portal — must not,
        /// and that is what stops a loaded save from playing a cutscene at the player.
        /// </summary>
        public void RestorePulled(bool pulled) => latch?.Restore(pulled);

        private void Awake()
        {
            if (handle == null) handle = transform;
            restRotation = handle.localRotation;

            // oneShot IS the one-way latch, rather than a second state machine beside one. A lever
            // that can be pulled again is then simply a latch that toggles: out on the first press,
            // back to rest on the next. That is a change from the old behaviour, which re-ran the
            // swing from rest every time and so SNAPPED the handle back on frame one of each repeat
            // pull; nothing in the project uses a non-one-shot lever yet, so nothing depended on it.
            latch = new NetLatch(this, ApplySwing, canChange: () => !busy, oneWay: oneShot);
        }

        // Null-conditional so a latch that failed to construct — a wiring mistake NetLatch throws
        // on — costs one loud error in Awake rather than one per frame forever after.
        private void OnEnable() => latch?.Enable();

        private void OnDisable() => latch?.Disable();

        /// <summary>
        /// Delegated to the latch so the crosshair, the key and the server's re-check all read the
        /// same sentence: a one-shot lever that has been pulled refuses, and so does one mid-swing.
        /// </summary>
        public bool CanInteract() => latch != null && latch.Accepts(latch.Next);

        /// <summary>
        /// Nothing moves from this call. It asks the server, which decides and tells every machine —
        /// which is what puts OnPulled on everybody's machine instead of only the puller's.
        /// </summary>
        public void Interact(Interactor interactor)
        {
            if (!CanInteract()) return;

            latch.Toggle();
        }

        /// <summary>
        /// Put the handle where the session says it is. Called on every machine by the latch.
        /// </summary>
        private void ApplySwing(bool pulled, bool instant)
        {
            Quaternion target = pulled ? restRotation * Quaternion.Euler(pulledLocalEuler)
                                       : restRotation;

            if (swing != null)
            {
                StopCoroutine(swing);
                swing = null;
                busy = false;
            }

            if (instant)
            {
                // The lever was already pulled before this machine was looking. Land on the pose
                // silently — a joiner should not hear a clunk for something that happened an hour
                // ago — and then decide about the consequence, which is the interesting half.
                handle.localRotation = target;

                if (pulled && replayOnJoin) Fire();
                return;
            }

            swing = StartCoroutine(SwingRoutine(target, fireOnArrival: pulled));
        }

        private IEnumerator SwingRoutine(Quaternion target, bool fireOnArrival)
        {
            busy = true;

            // At the start of the swing rather than the end: the clunk belongs to the moment the
            // player pulled it, not to whatever the lever eventually triggers.
            Sfx.Play(pullId, transform.position, pullSound, GetInstanceID());

            Quaternion from = handle.localRotation;
            float t = 0f;
            float dur = Mathf.Max(0.01f, animDuration);
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                handle.localRotation = Quaternion.Slerp(from, target, k);
                yield return null;
            }
            handle.localRotation = target;

            // Only on the way OUT. A lever swinging back to rest is undoing its own gesture, and
            // there is deliberately no onReleased to pair with it: a UnityEvent nobody has asked for
            // is a second thing every designer has to reason about, and every lever in the project
            // today is one-shot.
            if (fireOnArrival) Fire();

            swing = null;
            busy = false;
        }

        /// <summary>
        /// Run the designer's event, on every machine, exactly once per pull.
        ///
        /// Whatever it invokes owns its own replication — the same contract IInteractable has. That
        /// is not a caveat but the point: this fires symmetrically everywhere now, so an event wired
        /// to something purely local (SetActive on a hidden door) lands on every machine, and one
        /// wired to something already networked is stopped by that thing's own server gate rather
        /// than by a rule here that could only ever be a guess.
        /// </summary>
        private void Fire()
        {
            // A throwing handler must not take the rest of the pull with it. On a client this runs
            // inside a network dispatch, and although NetChannel already isolates handlers from each
            // other, the coroutine path above has no such net underneath it.
            try { onPulled?.Invoke(); }
            catch (System.Exception e) { Debug.LogException(e, this); }
        }
    }
}
