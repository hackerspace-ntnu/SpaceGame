using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;

namespace SpaceGame.World
{
    /// <summary>
    /// The place a carried load is set into, as something to aim at: the empty oxygen-plant mount. Carrying the load, the
    /// prompt offers to set it in; empty-handed, it says what is missing. The press only asks — the load's server checks that
    /// this player carries it and stands near enough (<see cref="Liftable.RequestPlace"/>), and the destination takes it.
    ///
    /// <para>
    /// On a trigger volume, sized generously: the player aiming at it is carrying a 3.8 m plant in front of them. A trigger
    /// answers the interaction ray only with an interactable on its own GameObject, which this is.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class LiftDock : MonoBehaviour, IInteractable, IContextualInteractable, IInteractionReadout, IInteractionMoment
    {
        [Tooltip("Where a load set in here goes: a component implementing ILiftDestination (the oxygen plant mount).")]
        [SerializeField] private MonoBehaviour destination;

        [SerializeField] private string label = "Oxygen plant mount";
        [SerializeField] private string placePrompt = "RMB: set the oxygen plant in its mount";
        [SerializeField] private string missingPrompt = "Oxygen plant missing: find it outside";

        private ILiftDestination Destination => destination as ILiftDestination;

        public string Label => label;
        public float? Value01 => null;
        public string ValueText => "";

        /// <summary>The body shows the set-down itself, not a reach.</summary>
        public CharacterMoment InteractionMoment => CharacterMoment.None;

        public string Prompt => LoadFor(LocalPlayer) != null ? placePrompt : missingPrompt;

        public bool CanInteract() => Destination != null;

        public bool CanInteract(Interactor interactor) => Destination != null && interactor != null;

        /// <summary>Does this dock take <paramref name="load"/>, so a press at it means "set it in" rather than "put it down"?</summary>
        public bool Takes(Liftable load) => load != null && Destination != null && Destination.Accepts(load);

        public void Interact(Interactor interactor)
        {
            GameObject who = interactor != null ? NetChannel.RootOf(interactor) : null;
            if (who == null || !Network.Owns(who.transform)) return;

            Liftable load = LoadFor(who);
            if (load != null)
            {
                load.RequestPlace();
                return;
            }

            // Nothing in the hands: say so on this machine only, as an oxygen dock refuses an empty hand.
            Sfx.Play(SfxId.InteractDenied, transform.position, GetInstanceID());
        }

        /// <summary>The load <paramref name="who"/> is carrying, if this dock takes it.</summary>
        private Liftable LoadFor(GameObject who)
        {
            Liftable load = Liftable.CarriedBy(who);
            return Takes(load) ? load : null;
        }

        private static GameObject LocalPlayer
        {
            get
            {
                Unity.Netcode.NetworkManager manager = Unity.Netcode.NetworkManager.Singleton;
                Unity.Netcode.NetworkObject player = manager != null && manager.IsListening ? manager.LocalClient?.PlayerObject : null;
                return player != null ? player.gameObject : null;
            }
        }
    }
}
