// Authors the Sand and Sky Tribe rosters from what their nomads already are, and (Tasks 4 and 8) wires the
// world sim to it. Idempotent: re-run it any time; it overwrites what it owns and nothing else.
//
// Run from: Tools > SpaceGame > Agents > Author Sand Tribe Roster / Author Sky Tribe Roster
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

            AuthorRoster("Sand", SandFactionPath, SandRosterPath, SandHostileLinesPath, SandHostileLines, members, new[]
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

            AuthorRoster("Sky", SkyFactionPath, SkyRosterPath, SkyHostileLinesPath, SkyHostileLines, members, new[]
            {
                Tier((RosterRole.Scout, 2)),
                Tier((RosterRole.Warrior, 3), (RosterRole.Scout, 1)),
                Tier((RosterRole.Warrior, 5), (RosterRole.Scout, 2)),
            });
        }

        /// <summary>
        /// Writes one tribe's roster and hostile-lines assets, points the faction back at the roster,
        /// and logs every <see cref="RosterValidation"/> problem. Overwrites what it owns, nothing else.
        /// </summary>
        private static void AuthorRoster(string tribe, string factionPath, string rosterPath, string hostileLinesPath,
                                         string[] hostileLines, RosterMember[] members, WarPartyTier[] tiers)
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
            roster.handItems = NomadHandItemPaths.Select(Load<InventoryItem>).Where(i => i != null).ToArray();
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
        public static void AuthorSkyTribeFaction()
        {
            var sand = Load<FactionDefinition>(SandFactionPath);
            if (sand == null) return;

            Directory.CreateDirectory("Assets/Game/ScriptableObjects/Factions/Core");

            FactionDefinition sky = LoadOrCreate<FactionDefinition>(SkyFactionPath);
            sky.factionName = "Sky Tribe";
            sky.defaultStance = FactionRelationship.Neutral;
            sky.hudColor = SkyTribeHudColor;

            // OnValidate stamps this from the asset's own GUID once Unity gets around to it; stamped
            // here too so a test reading the asset straight after this method returns never sees an
            // empty ID (FactionDefinition.ID is the save-file key, never the class default).
            string guid = AssetDatabase.AssetPathToGUID(SkyFactionPath);
            if (!string.IsNullOrEmpty(guid)) sky.ID = guid;

            EditorUtility.SetDirty(sky);

            int copiedRows = CopyRelationshipRows(sand, sky);

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
                    if (tribes.GetArrayElementAtIndex(i).objectReferenceValue == sky)
                        return; // already wired

                int index = tribes.arraySize;
                tribes.InsertArrayElementAtIndex(index);
                tribes.GetArrayElementAtIndex(index).objectReferenceValue = sky;
                so.ApplyModifiedPropertiesWithoutUndo();
            });

            Debug.Log($"[RosterAuthoring] Wrote {SkyFactionPath}: Neutral default, " +
                      $"{copiedRows} relationship row(s) mirrored from Sand, ledger wired.", sky);
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
        /// Gives the caravans their tribe and adds the Sand and Sky War Party templates. Both copy the
        /// sand nomads' formation; members and tasks are emptied because a war party's people come from
        /// its tier. The Sky party flies in: a skiff for a party its seats fit, else a freighter, out of
        /// the Sky City.
        /// </summary>
        [MenuItem("Tools/SpaceGame/Agents/Wire War Party Templates")]
        public static void WireWorldSim()
        {
            var sand = Load<FactionDefinition>(SandFactionPath);
            var sky = Load<FactionDefinition>(SkyFactionPath);
            var outlaws = Load<FactionDefinition>(OutlawFactionPath);
            var skiff = Load<GameObject>(SkyVesselBuilder.Skiff.PrefabPath);
            var freighter = Load<GameObject>(SkyVesselBuilder.Freighter.PrefabPath);
            if (sand == null || sky == null || outlaws == null || skiff == null || freighter == null) return;

            WithWorldSim(sim =>
            {
                var so = new SerializedObject(sim);
                SerializedProperty templates = so.FindProperty("templates");

                int sandNomads = -1, warParty = -1, skyParty = -1;
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
                }

                SetWarParty(templates.GetArrayElementAtIndex(warParty), SandWarPartyTemplateId, "Sand War Party", sand);

                if (skyParty < 0)
                {
                    templates.GetArrayElementAtIndex(warParty).DuplicateCommand();
                    skyParty = warParty + 1;
                }

                SerializedProperty skyTemplate = templates.GetArrayElementAtIndex(skyParty);
                SetWarParty(skyTemplate, SkyWarPartyTemplateId, "Sky War Party", sky);
                SerializedProperty transport = skyTemplate.FindPropertyRelative("transport");
                transport.FindPropertyRelative("smallVessel").objectReferenceValue = skiff;
                transport.FindPropertyRelative("largeVessel").objectReferenceValue = freighter;
                transport.FindPropertyRelative("travelSpeed").floatValue = FoldedTravelSpeed(skiff, freighter);
                transport.FindPropertyRelative("homeSiteName").stringValue = WorldSite.SkyCityName;

                so.ApplyModifiedPropertiesWithoutUndo();

                if (sim.GetComponent<WarPartyDirector>() == null)
                    sim.gameObject.AddComponent<WarPartyDirector>();
            });
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
        private static void WithWorldSim(Action<NpcWorldSim> edit)
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

        private static RosterMember Member(RosterRole role, GameObject prefab) =>
            new RosterMember { role = role, prefab = prefab, weight = 1f };

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
