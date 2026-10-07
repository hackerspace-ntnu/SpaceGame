// The Mars colony's residents, authored in one idempotent pass: the SpotUse assets its rooms offer, the chores that connect
// them, ten archetypes, the ColonyCulture that holds them, the spots on every AstroDeco prop that brings a job or a place to sit,
// the beds in the bunks, and the dwelling each building becomes. Nothing here is colony-only code: it is data on the shared
// resident system, written by the same builder as the nomads'.
//
// Spots go on the PROPS (AstroDeco_*), never on a building, so all seven Colony_* buildings inherit them (the "jobs come with
// decorations" rule of Errands.md). A prop's spot stands in front of its collider box, looking at its middle; a wide prop gets
// two. A prop open in Prefab Mode is skipped and named.
//
// Run from: Tools > SpaceGame > Colony > Author Colony Residents
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;
using SpaceGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceGame.Agents.Residents.EditorTools
{
    public static partial class ResidentErrandContentBuilder
    {
        private const string ColonyCulturePath = "Assets/Game/ScriptableObjects/Residents/ColonyCulture.asset";
        private const string ColonyLinesPath = "Assets/Game/ScriptableObjects/Residents/Lines/ColonyLines.txt";
        private const string ColonyArchetypeDir = "Assets/Game/ScriptableObjects/Residents/Archetypes/Colony";
        private const string ColonyConfigPath = "Assets/Game/ScriptableObjects/Settlements/AstronautSettlement.asset";
        private const string AstronautDir = "Assets/Game/Prefabs/Environment/Decorations/Astronaut";
        private const string ColonyBuildingDir = "Assets/Game/Prefabs/Environment/Structures/AstronautSettlement";

        // Metres in front of a prop's box a resident stands: the NavMesh keeps half a metre off every solid, so a spot closer than that is
        // snapped; a work loop's reach (a strike lands about half a metre ahead) puts the tool on the prop from here.
        private const float ColonyStandOff = 0.9f;
        // How far apart two spots on one wide prop stand, and the width a prop needs to take two.
        private const float PairSpacing = 1.6f, PairWidth = 2.6f;
        // Where the two berths of a bunk lie along its length, so each bed's stand spot is plainly nearest its own seat; and how far the
        // berth sits toward the bunk's front, so the stand spot (the NavMesh edge is half a metre off the bunk) is within a seat's reach.
        private const float BerthOffset = 0.4f, BerthTowardFront = 0.5f, BedStandOff = 0.6f;
        // The mattress surfaces of the bunk (its manifest: bottom, top).
        private const float BottomBerthHeight = 1.10f, TopBerthHeight = 3.64f;
        private const float WanderFloorLift = 0.05f, MovedWithin = 0.01f;

        /// <summary>One kind of spot on one prop: its use, how many stand at it and the loop it holds when that is not its use's.</summary>
        private readonly struct PropSpot
        {
            public readonly string Prop, Use, Cue;
            public readonly int Count;

            public PropSpot(string prop, string use, int count = 1, string cue = null)
            {
                Prop = prop;
                Use = use;
                Count = count;
                Cue = cue;
            }
        }

        // Jobs, by prop. A use that no loop of the prop's own fits borrows a bare-handed one (wipe, rummage, operate): the astronaut's hand is
        // 1.7 times a human's and the tool grips were only fitted to the Raxy, so a colonist works with empty hands for now.
        private static readonly PropSpot[] ColonyJobs =
        {
            new("AstroDeco_LifeSupportUnit", "LifeSupport", 2),
            new("AstroDeco_MaintenancePanel", "LifeSupport"),
            new("AstroDeco_ServerRack", "LifeSupport", 1, "operate"),
            new("AstroDeco_ResearchTerminal", "Terminal", 2),
            new("AstroDeco_MonitorDesk", "Terminal"),
            new("AstroDeco_WallConsole", "Terminal"),
            new("AstroDeco_RockAnalysisStation", "RockAnalysis", 2),
            new("AstroDeco_ResearchMicroscope", "RockAnalysis"),
            new("AstroDeco_ResearchDesk", "RockAnalysis"),
            new("AstroDeco_RoverWorkStation", "RoverBay"),
            new("AstroDeco_RoverPartsTable", "RoverBay", 2),
            new("AstroDeco_RoverDiagnosticCart", "RoverBay"),
            new("AstroDeco_RoverJackStands", "RoverBay"),
            new("AstroDeco_HydroponicsRack", "Garden", 2),
            new("AstroDeco_PlantPots", "Garden"),
            new("AstroDeco_GalleyCounter", "Galley", 2),
            new("AstroDeco_MedBayCot", "MedBay"),
            new("AstroDeco_SampleFreezer", "MedBay", 1, "rummage"),
            // Errand stops: where a chore goes.
            new("AstroDeco_HydroponicsRack", "Plant"),
            new("AstroDeco_PlantPots", "Plant"),
            new("AstroDeco_WaterTank", "Well"),
            new("AstroDeco_WaterDispenser", "Well"),
            new("AstroDeco_SpecimenCase", "SampleStop"),
            new("AstroDeco_SampleFreezer", "SampleStop"),
            new("AstroDeco_DrillCoreRack", "SampleStop"),
            // Free time and the evening: a seat at a table, a stool at the projector, a place to stand and talk.
            new("AstroDeco_Table", "Table", 2),
        };

        // A seat's spot stands where the seat's sitter stands up (its standUpDistance), looking at the seat.
        private static readonly (string Prop, string Use)[] ColonySeatUses =
        {
            ("AstroDeco_Stool", "HearthSeat"), ("AstroDeco_Chair", "Seat"), ("AstroDeco_Bench", "Seat"),
        };

        private static readonly string[] ColonyNames =
        {
            "Ada", "Idris", "Mira", "Tobias", "Yuki", "Casimir", "Noor", "Halvard", "Selene", "Dmitri", "Ines", "Kofi", "Liv", "Rafael",
            "Anouk", "Bjorn", "Priya", "Teodor", "Wren", "Zane", "Elio", "Freya", "Gideon", "Hana", "Ivo", "Jun", "Kaia", "Lazar",
        };

        [MenuItem("Tools/SpaceGame/Colony/Author Colony Residents")]
        public static void RunColony()
        {
            var notes = new List<string>();
            AssetDatabase.StartAssetEditing();
            try
            {
                AuthorColonyAssets(notes);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            AuthorColonyProps(notes);
            AuthorColonyBuildings(notes);
            AssetDatabase.SaveAssets();
            Debug.Log("[Colony] residents authored:\n" + string.Join("\n", notes));
        }

        // ── assets ───────────────────────────────────────────────────────────────────────────────

        private static void AuthorColonyAssets(List<string> notes)
        {
            SpotUse lifeSupport = Spot("LifeSupport", "life support", SpotRole.Work, "wipe");
            SpotUse terminal = Spot("Terminal", "the terminal", SpotRole.Work, "operate");
            SpotUse rockAnalysis = Spot("RockAnalysis", "rock analysis", SpotRole.Work, "rummage");
            SpotUse roverBay = Spot("RoverBay", "the rover bay", SpotRole.Work, "wipe");
            SpotUse sampleStop = Spot("SampleStop", "the sample store", SpotRole.Errand, null);
            SpotUse wander = Spot("Wander", "the corridor", SpotRole.Errand, null);
            SpotUse bunk = Spot("Bunk", "the bunk", SpotRole.Leisure, null);
            bunk.seated = true;
            bunk.sleeps = true;
            Save(bunk);

            // The galley and the med bay are their own uses, not the nomad kitchen and infirmary: those hold loops that need a ladle or a
            // mortar, and a colonist works with empty hands.
            SpotUse galley = Spot("Galley", "the galley", SpotRole.Work, "wipe");
            SpotUse medBay = Spot("MedBay", "the med bay", SpotRole.Work, "tend");
            SpotUse garden = Require<SpotUse>($"{SpotDir}/Garden.asset");
            ChoreDefinition water = Require<ChoreDefinition>($"{ChoreDir}/Water.asset");
            ChoreDefinition carrySamples = Chore("CarrySamples", "carrying samples", sampleStop, sampleStop, null, new Vector2Int(1, 2), carryThroughout: false);

            ResidentArchetype engineer = ColonyArchetype("Engineer", "engineer", lifeSupport, TripKind.None, null, ResidentDuty.None, 0.5f, 0.4f);
            ResidentArchetype operatorArchetype = ColonyArchetype("Operator", "operator", terminal, TripKind.None, null, ResidentDuty.None, 0.4f, 0.3f);
            ResidentArchetype geologist = ColonyArchetype("Geologist", "geologist", rockAnalysis, TripKind.None, carrySamples, ResidentDuty.None, 0.5f, 0.4f);
            ResidentArchetype roverTech = ColonyArchetype("RoverTech", "rover tech", roverBay, TripKind.None, null, ResidentDuty.None, 0.6f, 0.5f);
            ResidentArchetype botanist = ColonyArchetype("Botanist", "botanist", garden, TripKind.None, water, ResidentDuty.None, 0.3f, 0.2f);
            ResidentArchetype chef = ColonyArchetype("Chef", "chef", galley, TripKind.None, null, ResidentDuty.None, 0.4f, 0.5f);
            ResidentArchetype medic = ColonyArchetype("Medic", "medic", medBay, TripKind.None, null, ResidentDuty.None, 0.5f, 0.2f);
            ResidentArchetype surveyor = ColonyArchetype("Surveyor", "surveyor", null, TripKind.Survey, null, ResidentDuty.None, 0.7f, 0.4f);
            ResidentArchetype evaGuard = ColonyArchetype("EvaGuard", "EVA guard", null, TripKind.None, null, ResidentDuty.Patrol, 0.9f, 0.7f);
            evaGuard.challengesArmed = true;
            Save(evaGuard);
            ResidentArchetype crew = ColonyArchetype("Crew", "crew", null, TripKind.None, null, ResidentDuty.None, 0.5f, 0.4f);
            ResidentArchetype[] archetypes = { engineer, operatorArchetype, geologist, roverTech, botanist, chef, medic, surveyor, evaGuard, crew };

            AuthorSurveyTrip(notes);
            AppendColonyLines(notes);

            var culture = Ensure<SettlementCulture>(ColonyCulturePath);
            culture.lines = AssetDatabase.LoadAssetAtPath<TextAsset>(ColonyLinesPath);
            culture.names = ColonyNames;
            culture.archetypes = archetypes;
            (culture.stationedShare, culture.roamerShare, culture.outriderShare, culture.patrolShare) = (0.62f, 0.2f, 0.1f, 0.1f);
            culture.ambleUse = wander;
            culture.freeTimeReachMinutes = 6f;
            var config = Require<SettlementConfig>(ColonyConfigPath);
            GameObject astronaut = config.characters.Length > 0 ? config.characters[0].prefab : null;
            culture.profiles = astronaut != null ? new[] { new CharacterProfile { prefab = astronaut, suits = archetypes } } : Array.Empty<CharacterProfile>();
            if (astronaut == null) notes.Add("the config has no character prefab: the culture has no profile");
            Save(culture);

            config.culture = culture;
            if (config.characters.Length > 0)
            {
                config.characters[0].count = ColonyPopulation;
                config.characters[0].spawnChance = 1f;
            }
            Save(config);
            notes.Add($"culture {culture.name}: {archetypes.Length} archetypes, {culture.names.Length} names; config now has residents");
        }

        // The Survey trip: out on the trip ring to read the land, with a look-about loop.
        private static void AuthorSurveyTrip(List<string> notes)
        {
            var tuning = Require<ResidentTuning>(TuningPath);
            int row = Array.FindIndex(tuning.tripKinds, r => r.kind == TripKind.Survey);
            if (row >= 0) return;

            // Appended, never reordered: the prop byte presence replicates is the row's index.
            tuning.tripKinds = tuning.tripKinds.Concat(new[]
            {
                new TripKindRow { kind = TripKind.Survey, cue = Require<CharacterCue>($"{CueDir}/bored.asset") },
            }).ToArray();
            Save(tuning);
            notes.Add("Survey trip row appended to the tuning (a look-about loop, no prop)");
        }

        // More than the beds can hold: Generate places one character per bed and leaves the rest out.
        private const int ColonyPopulation = 30;

        private static ResidentArchetype ColonyArchetype(string name, string role, SpotUse post, TripKind trips, ChoreDefinition chore,
                                                         ResidentDuty duty, float nerve, float temper)
        {
            ResidentArchetype archetype = Ensure<ResidentArchetype>($"{ColonyArchetypeDir}/{name}.asset");
            (archetype.roleName, archetype.post, archetype.trips, archetype.chore, archetype.duty) = (role, post, trips, chore, duty);
            (archetype.nerve, archetype.temper) = (nerve, temper);
            Save(archetype);
            return archetype;
        }

        private static void AppendColonyLines(List<string> notes)
        {
            if (!File.Exists(ColonyLinesPath)) throw new InvalidOperationException($"[Colony] {ColonyLinesPath} is missing: the culture needs its lines.");
            AssetDatabase.ImportAsset(ColonyLinesPath);
            notes.Add($"lines: {File.ReadAllLines(ColonyLinesPath).Length} rows");
        }

        // ── spots on props ───────────────────────────────────────────────────────────────────────

        private static void AuthorColonyProps(List<string> notes)
        {
            foreach (IGrouping<string, PropSpot> prop in ColonyJobs.GroupBy(j => j.Prop))
                EditProp(prop.Key, notes, (root, box) =>
                {
                    foreach (PropSpot job in prop)
                    {
                        SpotUse use = Require<SpotUse>($"{SpotDir}/{job.Use}.asset");
                        CharacterCue cue = job.Cue != null ? Require<CharacterCue>($"{CueDir}/{job.Cue}.asset") : null;
                        int count = job.Count == 1 || box.size.x < PairWidth ? 1 : job.Count;
                        for (int i = 0; i < count; i++)
                        {
                            float across = count == 1 ? 0f : (i - (count - 1) * 0.5f) * PairSpacing;
                            PutSpot(root, box, use, $"Spot_{job.Use}_{i + 1}", across, cue);
                        }
                    }
                });

            foreach ((string prop, string useName) in ColonySeatUses)
                EditProp(prop, notes, (root, box) =>
                {
                    SpotUse use = Require<SpotUse>($"{SpotDir}/{useName}.asset");
                    int n = 0;
                    foreach (Seat seat in root.GetComponentsInChildren<Seat>(true))
                        PutSeatSpot(root, seat, use, $"Spot_{useName}_{++n}");
                });

            EditProp("AstroDeco_Bunk", notes, (root, box) => PutBerths(root, box, Require<SpotUse>($"{SpotDir}/Bunk.asset")));
        }

        // Runs an edit on a prop prefab's contents and saves it only when it changed anything, reading the result back.
        private static void EditProp(string prop, List<string> notes, Action<GameObject, BoxCollider> edit)
        {
            string path = $"{AstronautDir}/{prop}.prefab";
            if (!File.Exists(path)) { notes.Add($"missing prop {path}"); return; }
            if (PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == path) { notes.Add($"skipped {prop}: open in Prefab Mode"); return; }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                BoxCollider box = root.GetComponentInChildren<BoxCollider>(true);
                if (box == null) { notes.Add($"{prop}: no collider box to stand beside"); return; }

                ClearColonySpots(root);
                edit(root, box);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                notes.Add($"{prop}: {root.GetComponentsInChildren<SettlementSpot>(true).Length} spot(s)");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // What an earlier run put on a prop is taken off first, so a changed table of spots replaces rather than adds to it.
        private static void ClearColonySpots(GameObject root)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true).Reverse())
                if (child != root.transform && (child.name.StartsWith("Spot_") || child.name.StartsWith("Face_") || child.name.StartsWith("Seat_Berth_")))
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
        }

        // The prop's front is +Z (its collider box's far side from the wall it backs onto); the spot stands ColonyStandOff beyond it.
        private static void PutSpot(GameObject root, BoxCollider box, SpotUse use, string name, float across, CharacterCue cue)
        {
            if (root.transform.Find(name) != null) return;

            Transform boxTransform = box.transform;
            Vector3 centre = boxTransform.TransformPoint(box.center);
            Vector3 size = Vector3.Scale(box.size, boxTransform.lossyScale);
            Vector3 stand = new(centre.x + across, root.transform.position.y, centre.z + size.z * 0.5f + ColonyStandOff);
            AddSpotObject(root, use, name, stand, new Vector3(centre.x + across, Mathf.Min(centre.y, EyeHeight * 2f), centre.z), cue);
        }

        private static void PutSeatSpot(GameObject root, Seat seat, SpotUse use, string name)
        {
            if (root.transform.Find(name) != null) return;

            Vector3 feet = seat.FeetPosition;
            Vector3 forward = Vector3.ProjectOnPlane(seat.SitPoint.forward, Vector3.up).normalized;
            AddSpotObject(root, use, name, feet + forward * SeatStandUp(seat), seat.SitPosition, null);
        }

        // The seat's own standUpDistance, read through its serialized field.
        private static float SeatStandUp(Seat seat) => new SerializedObject(seat).FindProperty("standUpDistance").floatValue;

        // A bunk: two berths a body-length apart in height, each with a Lie seat and a bed spot in front of it.
        private static void PutBerths(GameObject root, BoxCollider box, SpotUse bed)
        {
            Vector3 centre = box.transform.TransformPoint(box.center);
            float front = centre.z + box.size.z * 0.5f;
            float[] heights = { BottomBerthHeight, TopBerthHeight };
            for (int i = 0; i < heights.Length; i++)
            {
                float along = (i == 0 ? -1f : 1f) * BerthOffset;
                var seatObject = new GameObject($"Seat_Berth_{i + 1}").transform;
                seatObject.SetParent(root.transform, false);
                seatObject.localPosition = new Vector3(centre.x + along, root.transform.position.y + heights[i], centre.z + BerthTowardFront);
                // The sleeper lies along the bunk's length, head toward +X.
                seatObject.localRotation = Quaternion.LookRotation(Vector3.right);
                var trigger = seatObject.gameObject.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.size = new Vector3(0.6f, 0.4f, 3f);

                var so = new SerializedObject(seatObject.gameObject.AddComponent<Seat>());
                so.FindProperty("pose").enumValueIndex = (int)SeatPose.Lie;
                // The sleeper's root is on the mattress: no drop to a floor below.
                so.FindProperty("footDrop").floatValue = 0f;
                so.ApplyModifiedPropertiesWithoutUndo();

                Vector3 stand = new(centre.x + along, root.transform.position.y, front + BedStandOff);
                AddSpotObject(root, bed, $"Spot_Bed_{i + 1}", stand, new Vector3(centre.x + along, centre.y, centre.z), null);
            }
        }

        // Also how the outpost builder puts a spot on a prefab, so a spot is made one way.
        internal static void AddSpotObject(GameObject root, SpotUse use, string name, Vector3 stand, Vector3 look, CharacterCue cue)
        {
            var face = new GameObject($"Face_{name.Substring("Spot_".Length)}").transform;
            face.SetParent(root.transform, false);
            face.position = look;

            var spot = new GameObject(name).transform;
            spot.SetParent(root.transform, false);
            spot.position = stand;
            Vector3 toward = Vector3.ProjectOnPlane(look - stand, Vector3.up);
            if (toward.sqrMagnitude > Mathf.Epsilon) spot.rotation = Quaternion.LookRotation(toward);

            var so = new SerializedObject(spot.gameObject.AddComponent<SettlementSpot>());
            so.FindProperty("use").objectReferenceValue = use;
            so.FindProperty("face").objectReferenceValue = face;
            if (cue != null) so.FindProperty("stationCue").objectReferenceValue = cue;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── buildings: a dwelling per building with bunks, an entrance, wander spots ─────────────

        private static void AuthorColonyBuildings(List<string> notes)
        {
            foreach (string path in AssetDatabase.FindAssets("t:Prefab Colony_", new[] { ColonyBuildingDir }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
            {
                if (PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == path) { notes.Add($"skipped {path}: open in Prefab Mode"); continue; }

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    string result = AuthorBuilding(root);
                    if (result == null) continue;

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    notes.Add($"{root.name}: {result}");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        private static string AuthorBuilding(GameObject root)
        {
            Physics.SyncTransforms();   // the airlock zones' bounds are read below
            SpotUse wander = Require<SpotUse>($"{SpotDir}/Wander.asset");
            var changes = new List<string>();

            int berths = root.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("Bunk_") &&
                                                                                  PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) * 2;
            Dwelling dwelling = root.GetComponent<Dwelling>();
            if (berths > 0)
            {
                if (dwelling == null) { dwelling = root.AddComponent<Dwelling>(); changes.Add("dwelling"); }
                var so = new SerializedObject(dwelling);
                if (so.FindProperty("beds").intValue != berths)
                {
                    so.FindProperty("beds").intValue = berths;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    changes.Add($"{berths} beds");
                }
                if (AddHallEntrance(root)) changes.Add("hall entrance");
            }

            int added = AddWanderSpots(root, wander);
            if (added > 0) changes.Add($"{added} wander spots");
            return changes.Count == 0 ? null : string.Join(", ", changes);
        }

        // Where a resident waits when it has nothing booked: the room-side stand of the building's first airlock, looking into the room. A
        // dwelling's door is its first SettlementEntrance, and the colony has no door to go through to bed, so this stands in for one.
        private static bool AddHallEntrance(GameObject root)
        {
            AirlockPassage passage = root.GetComponentInChildren<AirlockPassage>(true);
            if (passage == null) return false;

            passage.Ends(out Vector3 outside, out Vector3 inside);
            Vector3 inward = Vector3.ProjectOnPlane(inside - outside, Vector3.up).normalized;
            Quaternion facing = Quaternion.LookRotation(inward);

            Transform hall = root.transform.Find("Hall");
            bool created = hall == null;
            if (created)
            {
                hall = new GameObject("Hall").transform;
                hall.SetParent(root.transform, false);
                hall.gameObject.AddComponent<SettlementEntrance>();
            }

            bool moved = Vector3.Distance(hall.position, inside) > MovedWithin || Quaternion.Angle(hall.rotation, facing) > 1f;
            hall.SetPositionAndRotation(inside, facing);
            return created || moved;
        }

        // A free point at the middle of every room, hub and tube (where its air volume is), for ambles to wander between.
        private static int AddWanderSpots(GameObject root, SpotUse wander)
        {
            Transform interior = root.transform.Find("Interior");
            if (interior == null) return 0;

            int added = 0;
            foreach (BreathableVolume volume in interior.GetComponentsInChildren<BreathableVolume>(true))
            {
                string name = $"Wander_{volume.transform.parent.name}_{volume.name}";
                if (root.transform.Find(name) != null) continue;

                BoxCollider area = volume.GetComponent<BoxCollider>();
                if (area == null) continue;

                Vector3 centre = area.transform.TransformPoint(area.center);
                float floor = centre.y - area.size.y * 0.5f * Mathf.Abs(area.transform.lossyScale.y);
                var spot = new GameObject(name).transform;
                spot.SetParent(root.transform, false);
                spot.position = new Vector3(centre.x, floor + WanderFloorLift, centre.z);
                var so = new SerializedObject(spot.gameObject.AddComponent<SettlementSpot>());
                so.FindProperty("use").objectReferenceValue = wander;
                so.ApplyModifiedPropertiesWithoutUndo();
                added++;
            }
            return added;
        }
    }
}
