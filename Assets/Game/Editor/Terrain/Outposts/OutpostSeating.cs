using System.Collections.Generic;
using SpaceGame.Agents.Residents.EditorTools;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools.Outposts
{
    /// <summary>
    /// A seat only matters to a resident once a sit spot stands beside it: the spot is where the body walks to, the seat is where it
    /// ends up. The outposts' seats are loose <c>Deco_Seat_*</c> pieces the author set round their fires, so each gets its spot here.
    /// Seats within <see cref="CircleReach"/> of a fire are one circle with it (<c>HearthSeat</c>: the evening gathers there, and
    /// residents of one circle talk to each other); any others are a seat for free time (<c>Seat</c>).
    /// </summary>
    public static class OutpostSeating
    {
        // Metres, flat: a seat this near a fire sits at it. The author's circles are 2 to 5 m across.
        private const float CircleReach = 6f;

        private const string SpotUseDir = "Assets/Game/ScriptableObjects/Settlements/Spots";

        private static readonly HashSet<string> Fires = new() { "CookingHearth_Pot", "CookingHearth_Spit", "Cauldron" };

        public static bool IsFire(string kind) => Fires.Contains(kind);
        public static bool IsSeat(string kind) => kind.StartsWith("Seat_");

        /// <summary>Puts a sit spot at every seat piece under <paramref name="parent"/>; returns how many.</summary>
        public static int Add(Transform parent, IReadOnlyList<PlacedPiece> pieces)
        {
            SpotUse hearthSeat = AssetDatabase.LoadAssetAtPath<SpotUse>($"{SpotUseDir}/HearthSeat.asset");
            SpotUse seat = AssetDatabase.LoadAssetAtPath<SpotUse>($"{SpotUseDir}/Seat.asset");

            var circles = new Dictionary<PlacedPiece, GameObject>();
            int count = 0;
            foreach (PlacedPiece piece in pieces)
            {
                if (!IsSeat(piece.row.kind)) continue;

                Seat sittable = piece.instance.GetComponentInChildren<Seat>(true);
                if (sittable == null) continue;

                PlacedPiece fire = NearestFire(piece, pieces);
                GameObject spotParent;
                if (fire == null) spotParent = parent.gameObject;
                else if (!circles.TryGetValue(fire, out spotParent))
                {
                    spotParent = new GameObject($"Circle_{fire.instance.name}");
                    spotParent.transform.SetParent(parent, false);
                    spotParent.transform.position = fire.instance.position;
                    circles[fire] = spotParent;
                }

                Vector3 look = fire != null ? fire.instance.position + Vector3.up : sittable.SitPosition + sittable.Facing * Vector3.forward;
                string name = $"Spot_Seat_{piece.instance.name}";
                ResidentErrandContentBuilder.AddSpotObject(spotParent, fire != null ? hearthSeat : seat, name, sittable.StandUpPosition, look, null);
                if (fire != null) NameGroup(spotParent.transform.Find(name));
                count++;
            }
            return count;
        }

        private static void NameGroup(Transform spot)
        {
            var so = new SerializedObject(spot.GetComponent<SettlementSpot>());
            so.FindProperty("group").stringValue = "fire";
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static PlacedPiece NearestFire(PlacedPiece seat, IReadOnlyList<PlacedPiece> pieces)
        {
            PlacedPiece nearest = null;
            float nearestSqr = CircleReach * CircleReach;
            foreach (PlacedPiece other in pieces)
            {
                if (!IsFire(other.row.kind)) continue;

                float sqr = Vector3.ProjectOnPlane(other.instance.position - seat.instance.position, Vector3.up).sqrMagnitude;
                if (sqr > nearestSqr) continue;
                nearest = other;
                nearestSqr = sqr;
            }
            return nearest;
        }
    }
}
