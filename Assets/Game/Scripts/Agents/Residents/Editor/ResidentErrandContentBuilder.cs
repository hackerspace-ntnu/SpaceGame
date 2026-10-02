// Authors the content the errand layer runs on, idempotently: the errand SpotUse assets (well, plant, ore pile,
// smelter, goods pile) and the tower post, the chores that connect them, the archetypes that keep them (gardeners
// water, apprentices haul ore, haulers carry goods between shops and houses, guards patrol, tower guards stand
// watch), the hand tools the chores carry, and the spots on the decoration prefabs that offer them. Re-running
// changes nothing that is already right; every write is read back and any mismatch is an error.
//
// Spots go on the DECORATION prefabs only (a well, planter beds, ore piles, the smelter, market stalls and crates,
// the three watchtowers) — they take effect where a settlement's config lists those decorations. Each spot stands
// just in front of the prefab's bounds, looking at its middle; a prefab open in Prefab Mode is skipped and named.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceGame.Items;
using SpaceGame.Presentation;
using SpaceGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceGame.Agents.Residents.EditorTools
{
    public static partial class ResidentErrandContentBuilder
    {
        private const string SpotDir = "Assets/Game/ScriptableObjects/Settlements/Spots";
        private const string ChoreDir = "Assets/Game/ScriptableObjects/Residents/Chores";
        private const string ArchetypeDir = "Assets/Game/ScriptableObjects/Residents/Archetypes";
        private const string CulturePath = "Assets/Game/ScriptableObjects/Residents/NomadCulture.asset";
        private const string TuningPath = "Assets/Game/Resources/Residents/ResidentTuning.asset";
        private const string LinesPath = "Assets/Game/ScriptableObjects/Residents/Lines/NomadLines.txt";
        private const string CueDir = "Assets/Game/ScriptableObjects/Animation/Cues";
        private const string ToolDir = "Assets/Game/Resources/Items/Tools";
        private const string DecorationDir = "Assets/Game/Prefabs/Environment/Decorations";
        // How far in front of a prefab's bounds a resident stands, and how far in from the ladder's exit on a deck.
        private const float StandOff = 1.1f, DeckInset = 0.8f, EyeHeight = 1f;

        private static readonly string[] PlantPrefabs =
        {
            "Agriculture/Deco_PlanterBed_Round", "Agriculture/Deco_PlanterBed_Bulbs", "Agriculture/Deco_PlanterBed_Spikes",
            "Nature/Deco_PlantPot_Bulb", "Nature/Deco_PlantPot_Fern", "Nature/Deco_PlantPot_Spike",
            "Farming/Deco_FarmBed_Grain", "Farming/Deco_FarmBed_Leafy", "Farming/Deco_FarmBed_Roots",
            "Farming/Deco_FarmBed_Seedlings", "Farming/Deco_FarmBed_Tubers", "Farming/Deco_FarmBed_Vines",
        };

        private static readonly string[] GoodsPrefabs =
        {
            "Storage/Deco_MarketStall_Bare", "Storage/Deco_MarketStall_Cloth", "Storage/Deco_MarketStall_Produce",
            "Workshop/Deco_Crate_Wood", "Workshop/Deco_Crate_WoodStack", "Workshop/Deco_Crate_Metal", "Workshop/Deco_Crate_MetalStack",
            "Farming/Deco_HarvestCrates", "Mining/Deco_MiningToolCrate", "Transport/Deco_Handcart",
        };

        private static readonly string[] TowerPrefabs =
        {
            "Watchtowers/Deco_Watchtower_Wood", "Watchtowers/Deco_Watchtower_MetalScaffold", "Watchtowers/Deco_Watchtower_MetalLattice",
        };

        [MenuItem("Tools/SpaceGame/Residents/Author Errand Content")]
        public static void Run()
        {
            var notes = new List<string>();
            SpotUse well = Spot("Well", "the well", SpotRole.Errand, "pickup");
            SpotUse plant = Spot("Plant", "the plants", SpotRole.Errand, "putdown");
            SpotUse orePile = Spot("OrePile", "the ore pile", SpotRole.Errand, "pickup");
            SpotUse smelter = Spot("Smelter", "the smelter", SpotRole.Errand, "putdown");
            SpotUse goods = Spot("GoodsPile", "the goods store", SpotRole.Errand, "pickup");
            SpotUse tower = Spot("TowerWatch", "the tower", SpotRole.Work, null, alwaysManned: true, nightManned: true, elevated: true);

            InventoryItem bucket = Tool("Carry_Bucket_Wood"), basket = Tool("Carry_Basket_Wicker"), ore = Tool("Carry_Basket_Open");
            ChoreDefinition water = Chore("Water", "watering", well, plant, bucket, new Vector2Int(2, 4), carryThroughout: true);
            ChoreDefinition haulOre = Chore("HaulOre", "hauling ore", orePile, smelter, ore, new Vector2Int(1, 1), carryThroughout: false);
            ChoreDefinition haulGoods = Chore("HaulGoods", "carrying goods", goods, goods, basket, new Vector2Int(1, 2), carryThroughout: false);

            ResidentArchetype guard = Archetype("Guard");
            guard.post = null;
            guard.duty = ResidentDuty.Patrol;
            guard.challengesArmed = true;
            (guard.nerve, guard.temper) = (0.9f, 0.8f);
            Save(guard);

            ResidentArchetype towerGuard = Archetype("TowerGuard");
            EditorUtility.CopySerialized(guard, towerGuard);
            towerGuard.name = "TowerGuard";
            (towerGuard.roleName, towerGuard.post, towerGuard.duty, towerGuard.challengesArmed) = ("tower guard", tower, ResidentDuty.None, true);
            (towerGuard.nerve, towerGuard.temper) = (0.8f, 0.6f);
            Save(towerGuard);

            SetChore("Gardener", water);
            SetChore("Waterkeeper", water);
            SetChore("Apprentice", haulOre);
            ResidentArchetype hauler = Roamer("Hauler", "hauler", haulGoods, nerve: 0.4f, temper: 0.3f);
            ResidentArchetype oreCarrier = Roamer("OreCarrier", "ore carrier", haulOre, nerve: 0.5f, temper: 0.5f);

            RegisterInCulture(hauler, oreCarrier, towerGuard);
            RegisterCarryItems(bucket, basket, ore);
            AppendLines(notes);

            foreach (string path in PlantPrefabs) AddFrontSpot(path, plant, notes);
            AddFrontSpot("Water/Deco_WaterWell_Windlass", well, notes);
            foreach (string path in new[] { "Mining/Deco_OrePile_Crystal", "Mining/Deco_OrePile_Rust" }) AddFrontSpot(path, orePile, notes);
            foreach (string path in new[] { "Mining/Deco_Smelter", "Work/Deco_Forge", "Work/Deco_Kiln" }) AddFrontSpot(path, smelter, notes);
            foreach (string path in GoodsPrefabs) AddFrontSpot(path, goods, notes);
            foreach (string path in TowerPrefabs) AddDeckSpot(path, tower, notes);

            AuthorSettlementWork(goods, plant, orePile, smelter, haulGoods, haulOre, notes);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Verify(water, haulOre, haulGoods, notes);
            Debug.Log("[Residents] Errand content authored:\n" + string.Join("\n", notes));
        }

        // ── assets ───────────────────────────────────────────────────────────────────────────────

        private static SpotUse Spot(string name, string display, SpotRole role, string cue, bool alwaysManned = false,
                                    bool nightManned = false, bool elevated = false)
        {
            SpotUse use = Ensure<SpotUse>($"{SpotDir}/{name}.asset");
            use.displayName = display;
            use.role = role;
            use.holdCue = cue != null ? Require<CharacterCue>($"{CueDir}/{cue}.asset") : null;
            (use.alwaysManned, use.nightManned, use.elevated) = (alwaysManned, nightManned, elevated);
            Save(use);
            return use;
        }

        private static ChoreDefinition Chore(string name, string display, SpotUse source, SpotUse target, InventoryItem carried,
                                             Vector2Int perRound, bool carryThroughout)
        {
            ChoreDefinition chore = Ensure<ChoreDefinition>($"{ChoreDir}/{name}.asset");
            (chore.displayName, chore.source, chore.target, chore.carried) = (display, source, target, carried);
            (chore.targetsPerRound, chore.carryThroughout) = (perRound, carryThroughout);
            Save(chore);
            return chore;
        }

        private static ResidentArchetype Archetype(string name) => Ensure<ResidentArchetype>($"{ArchetypeDir}/{name}.asset");

        private static ResidentArchetype Roamer(string name, string role, ChoreDefinition chore, float nerve, float temper)
        {
            ResidentArchetype archetype = Archetype(name);
            (archetype.roleName, archetype.post, archetype.trips, archetype.chore) = (role, null, TripKind.None, chore);
            (archetype.nerve, archetype.temper) = (nerve, temper);
            Save(archetype);
            return archetype;
        }

        private static void SetChore(string archetypeName, ChoreDefinition chore)
        {
            ResidentArchetype archetype = Require<ResidentArchetype>($"{ArchetypeDir}/{archetypeName}.asset");
            archetype.chore = chore;
            Save(archetype);
        }

        private static void RegisterInCulture(params ResidentArchetype[] added)
        {
            var culture = Require<SettlementCulture>(CulturePath);
            culture.archetypes = culture.archetypes.Concat(added.Where(a => !culture.archetypes.Contains(a))).ToArray();
            Save(culture);
        }

        // Appended, never reordered: the index travels on the wire.
        private static void RegisterCarryItems(params InventoryItem[] items)
        {
            var tuning = Require<ResidentTuning>(TuningPath);
            tuning.carryItems = tuning.carryItems.Concat(items.Where(i => !tuning.carryItems.Contains(i))).ToArray();
            Save(tuning);
        }

        private static InventoryItem Tool(string name) => Require<InventoryItem>($"{ToolDir}/{name}.asset");

        private static T Ensure<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var made = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(made, path);
            return made;
        }

        private static T Require<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException($"[Residents] {path} is missing — the errand content needs it.");
            return asset;
        }

        private static void Save(UnityEngine.Object asset) => EditorUtility.SetDirty(asset);

        // ── lines ────────────────────────────────────────────────────────────────────────────────

        // Keys already in the table are left alone, so the file can be edited by hand afterwards.
        private static void AppendLines(List<string> notes)
        {
            string text = File.ReadAllText(LinesPath);
            var present = new HashSet<string>(text.Split('\n').Select(l => l.Split('\t')[0]));
            var rows = Lines().Where(r => !present.Contains(r.Split('\t')[0])).ToList();
            if (rows.Count == 0) return;

            File.AppendAllText(LinesPath, (text.EndsWith("\n") ? "" : "\n") + string.Join("\n", rows) + "\n");
            notes.Add($"{rows.Count} line rows appended to {LinesPath}");
        }

        private static IEnumerable<string> Lines()
        {
            // key, speaker, topic, stance, observation, activity, repliesTo, register, text — tab-separated.
            string Row(string key, string speaker, string topic, string stance, string observation, string activity,
                       string repliesTo, string register, string text) =>
                string.Join("\t", key, speaker, topic, stance, observation, activity, repliesTo, register, text);

            yield return "# -- Guards on patrol: always pairs, always about the peace --";
            yield return Row("guard.patrol.1", "Guard", "Work", "", "", "Patrol", "", "Statement", "Round the wall again. Nobody gets in unseen.");
            yield return Row("guard.patrol.2", "Guard", "Work", "", "", "Patrol", "", "Statement", "Keeping the peace is mostly walking.");
            yield return Row("guard.patrol.3", "Guard", "Work", "", "", "Patrol", "", "Question", "Hear that? Probably the wind. Probably.");
            yield return Row("guard.patrol.4", "Guard", "Gossip", "", "", "Patrol", "", "Statement", "Raiders were seen past the ridge. We walk the wall twice tonight.");
            yield return Row("guard.patrol.5", "Guard", "Gossip", "", "", "Patrol", "", "Statement", "Nobody here sleeps badly because of us. That is the job.");
            yield return Row("guard.patrol.6", "Guard", "Ambition", "", "", "Patrol", "", "Statement", "One night where the worst thing I chase off is a stray goat.");
            yield return Row("guard.patrol.7", "Guard", "Work", "", "", "Patrol", "", "Statement", "We protect this place. Everything else is detail.");
            yield return Row("guard.patrol.reply.q", "Guard", "Reply", "", "", "Patrol", "Question", "Statement", "Quiet so far. Keep it that way.");
            yield return Row("guard.patrol.reply.s", "Guard", "Reply", "", "", "Patrol", "Statement", "Statement", "Aye. Eyes open.");
            yield return Row("guard.patrol.reply.c", "Guard", "Reply", "", "", "Patrol", "Complaint", "Statement", "Better sore feet than a breach.");
            yield return Row("guard.patrol.reply.e", "Guard", "Reply", "", "", "Patrol", "Exclamation", "Statement", "Then we are doing it right.");
            yield return Row("guard.challenge.armed", "Guard", "Warning", "wary", "ArmedHeld", "", "", "Exclamation", "Lower that. I will not ask twice.");
            yield return Row("guard.challenge.sprint", "Guard", "Warning", "wary", "Sprinting", "", "", "Exclamation", "Stop running. Walk, or explain yourself.");
            yield return Row("guard.challenge.halt", "Guard", "Warning", "wary", "", "", "", "Question", "Halt. State your business.");
            yield return Row("guard.standdown", "Guard", "Warning", "Cold", "ArmedHeld", "", "", "Exclamation", "Stand down. This is your last warning.");

            yield return "# -- Tower guards: the view from up there --";
            yield return Row("tower.work.1", "TowerGuard", "Work", "", "", "Work", "", "Statement", "I can see the whole valley from here. Nothing moves that I don't know.");
            yield return Row("tower.work.2", "TowerGuard", "Work", "", "", "Work", "", "Statement", "Long watch. Good wind up here, though.");
            yield return Row("tower.greet", "TowerGuard", "Greeting", "", "", "", "", "Statement", "Keep to the paths. I am watching.");
            yield return Row("tower.armed", "TowerGuard", "Remark", "", "ArmedHeld", "", "", "Statement", "Weapons down. My eyes are on you.");
            yield return Row("tower.warn", "TowerGuard", "Warning", "Cold", "", "", "", "Exclamation", "Step back. The wall's archers are listening.");

            yield return "# -- Chores: what the work is, said at the work --";
            yield return Row("chore.water.1", "Gardener", "Work", "", "", "Chore", "", "Statement", "Every bed gets a full bucket. No shortcuts.");
            yield return Row("chore.water.2", "Waterkeeper", "Work", "", "", "Chore", "", "Statement", "The well was low this morning. I carry what I can.");
            yield return Row("chore.water.3", "Gardener", "Gossip", "", "", "Chore", "", "Statement", "The spiked ones drink the most. Stubborn things.");
            yield return Row("chore.ore.1", "Apprentice", "Work", "", "", "Chore", "", "Complaint", "Ore from the pile to the smelter. Again. My back.");
            yield return Row("chore.ore.2", "OreCarrier", "Work", "", "", "Chore", "", "Statement", "Heavy load. The smith pays for heavy loads.");
            yield return Row("chore.goods.1", "Hauler", "Work", "", "", "Chore", "", "Statement", "Goods from the store to the stalls. Somebody has to.");
            yield return Row("chore.goods.2", "Hauler", "Gossip", "", "", "Chore", "", "Statement", "Half of what I carry never reaches a customer. It reaches a neighbour.");
            yield return Row("chore.reply.q", "", "Reply", "", "", "Chore", "Question", "Statement", "Busy. Ask me later.");
            yield return Row("chore.reply.s", "", "Reply", "", "", "Chore", "Statement", "Statement", "Mm. Work does not carry itself.");

            yield return "# -- Wandering: people out and about --";
            yield return Row("amble.work.1", "", "Work", "", "", "Amble", "", "Statement", "Just stretching my legs before the afternoon.");
            yield return Row("amble.work.2", "", "Work", "", "", "Amble", "", "Question", "Is the market busy today?");
            yield return Row("amble.gossip.1", "", "Gossip", "", "", "Amble", "", "Statement", "Did you see the new wares at the stalls?");
            yield return Row("amble.gossip.2", "", "Gossip", "", "", "Amble", "", "Statement", "Somebody has been leaving the gate open. I would not say who.");
            yield return Row("amble.ambition.1", "", "Ambition", "", "", "Amble", "", "Statement", "One day I will walk further than the wall.");
            yield return Row("amble.reply.q", "", "Reply", "", "", "Amble", "Question", "Statement", "Busy enough. Come along, I'll show you.");
            yield return Row("amble.reply.s", "", "Reply", "", "", "Amble", "Statement", "Statement", "Is that so. Well, walk with me.");
            yield return Row("amble.reply.c", "", "Reply", "", "", "Amble", "Complaint", "Statement", "It always is. Come, the walk helps.");
        }

        // ── spots on prefabs ─────────────────────────────────────────────────────────────────────

        private static void AddFrontSpot(string relativePath, SpotUse use, List<string> notes) =>
            AddSideSpot(relativePath, use, Side.Front, notes);

        private enum Side { Front, Back, Left, Right }

        // Just outside the prefab's bounds on one side, looking at its middle: several uses share one prefab by side.
        private static void AddSideSpot(string relativePath, SpotUse use, Side side, List<string> notes) =>
            AddSpot(relativePath, use, notes, (root, bounds) =>
            {
                Vector3 c = bounds.center;
                Vector3 floor = side switch
                {
                    Side.Front => new Vector3(c.x, bounds.min.y, bounds.max.z + StandOff),
                    Side.Back => new Vector3(c.x, bounds.min.y, bounds.min.z - StandOff),
                    Side.Left => new Vector3(bounds.min.x - StandOff, bounds.min.y, c.z),
                    _ => new Vector3(bounds.max.x + StandOff, bounds.min.y, c.z),
                };
                return (floor, new Vector3(c.x, bounds.min.y + EyeHeight, c.z));
            });

        // The deck: just inside the ladder's exit, looking out over the rail.
        private static void AddDeckSpot(string relativePath, SpotUse use, List<string> notes) =>
            AddSpot(relativePath, use, notes, (root, bounds) =>
            {
                Transform exit = root.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Exit");
                if (exit == null) return (null, default);

                Vector3 centre = new Vector3(bounds.center.x, exit.position.y, bounds.center.z);
                Vector3 inward = Vector3.ProjectOnPlane(centre - exit.position, Vector3.up).normalized;
                Vector3 stand = exit.position + inward * DeckInset;
                return (stand, stand - inward * 4f + Vector3.up * EyeHeight);
            });

        private static void AddSpot(string relativePath, SpotUse use, List<string> notes,
                                    Func<GameObject, Bounds, (Vector3? stand, Vector3 look)> place)
        {
            string path = $"{DecorationDir}/{relativePath}.prefab";
            if (!File.Exists(path)) { notes.Add($"missing prefab {path}"); return; }
            if (PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == path) { notes.Add($"skipped {relativePath}: open in Prefab Mode"); return; }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponentsInChildren<SettlementSpot>(true).Any(s => s.Use == use)) return;

                Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) { notes.Add($"{relativePath}: no renderers to stand beside"); return; }

                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
                (Vector3? stand, Vector3 look) = place(root, bounds);
                if (stand == null) { notes.Add($"{relativePath}: no ladder exit to put a post beside"); return; }

                var face = new GameObject($"Face_{use.name}").transform;
                face.SetParent(root.transform, true);
                face.position = look;
                var spot = new GameObject($"Spot_{use.name}").transform;
                spot.SetParent(root.transform, true);
                spot.position = stand.Value;
                Vector3 toward = Vector3.ProjectOnPlane(look - stand.Value, Vector3.up);
                if (toward.sqrMagnitude > Mathf.Epsilon) spot.rotation = Quaternion.LookRotation(toward);

                var so = new SerializedObject(spot.gameObject.AddComponent<SettlementSpot>());
                so.FindProperty("use").objectReferenceValue = use;
                so.FindProperty("face").objectReferenceValue = face;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                notes.Add($"{use.name} spot added to {relativePath}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ── read it back ─────────────────────────────────────────────────────────────────────────

        private static void Verify(ChoreDefinition water, ChoreDefinition haulOre, ChoreDefinition haulGoods, List<string> notes)
        {
            var tuning = Require<ResidentTuning>(TuningPath);
            foreach (ChoreDefinition chore in new[] { water, haulOre, haulGoods })
            {
                var reloaded = Require<ChoreDefinition>(AssetDatabase.GetAssetPath(chore));
                if (!reloaded.IsComplete) Fail($"chore {chore.name} lost an end on save");
                if (tuning.PropIndexOf(reloaded.carried) == 0) Fail($"chore {chore.name} carries {reloaded.carried} which the tuning does not list");
            }

            var culture = Require<SettlementCulture>(CulturePath);
            foreach (string name in new[] { "Hauler", "OreCarrier", "TowerGuard", "Guard" })
                if (!culture.archetypes.Any(a => a != null && a.name == name)) Fail($"{name} is not in the culture");

            var guard = Require<ResidentArchetype>($"{ArchetypeDir}/Guard.asset");
            if (guard.duty != ResidentDuty.Patrol || guard.post != null) Fail("Guard did not become a patrol");

            var table = LineTable.Parse(File.ReadAllText(LinesPath));
            if (table.Errors.Count > 0) Fail($"the line table has {table.Errors.Count} bad rows: {table.Errors[0]}");
            notes.Add("verified: chores, carry items, culture, guard duty and line table read back clean");
        }

        private static void Fail(string message) => throw new InvalidOperationException($"[Residents] Errand content verify failed: {message}");
    }
}
