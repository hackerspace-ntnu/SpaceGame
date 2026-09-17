// Builds the Sky Tribe's NPC transports: the skiff and the freighter that carry war parties to
// their quarry, land or hover, and set them down.
//
// Same models as the escorts parked round the Sky City (SkyFleetBuilder), and those stay scenery.
// These are separate prefabs because nothing about a static escort survives being flown: the escort
// is marked static (batched, baked into navigation), carries no NetworkObject and pivots on its
// middle. A transport pivots on its keel — VesselPilot sets that point down on the landing site —
// faces its prow along +Z so it flies forward, and is a server-authoritative network entity.
//
// Re-run from: Tools > SpaceGame > Vehicles > Build Sky Transports
//
// Prefab:
//   root   NetworkObject, NetRelay, NetAuthority, NetworkTransform (server authority, interpolated),
//          kinematic Rigidbody, EntityFaction (Sky), HealthComponent + NetworkedHealthComponent,
//          ChairPose, VesselSeats, VesselPilot
//   Model  the FBX, turned prow-forward and lifted so its lowest point is the root; the escort's
//          collider rules (solid, not triggers) and cull group
//   Seats  one marker per seat on the deck, facing outboard (skiff 4, freighter 8)
//   Ramp   astern, at keel height: where a landed party walks off
//   Drop   under the keel: where a hovering vessel drops its party
//
// Persistence: none. A transport and its passengers are rebuilt by the war party that launched them,
// and are spawned through NpcSpawn, which disowns them from the world save.
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public static class SkyVesselBuilder
    {
        private const string PrefabFolder = "Assets/Game/Prefabs/Vehicles/Sky";

        // How far astern of the hull's end the ramp foot is put, so a party walks off clear of it.
        private const float RampStandOff = 2f;

        // A seat marker must have deck within this far below it, or the model has changed under it.
        private const float DeckTolerance = 0.5f;

        public readonly struct Transport
        {
            public readonly string Name;
            /// <summary>The escort in <see cref="SkyFleetBuilder.Vessels"/> whose model this flies.</summary>
            public readonly string Escort;
            public readonly float FootprintRadius;
            public readonly int MaxHealth;
            /// <summary>Seat points on the deck, in the MODEL's own space (as exported, prow at −Z).</summary>
            public readonly Vector3[] Seats;

            public Transport(string name, string escort, float footprintRadius, int maxHealth, Vector3[] seats)
            {
                Name = name; Escort = escort; FootprintRadius = footprintRadius; MaxHealth = maxHealth; Seats = seats;
            }

            public string PrefabPath => $"{PrefabFolder}/{Name}.prefab";

            // Names the test cases that run once per transport.
            public override string ToString() => Name;
        }

        // Deck points measured by raycasting the models: the skiff's gondola catwalk is ~3.5 × 3 m
        // under its beacon and hab capsule; the freighter's two catwalks make a 3 × 18 m deck.
        // The skiff carries a small war party, the freighter a large one (RosterAuthoring.WireWorldSim).
        public static readonly Transport Skiff =
            new Transport("SkySkiffTransport", "SkySkiff", footprintRadius: 12f, maxHealth: 600, seats: new[]
            {
                new Vector3(-0.4f, -6.1f, -0.6f), new Vector3(1.9f, -6.1f, -0.6f),
                new Vector3(-0.4f, -6.1f, 1.4f), new Vector3(1.9f, -6.1f, 1.4f),
            });

        public static readonly Transport Freighter =
            new Transport("SkyFreighterTransport", "SkyFreighter", footprintRadius: 18f, maxHealth: 1200, seats: new[]
            {
                new Vector3(-1f, -5.3f, -8.5f), new Vector3(1.4f, -5.3f, -8.5f),
                new Vector3(-1f, -5.3f, -4f), new Vector3(1.4f, -5.3f, -4f),
                new Vector3(-1f, -5.3f, 0.5f), new Vector3(1.4f, -5.3f, 0.5f),
                new Vector3(-1f, -5.3f, 5f), new Vector3(1.4f, -5.3f, 5f),
            });

        public static readonly Transport[] Transports = { Skiff, Freighter };

        [MenuItem("Tools/SpaceGame/Vehicles/Build Sky Transports")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[SkyVesselBuilder] Refusing to build in Play mode.");
                return;
            }

            var report = new System.Text.StringBuilder("[SkyVesselBuilder]\n");
            StaticPropBuilder.EnsureFolder(PrefabFolder);

            foreach (Transport transport in Transports)
                report.AppendLine($"  {transport.Name}: {BuildTransport(transport)}");

            string[] paths = Transports.Select(t => t.PrefabPath).ToArray();
            foreach (string path in paths)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            report.AppendLine(NetworkPrefabRegistrar.Sync(out _, out _));

            // A NetworkObject created by script ships GlobalObjectIdHash 0 until OnValidate runs
            // against the saved asset; re-import and reserialize so the real hash reaches the YAML.
            AssetDatabase.ForceReserializeAssets(paths);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(report.ToString());
        }

        private static string BuildTransport(Transport transport)
        {
            SkyFleetBuilder.Vessel escort = SkyFleetBuilder.Vessels.First(v => v.Name == transport.Escort);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(escort.FbxPath);
            if (source == null)
                throw new System.InvalidOperationException($"No FBX at {escort.FbxPath}. Run sky_fleet_export.py first.");
            StaticPropBuilder.ConfigureImporter(escort.FbxPath);

            var root = new GameObject(transport.Name);
            try
            {
                // NetworkObject first: the NetworkBehaviours below look for it when added.
                root.AddComponent<NetworkObject>();

                Transform model = BuildModel(root.transform, source, out Bounds bounds, out StaticPropBuilder.FitCounts fits);
                Transform[] seats = BuildSeats(root.transform, model, transport.Seats);
                string deckProblems = VerifyDeck(model, seats);

                var ramp = new GameObject("Ramp").transform;
                ramp.SetParent(root.transform, false);
                ramp.localPosition = new Vector3(0f, 0f, bounds.min.z - RampStandOff);

                var drop = new GameObject("Drop").transform;
                drop.SetParent(root.transform, false);

                AddNetworking(root);
                AddBody(root);
                AddIdentity(root, transport.MaxHealth);
                AddCrew(root, seats, transport.FootprintRadius, ramp, drop);

                PrefabUtility.SaveAsPrefabAsset(root, transport.PrefabPath);
                return $"{seats.Length} seats, keel-to-top {bounds.size.y:F1} m, length {bounds.size.z:F1} m, " +
                       $"colliders {fits}{deckProblems}";
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The FBX under a Model child, turned so the prow (−Z as exported) points along the root's +Z
        /// and lifted so the lowest drawn point sits on the root. Returns the model's bounds in root space.
        /// </summary>
        private static Transform BuildModel(Transform root, GameObject source, out Bounds bounds,
                                            out StaticPropBuilder.FitCounts fits)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model";
            model.transform.SetParent(root, false);
            model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            bounds = SkyFleetBuilder.RendererBounds(model);
            model.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            bounds = SkyFleetBuilder.RendererBounds(model);

            fits = StaticPropBuilder.ApplyFits(model, SkyFleetBuilder.Rules);
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            StaticPropBuilder.BuildLodGroup(model, renderers, SkyFleetBuilder.LodCullRatio);
            return model.transform;
        }

        /// <summary>Seat markers on the deck, each facing outboard from the deck's centre line.</summary>
        private static Transform[] BuildSeats(Transform root, Transform model, Vector3[] deckPoints)
        {
            var parent = new GameObject("Seats").transform;
            parent.SetParent(root, false);

            float centreLine = deckPoints.Average(p => p.x);
            var seats = new Transform[deckPoints.Length];
            for (int i = 0; i < deckPoints.Length; i++)
            {
                Vector3 outboard = model.TransformDirection(new Vector3(Mathf.Sign(deckPoints[i].x - centreLine), 0f, 0f));
                seats[i] = new GameObject($"Seat_{i}").transform;
                seats[i].SetParent(parent, false);
                seats[i].SetPositionAndRotation(model.TransformPoint(deckPoints[i]),
                                                Quaternion.LookRotation(outboard, Vector3.up));
            }
            return seats;
        }

        /// <summary>Each seat must have the model's own deck just under it. Returns a report suffix.</summary>
        private static string VerifyDeck(Transform model, Transform[] seats)
        {
            Physics.SyncTransforms();
            Collider[] colliders = model.GetComponentsInChildren<Collider>(true);
            var floating = new List<string>();
            foreach (Transform seat in seats)
            {
                var ray = new Ray(seat.position + Vector3.up * DeckTolerance, Vector3.down);
                if (!colliders.Any(c => c.Raycast(ray, out _, DeckTolerance * 2f)))
                    floating.Add(seat.name);
            }

            if (floating.Count == 0) return string.Empty;
            Debug.LogError($"[SkyVesselBuilder] {model.parent.name}: no deck under {string.Join(", ", floating)}. " +
                           "The model changed; re-measure Transports' seat points.");
            return $" — NO DECK under {string.Join(", ", floating)}";
        }

        private static void AddNetworking(GameObject root)
        {
            // NetRelay carries the damage requests NetworkedHealthComponent answers.
            root.AddComponent<NetRelay>();
            root.AddComponent<NetAuthority>();

            // Server-authoritative: the pilot runs on the server and every client only watches.
            var netTransform = root.AddComponent<NetworkTransform>();
            netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            netTransform.SyncPositionX = true;
            netTransform.SyncPositionY = true;
            netTransform.SyncPositionZ = true;
            netTransform.SyncRotAngleX = true;
            netTransform.SyncRotAngleY = true;
            netTransform.SyncRotAngleZ = true;
            netTransform.SyncScaleX = false;
            netTransform.SyncScaleY = false;
            netTransform.SyncScaleZ = false;
            netTransform.InLocalSpace = false;
            netTransform.Interpolate = true;
        }

        private static void AddBody(GameObject root)
        {
            // Kinematic: the pilot places the hull; physics only needs it as something solid to hit.
            // No interpolation, because a body interpolated between physics steps would drag the
            // hull back behind where the pilot and the NetworkTransform put it.
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
        }

        private static void AddIdentity(GameObject root, int maxHealth)
        {
            var health = root.AddComponent<HealthComponent>();
            Apply(health, so =>
            {
                SerializedFields.SetInt(so, "maxHealth", maxHealth);
                SerializedFields.SetInt(so, "currentHealth", maxHealth);
            });
            root.AddComponent<NetworkedHealthComponent>();

            var faction = root.AddComponent<EntityFaction>();
            Apply(faction, so =>
            {
                SerializedFields.Set(so, "faction", Load<Object>(RosterAuthoring.SkyFactionPath));
                SerializedFields.Set(so, "relationshipTable", Load<Object>(RosterAuthoring.GlobalRelationshipsPath));
            });
        }

        private static void AddCrew(GameObject root, Transform[] seats, float footprintRadius, Transform ramp, Transform drop)
        {
            var chair = root.AddComponent<ChairPose>();

            var vesselSeats = root.AddComponent<VesselSeats>();
            Apply(vesselSeats, so =>
            {
                SerializedProperty list = so.FindProperty("seats");
                list.arraySize = seats.Length;
                for (int i = 0; i < seats.Length; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = seats[i];
                SerializedFields.Set(so, "chairPose", chair);
            });

            var pilot = root.AddComponent<VesselPilot>();
            Apply(pilot, so =>
            {
                SerializedFields.SetFloat(so, "footprintRadius", footprintRadius);
                SerializedFields.Set(so, "ramp", ramp);
                SerializedFields.Set(so, "drop", drop);
            });
        }

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new System.InvalidOperationException($"No asset at {path}.");
            return asset;
        }

        private static void Apply(Object target, System.Action<SerializedObject> edit)
        {
            var so = new SerializedObject(target);
            edit(so);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
