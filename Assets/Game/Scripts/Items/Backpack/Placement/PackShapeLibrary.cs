using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The config asset: which cells of the pack's grid each item fills, drawn rather than typed.
    ///
    /// <para>
    /// One asset for the project, at <c>Assets/Game/ScriptableObjects/Items/PackShapes.asset</c>,
    /// beside <c>PackHolderLibrary.asset</c> — the two answer the same kind of question about the
    /// same roster (what holds an item, what space an item takes) and belong in the same drawer.
    /// <c>Tools/SpaceGame/Items/Create Pack Shape Library</c> creates it and fills it with every
    /// item's derived default; <c>PackShapeLibraryEditor</c> is the clickable grid that makes it
    /// worth having.
    /// </para>
    /// <para>
    /// <b>The lookup is partial, deliberately.</b> An item with no row here is not an error and is
    /// not unplaceable — it gets the block <see cref="PackShape.ForFootprint"/> derives from its
    /// true size. Authoring a shape is how you say "this one is not a rectangle", and the sixteen
    /// shipped items must not all have to be drawn before anything works.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "PackShapes", menuName = "Items/Pack Shape Library")]
    public sealed class PackShapeLibrary : ScriptableObject
    {
        /// <summary>One item's drawn shape.</summary>
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("The item this shape belongs to. A row with no item is ignored.")]
            public InventoryItem item;

            [Tooltip("Cells across, before rotation.")]
            [Min(1)] public int width = 1;

            [Tooltip("Cells up, before rotation.")]
            [Min(1)] public int height = 1;

            [Tooltip("Row-major, width * height. Every cell on means a plain rectangle. Drawn in " +
                     "the inspector rather than edited here.")]
            public bool[] cells = Array.Empty<bool>();

            [Tooltip("May the player turn this item on the mat? Off pins it to yaw 0 — for a " +
                     "shape whose art only reads one way up.")]
            public bool allowRotation = true;
        }

        [SerializeField] private List<Entry> entries = new();

        /// <summary>The rows, for the editor. Never null.</summary>
        public List<Entry> Entries => entries ??= new List<Entry>();

        /// <summary>
        /// Resolved shapes by item id. Rebuilt lazily and thrown away by
        /// <see cref="OnValidate"/>, so a shape edited in the inspector takes effect on the next
        /// layout change rather than on a domain reload.
        /// </summary>
        [NonSerialized] private Dictionary<string, Entry> byId;

        /// <summary>The authored row for an item, or null when nobody has drawn one.</summary>
        public Entry Find(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;

            if (byId == null)
            {
                byId = new Dictionary<string, Entry>();

                for (int i = 0; i < Entries.Count; i++)
                {
                    Entry row = Entries[i];

                    if (row?.item == null || string.IsNullOrEmpty(row.item.ID)) continue;

                    // First row wins. A duplicate is an authoring slip and the editor flags it;
                    // silently taking the last one would make which shape you get depend on where
                    // in the list somebody happened to append.
                    byId.TryAdd(row.item.ID, row);
                }
            }

            return byId.TryGetValue(itemId, out Entry found) ? found : null;
        }

        /// <summary>Forget the id index. Called by the editor after any change.</summary>
        public void Invalidate() => byId = null;

        private void OnValidate() => Invalidate();

        private void OnEnable() => Invalidate();
    }
}
