// The editor half of the residents baseline: Tools ▸ Agents ▸ Run Residents Baseline, or one bridge call
//
//     SpaceGame.EditorTools.ResidentsBaseline.Run();
//
// then poll Temp/residents_baseline_summary.txt for a last line of DONE. The play-mode half, and what
// is recorded, is ResidentsBaselineRun; the CSV's columns are documented in Residents.md ("Baseline").
// Getting into and out of play mode is PlayModeHarness's. No scene is staged: the run plays whatever is
// open (unsaved edits included — play mode restores them) and loads the world scene itself, which
// starts the world offline with its authored player standing in as the observer.
using System.IO;
using SpaceGame.Agents;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class ResidentsBaseline
    {
        private static readonly PlayModeHarness Harness =
            new(nameof(ResidentsBaseline), null, StartRun, Refuse);

        [MenuItem("Tools/Agents/Run Residents Baseline")]
        private static void RunFromMenu() => Run();

        /// <summary>One in-game day of the Chunk_6_3 settlement with the default settings.</summary>
        public static void Run() => Run(new ResidentsBaselineSettings());

        /// <summary>
        /// Arms a run and returns at once; play mode starts on a later editor tick. The outcome is always
        /// in <see cref="ResidentsBaselineSettings.summaryPath"/>.
        /// </summary>
        public static void Run(ResidentsBaselineSettings settings)
        {
            // A stale summary that ends in DONE would read as this run's result to a poller.
            File.Delete(settings.summaryPath);
            File.Delete(settings.csvPath);
            Harness.Request(JsonUtility.ToJson(settings));
        }

        [InitializeOnLoadMethod]
        private static void Install() => Harness.Install();

        private static void StartRun(string json) =>
            ResidentsBaselineRun.Begin(JsonUtility.FromJson<ResidentsBaselineSettings>(json), PlayModeHarness.Leave);

        private static void Refuse(string json, string reason) =>
            ResidentsBaselineRun.WriteFailure(JsonUtility.FromJson<ResidentsBaselineSettings>(json), "not started: " + reason);
    }
}
