// The Striders' walking houses: a prefab VARIANT of the hand-authored RigWalker, so the house on
// its deck and every leg stay the RigWalker's, with the player's helm taken out (a Strider house is
// nobody's to steer) and a crew added: six posts on the deck, a gangway at its feet, a CrewShift
// that puts the crew ashore at every stop and calls them back, and a ScoutRota that keeps two of
// the column's monowheel scouts out on a sweep.
//
// The RigWalker itself is untouched: players still pilot their own.
//
// Re-run from: Tools > SpaceGame > Vehicles > Build Strider Habitat Walker
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Core.Persistence.EditorTools;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public static class StriderCityBuilder
    {
        private const string RigWalkerPath = "Assets/Game/Prefabs/Agents/Vehicles/Ground/RigWalker.prefab";
        public const string HabitatPath = "Assets/Game/Prefabs/Agents/Vehicles/Ground/StriderHabitatWalker.prefab";

        private enum PostRow { Front, Side, Back }

        /// <summary>Where each crew post stands on the ring of open deck around the house: which side
        /// of the hull, and at the front corner, mid-side or back corner. Two at the front, two at the
        /// back, one down each side.</summary>
        private static readonly (bool Right, PostRow Row)[] PostLayout =
        {
            (false, PostRow.Front), (true, PostRow.Front),
            (false, PostRow.Back), (true, PostRow.Back),
            (false, PostRow.Side), (true, PostRow.Side),
        };

        /// <summary>One post per <see cref="PostLayout"/> entry; the city's crew is this per house.</summary>
        public static int CrewPosts => PostLayout.Length;

        /// <summary>The RigWalker's DesertCrawlerDriver.moveSpeed.</summary>
        public const float RigWalkerMoveSpeed = 6f;
        /// <summary>The leader walks at this fraction of its top speed so every follower -- the
        /// crawler at 3.2 m/s, the crab at 3.2 -- can keep up: LeggedDriver never exceeds 1x.</summary>
        public const float CityTravelMultiplier = 0.45f;
        public const float CityLeaderSpeed = RigWalkerMoveSpeed * CityTravelMultiplier;

        /// <summary>How far the city looks for its next Ruin or ScrapField. Salvage sites lie
        /// kilometres apart, and a city that only looked near itself would roam instead of stopping.</summary>
        public const float CitySearchRadius = 2500f;
        /// <summary>Headroom over the straight-line walk: the leader detours round ridges and ruins.</summary>
        private const float TravelTimeoutMargin = 1.5f;
        /// <summary>The walk to the furthest stop the search can pick (~930 s at the city's pace) plus
        /// the margin. NpcTaskModule's person-sized default of 240 s gives up after ~650 m, so the
        /// city would re-choose every four minutes and almost never reach a stop.</summary>
        public const float CityTravelTimeout = CitySearchRadius / CityLeaderSpeed * TravelTimeoutMargin;

        /// <summary>"Fairly flat" for the city: the steepest average grade across its footprint at a
        /// stop or at its start. The houses stand on their marching slots across the whole footprint
        /// and put their crew ashore at their feet, so this bounds how far one house stands above
        /// another and what the crew step off onto.</summary>
        public const float CityMaxSlopeDegrees = 3f;
        /// <summary>How far round a candidate stop the city looks for level ground before rejecting it:
        /// a slot row or so, so a stop moves off a dune face onto the pan beside it, not to a new place.</summary>
        public const float CityLevelSearchRadius = 200f;
        /// <summary>Two rings inside that radius: a coarser walk misses a shelf, a finer one costs
        /// NavMesh queries on the server for a choice made once every few minutes.</summary>
        public const float CityLevelSearchStep = 100f;
        /// <summary>Roam points tried per choice before the leader waits NpcTaskModule's retryDelay and
        /// tries again: the TryRoamPoint count, so a failed choice costs what an unfiltered one does.</summary>
        public const int CityLevelAttempts = 8;
        /// <summary>A footprint sample may find the NavMesh this far above or below the candidate: past
        /// the ~23 m of relief the slope limit accepts over the footprint, so acceptable ground is never
        /// missed. No wider: a NavMesh query's cost grows with its reach (measured 0.009 ms at 30 m,
        /// 0.04 ms at 60 m), and a choice makes up to ~1200 of them.</summary>
        public const float CityLevelSampleReach = 30f;
        /// <summary>A sample the NavMesh answers farther sideways than this landed in a hole or off a
        /// ledge; a rock narrower than twice this is walked round and still counts as ground.</summary>
        public const float CityLevelSampleTolerance = 10f;

        /// <summary>
        /// The ground the city needs level wherever it stands still: a square reaching its farthest
        /// carrier (<see cref="RosterAuthoring.CityFarthestCarrierSlot"/>, so every house's and barge's
        /// slot is on it whichever way the column arrived; the scouts behind them manage a slope).
        /// Written onto every city stop by <c>RosterAuthoring.WireStriderCity</c> and used by <see cref="StriderCityStartSite"/> for the start, so the city is held to one
        /// standard wherever it stands still.
        /// </summary>
        public static LevelGroundRule CityLevelGround => new LevelGroundRule
        {
            footprintRadius = RosterAuthoring.CityFarthestCarrierSlot,
            maxSlopeDegrees = CityMaxSlopeDegrees,
            searchRadius = CityLevelSearchRadius,
            searchStep = CityLevelSearchStep,
            attempts = CityLevelAttempts,
            sampleReach = CityLevelSampleReach,
            sampleTolerance = CityLevelSampleTolerance,
        };

        /// <summary>The scouts ride a ring this far round the city: well beyond the column's own
        /// perception, near enough to be back within minutes.</summary>
        public const float ScoutSweepRadius = 600f;
        public const int ScoutSweepPoints = 6;
        /// <summary>One closed loop at the monowheels' cruise speed (the speed the rota sweeps at) plus
        /// the same margin as the leader's walk (~450 s): the timeout is for a sweep that cannot
        /// finish, not one riding round a ridge.</summary>
        public static float ScoutSweepTimeout =>
            ScoutRotaLogic.LoopLength(ScoutSweepRadius, ScoutSweepPoints) / StriderMonowheelBuilder.CruiseSpeed * TravelTimeoutMargin;

        // Machine-sized formation tolerances (Spike findings: a 21 m hull on person-sized defaults
        // never settles into its slot).
        private const float RestRadius = 45f;
        private const float SlotTolerance = 8f;
        private const float RegroupDistance = 120f;
        private const float FormationNavSample = 20f;

        /// <summary>How far out from the hull's side the gangway is, clear of the legs' swing.</summary>
        private const float GangwayStandoff = 6f;
        /// <summary>Unseat reach from the gangway to NavMesh (VesselSeats default 6 is for a vessel's ramp).</summary>
        private const float GangwayNavMeshReach = 8f;
        private const string DeckColliderName = "COL_Deck";
        /// <summary>The house standing on the deck: posts go on the ring of deck around it.</summary>
        private const string HullColliderName = "COL_Hull";

        [MenuItem("Tools/SpaceGame/Vehicles/Build Strider Habitat Walker")]
        public static void BuildHabitat()
        {
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigWalkerPath);
            if (rig == null) { Debug.LogError($"[StriderCityBuilder] No RigWalker at {RigWalkerPath}."); return; }

            // Kept connected to the RigWalker: saving a connected instance to a new path is what
            // makes the result a variant rather than a copy.
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(rig);
            instance.transform.position = Vector3.zero;
            Physics.SyncTransforms();
            try
            {
                if (!RemoveHelm(instance) || !AddCrew(instance)) return;
                AddBrain(instance);
                PrefabUtility.SaveAsPrefabAsset(instance, HabitatPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            Debug.Log(NetworkPrefabRegistrar.Sync(out _, out _));
            if (!SaveableWiring.TryWirePrefabs())
                Debug.LogError("[StriderCityBuilder] Save wiring failed; run Tools > Save System > Wire Saveable Prefabs.");
            // Last write: the scratch instance lived in the open scene, which switched this off.
            NetworkObjectDefaults.KeepSceneMigrationSync(HabitatPath);
            Debug.Log($"[StriderCityBuilder] Built {HabitatPath}.");
        }

        /// Components that [RequireComponent(MountModule)] go first, or Unity refuses to remove it.
        private static bool RemoveHelm(GameObject root)
        {
            DestroyAll<SteerModule>(root);
            DestroyAll<MountSaveable>(root);
            DestroyAll<MountNetworkSync>(root);
            foreach (MountStation station in root.GetComponentsInChildren<MountStation>(true))
                Object.DestroyImmediate(station.gameObject);
            DestroyAll<MountModule>(root);
            DestroyAll<WanderSaveable>(root);   // requires WanderModule, so it goes first
            DestroyAll<WanderModule>(root);     // a stopped house must hold its slot, not stroll off

            if (root.GetComponent<MountModule>() == null && root.GetComponent<WanderModule>() == null) return true;
            Debug.LogError("[StriderCityBuilder] Could not remove the RigWalker's helm or WanderModule; see Spike findings.");
            return false;
        }

        private static void AddBrain(GameObject root)
        {
            EntityFactionWiring.Ensure(root, System.IO.Path.GetFileNameWithoutExtension(HabitatPath));

            // AddComponent does not run Reset, so every module's priority is set here.
            SerializedFields.Edit(root.AddComponent<NpcTaskModule>(), so =>
            {
                SerializedFields.SetInt(so, "priority", ModulePriority.Fallback);
                SerializedFields.SetFloat(so, "travelTimeout", CityTravelTimeout);
            });
            SerializedFields.Edit(root.AddComponent<GoalTravelModule>(), so => SerializedFields.SetInt(so, "priority", ModulePriority.Fallback + 1));

            SerializedFields.Edit(root.AddComponent<FormationModule>(), so =>
            {
                SerializedFields.SetInt(so, "priority", ModulePriority.Social);
                SerializedFields.SetString(so, "formationId", string.Empty);
                SerializedFields.SetFloat(so, "restRadius", RestRadius);
                SerializedFields.SetBool(so, "holdSlotAtRest", true);
                SerializedFields.SetFloat(so, "slotTolerance", SlotTolerance);
                SerializedFields.SetFloat(so, "regroupDistance", RegroupDistance);
                SerializedFields.SetFloat(so, "navSampleDistance", FormationNavSample);
            });

            // Every house carries one; only the house the column follows sends anyone out.
            SerializedFields.Edit(root.AddComponent<ScoutRota>(), so =>
            {
                SerializedFields.SetFloat(so, "sweepRadius", ScoutSweepRadius);
                SerializedFields.SetInt(so, "sweepPoints", ScoutSweepPoints);
                SerializedFields.SetFloat(so, "sweepTimeout", ScoutSweepTimeout);
            });
        }

        private static bool AddCrew(GameObject root)
        {
            if (!TryRootBounds(root, DeckColliderName, out Bounds deck) ||
                !TryRootBounds(root, HullColliderName, out Bounds hull))
                return false;

            // The house stands on the deck and fills most of it, so a post placed from the deck's
            // bounds alone ends up inside the walls. Posts stand mid-way across the ring of open
            // deck between the hull and the deck's edge, laid out by PostLayout.
            float top = deck.max.y;
            float left = (deck.min.x + hull.min.x) * 0.5f, right = (deck.max.x + hull.max.x) * 0.5f;
            float back = (deck.min.z + hull.min.z) * 0.5f, front = (deck.max.z + hull.max.z) * 0.5f;
            float side = hull.center.z;
            var centre = new Vector3(deck.center.x, top, deck.center.z);

            var spots = new Vector3[CrewPosts];
            for (int i = 0; i < CrewPosts; i++)
            {
                (bool onRight, PostRow row) = PostLayout[i];
                float z = row switch { PostRow.Front => front, PostRow.Back => back, _ => side };
                spots[i] = new Vector3(onRight ? right : left, top, z);
            }

            var gangway = new Vector3(deck.max.x + GangwayStandoff / root.transform.lossyScale.x, 0f, deck.center.z);
            CrewDeckWiring.AddCrewDeck(root, spots, centre, gangway, GangwayNavMeshReach);
            return true;
        }

        /// <summary>A named collider's world bounds in the root's own (scaled) space, so what is
        /// placed from them rides the walker wherever it is.</summary>
        private static bool TryRootBounds(GameObject root, string colliderName, out Bounds bounds)
        {
            bounds = default;
            Transform holder = FindDeep(root.transform, colliderName);
            if (holder == null || !holder.TryGetComponent(out Collider collider))
            {
                Debug.LogError($"[StriderCityBuilder] No {colliderName} collider on the RigWalker; crew posts cannot be placed.");
                return false;
            }

            Bounds world = collider.bounds;
            if (world.size.sqrMagnitude < 1f)
            {
                Debug.LogError($"[StriderCityBuilder] {colliderName} reports empty bounds {world}; is it disabled?");
                return false;
            }

            Vector3 min = root.transform.InverseTransformPoint(world.min);
            Vector3 max = root.transform.InverseTransformPoint(world.max);
            bounds = new Bounds();
            bounds.SetMinMax(Vector3.Min(min, max), Vector3.Max(min, max));
            return true;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static void DestroyAll<T>(GameObject root) where T : Component
        {
            foreach (T c in root.GetComponentsInChildren<T>(true)) Object.DestroyImmediate(c);
        }
    }
}
