// Authors the Sand, Sky Tribe and Strider rosters from what their people already are, and wires the world
// sim to them: the war-party templates and the Striders' walking city. Idempotent: re-run it any time; it overwrites what it owns and nothing else.
//
// Run from: Tools > SpaceGame > Agents > Author Sand Tribe Roster / Author Sky Tribe Roster / Author Strider Roster
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.World;
using Object = UnityEngine.Object;

namespace SpaceGame.EditorTools
{
    public static class RosterAuthoring
    {
        private const string RosterDir = "Assets/Game/ScriptableObjects/Factions/Rosters";
        public const string SandRosterPath = RosterDir + "/SandTribe.asset";
        private const string SandHostileLinesPath = RosterDir + "/SandTribeHostileLines.asset";
        public const string SandFactionPath = "Assets/Game/ScriptableObjects/Factions/Core/SandTribeFaction.asset";

        private const string CharacterDir = "Assets/Game/Prefabs/Agents/Characters";
        private const string NomadOstrichPath = "Assets/Game/Prefabs/Agents/Caravan/NomadOstrich.prefab";

        public const string SandWarPartyTemplateId = "sand-war-party";
        public const string SkyWarPartyTemplateId = "sky-war-party";

        // On foot once dropped off, like the Sand party. Folded in the air it moves at its vessels'
        // own cruise speed, read off the prefabs (FoldedTravelSpeed).
        private const float WarPartyWalkSpeed = 3f;
        private const string WorldScenePath = "Assets/Game/Scenes/world/persistentScene.unity";
        private const string OutlawFactionPath = "Assets/Game/ScriptableObjects/Factions/Core/OutlawFaction.asset";

        public const string SkyFactionPath = "Assets/Game/ScriptableObjects/Factions/Core/SkyTribeFaction.asset";
        public const string SkyRosterPath = RosterDir + "/SkyTribe.asset";
        private const string SkyHostileLinesPath = RosterDir + "/SkyTribeHostileLines.asset";
        public const string GlobalRelationshipsPath = "Assets/Game/ScriptableObjects/Factions/Core/GlobalRelationships.asset";

        // Design §"Sky tribe stance": a sky-coloured HUD readout, distinct from Sand's white default.
        private static readonly Color SkyTribeHudColor = new Color(0.55f, 0.78f, 0.98f);

        public const string StriderFactionPath = "Assets/Game/ScriptableObjects/Factions/Core/StriderFaction.asset";
        public const string StriderRosterPath = RosterDir + "/Striders.asset";
        private const string StriderHostileLinesPath = RosterDir + "/StriderHostileLines.asset";
        public const string StriderWarPartyTemplateId = "strider-war-party";
        public const string StriderCityTemplateId = "strider-city";
        // A war party's Riders are dealt from a deck (FactionRoster.Deal): every monowheel once a round,
        // a weight only ordering the round. Alike, so no wheel sits at the back of every round where a
        // two- or three-wheel party never reaches it (the user, 2026-10-05: some were too rare).
        private const float MonowheelWeight = 1f;
        // Rust red: the colour the whole column reads as from across a dune (spec §2).
        private static readonly Color StriderHudColor = new Color(0.72f, 0.22f, 0.12f);

        private static readonly string[] SandPeople =
        {
            CharacterDir + "/Nomad.prefab",
            CharacterDir + "/Nomad_Maroon.prefab",
            CharacterDir + "/Nomad_StrawHat.prefab",
            CharacterDir + "/Nomad_Tan.prefab",
            CharacterDir + "/Nomad_Umber.prefab",
        };

        // Moved here from NomadPrefabBuilder.WeaponArtifactPaths, which is deleted: the roster owns the
        // list now. Ranged artifacts only: NpcItemUseModule fires the held item at a target between
        // minRange and maxRange, so a gauntlet or a scanner in the hand would be "used" at nothing.
        // Sand and Sky field the same seven.
        private static readonly string[] NomadHandItemPaths =
        {
            "Assets/Game/Resources/Items/Artifacts/basicgun.asset",
            "Assets/Game/Resources/Items/Artifacts/GravelBlaster.asset",
            "Assets/Game/Resources/Items/Artifacts/NetGun.asset",
            "Assets/Game/Resources/Items/Artifacts/LaserStaff.asset",
            "Assets/Game/Resources/Items/Artifacts/BallLightningWeapon.asset",
            "Assets/Game/Resources/Items/Artifacts/LightningSpell.asset",
            "Assets/Game/Resources/Items/Artifacts/DragonBazooka.asset",
        };

        private static readonly string[] SandHostileLines =
        {
            "There you are.",
            "The tribe remembers you.",
            "You should not have come back.",
            "Sand take you!",
        };

        // Shouted on a war party's first sight of its quarry. People who live above the clouds.
        private static readonly string[] SkyHostileLines =
        {
            "We saw you from the clouds.",
            "The wind carried your name to us.",
            "No ground is far enough below.",
            "Fall, groundling!",
        };

        // The Striders carry the three salvage guns (design §3.6 named the first two).
        private static readonly string[] StriderHandItemPaths =
        {
            "Assets/Game/Resources/Items/Artifacts/basicgun.asset",
            "Assets/Game/Resources/Items/Artifacts/GravelBlaster.asset",
            "Assets/Game/Resources/Items/Artifacts/NetGun.asset",
        };

        // Shouted on a war party's first sight of its quarry. People who live on walking machines.
        private static readonly string[] StriderHostileLines =
        {
            "You'll make good scrap.",
            "Strip it down to the frame!",
            "The city remembers what you did.",
            "Nothing runs forever. Neither will you.",
            "Grind them under!",
            "We walked a thousand miles. We'll walk one more for you.",
            "Every bolt you carry is ours now.",
            "Rust takes everything. We just take it first.",
        };

        [MenuItem("Tools/SpaceGame/Agents/Author Sand Tribe Roster")]
        public static void AuthorSandRoster()
        {
            GameObject[] people = SandPeople.Select(Load<GameObject>).Where(p => p != null).ToArray();
            GameObject ostrich = Load<GameObject>(NomadOstrichPath);

            // Every nomad can scout or fight; nothing tells one from the other yet. Role-specific
            // people arrive with sub-project 3's art.
            RosterMember[] members = people.Select(p => Member(RosterRole.Scout, p))
                .Concat(people.Select(p => Member(RosterRole.Warrior, p)))
                .Concat(ostrich != null ? new[] { Member(RosterRole.Rider, ostrich) } : Array.Empty<RosterMember>())
                .ToArray();

            AuthorRoster("Sand", SandFactionPath, SandRosterPath, SandHostileLinesPath, SandHostileLines, members,
                NomadHandItemPaths, new[]
                {
                    Tier((RosterRole.Scout, 2)),
                    Tier((RosterRole.Warrior, 3), (RosterRole.Scout, 1)),
                    Tier((RosterRole.Warrior, 3), (RosterRole.Rider, 2)),
                });
        }

