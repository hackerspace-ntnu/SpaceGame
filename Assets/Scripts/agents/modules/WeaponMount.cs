// Holds one or more weapon slots pre-placed in the prefab hierarchy.
// Switching weapons is instant — just toggles which child model is active.
// AgentRangedCombatModule reads ActiveMuzzle and ActiveDefinition from here
// instead of its own fields when a WeaponMount is present on the agent.
//
// Setup per slot:
//   model      — the weapon GameObject (child of the hand bone)
//   muzzle     — empty child at the barrel tip, used as the projectile spawn point
//   definition — AgentWeaponDefinition ScriptableObject (damage, projectile, audio)
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct WeaponSlot
{
    public string label;
    public GameObject model;
    public Transform muzzle;
    public AgentWeaponDefinition definition;
}

public class WeaponMount : MonoBehaviour
{
    [SerializeField] private List<WeaponSlot> slots = new();
    [SerializeField] private int activeIndex = 0;

    public event Action OnWeaponChanged;

    public int ActiveIndex => activeIndex;
    public int SlotCount => slots.Count;
    public AgentWeaponDefinition ActiveDefinition => slots.Count > 0 ? slots[activeIndex].definition : null;
    public Transform ActiveMuzzle => slots.Count > 0 ? slots[activeIndex].muzzle : null;

    private void Awake() => RefreshVisibility();

    public void Equip(int index)
    {
        if (slots.Count == 0) return;
        activeIndex = Mathf.Clamp(index, 0, slots.Count - 1);
        RefreshVisibility();
        OnWeaponChanged?.Invoke();
    }

    public void Equip(string label)
    {
        int idx = slots.FindIndex(s => s.label == label);
        if (idx >= 0) Equip(idx);
        else Debug.LogWarning($"{name}: WeaponMount has no slot labelled '{label}'.");
    }

    public void EquipNext() => Equip((activeIndex + 1) % Mathf.Max(1, slots.Count));
    public void EquipPrevious() => Equip((activeIndex - 1 + slots.Count) % Mathf.Max(1, slots.Count));

    private void RefreshVisibility()
    {
        for (int i = 0; i < slots.Count; i++)
            if (slots[i].model != null)
                slots[i].model.SetActive(i == activeIndex);
    }
}
