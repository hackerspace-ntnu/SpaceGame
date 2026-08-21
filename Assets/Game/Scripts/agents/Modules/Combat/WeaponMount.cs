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

namespace SpaceGame.Agents
{
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

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// <para>
        /// `activeIndex` is a [SerializeField], which reads as authored data and is in fact runtime
        /// state the moment anything calls <see cref="Equip(int)"/> or <see cref="EquipNext"/>: the
        /// value lives on the INSTANCE, and a scene reload puts the prefab's back. So an NPC that
        /// swapped to its second weapon comes back holding its first, with the wrong model showing
        /// and the wrong <see cref="AgentWeaponDefinition"/> feeding damage and lead prediction.
        /// </para>
        /// <para>
        /// Routed through <see cref="Equip(int)"/> rather than assigning the field, so the model
        /// visibility and the OnWeaponChanged listeners are brought along — a restore that set the
        /// index quietly would leave the mount claiming one weapon and showing another.
        /// </para>
        /// </summary>
        public void RestoreActiveIndex(int index) => Equip(index);

        private void RefreshVisibility()
        {
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].model != null)
                    slots[i].model.SetActive(i == activeIndex);
        }
    }
}
