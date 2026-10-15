// A pose a settlement prop can rest at — where the builder stood a basket, a free patch of floor beside a
// pile or a counter — and the errand spot a resident stands at to pick up or set down a prop there.
// Scene content on the building prefabs, gathered at runtime in hierarchy order, so an index names the
// same rest on every machine.
using UnityEngine;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    public sealed class PropRest : MonoBehaviour
    {
        [Tooltip("The errand spot a resident stands at to reach this rest. Its use decides which chores reach it.")]
        [SerializeField] private SettlementSpot spot;

        public SettlementSpot Spot => spot;

        public Vector3 Position => transform.position;
        public Quaternion Rotation => transform.rotation;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.8f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * 0.1f, new Vector3(0.4f, 0.2f, 0.4f));
            if (spot != null) Gizmos.DrawLine(transform.position, spot.transform.position);
        }
    }
}
