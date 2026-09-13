using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SpaceGame.Items;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>
    /// Every item a shipped container starts with must be one the item registry can hand back.
    ///
    /// <para>
    /// A container holds its starting items as DIRECT references, so it can display and hand over
    /// an asset from anywhere — <c>PackContainer.ItemFor</c> answers from its own cache before it
    /// ever asks the registry. The hotbar has no such cache: it stores nothing but the item's
    /// <c>ID</c> and resolves it through <c>Registry&lt;InventoryItem&gt;.Get</c>, which only knows
    /// assets under <c>Assets/Game/Resources/Items</c>. An item authored from anywhere else rides
    /// that seam exactly once — the first take into the hotbar — and vanishes: off the mat, into a
    /// slot that resolves to null, gone from both. That is how the ExpeditionRig's whole starting
    /// roster, authored against the dead duplicates in <c>Assets/Game/ScriptableObjects/Items</c>,
    /// was silently lost one take at a time.
    /// </para>
    /// </summary>
    public class PackStartingItemTests
    {
        private const string RegistryRoot = "Assets/Game/Resources/Items";

        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void EveryAuthoredStartingItemResolvesThroughTheRegistry()
        {
            var offenders = new List<string>();

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Prefabs" });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                foreach (PackContainer container in prefab.GetComponentsInChildren<PackContainer>(true))
                {
                    Check(container, "startingStrapItems", path, offenders);
                    Check(container, "startingMainItems", path, offenders);
                }
            }

            Assert.That(offenders, Is.Empty,
                        "These starting items live outside " + RegistryRoot + ", so the registry " +
                        "cannot resolve their IDs and the first take into a hotbar loses them. " +
                        "Repoint each reference at the copy under " + RegistryRoot + ".\n  " +
                        string.Join("\n  ", offenders));
        }

        private static void Check(PackContainer container, string fieldName, string prefabPath,
                                  List<string> offenders)
        {
            var field = typeof(PackContainer).GetField(fieldName, Hidden);
            Assert.That(field, Is.Not.Null,
                        $"PackContainer no longer has a '{fieldName}' — update this test to sweep "
                        + "whatever replaced it, because the seam it guards has not gone anywhere.");

            var items = (List<InventoryItem>)field.GetValue(container);
            if (items == null) return;

            foreach (InventoryItem item in items)
            {
                if (item == null) continue;

                string itemPath = AssetDatabase.GetAssetPath(item);

                if (!itemPath.StartsWith(RegistryRoot))
                    offenders.Add($"{prefabPath} ({container.GetType().Name}.{fieldName}) -> {itemPath}");
            }
        }
    }
}
