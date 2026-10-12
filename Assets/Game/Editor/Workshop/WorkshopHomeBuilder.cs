// What makes the workshop a place people live in: beds, a table, seats, work posts, and the markers the settlement
// reads to settle residents there (docs/AI/systems/Residents.md). Called by WorkshopPrefabBuilder, so it is rebuilt
// with the prefab; the rows below are the whole of it.
//
// Everything stands on the upper levels: the ledge round the main room and the tiers of caves above it are one
// walkable area whatever the two turning rooms are doing, and no NavMesh joins it to the pit floors. So nobody who
// lives here is ever cut off from bed by a room the player turned, or found standing on a floor that has to turn.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public static class WorkshopHomeBuilder
    {
        private const string Decorations = "Assets/Game/Prefabs/Environment/Decorations/";
        private const string Uses = "Assets/Game/ScriptableObjects/Settlements/Spots/";

        /// <summary>A furniture prefab stood on the floor. It brings its own spots, seats and collision.</summary>
        private readonly struct Piece
        {
            public readonly string name, prefab;
            public readonly float x, z, yaw, nearY;

            public Piece(string name, string prefab, float x, float z, float yaw, float nearY) =>
                (this.name, this.prefab, this.x, this.z, this.yaw, this.nearY) = (name, prefab, x, z, yaw, nearY);
        }

        /// <summary>A place to stand that needs nothing built: a work post at a machine of the model, or a view. Where, and what to face.</summary>
        private readonly struct Post
        {
            public readonly string name, group;
            public readonly float x, z, nearY;
            public readonly Vector3 face;

            public Post(string name, string group, float x, float z, float nearY, Vector3 face) =>
                (this.name, this.group, this.x, this.z, this.nearY, this.face) = (name, group, x, z, nearY, face);
        }

        // Prefab space: the main room's pit-floor centre is the origin. Measured off a NavMesh bake of the prefab
        // (2026-10-09): each row stands where that bake left open floor round it.
        private const float LedgeY = 6.1f;
        private const float MessY = 17.45f;
        private const float LoftY = 48.7f;

        // The sleeping mats lie in a row in the loft, heads to the north wall. A mat's long side is its local X and
        // its bolster is at +X, so a yaw of -90 puts the head at +Z.
        private const string Mat = Decorations + "Living/Deco_SleepingMat_Set.prefab";
        private const float MatYaw = -90f;
        private const float MatRowZ = -4.6f;
        private static readonly float[] MatRowX = { 32.9f, 34.8f, 36.7f, 38.6f };

        // Where a sleeper's owner stands before lying down: the foot of the mat, inside ResidentTuning.seatReach of it.
        private const float BedStandBack = 1.3f;
        private const float MatThickness = 0.09f;
        private static readonly Vector3 BerthTrigger = new(0.6f, 0.3f, 2.4f);

        // The table stands along the mess's west wall and brings a place to stand at it.
        private static readonly Piece[] Furniture =
        {
            new("Mess_Table", Decorations + "Furniture/Deco_Table_Dining.prefab", 18.75f, 26.5f, 90f, MessY),
        };

        // Clay stools, each a place to sit: two drawn up to the table and one across the room from it.
        private const string Stool = Decorations + "Furniture/Deco_Seat_Clay.prefab";
        private const string StoolCircle = "mess";
        private static readonly Piece[] Stools =
        {
            new("Mess_Stool_1", Stool, 19.85f, 25.4f, -90f, MessY),
            new("Mess_Stool_2", Stool, 19.85f, 27.6f, -90f, MessY),
            new("Mess_Stool_3", Stool, 20.8f, 28.3f, 180f, MessY),
        };

        private static readonly Post[] Posts =
        {
            new("Post_Hoist", string.Empty, -19.68f, 7.5f, LedgeY, new Vector3(-18.76f, 7f, 7.77f)),
            new("Post_WheelWatch_1", "wheel", -20f, 1.5f, LedgeY, new Vector3(12.4f, 1.75f, 10.4f)),
            new("Post_WheelWatch_2", "wheel", -20.2f, 4.6f, LedgeY, new Vector3(12.4f, 1.75f, 10.4f)),
        };

        // Places to stand and look: two at the ledge's rail over the main room, one in the lookout. Nothing is built for them.
        private const float LookoutY = 48.7f;
        private static readonly Post[] Views =
        {
            new("View_LedgeNorth", string.Empty, -13.24f, 15.78f, LedgeY, new Vector3(0f, 4f, 0f)),
            new("View_LedgeSouth", string.Empty, -18.67f, -8.71f, LedgeY, new Vector3(0f, 4f, 0f)),
            new("View_Lookout", string.Empty, -33.3f, -5.2f, LookoutY, new Vector3(-38.2f, 50.3f, -6f)),
        };

        // The open ledge over the lane's mouth: the middle of the area everything else is walked to from.
        private static readonly Vector3 Heart = new(-22f, LedgeY, 6.5f);

        // The mouth of the loft, facing out along the gallery that leads to it.
        private static readonly Vector3 Door = new(30.5f, LoftY, -8.6f);
        private const float DoorYaw = -90f;

        private const float FloorProbeUp = 1.2f;
        private const float FloorProbeDown = 2.6f;

        /// <summary>
        /// Furnish <paramref name="root"/>, which must already carry the workshop's collision. Returns what could not
        /// be stood on a floor, empty when everything was.
        /// </summary>
        public static List<string> Furnish(GameObject root)
        {
            var problems = new List<string>();
            Physics.SyncTransforms();

            Transform home = Child(root.transform, "Home");

            var dwelling = new SerializedObject(root.AddComponent<Dwelling>());
            SerializedFields.SetInt(dwelling, "beds", MatRowX.Length);
            dwelling.ApplyModifiedPropertiesWithoutUndo();

            Transform heart = Child(home, "Heart");
            heart.position = OnFloor(root, Heart.x, Heart.z, Heart.y, "Heart", problems);
            heart.gameObject.AddComponent<SettlementHeart>();

            Transform door = Child(home, "Door");
            door.SetPositionAndRotation(OnFloor(root, Door.x, Door.z, Door.y, "Door", problems), Quaternion.Euler(0f, DoorYaw, 0f));
            door.gameObject.AddComponent<SettlementEntrance>();

            SpotUse bunk = Use("Bunk");
            for (int i = 0; i < MatRowX.Length; i++) AddBed(root, home, i, bunk, problems);

            foreach (Piece piece in Furniture)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(piece.prefab);
                if (asset == null)
                {
                    problems.Add($"{piece.name}: no prefab at {piece.prefab}");
                    continue;
                }

                Stand(asset, home, piece.name, OnFloor(root, piece.x, piece.z, piece.nearY, piece.name, problems), piece.yaw);
            }

            SpotUse sit = Use("Seat");
            foreach (Piece stool in Stools)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(stool.prefab);
                if (asset == null)
                {
                    problems.Add($"{stool.name}: no prefab at {stool.prefab}");
                    continue;
                }

                // A sit spot is authored where its seat is: the settlement finds the seat from the spot.
                Vector3 floor = OnFloor(root, stool.x, stool.z, stool.nearY, stool.name, problems);
                Stand(asset, home, stool.name, floor, stool.yaw);
                Transform spot = Child(home, stool.name + "_Spot");
                spot.SetPositionAndRotation(floor, Quaternion.Euler(0f, stool.yaw, 0f));
                MakeSpot(spot, sit, null, StoolCircle);
            }

            AddStandingSpots(root, home, Posts, Use("Workshop"), problems);
            AddStandingSpots(root, home, Views, Use("View"), problems);
            return problems;
        }

        private static void AddStandingSpots(GameObject root, Transform home, Post[] rows, SpotUse use, List<string> problems)
        {
            foreach (Post row in rows)
            {
                Transform spot = Child(home, row.name);
                Vector3 feet = OnFloor(root, row.x, row.z, row.nearY, row.name, problems);
                Vector3 toward = Vector3.ProjectOnPlane(row.face - feet, Vector3.up);
                spot.SetPositionAndRotation(feet, Quaternion.LookRotation(toward.normalized, Vector3.up));

                Transform face = Child(spot, "Face");
                face.position = row.face;
                MakeSpot(spot, use, face, row.group);
            }
        }

        private static void AddBed(GameObject root, Transform home, int index, SpotUse use, List<string> problems)
        {
            string name = $"Bed_{index + 1}";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Mat);
            Vector3 floor = OnFloor(root, MatRowX[index], MatRowZ, LoftY, name, problems);
            if (asset == null) problems.Add($"{name}: no prefab at {Mat}");
            else Stand(asset, home, name, floor, MatYaw);

            // The mat prefab is scenery with no place on it, so the bed is made here: a lying seat on the mat and the
            // spot its owner walks to. A sleeper lies with its head behind the seat's forward (seen in Play 2026-10-09),
            // so the seat looks down the mat from the bolster.
            Quaternion headward = Quaternion.Euler(0f, MatYaw, 0f) * Quaternion.LookRotation(Vector3.right, Vector3.up);
            Transform berth = Child(home, name + "_Berth");
            berth.SetPositionAndRotation(floor + (Vector3.up * MatThickness), headward * Quaternion.Euler(0f, 180f, 0f));

            BoxCollider reach = berth.gameObject.AddComponent<BoxCollider>();
            reach.isTrigger = true;
            reach.size = BerthTrigger;

            var seat = new SerializedObject(berth.gameObject.AddComponent<Seat>());
            seat.FindProperty("pose").intValue = (int)SeatPose.Lie;
            seat.ApplyModifiedPropertiesWithoutUndo();

            Transform spot = Child(home, name + "_Spot");
            spot.SetPositionAndRotation(floor - (headward * Vector3.forward * BedStandBack), headward);
            MakeSpot(spot, use, berth, string.Empty);
        }

        private static void MakeSpot(Transform spot, SpotUse use, Transform face, string group)
        {
            var so = new SerializedObject(spot.gameObject.AddComponent<SettlementSpot>());
            SerializedFields.Set(so, "use", use);
            SerializedFields.Set(so, "face", face);
            SerializedFields.SetString(so, "group", group);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Stand(GameObject asset, Transform parent, string name, Vector3 position, float yaw)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            instance.name = name;
            instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        }

        private static SpotUse Use(string name)
        {
            var use = AssetDatabase.LoadAssetAtPath<SpotUse>(Uses + name + ".asset");
            if (use == null) throw new System.IO.FileNotFoundException($"no SpotUse at {Uses}{name}.asset");
            return use;
        }

        /// <summary>The workshop's own floor under a point: the highest of its colliders within reach below it.</summary>
        internal static Vector3 OnFloor(GameObject root, float x, float z, float nearY, string what, List<string> problems)
        {
            var from = new Vector3(x, nearY + FloorProbeUp, z);
            float best = float.NegativeInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, FloorProbeUp + FloorProbeDown, ~0,
                                                          QueryTriggerInteraction.Ignore))
                if (hit.transform.IsChildOf(root.transform) && hit.point.y > best) best = hit.point.y;

            if (!float.IsNegativeInfinity(best)) return new Vector3(x, best, z);

            problems.Add($"{what}: no floor of the workshop under ({x:0.##}, {nearY:0.##}, {z:0.##})");
            return new Vector3(x, nearY, z);
        }

        private static Transform Child(Transform parent, string name)
        {
            Transform child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }
    }
}
