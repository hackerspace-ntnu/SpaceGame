// Q, standing next to a saddled animal, takes the saddle off.
//
// The saddle already offers "E: take saddle off" on its own grips, and that is the discoverable
// route -- it puts a prompt on the screen. This is the same request without the aiming, which
// matters more the bigger the animal is: on Appa the straps sit three metres up and a metre out,
// so lining a crosshair up on one is real work for a thing you do all the time.
//
// It deliberately does NOT duplicate the decision. Both paths call SaddleSocket.Request(false),
// so the refusals -- no saddle on, someone in the seat -- are written once, on the server.
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SaddleSocket))]
    public class SaddleQuickRelease : MonoBehaviour
    {
        [SerializeField] private SaddleSocket socket;

        [Tooltip("How close the player has to be, in metres from the animal's origin. Wants to be " +
                 "measured against the animal: it is 'standing next to him', so on something the " +
                 "size of Appa it is a bigger number than on a goat.")]
        [SerializeField] private float reach = 4f;

        [Tooltip("Input action that takes the saddle off. Bound to Q in the Player map — the same verb placeables use.")]
        [SerializeField] private string actionName = "Retrieve";

        private InputAction action;

        private void Awake()
        {
            if (socket == null) socket = GetComponent<SaddleSocket>();
        }

        private void OnEnable()
        {
            // Resolved late and re-resolved on every enable: the project-wide asset is not
            // guaranteed to exist yet at Awake in a scene that is still loading.
            action = InputSystem.actions != null ? InputSystem.actions.FindAction(actionName) : null;
            if (action == null)
                Debug.LogWarning($"{name}: no '{actionName}' action, so the saddle can only be " +
                                 "taken off by looking at it and pressing E.", this);
        }

        private void Update()
        {
            if (socket == null || !socket.IsSaddled) return;
            if (action == null || !action.WasPressedThisFrame()) return;

            GameObject actor = NearbyLocalPlayer();
            if (actor == null) return;

            socket.Request(false, actor);
        }

        /// <summary>
        /// The local player, if they are standing close enough to reach the girth.
        ///
        /// <para>
        /// The key press is already local -- it only happens on the machine that made it -- but on
        /// a host the scene also holds every remote player's body, so the press still has to be
        /// attributed to the one this machine owns rather than to whoever happens to be nearest.
        /// </para>
        /// </summary>
        private GameObject NearbyLocalPlayer()
        {
            float best = reach * reach;
            GameObject found = null;

            foreach (Interactor candidate in
                     FindObjectsByType<Interactor>(FindObjectsSortMode.None))
            {
                if (!Network.Owns(candidate)) continue;

                float d = (candidate.transform.position - transform.position).sqrMagnitude;
                if (d > best) continue;

                best = d;
                found = candidate.gameObject;
            }

            return found;
        }

        private void OnValidate() => reach = Mathf.Max(0.5f, reach);
    }
}
