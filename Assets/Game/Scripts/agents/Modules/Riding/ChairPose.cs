// Sits a rider in a CHAIR, as opposed to a saddle.
//
// Drop this next to a MountModule and whoever takes that seat stops standing to attention in it
// and sits down properly: knees over the front edge, feet on the deck, arms at their sides.
//
// This is the counterpart to MountedRiderPose, and the split between them is deliberate. That one
// builds a pose bone by bone because straddling an animal is a shape no clip in this project has;
// a chair does have one -- "Sit Idle" on the player's animator -- so this class does not touch a
// single bone. It raises a flag and lets Mecanim blend, which is why it needs no blend weight, no
// per-frame LateUpdate, and no opinion about how wide the seat is.
//
// Being on the CHAIR rather than on the player is the other deliberate part. A player cannot tell
// a cockpit chair from an ostrich, so the knowledge of which seats are chairs lives on the seats.
// Adding this component is what makes a seat a chair; the ostrich simply does not get one.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Characters;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    public class ChairPose : MonoBehaviour
    {
        [Tooltip("Mount whose rider this seats. Found on this GameObject when empty. May be " +
                 "absent entirely: a seat that is filled by something other than a mount -- the " +
                 "ship's arrival seating, which does not go through MountModule -- drives this " +
                 "through PoseRider/ReleaseRider instead.")]
        [SerializeField] private MountModule mountModule;

        [Tooltip("Bool on the rider's animator that selects the seated idle. Serialized rather " +
                 "than hard-coded so a rig with a differently named parameter can be seated " +
                 "without a code change.")]
        [SerializeField] private string seatedParameter = "Seated";

        /// <summary>
        /// Who this chair is currently sitting down.
        ///
        /// <para>
        /// Kept only so teardown can undo itself. Seating is otherwise stateless -- raising a bool
        /// is idempotent, and a release names its own rider -- but a chair that is disabled or
        /// destroyed with somebody in it would otherwise leave that player's animator latched to
        /// the seated idle with nothing left alive to clear it, which is a body permanently stuck
        /// sitting in mid-air.
        /// </para>
        /// <para>
        /// A list rather than a single reference because one of these can serve several seats at
        /// once: the ship's arrival seating runs off a single component on the hull and puts four
        /// people in chairs.
        /// </para>
        /// </summary>
        private readonly List<Transform> seated = new();

        private void Awake()
        {
            if (!mountModule)
                mountModule = GetComponent<MountModule>();
        }

        private void OnEnable()
        {
            if (!mountModule)
                return;

            mountModule.Mounted += HandleMounted;
            mountModule.Dismounted += HandleDismounted;

            // Enabled onto an already-occupied seat -- a domain reload, or a component toggled
            // back on mid-flight. Without this the rider stands up and never sits again.
            if (mountModule.IsMounted)
                PoseRider(mountModule.MountedPlayerTransform);
        }

        private void OnDisable()
        {
            if (mountModule)
            {
                mountModule.Mounted -= HandleMounted;
                mountModule.Dismounted -= HandleDismounted;
            }

            ReleaseEveryone();
        }

        private void HandleMounted(PlayerMovement rider) =>
            PoseRider(mountModule ? mountModule.MountedPlayerTransform : null);

        private void HandleDismounted(PlayerMovement rider) =>
            ReleaseRider(rider ? rider.transform : null);

        /// <summary>
        /// Sit <paramref name="rider"/> down.
        ///
        /// <para>
        /// Public because a chair is filled by more than one system: a player takes a cockpit seat
        /// through <see cref="MountModule"/>, and the crash landing seats the whole crew through
        /// <c>SeatedRider</c>, which deliberately does not use mounts at all. Safe to call every
        /// frame -- <c>SeatedRider</c>'s repair pass effectively does.
        /// </para>
        /// </summary>
        public void PoseRider(Transform rider)
        {
            if (rider == null || !SetSeated(rider, true))
                return;

            if (!seated.Contains(rider))
                seated.Add(rider);
        }

        /// <summary>
        /// Stand <paramref name="rider"/> back up. Ignores anybody this chair is not holding, so a
        /// late release cannot cancel the pose of whoever has since taken the seat.
        /// </summary>
        public void ReleaseRider(Transform rider)
        {
            if (rider == null || !seated.Remove(rider))
                return;

            SetSeated(rider, false);
        }

        private void ReleaseEveryone()
        {
            foreach (Transform rider in seated)
            {
                if (rider != null)
                    SetSeated(rider, false);
            }

            seated.Clear();
        }

        /// <summary>
        /// Writes the flag, and reports whether there was an animator willing to take it.
        ///
        /// <para>
        /// The parameter is looked up rather than assumed: setting a bool an animator does not
        /// declare is not an error in Unity, it is a warning per call, once a frame, forever. A rig
        /// with no seated idle is a rig that stays standing, and saying so once is more use than
        /// flooding the console.
        /// </para>
        /// </summary>
        private bool SetSeated(Transform rider, bool value)
        {
            Animator animator = rider.GetComponentInChildren<Animator>(true);

            if (animator == null || animator.runtimeAnimatorController == null)
                return false;

            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.type != AnimatorControllerParameterType.Bool ||
                    parameter.name != seatedParameter)
                    continue;

                animator.SetBool(seatedParameter, value);
                return true;
            }

            if (value)
                Debug.LogWarning($"[ChairPose] '{rider.name}' has no bool '{seatedParameter}' on " +
                                 "its animator, so it will stand up in this chair.", this);
            return false;
        }
    }
}