        /// <summary>
        /// The Sky Tribe roster: the four sky nomads as scouts and warriors, and no Rider role —
        /// Sky parties fly in vessels, so a mount has nowhere to go. The top tier's seven people fit
        /// the freighter, not the skiff. Each tier also brings escort fliers on their own wings (SkyEscortFliers),
        /// who take no seat (RoleCount.ownWings).
        ///
        /// Run after Build Sky Nomad NPCs (validation reads the members' baked faction), then build
        /// again so the prefabs bake this roster's hand items.
        /// </summary>
        [MenuItem("Tools/SpaceGame/Agents/Author Sky Tribe Roster")]
        public static void AuthorSkyRoster()
        {
            GameObject[] people = NomadPrefabBuilder.SkyTribePeople
                .Select(recipe => Load<GameObject>(recipe.PrefabPath)).Where(p => p != null).ToArray();

            RosterMember[] members = people.Select(p => Member(RosterRole.Scout, p))
                .Concat(people.Select(p => Member(RosterRole.Warrior, p)))
                .ToArray();

            AuthorRoster("Sky", SkyFactionPath, SkyRosterPath, SkyHostileLinesPath, SkyHostileLines, members,
                NomadHandItemPaths, new[]
                {
                    WithFliers(Tier((RosterRole.Scout, 2)), RosterRole.Warrior, SkyEscortFliers[0]),
                    WithFliers(Tier((RosterRole.Warrior, 3), (RosterRole.Scout, 1)), RosterRole.Warrior, SkyEscortFliers[1]),
                    WithFliers(Tier((RosterRole.Warrior, 5), (RosterRole.Scout, 2)), RosterRole.Warrior, SkyEscortFliers[2]),
                });
        }

        /// <summary>
        /// Fliers per Sky war-party tier, on top of the seated riders: they escort the vessel on their own wings and
        /// land beside its drop (user decision 2026-10-07).
        /// </summary>
        internal static readonly int[] SkyEscortFliers = { 2, 4, 6 };

        /// <summary>
        /// The Striders' roster: the four Strider nomads as scouts and warriors, and the five
        /// monowheels as Riders, so every war party is a convoy, dealt its wheels from a deck of all five:
        /// no wheel twice before every one has ridden (a five-wheel party rides each once). The crab
        /// outrider is not a Rider here; it stays with the walking city as a fixed prefab.
        ///
        /// Run after Build Strider Nomad NPCs and Build Strider Monowheels (validation reads the
        /// members' baked faction), then rebuild the nomads so they bake this roster's hand items,
        /// and the monowheels so they seat riders and gunners carrying them.
        /// </summary>
        [MenuItem("Tools/SpaceGame/Agents/Author Strider Roster")]
        public static void AuthorStriderRoster()
        {
            GameObject[] people = NomadPrefabBuilder.StriderNomads
                .Select(recipe => Load<GameObject>(recipe.PrefabPath)).Where(p => p != null).ToArray();
            RosterMember[] convoy = StriderMonowheelBuilder.Singles.Concat(StriderMonowheelBuilder.Doubles)
                .Select(v => Member(RosterRole.Rider, Load<GameObject>(StriderMonowheelBuilder.PrefabPath(v)), MonowheelWeight))
                .Where(m => m.prefab != null)
                .ToArray();

            RosterMember[] members = people.Select(p => Member(RosterRole.Scout, p))
                .Concat(people.Select(p => Member(RosterRole.Warrior, p)))
                .Concat(convoy)
                .ToArray();

            // Dealt, not counted: 2 and 3 different wheels, then all five.
            AuthorRoster("Striders", StriderFactionPath, StriderRosterPath, StriderHostileLinesPath, StriderHostileLines,
                members, StriderHandItemPaths, new[]
                {
                    Tier((RosterRole.Rider, 2)),
                    Tier((RosterRole.Rider, 3)),
                    Tier((RosterRole.Rider, 5)),
                });
        }

        /// <summary>
        /// Writes one tribe's roster and hostile-lines assets, points the faction back at the roster,
        /// and logs every <see cref="RosterValidation"/> problem. Overwrites what it owns, nothing else.
        /// </summary>
        private static void AuthorRoster(string tribe, string factionPath, string rosterPath, string hostileLinesPath,
                                         string[] hostileLines, RosterMember[] members, string[] handItemPaths,
                                         WarPartyTier[] tiers)
        {
            var faction = Load<FactionDefinition>(factionPath);
            if (faction == null) return;

            Directory.CreateDirectory(RosterDir);

            DialogPool hostile = LoadOrCreate<DialogPool>(hostileLinesPath);
            hostile.lines = hostileLines.ToArray();
            EditorUtility.SetDirty(hostile);

            FactionRoster roster = LoadOrCreate<FactionRoster>(rosterPath);
            roster.faction = faction;
            roster.hostileLines = hostile;
            roster.handItems = handItemPaths.Select(Load<InventoryItem>).Where(i => i != null).ToArray();
            roster.members = members;
            roster.warPartyTiers = tiers;
            EditorUtility.SetDirty(roster);

            faction.roster = roster;
            EditorUtility.SetDirty(faction);

            AssetDatabase.SaveAssets();

            foreach (string problem in RosterValidation.Problems(roster))
                Debug.LogError($"[RosterAuthoring] {tribe} roster: {problem}", roster);

            Debug.Log($"[RosterAuthoring] Wrote {rosterPath}: {roster.members.Length} members, " +
                      $"{roster.handItems.Length} hand items, {roster.warPartyTiers.Length} tiers.", roster);
        }

        /// <summary>
        /// Task 1 of the sky tribe plan: the Sky Tribe's <see cref="FactionDefinition"/>, its
        /// relationships and its goodwill registration — everything spacegame-tribe steps 1-2 ask
        /// for. No roster yet; that is a later task.
        ///
        /// Idempotent: re-run any time. The faction asset is overwritten wholesale (it is owned by
        /// this method); the relationship rows and the ledger's tribes list are only ever appended
        /// to, never duplicated.
        /// </summary>
        [MenuItem("Tools/SpaceGame/Agents/Author Sky Tribe Faction")]
        public static void AuthorSkyTribeFaction() =>
            AuthorTribeFaction(SkyFactionPath, "Sky Tribe", SkyTribeHudColor);

        /// <summary>
        /// The Striders' <see cref="FactionDefinition"/> and goodwill registration, authored exactly
        /// as the Sky Tribe's is. Mirroring Sand's rows is the Sky decision too; Sand has none today,
        /// so the Striders get none (spec §2).
        /// </summary>
        [MenuItem("Tools/SpaceGame/Agents/Author Strider Faction")]
        public static void AuthorStriderFaction() =>
            AuthorTribeFaction(StriderFactionPath, "The Striders", StriderHudColor);

