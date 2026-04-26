// Place on the weapon mount bone (e.g. Arm2.R).
// Activates the correct weapon model based on which combat module the agent has.
using UnityEngine;

public class WeaponSelector : MonoBehaviour
{
    [SerializeField] private GameObject meleeWeapon;
    [SerializeField] private GameObject rangedWeapon;

    private void Awake()
    {
        bool hasMelee = HasActiveModule<CloseCombatModule>();
        bool hasRanged = HasActiveModule<AgentRangedCombatModule>();

        if (meleeWeapon) meleeWeapon.SetActive(hasMelee && !hasRanged);
        if (rangedWeapon) rangedWeapon.SetActive(hasRanged);
    }

    private bool HasActiveModule<T>() where T : BehaviourModuleBase
    {
        foreach (T module in GetComponentsInParent<T>(true))
            if (module.IsActive)
                return true;

        return false;
    }
}
