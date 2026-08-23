// Place on the weapon mount bone (e.g. Arm2.R).
// Activates the correct weapon model based on which combat module the agent has.
using UnityEngine;

namespace SpaceGame.Agents
{
    public class WeaponSelector : MonoBehaviour
    {
        [SerializeField] private GameObject meleeWeapon;
        [SerializeField] private GameObject rangedWeapon;

        private void Awake() => Refresh();

        /// <summary>
        /// Re-derive which weapon model is showing from which combat modules are currently active.
        ///
        /// <para>
        /// Split out of Awake because the answer is not fixed at spawn. Which combat modules are
        /// enabled is itself runtime state — <c>HealthReactionModule</c> switches them at health
        /// thresholds — so an agent that was enraged into melee reloads with its Awake-time answer
        /// (from the prefab's module states) and shows the wrong weapon in its hand until something
        /// happens to disable one again. Nothing about this component is saved; it is a projection
        /// of state that is, and this is the call that re-projects it.
        /// </para>
        /// </summary>
        public void Refresh()
        {
            bool hasMelee = HasActiveModule<CloseCombatModule>();
            bool hasRanged = HasActiveModule<AgentRangedCombatModule>();

            if (meleeWeapon) meleeWeapon.SetActive(hasMelee && !hasRanged);
            if (rangedWeapon) rangedWeapon.SetActive(hasRanged);
        }

        /// <summary>
        /// Re-derive every selector under <paramref name="root"/>. The selectors sit on hand bones
        /// deep in the rig while the state they read lives on the entity root, so a restore that has
        /// just changed a module's enabled flag has no reference to the selectors it invalidated.
        /// </summary>
        public static void RefreshAll(GameObject root)
        {
            if (root == null) return;

            foreach (WeaponSelector selector in root.GetComponentsInChildren<WeaponSelector>(true))
                selector.Refresh();
        }

        private bool HasActiveModule<T>() where T : BehaviourModuleBase
        {
            foreach (T module in GetComponentsInParent<T>(true))
                if (module.IsActive)
                    return true;

            return false;
        }
    }
}