        /// <summary>
        /// A neutral tribe's faction asset at <paramref name="path"/>: its name, Neutral default,
        /// HUD colour and ID, Sand's relationship rows mirrored onto it, and its place in the
        /// goodwill ledger's tribes list. Returns null when Sand's faction is missing.
        /// </summary>
        private static FactionDefinition AuthorTribeFaction(string path, string displayName, Color hudColor)
        {
            var sand = Load<FactionDefinition>(SandFactionPath);
            if (sand == null) return null;

            Directory.CreateDirectory("Assets/Game/ScriptableObjects/Factions/Core");

            FactionDefinition faction = LoadOrCreate<FactionDefinition>(path);
            faction.factionName = displayName;
            faction.defaultStance = FactionRelationship.Neutral;
            faction.hudColor = hudColor;

            // OnValidate stamps this from the asset's own GUID once Unity gets around to it; stamped
            // here too so a test reading the asset straight after this method returns never sees an
            // empty ID (FactionDefinition.ID is the save-file key, never the class default).
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (!string.IsNullOrEmpty(guid)) faction.ID = guid;

            EditorUtility.SetDirty(faction);

            int copiedRows = CopyRelationshipRows(sand, faction);

            AssetDatabase.SaveAssets();

            WithWorldSim(sim =>
            {
                var ledger = sim.GetComponent<FactionGoodwillLedger>();
                if (ledger == null)
                {
                    Debug.LogError("[RosterAuthoring] No FactionGoodwillLedger on the NpcWorldSim object.");
                    return;
                }

                var so = new SerializedObject(ledger);
                SerializedProperty tribes = so.FindProperty("tribes");

                for (int i = 0; i < tribes.arraySize; i++)
                    if (tribes.GetArrayElementAtIndex(i).objectReferenceValue == faction)
                        return; // already wired

                int index = tribes.arraySize;
                tribes.InsertArrayElementAtIndex(index);
                tribes.GetArrayElementAtIndex(index).objectReferenceValue = faction;
                so.ApplyModifiedPropertiesWithoutUndo();
            });

            Debug.Log($"[RosterAuthoring] Wrote {path}: Neutral default, " +
                      $"{copiedRows} relationship row(s) mirrored from Sand, ledger wired.", faction);
            return faction;
        }

        /// <summary>
        /// Every non-default row Sand has in GlobalRelationships, mirrored onto <paramref name="to"/>
        /// toward the same other faction with the same stance (plan Task 1: "Sky's relationships
        /// mirror Sand's"). Skips a row between <paramref name="from"/> and <paramref name="to"/>
        /// themselves — two neutral tribes need no row (spacegame-tribe §1) — and any row
        /// <paramref name="to"/> already has toward that other faction, so re-running never doubles a
        /// row up.
        /// </summary>
        private static int CopyRelationshipRows(FactionDefinition from, FactionDefinition to)
        {
            var table = Load<FactionRelationshipTable>(GlobalRelationshipsPath);
            if (table == null) return 0;

            var so = new SerializedObject(table);
            SerializedProperty rows = so.FindProperty("relationships");

            var toCopy = new System.Collections.Generic.List<(FactionDefinition Other, FactionRelationship Stance)>();
            for (int i = 0; i < rows.arraySize; i++)
            {
                SerializedProperty row = rows.GetArrayElementAtIndex(i);
                var a = row.FindPropertyRelative("factionA").objectReferenceValue as FactionDefinition;
                var b = row.FindPropertyRelative("factionB").objectReferenceValue as FactionDefinition;

                FactionDefinition other = a == from ? b : b == from ? a : null;
                if (other == null || other == to) continue;

                toCopy.Add((other, (FactionRelationship)row.FindPropertyRelative("relationship").enumValueIndex));
            }

            int added = 0;
            foreach ((FactionDefinition other, FactionRelationship stance) in toCopy)
            {
                if (HasRow(rows, to, other)) continue;

                int index = rows.arraySize;
                rows.InsertArrayElementAtIndex(index);
                SerializedProperty row = rows.GetArrayElementAtIndex(index);
                row.FindPropertyRelative("factionA").objectReferenceValue = to;
                row.FindPropertyRelative("factionB").objectReferenceValue = other;
                row.FindPropertyRelative("relationship").enumValueIndex = (int)stance;
                added++;
            }

            if (added > 0) so.ApplyModifiedPropertiesWithoutUndo();
            return added;
        }

        private static bool HasRow(SerializedProperty rows, FactionDefinition x, FactionDefinition y)
        {
            for (int i = 0; i < rows.arraySize; i++)
            {
                SerializedProperty row = rows.GetArrayElementAtIndex(i);
                Object a = row.FindPropertyRelative("factionA").objectReferenceValue;
                Object b = row.FindPropertyRelative("factionB").objectReferenceValue;
                if ((a == x && b == y) || (a == y && b == x)) return true;
            }

            return false;
        }

        /// <summary>
        /// Gives the caravans their tribe and adds the Sand, Sky and Strider War Party templates. All copy
        /// the sand nomads' formation; members and tasks are emptied because a war party's people come from
        /// its tier. The Sky party flies in: a skiff for a party its seats fit, else a freighter, out of
        /// the Sky City. The Strider party rides in on monowheels, so it travels folded at their cruise speed.
        /// </summary>
        [MenuItem("Tools/SpaceGame/Agents/Wire War Party Templates")]
        public static void WireWorldSim()
        {
            var sand = Load<FactionDefinition>(SandFactionPath);
            var sky = Load<FactionDefinition>(SkyFactionPath);
            var striders = Load<FactionDefinition>(StriderFactionPath);
            var outlaws = Load<FactionDefinition>(OutlawFactionPath);
            var skiff = Load<GameObject>(SkyVesselBuilder.Skiff.PrefabPath);
            var freighter = Load<GameObject>(SkyVesselBuilder.Freighter.PrefabPath);
            if (sand == null || sky == null || striders == null || outlaws == null || skiff == null || freighter == null) return;

            WithWorldSim(sim =>
            {
                var so = new SerializedObject(sim);
                SerializedProperty templates = so.FindProperty("templates");

                int sandNomads = -1, warParty = -1, skyParty = -1, striderParty = -1;
                for (int i = 0; i < templates.arraySize; i++)
                {
                    SerializedProperty template = templates.GetArrayElementAtIndex(i);
                    string id = template.FindPropertyRelative("id").stringValue;

                    if (id == "nomad-caravan" || id == "sand-nomads")
                        template.FindPropertyRelative("tribe").objectReferenceValue = sand;
                    if (id == "bounty-hunters")
                        template.FindPropertyRelative("tribe").objectReferenceValue = outlaws;
                    if (id == "sand-nomads") sandNomads = i;
                    if (id == SandWarPartyTemplateId) warParty = i;
                    if (id == SkyWarPartyTemplateId) skyParty = i;
                    if (id == StriderWarPartyTemplateId) striderParty = i;
                }

                if (warParty < 0)
                {
                    if (sandNomads < 0)
                    {
                        Debug.LogError("[RosterAuthoring] No 'sand-nomads' template to copy the formation from.");
                        return;
                    }

                    templates.GetArrayElementAtIndex(sandNomads).DuplicateCommand();
                    warParty = sandNomads + 1;
                    if (skyParty > sandNomads) skyParty++;   // the duplicate was inserted before it
                    if (striderParty > sandNomads) striderParty++;
                }

                SetWarParty(templates.GetArrayElementAtIndex(warParty), SandWarPartyTemplateId, "Sand War Party", sand);

                if (skyParty < 0)
                {
                    templates.GetArrayElementAtIndex(warParty).DuplicateCommand();
                    skyParty = warParty + 1;
                    if (striderParty > warParty) striderParty++;   // the duplicate was inserted before it
                }

                SerializedProperty skyTemplate = templates.GetArrayElementAtIndex(skyParty);
                SetWarParty(skyTemplate, SkyWarPartyTemplateId, "Sky War Party", sky);
                SerializedProperty transport = skyTemplate.FindPropertyRelative("transport");
                transport.FindPropertyRelative("smallVessel").objectReferenceValue = skiff;
                transport.FindPropertyRelative("largeVessel").objectReferenceValue = freighter;
                transport.FindPropertyRelative("travelSpeed").floatValue = FoldedTravelSpeed(skiff, freighter);
                transport.FindPropertyRelative("homeSiteName").stringValue = WorldSite.SkyCityName;

                // After the Sky block, so the duplicate it may insert shifts no index already used.
                if (striderParty < 0)
                {
                    templates.GetArrayElementAtIndex(warParty).DuplicateCommand();
                    striderParty = warParty + 1;
                }

                SerializedProperty striderTemplate = templates.GetArrayElementAtIndex(striderParty);
                SetWarParty(striderTemplate, StriderWarPartyTemplateId, "Strider War Party", striders);
                // A convoy: folded, it keeps up with where its monowheels would be, and it rides in a shape
                // sized for machines rather than the copied infantry file.
                striderTemplate.FindPropertyRelative("travelSpeed").floatValue = StriderMonowheelBuilder.CruiseSpeed;
                WriteShape(striderTemplate.FindPropertyRelative("formation"), ConvoyShape);

                so.ApplyModifiedPropertiesWithoutUndo();

                if (sim.GetComponent<WarPartyDirector>() == null)
                    sim.gameObject.AddComponent<WarPartyDirector>();
            });
        }

