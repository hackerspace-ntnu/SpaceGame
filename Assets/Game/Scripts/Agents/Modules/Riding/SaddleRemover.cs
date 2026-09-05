// "Take the saddle off", offered by looking at the saddle and not at the animal under it.
//
// A trigger collider that carries its own IInteractable is the one thing Interactor.ResolveAlongRay
// will answer with while letting the ray pass through otherwise — so this sits on a trigger on the
// saddle, and the animal's own solid collider goes on offering "ride" exactly as before. Put the
// component on the animal root instead and the two verbs fight over every square metre of it.
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    [RequireComponent(typeof(Collider))]
    public class SaddleRemover : MonoBehaviour, IInteractable, IRetrievable, IInteractionReadout
    {
        [SerializeField] private string label = "Saddle";

        private SaddleSocket socket;

        /// <summary>Called by the socket that built this saddle. There is no other way to find it:
        /// the saddle is instantiated onto a bone, so a parent search would cross the whole rig.</summary>
        public void Bind(SaddleSocket owner) => socket = owner;

        private void Reset()
        {
            Collider own = GetComponent<Collider>();
            if (own != null) own.isTrigger = true;
        }

        // A saddle with a rider on it stays on. Taking it off would drop the seat out from under
        // them and disable the module still holding them.
        public bool CanInteract() => socket != null && socket.IsSaddled && !RiderAboard();

        public void Interact(Interactor interactor) => TakeOff(interactor);

        // Q as well as E, and the same Q that picks up every other placeable. A saddle IS placed
        // into the world -- onto an animal rather than onto the ground -- so the verb that undoes
        // that should not be a different key here than it is on a lantern.
        public bool CanRetrieve() => CanInteract();

        public void Retrieve(Interactor interactor) => TakeOff(interactor);

        private void TakeOff(Interactor interactor)
        {
            if (!CanInteract() || interactor == null) return;
            socket.Request(false, interactor.gameObject);
        }

        private bool RiderAboard()
        {
            MountModule mount = socket.GetComponentInChildren<MountModule>(true);
            return mount != null && mount.IsMounted;
        }

        public string Label => label;
        public string Prompt => "E or Q: take saddle off";
        public float? Value01 => null;
        public string ValueText => "";
    }
}
