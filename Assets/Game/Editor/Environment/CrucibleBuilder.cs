// Builds the Crucible greybox and its power cell from primitives.
//
// Primitives on purpose, and for a while yet. Wall heights and chimney widths ARE the tuning surface
// of this room — the cell can never rise above rail height, so difficulty is authored entirely by
// how tall each wall is — and every one of those numbers will move in playtest. Art before the
// layout stops moving is art thrown away.
//
// Re-running is safe and is the intended workflow: change a constant below, run it again, and the
// room is rebuilt in place.
//
// Re-run from: Tools > SpaceGame > Build Crucible (cell + room)
using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public static class CrucibleBuilder
    {
        private const string Folder = "Assets/Game/Prefabs/Gameplay/Crucible";
        private const string CellPath = Folder + "/CruciblePowerCell.prefab";
        private const string RoomPath = Folder + "/CrucibleRoom.prefab";

        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

        // ── The room, in metres ────────────────────────────────────────────────
        //
        // Rails sit at RailY on the rim's inner face. Anything taller than that must be threaded;
        // anything shorter is a shortcut. That is the whole difficulty dial.

        private const float PitLength = 24f;      // along X
        private const float PitWidth = 16f;       // along Z
        private const float RimHeight = 1.2f;
        private const float LavaY = -9f;
        private const float RailY = -0.4f;
        private const float WallTopY = 0.6f;      // above RailY: cannot be cleared
        private const float LowWallTopY = -1.4f;  // below RailY: can be cleared

        [MenuItem("Tools/SpaceGame/Build Crucible (cell + room)")]
        public static void BuildAll()
        {
            Directory.CreateDirectory(Folder);

            GameObject cell = BuildCell();
            RegisterNetworkPrefab(cell);
            BuildRoom(cell);

            AssetDatabase.SaveAssets();

            Debug.Log($"[Crucible] Built {CellPath} and {RoomPath}. " +
                      "Drag CrucibleRoom into a scene, set the Leash prefab's Wrap Layers to the " +
                      "room's layer, and press play.");
        }

        // ── The cell ───────────────────────────────────────────────────────────

        private static GameObject BuildCell()
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "CruciblePowerCell";
            root.transform.localScale = Vector3.one * 0.4f;

            var body = root.AddComponent<Rigidbody>();
            body.mass = 12f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            root.AddComponent<NetworkObject>();

            // Server authority: both ropes' cell-ends have to resolve on ONE machine, and
            // LeashEnd.ResolvedHere routes that by ownership. Owner authority here would put each
            // rope's correction on a different machine and the cell in two places.
            var transformSync = root.AddComponent<NetworkTransform>();
            transformSync.AuthorityMode = NetworkTransform.AuthorityModes.Server;

            root.AddComponent<LeashAttachable>();
            root.AddComponent<CrucibleCarrier>();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, CellPath);
            Object.DestroyImmediate(root);

            return saved;
        }

        /// <summary>
        /// Put the cell in the network prefab list.
        ///
        /// <para>
        /// Not optional and not visible if skipped: an unregistered runtime-spawned prefab works
        /// perfectly in single player, because single player is a host of one, and fails silently
        /// for every client.
        /// </para>
        /// </summary>
        private static void RegisterNetworkPrefab(GameObject prefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (list == null)
            {
                Debug.LogError($"[Crucible] No network prefab list at {NetworkPrefabsPath}. " +
                               "The cell will not spawn for clients.");
                return;
            }

            foreach (NetworkPrefab entry in list.PrefabList)
                if (entry != null && entry.Prefab == prefab) return;

            // Through the serialized field rather than the runtime accessor: PrefabList is exposed
            // as an IReadOnlyList, and writing the asset the way the Inspector does is also what
            // marks it dirty so the entry survives to disk.
            var serialized = new SerializedObject(list);
            SerializedProperty entries = serialized.FindProperty("List");

            entries.arraySize++;
            SerializedProperty added = entries.GetArrayElementAtIndex(entries.arraySize - 1);
            added.FindPropertyRelative("Override").enumValueIndex = 0;
            added.FindPropertyRelative("Prefab").objectReferenceValue = prefab;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(list);
        }

        // ── The room ───────────────────────────────────────────────────────────

        private static void BuildRoom(GameObject cellPrefab)
        {
            var root = new GameObject("CrucibleRoom");

            var lava = new GameObject("LavaVisuals");
            lava.transform.SetParent(root.transform, false);
            Slab(lava.transform, "LavaSurface", new Vector3(0f, LavaY, 0f),
                 new Vector3(PitLength, 0.2f, PitWidth), new Color(1f, 0.42f, 0.12f));

            var floor = new GameObject("FloorVisuals");
            floor.transform.SetParent(root.transform, false);
            Slab(floor.transform, "FloorSurface", new Vector3(0f, LavaY, 0f),
                 new Vector3(PitLength, 0.2f, PitWidth), new Color(0.24f, 0.22f, 0.19f));
            floor.SetActive(false);

            // The kill volume sits just above the surface so the cell is caught as it arrives
            // rather than after it has sunk through.
            var hazard = new GameObject("HazardTrigger");
            hazard.transform.SetParent(root.transform, false);
            hazard.transform.localPosition = new Vector3(0f, LavaY + 0.8f, 0f);
            var hazardBox = hazard.AddComponent<BoxCollider>();
            hazardBox.isTrigger = true;
            hazardBox.size = new Vector3(PitLength, 1.4f, PitWidth);

            var cradle = new GameObject("Cradle");
            cradle.transform.SetParent(root.transform, false);
            cradle.transform.localPosition = new Vector3(-PitLength * 0.5f + 2f, RailY - 2.5f, 0f);

            BuildRim(root.transform);
            List<LeashRail> rails = BuildRails(root.transform);
            BuildMaze(root.transform);
            GameObject vault = BuildVaultAndSocket(root.transform, out CrucibleSocket socket);

            var pit = root.AddComponent<CruciblePit>();
            var room = root.AddComponent<CrucibleRoom>();

            Wire(pit, "lavaVisuals", lava);
            Wire(pit, "floorVisuals", floor);
            Wire(pit, "cradle", cradle.transform);
            Wire(room, "vaultDoor", vault);
            Wire(socket, "room", room);

            root.AddComponent<NetworkObject>();

            Debug.Log($"[Crucible] {rails.Count} rails, wired both ways.");

            PrefabUtility.SaveAsPrefabAsset(root, RoomPath);
            Object.DestroyImmediate(root);
        }

        private static void BuildRim(Transform parent)
        {
            float halfL = PitLength * 0.5f, halfW = PitWidth * 0.5f;

            Slab(parent, "Rim_North", new Vector3(0f, RimHeight * -0.5f, halfW + 0.6f),
                 new Vector3(PitLength + 2.4f, RimHeight, 1.2f), Color.grey);
            Slab(parent, "Rim_South", new Vector3(0f, RimHeight * -0.5f, -halfW - 0.6f),
                 new Vector3(PitLength + 2.4f, RimHeight, 1.2f), Color.grey);
            Slab(parent, "Rim_East", new Vector3(halfL + 0.6f, RimHeight * -0.5f, 0f),
                 new Vector3(1.2f, RimHeight, PitWidth), Color.grey);
            Slab(parent, "Rim_West", new Vector3(-halfL - 0.6f, RimHeight * -0.5f, 0f),
                 new Vector3(1.2f, RimHeight, PitWidth), Color.grey);
        }

        /// <summary>
        /// Six slots, three a side, joined into a graph.
        ///
        /// <para>
        /// Neighbours along one wall are connected, and the two walls are NOT connected to each
        /// other. That is the level design: a player is committed to their own side of the pit and
        /// can only slide along it, which is what forces the two of them to co-operate rather than
        /// both walking to whichever side is easiest.
        /// </para>
        /// </summary>
        private static List<LeashRail> BuildRails(Transform parent)
        {
            var rails = new List<LeashRail>();
            float halfW = PitWidth * 0.5f;

            var north = new List<LeashRail>();
            var south = new List<LeashRail>();

            for (int side = 0; side < 2; side++)
            {
                float z = side == 0 ? halfW : -halfW;
                List<LeashRail> run = side == 0 ? north : south;

                for (int i = 0; i < 3; i++)
                {
                    float from = -PitLength * 0.5f + 1f + i * 7.5f;
                    float to = from + 6.5f;

                    run.Add(MakeRail(parent, $"Rail_{(side == 0 ? "N" : "S")}{i}",
                                     new Vector3(from, RailY, z), new Vector3(to, RailY, z)));
                }

                // Both ways. A one-way link is a rail a rope gets onto and can never come off.
                for (int i = 0; i < run.Count - 1; i++) Connect(run[i], run[i + 1]);

                rails.AddRange(run);
            }

            return rails;
        }

        private static LeashRail MakeRail(Transform parent, string name, Vector3 from, Vector3 to)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = (from + to) * 0.5f;

            var a = new GameObject("A");
            a.transform.SetParent(go.transform, false);
            a.transform.position = from;

            var b = new GameObject("B");
            b.transform.SetParent(go.transform, false);
            b.transform.position = to;

            var rail = go.AddComponent<LeashRail>();
            Wire(rail, "from", a.transform);
            Wire(rail, "to", b.transform);

            return rail;
        }

        private static void Connect(LeashRail one, LeashRail other)
        {
            Append(one, other);
            Append(other, one);
        }

        private static void Append(LeashRail rail, LeashRail next)
        {
            var serialized = new SerializedObject(rail);
            SerializedProperty list = serialized.FindProperty("connections");

            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = next;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Three chimneys, tightest last, with tall walls as the connective tissue and one low wall
        /// as a shortcut. The last chimney sits just before the socket, when both players are
        /// already rattled.
        /// </summary>
        private static void BuildMaze(Transform parent)
        {
            float[] gapAt = { -6f, 1f, 7f };
            float[] gapWidth = { 2.5f, 2.0f, 1.6f };

            for (int i = 0; i < gapAt.Length; i++)
            {
                float wallHeight = WallTopY - LavaY;
                float centreY = LavaY + wallHeight * 0.5f;

                float half = gapWidth[i] * 0.5f;
                float sideWidth = (PitWidth * 0.5f) - half;

                Slab(parent, $"Chimney{i}_Left",
                     new Vector3(gapAt[i], centreY, half + sideWidth * 0.5f),
                     new Vector3(1.2f, wallHeight, sideWidth), new Color(0.28f, 0.3f, 0.34f));

                Slab(parent, $"Chimney{i}_Right",
                     new Vector3(gapAt[i], centreY, -half - sideWidth * 0.5f),
                     new Vector3(1.2f, wallHeight, sideWidth), new Color(0.28f, 0.3f, 0.34f));
            }

            // Below rail height on purpose: the one obstacle that CAN be lifted over, so the rule
            // is provable in both directions.
            float lowHeight = LowWallTopY - LavaY;
            Slab(parent, "LowWall_Shortcut",
                 new Vector3(-1.5f, LavaY + lowHeight * 0.5f, 0f),
                 new Vector3(1.2f, lowHeight, PitWidth), new Color(0.36f, 0.38f, 0.42f));
        }

        private static GameObject BuildVaultAndSocket(Transform parent, out CrucibleSocket socket)
        {
            float x = PitLength * 0.5f - 1.5f;

            var socketGo = new GameObject("Socket");
            socketGo.transform.SetParent(parent, false);
            socketGo.transform.localPosition = new Vector3(x, RailY - 2.5f, 0f);

            var box = socketGo.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(1.4f, 1.4f, 1.4f);

            socket = socketGo.AddComponent<CrucibleSocket>();

            GameObject vault = Slab(parent, "VaultDoor",
                                    new Vector3(x, RailY - 2.5f, PitWidth * 0.5f + 0.4f),
                                    new Vector3(2.5f, 3f, 0.3f), new Color(0.5f, 0.42f, 0.2f));

            return vault;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static GameObject Slab(Transform parent, string name, Vector3 position,
                                       Vector3 size, Color colour)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;

            var renderer = go.GetComponent<MeshRenderer>();
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = colour };
            renderer.sharedMaterial = material;

            return go;
        }

        /// <summary>
        /// Set a private [SerializeField] by name.
        ///
        /// <para>
        /// SerializedObject rather than reflection: it is what the Inspector itself uses, it marks
        /// the object dirty so the write actually survives to disk, and a misspelled field name
        /// throws here rather than leaving a silently unwired prefab.
        /// </para>
        /// </summary>
        private static void Wire(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);

            if (property == null)
                throw new System.InvalidOperationException(
                    $"{target.GetType().Name} has no serialized field '{field}'");

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