        // The city's stops: salvage, where the workers dig and the crew work the ground.
        private static readonly (string label, SiteKind site, string[] chatter)[] StriderCityStops =
        {
            ("picking over a ruin", SiteKind.Ruin, new[] { "Old metal. Good metal.", "Set the legs down, we're working." }),
            ("working a scrap field", SiteKind.ScrapField, new[] { "Crawlers out! Everything that isn't sand.", "Load the houses." }),
        };
        // Long enough for the crew to walk down the gangway, work the ground and climb back up; the
        // departure gate holds the city anyway until they are aboard.
        private static readonly Vector2 StriderCityDwell = new Vector2(90f, 180f);
        // A 21 m hull stops well short of a site marker; a person-sized arrive radius never arrives.
        private const float StriderCityArriveRadius = 25f;
        // A new world's city stays parked at its start this long, so a player landing 570 m away can walk
        // over and find it there rather than chase it toward a stop 1-2.5 km off (the user, 2026-10-04).
        public const float StriderCityInitialStay = 600f;
        // The houses that carry the city; the first leads. Crew posts are per house, so the crew scales with it.
        // Two: three read as too many walking houses (the user, 2026-10-05).
        public const int StriderCityHouses = 2;
        // The rows a carrier (a house or a barge) may ride in: the ground the city levels at a stop covers
        // them (CityFarthestCarrierSlot). The column's deal keeps every carrier there (StriderCityColumn).
        private const int CityCarrierRows = 6;
        private const int StriderCityWorkers = 2;
        private const int StriderCityOutriders = 2;
        // Monowheel scouts riding with the column; ScoutRota keeps two of them out on a sweep. Shared out
        // over every kind of monowheel in order, one extra each to the first while the remainder lasts
        // (2, 2, 2, 1, 1): every kind rides with the city (the user, 2026-10-05).
        public const int StriderCityScouts = 8;
        // One crewed barge of each kind (StriderBargeBuilder.Barges; the user, 2026-09-25).
        public static int StriderCityBarges => StriderBargeBuilder.Barges.Length;
        // A barge (34 m) is longer than a row is deep: its card keeps it clear of its neighbours at their
        // slots (StriderCityColumn; StriderCityTemplateTests deals the column for many seeds).

        /// <summary>The walking city's column.</summary>
        public static readonly FormationShape CityShape = new FormationShape
        {
            // Two lanes, so the column reads as a street of houses rather than a single file (spec §"strider-city").
            Lanes = 2,
            // Lanes ~30 m apart: a 21 m hull plus its swinging legs, the spacing the walking spike held. Rows
            // 35 m: a barge reaches 22 m behind its pivot, so a scout in the next row of its lane needs that
            // much with every offset at its worst (measured from the prefabs by StriderCityTemplateTests).
            RowSpacing = 35f,
            LaneSpacing = 30f,
            // The caravan's metre-scale jitter and drift, scaled up so the houses visibly stagger at this size
            // while staying well inside the ~9 m gap between hulls.
            LateralJitter = 2f,
            LongitudinalJitter = 3f,
            DriftAmplitude = 1f,
            // A slower sway than a caravan's: a house takes seconds to shift its weight.
            DriftRate = 0.05f,
        };

        /// <summary>Members of the city that hold a slot behind the lead house: the crew ride the carriers instead.</summary>
        public static int CityFollowers =>
            StriderCityHouses - 1 + StriderCityWorkers + StriderCityOutriders + StriderCityBarges + StriderCityScouts;

        /// <summary>
        /// How far from the lead house the city's farthest follower slot can be, jitter and drift
        /// included. A member whose FormationModule.regroupDistance is shorter gives up its slot
        /// and rides for the leader instead, so it can never hold its place in the column.
        /// </summary>
        public static float CityFarthestSlot => FormationMath.FarthestSlot(CityFollowers, CityShape);

        /// <summary>Beyond the column's farthest slot, so a member at the very back still holds its own.</summary>
        private const float CityRegroupMargin = 30f;

        /// <summary>
        /// Every city member's FormationModule.regroupDistance: the column is dealt anew in every world
        /// (<see cref="StriderCityColumn"/>), so any vehicle may ride in its farthest slot. Rebuild the
        /// members after a change to the column.
        /// </summary>
        public static float CityRegroupDistance => CityFarthestSlot + CityRegroupMargin;

        /// <summary>The follower slots a carrier may take: the first <see cref="CityCarrierRows"/> rows.</summary>
        public static int CityCarrierSlots => CityCarrierRows * CityShape.Lanes;

        /// <summary>Whether <paramref name="prefab"/> carries crew and stands still for a whole stop: a house or a barge.</summary>
        public static bool IsCityCarrier(GameObject prefab) =>
            IsAt(prefab, StriderCityBuilder.HabitatPath) || IsCityBarge(prefab);

