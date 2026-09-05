using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>What a reservoir holds. A receptacle refuses the kind it was not built for.</summary>
    /// <remarks>
    /// Values are persisted and sent as a byte. DO NOT renumber; new kinds are APPENDED.
    /// </remarks>
    public enum SupplyKind : byte
    {
        Oxygen = 0,
        Power = 1,
    }

    /// <summary>
    /// The one definition of "how full is this supply item" — the state key it is saved under, the
    /// byte it is sent as, and where its capacity comes from.
    ///
    /// <para>
    /// <b>Why this exists at all.</b> Until 2026-09-04 a bottle's charge was its item IDENTITY: a
    /// full bottle and an empty one were two <see cref="InventoryItem"/> assets. That was the right
    /// answer while the only question was "full or not", because an id replicates, saves, stows and
    /// draws its own icon for free. It cannot express 43%, and it cannot express it in a way that
    /// scales — a tank readable to a percent would need a hundred assets, and a second tank type
    /// would need a hundred more.
    /// </para>
    /// <para>
    /// <b>The value is a FRACTION, never a quantity.</b> Capacity lives on the item's prefab
    /// (<see cref="DockableSupply.Capacity"/>) and the fraction lives on the instance. That is what
    /// makes a new tank type free: a fifteen-minute tank is a prefab with a different capacity, and
    /// every saved fraction in every existing world still means what it meant. Storing seconds
    /// instead would make each saved number depend on a capacity that is authored and can change,
    /// which is a silent rebalance of every save on disk.
    /// </para>
    /// <para>
    /// <b>A fraction also fits in one byte</b> (<see cref="ToByte"/>, ~0.4% resolution — finer than
    /// any readout that shows whole percent). That is what let both container wire formats carry a
    /// charge for one byte per item rather than four, and it is why the quantisation lives here
    /// rather than being written out twice.
    /// </para>
    /// </summary>
    public static class SupplyCharge
    {
        /// <summary>
        /// State key for the charge fraction. Written into save files — never rename.
        /// </summary>
        public const string StateKey = "supply.charge";

        /// <summary>
        /// What a container stores for an item that carries no charge at all. Distinct from 0,
        /// which is a real reading meaning "empty": a rifle is not an empty tank.
        /// </summary>
        public const float None = -1f;

        /// <summary>
        /// Capacity per item prefab. Resolved once — it is a
        /// <c>GetComponent</c> on a prefab, and the pack asks for it once per item per redraw.
        /// </summary>
        private static readonly Dictionary<InventoryItem, DockableSupply> Prefabs = new();

        /// <summary>The supply component on an item's prefab, or null if it is not a supply.</summary>
        public static DockableSupply Of(InventoryItem item)
        {
            if (item == null) return null;

            if (Prefabs.TryGetValue(item, out DockableSupply cached)) return cached;

            DockableSupply found = item.itemPrefab != null
                ? item.itemPrefab.GetComponent<DockableSupply>()
                : null;

            Prefabs[item] = found;
            return found;
        }

        /// <summary>Does this item hold a charge worth carrying between containers?</summary>
        public static bool Carries(InventoryItem item) => Of(item) != null;

        /// <summary>
        /// Does this item hold <paramref name="kind"/>? False for anything that is not a supply,
        /// which is what lets a receptacle refuse a rifle and an oxygen bottle with one question.
        /// </summary>
        public static bool Holds(InventoryItem item, SupplyKind kind)
        {
            DockableSupply supply = Of(item);
            return supply != null && supply.Kind == kind;
        }

        /// <summary>
        /// This item's full capacity in its own unit — seconds of air, or watt-hours.
        /// Zero for anything that is not a supply.
        /// </summary>
        public static float CapacityOf(InventoryItem item)
        {
            DockableSupply supply = Of(item);
            return supply != null ? supply.Capacity : 0f;
        }

        /// <summary>
        /// What a freshly created one of these holds, as a fraction. Authored per prefab, so a
        /// battery can enter the world full and a tank empty.
        /// </summary>
        public static float StartingChargeOf(InventoryItem item)
        {
            DockableSupply supply = Of(item);
            return supply != null ? supply.StartingCharge : None;
        }

        // ── The wire ─────────────────────────────────────────────────────────────

        /// <summary>
        /// A fraction as one byte. <see cref="None"/> and anything below zero become 0 — a wire
        /// form has no room for "not a supply", and the receiving end already knows from the item
        /// id whether the number means anything.
        /// </summary>
        public static byte ToByte(float charge01) =>
            (byte)Mathf.RoundToInt(Mathf.Clamp01(charge01) * 255f);

        public static float FromByte(byte raw) => raw / 255f;

        // ── The bag ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The charge in a state bag, or <see cref="None"/> when it holds none — which is the
        /// ordinary case for every item that is not a supply, and for a bag written before this
        /// system existed.
        /// </summary>
        public static float Read(ItemState state) =>
            state != null && state.Has(StateKey) ? state.GetFloat(StateKey, None) : None;

        /// <summary>
        /// Put a charge in a bag. A <see cref="None"/> value writes nothing rather than writing
        /// "-1", so a bag never carries a key meaning "no charge" — absent already means that.
        /// </summary>
        public static void Write(ItemState state, float charge01)
        {
            if (state == null) return;

            if (charge01 < 0f) return;
            state.Set(StateKey, Mathf.Clamp01(charge01));
        }

        // ── What the player reads ────────────────────────────────────────────────

        /// <summary>
        /// A charge as the player sees it, everywhere: whole percent, no decimals, no unit.
        ///
        /// <para>
        /// Percent rather than the item's own unit is a deliberate choice and a slightly lossy one
        /// — a 15-minute tank at 100% reads the same as a 30-minute one — but it is the same
        /// number on the visor, on the item's own gauge, in the reticle's info box and on the
        /// machine's readout, and one number in four places beats four correct-but-different ones.
        /// </para>
        /// </summary>
        public static string Describe(float charge01) =>
            Mathf.RoundToInt(Mathf.Clamp01(charge01) * 100f).ToString(CultureInfo.InvariantCulture) + "%";
    }
}
