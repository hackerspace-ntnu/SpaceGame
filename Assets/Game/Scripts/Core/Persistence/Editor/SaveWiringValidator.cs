using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Agents;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence.EditorTools
{
    /// <summary>
    /// Checks the save system's wiring and reports what would fail silently at runtime.
    ///
    /// Every fault here shares a shape: the game runs, nothing throws, and something the player did
    /// is simply not there next session. A crate placed in a chunk scene without a
    /// <see cref="SaveableEntity"/> is never saved and never complains. A restored prefab missing
    /// from NetworkManager's list exists on the host alone. Two objects sharing an instance id
    /// fight over one record and the loser is whichever loaded second.
    ///
    /// None of these can be caught by a compiler or a unit test, because all of them are asset and
    /// scene wiring. This is where they get caught instead: Tools ▸ Save System ▸ Validate.
    /// </summary>
    public static class SaveWiringValidator
    {
        private class Report
        {
            public readonly List<string> Errors = new();
            public readonly List<string> Warnings = new();
            public int Checked;
        }

        [MenuItem("Tools/Save System/Validate Save Wiring")]
        public static void Validate()
        {
            var report = new Report();

            ValidateSaveablePrefabs(report);
            ValidateStampedOnDisk(report);
            ValidatePersistentEntityPrefabs(report);
            ValidateItemPrefabs(report);
            ValidateNetworkPrefabRegistration(report);
            ValidateOpenScenes(report);
            ValidateSaveKeys(report);

            var summary = new StringBuilder();
            summary.AppendLine($"[Save] Validation checked {report.Checked} object(s): " +
                               $"{report.Errors.Count} error(s), {report.Warnings.Count} warning(s).");

            foreach (string warning in report.Warnings) summary.AppendLine("  WARN  " + warning);
            foreach (string error in report.Errors) summary.AppendLine("  ERROR " + error);

            if (report.Errors.Count > 0) Debug.LogError(summary.ToString());
            else if (report.Warnings.Count > 0) Debug.LogWarning(summary.ToString());
            else Debug.Log(summary + "  Everything the save system needs is wired.");
        }

        /// <summary>Prefabs the store can be asked to instantiate must have an identity to be found by.</summary>
        private static void ValidateSaveablePrefabs(Report report)
        {
            foreach (GameObject prefab in Resources.LoadAll<GameObject>(SaveablePrefabRegistry.ResourcesFolder))
            {
                report.Checked++;

                var entity = prefab.GetComponent<SaveableEntity>();

                if (entity == null)
                {
                    report.Errors.Add($"'{prefab.name}' is in Resources/{SaveablePrefabRegistry.ResourcesFolder} " +
                                      "but has no SaveableEntity, so it can never be restored.");
                    continue;
                }

                if (string.IsNullOrEmpty(entity.PrefabId))
                {
                    report.Errors.Add($"'{prefab.name}' has a SaveableEntity with no prefab id. " +
                                      "Re-import it so OnValidate can stamp one.");
                }

                // Invalidate first: the saver list is cached on the component, and a prefab asset
                // lives in memory across the whole editor session — so a validation run after the
                // wiring tool added savers would otherwise still be reading the list from before it.
                entity.InvalidateSavers();

                if (entity.Savers().Count == 0)
                {
                    report.Warnings.Add($"'{prefab.name}' is saveable but has no ISaveable components, " +
                                        "so only its position and rotation will persist.");
                }
            }
        }

        /// <summary>
        /// Every saveable prefab must carry its <c>prefabId</c> ON DISK, not merely in memory.
        ///
        /// <b>Reading it through the component cannot detect this, which is why it went unnoticed.</b>
        /// <c>SaveableEntity.OnValidate</c> is inside <c>#if UNITY_EDITOR</c> and stamps the field
        /// the moment the asset is loaded, so in the editor <c>entity.PrefabId</c> always looks
        /// right — every runtime spawn inherits the in-memory value and works, and every check that
        /// asks the object agrees. Nothing wrote it to the file, and in a player build
        /// <c>OnValidate</c> never runs, so the value the game actually ships with is the empty one.
        ///
        /// Two of <see cref="SaveablePrefabRegistry"/>'s three lookup routes key on that field, so
        /// in a build they registered nothing and every runtime-spawned world object was captured
        /// into the save and dropped on load. The only way to see it is to read the YAML.
        ///
        /// The second check catches the other half: a prefab whose stamped id is not its own GUID.
        /// That happens to any prefab that is a VARIANT of an imported model, because the old
        /// derivation walked the variant chain to its root and found the <c>.fbx</c>.
        /// </summary>
        private static void ValidateStampedOnDisk(Report report)
        {
            // Every prefab in the project, not just the two populations SaveablePrefabsInProject
            // knows about: the point is to find prefabs that carry an identity and cannot use it.
            foreach (string assetGuid in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(assetGuid);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/Game/", System.StringComparison.Ordinal))
                    continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                var entity = prefab.GetComponent<SaveableEntity>();
                if (entity == null) continue;

                // The player is SaveScope.External: the world store never instantiates it, so it has
                // no use for a prefab id and is deliberately left blank.
                if (entity.Scope == SaveScope.External) continue;

                string guid = assetGuid;
                string onDisk = SaveablePrefabFile.ReadPrefabId(path);

                if (string.IsNullOrEmpty(onDisk))
                {
                    report.Errors.Add(
                        $"'{prefab.name}' has no prefabId in its FILE ({path}). It looks correct in " +
                        "the editor because OnValidate stamps it in memory, but a build ships the " +
                        "empty value — so anything spawned from it at runtime is saved and then " +
                        "cannot be restored. Run Tools ▸ Save System ▸ Wire Saveable Prefabs.");
                    continue;
                }

                if (onDisk != guid)
                {
                    report.Errors.Add(
                        $"'{prefab.name}' is stamped with prefabId '{onDisk}' but its own asset GUID " +
                        $"is '{guid}' ({path}). A prefab that is a variant of an imported model used " +
                        "to be stamped with the model's GUID, which nothing can instantiate. Re-run " +
                        "Wire Saveable Prefabs.");
                }
            }
        }


        /// <summary>
        /// Every agent, mount and vehicle prefab must carry a baked identity.
        ///
        /// <b>This is the check that would have caught the bug this whole system was rebuilt around.</b>
        /// The Ostrich, the DuneFoil, the DesertCrawler and the RigWalker all sat in
        /// <c>persistentScene</c> with no <see cref="SaveableEntity"/> and no complaint from anything —
        /// the game ran, saves were written, and the mounts and vehicles simply were not in them.
        ///
        /// A missing identity is only a WARNING rather than an error, because the runtime pass in
        /// <see cref="SaveablePolicy.EnsureScene"/> does wire these on load. But it wires them with an
        /// identity derived from the hierarchy path, which is orphaned the moment anyone renames or
        /// re-parents the object — so an unwired prefab is a save that works until it quietly stops.
        /// Run <c>Wire Saveable Prefabs</c> and it becomes a baked GUID.
        /// </summary>
        private static void ValidatePersistentEntityPrefabs(Report report)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/Game/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                if (prefab == null) continue;
                if (prefab.GetComponent<IPersistentEntity>() == null) continue;

                report.Checked++;

                // The player is owned by PlayerSaveService and deliberately carries no world identity.
                if (!SaveablePolicy.NeedsSaving(prefab, out _)) continue;

                if (prefab.GetComponent<SaveableEntity>() == null)
                {
                    report.Warnings.Add($"'{prefab.name}' is a world entity ({path}) with no SaveableEntity. " +
                                        "It will be wired at runtime with a path-derived identity, which " +
                                        "breaks if the object is ever renamed or re-parented. Run " +
                                        "Tools ▸ Save System ▸ Wire Saveable Prefabs.");
                    continue;
                }

                // A mount with no MountSaveable saves its own position but forgets its rider, which is
                // the single most visible thing a mount can forget.
                if (prefab.GetComponent<MountModule>() != null &&
                    prefab.GetComponent<MountSaveable>() == null)
                {
                    report.Warnings.Add($"'{prefab.name}' is a mount with no MountSaveable, so a player " +
                                        "riding it at save time comes back dismounted.");
                }

                if (prefab.GetComponent<AgentTargeting>() != null &&
                    prefab.GetComponent<AgentStateSaveable>() == null)
                {
                    report.Warnings.Add($"'{prefab.name}' can fight but has no AgentStateSaveable, so it " +
                                        "forgets its target and forgets it was in a fight.");
                }
            }
        }

        /// <summary>
        /// Dropped items are restored through the item registry, so an item whose prefab is missing
        /// leaves a record that can never be turned back into an object.
        /// </summary>
        private static void ValidateItemPrefabs(Report report)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(InventoryItem)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(path);
                if (item == null) continue;

                report.Checked++;

                if (string.IsNullOrEmpty(item.ID))
                {
                    report.Errors.Add($"Item '{item.name}' has no ID. Re-import it so OnValidate can " +
                                      "stamp its GUID — without one it cannot be saved in a hotbar.");
                }
                else if (item.ID != guid)
                {
                    report.Errors.Add($"Item '{item.name}' has ID '{item.ID}' but its asset GUID is " +
                                      $"'{guid}'. Saves written now will not resolve after a re-import.");
                }

                if (item.itemPrefab == null)
                {
                    report.Warnings.Add($"Item '{item.name}' has no itemPrefab, so dropping it saves " +
                                        "a record nothing can restore.");
                }
            }
        }

        /// <summary>
        /// A restored object carrying a NetworkObject has to be network-spawned, and Netcode refuses
        /// to spawn a prefab it does not know. The failure is host-only visibility: the object is
        /// there for whoever is hosting and for nobody else.
        /// </summary>
        private static void ValidateNetworkPrefabRegistration(Report report)
        {
            NetworkManager manager = FindNetworkManagerAsset();
            if (manager == null)
            {
                report.Warnings.Add("No NetworkManager prefab found, so network-prefab registration " +
                                    "could not be checked.");
                return;
            }

            HashSet<GameObject> registered = CollectRegisteredPrefabs(manager);

            if (registered.Count == 0)
            {
                // Better to say nothing than to accuse every prefab in the project. An empty result
                // almost certainly means the registration moved somewhere this method does not read
                // yet, not that nothing is registered.
                report.Warnings.Add("NetworkManager reports no registered prefabs at all, so this " +
                                    "check was skipped rather than flagging every saveable prefab.");
                return;
            }

            foreach (GameObject prefab in SaveablePrefabsInProject().Distinct())
            {
                if (prefab.GetComponent<NetworkObject>() == null) continue;
                if (registered.Contains(prefab)) continue;

                report.Errors.Add($"'{prefab.name}' is saveable and networked but is not in " +
                                  "NetworkManager's prefab list. Restoring it would spawn it on the host only.");
            }
        }

        private static IEnumerable<GameObject> SaveablePrefabsInProject()
        {
            foreach (GameObject prefab in Resources.LoadAll<GameObject>(SaveablePrefabRegistry.ResourcesFolder))
                yield return prefab;

            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(InventoryItem)))
            {
                var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(AssetDatabase.GUIDToAssetPath(guid));
                if (item != null && item.itemPrefab != null) yield return item.itemPrefab;
            }
        }

        private static NetworkManager FindNetworkManagerAsset()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Contains("NetworkManager")) continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var manager = prefab != null ? prefab.GetComponent<NetworkManager>() : null;
                if (manager != null) return manager;
            }

            return null;
        }

        /// <summary>
        /// Every prefab Netcode knows about, from BOTH places it can be told.
        ///
        /// The inline <c>Prefabs.Prefabs</c> list is the obvious one and, in this project, is empty:
        /// all 22 entries live in a <c>NetworkPrefabsList</c> asset (`DefaultNetworkPrefabs`)
        /// referenced from the config. Reading only the inline list made this check report every
        /// droppable item in the game as unregistered — seventeen confident, wrong errors, which is
        /// worse than not checking at all.
        /// </summary>
        private static HashSet<GameObject> CollectRegisteredPrefabs(NetworkManager manager)
        {
            var registered = new HashSet<GameObject>();

            var config = manager.NetworkConfig;
            if (config?.Prefabs == null) return registered;

            if (config.Prefabs.Prefabs != null)
            {
                foreach (var entry in config.Prefabs.Prefabs)
                    if (entry?.Prefab != null) registered.Add(entry.Prefab);
            }

            if (config.Prefabs.NetworkPrefabsLists != null)
            {
                foreach (var list in config.Prefabs.NetworkPrefabsLists)
                {
                    if (list?.PrefabList == null) continue;

                    foreach (var entry in list.PrefabList)
                        if (entry?.Prefab != null) registered.Add(entry.Prefab);
                }
            }

            return registered;
        }

        /// <summary>
        /// Only the scenes currently open. Loading all 240 chunk scenes to check them would take
        /// minutes and is exactly the kind of tool nobody runs; checking what is in front of you is
        /// the version that gets used.
        /// </summary>
        private static void ValidateOpenScenes(Report report)
        {
            var byInstanceId = new Dictionary<string, SaveableEntity>();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded) continue;

                foreach (SaveableEntity entity in WorldSaveStore.EntitiesIn(scene))
                {
                    report.Checked++;

                    if (string.IsNullOrEmpty(entity.InstanceId))
                    {
                        report.Errors.Add($"'{entity.name}' in '{scene.name}' has no instance id, " +
                                          "so its saved state can never be matched back to it.");
                        continue;
                    }

                    if (byInstanceId.TryGetValue(entity.InstanceId, out SaveableEntity other))
                    {
                        report.Errors.Add($"'{entity.name}' and '{other.name}' share instance id " +
                                          $"{entity.InstanceId}. They will fight over one save record.");
                        continue;
                    }

                    byInstanceId[entity.InstanceId] = entity;

                    if (entity.IsAuthored) continue;

                    // A scene-placed object that is not marked authored would be re-instantiated
                    // from its prefab on load, on top of the copy the scene file already provides.
                    if (!string.IsNullOrEmpty(scene.path))
                    {
                        report.Warnings.Add($"'{entity.name}' sits in scene '{scene.name}' but is not " +
                                            "marked authored. Re-save the scene so OnValidate can mark it, " +
                                            "or it will be duplicated on load.");
                    }
                }
            }
        }

        /// <summary>
        /// Two savers on one entity sharing a key means one silently overwrites the other, and which
        /// one wins depends on component order.
        /// </summary>
        private static void ValidateSaveKeys(Report report)
        {
            foreach (GameObject prefab in SaveablePrefabsInProject().Distinct())
            {
                var entity = prefab.GetComponent<SaveableEntity>();
                if (entity == null) continue;

                var seen = new HashSet<string>();

                foreach (ISaveable saver in entity.Savers())
                {
                    if (saver == null) continue;

                    if (string.IsNullOrEmpty(saver.SaveKey))
                    {
                        report.Errors.Add($"A saver on '{prefab.name}' has an empty SaveKey and will " +
                                          "never be written.");
                        continue;
                    }

                    if (!seen.Add(saver.SaveKey))
                    {
                        report.Errors.Add($"'{prefab.name}' has two savers using the key " +
                                          $"'{saver.SaveKey}'. One will overwrite the other.");
                    }
                }
            }
        }
    }
}
