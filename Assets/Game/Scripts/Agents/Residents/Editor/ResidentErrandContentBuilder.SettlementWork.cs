// The settlement's work, as errand content: every job a nomad settlement's buildings offer (mining, smelting, cooking,
// farming, herding, weaving, healing …) as a work post on the decoration prefab that offers it, the errands that move
// real props between them (harvest, feed, firewood, goods, ore), the archetypes that do the work, which character
// prefab makes which kind of person, the job-specific hold cues, and the SettlementFixture that lets a player find and
// read every working decoration. Same rules as the rest of this builder: idempotent, appended never reordered,
// a prefab open in Prefab Mode is skipped and named.
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
        private const string ActionDir = "Assets/Game/ScriptableObjects/Animation/Actions";
        private const string CharacterDir = "Assets/Game/Prefabs/agents/Characters/Raxy";

        private readonly struct Fixture
        {
            public readonly string name, description;
            public readonly ScanClass scan;
            public Fixture(string name, string description, ScanClass scan = ScanClass.Site) => (this.name, this.description, this.scan) = (name, description, scan);
        }

        // What a player reads at each working decoration. Keyed by the decoration's name without "Deco_".
        private static readonly Dictionary<string, Fixture> Fixtures = new()
        {
            ["BarStools"] = new("Bar stools", "Worn smooth by a hundred evenings of complaint."),
            ["Bench_Carved"] = new("Carved bench", "Somebody's grandmother carved the dune-fox on the end."),
            ["Stool_Set"] = new("Stools", "Low stools, for sitting close to a fire."),
            ["Cushions_Pile"] = new("Cushions", "Faded cushions where the elders sit and argue."),
            ["SeatingStones_Ring"] = new("Seating stones", "A ring of sun-warm stones round the hearth."),
            ["Table_Dining"] = new("Dining table", "Where a household eats, and settles its quarrels."),
            ["TavernTable"] = new("Tavern table", "Sticky with spilt cactus beer."),
            ["LowTable_Meal"] = new("Low table", "A meal is shared here, sitting on the floor."),
            ["GameTable"] = new("Game table", "A board game nobody outside the settlement can follow."),
            ["CouncilTable"] = new("Council table", "Where the settlement's decisions are made, slowly."),
            ["Hammock"] = new("Hammock", "Strung in the shade for the hottest hours."),
            ["HealerCot"] = new("Healer's cot", "Clean linen and the smell of crushed herbs."),
            ["SleepingMat_Rolled"] = new("Sleeping mat", "Rolled up for the day."),
            ["Forge"] = new("Forge", "The smith's fire. It is never allowed to go cold."),
            ["AnvilStone"] = new("Anvil", "Every tool in the settlement was beaten straight here."),
            ["Smelter"] = new("Smelter", "Ore goes in, slag and good metal come out."),
            ["DrillRig"] = new("Drill rig", "Bites into the rock for ore."),
            ["MineCart"] = new("Mine cart", "Carries ore from the drill face."),
            ["OrePile_Rust"] = new("Ore pile", "Raw ore waiting for the smelter.", ScanClass.Container),
            ["SluiceBox"] = new("Sluice box", "Washes the fines out of the gravel."),
            ["ScrapPile"] = new("Scrap pile", "Salvage waiting for a tinker's eye.", ScanClass.Container),
            ["ScrapPile_Small"] = new("Scrap pile", "Odd bits of salvage.", ScanClass.Container),
            ["CookingHearth_Pot"] = new("Cooking hearth", "A stew that has been simmering for days."),
            ["BreadOven"] = new("Bread oven", "Flatbread every morning, if the firewood came."),
            ["GrillBrazier"] = new("Grill", "Meat and roots over glowing coals."),
            ["FirePit"] = new("Fire pit", "Where the settlement gathers when the sun goes down."),
            ["SpiceRack"] = new("Spice rack", "Dried pods, salts and powders.", ScanClass.Container),
            ["BarCounter"] = new("Bar counter", "Cactus beer and stronger things."),
            ["KegRack"] = new("Keg rack", "Barrels of beer and water.", ScanClass.Container),
            ["Keg_Tap"] = new("Keg tap", "The barkeep's pride."),
            ["FarmBed_Roots"] = new("Root bed", "Root crops, hoed and watered by hand."),
            ["FarmBed_Tubers"] = new("Tuber bed", "Tubers that keep through the dry season."),
            ["FarmBed_Vines"] = new("Vine bed", "Climbing vines on a frame of salvage."),
            ["PlanterBed_Bulbs"] = new("Bulb planter", "Bulbs that flower after a sandstorm."),
            ["PlanterBed_Round"] = new("Round planter", "A small kitchen garden."),
            ["PlanterBed_Spikes"] = new("Spike planter", "Spiked succulents. They drink the most."),
            ["Hydroponic_Column"] = new("Hydroponic column", "Greens grown on water and patience."),
            ["Hydroponic_Wall"] = new("Hydroponic wall", "A wall of greens fed from the cistern."),
            ["HydroponicRack"] = new("Hydroponic rack", "Seedlings under glass."),
            ["GrowLampFrame_Bed"] = new("Grow lamp", "Light for the beds through the long nights."),
            ["CompostBin"] = new("Compost bin", "Nothing in the settlement is wasted.", ScanClass.Container),
            ["CompostBays"] = new("Compost bays", "Turned once a week, smelled daily.", ScanClass.Container),
            ["IrrigationPump"] = new("Irrigation pump", "Pushes cistern water out to the beds."),
            ["FeedingTrough_Clay"] = new("Feeding trough", "The animals crowd it twice a day."),
            ["FodderStack"] = new("Fodder", "Dried fodder for the animals.", ScanClass.Container),
            ["StableStall"] = new("Stable stall", "Shade and a manger for the mounts."),
            ["SaddleRack"] = new("Saddle rack", "Saddles and tack, oiled against the sand."),
            ["YokeHarness"] = new("Yoke", "For hauling with a pack animal."),
            ["HitchingPost"] = new("Hitching post", "Tie a mount here."),
            ["InsectHive"] = new("Insect hive", "Desert bees, and their bitter honey."),
            ["ButcherBlock"] = new("Butcher's block", "Scarred by a thousand cuts."),
            ["FishRack"] = new("Drying rack", "Fish and lizard drying in the sun."),
            ["DryingRack_Meat"] = new("Meat rack", "Strips of meat curing in the dry wind."),
            ["HideFrame"] = new("Hide frame", "A hide stretched and scraped for tanning."),
            ["NetPoles"] = new("Net poles", "Nets hung to dry and be mended."),
            ["HarpoonRack"] = new("Harpoon rack", "Harpoons for the sand-whale hunt."),
            ["DryingRack_Herbs"] = new("Herb rack", "Healing herbs drying in the shade."),
            ["AlchemyTable"] = new("Alchemy table", "Tinctures, poultices and a few things best not asked about."),
            ["ApothecaryShelf"] = new("Apothecary shelf", "Jars of remedies, labelled in the healer's hand.", ScanClass.Container),
            ["SpecimenJars"] = new("Specimen jars", "Odd creatures preserved in brine.", ScanClass.Container),
            ["HerbBundles"] = new("Herb bundles", "Bundles hung to dry."),
            ["PotteryWheel"] = new("Pottery wheel", "Every jar in the settlement started here."),
            ["Kiln"] = new("Kiln", "Fires the potter's clay hard."),
            ["Loom"] = new("Loom", "Cloth for awnings, robes and trade."),
            ["Workbench"] = new("Workbench", "Half-mended things and the tools to mend them."),
            ["Worktable_Wood"] = new("Worktable", "A sturdy table for any work at hand."),
            ["Worktable_Metal"] = new("Metal worktable", "A tinker's bench, scorched and scratched."),
            ["ToolRack"] = new("Tool rack", "Tools, each hung in its own outline.", ScanClass.Container),
            ["ToolRack_Metal"] = new("Tool rack", "Wrenches and pliers.", ScanClass.Container),
            ["ToolRack_Wood"] = new("Tool rack", "Hoes, rakes and shovels.", ScanClass.Container),
            ["WeaponRack"] = new("Weapon rack", "The settlement's spears, ready for trouble.", ScanClass.Container),
            ["GrindingStone_Rotary"] = new("Grindstone", "Grain goes in, flour comes out."),
            ["GrindingStone_Saddle"] = new("Grinding stone", "Seeds ground by hand."),
            ["RepairDrone_Docked"] = new("Repair drone", "A salvaged drone, docked and humming."),
            ["Handcart"] = new("Handcart", "For hauling goods across the settlement."),
            ["MarketStall_Bare"] = new("Market stall", "A stall waiting for its wares."),
            ["MarketStall_Cloth"] = new("Cloth stall", "Bolts of woven cloth for trade."),
            ["MarketStall_Produce"] = new("Produce stall", "Roots, greens and dried fruit."),
            ["TradeScales"] = new("Trade scales", "Honest weights, mostly."),
            ["Chest_Storage"] = new("Storage chest", "Locked, and the key is not here.", ScanClass.Container),
            ["Shelf_Wood"] = new("Shelf", "Jars, rope and odds and ends.", ScanClass.Container),
            ["ShelvingRack_Low"] = new("Shelves", "Stores for the household.", ScanClass.Container),
            ["ShelvingRack_Tall"] = new("Shelves", "Stores stacked to the roof.", ScanClass.Container),
            ["PotCluster_Stacked"] = new("Pots", "Stacked pots of grain and oil.", ScanClass.Container),
            ["GourdJar_Group"] = new("Gourd jars", "Water and oil kept cool.", ScanClass.Container),
            ["Crate_WoodStack"] = new("Crates", "The settlement's goods store.", ScanClass.Container),
            ["Firewood_Stack"] = new("Woodpile", "Precious wood, hauled from the far oases.", ScanClass.Container),
            ["OfferingShrine"] = new("Shrine", "Offerings to the ones who came before."),
            ["Podium"] = new("Podium", "Where news and judgements are spoken."),
            ["NoticeBoard"] = new("Notice board", "Work wanted, goods offered, a lost goat."),
            ["DrumSet"] = new("Drums", "Played at every gathering, and some nights for no reason."),
            ["BellFrame"] = new("Bell", "Rung for storms, raiders and weddings."),
            ["WaterBasin_Carved"] = new("Water basin", "Fresh water. Do not waste it."),
            ["WashStand"] = new("Wash stand", "A basin and a cracked mirror."),
            ["SteamTub"] = new("Steam tub", "Hot stones and a little water go a long way."),
            ["CondensationTower"] = new("Condensation tower", "Pulls water out of the night air."),
            ["Watchtower"] = new("Watchtower", "Sees the whole valley."),
            ["Watchtower_Wood"] = new("Watchtower", "Sees the whole valley."),
        };

        // The jobs: a work post per decoration that offers it, and the side of the prefab it stands on (the front is
        // taken by an errand stop where the decoration already has one).
        private static readonly (string spot, string display, string cue, bool always, string[] front, string[] back)[] Jobs =
        {
            ("Mine", "the mine", "dig", false, new[] { "DrillRig", "SluiceBox" }, new[] { "MineCart" }),
            ("Smeltery", "the smelter", "hammer", false, new string[0], new[] { "Smelter" }),
            ("Forge", "the forge", "repair", false, new[] { "AnvilStone" }, new[] { "Forge" }),
            ("Kitchen", "the kitchen", "work", true, new string[0], new[] { "CookingHearth_Pot", "BreadOven", "GrillBrazier" }),
            ("Bar", "the bar", "cook", true, new[] { "Keg_Tap" }, new[] { "BarCounter" }),
            ("Loom", "the loom", "craft", false, new[] { "Loom" }, new string[0]),
            ("Pottery", "the potter's wheel", "craft", false, new[] { "PotteryWheel" }, new[] { "Kiln" }),
            ("Apiary", "the hives", "tend", false, new[] { "InsectHive" }, new string[0]),
            ("Infirmary", "the healer's", "craft", true, new[] { "HealerCot", "AlchemyTable", "ApothecaryShelf" }, new string[0]),
            ("Butchery", "the butcher's block", "cook", false, new[] { "ButcherBlock", "FishRack", "DryingRack_Meat" }, new string[0]),
            ("Tannery", "the hide frame", "craft", false, new[] { "HideFrame" }, new string[0]),
            ("Shrine", "the shrine", "listen", false, new[] { "OfferingShrine", "Podium" }, new string[0]),
            ("Workshop", "the workshop", "repair", false, new[] { "Workbench", "Worktable_Wood", "Worktable_Metal" }, new string[0]),
            ("Mill", "the grindstone", "hammer", false, new[] { "GrindingStone_Rotary", "GrindingStone_Saddle" }, new string[0]),
            ("Farm", "the fields", "dig", false, new string[0], new[] { "FarmBed_Roots", "FarmBed_Tubers", "FarmBed_Vines" }),
            ("Pen", "the pen", "tend", false, new[] { "StableStall", "Pen_Round" }, new string[0]),
            ("Garden", "the garden", "tend", false, new[] { "Hydroponic_Wall", "Hydroponic_Column", "HydroponicRack", "GrowLampFrame_Bed" }, new string[0]),
            ("Stall", "the stall", "explain", true, new string[0], new[] { "MarketStall_Bare", "MarketStall_Cloth", "MarketStall_Produce" }),
        };

        // Job-specific loops, so a cook stirs and a miner digs instead of drawing any of the 34 "work" actions.
        private static readonly (string cue, string meaning, string[] actions)[] JobCues =
        {
            ("cook", "Cooking or serving: stirring, chopping, wiping down.", new[] { "Stir Pot", "Chop Food", "Wipe Surface" }),
            ("dig", "Digging: mining, hoeing, shovelling.", new[] { "Dig", "Push Heavy", "Pull Heavy" }),
            ("hammer", "Hammering and grinding: smith, smelter, miller.", new[] { "Hammer", "Saw" }),
            ("craft", "Fine handwork: weaving, potting, tinkering, mixing.", new[] { "Screwdriver", "Coil Rope", "Wrench Tighten", "Wipe Surface" }),
            ("tend", "Tending living things: plants, bees, animals.", new[] { "Dig", "Coil Rope", "Wipe Surface" }),
        };

        private static Dictionary<string, string> decorationPaths;

        private static void AuthorSettlementWork(SpotUse goods, SpotUse plant, SpotUse orePile, SpotUse smelter,
                                                 ChoreDefinition haulGoods, ChoreDefinition haulOre, List<string> notes)
        {
            foreach (var (cue, meaning, actions) in JobCues) JobCue(cue, meaning, actions, notes);

            var posts = new Dictionary<string, SpotUse>();
            foreach (var job in Jobs)
            {
                SpotUse post = Spot(job.spot, job.display, SpotRole.Work, job.cue, alwaysManned: job.always);
                posts[job.spot] = post;
                foreach (string deco in job.front) AddSideSpot(DecorationPath(deco, notes), post, Side.Front, notes);
                foreach (string deco in job.back) AddSideSpot(DecorationPath(deco, notes), post, Side.Back, notes);
            }

            SpotUse fodder = Spot("Fodder", "the fodder", SpotRole.Errand, "pickup");
            SpotUse trough = Spot("Trough", "the trough", SpotRole.Errand, "putdown");
            SpotUse woodpile = Spot("Woodpile", "the woodpile", SpotRole.Errand, "pickup");
            SpotUse firebox = Spot("Firebox", "the fire", SpotRole.Errand, "putdown");
            SpotUse seat = Require<SpotUse>($"{SpotDir}/Seat.asset");
            SpotUse table = Require<SpotUse>($"{SpotDir}/Table.asset");
            AddSideSpot(DecorationPath("FodderStack", notes), fodder, Side.Front, notes);
            AddSideSpot(DecorationPath("FeedingTrough_Clay", notes), trough, Side.Front, notes);
            AddSideSpot(DecorationPath("Firewood_Stack", notes), woodpile, Side.Front, notes);
            foreach (string deco in new[] { "CookingHearth_Pot", "BreadOven", "GrillBrazier", "FirePit" })
                AddSideSpot(DecorationPath(deco, notes), firebox, Side.Left, notes);
            AddSideSpot(DecorationPath("Kiln", notes), firebox, Side.Left, notes);
            AddSideSpot(DecorationPath("Forge", notes), firebox, Side.Right, notes);
            foreach (string deco in new[] { "Bench_Carved", "Stool_Set", "BarStools", "Cushions_Pile", "SeatingStones_Ring" })
                AddSideSpot(DecorationPath(deco, notes), seat, Side.Front, notes);
            foreach (string deco in new[] { "Table_Dining", "TavernTable", "LowTable_Meal", "GameTable", "CouncilTable" })
                AddSideSpot(DecorationPath(deco, notes), table, Side.Front, notes);

            InventoryItem wicker = Tool("Carry_Basket_Wicker"), open = Tool("Carry_Basket_Open");
            ChoreDefinition harvest = Chore("Harvest", "bringing in the harvest", plant, goods, wicker, new Vector2Int(1, 1), carryThroughout: false);
            ChoreDefinition feed = Chore("Feed", "feeding the animals", fodder, trough, open, new Vector2Int(1, 2), carryThroughout: false);
            ChoreDefinition firewood = Chore("Firewood", "carrying firewood", woodpile, firebox, open, new Vector2Int(1, 2), carryThroughout: false);
            foreach (ChoreDefinition moving in new[] { haulGoods, haulOre, harvest })
            {
                moving.carriesProps = true;
                Save(moving);
            }

            // Everything a settlement prop becomes in a hand. Appended after the chore items: the index is on the wire.
            RegisterCarryItems(Tool("Carry_Bucket_Metal"), Tool("Carry_Cart_Hand"), Tool("Tool_Lantern"), Tool("Tool_SignalFlag"),
                               Tool("Tool_MortarPestle"), Tool("Tool_CookingPot"), Tool("Carry_Tank_Water"));

            var made = new List<ResidentArchetype>
            {
                Worker("Miner", "miner", posts["Mine"], 0.7f, 0.6f, "Tool_Pickaxe", "Tool_RockHammer", "Tool_Shovel"),
                Worker("Smelter", "smelter", posts["Smeltery"], 0.6f, 0.5f, "Tool_Hammer", "Tool_Crowbar"),
                Worker("Barkeep", "barkeep", posts["Bar"], 0.5f, 0.4f, "Tool_Flask", "Tool_Ladle"),
                Worker("Weaver", "weaver", posts["Loom"], 0.3f, 0.3f, "Tool_Spindle"),
                Worker("Potter", "potter", posts["Pottery"], 0.4f, 0.3f, "Tool_Trowel", "Tool_BrickMould"),
                Worker("Beekeeper", "beekeeper", posts["Apiary"], 0.5f, 0.2f, "Tool_Lantern", "Tool_SandBrush"),
                Worker("Healer", "healer", posts["Infirmary"], 0.4f, 0.2f, "Tool_PoulticeBowl", "Tool_BandageRoll", "Tool_Splint"),
                Worker("Butcher", "butcher", posts["Butchery"], 0.7f, 0.6f, "Tool_Cleaver", "Tool_SkinningKnife"),
                Worker("Tanner", "tanner", posts["Tannery"], 0.5f, 0.6f, "Tool_TanningPaddle", "Tool_FleshingScraper"),
                Worker("ShrineKeeper", "shrine keeper", posts["Shrine"], 0.3f, 0.2f, null),
                Worker("Mechanic", "mechanic", posts["Workshop"], 0.5f, 0.5f, "Tool_Wrench_Ring", "Tool_Pliers", "Tool_PipeWrench"),
                Worker("Miller", "miller", posts["Mill"], 0.4f, 0.4f, "Tool_Mallet"),
                Worker("Farmer", "farmer", posts["Farm"], 0.4f, 0.3f, "Tool_Hoe", "Tool_Sickle", null, harvest),
                Roamer("Stablehand", "stablehand", feed, nerve: 0.5f, temper: 0.4f),
                Roamer("WoodCarrier", "wood carrier", firewood, nerve: 0.4f, temper: 0.5f),
            };
            SetChore("Herder", feed);
            SetChore("Cook", firewood);
            RegisterInCulture(made.ToArray());
            AuthorProfiles(notes);
            AppendWorkLines(notes);

            foreach (KeyValuePair<string, Fixture> fixture in Fixtures) AddFixture(fixture.Key, fixture.Value, notes);
        }

        private static ResidentArchetype Worker(string name, string role, SpotUse post, float nerve, float temper, string held,
                                                string belt1 = null, string belt2 = null, ChoreDefinition chore = null)
        {
            ResidentArchetype archetype = Archetype(name);
            (archetype.roleName, archetype.post, archetype.trips, archetype.chore, archetype.duty) = (role, post, TripKind.None, chore, ResidentDuty.None);
            (archetype.nerve, archetype.temper) = (nerve, temper);
            archetype.heldItem = held != null ? Tool(held) : null;
            archetype.beltItems = new[] { belt1, belt2 }.Where(b => b != null).Select(Tool).ToArray();
            Save(archetype);
            return archetype;
        }

        // Which kind of person each Raxy makes. A prefab missing from disk is named and skipped.
        private static void AuthorProfiles(List<string> notes)
        {
            var table = new (string prefab, string[] suits)[]
            {
                ("Raxy_handyman", new[] { "Mechanic", "Tinker", "Smith", "Smelter" }),
                ("Raxy_Warrior", new[] { "Guard", "TowerGuard", "Hunter", "Butcher" }),
                ("Drifter_RaxyArmor", new[] { "Guard", "TowerGuard", "Lookout" }),
                ("Raxy_equipped", new[] { "Miner", "Salvager", "Scout", "Hunter" }),
                ("Raxy_Jock", new[] { "Miner", "Smelter", "OreCarrier", "Hauler" }),
                ("Raxy_poor", new[] { "Hauler", "WoodCarrier", "Stablehand", "OreCarrier", "Villager" }),
                ("Raxy_townsman", new[] { "Trader", "Barkeep", "Elder", "Storyteller" }),
                ("Raxy_Wanderer", new[] { "Scout", "WaterRunner", "Forager", "Salvager" }),
                ("Raxy_Kid", new[] { "Apprentice", "Villager", "Forager" }),
                ("Raxy_clothed", new[] { "Weaver", "Potter", "Trader", "Cook" }),
                ("Drifter_rax_young", new[] { "Apprentice", "Stablehand", "Farmer", "Gardener" }),
                ("Drifter_RaxyPoncho", new[] { "Herder", "Drover", "Farmer", "Beekeeper" }),
                ("Drifter_RaxySage", new[] { "Healer", "ShrineKeeper", "Elder", "Gardener" }),
                ("Drifter_RaxyAsh", new[] { "Smith", "Smelter", "Tanner", "Miner" }),
                ("Drifter_RaxyChill", new[] { "Barkeep", "Storyteller", "Weaver", "Beekeeper" }),
                ("Drifter_RaxyClassic", new[] { "Farmer", "Miller", "Cook", "Waterkeeper" }),
                ("Drifter_RaxyMauve", new[] { "Potter", "Weaver", "Healer", "Trader" }),
                ("Drifter_Raxy", new[] { "Villager", "Hauler", "Gardener", "Butcher" }),
            };

            var culture = Require<SettlementCulture>(CulturePath);
            var profiles = new List<CharacterProfile>();
            foreach (var (prefabName, suits) in table)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{CharacterDir}/{prefabName}.prefab");
                if (prefab == null) { notes.Add($"no character prefab {prefabName}: its profile is skipped"); continue; }

                ResidentArchetype[] archetypes = suits.Select(s => AssetDatabase.LoadAssetAtPath<ResidentArchetype>($"{ArchetypeDir}/{s}.asset"))
                                                      .Where(a => a != null).ToArray();
                profiles.Add(new CharacterProfile { prefab = prefab, suits = archetypes });
            }
            culture.profiles = profiles.ToArray();
            Save(culture);
        }

        private static void JobCue(string name, string meaning, string[] actions, List<string> notes)
        {
            CharacterCue cue = Ensure<CharacterCue>($"{CueDir}/{name}.asset");
            var so = new SerializedObject(cue);
            so.FindProperty("meaning").stringValue = meaning;
            so.FindProperty("fallback").objectReferenceValue = Require<CharacterCue>($"{CueDir}/work.asset");
            so.ApplyModifiedPropertiesWithoutUndo();

            foreach (string actionName in actions)
            {
                string guid = AssetDatabase.FindAssets($"\"{actionName}\" t:CharacterAction", new[] { ActionDir })
                    .FirstOrDefault(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == actionName);
                if (guid == null) { notes.Add($"no action '{actionName}' to tag {name}"); continue; }

                var action = AssetDatabase.LoadAssetAtPath<CharacterAction>(AssetDatabase.GUIDToAssetPath(guid));
                var actionSo = new SerializedObject(action);
                SerializedProperty cues = actionSo.FindProperty("cues");
                bool tagged = Enumerable.Range(0, cues.arraySize).Any(i => cues.GetArrayElementAtIndex(i).objectReferenceValue == cue);
                if (tagged) continue;
                cues.InsertArrayElementAtIndex(cues.arraySize);
                cues.GetArrayElementAtIndex(cues.arraySize - 1).objectReferenceValue = cue;
                actionSo.ApplyModifiedPropertiesWithoutUndo();
            }
            Save(cue);
        }

        private static string DecorationPath(string decoration, List<string> notes)
        {
            if (decorationPaths == null)
            {
                decorationPaths = new Dictionary<string, string>();
                foreach (string guid in AssetDatabase.FindAssets("Deco_ t:Prefab", new[] { DecorationDir }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    decorationPaths[Path.GetFileNameWithoutExtension(path)] = path.Substring(DecorationDir.Length + 1, path.Length - DecorationDir.Length - 1 - ".prefab".Length);
                }
            }
            if (decorationPaths.TryGetValue("Deco_" + decoration, out string relative)) return relative;
            notes.Add($"no decoration prefab Deco_{decoration}");
            return "missing/Deco_" + decoration;
        }

        private static void AddFixture(string decoration, Fixture fixture, List<string> notes)
        {
            string path = $"{DecorationDir}/{DecorationPath(decoration, notes)}.prefab";
            if (!File.Exists(path)) return;
            if (PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == path) { notes.Add($"skipped fixture on {decoration}: open in Prefab Mode"); return; }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                SettlementFixture component = root.GetComponent<SettlementFixture>();
                bool added = component == null;
                if (added) component = root.AddComponent<SettlementFixture>();

                var so = new SerializedObject(component);
                bool changed = added | Set(so, "displayName", fixture.name) | Set(so, "description", fixture.description);
                SerializedProperty scan = so.FindProperty("scanClass");
                if (scan.enumValueIndex != (int)fixture.scan) { scan.enumValueIndex = (int)fixture.scan; changed = true; }
                if (!changed) return;

                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                notes.Add($"fixture '{fixture.name}' on Deco_{decoration}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool Set(SerializedObject so, string property, string value)
        {
            SerializedProperty p = so.FindProperty(property);
            if (p.stringValue == value) return false;
            p.stringValue = value;
            return true;
        }

        private static void AppendWorkLines(List<string> notes)
        {
            string text = File.ReadAllText(LinesPath);
            var present = new HashSet<string>(text.Split('\n').Select(l => l.Split('\t')[0]));
            var rows = WorkLines().Where(r => r.StartsWith("#") ? !text.Contains(r) : !present.Contains(r.Split('\t')[0])).ToList();
            if (rows.Count == 0) return;

            File.AppendAllText(LinesPath, (text.EndsWith("\n") ? "" : "\n") + string.Join("\n", rows) + "\n");
            notes.Add($"{rows.Count} settlement work line rows appended to {LinesPath}");
        }

        private static IEnumerable<string> WorkLines()
        {
            string Row(string key, string speaker, string topic, string stance, string activity, string register, string text) =>
                string.Join("\t", key, speaker, topic, stance, "", activity, "", register, text);

            yield return "# -- Settlement work: what each trade says at its post --";
            yield return Row("miner.work.1", "Miner", "Work", "", "Work", "Statement", "The seam runs deeper every week. So do we.");
            yield return Row("miner.work.2", "Miner", "Work", "", "Work", "Complaint", "Rock dust in my teeth again.");
            yield return Row("miner.greet", "Miner", "Greeting", "", "", "Statement", "Mind the cart track. It doesn't stop for anyone.");
            yield return Row("smelter.work.1", "Smelter", "Work", "", "Work", "Statement", "Keep the bellows going and the slag runs clean.");
            yield return Row("smelter.work.2", "Smelter", "Work", "", "Work", "Complaint", "Not enough ore today. Never enough ore.");
            yield return Row("smelter.greet", "Smelter", "Greeting", "", "", "Statement", "Stand back from the mouth. It spits.");
            yield return Row("barkeep.work.1", "Barkeep", "Work", "", "Work", "Statement", "Cactus beer, cold as I can make it.");
            yield return Row("barkeep.work.2", "Barkeep", "Gossip", "", "Work", "Statement", "You hear everything behind a bar. Everything.");
            yield return Row("barkeep.greet", "Barkeep", "Greeting", "", "", "Question", "Thirsty? Everyone's thirsty.");
            yield return Row("weaver.work.1", "Weaver", "Work", "", "Work", "Statement", "This pattern was my mother's. The colours are mine.");
            yield return Row("weaver.work.2", "Weaver", "Work", "", "Work", "Complaint", "Sand in the warp again. It gets in everything.");
            yield return Row("weaver.greet", "Weaver", "Greeting", "", "", "Statement", "Careful of the threads.");
            yield return Row("potter.work.1", "Potter", "Work", "", "Work", "Statement", "Every jar holds water a little better than the last.");
            yield return Row("potter.work.2", "Potter", "Work", "", "Work", "Statement", "The kiln decides. I only ask nicely.");
            yield return Row("potter.greet", "Potter", "Greeting", "", "", "Statement", "Clay on your boots is good luck.");
            yield return Row("beekeeper.work.1", "Beekeeper", "Work", "", "Work", "Statement", "Slow hands. They only sting the hurried.");
            yield return Row("beekeeper.work.2", "Beekeeper", "Gossip", "", "Work", "Statement", "The bees knew about the storm two days early.");
            yield return Row("beekeeper.greet", "Beekeeper", "Greeting", "", "", "Statement", "Don't swat. Breathe.");
            yield return Row("healer.work.1", "Healer", "Work", "", "Work", "Statement", "Bitterroot for fever, ash-salve for burns.");
            yield return Row("healer.work.2", "Healer", "Work", "", "Work", "Complaint", "They only come to me once it's bad.");
            yield return Row("healer.greet", "Healer", "Greeting", "", "", "Question", "Are you hurt, or just curious?");
            yield return Row("butcher.work.1", "Butcher", "Work", "", "Work", "Statement", "Nothing from a beast goes to waste here.");
            yield return Row("butcher.work.2", "Butcher", "Work", "", "Work", "Statement", "Sharp knife, steady hand, quick work.");
            yield return Row("butcher.greet", "Butcher", "Greeting", "", "", "Statement", "Fresh cuts at midday. Come early.");
            yield return Row("tanner.work.1", "Tanner", "Work", "", "Work", "Complaint", "Yes, it smells. Hides always smell.");
            yield return Row("tanner.work.2", "Tanner", "Work", "", "Work", "Statement", "Scrape it thin and it lasts a lifetime.");
            yield return Row("tanner.greet", "Tanner", "Greeting", "", "", "Statement", "Stand upwind, friend.");
            yield return Row("shrine.work.1", "ShrineKeeper", "Work", "", "Work", "Statement", "We remember them, so they remember us.");
            yield return Row("shrine.work.2", "ShrineKeeper", "Gossip", "", "Work", "Statement", "The offerings were moved in the night. Nobody admits it.");
            yield return Row("shrine.greet", "ShrineKeeper", "Greeting", "", "", "Statement", "Walk softly here.");
            yield return Row("mechanic.work.1", "Mechanic", "Work", "", "Work", "Statement", "Everything breaks. That's what keeps me fed.");
            yield return Row("mechanic.work.2", "Mechanic", "Work", "", "Work", "Complaint", "Who stripped this bolt? Who does that?");
            yield return Row("mechanic.greet", "Mechanic", "Greeting", "", "", "Question", "Got something rattling? Bring it here.");
            yield return Row("miller.work.1", "Miller", "Work", "", "Work", "Statement", "Grain in, flour out. All day, every day.");
            yield return Row("miller.greet", "Miller", "Greeting", "", "", "Statement", "Flour's not ready yet. Ask again later.");
            yield return Row("farmer.work.1", "Farmer", "Work", "", "Work", "Statement", "Turn the soil, mind the water, pray for no storm.");
            yield return Row("farmer.work.2", "Farmer", "Work", "", "Chore", "Statement", "First baskets of the season. Off to the store with them.");
            yield return Row("farmer.greet", "Farmer", "Greeting", "", "", "Statement", "Stay off the beds, please.");
            yield return Row("stablehand.work.1", "Stablehand", "Work", "", "Chore", "Statement", "Fodder to the trough before they start shouting.");
            yield return Row("stablehand.greet", "Stablehand", "Greeting", "", "", "Statement", "Don't stand behind that one. She kicks.");
            yield return Row("wood.work.1", "WoodCarrier", "Work", "", "Chore", "Complaint", "Wood for every fire in the settlement. My arms.");
            yield return Row("wood.greet", "WoodCarrier", "Greeting", "", "", "Statement", "Make way, heavy load.");
            yield return Row("chore.harvest.1", "", "Work", "", "Chore", "Statement", "Good basket this one. The cook will be pleased.");
            yield return Row("chore.feed.1", "Herder", "Work", "", "Chore", "Statement", "Feeding time. They know the sound of my steps.");
            yield return Row("chore.firewood.1", "Cook", "Work", "", "Chore", "Statement", "No wood, no bread. Simple as that.");
        }
    }
}
