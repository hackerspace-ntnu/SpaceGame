// What the residents baseline plays and how. One object, so the editor menu, a bridge call and the
// play-mode harness read the same numbers — and a run to compare against an earlier one must use the
// same values, because the plan itself depends on them (walking times are measured in game minutes,
// so a shorter day stretches every walk across more of it).
using System;
using UnityEngine;

namespace SpaceGame.Agents
{
    [Serializable]
    public sealed class ResidentsBaselineSettings
    {
        [Tooltip("The world's root scene, by name (it is in the build settings). Loaded after the Bootstrap chain unless it is already the active scene; it starts the world offline.")]
        public string worldScene = "persistentScene";

        [Tooltip("The chunk scene that holds the settlement. Its residents are the ones sampled.")]
        public string settlementScene = "Chunk_6_3";

        [Tooltip("Time.captureDeltaTime = 1 / this. An unfocused editor runs at a few fps; a fixed step keeps the walking honest.")]
        public float simulatedFrameRate = 60f;

        [Tooltip("Real seconds per in-game day during the run (the shipped Sun uses 2400). Shorter fits a day into fewer frames.")]
        public float dayLengthSeconds = 600f;

        [Tooltip("The day the run is anchored to. Plans are seeded by (settlement, day), so equal runs plan the same day.")]
        public int startDay = 1;

        [Tooltip("The hour (0-24) the run starts at; it ends one in-game day later. Crossing midnight ends the start day and spreads gossip.")]
        [Range(0f, 24f)] public float startHour = 5f;

        [Tooltip("Game seconds between samples. Segment boundaries, distances and counters are read this often.")]
        public float sampleSeconds = 0.25f;

        [Tooltip("A segment counts as reached when its resident came this close to the plan's place, metres.")]
        public float reachedWithin = 3f;

        [Tooltip("The observer (the offline player) stands this far from the settlement's walkable heart. Residents only talk with a player in earshot.")]
        public Vector3 observerOffset = new Vector3(4f, 0f, 4f);

        [Tooltip("Give one resident with family a first-hand deed at the start, so the day-end gossip has something to spread.")]
        public bool seedDeed = true;

        [Tooltip("Days the seeded deed is held before it is forgiven.")]
        public float seededDeedDays = 7f;

        [Tooltip("Frames to wait for the game's Bootstrap scene chain to finish loading.")]
        public int settleTimeoutFrames = 1800;

        [Tooltip("Frames to wait for the settlement chunk to stream in and its society to have residents.")]
        public int settlementTimeoutFrames = 3600;

        [Tooltip("Frames the world runs before the observer is moved, and again after it arrives and before the clock is set: NavMesh, stands and bodies settle.")]
        public int warmupFrames = 120;

        [Tooltip("Wall-clock minutes after which the run stops where it is and reports PARTIAL.")]
        public float maxWallClockMinutes = 90f;

        [Tooltip("One row per resident per segment boundary, relative to the project root.")]
        public string csvPath = "Temp/residents_baseline.csv";

        [Tooltip("Totals and the outcome, relative to the project root. Its last line is DONE.")]
        public string summaryPath = "Temp/residents_baseline_summary.txt";
    }
}
