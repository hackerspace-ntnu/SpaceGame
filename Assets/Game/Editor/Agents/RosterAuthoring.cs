// Authors the Sand, Sky Tribe and Strider rosters from what their people already are, and wires the world
// sim to them: the war-party templates and the Striders' walking city. Idempotent: re-run it any time; it overwrites what it owns and nothing else.
//
// Run from: Tools > SpaceGame > Agents > Author Sand Tribe Roster / Author Sky Tribe Roster / Author Strider Roster
using System;
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
        // A war party's Rider draw: a single monowheel three times as often as a double (spec §4).
        private const float SingleMonowheelWeight = 3f;
        private const float DoubleMonowheelWeight = 1f;
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
        /// the freighter, not the skiff.
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
                    Tier((RosterRole.Scout, 2)),
                    Tier((RosterRole.Warrior, 3), (RosterRole.Scout, 1)),
                    Tier((RosterRole.Warrior, 5), (RosterRole.Scout, 2)),
                });
        }

        /// <summary>
        /// The Striders' roster: the four Strider nomads as scouts and warriors, and the five
        /// monowheels as Riders, so every war party is a convoy. A single is drawn three times as
        /// often as a double: about one draw in four brings a driver and three gunners. The crab
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
            RosterMember[] convoy = StriderMonowheelBuilder.Singles
                .Select(v => Member(RosterRole.Rider, Load<GameObject>(StriderMonowheelBuilder.PrefabPath(v)), SingleMonowheelWeight))
                .Concat(StriderMonowheelBuilder.Doubles
                    .Select(v => Member(RosterRole.Rider, Load<GameObject>(StriderMonowheelBuilder.PrefabPath(v)), DoubleMonowheelWeight)))
                .Where(m => m.prefab != null)
                .ToArray();

            RosterMember[] members = people.Select(p => Member(RosterRole.Scout, p))
                .Concat(people.Select(p => Member(RosterRole.Warrior, p)))
                .Concat(convoy)
                .ToArray();

            // Weighted draws, not exact counts: roughly 2 singles / 2 singles + a double / 3 singles +
            // 2 doubles (spec §4). Exact counts would need a role of their own.
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
        // The houses that carry the city; the first leads. Crew posts are per house, so the crew scales with it.
        public const int StriderCityHouses = 3;
        private const int StriderCityWorkers = 2;
        private const int StriderCityOutriders = 2;
        // Monowheel scouts riding with the column; ScoutRota keeps two of them out on a sweep. Shared out
        // over the singles in order, one extra each to the first while the remainder lasts (3, 3, 2).
        public const int StriderCityScouts = 8;

        /// <summary>The walking city's column.</summary>
        public static readonly FormationShape CityShape = new FormationShape
        {
            // Two lanes, so the column reads as a street of houses rather than a single file (spec §"strider-city").
            Lanes = 2,
            // Rows and lanes ~30 m apart: a 21 m hull plus its swinging legs, the spacing the walking spike held.
            RowSpacing = 30f,
            LaneSpacing = 30f,
            // The caravan's metre-scale jitter and drift, scaled up so the houses visibly stagger at this size
            // while staying well inside the ~9 m gap between hulls.
            LateralJitter = 2f,
            LongitudinalJitter = 3f,
            DriftAmplitude = 1f,
            // A slower sway than a caravan's: a house takes seconds to shift its weight.
            DriftRate = 0.05f,
        };

        /// <summary>Members of the city that hold a slot behind the lead house: the crew ride the houses instead.</summary>
        public const int CityFollowers = StriderCityHouses - 1 + StriderCityWorkers + StriderCityOutriders + StriderCityScouts;

        /// <summary>
        /// How far from the lead house the city's farthest follower slot can be, jitter and drift
        /// included. A member whose FormationModule.regroupDistance is shorter gives up its slot
        /// and rides for the leader instead, so it can never hold its place in the column.
        /// </summary>
        public static float CityFarthestSlot => FormationMath.FarthestSlot(CityFollowers, CityShape);

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
        /// The Striders' one walking city: three houses (the first leads), two worker crawlers, eight
        /// monowheel scouts, two crab outriders and a crew that fills every crew post on the houses.
        /// Carriers are listed before the crew because NpcWorldSim seats each crew member on a carrier already spawned.
        /// It starts near the middle of the map (<see cref="StriderCityStartSite"/>).
        /// Idempotent: finds its template by id and rewrites every field it owns.
        /// </summary>
        [MenuItem("Tools/SpaceGame/Agents/Wire Strider City")]
        public static void WireStriderCity()
        {
            var striders = Load<FactionDefinition>(StriderFactionPath);
            var habitat = Load<GameObject>(StriderCityBuilder.HabitatPath);
            var crawler = Load<GameObject>(DesertCrawlerBuilder.PrefabPath);
            var crab = Load<GameObject>(StriderCrabOutriderBuilder.PrefabPath);
            GameObject[] scouts = StriderMonowheelBuilder.Singles
                .Select(v => Load<GameObject>(StriderMonowheelBuilder.PrefabPath(v))).ToArray();
            if (striders == null || habitat == null || crawler == null || crab == null || scouts.Any(s => s == null)) return;

            // Near the middle of the map rather than at a Ruin: see StriderCityStartSite.
            if (!StriderCityStartSite.TryChoose(out Vector3 start, out _)) return;

            WithWorldSim(sim =>
            {
                var so = new SerializedObject(sim);
                SerializedProperty templates = so.FindProperty("templates");

                int city = -1, source = -1;
                for (int i = 0; i < templates.arraySize; i++)
                {
                    string id = templates.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue;
                    if (id == StriderCityTemplateId) city = i;
                    if (id == "sand-nomads") source = i;
                }

                if (city < 0)
                {
                    if (source < 0)
                    {
                        Debug.LogError("[RosterAuthoring] No 'sand-nomads' template to copy.");
                        return;
                    }

                    templates.GetArrayElementAtIndex(source).DuplicateCommand();
                    city = source + 1;
                }

                SerializedProperty t = templates.GetArrayElementAtIndex(city);
                t.FindPropertyRelative("id").stringValue = StriderCityTemplateId;
                t.FindPropertyRelative("displayName").stringValue = "Strider City";
                t.FindPropertyRelative("tribe").objectReferenceValue = striders;
                t.FindPropertyRelative("runtimeOnly").boolValue = false;
                t.FindPropertyRelative("bountyHunters").boolValue = false;
                t.FindPropertyRelative("useStartPosition").boolValue = true;
                t.FindPropertyRelative("startPosition").vector3Value = start;
                t.FindPropertyRelative("startNearSite").enumValueIndex = (int)SiteKind.Ruin;
                t.FindPropertyRelative("travelSpeed").floatValue = StriderCityBuilder.CityLeaderSpeed;

                // It walks: no vessel, whatever the copied template carried.
                SerializedProperty transport = t.FindPropertyRelative("transport");
                transport.FindPropertyRelative("smallVessel").objectReferenceValue = null;
                transport.FindPropertyRelative("largeVessel").objectReferenceValue = null;

                SerializedProperty members = t.FindPropertyRelative("members");
                members.arraySize = 0;
                AddMember(members, habitat, RosterRole.Scout, 1, leader: true, crew: false);
                AddMember(members, habitat, RosterRole.Scout, StriderCityHouses - 1, leader: false, crew: false);
                AddMember(members, crawler, RosterRole.Scout, StriderCityWorkers, leader: false, crew: false);
                // A fixed prefab, never the roster's Rider draw: that is the war parties' monowheels. Before
                // the scouts, so the slow crabs flank the column beside the crawlers (row 2) and the fast
                // scouts take the rows behind.
                AddMember(members, crab, RosterRole.Rider, StriderCityOutriders, leader: false, crew: false);
                for (int i = 0; i < scouts.Length; i++)
                    AddMember(members, scouts[i], RosterRole.Scout, ScoutShare(i, scouts.Length), leader: false, crew: false);
                // Half fighters, half scouts, one per crew post on every house; an odd post goes to a scout.
                int crew = StriderCityHouses * StriderCityBuilder.CrewPosts;
                int fighters = crew / 2;
                AddMember(members, null, RosterRole.Warrior, fighters, leader: false, crew: true);
                AddMember(members, null, RosterRole.Scout, crew - fighters, leader: false, crew: true);

                SerializedProperty tasks = t.FindPropertyRelative("tasks");
                tasks.arraySize = StriderCityStops.Length;
                for (int i = 0; i < StriderCityStops.Length; i++)
                {
                    SerializedProperty task = tasks.GetArrayElementAtIndex(i);
                    task.FindPropertyRelative("label").stringValue = StriderCityStops[i].label;
                    task.FindPropertyRelative("targetSite").enumValueIndex = (int)StriderCityStops[i].site;
                    task.FindPropertyRelative("searchRadius").floatValue = StriderCityBuilder.CitySearchRadius;
                    task.FindPropertyRelative("searchFromHome").boolValue = false;
                    task.FindPropertyRelative("dwellSeconds").vector2Value = StriderCityDwell;
                    task.FindPropertyRelative("yields").objectReferenceValue = null;
                    task.FindPropertyRelative("yieldChance").floatValue = 1f;
                    task.FindPropertyRelative("dwellFlag").stringValue = string.Empty;
                    task.FindPropertyRelative("weight").floatValue = 1f;
                    task.FindPropertyRelative("arriveRadius").floatValue = StriderCityArriveRadius;
                    task.FindPropertyRelative("travelSpeedMultiplier").floatValue = StriderCityBuilder.CityTravelMultiplier;
                    WriteLevelGround(task.FindPropertyRelative("levelGround"), StriderCityBuilder.CityLevelGround);
                    SerializedProperty chatter = task.FindPropertyRelative("chatter");
                    chatter.arraySize = StriderCityStops[i].chatter.Length;
                    for (int c = 0; c < chatter.arraySize; c++)
                        chatter.GetArrayElementAtIndex(c).stringValue = StriderCityStops[i].chatter[c];
                }

                WriteShape(t.FindPropertyRelative("formation"), CityShape);

                so.ApplyModifiedPropertiesWithoutUndo();
            });
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

        /// <summary>The <paramref name="index"/>-th single's share of <see cref="StriderCityScouts"/>; the first ones take one extra each while the remainder lasts.</summary>
        private static int ScoutShare(int index, int singles) =>
            StriderCityScouts / singles + (index < StriderCityScouts % singles ? 1 : 0);

        private static void AddMember(SerializedProperty members, GameObject prefab, RosterRole role, int count, bool leader, bool crew)
        {
            int i = members.arraySize;
            members.InsertArrayElementAtIndex(i);
            SerializedProperty m = members.GetArrayElementAtIndex(i);
            m.FindPropertyRelative("prefab").objectReferenceValue = prefab;
            m.FindPropertyRelative("role").enumValueIndex = (int)role;
            m.FindPropertyRelative("count").intValue = count;
            m.FindPropertyRelative("isLeader").boolValue = leader;
            m.FindPropertyRelative("crew").boolValue = crew;
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
