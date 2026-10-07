// Gives every settlement in the open scenes whose culture sends bands out a muster spot, by SettlementMuster's rule,
// without regenerating anything. A settlement keeps no street data of its own after Generate, so its streets are
// traced from the pieces Generate laid under Generated/Streets -- told apart from the terrace walls, and paving from
// stepping stones, by the prefab each was laid from (the style's surface and paved pieces), not by name. Idempotent: a settlement that already has a spot of
// its profile's muster use, placed by rule or brought by a prefab, is left as it is, and nothing else is touched.
// Saves only the scenes it changed, with EditorSceneManager.SaveScene -- never AssetDatabase.SaveAssets, which would
// flush pending terrain edits.
using System.Collections.Generic;
using SpaceGame.Agents.Expeditions;
using SpaceGame.Agents.Residents.EditorTools;
using SpaceGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceGame.EditorTools
{
    public static class MusterSpotAuthoring
    {
        // Metres within which two laid pieces belong to one street. The road node sits ~3.5 m from the first tiles round
        // it, and a run no tile fits is left bare for 2-3 m between a stair foot and a node (SettlementTerraceKit.md).
        private const float PieceLink = 6f;

        [MenuItem("Tools/SpaceGame/Expeditions/Place Muster Spots")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[Expeditions] Place Muster Spots writes scenes; leave Play mode first.");
                return;
            }

            var changed = new HashSet<Scene>();
            foreach (Settlement settlement in Object.FindObjectsByType<Settlement>(FindObjectsSortMode.InstanceID))
                if (!EditorSceneManager.IsPreviewScene(settlement.gameObject.scene) && Place(settlement))
                    changed.Add(settlement.gameObject.scene);

            foreach (Scene scene in changed)
                if (!EditorSceneManager.SaveScene(scene))
                    Debug.LogError($"[Expeditions] {scene.path} was not saved; its new muster spot exists only in memory.");
        }

        // True when a spot was placed: the settlement's scene needs saving.
        private static bool Place(Settlement settlement)
        {
            ExpeditionProfile profile = settlement.Culture != null ? settlement.Culture.expeditions : null;
            if (profile == null) return false;   // sends no bands, needs no muster
            if (profile.musterUse == null)
            {
                Debug.LogError($"[Expeditions] {settlement.name}: {profile.name} has no musterUse. Run Tools/SpaceGame/Expeditions/Author Expedition Content.", profile);
                return false;
            }

            Transform generated = settlement.GeneratedRoot;
            if (generated == null)
            {
                Debug.LogWarning($"[Expeditions] {settlement.name} has not been generated; Generate places its muster spot itself.", settlement);
                return false;
            }

            SettlementSpot existing = SettlementMuster.Find(generated, profile.musterUse);
            if (existing != null)
            {
                Debug.Log($"[Expeditions] {settlement.name} already musters at {Describe(generated, existing)}; left as it is.", existing);
                return false;
            }

            SettlementStreetStyle style = settlement.StreetStyle;
            Transform streets = generated.Find(SettlementStreetLayout.StreetsRootName);
            if (style == null || streets == null)
            {
                Debug.LogError($"[Expeditions] {settlement.name} has no streets to muster on: put a SettlementSpot using " +
                               $"{profile.musterUse.name} under {generated.name} by hand, +Z facing the way out.", settlement);
                return false;
            }

            Vector3 centre = settlement.transform.position;
            List<SettlementMuster.StreetPoint[]> traced = SettlementMuster.TraceStreets(centre, SurfacePieces(streets, style), PieceLink);
            if (!SettlementMuster.TryChoose(centre, traced, ExpeditionTuning.Instance.musterInset, out Pose pose))
            {
                Debug.LogError($"[Expeditions] {settlement.name}: no street could be traced out from the centre under {streets.name}.", settlement);
                return false;
            }

            using (new WorldNavMeshScope(pose.position))
            {
                if (!SettlementMuster.TryPlace(generated, profile.musterUse, pose, settlement.WalkableHeart, out SettlementSpot spot, out string why))
                {
                    Debug.LogError($"[Expeditions] {settlement.name}: the rule's muster pose {pose.position} (facing {Heading(pose.forward):0.#}°) " +
                                   $"is not placed: {why}.", settlement);
                    return false;
                }

                Undo.RegisterCreatedObjectUndo(spot.gameObject, "Place Muster Spot");
                Debug.Log($"[Expeditions] {settlement.name}: muster spot placed at {Describe(generated, spot)}, " +
                          $"{traced.Count} street ends traced from the centre.", spot);
            }
            return true;
        }

        // Every piece a street or stair is laid with, marked paved unless it is a stepping stone; the walls are left out.
        private static List<SettlementMuster.StreetPoint> SurfacePieces(Transform streets, SettlementStreetStyle style)
        {
            var surface = new HashSet<GameObject>(style.SurfacePieces());
            var paved = new HashSet<GameObject>(style.PavedPieces());
            var pieces = new List<SettlementMuster.StreetPoint>();
            foreach (Transform piece in streets)
                if (LaidFrom(piece.gameObject, surface))
                    pieces.Add(new SettlementMuster.StreetPoint(piece.position, LaidFrom(piece.gameObject, paved)));
            return pieces;
        }

        // Up the piece's chain of sources, so a style prefab that is a variant, or a settlement that is itself a prefab, still matches.
        private static bool LaidFrom(GameObject piece, HashSet<GameObject> prefabs)
        {
            for (GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(piece); source != null;
                 source = PrefabUtility.GetCorrespondingObjectFromSource(source))
                if (prefabs.Contains(source)) return true;
            return false;
        }

        private static string Describe(Transform generated, SettlementSpot spot) =>
            $"{SettlementPlaces.PathBelow(generated, spot.transform)} {spot.Position:F2}, facing {Heading(spot.transform.forward):0.#}°";

        // Degrees clockwise from north (+Z).
        private static float Heading(Vector3 forward) => Mathf.Repeat(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 360f);
    }
}
