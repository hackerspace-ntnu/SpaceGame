using System.Collections.Generic;
using System.Linq;
using SpaceGame.Agents.Residents;
using SpaceGame.Agents.Residents.EditorTools;
using SpaceGame.Items;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools.Outposts
{
    /// <summary>
    /// What residents move about at an outpost. A prop is a <see cref="SettlementProp"/> resting at a <see cref="PropRest"/> served by
    /// an errand stop (a <c>GoodsPile</c> spot); a hauler fetches it from the stop it rests at and sets it down at a free rest of another.
    /// The crates, tool crates and handcarts the outposts are made of already bring a <c>GoodsPile</c> stop each; here the carried
    /// things the author stood about (<see cref="Carried"/>) become props, resting beside the nearest stop, and every stop gets
    /// floor rests for the others to be set down on.
    /// </summary>
    public static class OutpostProps
    {
        /// <summary>The standing <c>Carry_*</c> items a resident's hand can hold, and so a hauler can move.</summary>
        private static readonly string[] Carried = { "Carry_Bucket_Metal", "Carry_Tank_Oil" };

        // Metres: a stop with no prop of its own to serve is given two rests this far in front of it, side by side.
        private const float RestAhead = 0.9f;
        private const float RestApart = 0.45f;

        // A prop rests at the stop of the same pile when one is this close, flat; else it gets a stop of its own.
        private const float StopReach = 4f;
        private const float OwnStopAhead = 1.3f;

        private const string SpotUseDir = "Assets/Game/ScriptableObjects/Settlements/Spots";
        private const string TuningPath = "Assets/Game/Resources/Residents/ResidentTuning.asset";

        public static bool IsProp(string kind) => Carried.Contains(kind);

        /// <summary>Stands the props and their rests; returns (props, rests) added.</summary>
        public static (int props, int rests) Add(Transform parent, IReadOnlyList<PlacedPiece> pieces, Vector3 outpostCentre)
        {
            SpotUse goodsPile = AssetDatabase.LoadAssetAtPath<SpotUse>($"{SpotUseDir}/GoodsPile.asset");
            var stops = pieces.Where(p => p.instance != null).SelectMany(p => p.instance.GetComponentsInChildren<SettlementSpot>(true))
                              .Where(s => s.Use == goodsPile && s.gameObject.activeInHierarchy).ToList();

            int props = 0, rests = 0;
            foreach (PlacedPiece piece in pieces)
            {
                if (!IsProp(piece.row.kind)) continue;

                InventoryItem item = OutpostItemModels.LoadItem(piece.row.kind);
                EnsureCarryItem(item);
                GameObject model = OutpostItemModels.EnsureModel(item, carried: true, out float lift);

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
                instance.name = piece.name;
                instance.transform.SetPositionAndRotation(piece.row.Position + Vector3.down * lift, piece.row.Rotation);

                SettlementSpot stop = NearestStop(stops, instance.transform.position) ?? AddOwnStop(parent, goodsPile, instance.transform, outpostCentre, stops);
                PropRest home = AddRest(parent, $"Rest_{instance.name}", instance.transform.position, instance.transform.rotation, stop);
                var so = new SerializedObject(instance.GetComponent<SettlementProp>());
                so.FindProperty("home").objectReferenceValue = home;
                so.ApplyModifiedPropertiesWithoutUndo();
                props++;
                rests++;
            }

            foreach (SettlementSpot stop in stops)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 at = stop.transform.position + stop.transform.forward * RestAhead + stop.transform.right * (RestApart * side);
                    AddRest(parent, $"Rest_{stop.transform.parent.name}_{side}", at, stop.transform.rotation, stop);
                    rests++;
                }
            }
            return (props, rests);
        }

        private static SettlementSpot NearestStop(List<SettlementSpot> stops, Vector3 point)
        {
            SettlementSpot nearest = null;
            float nearestSqr = StopReach * StopReach;
            foreach (SettlementSpot stop in stops)
            {
                float sqr = Vector3.ProjectOnPlane(stop.transform.position - point, Vector3.up).sqrMagnitude;
                if (sqr > nearestSqr) continue;
                nearest = stop;
                nearestSqr = sqr;
            }
            return nearest;
        }

        // A stop on the prop's open side (toward the outpost's centre), looking at it.
        private static SettlementSpot AddOwnStop(Transform parent, SpotUse goodsPile, Transform prop, Vector3 outpostCentre, List<SettlementSpot> stops)
        {
            Vector3 inward = Vector3.ProjectOnPlane(outpostCentre - prop.position, Vector3.up).normalized;
            string name = $"Spot_Stop_{prop.name}";
            ResidentErrandContentBuilder.AddSpotObject(parent.gameObject, goodsPile, name, prop.position + inward * OwnStopAhead, prop.position, null);
            SettlementSpot stop = parent.Find(name).GetComponent<SettlementSpot>();
            stops.Add(stop);
            return stop;
        }

        private static PropRest AddRest(Transform parent, string name, Vector3 position, Quaternion rotation, SettlementSpot stop)
        {
            var rest = new GameObject(name);
            rest.transform.SetParent(parent, false);
            rest.transform.SetPositionAndRotation(position, rotation);
            var so = new SerializedObject(rest.AddComponent<PropRest>());
            so.FindProperty("spot").objectReferenceValue = stop;
            so.ApplyModifiedPropertiesWithoutUndo();
            return rest.GetComponent<PropRest>();
        }

        // A resident can only carry what is in the tuning's carry list, and the list is append-only: the prop byte replicated
        // between machines is an index into it.
        private static void EnsureCarryItem(InventoryItem item)
        {
            var tuning = AssetDatabase.LoadAssetAtPath<ResidentTuning>(TuningPath);
            var so = new SerializedObject(tuning);
            SerializedProperty list = so.FindProperty("carryItems");
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == item) return;

            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = item;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(tuning);
            AssetDatabase.SaveAssetIfDirty(tuning);
        }
    }
}
