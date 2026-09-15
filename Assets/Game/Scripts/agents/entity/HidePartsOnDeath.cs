// Take a piece of a creature off it when it dies, because the piece is now lying on the floor.
//
// The conjurer's staff is loot: EntityLootTable drops a real, pickup-able one where the creature
// fell. Without this the creature also keeps the one in its fist for the twelve seconds it takes
// to fade out, so the reward for the fight is two staffs, one of which is a lie.
//
// Deliberately about RENDERERS and not about the GameObject. The staff parts are bone-parented
// children of a rig that is still being animated and still being read for bone positions
// (ConjurerCastModule resolves StaffTip by name, and does so on every machine); disabling the
// objects themselves would take those transforms out from under anything that had found them.
// Hiding is the whole of what is wanted here, and it is all this does.
//
// Cosmetic, so it runs on EVERY machine with no authority check — the opposite of EntityLootTable
// beside it, which must run on one. It shares that class's other guard though: a save being loaded
// is not a kill, and the staff on a creature restored from a save has to come back with it.
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    public class HidePartsOnDeath : MonoBehaviour
    {
        [Tooltip("Names of child objects to hide on death, with everything under them. Matched " +
                 "over all descendants, so the path does not matter — which is what keeps this " +
                 "working when the rig is re-exported and the hierarchy shifts.")]
        [SerializeField] private string[] partNames;

        private HealthComponent health;

        private void Awake()
        {
            health = GetComponent<HealthComponent>();

            if (!health)
                Debug.LogWarning($"{name}: HidePartsOnDeath needs a HealthComponent.", this);
        }

        private void OnEnable()
        {
            if (health) health.OnDeath += HandleDeath;
        }

        private void OnDisable()
        {
            if (health) health.OnDeath -= HandleDeath;
        }

        private void HandleDeath()
        {
            // A save being loaded, not a kill. The loot from this death was dropped in the session
            // that caused it; a creature that comes back from a save comes back whole.
            if (health && health.IsRestoring) return;

            SetPartsVisible(false);
        }

        /// <summary>
        /// Show or hide the named parts. Public so a revive — or a test — can put them back.
        /// </summary>
        public void SetPartsVisible(bool visible)
        {
            if (partNames == null) return;

            foreach (string partName in partNames)
            {
                if (string.IsNullOrEmpty(partName)) continue;

                Transform part = FindDescendant(transform, partName);
                if (part == null) continue;

                foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = visible;
            }
        }

        /// Breadth-first by name over the whole subtree. Transform.Find only walks a literal path,
        /// and the parts sit deep inside an imported rig whose intermediate bones are not this
        /// component's business to know.
        private static Transform FindDescendant(Transform root, string childName)
        {
            if (root.name == childName) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDescendant(root.GetChild(i), childName);
                if (found != null) return found;
            }

            return null;
        }
    }
}
