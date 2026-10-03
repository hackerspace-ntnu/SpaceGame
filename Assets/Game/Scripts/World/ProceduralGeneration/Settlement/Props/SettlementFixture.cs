// A piece of settlement furniture the item scanner can list: a forge, a loom, a table, a bed of tubers. It is
// scenery, not something a player can interact with — only building doors and pen gates are. Residents find the
// same fixture through the SettlementSpot children it carries (a post, a seat, an errand stop); this component is
// only the scanner's side of it.
//
// Purely presentational and local: nothing here is sent or saved.
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    public sealed class SettlementFixture : MonoBehaviour, IScanTarget
    {
        [SerializeField] private string displayName;
        [SerializeField] private ScanClass scanClass = ScanClass.Site;

        public string DisplayName => displayName;

        public bool IsScannable => isActiveAndEnabled;
        public Vector3 ScanPosition => transform.position;
        public ScanClass ScanClass => scanClass;
        public string ScanLabel => displayName;

        private void OnEnable() => ScannerRegistry.Register(this);
        private void OnDisable() => ScannerRegistry.Unregister(this);
    }
}
