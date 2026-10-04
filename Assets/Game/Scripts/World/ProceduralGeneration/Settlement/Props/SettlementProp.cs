// A small thing in a settlement that people move about: a basket, a crate, a bucket, a jar. It rests at a
// PropRest; a resident on an errand picks it up and sets it down at another rest. To a player it is scenery
// (only building doors and pen gates can be interacted with); the item scanner still lists it.
//
// It owns no state. Where it is — which rest, or in someone's hands — is one short in the
// SettlementPropSync of the building it was authored in, decided by the server and shown by every machine.
// While carried the whole object is switched off; what a resident holds is the prop's item, drawn
// in the hand by the equipment like any carried tool.
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    public sealed class SettlementProp : MonoBehaviour, IScanTarget
    {
        [Tooltip("What this becomes in a hand. Must be in ResidentTuning.carryItems for residents to carry it.")]
        [SerializeField] private InventoryItem item;

        [Tooltip("Where it rests when the settlement is new.")]
        [SerializeField] private PropRest home;

        public InventoryItem Item => item;
        public PropRest Home => home;

        /// <summary>Stand at <paramref name="rest"/>, or vanish (carried) when it is null. Called by the sync on every machine.</summary>
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

        public bool IsScannable => isActiveAndEnabled;
        public Vector3 ScanPosition => transform.position;
        public ScanClass ScanClass => ScanClass.Item;
        public string ScanLabel => item != null ? item.itemName : name;

        private void OnEnable() => ScannerRegistry.Register(this);
        private void OnDisable() => ScannerRegistry.Unregister(this);
    }
}
