// Puts a Seat on every sit spot a prefab brings along: a seated SpotUse says a resident sits there, and with no Seat
// near it nobody will. Idempotent, like the errand content builder that runs it: a spot that already has its seat (named
// Seat_<spot>) is left alone, a prefab open in Prefab Mode is skipped and named, and every save is read back off disk.
//
// A seat is placed on its OWN prefab's spots only. A spot that arrives through a nested prefab instance belongs to that
// prefab, which gets its seat when this reaches it, and the seat then shows up in every prefab that nests it.
//
// The seat stands where the spot stands, facing the way the spot faces, at true scale whatever the spot's parent is
// scaled to: a seat is sized for the people who sit on it, not for the tent it is under.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceGame.Agents.Residents.EditorTools
{
    public static class SeatPlacer
    {
        private const string PrefabsDir = "Assets/Game/Prefabs/Environment";
        private const string SeatDir = PrefabsDir + "/Decorations/Furniture";

        // What a seated use with no seatPrefab of its own gets, in turn.
        private static readonly string[] DefaultSeats = { "Deco_Seat_Wood", "Deco_Seat_Clay", "Deco_Seat_Pillow" };

        [MenuItem("Tools/SpaceGame/Residents/Place Seats At Sit Spots")]
        public static void Run()
        {
            var notes = new List<string>();
            int placed = PlaceAll(notes);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Residents] {placed} seat(s) placed:\n" + string.Join("\n", notes));
        }

        /// <summary>Places the seats every prefab's sit spots lack; returns how many were added.</summary>
        public static int PlaceAll(List<string> notes)
        {
            string[] seatedGuids = AssetDatabase.FindAssets("t:SpotUse")
                .Where(guid => AssetDatabase.LoadAssetAtPath<SpotUse>(AssetDatabase.GUIDToAssetPath(guid)).seated)
                .ToArray();

            int placed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabsDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string yaml = File.ReadAllText(path);
                if (seatedGuids.Any(yaml.Contains)) placed += Place(path, notes);
            }
            return placed;
        }

        private static int Place(string path, List<string> notes)
        {
            if (PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == path)
            {
                notes.Add($"skipped {path}: open in Prefab Mode");
                return 0;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                List<SettlementSpot> spots = OwnSitSpots(root);
                int placed = 0;
                for (int i = 0; i < spots.Count; i++)
                    if (PlaceSeat(spots[i], i, notes, path)) placed++;
                if (placed == 0) return 0;

                PrefabUtility.SaveAsPrefabAsset(root, path);
                Verify(path);
                return placed;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // The sit spots the prefab itself brings, not the ones a nested prefab instance does.
        private static List<SettlementSpot> OwnSitSpots(GameObject root) =>
            root.GetComponentsInChildren<SettlementSpot>(true)
                .Where(spot => spot.Use != null && spot.Use.seated && !PrefabUtility.IsPartOfNonAssetPrefabInstance(spot.gameObject))
                .ToList();

        private static bool PlaceSeat(SettlementSpot spot, int index, List<string> notes, string path)
        {
            Transform parent = ParentOf(spot);
            if (parent.Find(SeatName(spot)) != null) return false;

            GameObject prefab = spot.Use.seatPrefab != null
                ? spot.Use.seatPrefab
                : AssetDatabase.LoadAssetAtPath<GameObject>($"{SeatDir}/{DefaultSeats[index % DefaultSeats.Length]}.prefab");
            if (prefab == null || prefab.GetComponent<Seat>() == null)
                throw new InvalidOperationException($"[Residents] Seat placement: the seat prefab for '{spot.Use.name}' is missing or has no Seat component.");

            var seat = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            seat.name = SeatName(spot);
            Vector3 facing = Vector3.ProjectOnPlane(spot.transform.forward, Vector3.up);
            seat.transform.SetPositionAndRotation(spot.transform.position,
                                                  facing.sqrMagnitude > Mathf.Epsilon ? Quaternion.LookRotation(facing) : Quaternion.identity);
            seat.transform.localScale = Vector3.one / parent.lossyScale.x;
            notes.Add($"{prefab.name} placed at {spot.Use.name} spot '{spot.name}' in {path}");
            return true;
        }

        private static Transform ParentOf(SettlementSpot spot) => spot.transform.parent != null ? spot.transform.parent : spot.transform.root;

        private static string SeatName(SettlementSpot spot) => $"Seat_{spot.name}";

        // Re-read off disk: the AssetDatabase can go read-only and discard a save without a word.
        private static void Verify(string path)
        {
            GameObject saved = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (SettlementSpot spot in OwnSitSpots(saved))
                    if (ParentOf(spot).Find(SeatName(spot)) == null)
                        throw new InvalidOperationException($"[Residents] Seat placement verify failed: {path} lost the seat of '{spot.name}' after saving.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(saved);
            }
        }
    }
}