        /// <summary>Whether <paramref name="prefab"/> is one of the city's barges.</summary>
        public static bool IsCityBarge(GameObject prefab) =>
            StriderBargeBuilder.Barges.Any(b => IsAt(prefab, StriderBargeBuilder.PrefabPath(b.Variant)));

        /// <summary>
        /// What a city member may not ride beside or behind (StriderCityColumn): its prefab, or "barge" for
        /// every barge; null for the monowheel scouts, which go anywhere.
        /// </summary>
        public static string CityKind(GameObject prefab)
        {
            if (IsCityBarge(prefab)) return "barge";
            if (CityScoutVariants.Any(v => IsAt(prefab, StriderMonowheelBuilder.PrefabPath(v)))) return null;
            return prefab.name;
        }

        /// <summary>Every kind of monowheel, in the order <see cref="ScoutShare"/> shares the city's scouts out.</summary>
        private static string[] CityScoutVariants => StriderMonowheelBuilder.Singles.Concat(StriderMonowheelBuilder.Doubles).ToArray();

        private static bool IsAt(GameObject prefab, string path) => AssetDatabase.GetAssetPath(prefab) == path;

        /// <summary>
        /// How far from the lead house the farthest carrier -- a house or a barge, whatever puts a crew
        /// ashore and stands still for the whole stop -- can be. The ground the city needs level at a stop
        /// (<see cref="StriderCityBuilder.CityLevelGround"/>); the crawlers, crabs and scouts behind manage
        /// a slope.
        /// </summary>
        public static float CityFarthestCarrierSlot => FormationMath.FarthestSlot(CityCarrierSlots, CityShape);

        /// <summary>
        /// A war party of monowheels: two abreast, sized from the wheels' footprints (the builder's
        /// colliders: 7.3-8.9 m long, 1.8 m wide for a single, up to 5.7 m for the doubles) so no two share ground even at the
        /// extremes of their jitter and drift. StriderWarPartyTemplateTests measures the prefabs against it.
        /// </summary>
        public static readonly FormationShape ConvoyShape = new FormationShape
        {
            Lanes = 2,
            // The longest wheel plus a wheel-length gap: room to brake behind the one in front.
            RowSpacing = 18f,
            // The widest double (5.7 m) plus more than its width of clear ground: wide enough that two
            // neighbours each parked slotTolerance (4 m) off their slots towards each other still clear.
            LaneSpacing = 14f,
            // A convoy's habitual offsets: small next to the gaps, so rows never swap.
            LateralJitter = 1.5f,
            LongitudinalJitter = 2f,
            DriftAmplitude = 1f,
            // Faster than the city's sway: a monowheel weaves.
            DriftRate = 0.1f,
        };

        /// <summary>
        /// The Striders' one walking city: two houses (the first leads), two worker crawlers, two crab
        /// outriders, three crewed barges, eight monowheel scouts and a crew that fills every crew post on
        /// the houses and barges.
        /// Carriers are listed before the crew because NpcWorldSim seats each crew member on a carrier already spawned.
        /// The vehicles behind the lead house are dealt into a column at spawn from the group's per-world
        /// seed (<see cref="ColumnDeal"/>, cards from <see cref="StriderCityColumn"/>); it starts a short
        /// walk from the player's spawn (<see cref="StriderCityStartSite"/>).
        /// Idempotent: finds its template by id and rewrites every field it owns.
        /// </summary>
        [MenuItem("Tools/SpaceGame/Agents/Wire Strider City")]
        public static void WireStriderCity()
        {
            var striders = Load<FactionDefinition>(StriderFactionPath);
            var habitat = Load<GameObject>(StriderCityBuilder.HabitatPath);
            var crawler = Load<GameObject>(DesertCrawlerBuilder.PrefabPath);
            var crab = Load<GameObject>(StriderCrabOutriderBuilder.PrefabPath);
            var elder = Load<GameObject>(StriderElderBuilder.PrefabPath);
            GameObject[] scouts = CityScoutVariants
                .Select(v => Load<GameObject>(StriderMonowheelBuilder.PrefabPath(v))).ToArray();
            GameObject[] barges = StriderBargeBuilder.Barges
                .Select(b => Load<GameObject>(StriderBargeBuilder.PrefabPath(b.Variant))).ToArray();
            if (striders == null || habitat == null || crawler == null || crab == null || elder == null
                || scouts.Any(s => s == null) || barges.Any(b => b == null)) return;

            // Where the user asked for it rather than at a Ruin: see StriderCityStartSite.
            if (!StriderCityStartSite.TryChoose(out Vector3 start)) return;

            // Every vehicle behind the lead house and how many of it: a card each in the column's deck.
            List<(GameObject prefab, int count)> followers = new List<(GameObject, int)>
                {
                    (habitat, StriderCityHouses - 1),
                    (crawler, StriderCityWorkers),
                    (crab, StriderCityOutriders),
                }
                .Concat(barges.Select(barge => (barge, 1)))
                .Concat(scouts.Select((scout, i) => (scout, ScoutShare(i, scouts.Length))))
                .ToList();
            ColumnCard leaderCard = StriderCityColumn.Card(habitat, shuffled: false);
            var cards = followers.ToDictionary(f => f.prefab, f => StriderCityColumn.Card(f.prefab, shuffled: true));
            // Caught here rather than at a spawn: a deck no shuffle can deal (ColumnDeal logs why).
            List<ColumnCard> deck = followers.SelectMany(f => Enumerable.Repeat(cards[f.prefab], f.count)).ToList();
            if (!ColumnDeal.TryDeal(leaderCard, deck, Enumerable.Range(0, deck.Count).ToList(), CityShape,
                                    RosterDraw.StableHash(StriderCityTemplateId), out _)) return;

            WithWorldSim(sim =>
            {
                var so = new SerializedObject(sim);
                SerializedProperty t = FindOrCopySandNomads(so.FindProperty("templates"), StriderCityTemplateId);
                if (t == null) return;

                t.FindPropertyRelative("id").stringValue = StriderCityTemplateId;
                t.FindPropertyRelative("displayName").stringValue = "Strider City";
                t.FindPropertyRelative("tribe").objectReferenceValue = striders;
                t.FindPropertyRelative("runtimeOnly").boolValue = false;
                t.FindPropertyRelative("bountyHunters").boolValue = false;
                // Seen marching from the edge of the loaded ground (DistantGroupSilhouette, SettlementLods.md).
                t.FindPropertyRelative("showFromAfar").boolValue = true;
                t.FindPropertyRelative("useStartPosition").boolValue = true;
                t.FindPropertyRelative("startPosition").vector3Value = start;
                t.FindPropertyRelative("initialStaySeconds").floatValue = StriderCityInitialStay;
                t.FindPropertyRelative("startNearSite").enumValueIndex = (int)SiteKind.Ruin;
                t.FindPropertyRelative("travelSpeed").floatValue = StriderCityBuilder.CityLeaderSpeed;

                // It walks: no vessel, whatever the copied template carried.
                SerializedProperty transport = t.FindPropertyRelative("transport");
                transport.FindPropertyRelative("smallVessel").objectReferenceValue = null;
                transport.FindPropertyRelative("largeVessel").objectReferenceValue = null;

                SerializedProperty members = t.FindPropertyRelative("members");
                members.arraySize = 0;
                AddMember(members, habitat, RosterRole.Scout, 1, leader: true, crew: false, leaderCard);
                // Listed by kind; each world deals them into its own column. The crabs are a fixed
                // prefab, never the roster's Rider deal: that is the war parties' monowheels.
                foreach ((GameObject vehicle, int count) in followers)
                    AddMember(members, vehicle, vehicle == crab ? RosterRole.Rider : RosterRole.Scout, count, leader: false, crew: false,
                              cards[vehicle]);
                // Half fighters, half scouts, one per crew post on every carrier; an odd post goes to a scout.
                int crew = StriderCityHouses * StriderCityBuilder.CrewPosts + barges.Length * StriderBargeBuilder.CrewPosts;
                int fighters = crew / 2;
                AddMember(members, null, RosterRole.Warrior, fighters, leader: false, crew: true);
                AddMember(members, null, RosterRole.Scout, crew - fighters, leader: false, crew: true);
                // The elders last: their count is drawn, and a drawn count shifts the plan index (and
                // with it the roster draw and loadout) of every member listed after it.
                AddMember(members, elder, RosterRole.Scout, 1, leader: false, crew: true);
                WriteCountWeights(members.GetArrayElementAtIndex(members.arraySize - 1), StriderCityElders);

                SerializedProperty tasks = t.FindPropertyRelative("tasks");
                tasks.arraySize = StriderCityStops.Length;
                for (int i = 0; i < StriderCityStops.Length; i++)
                {
                    SerializedProperty task = tasks.GetArrayElementAtIndex(i);
                    WriteStop(task, StriderCityStops[i], StriderCityBuilder.CitySearchRadius, StriderCityDwell,
                              StriderCityArriveRadius, StriderCityBuilder.CityTravelMultiplier);
                    WriteLevelGround(task.FindPropertyRelative("levelGround"), StriderCityBuilder.CityLevelGround);
                }

                WriteShape(t.FindPropertyRelative("formation"), CityShape);

                so.ApplyModifiedPropertiesWithoutUndo();
            });
        }

