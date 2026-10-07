// The tests that read the FILE rather than the object.
//
// Everything else in this folder asks Unity what a prefab looks like. That is exactly the question
// that could not detect the worst bug this system has had, because SaveableEntity.OnValidate is
// inside `#if UNITY_EDITOR` and fills `prefabId` in memory the moment an asset is loaded. So in the
// editor the field always looked right — every runtime spawn inherited the in-memory value and
// worked, and every check that asked the component agreed with it.
//
// Nothing wrote it to disk. In a player build OnValidate never runs, so the value the game shipped
// with was the empty one, and two of SaveablePrefabRegistry's three lookup routes key on that field.
// They registered nothing at all. Every runtime-spawned world object was captured faithfully into
// the save and then dropped on load, and the console said "No prefab registered for id ''" on a
// machine no developer was looking at.
//
// The only way to see any of that from the editor is to read the serialized bytes. That is what
// these tests do, and it is why they are separate from PrefabPersistenceTests: a different oracle,
// not a different subject.
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.Core.Persistence.EditorTools;

namespace SpaceGame.EditorTools
{
    public class SaveWiringOnDiskTests
    {
        /// <summary>
        /// Every saveable prefab names itself in its own file.
        ///
        /// The regression test for the build-only data loss described at the top of this file. It
        /// deliberately does not consult <c>SaveableEntity.PrefabId</c>, which is the property that
        /// lied.
        /// </summary>
        [Test]
        public void EverySaveablePrefabHasItsPrefabIdSerialized()
        {
            var unstamped = new List<string>();

            foreach ((string path, SaveableEntity entity) in SaveablePrefabAssets())
            {
                // The player is SaveScope.External — the world store never instantiates it, so it has
                // no use for a prefab id and is deliberately blank.
                if (entity.Scope == SaveScope.External) continue;

                if (string.IsNullOrEmpty(SaveablePrefabFile.ReadPrefabId(path))) unstamped.Add(path);
            }

            Assert.IsEmpty(unstamped,
                $"{unstamped.Count} prefab(s) carry a SaveableEntity whose prefabId is EMPTY in the " +
                "asset file. They look correct in the editor because OnValidate stamps the value in " +
                "memory, but a build ships the empty one — so anything spawned from them at runtime " +
                "is written into the save and can never be restored:\n  " +
                string.Join("\n  ", unstamped) +
                "\n\nFix: Tools ▸ Save System ▸ Wire Saveable Prefabs.");
        }

        /// <summary>
        /// A stamped id is the prefab's OWN guid.
        ///
        /// Catches the other half of the same failure. The derivation used to walk a variant chain to
        /// its root with <c>GetCorrespondingObjectFromOriginalSource</c>, and in this project that
        /// root is usually an imported <c>.fbx</c> — Golem and DuneRat are both variants of their
        /// model prefabs. So they were stamped with the model's guid, which the registry can never
        /// resolve to something instantiable, while the asset branch of the same OnValidate stamped
        /// the real one. One object, two disagreeing ids, decided by which branch ran.
        /// </summary>
        [Test]
        public void EveryStampedPrefabIdMatchesItsOwnAssetGuid()
        {
            var wrong = new List<string>();

            foreach ((string path, SaveableEntity entity) in SaveablePrefabAssets())
            {
                if (entity.Scope == SaveScope.External) continue;

                string onDisk = SaveablePrefabFile.ReadPrefabId(path);
                if (string.IsNullOrEmpty(onDisk)) continue;      // reported by the test above

                string own = AssetDatabase.AssetPathToGUID(path);
                if (onDisk != own) wrong.Add($"{path}\n      stamped {onDisk}, should be {own}");
            }

            Assert.IsEmpty(wrong,
                $"{wrong.Count} prefab(s) are stamped with a guid that is not their own — most likely " +
                "the guid of the .fbx they are a variant of, which nothing can instantiate:\n  " +
                string.Join("\n  ", wrong) +
                "\n\nFix: Tools ▸ Save System ▸ Wire Saveable Prefabs.");
        }

        /// <summary>
        /// A scene instance's <c>prefabId</c> override names the prefab it is an instance of.
        ///
        /// Same defect as the test above, seen from the scene side. The override block carries the
        /// source prefab's guid on its own <c>target:</c> line, so the correct value is knowable from
        /// the file itself and this needs no fixture.
        /// </summary>
        [Test]
        public void ScenePrefabIdOverridesNameTheirSourcePrefab()
        {
            var mismatched = new List<string>();

            var pattern = new Regex(
                @"- target: \{fileID: -?\d+, guid: (?<source>[0-9a-f]{32}), type: 3\}\s*\n" +
                @"\s*propertyPath: prefabId\s*\n" +
                @"\s*value: (?<value>[0-9a-f]*)",
                RegexOptions.Compiled);

            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Game/Scenes" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                string text;
                try { text = File.ReadAllText(path); }
                catch (IOException) { continue; }

                foreach (Match match in pattern.Matches(text))
                {
                    string source = match.Groups["source"].Value;
                    string value = match.Groups["value"].Value;

                    if (value == source) continue;

                    mismatched.Add($"{path}\n      instance of {AssetDatabase.GUIDToAssetPath(source)} " +
                                   $"is stamped {value} ({AssetDatabase.GUIDToAssetPath(value)})");
                }
            }

            Assert.IsEmpty(mismatched,
                $"{mismatched.Count} scene instance(s) carry a prefabId that is not the prefab they " +
                "came from. Harmless while the object stays authored — the scene file recreates it — " +
                "and unrecoverable the moment one is spawned at runtime:\n  " +
                string.Join("\n  ", mismatched));
        }

        /// <summary>
        /// Everything filed under <c>Resources/Saveable</c> can actually be rebuilt.
        ///
        /// That folder exists for exactly one purpose: prefabs the store must be able to instantiate
        /// from a record. A prefab in it with no <c>SaveableEntity</c>, or with an unstamped one, is
        /// a contradiction the registry reports at runtime with a warning nobody reads.
        /// </summary>
        [Test]
        public void EveryResourcesSaveablePrefabIsRestorable()
        {
            var broken = new List<string>();

            foreach (GameObject prefab in Resources.LoadAll<GameObject>(SaveablePrefabRegistry.ResourcesFolder))
            {
                string path = AssetDatabase.GetAssetPath(prefab);
                var entity = prefab.GetComponent<SaveableEntity>();

                if (entity == null)
                {
                    broken.Add($"{path} — no SaveableEntity");
                    continue;
                }

                if (string.IsNullOrEmpty(SaveablePrefabFile.ReadPrefabId(path)))
                    broken.Add($"{path} — prefabId empty in the file");
            }

            Assert.IsEmpty(broken,
                $"{broken.Count} prefab(s) under Resources/{SaveablePrefabRegistry.ResourcesFolder} " +
                "cannot be restored from a save record:\n  " + string.Join("\n  ", broken));
        }

        // ─────────────────────────────────────────────
        //  Internals
        // ─────────────────────────────────────────────

        /// <summary>Every prefab under Assets/Game that carries a <see cref="SaveableEntity"/>.</summary>
        private static IEnumerable<(string path, SaveableEntity entity)> SaveablePrefabAssets()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) continue;

                var entity = asset.GetComponent<SaveableEntity>();
                if (entity == null) continue;

                yield return (path, entity);
            }
        }

    }
}
