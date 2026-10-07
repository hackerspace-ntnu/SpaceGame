using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using SpaceGame.Persistence;

namespace SpaceGame.Items
{
    /// <summary>
    /// The per-instance state of one item, in a form that outlives the object holding it.
    ///
    /// <para>
    /// <b>The gap this closes.</b> <see cref="EquipItemSocket"/> instantiates a fresh copy of the
    /// item prefab on every equip and destroys it on every unequip, and an
    /// <see cref="InventorySlot"/> held nothing but an index and an <see cref="InventoryItem"/>
    /// asset. So a half-empty magazine, a spent charge and a running cooldown were all lost by
    /// switching hotbar slot and back — not only by saving. Persistence could not be added on top of
    /// that, because there was nowhere for the state to live between the two instances.
    /// </para>
    /// <para>
    /// <b>Why a string bag rather than a struct per item.</b> This travels through the save file as
    /// one dictionary per slot, so the shape has to be open — a weapon stores ammo, the grapple
    /// stores a hook point, the scanner stores a cooldown, and none of them may need a change to the
    /// inventory or to the saver to do it. Strings also keep this assembly out of JSON entirely, and
    /// sidestep the <c>Vector3</c>/<c>Quaternion</c> serialization trap that bites anything reading
    /// a save payload by hand: a vector is written here as three invariant-culture numbers and read
    /// back the same way.
    /// </para>
    /// <para>
    /// Missing keys are normal — a bag written by an older build, or by an item that had nothing to
    /// say — so every read takes the value to fall back to.
    /// </para>
    /// </summary>
    public sealed class ItemState
    {
        private readonly Dictionary<string, string> values;

        public ItemState() => values = new Dictionary<string, string>();

        public ItemState(IReadOnlyDictionary<string, string> source)
        {
            values = new Dictionary<string, string>();
            if (source == null) return;

            foreach (KeyValuePair<string, string> entry in source)
                if (!string.IsNullOrEmpty(entry.Key)) values[entry.Key] = entry.Value;
        }

        /// <summary>Nothing worth keeping. A bag that answers true is stored as no bag at all.</summary>
        public bool IsEmpty => values.Count == 0;

        /// <summary>The raw pairs, for a saver to write out. Read-only by contract.</summary>
        public IReadOnlyDictionary<string, string> Raw => values;

        /// <summary>
        /// A detached copy, for handing to a serializer.
        ///
        /// A copy rather than the live dictionary because the bag stays in the slot and keeps being
        /// written to while the save is assembled.
        /// </summary>
        public Dictionary<string, string> Copy() => new(values);

        public bool Has(string key) => !string.IsNullOrEmpty(key) && values.ContainsKey(key);

        // ── Writing ────────────────────────────────────────────────────────────

        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            values[key] = value;
        }

        public void Set(string key, int value) =>
            Set(key, value.ToString(CultureInfo.InvariantCulture));

        public void Set(string key, bool value) => Set(key, value ? "1" : "0");

        public void Set(string key, float value) =>
            Set(key, value.ToString("R", CultureInfo.InvariantCulture));

        public void Set(string key, Vector3 value) =>
            Set(key, string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R}",
                                   value.x, value.y, value.z));

        /// <summary>
        /// Writes a reference to another saved object. An unset reference writes nothing, so a
        /// bag never carries a key meaning "no referent" — absent already means that.
        /// </summary>
        public void Set(string key, SaveRef value)
        {
            if (!value.IsSet) return;
            Set(key, value.Kind + "|" + value.Id);
        }

        // ── Reading ────────────────────────────────────────────────────────────

        public string GetString(string key, string fallback = null) =>
            !string.IsNullOrEmpty(key) && values.TryGetValue(key, out string raw) ? raw : fallback;

        public int GetInt(string key, int fallback = 0) =>
            int.TryParse(GetString(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                ? v
                : fallback;

        public bool GetBool(string key, bool fallback = false)
        {
            string raw = GetString(key);
            return string.IsNullOrEmpty(raw) ? fallback : raw == "1";
        }

        public float GetFloat(string key, float fallback = 0f) =>
            float.TryParse(GetString(key), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
                ? v
                : fallback;

        public Vector3 GetVector3(string key, Vector3 fallback = default)
        {
            string raw = GetString(key);
            if (string.IsNullOrEmpty(raw)) return fallback;

            string[] parts = raw.Split(',');
            if (parts.Length != 3) return fallback;

            return float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                   float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                   float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
                ? new Vector3(x, y, z)
                : fallback;
        }

        public SaveRef GetRef(string key)
        {
            string raw = GetString(key);
            if (string.IsNullOrEmpty(raw)) return SaveRef.None;

            int split = raw.IndexOf('|');
            if (split <= 0 || split == raw.Length - 1) return SaveRef.None;

            return new SaveRef { Kind = raw[..split], Id = raw[(split + 1)..] };
        }
    }

    /// <summary>
    /// Implemented by a held item with state worth keeping across an equip — and therefore across a
    /// save.
    ///
    /// <para>
    /// <see cref="UsableItem"/> implements it for every item there is (it owns the charge count), so
    /// a subclass overrides and calls base rather than starting from nothing. The bag is handed in
    /// rather than returned so that adding one more field to an item is one more line, with no
    /// struct to define and no saver to edit.
    /// </para>
    /// <para>
    /// Both halves run on the item's OWN instance, which is why they can read and write private
    /// fields directly and why almost none of these items need a public restore API.
    /// </para>
    /// </summary>
    public interface IItemStateCarrier
    {
        /// <summary>Write whatever this instance would otherwise lose when it is destroyed.</summary>
        void CaptureItemState(ItemState state);

        /// <summary>
        /// Apply a previously captured bag. Called after <c>OnEquipped</c>, so it wins over anything
        /// the component set up for itself on <c>Awake</c>/<c>OnEnable</c>/equip.
        /// </summary>
        void RestoreItemState(ItemState state);
    }

    /// <summary>
    /// A restored item whose state names something else in the world — a lassoed creature, a
    /// grapple anchor, a deployed craft.
    ///
    /// <para>
    /// The referent does not exist when <see cref="IItemStateCarrier.RestoreItemState"/> runs: the
    /// hotbar is rebuilt as part of the player's own record, which is applied before the deferred
    /// pass that everything else in the save system resolves references in. So the item stashes
    /// what it read and is asked again, from <c>PlayerInventorySaveable</c>'s deferred pass, once
    /// the world is there.
    /// </para>
    /// </summary>
    public interface IItemDeferredRestore
    {
        /// <summary>True while this item is still holding restored state it could not apply.</summary>
        bool HasPendingRestore { get; }

        /// <summary>
        /// Try to finish. Runs more than once — once per player bind and once per late chunk — so it
        /// must be idempotent, and it must keep its pending state when the referent is not here yet.
        /// </summary>
        void TryCompleteRestore();
    }
}