        public const string SkyWingTemplateId = "sky-wing";

        // Folded, the record travels at the craft's cruise ground speed: Spike 5.2a E4 cruised 22.7-28.3 m/s.
        private const float SkyWingFoldedSpeed = 22f;
        private const float SkyWingSearchRadius = 700f;
        private const float SkyWingArriveRadius = 12f;
        private const float SkyWingTravelMultiplier = 1f;

        // Where the wing is seeded: the ground NavMesh under the Sky City's first mooring (route[0],
        // 3350/1400). No Ruin, ScrapField or Camp site exists to seed it at, and startNearSite with
        // none falls back to the sim's own origin, under the terrain. Verified 2026-10-06 against
        // WorldNavMesh.asset: terrain 110.1, NavMesh 110.3.
        internal static readonly Vector3 SkyWingStart = new Vector3(3350f, 110.3f, 1400f);
        private static readonly Vector2 SkyWingDwell = new Vector2(20f, 45f);
        private static readonly (string label, SiteKind site, string[] chatter)[] SkyWingStops =
        {
            ("looking over a ruin", SiteKind.Ruin, new[] { "Saw this from the city. Worth a look." }),
            ("picking through a scrap field", SiteKind.ScrapField, new[] { "Light pieces only. We have to fly it home." }),
            ("visiting a camp", SiteKind.Camp, new[] { "Ground folk. Keep your wings folded and your hands empty." }),
        };

        /// <summary>
        /// A pair of Sky scouts that hop between ground sites on their wing packs: the ground-to-ground
        /// flights of D2. Seeded at startup at <see cref="SkyWingStart"/>; a stop whose site kind does
        /// not exist is a roam point instead (NpcTaskPlanner). Each leg too long to walk is flown
        /// (NpcFlightModule), each pilot alone to the shared goal (D5). Idempotent: rewrites its template by id.
        /// </summary>
        [MenuItem("Tools/SpaceGame/Agents/Wire Sky Wing")]
        public static void WireSkyWing()
        {
            var sky = Load<FactionDefinition>(SkyFactionPath);
            if (sky == null) return;

            WithWorldSim(sim =>
            {
                var so = new SerializedObject(sim);
                SerializedProperty t = FindOrCopySandNomads(so.FindProperty("templates"), SkyWingTemplateId);
                if (t == null) return;

                t.FindPropertyRelative("id").stringValue = SkyWingTemplateId;
                t.FindPropertyRelative("displayName").stringValue = "Sky Wing";
                t.FindPropertyRelative("tribe").objectReferenceValue = sky;
                t.FindPropertyRelative("runtimeOnly").boolValue = false;
                t.FindPropertyRelative("bountyHunters").boolValue = false;
                t.FindPropertyRelative("showFromAfar").boolValue = false;
                t.FindPropertyRelative("useStartPosition").boolValue = true;
                t.FindPropertyRelative("startPosition").vector3Value = SkyWingStart;
                t.FindPropertyRelative("initialStaySeconds").floatValue = 0f;
                t.FindPropertyRelative("travelSpeed").floatValue = SkyWingFoldedSpeed;

                // It flies on wing packs: no vessel, whatever the copied template carried.
                SerializedProperty transport = t.FindPropertyRelative("transport");
                transport.FindPropertyRelative("smallVessel").objectReferenceValue = null;
                transport.FindPropertyRelative("largeVessel").objectReferenceValue = null;

                SerializedProperty members = t.FindPropertyRelative("members");
                members.arraySize = 0;
                AddMember(members, null, RosterRole.Scout, 1, leader: true, crew: false);
                AddMember(members, null, RosterRole.Scout, 1, leader: false, crew: false);

                SerializedProperty tasks = t.FindPropertyRelative("tasks");
                tasks.arraySize = SkyWingStops.Length;
                for (int i = 0; i < SkyWingStops.Length; i++)
                    WriteStop(tasks.GetArrayElementAtIndex(i), SkyWingStops[i], SkyWingSearchRadius, SkyWingDwell,
                              SkyWingArriveRadius, SkyWingTravelMultiplier);

                so.ApplyModifiedPropertiesWithoutUndo();
            });
        }

        public const string SkyPatrolTemplateId = "sky-patrol";

