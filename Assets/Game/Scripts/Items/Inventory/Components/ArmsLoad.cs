using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Something a player is holding in their arms instead of an item — a sprite-hamlet keeper
    /// carried out of the poison. While the arms are full the hands cannot hold anything else.
    ///
    /// <para>
    /// <b>What that means, and who enforces it.</b> <see cref="EquipmentController"/> asks this every
    /// frame: the equipped item is hidden (<c>BodyVeil</c>) and the Use button goes to whatever is in
    /// the arms instead of firing it — so a player carrying keepers cannot also fire the Oathbranch,
    /// which is the choice the witch fight is built on. The item comes back the moment the arms empty.
    /// </para>
    /// <para>
    /// <b>Derived on every machine.</b> Each holder takes and releases from replicated state of its
    /// own, so every machine hides the same item on the same body; only the owner's Use press does
    /// anything with it. Refcounted by holder, like <c>BodyVeil</c> — three keepers are three holders,
    /// and the arms are empty only when the last lets go.
    /// </para>
    /// </summary>
    public static class ArmsLoad
    {
        private sealed class Load
        {
            public readonly List<object> Holders = new();
            public readonly Dictionary<object, Action> OnUse = new();
        }

        private static readonly Dictionary<GameObject, Load> Loads = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Loads.Clear();

        /// <summary>Is anything in <paramref name="body"/>'s arms?</summary>
        public static bool IsFull(GameObject body) => body != null && Loads.ContainsKey(body);

        /// <summary>
        /// <paramref name="holder"/> is in <paramref name="body"/>'s arms. <paramref name="onUse"/> is
        /// what the owner's Use press does while it is — the most recent holder's answer wins.
        /// Idempotent per holder.
        /// </summary>
        public static void Take(GameObject body, object holder, Action onUse)
        {
            if (body == null || holder == null) return;

            if (!Loads.TryGetValue(body, out Load load))
            {
                load = new Load();
                Loads[body] = load;
            }

            if (!load.Holders.Contains(holder)) load.Holders.Add(holder);
            load.OnUse[holder] = onUse;
        }

        /// <summary><paramref name="holder"/> has left <paramref name="body"/>'s arms. Safe to repeat.</summary>
        public static void Release(GameObject body, object holder)
        {
            if (body == null || holder == null) return;
            if (!Loads.TryGetValue(body, out Load load)) return;

            load.Holders.Remove(holder);
            load.OnUse.Remove(holder);
            if (load.Holders.Count == 0) Loads.Remove(body);
        }

        /// <summary>
        /// The owner pressed Use with full arms. True when the press was spent on the load — the
        /// caller must not also fire the item.
        /// </summary>
        public static bool PressUse(GameObject body)
        {
            if (body == null || !Loads.TryGetValue(body, out Load load)) return false;

            object latest = load.Holders[load.Holders.Count - 1];
            if (load.OnUse.TryGetValue(latest, out Action onUse)) onUse?.Invoke();
            return true;
        }
    }
}
