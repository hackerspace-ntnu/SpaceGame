// A small thing in a settlement that people move about: a basket, a crate, a bucket, a jar. It rests at a
// PropRest; a resident on an errand picks it up and sets it down at another rest; a player can take it.
//
// It owns no state. Where it is — which rest, in someone's hands, taken for good — is one short in the
// SettlementPropSync of the building it was authored in, decided by the server and shown by every machine.
// While carried or taken the whole object is switched off; what a resident holds is the prop's item, drawn
// in the hand by the equipment like any carried tool.
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Presentation;
using UnityEngine;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    public sealed class SettlementProp : MonoBehaviour, IInteractable, IInteractionReadout, IInteractionMoment, IScanTarget
    {
        [Tooltip("What this becomes in a hand or a bag. Must be in ResidentTuning.carryItems for residents to carry it.")]
        [SerializeField] private InventoryItem item;

        [Tooltip("Where it rests when the settlement is new.")]
        [SerializeField] private PropRest home;

        private SettlementPropSync sync;

        public InventoryItem Item => item;
        public PropRest Home => home;

        private SettlementPropSync Sync => sync != null ? sync : sync = GetComponentInParent<SettlementPropSync>(true);

        /// <summary>Stand at <paramref name="rest"/>, or vanish (carried, taken) when it is null. Called by the sync on every machine.</summary>
        internal void ShowAt(PropRest rest)
        {
            if (rest == null)
            {
                gameObject.SetActive(false);
                return;
            }

            transform.SetPositionAndRotation(rest.Position, rest.Rotation);
            gameObject.SetActive(true);
        }

        public bool CanInteract() => item != null && Sync != null;

        public void Interact(Interactor interactor)
        {
            if (interactor != null && CanInteract()) Sync.RequestTake(this, interactor);
        }

        public string Label => item != null ? item.itemName : name;
        public string Prompt => "RMB: take";
        public float? Value01 => null;
        public string ValueText => null;

        public CharacterMoment InteractionMoment => CharacterMoment.PickedUp;

        public bool IsScannable => isActiveAndEnabled;
        public Vector3 ScanPosition => transform.position;
        public ScanClass ScanClass => ScanClass.Item;
        public string ScanLabel => Label;

        private void OnEnable() => ScannerRegistry.Register(this);
        private void OnDisable() => ScannerRegistry.Unregister(this);
    }
}