        /// <summary>
        /// The patrol's loop: a ~5 km figure through the low basin — the western lobe the player spawns in (passing
        /// the spawn point at (2610, 880) about 130 m off, twice a lap), the southern flats, and the eastern lobe
        /// towards the Sand camps — threading round the rock spires (x≈2900 and x≈3750 near z≈1150) and the ridge
        /// at x 3100–3250, z 1250–2000. On the plain (y 110); NpcAirPatrol takes the real ground under the record
        /// when it spawns. Measured 2026-10-07 against the world NavMesh: no leg's corridor rises above 125 m
        /// (SkyPatrolContentTests holds it under 150).
        /// </summary>
        internal static readonly Vector3[] SkyPatrolRoute =
        {
            new Vector3(2600f, 110f, 1600f),
            new Vector3(2450f, 110f, 1050f),
            new Vector3(2550f, 110f, 450f),
            new Vector3(3150f, 110f, 400f),
            new Vector3(3450f, 110f, 750f),
            new Vector3(3450f, 110f, 1450f),   // the nearest it comes to the Sand camps
            new Vector3(3200f, 110f, 850f),
            new Vector3(2650f, 110f, 800f),
        };

        /// <summary>Wingmen behind the leader: two to four, so a patrol is three to five fliers.</summary>
        internal static readonly WeightedCount[] SkyPatrolWingmen =
        {
            new WeightedCount { count = 2, weight = 0.35f },
            new WeightedCount { count = 3, weight = 0.40f },
            new WeightedCount { count = 4, weight = 0.25f },
        };

        // The leader cruises at this share of the craft's top speed; folded, the record moves at the same speed.
        internal const float SkyPatrolLeaderSpeed = 0.7f;

        /// <summary>
        /// A Sky patrol: three to five fliers in a chevron, always in the air, flying a loop over the basin — the
        /// airborne twin of the Sand riders' caravans (user, 2026-10-07: "I want to sometimes see flyers flying idle
        /// through the sky in formation"). Seeded at the loop's first point; never lands (NpcAirPatrol).
        /// Idempotent: rewrites its template by id.
        /// </summary>
        [MenuItem("Tools/SpaceGame/Agents/Wire Sky Patrol")]
        public static void WireSkyPatrol()
        {
            var sky = Load<FactionDefinition>(SkyFactionPath);
            if (sky == null) return;

            WithWorldSim(sim =>
            {
                var so = new SerializedObject(sim);
                SerializedProperty t = FindOrCopySandNomads(so.FindProperty("templates"), SkyPatrolTemplateId);
                if (t == null) return;

                t.FindPropertyRelative("id").stringValue = SkyPatrolTemplateId;
                t.FindPropertyRelative("displayName").stringValue = "Sky Patrol";
                t.FindPropertyRelative("tribe").objectReferenceValue = sky;
                t.FindPropertyRelative("runtimeOnly").boolValue = false;
                t.FindPropertyRelative("bountyHunters").boolValue = false;
                t.FindPropertyRelative("showFromAfar").boolValue = false;
                t.FindPropertyRelative("useStartPosition").boolValue = true;
                t.FindPropertyRelative("startPosition").vector3Value = SkyPatrolRoute[0];
                t.FindPropertyRelative("initialStaySeconds").floatValue = 0f;
                t.FindPropertyRelative("travelSpeed").floatValue = SkyPatrolLeaderSpeed * NpcOrnithopterBuilder.CruiseSpeed;
                t.FindPropertyRelative("tasks").arraySize = 0;

                // It flies its own craft: no vessel, whatever the copied template carried.
                SerializedProperty transport = t.FindPropertyRelative("transport");
                transport.FindPropertyRelative("smallVessel").objectReferenceValue = null;
                transport.FindPropertyRelative("largeVessel").objectReferenceValue = null;

                SerializedProperty patrol = t.FindPropertyRelative("airPatrol");
                SerializedProperty route = patrol.FindPropertyRelative("route");
                route.arraySize = SkyPatrolRoute.Length;
                for (int i = 0; i < SkyPatrolRoute.Length; i++)
                    route.GetArrayElementAtIndex(i).vector3Value = SkyPatrolRoute[i];
                patrol.FindPropertyRelative("leaderSpeed").floatValue = SkyPatrolLeaderSpeed;

                SerializedProperty members = t.FindPropertyRelative("members");
                members.arraySize = 0;
                AddMember(members, null, RosterRole.Scout, 1, leader: true, crew: false);
                AddMember(members, null, RosterRole.Warrior, 1, leader: false, crew: false);
                WriteCountWeights(members.GetArrayElementAtIndex(1), SkyPatrolWingmen);

                so.ApplyModifiedPropertiesWithoutUndo();
            });
        }

        /// <summary>
        /// The template with <paramref name="id"/>, or a fresh copy of 'sand-nomads' right after it (the
        /// caller renames it). Null, logged, when neither exists.
        /// </summary>
        private static SerializedProperty FindOrCopySandNomads(SerializedProperty templates, string id)
        {
            int found = -1, source = -1;
            for (int i = 0; i < templates.arraySize; i++)
            {
                string each = templates.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue;
                if (each == id) found = i;
                if (each == "sand-nomads") source = i;
            }
            if (found >= 0) return templates.GetArrayElementAtIndex(found);

            if (source < 0)
            {
                Debug.LogError($"[RosterAuthoring] No 'sand-nomads' template to copy for '{id}'.");
                return null;
            }
            templates.GetArrayElementAtIndex(source).DuplicateCommand();
            return templates.GetArrayElementAtIndex(source + 1);
        }

        /// <summary>One stop of a group's errand list: every NpcTask field but the level-ground rule.</summary>
        private static void WriteStop(SerializedProperty task, (string label, SiteKind site, string[] chatter) stop,
                                      float searchRadius, Vector2 dwell, float arriveRadius, float travelMultiplier)
        {
            task.FindPropertyRelative("label").stringValue = stop.label;
            task.FindPropertyRelative("targetSite").enumValueIndex = (int)stop.site;
            task.FindPropertyRelative("searchRadius").floatValue = searchRadius;
            task.FindPropertyRelative("searchFromHome").boolValue = false;
            task.FindPropertyRelative("dwellSeconds").vector2Value = dwell;
            task.FindPropertyRelative("yields").objectReferenceValue = null;
            task.FindPropertyRelative("yieldChance").floatValue = 1f;
            task.FindPropertyRelative("dwellFlag").stringValue = string.Empty;
            task.FindPropertyRelative("weight").floatValue = 1f;
            task.FindPropertyRelative("arriveRadius").floatValue = arriveRadius;
            task.FindPropertyRelative("travelSpeedMultiplier").floatValue = travelMultiplier;
            SerializedProperty chatter = task.FindPropertyRelative("chatter");
            chatter.arraySize = stop.chatter.Length;
            for (int c = 0; c < chatter.arraySize; c++)
                chatter.GetArrayElementAtIndex(c).stringValue = stop.chatter[c];
        }

        private static void WriteLevelGround(SerializedProperty rule, LevelGroundRule r)
        {
            rule.FindPropertyRelative("footprintRadius").floatValue = r.footprintRadius;
            rule.FindPropertyRelative("maxSlopeDegrees").floatValue = r.maxSlopeDegrees;
            rule.FindPropertyRelative("searchRadius").floatValue = r.searchRadius;
            rule.FindPropertyRelative("searchStep").floatValue = r.searchStep;
            rule.FindPropertyRelative("attempts").intValue = r.attempts;
            rule.FindPropertyRelative("sampleReach").floatValue = r.sampleReach;
            rule.FindPropertyRelative("sampleTolerance").floatValue = r.sampleTolerance;
        }

