// Drop this on the root of a hand-authored settlement arrangement (e.g. NomadSettlement/sections/market),
// right-click the header -> Roll Contents to bake which of its SpawnChance items are present at this
// spot. Move the section to get a different roll, like Settlement. Edit-time only.
using UnityEngine;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    public class SettlementSection : MonoBehaviour
    {
        [ContextMenu("Roll Contents")]
        public void RollContents() => SpawnChance.RollAll(transform);

        [ContextMenu("Show All Contents")]
        public void ShowAllContents() => SpawnChance.ShowAll(transform);
    }
}
