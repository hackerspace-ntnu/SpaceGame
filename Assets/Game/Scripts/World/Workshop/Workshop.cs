using UnityEngine;
using SpaceGame.Persistence;

namespace SpaceGame.World
{
    /// <summary>
    /// The workshop in the mountain: a web of round rooms that each turn about a hand cog, and a hoist that runs
    /// round the main room by itself. This is the root everything else hangs from, and it decides nothing: a
    /// turning room is decided by its own <see cref="WorkshopWheel"/>, nothing ties one room to another, and the
    /// hoist is the same on every machine by the clock (<see cref="WorkshopMachinery"/>).
    ///
    /// <para>
    /// <b>Saved.</b> How far every room has turned survives a reload through <c>WorkshopSaveable</c>, which is
    /// why this is a persistent entity.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Workshop : MonoBehaviour, IPersistentEntity
    {
        [SerializeField] private WorkshopMachinery machinery;

        private void Awake()
        {
            if (machinery == null)
                Debug.LogError($"[Workshop] '{name}' has no machinery. Rebuild it with " +
                               "Tools/SpaceGame/Workshop/Rebuild Workshop Prefab.", this);
        }
    }
}
