using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists which weapon an agent has drawn.
    ///
    /// <b>Why a [SerializeField] needed saving at all.</b> <c>WeaponMount.activeIndex</c> is
    /// serialized, which reads as authoring and is in fact runtime state the moment anything calls
    /// <c>Equip</c> or <c>EquipNext</c> — those are reachable from a UnityEvent, from a script, and
    /// from any designer wiring that swaps an agent onto its second barrel mid-fight. The value lives
    /// on the instance, and a scene reload puts the prefab's back. So the agent reloads with the
    /// wrong model showing, firing the wrong <c>AgentWeaponDefinition</c> — wrong damage, wrong
    /// projectile speed, and therefore wrong lead prediction on every shot.
    ///
    /// <b>The model on the hand bone is a second, quieter copy of the same problem.</b>
    /// <c>WeaponSelector</c> decides which weapon mesh is visible in <c>Awake</c>, from which combat
    /// modules are enabled — and which modules are enabled is itself restored state (see
    /// <see cref="HealthReactionSaveable"/>). Its Awake therefore ran with the prefab's answer, before
    /// anything was restored. Nothing about the selector is saved; it is re-derived here, after the
    /// state it reads has landed.
    ///
    /// <b>Asked of the whole subtree.</b> A WeaponMount lives on a hand bone — that is how
    /// <c>AgentRangedCombatModule</c> finds it — while the saver belongs on the entity that owns it,
    /// the same split <see cref="ArticulatedPartsSaveable"/> makes.
    ///
    /// Not deferred: an index into this agent's own slots is self-contained.
    /// </summary>
    public class WeaponMountSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "weaponMount";

        private WeaponMount[] mounts;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private WeaponMount[] Mounts => mounts ??= GetComponentsInChildren<WeaponMount>(true);

        public string SaveKey => Key;

        public struct State
        {
            /// <summary>Positional: entry i is the i'th mount in hierarchy order.</summary>
            public int[] activeIndices;
        }

        public object CaptureState()
        {
            WeaponMount[] found = Mounts;
            if (found.Length == 0) return null;

            var indices = new int[found.Length];
            for (int i = 0; i < found.Length; i++)
                indices[i] = found[i].ActiveIndex;

            return new State { activeIndices = indices };
        }

        public void RestoreState(JObject state)
        {
            WeaponMount[] found = Mounts;
            if (found.Length == 0) return;

            // A record that says nothing means every mount was on its first slot, which is what
            // Equip(0) asserts. Not "leave whatever is there": a live mount already swapped by
            // something in the scene would otherwise keep a weapon the save denies.
            int[] indices = state == null
                ? null
                : state.ToObject<State>(SaveSerializer.Serializer).activeIndices;

            for (int i = 0; i < found.Length; i++)
            {
                int index = indices != null && i < indices.Length ? indices[i] : 0;

                // Clamped inside Equip, so a save naming a slot a since-shortened mount no longer has
                // lands on the last real one rather than throwing.
                found[i].RestoreActiveIndex(index);
            }

            WeaponSelector.RefreshAll(gameObject);
        }
    }
}
