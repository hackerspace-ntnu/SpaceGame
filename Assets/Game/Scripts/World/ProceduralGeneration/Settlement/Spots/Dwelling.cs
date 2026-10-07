// A building (or tent) people live in. Put it on the prefab root; its door is the first
// SettlementEntrance under it. A settlement's population is the sum of its dwellings' beds, and the
// residents who share a dwelling are a household — family to each other.
using UnityEngine;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    public sealed class Dwelling : MonoBehaviour
    {
        [Tooltip("How many residents live here. The settlement's population is the sum over its dwellings.")]
        [SerializeField, Min(1)] private int beds = 2;

        public int Beds => beds;

        /// <summary>The door residents leave and come home by; null when the prefab has no SettlementEntrance.</summary>
        public SettlementEntrance Door => GetComponentInChildren<SettlementEntrance>(true);
    }
}
