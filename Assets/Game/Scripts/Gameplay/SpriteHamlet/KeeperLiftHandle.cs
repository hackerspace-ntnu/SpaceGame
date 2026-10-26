using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay.SpriteHamlet
{
    /// <summary>
    /// What the crosshair finds on a keeper during the witch fight: "pick up". A trigger of its own on
    /// a child, because the keeper's body already answers with its dialog and the interactor takes the
    /// first <see cref="IInteractable"/> it meets; <see cref="KeeperCarry"/> switches this collider off
    /// whenever the keeper cannot be lifted, so outside the fight the dialog answers as before.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class KeeperLiftHandle : MonoBehaviour, IInteractable, IInteractionReadout, IInteractionMoment
    {
        [Tooltip("{0} is the keeper's name.")]
        [SerializeField] private string promptFormat = "RMB: pick up {0}";

        private KeeperCarry keeper;

        private KeeperCarry Keeper => keeper != null ? keeper : keeper = GetComponentInParent<KeeperCarry>();

        public bool CanInteract() => Keeper != null && Keeper.CanBeLifted;

        public void Interact(Interactor interactor)
        {
            if (!CanInteract() || interactor == null) return;
            Keeper.RequestLift(interactor.transform.root.gameObject);
        }

        public string Label => Keeper != null ? Keeper.KeeperName : string.Empty;

        public string Prompt => string.Format(promptFormat, Label);

        public float? Value01 => null;

        public string ValueText => null;

        public CharacterMoment InteractionMoment => CharacterMoment.PickedUp;
    }
}
