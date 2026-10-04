// Makes the four cart decorations pushable, and wires everything a pushed cart needs around them. Idempotent, like the other
// content builders: what is already there is updated in place (the markers and components keep their fileIDs, so every prefab and
// scene that nests a cart keeps its overrides), a prefab open in Prefab Mode is skipped and named, and every save is read back.
//
// A cart gets: a Pushable, two grip markers where the hands close, a handlebar for them to close on, a marker under the bottom of
// each wheel, an axle marker when it rolls on two wheels with shafts, a kinematic Rigidbody (it is moved, and a moved collider on no body is rebuilt into the static
// tree every frame) and NO static flags: a batching-static renderer keeps drawing where it was baked while its collider moves.
// The flags a building prefab carries on its nested cart instance are overrides, so those are cleared there too.
//
// Around the carts: the PushableLedger on the NetworkGameManager prefab (where a left-behind cart is remembered and shown to
// everyone) and PlayerPushing on the networked player. The Drover archetype pushes a cart; its tool (the lasso, RaxyToolLoadouts) is
// stowed on the belt while it does, so this never writes its held item.
using System;
using System.Collections.Generic;
using System.Linq;
using SpaceGame.Agents.Residents;
using SpaceGame.Characters;
using SpaceGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class PushableAuthoring
    {
        private const string Decorations = "Assets/Game/Prefabs/Environment/Decorations/";
        private const string NomadBuildings = "Assets/Game/Prefabs/Environment/Structures/NomadSettlement/";
        private const string LedgerHost = "Assets/Game/Prefabs/Systems/NetworkGameManager.prefab";
        private const string PlayerPrefab = "Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab";
        private const string DroverAsset = "Assets/Game/ScriptableObjects/Residents/Archetypes/Drover.asset";

        private const string HandlebarName = "Handlebar";
        private const float HandlebarThickness = 0.09f;
        private const string GripName = "Grip_";
        private const string ContactName = "Wheel_";
        private const string AxleName = "Axle";

        /// <summary>One cart's markers, in the prefab's own space (measured off its mesh: see Pushables.md).</summary>
        private sealed class CartSpec
        {
            public string Path;
            public string DisplayName;
            public Vector3[] Grips;

            // The bar the hands close on, in the prefab's space: its middle, the way it lies and half its length. A cart's own
            // shafts are as far apart as the cart is wide (a settlement places carts at 1.4 to 1.8 times the prefab), which is
            // wider than a Raxy's arms reach, so the hands go to a bar between them. Its wood is borrowed from `BarWoodOf`.
            public Vector3 BarAt;
            public Vector3 BarAlong;
            public float BarHalfLength;
            public string BarWoodOf;
            public Vector3[] Contacts;
            public Vector3? Axle;
            public float RideHeight;
        }

        private static readonly CartSpec[] Carts =
        {
            // Two wheels and shafts that end in a cross-bar at z 1.9: lifted until the bar is in the hands.
            new CartSpec
            {
                Path = Decorations + "Transport/Deco_Handcart.prefab", DisplayName = "Handcart",
                Grips = new[] { new Vector3(0.14f, 0.52f, 1.9f), new Vector3(-0.14f, 0.52f, 1.9f) },
                BarAt = new Vector3(0f, 0.52f, 1.9f), BarAlong = Vector3.right, BarHalfLength = 0.58f, BarWoodOf = "Handcart_Shafts",
                Contacts = new[] { new Vector3(0.8f, -0.05f, -0.05f), new Vector3(-0.8f, -0.05f, -0.05f) },
                Axle = new Vector3(0f, 0.55f, -0.05f),
            },
            // No wheels: a plate that floats. Handles at z 1.8.
            new CartSpec
            {
                Path = Decorations + "Transport/Deco_Handcart_Hover.prefab", DisplayName = "Hover cart",
                Grips = new[] { new Vector3(0.14f, 0.87f, 1.8f), new Vector3(-0.14f, 0.87f, 1.8f) },
                BarAt = new Vector3(0f, 0.87f, 1.8f), BarAlong = Vector3.right, BarHalfLength = 0.58f, BarWoodOf = "Handcart_Hover_Handles",
                Contacts = new[]
                {
                    new Vector3(0.5f, 0.02f, 0.5f), new Vector3(-0.5f, 0.02f, 0.5f),
                    new Vector3(0.5f, 0.02f, -0.5f), new Vector3(-0.5f, 0.02f, -0.5f),
                },
                RideHeight = 0.15f,
            },
            // A stall on two wheels whose shaft bars end at z -1.2: pushed from there, toward +Z.
            new CartSpec
            {
                Path = Decorations + "Tavern/Deco_FoodCart.prefab", DisplayName = "Food cart",
                Grips = new[] { new Vector3(0.14f, 0.74f, -1.2f), new Vector3(-0.14f, 0.74f, -1.2f) },
                BarAt = new Vector3(0f, 0.74f, -1.2f), BarAlong = Vector3.right, BarHalfLength = 0.55f, BarWoodOf = "FoodCart_Cart",
                Contacts = new[] { new Vector3(0.97f, 0f, 0f), new Vector3(-0.97f, 0f, 0f) },
            },
            // Four small wheels on rails, long axis along X: pushed from the +X end.
            new CartSpec
            {
                Path = Decorations + "Mining/Deco_MineCart.prefab", DisplayName = "Mine cart",
                Grips = new[] { new Vector3(1.25f, 1.1f, 0.14f), new Vector3(1.25f, 1.1f, -0.14f) },
                BarAt = new Vector3(1.25f, 1.1f, 0f), BarAlong = Vector3.forward, BarHalfLength = 0.55f, BarWoodOf = "MineCart_Chassis",
                Contacts = new[]
                {
                    new Vector3(0.55f, -0.045f, 0.5f), new Vector3(0.55f, -0.045f, -0.5f),
                    new Vector3(-0.55f, -0.045f, 0.5f), new Vector3(-0.55f, -0.045f, -0.5f),
                },
            },
        };

        [MenuItem("Tools/SpaceGame/Pushables/Author Carts And Wiring")]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[Pushables] Author Carts And Wiring does not run in Play mode: prefab saves are discarded there.");
                return;
            }

            var notes = new List<string>();
            foreach (CartSpec cart in Carts) Author(cart, notes);
            foreach (string building in BuildingsNesting()) FreeNestedCarts(building, notes);
            EnsureComponent<PushableLedger>(LedgerHost, notes);
            EnsureComponent<PlayerPushing>(PlayerPrefab, notes);
            AuthorDrover(notes);
            AssetDatabase.SaveAssets();
            Debug.Log("[Pushables] authored:\n" + string.Join("\n", notes));
        }

        // ── the carts ────────────────────────────────────────────────────────────────────────────

        private static void Author(CartSpec spec, List<string> notes)
        {
            if (IsOpen(spec.Path, notes)) return;

            GameObject root = PrefabUtility.LoadPrefabContents(spec.Path);
            try
            {
                Pushable pushable = root.TryGetComponent(out Pushable have) ? have : root.AddComponent<Pushable>();
                Transform[] grips = Markers(root.transform, GripName, spec.Grips);
                Transform[] contacts = Markers(root.transform, ContactName, spec.Contacts);
                Transform axle = spec.Axle.HasValue ? Marker(root.transform, AxleName, spec.Axle.Value) : null;
                Handlebar(root.transform, spec);

                var serialized = new SerializedObject(pushable);
                SerializedFields.SetString(serialized, "displayName", spec.DisplayName);
                SerializedFields.Set(serialized, "handleLeft", grips[0]);
                SerializedFields.Set(serialized, "handleRight", grips[1]);
                SerializedFields.SetObjects(serialized, "wheelContacts", contacts);
                SerializedFields.Set(serialized, "axle", axle);
                SerializedFields.SetFloat(serialized, "rideHeight", spec.RideHeight);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Rigidbody body = root.TryGetComponent(out Rigidbody existing) ? existing : root.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;

                ClearStatic(root);
                PrefabUtility.SaveAsPrefabAsset(root, spec.Path);
                notes.Add($"{spec.DisplayName}: {spec.Path}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            VerifyCart(spec);
        }

        // One child per position, found by name when it already exists (so its fileID survives a re-run) and created when it does not.
        private static Transform[] Markers(Transform root, string prefix, Vector3[] positions)
        {
            var made = new Transform[positions.Length];
            for (int i = 0; i < positions.Length; i++) made[i] = Marker(root, prefix + (char)('A' + i), positions[i]);
            return made;
        }

        private static Transform Marker(Transform root, string name, Vector3 position)
        {
            Transform marker = root.Find(name);
            if (marker == null)
            {
                marker = new GameObject(name).transform;
                marker.SetParent(root, false);
            }
            marker.localPosition = position;
            marker.localRotation = Quaternion.identity;
            marker.localScale = Vector3.one;
            return marker;
        }

        // A cylinder in the cart's own wood across the end of its shafts (or along the rim): what the hands close on. Found by name when it
        // is there, so a re-run keeps its fileID.
        private static void Handlebar(Transform root, CartSpec spec)
        {
            Transform bar = root.Find(HandlebarName);
            if (bar == null)
            {
                GameObject made = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                made.name = HandlebarName;
                UnityEngine.Object.DestroyImmediate(made.GetComponent<Collider>());
                bar = made.transform;
                bar.SetParent(root, false);
            }

            Renderer wood = root.GetComponentsInChildren<Renderer>(true).First(r => r.name == spec.BarWoodOf);
            bar.GetComponent<Renderer>().sharedMaterial = wood.sharedMaterial;
            bar.localPosition = spec.BarAt;
            bar.localRotation = Quaternion.FromToRotation(Vector3.up, spec.BarAlong);
            bar.localScale = new Vector3(HandlebarThickness, spec.BarHalfLength, HandlebarThickness);
        }

        private static void ClearStatic(GameObject root)
        {
            foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(part.gameObject, 0);
        }

        private static void VerifyCart(CartSpec spec)
        {
            GameObject saved = PrefabUtility.LoadPrefabContents(spec.Path);
            try
            {
                Pushable pushable = saved.GetComponent<Pushable>();
                if (pushable == null || !pushable.IsAuthored)
                    throw new InvalidOperationException($"[Pushables] verify failed: {spec.Path} has no authored Pushable after saving.");
                if (saved.GetComponent<Rigidbody>() == null || !saved.GetComponent<Rigidbody>().isKinematic)
                    throw new InvalidOperationException($"[Pushables] verify failed: {spec.Path} lost its kinematic Rigidbody.");
                if (saved.GetComponentsInChildren<Transform>(true).Any(t => GameObjectUtility.GetStaticEditorFlags(t.gameObject) != 0))
                    throw new InvalidOperationException($"[Pushables] verify failed: {spec.Path} still has static flags.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(saved);
            }
        }

        // ── the buildings that carry carts ───────────────────────────────────────────────────────

        private static IEnumerable<string> BuildingsNesting()
        {
            HashSet<string> cartPaths = new HashSet<string>(Carts.Select(c => c.Path));
            return AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Prefabs" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.StartsWith(NomadBuildings) && AssetDatabase.GetDependencies(path, false).Any(cartPaths.Contains))
                .ToList();
        }

        // A nested instance's flags are its own overrides: the cart prefab being un-static does not reach them.
        private static void FreeNestedCarts(string path, List<string> notes)
        {
            if (IsOpen(path, notes)) return;

            HashSet<string> cartPaths = new HashSet<string>(Carts.Select(c => c.Path));
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int freed = 0;
                foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!IsNestedCart(part.gameObject, cartPaths)) continue;
                    ClearStatic(part.gameObject);
                    freed++;
                }
                if (freed == 0) return;

                PrefabUtility.SaveAsPrefabAsset(root, path);
                notes.Add($"{freed} nested cart(s) freed of static flags: {path}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            GameObject saved = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (Transform part in saved.GetComponentsInChildren<Transform>(true))
                    if (IsNestedCart(part.gameObject, cartPaths)
                        && part.GetComponentsInChildren<Transform>(true).Any(t => GameObjectUtility.GetStaticEditorFlags(t.gameObject) != 0))
                        throw new InvalidOperationException($"[Pushables] verify failed: a cart in {path} is still static after saving.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(saved);
            }
        }

        private static bool IsNestedCart(GameObject candidate, HashSet<string> cartPaths) =>
            PrefabUtility.IsOutermostPrefabInstanceRoot(candidate) && cartPaths.Contains(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(candidate));

        // ── the wiring around them ───────────────────────────────────────────────────────────────

        private static void EnsureComponent<T>(string path, List<string> notes) where T : Component
        {
            if (IsOpen(path, notes)) return;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<T>() != null) return;

                root.AddComponent<T>();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                notes.Add($"{typeof(T).Name} added to {path}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            GameObject saved = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (saved.GetComponent<T>() == null)
                    throw new InvalidOperationException($"[Pushables] verify failed: {path} lost its {typeof(T).Name} after saving.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(saved);
            }
        }

        private static void AuthorDrover(List<string> notes)
        {
            var drover = AssetDatabase.LoadAssetAtPath<ResidentArchetype>(DroverAsset);
            if (drover == null)
            {
                notes.Add($"no Drover archetype at {DroverAsset}");
                return;
            }

            var serialized = new SerializedObject(drover);
            SerializedFields.SetBool(serialized, "pushesCart", true);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(drover);
            notes.Add("Drover pushes a cart");
        }

        private static bool IsOpen(string path, List<string> notes)
        {
            if (PrefabStageUtility.GetCurrentPrefabStage()?.assetPath != path) return false;

            notes.Add($"skipped {path}: open in Prefab Mode");
            return true;
        }
    }
}
