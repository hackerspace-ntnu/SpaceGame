// Put this on any item inside a hand-authored settlement arrangement (a section prefab such as
// NomadSettlement/sections/market) to make it optional. The roll is seeded off the item's own world
// position, so every placed copy of a section comes out different but reproducible, and it is baked
// into the scene at edit time (SettlementSection's context menu, or Settlement.Generate) -- every
// machine loads the same result, nothing is rolled at runtime.
using UnityEngine;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    public class SpawnChance : MonoBehaviour
    {
        [Tooltip("Chance this item is present in a placed settlement. 1 = always.")]
        [SerializeField, Range(0f, 1f)] private float chance = 0.5f;

        /// <summary>Shows or hides every <see cref="SpawnChance"/> item under <paramref name="root"/> by its own seeded roll. Idempotent: the same positions always give the same result.</summary>
        public static void RollAll(Transform root)
        {
            foreach (var item in root.GetComponentsInChildren<SpawnChance>(true))
                item.SetPresent(item.Rolls());
        }

        /// <summary>Brings every <see cref="SpawnChance"/> item under <paramref name="root"/> back, for editing the arrangement.</summary>
        public static void ShowAll(Transform root)
        {
            foreach (var item in root.GetComponentsInChildren<SpawnChance>(true))
                item.SetPresent(true);
        }

        private bool Rolls()
        {
            var rng = new SettlementPlacementUtil.SeededRng(SettlementPlacementUtil.SeedFromPosition(transform.position));
            return rng.NextChance(chance);
        }

        private void SetPresent(bool present)
        {
            if (gameObject.activeSelf == present) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.Undo.RecordObject(gameObject, "Roll Settlement Contents");
#endif
            gameObject.SetActive(present);
        }
    }
}
