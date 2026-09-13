// Clears out item records that fell out of the world, from saves written before 2026-09-08.
//
// Until then an equipped copy kept the SaveableEntity its prefab ships for a copy lying in the sand,
// and WorldSaveStore.CaptureScene walks GetComponentsInChildren from every scene root — so the
// gauntlet on a player's forearm was written into the save as a world entity at its world pose. The
// next hydrate built that record back as a loose root object with its Rigidbody live: a duplicate at
// the player, falling. It was captured again on the next save, so the population grew by one per
// load and each generation ended up deeper than the last.
//
// EquipItemSocket.Sanitize now takes the record off the copy, so no new ones are written. This is
// for the ones already in the file. It drops RUNTIME records that have fallen below the world floor
// and nothing else: a record at a plausible height is indistinguishable from an item the player
// genuinely dropped there, and deleting someone's belongings to tidy up a bug is the worse failure.
//
// Run from: Tools ▸ Save System ▸ Drop Fallen Item Records
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence.EditorTools
{
    public static class FallenItemSweep
    {
        /// <summary>
        /// Below this, a record is not somewhere a player put something — it is something that fell.
        ///
        /// <para>
        /// Taken from the store rather than restated here, so this tool and the load-time hold that
        /// now catches these records cannot disagree about how far down is off the map. Same number
        /// again as <c>UnderTerrainGuard.absoluteFloorY</c>. The orphans this was written for are
        /// thousands of metres below it, so the margin is not fine.
        /// </para>
        /// <para>
        /// Still worth running on an old file even though <c>WorldSaveStore</c> now lands these
        /// rather than dropping them again: landing a record needs the chunk under it to load, and
        /// a player who never walks back there carries the whole fallen population in their save
        /// for the life of the world.
        /// </para>
        /// </summary>
        private const float WorldFloorY = WorldSaveStore.WorldFloorY;

        [MenuItem("Tools/Save System/Drop Fallen Item Records")]
        public static void Sweep()
        {
            string path = EditorUtility.OpenFilePanel("Save file to sweep", SaveManager.DefaultRoot, "json");
            if (string.IsNullOrEmpty(path)) return;

            SaveFileStore.ReadResult read = SaveFileStore.Read(path);
            if (!read.HasDocument)
            {
                EditorUtility.DisplayDialog("Drop Fallen Item Records",
                    $"Could not read {Path.GetFileName(path)}: {read.Outcome}. {read.Error}", "OK");
                return;
            }

            SaveDocument document = read.Document;
            WorldRecord world = document.World;
            if (world?.Entities == null || world.Entities.Count == 0)
            {
                EditorUtility.DisplayDialog("Drop Fallen Item Records", "That save holds no world records.", "OK");
                return;
            }

            List<KeyValuePair<string, EntityRecord>> fallen = world.Entities
                .Where(entry => HasFallenOutOfTheWorld(entry.Value))
                .OrderBy(entry => entry.Value.Position.y)
                .ToList();

            if (fallen.Count == 0)
            {
                EditorUtility.DisplayDialog("Drop Fallen Item Records",
                    $"{Path.GetFileName(path)} has nothing below y={WorldFloorY}. Nothing to do.", "OK");
                return;
            }

            string summary = Describe(fallen);
            Debug.Log($"[Save] Fallen records in {Path.GetFileName(path)}:\n{summary}");

            // Named and counted before the question is asked, not after. This edits a file the player
            // cannot rebuild, and "12 records" is not enough to consent to on its own.
            bool go = EditorUtility.DisplayDialog("Drop Fallen Item Records",
                $"{Path.GetFileName(path)}\n\n{fallen.Count} runtime record(s) below y={WorldFloorY}:\n\n" +
                $"{summary}\n\nDrop them? The previous file is kept as {SaveFileStore.BackupSuffix}.",
                "Drop them", "Cancel");

            if (!go) return;

            foreach (KeyValuePair<string, EntityRecord> entry in fallen)
                world.Entities.Remove(entry.Key);

            SaveFileStore.Write(path, document);

            Debug.Log($"[Save] Dropped {fallen.Count} fallen record(s) from {Path.GetFileName(path)}. " +
                      $"{world.Entities.Count} world record(s) left.");
        }

        /// <summary>
        /// Whether this record describes something that fell out of the world rather than something
        /// a player left where it is.
        ///
        /// <para>
        /// <b>Runtime only.</b> An authored record is a scene object the world ships with, and one of
        /// those reading as below the floor means something else is wrong — a chunk that failed to
        /// place it, a rig with a broken pose — which deleting the record would hide rather than fix.
        /// Authored records are also never dropped by <c>WorldSaveStore.DropVanishedRuntime</c>, and
        /// this must not be the one place that quietly disagrees.
        /// </para>
        /// </summary>
        private static bool HasFallenOutOfTheWorld(EntityRecord record) =>
            record != null && !record.Authored && record.HasPose && record.Position.y < WorldFloorY;

        private static string Describe(List<KeyValuePair<string, EntityRecord>> fallen)
        {
            var byPrefab = new Dictionary<string, List<float>>();

            foreach (KeyValuePair<string, EntityRecord> entry in fallen)
            {
                string name = NameOf(entry.Value.PrefabId);
                if (!byPrefab.TryGetValue(name, out List<float> depths))
                    byPrefab[name] = depths = new List<float>();

                depths.Add(entry.Value.Position.y);
            }

            var text = new StringBuilder();
            foreach (KeyValuePair<string, List<float>> group in byPrefab.OrderByDescending(g => g.Value.Count))
            {
                IEnumerable<string> depths = group.Value.OrderByDescending(y => y).Select(y => $"{y:0}");
                text.AppendLine($"  {group.Value.Count}× {group.Key}  (y = {string.Join(", ", depths)})");
            }

            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// The prefab a record was spawned from. Its id is the asset GUID, so this is a lookup rather
        /// than a guess — and an id that resolves to nothing is worth showing as itself, because an
        /// unresolvable record is a second thing worth knowing about the file.
        /// </summary>
        private static string NameOf(string prefabId)
        {
            if (string.IsNullOrEmpty(prefabId)) return "(no prefab id)";

            string path = AssetDatabase.GUIDToAssetPath(prefabId);
            return string.IsNullOrEmpty(path) ? $"(unknown prefab {prefabId})" : Path.GetFileNameWithoutExtension(path);
        }
    }
}
