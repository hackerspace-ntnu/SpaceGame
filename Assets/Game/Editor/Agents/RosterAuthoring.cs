// Authors the Sand Tribe roster from what the sand nomads already are, and (Tasks 4 and 8) wires the
// world sim to it. Idempotent: re-run it any time; it overwrites what it owns and nothing else.
//
// Run from: Tools > SpaceGame > Agents > Author Sand Tribe Roster
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.Items;
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
        private static readonly string[] SandHandItemPaths =
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

        [MenuItem("Tools/SpaceGame/Agents/Author Sand Tribe Roster")]
        public static void AuthorSandRoster()
        {
            var faction = Load<FactionDefinition>(SandFactionPath);
            if (faction == null) return;

            Directory.CreateDirectory(RosterDir);

            DialogPool hostile = LoadOrCreate<DialogPool>(SandHostileLinesPath);
            hostile.lines = SandHostileLines.ToArray();
            EditorUtility.SetDirty(hostile);

            FactionRoster roster = LoadOrCreate<FactionRoster>(SandRosterPath);
            roster.faction = faction;
            roster.hostileLines = hostile;
            roster.handItems = SandHandItemPaths.Select(Load<InventoryItem>).Where(i => i != null).ToArray();

            GameObject[] people = SandPeople.Select(Load<GameObject>).Where(p => p != null).ToArray();
            GameObject ostrich = Load<GameObject>(NomadOstrichPath);

            // Every nomad can scout or fight; nothing tells one from the other yet. Role-specific
            // people arrive with sub-project 3's art.
            roster.members = people.Select(p => Member(RosterRole.Scout, p))
                .Concat(people.Select(p => Member(RosterRole.Warrior, p)))
                .Concat(ostrich != null ? new[] { Member(RosterRole.Rider, ostrich) } : Array.Empty<RosterMember>())
                .ToArray();

            roster.warPartyTiers = new[]
            {
                Tier((RosterRole.Scout, 2)),
                Tier((RosterRole.Warrior, 3), (RosterRole.Scout, 1)),
                Tier((RosterRole.Warrior, 3), (RosterRole.Rider, 2)),
            };
            EditorUtility.SetDirty(roster);

            faction.roster = roster;
            EditorUtility.SetDirty(faction);

            AssetDatabase.SaveAssets();

            foreach (string problem in RosterValidation.Problems(roster))
                Debug.LogError($"[RosterAuthoring] Sand roster: {problem}", roster);

            Debug.Log($"[RosterAuthoring] Wrote {SandRosterPath}: {roster.members.Length} members, " +
                      $"{roster.handItems.Length} hand items, {roster.warPartyTiers.Length} tiers.", roster);
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
