using UnityEngine;
using SpaceGame.Core;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SpaceGame.Items
{
    /// <summary>
    /// ScriptableObject representing an item that can be stored in the inventory. Contains data about the item such as its name, prefab, and icon.
    /// </summary>
    [CreateAssetMenu(menuName = "Items/Item")]
    public class InventoryItem : ScriptableObject, IRegistryEntry
    {
        /// <summary>
        /// The asset's GUID, assigned by <see cref="OnValidate"/>. Save files store this rather than
        /// a name or a list index, so it must stay stable — it is what lets an item asset be renamed
        /// or moved without emptying that slot in every existing save.
        ///
        /// <para>
        /// <b><c>[field: SerializeField]</c> is what makes this exist outside the editor at all.</b>
        /// Unity does not serialize an auto-property's compiler-generated backing field unless it is
        /// asked to, and <see cref="OnValidate"/> is editor-only — so without the attribute the
        /// value was recomputed on every import and never written to the asset. In the editor that
        /// is invisible, because the recompute always ran. In a built player nothing assigns it and
        /// every item ships with a null id: <c>RegistryLoader</c> then calls
        /// <c>Registry.Register</c>, which indexes a dictionary by that null and throws
        /// <see cref="System.ArgumentNullException"/> on the first item, leaving the game with no
        /// item registry at all.
        /// </para>
        ///
        /// <para>
        /// That failure is editor-invisible and build-only, which is the worst combination this
        /// project has: real multiplayer means built players, so the one configuration that could
        /// not work is the one every session actually runs in.
        /// </para>
        /// </summary>
        [field: SerializeField]
        public string ID { get; set; }

        [Tooltip("Display name of the item")]
        public string itemName = "NewItem";

        [Tooltip("Prefab that will be instantiated and equipped when this item is selected.")]
        public GameObject itemPrefab;

        [Tooltip("Optional icon for UI display.")]
        public Sprite icon;

        [Tooltip("Optional prefab to render the icon from instead of itemPrefab, for items whose "
            + "held form is not what the player thinks of as the item — e.g. the Wing Pack's icon "
            + "shows the unfurled ornithopter, not the furled pack in the hand.")]
        public GameObject iconPrefab;

#if UNITY_EDITOR
        private void OnValidate()
        {
            string path = AssetDatabase.GetAssetPath(this);
            string guid = AssetDatabase.AssetPathToGUID(path);

            if (ID != guid)
            {
                ID = guid;
                EditorUtility.SetDirty(this);
            }
        }
#endif
    }
}
