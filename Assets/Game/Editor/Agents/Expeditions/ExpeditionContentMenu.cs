// Authors the settlement expedition content, idempotently: the global catalog and tuning assets, the nomad
// culture's profile with its Scout goal and the two kits that goal draws (spec §6.1, existing hand tools only),
// its Scout quota, the culture's link to the profile and back, the profile's place in the catalog, and every
// archetype's expedition roles (spec §3.2). The goal and kits are owned by this script: re-running writes them back to the table
// below. The profile's goal list is only appended to; its muster use is the Muster spot use authored here (an
// Assembly spot; Tools/SpaceGame/Expeditions/Place Muster Spots puts one in each settlement).
// Every asset is saved on its own, never with AssetDatabase.SaveAssets (it would flush pending terrain edits),
// and read back afterwards; a mismatch is an error.
using System;
using System.Linq;
using SpaceGame.Agents.Expeditions;
using SpaceGame.Agents.Residents;
using SpaceGame.Items;
using SpaceGame.Presentation;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class ExpeditionContentMenu
    {
        private const string ResourcesDir = "Assets/Game/Resources/Expeditions";
        private const string CatalogPath = ResourcesDir + "/ExpeditionCatalog.asset";
        private const string TuningPath = ResourcesDir + "/ExpeditionTuning.asset";
        private const string ContentDir = "Assets/Game/ScriptableObjects/Expeditions/Nomad";
        private const string ProfilePath = ContentDir + "/NomadExpeditions.asset";
        private const string CulturePath = "Assets/Game/ScriptableObjects/Residents/NomadCulture.asset";
        private const string ArchetypeDir = "Assets/Game/ScriptableObjects/Residents/Archetypes";
        private const string ToolDir = "Assets/Game/Resources/Items/Tools";
        private const string MusterPath = "Assets/Game/ScriptableObjects/Settlements/Spots/Muster.asset";
        // The loop held at the muster, an existing standing cue: 'bored' is waiting (Idle Waiting, Idle Impatient, Idle Calm),
        // which is what a band does until it walks out. No attention cue exists, and none is invented here.
        private const string MusterCuePath = "Assets/Game/ScriptableObjects/Animation/Cues/bored.asset";

        // The Scout goal (spec §3.1, §3.2, §5.2): weight 3, two warriors and one or two scouts, two days of searching.
        // With too few scouts home and rested, any adult stands in for the one the band must have (user, 2026-10-04).
        private const string ScoutGoalId = "scout";
        private const float ScoutWeight = 3f;
        private static readonly Vector2 SearchMinutes = new Vector2(240f, 480f);
        private static readonly Vector2Int SearchWaypoints = new Vector2Int(3, 5);

        // Scouts (the Scout and Lookout archetypes) a settlement keeps, so a band can form while the last scouts rest
        // (user, 2026-10-04): one or two small, three large, in the warrior quota's bed bands.
        private static readonly RoleQuota ScoutQuota = new RoleQuota { role = ExpeditionRole.Scout, small = new Vector2Int(1, 2), large = 3 };

        // Spec §3.2: what each archetype can be on a band. An archetype not listed keeps whatever it has.
        private static readonly (string archetype, ExpeditionRole roles)[] Roles =
        {
            ("Guard", ExpeditionRole.Warrior),
            ("TowerGuard", ExpeditionRole.Warrior),
            ("Hunter", ExpeditionRole.Warrior | ExpeditionRole.Hunter),
            ("Butcher", ExpeditionRole.Hunter),
            ("Herder", ExpeditionRole.Hunter),
            ("Scout", ExpeditionRole.Scout),
            ("Lookout", ExpeditionRole.Scout),
            ("Hauler", ExpeditionRole.Bearer),
            ("Drover", ExpeditionRole.Bearer),
            ("Stablehand", ExpeditionRole.Bearer),
            ("WoodCarrier", ExpeditionRole.Bearer),
            ("OreCarrier", ExpeditionRole.Bearer),
            ("Mechanic", ExpeditionRole.Builder),
            ("Tinker", ExpeditionRole.Builder),
            ("Smith", ExpeditionRole.Builder),
            ("Healer", ExpeditionRole.Healer),
        };

        [MenuItem("Tools/SpaceGame/Expeditions/Author Expedition Content")]
        public static void Run()
        {
            HumanoidControllerBuilder.EnsureFolder(ResourcesDir);
            HumanoidControllerBuilder.EnsureFolder(ContentDir);

            // The catalog first: the profile's OnValidate looks itself up in it.
            var catalog = Ensure<ExpeditionCatalog>(CatalogPath);
            Save(Ensure<ExpeditionTuning>(TuningPath));

            ExpeditionKit warrior = Kit("Kit_Warrior", Tool("Tool_Spear_Bone"), null, Tool("Tool_SignalHorn"));
            ExpeditionKit scout = Kit("Kit_Scout", Tool("Tool_Spear_Stone"), Tool("Tool_Spyglass"));
            ExpeditionGoal goal = ScoutGoal(warrior, scout);

            var culture = Require<SettlementCulture>(CulturePath);
            var profile = Ensure<ExpeditionProfile>(ProfilePath);
            if (!profile.goals.Contains(goal)) profile.goals = profile.goals.Append(goal).ToArray();
            profile.musterUse = Muster();
            profile.culture = culture;
            profile.roleQuotas = new[] { ScoutQuota };
            Save(profile);

            if (catalog.IndexOf(profile) < 0) catalog.profiles = catalog.profiles.Append(profile).ToArray();
            Save(catalog);

            culture.expeditions = profile;
            Save(culture);

            foreach ((string name, ExpeditionRole roles) in Roles)
            {
                ResidentArchetype archetype = Require<ResidentArchetype>($"{ArchetypeDir}/{name}.asset");
                archetype.expeditionRoles = roles;
                Save(archetype);
            }

            Verify(profile, catalog);
        }

        // Where a band musters: an Assembly spot, which the day planner never plans anyone onto; standing, not seated.
        private static SpotUse Muster()
        {
            var muster = Ensure<SpotUse>(MusterPath);
            (muster.displayName, muster.role, muster.holdCue) = ("the muster", SpotRole.Assembly, Require<CharacterCue>(MusterCuePath));
            (muster.seated, muster.seatPrefab, muster.alwaysManned, muster.nightManned, muster.elevated) = (false, null, false, false, false);
            Save(muster);
            return muster;
        }

        private static ExpeditionGoal ScoutGoal(ExpeditionKit warrior, ExpeditionKit scout)
        {
            var goal = Ensure<ExpeditionGoal>($"{ContentDir}/Goal_Scout.asset");
            (goal.id, goal.displayName, goal.weight) = (ScoutGoalId, "scouting", ScoutWeight);
            goal.slots = new[]
            {
                new RoleSlot { role = ExpeditionRole.Warrior, min = 2, max = 2 },
                new RoleSlot { role = ExpeditionRole.Scout, min = 1, max = 2, fillFromAnyAdult = true },
            };
            goal.stages = new[]
            {
                Stage(StageKind.Travel), Search(), Stage(StageKind.Halt),
                Stage(StageKind.Travel), Search(), Stage(StageKind.Halt),
                Stage(StageKind.ReturnHome),
            };
            goal.kits = new[]
            {
                new RoleKit { role = ExpeditionRole.Warrior, kit = warrior },
                new RoleKit { role = ExpeditionRole.Scout, kit = scout },
            };
            Save(goal);
            return goal;
        }

        private static StageSpec Stage(StageKind kind) => new StageSpec { kind = kind };

        private static StageSpec Search() =>
            new StageSpec { kind = StageKind.Search, minutesRange = SearchMinutes, countRange = SearchWaypoints };

        private static ExpeditionKit Kit(string name, InventoryItem weapon, InventoryItem tool, params InventoryItem[] beltItems)
        {
            var kit = Ensure<ExpeditionKit>($"{ContentDir}/{name}.asset");
            (kit.weapon, kit.tool, kit.beltItems) = (weapon, tool, beltItems);
            Save(kit);
            return kit;
        }

        private static InventoryItem Tool(string name) => Require<InventoryItem>($"{ToolDir}/{name}.asset");

        private static T Ensure<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            var made = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(made, path);
            return made;
        }

        private static T Require<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException($"[Expeditions] {path} is missing — the expedition content needs it.");
            return asset;
        }

        private static void Save(UnityEngine.Object asset)
        {
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }

        // ── read it back ─────────────────────────────────────────────────────────────────────────

        private static void Verify(ExpeditionProfile profile, ExpeditionCatalog catalog)
        {
            if (Require<SettlementCulture>(CulturePath).expeditions != profile) Fail("the culture does not point at the profile");
            if (Require<ExpeditionProfile>(ProfilePath).culture != Require<SettlementCulture>(CulturePath)) Fail("the profile does not point at its culture");
            if (Require<ExpeditionCatalog>(CatalogPath).IndexOf(profile) < 0) Fail("the catalog does not list the profile");
            Require<ExpeditionTuning>(TuningPath);
            SpotUse muster = Require<ExpeditionProfile>(ProfilePath).musterUse;
            if (muster == null || AssetDatabase.GetAssetPath(muster) != MusterPath || muster.role != SpotRole.Assembly || muster.seated || muster.holdCue == null)
                Fail("the profile's muster use is not the standing Assembly spot use at " + MusterPath);

            ExpeditionGoal goal = Require<ExpeditionProfile>(ProfilePath).goals.FirstOrDefault(g => g != null && g.id == ScoutGoalId);
            if (goal == null) Fail("the profile has no Scout goal");
            if (goal.slots.Length != 2 || goal.stages.Length != 7 || goal.kits.Any(k => k.kit == null || k.kit.weapon == null))
                Fail("the Scout goal lost its slots, stages or kit weapons on save");
            if (!goal.slots.Any(s => s.role == ExpeditionRole.Scout && s.fillFromAnyAdult))
                Fail("the Scout goal's Scout slot lost fillFromAnyAdult on save");
            RoleQuota[] quotas = Require<ExpeditionProfile>(ProfilePath).roleQuotas;
            if (quotas.Length != 1 || quotas[0].role != ScoutQuota.role || quotas[0].small != ScoutQuota.small || quotas[0].large != ScoutQuota.large)
                Fail("the profile's role quotas are not the Scout quota");

            foreach ((string name, ExpeditionRole roles) in Roles)
                if (Require<ResidentArchetype>($"{ArchetypeDir}/{name}.asset").expeditionRoles != roles)
                    Fail($"archetype {name} did not keep its roles {roles}");

            string summary = $"[Expeditions] Content authored and read back: {profile.name} with {profile.goals.Length} goal(s), " +
                             $"{Roles.Length} archetypes given roles.";
            var problems = ExpeditionValidation.Problems(profile, catalog);
            if (problems.Count == 0) Debug.Log(summary);
            else Debug.LogWarning($"{summary} Still to fix: {string.Join("; ", problems)}");
        }

        private static void Fail(string message) => throw new InvalidOperationException($"[Expeditions] Content verify failed: {message}");
    }
}