        internal static void WriteShape(SerializedProperty shape, FormationShape s)
        {
            shape.FindPropertyRelative("Lanes").intValue = s.Lanes;
            shape.FindPropertyRelative("RowSpacing").floatValue = s.RowSpacing;
            shape.FindPropertyRelative("LaneSpacing").floatValue = s.LaneSpacing;
            shape.FindPropertyRelative("LateralJitter").floatValue = s.LateralJitter;
            shape.FindPropertyRelative("LongitudinalJitter").floatValue = s.LongitudinalJitter;
            shape.FindPropertyRelative("DriftAmplitude").floatValue = s.DriftAmplitude;
            shape.FindPropertyRelative("DriftRate").floatValue = s.DriftRate;
        }

        /// <summary>The <paramref name="index"/>-th monowheel kind's share of <see cref="StriderCityScouts"/>; the first ones take one extra each while the remainder lasts.</summary>
        internal static int ScoutShare(int index, int kinds) =>
            StriderCityScouts / kinds + (index < StriderCityScouts % kinds ? 1 : 0);

        /// <summary>The city's elders: usually one, sometimes two (one per house at most).</summary>
        public static readonly WeightedCount[] StriderCityElders =
        {
            new WeightedCount { count = 1, weight = 0.75f },
            new WeightedCount { count = 2, weight = 0.25f },
        };

        private static void WriteCountWeights(SerializedProperty member, WeightedCount[] weights)
        {
            SerializedProperty array = member.FindPropertyRelative("countWeights");
            array.arraySize = weights.Length;
            for (int i = 0; i < weights.Length; i++)
            {
                array.GetArrayElementAtIndex(i).FindPropertyRelative("count").intValue = weights[i].count;
                array.GetArrayElementAtIndex(i).FindPropertyRelative("weight").floatValue = weights[i].weight;
            }
        }

        private static void AddMember(SerializedProperty members, GameObject prefab, RosterRole role, int count, bool leader, bool crew,
                                      ColumnCard column = default)
        {
            int i = members.arraySize;
            members.InsertArrayElementAtIndex(i);
            SerializedProperty m = members.GetArrayElementAtIndex(i);
            m.FindPropertyRelative("prefab").objectReferenceValue = prefab;
            m.FindPropertyRelative("role").enumValueIndex = (int)role;
            m.FindPropertyRelative("count").intValue = count;
            m.FindPropertyRelative("isLeader").boolValue = leader;
            m.FindPropertyRelative("crew").boolValue = crew;
            // InsertArrayElementAtIndex copies its neighbour: an elder's weights must not leak into the next spec.
            m.FindPropertyRelative("countWeights").arraySize = 0;
            SerializedProperty card = m.FindPropertyRelative("column");
            card.FindPropertyRelative("shuffled").boolValue = column.shuffled;
            card.FindPropertyRelative("kind").stringValue = column.kind ?? string.Empty;
            card.FindPropertyRelative("withinFirstSlots").intValue = column.withinFirstSlots;
            card.FindPropertyRelative("keepsClear").boolValue = column.keepsClear;
            card.FindPropertyRelative("footprint").rectValue = column.footprint;
            card.FindPropertyRelative("slotTolerance").floatValue = column.slotTolerance;
        }

        /// <summary>
        /// The slower of the vessels' serialized VesselPilot.cruiseSpeed: a folded party is never further
        /// on than whichever vessel spawns for it could have flown. One source for the speed.
        /// </summary>
        private static float FoldedTravelSpeed(params GameObject[] vessels) =>
            vessels.Min(vessel => new SerializedObject(vessel.GetComponent<SpaceGame.Vehicles.VesselPilot>())
                .FindProperty("cruiseSpeed").floatValue);

        /// <summary>A runtime-only hunting template for <paramref name="tribe"/>, walking, with no vessel.</summary>
        private static void SetWarParty(SerializedProperty party, string id, string displayName, FactionDefinition tribe)
        {
            party.FindPropertyRelative("id").stringValue = id;
            party.FindPropertyRelative("displayName").stringValue = displayName;
            party.FindPropertyRelative("tribe").objectReferenceValue = tribe;
            party.FindPropertyRelative("runtimeOnly").boolValue = true;
            party.FindPropertyRelative("bountyHunters").boolValue = true;
            party.FindPropertyRelative("useStartPosition").boolValue = false;
            party.FindPropertyRelative("travelSpeed").floatValue = WarPartyWalkSpeed;
            party.FindPropertyRelative("members").arraySize = 0;
            party.FindPropertyRelative("tasks").arraySize = 0;

            SerializedProperty transport = party.FindPropertyRelative("transport");
            transport.FindPropertyRelative("smallVessel").objectReferenceValue = null;
            transport.FindPropertyRelative("largeVessel").objectReferenceValue = null;
        }

        /// <summary>
        /// Open the world scene additively if needed, edit its NpcWorldSim, save, and leave the editor as
        /// it was found — the same dance NomadPrefabBuilder.AddSandNomadCaravan does, for the same reasons.
        /// </summary>
        internal static void WithWorldSim(Action<NpcWorldSim> edit)
        {
            Scene scene = SceneManager.GetSceneByPath(WorldScenePath);
            bool alreadyOpen = scene.IsValid() && scene.isLoaded;
            if (!alreadyOpen) scene = EditorSceneManager.OpenScene(WorldScenePath, OpenSceneMode.Additive);

            NpcWorldSim sim = scene.GetRootGameObjects()
                .Select(g => g.GetComponentInChildren<NpcWorldSim>(true))
                .FirstOrDefault(s => s != null);

            if (sim == null)
                Debug.LogError($"[RosterAuthoring] No NpcWorldSim in {WorldScenePath}.");
            else
            {
                edit(sim);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            if (!alreadyOpen) EditorSceneManager.CloseScene(scene, true);
        }

        private static RosterMember Member(RosterRole role, GameObject prefab, float weight = 1f) =>
            new RosterMember { role = role, prefab = prefab, weight = weight };

        private static WarPartyTier Tier(params (RosterRole role, int count)[] roles) => new WarPartyTier
        {
            roles = roles.Select(r => new RoleCount { role = r.role, count = r.count }).ToArray(),
        };

        /// <summary><paramref name="tier"/> plus <paramref name="count"/> of <paramref name="role"/> flying on their own wings.</summary>
        private static WarPartyTier WithFliers(WarPartyTier tier, RosterRole role, int count) => new WarPartyTier
        {
            roles = tier.roles.Append(new RoleCount { role = role, count = count, ownWings = true }).ToArray(),
        };

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) Debug.LogError($"[RosterAuthoring] Missing {typeof(T).Name} at {path}.");
            return asset;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
