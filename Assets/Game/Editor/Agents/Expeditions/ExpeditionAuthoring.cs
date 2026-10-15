// Prepares settlements and their residents for expeditions. The menus are idempotent and save only the scenes they
// changed, each with EditorSceneManager.SaveScene (never AssetDatabase.SaveAssets, which would flush pending terrain
// edits), then read the saved file back: a change that never reached the file is an error.
//
// Stamp Settlement Identity: every Settlement in every chunk scene of every WorldStreamingConfig gets an authored
// SaveableEntity with a baked id, which is the settlement's id for everything that outlives its chunk. It is scoped
// External: the settlement's expedition state is kept by the director under that id, so the world store gives it no
// record of its own. A settlement whose culture has an expedition profile also gets the SettlementExpeditions that
// performs its bands at home. On a prefab-placed settlement both are added on the scene instance, never on the prefab,
// so two placements never share an id and no prefab file is touched.
//
// Back-fill Resident Source Prefabs: every resident under a settlement in an open scene names the prefab its body is
// an instance of, the prefab an expedition stand-in is spawned from. Generate stamps it on new residents; this is for
// residents generated before the field existed.
//
// Preview / Apply Role Quotas (selected settlement): recasts enough of the settlement's residents to reach each quota of
// its culture's profile, the warrior quota first (ExpeditionRules.WarriorQuota), then every roleQuotas row in order
// (ExpeditionRules.RoleQuotaCount), all over the beds of its dwellings, the count Generate fills. One planner serves
// every quota. Preview only reports; Apply makes exactly the picks Preview listed, because both draw from a seed of the
// settlement id. A recruit for a role is an adult whose body's profile suits an archetype with that role and who holds
// neither that role nor Warrior (a warrior is never recast); the boldest go first (ExpeditionRules.PickRecruits), and
// nobody is recast twice in one run. It becomes the settlement's first fitting archetype with the role (ArchetypeFor).
// Only its archetype changes, as an instance override: its kit (ResidentCarry), derived tuning
// (Resident.ApplyDerivedTuning) and patrol pairing (Companions) are read off the archetype at runtime. Body, name, bed
// and bonds stay. Every resident is an adult until births add children.
//
// Wire Expedition Director: the NpcWorldSim object in persistentScene gets the ExpeditionDirector, its saver and
// the settlement-expedition group template (runtimeOnly, no members of its own, a walking column, not a hunter).
//
// A scene that is already open is edited in place. It is saved only if it had no unsaved edits of its own, because
// saving it would also write whatever the user has in progress; otherwise it is left dirty and the report says so.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Agents;
using SpaceGame.Agents.Expeditions;
using SpaceGame.Agents.Residents;
using SpaceGame.Core.Persistence;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public static class ExpeditionAuthoring
    {
        private const string MenuRoot = "Tools/SpaceGame/Expeditions/";
        private const string Tag = "[ExpeditionAuthoring]";
        // What a prefab instance writes for an overridden sourcePrefab; counted in the saved file to prove the back-fill landed.
        private const string SourcePrefabOverride = "propertyPath: " + nameof(Resident.sourcePrefab);
        private const string PreviewRoleQuotasMenu = "Preview Role Quotas (selected settlement)";
        private const string ApplyRoleQuotasMenu = "Apply Role Quotas (selected settlement)";
        private const string PersistentScenePath = "Assets/Game/Scenes/world/persistentScene.unity";
        // Metres per second a folded band walks: the pace on foot (spec §5.1).
        private const float BandTravelSpeed = 3.5f;

        /// <summary>One pass over one scene: changes it, and says how to find a problem in the saved file (null = none).</summary>
        private delegate int SceneEdit(Scene scene, StringBuilder report, out Func<string, string> problemInFile);

        [MenuItem(MenuRoot + "Stamp Settlement Identity")]
        public static void StampSettlementIdentity()
        {
            if (RefusedInPlayMode()) return;

            var report = new StringBuilder($"{Tag} Stamp Settlement Identity\n");
            List<string> scenes = ChunkScenePaths(report);
            int changed = 0;

            try
            {
                for (int i = 0; i < scenes.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("Stamp Settlement Identity", scenes[i], (float)i / scenes.Count);
                    changed += EditChunkScene(scenes[i], PrepareSettlements, report);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            report.Append($"  {changed} settlement(s) changed across {scenes.Count} chunk scene(s).");
            Debug.Log(report.ToString());
        }

        [MenuItem(MenuRoot + "Back-fill Resident Source Prefabs")]
        public static void BackfillResidentSourcePrefabs()
        {
            if (RefusedInPlayMode()) return;

            var report = new StringBuilder($"{Tag} Back-fill Resident Source Prefabs (open scenes)\n");
            int changed = 0;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) changed += Apply(scene, BackfillSourcePrefabs, openedHere: false, report);
            }

            report.Append($"  {changed} resident(s) back-filled.");
            Debug.Log(report.ToString());
        }

        [MenuItem(MenuRoot + "Wire Expedition Director")]
        public static void WireExpeditionDirector()
        {
            if (RefusedInPlayMode()) return;

            var report = new StringBuilder($"{Tag} Wire Expedition Director\n");
            int changed = EditChunkScene(PersistentScenePath, WireDirector, report);
            report.Append(changed == 0 ? "  Already wired; nothing changed." : $"  {changed} change(s).");
            Debug.Log(report.ToString());
        }

        [MenuItem(MenuRoot + PreviewRoleQuotasMenu)]
        public static void PreviewRoleQuotas() => RoleQuotas(apply: false);

        [MenuItem(MenuRoot + ApplyRoleQuotasMenu)]
        public static void ApplyRoleQuotas() => RoleQuotas(apply: true);

        [MenuItem(MenuRoot + PreviewRoleQuotasMenu, true)]
        [MenuItem(MenuRoot + ApplyRoleQuotasMenu, true)]
        private static bool SettlementSelected() => SelectedSettlement() != null;

        // ── the director ─────────────────────────────────────────────────────────────────────────

        /// <summary>The director and its saver beside the scene's NpcWorldSim, and the band template in the sim's list; each once.</summary>
        private static int WireDirector(Scene scene, StringBuilder report, out Func<string, string> problemInFile)
        {
            problemInFile = text =>
                !text.Contains("::" + typeof(ExpeditionDirector).FullName) ? "it has no ExpeditionDirector"
                : !text.Contains("::" + typeof(ExpeditionSaveable).FullName) ? "it has no ExpeditionSaveable"
                : !text.Contains("- id: " + ExpeditionDirector.TemplateId) ? $"the NpcWorldSim has no '{ExpeditionDirector.TemplateId}' template"
                : null;

            NpcWorldSim sim = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<NpcWorldSim>(true)).FirstOrDefault();
            if (sim == null)
            {
                report.Append($"  ! {scene.name}: no NpcWorldSim, nothing wired\n");
                return 0;
            }

            int changed = 0;
            if (!sim.TryGetComponent(out ExpeditionDirector _))
            {
                sim.gameObject.AddComponent<ExpeditionDirector>();
                report.Append($"  {sim.name}: + ExpeditionDirector\n");
                changed++;
            }
            if (!sim.TryGetComponent(out ExpeditionSaveable _))
            {
                sim.gameObject.AddComponent<ExpeditionSaveable>();
                report.Append($"  {sim.name}: + ExpeditionSaveable\n");
                changed++;
            }
            if (AddBandTemplate(sim))
            {
                report.Append($"  {sim.name}: + template '{ExpeditionDirector.TemplateId}'\n");
                changed++;
            }

            if (changed > 0) EditorSceneManager.MarkSceneDirty(scene);
            return changed;
        }

        /// <summary>
        /// Appends the template bands' groups are created from, unless the list has it. Written whole (boxedValue), so
        /// the new element takes nothing from the one before it, as an inserted array element otherwise would.
        /// </summary>
        private static bool AddBandTemplate(NpcWorldSim sim)
        {
            var so = new SerializedObject(sim);
            SerializedProperty templates = so.FindProperty("templates");
            if (templates == null)
            {
                Debug.LogError($"{Tag} NpcWorldSim has no 'templates' field any more; the band template was not added.", sim);
                return false;
            }

            for (int i = 0; i < templates.arraySize; i++)
                if (templates.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(NpcGroupTemplate.id)).stringValue == ExpeditionDirector.TemplateId)
                    return false;

            templates.arraySize++;
            templates.GetArrayElementAtIndex(templates.arraySize - 1).boxedValue = new NpcGroupTemplate
            {
                id = ExpeditionDirector.TemplateId,
                displayName = "Settlement Expedition",
                runtimeOnly = true,
                members = Array.Empty<NpcGroupMemberSpec>(),
                tasks = Array.Empty<NpcTask>(),
                travelSpeed = BandTravelSpeed,
                bountyHunters = false,
                formation = FormationShape.Column,
            };
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        // ── settlements ──────────────────────────────────────────────────────────────────────────

        private static int PrepareSettlements(Scene scene, StringBuilder report, out Func<string, string> problemInFile)
        {
            var ids = new List<string>();
            int changed = 0, performers = 0;

            foreach (Settlement settlement in SettlementsIn(scene))
            {
                if (PrepareSettlement(settlement, report)) changed++;
                ids.Add(settlement.GetComponent<SaveableEntity>().InstanceId);
                if (RunsBands(settlement)) performers++;
            }

            problemInFile = text =>
            {
                List<string> missing = ids.FindAll(id => !text.Contains(id));
                if (missing.Count > 0) return "settlement id(s) missing from the file: " + string.Join(", ", missing);
                int written = Occurrences(text, "::" + typeof(SettlementExpeditions).FullName);
                return written >= performers ? null : $"{written} SettlementExpeditions for {performers} settlement(s) that run bands";
            };
            return changed;
        }

        /// <summary>Everything one settlement needs before it can run expeditions; true when anything changed.</summary>
        private static bool PrepareSettlement(Settlement settlement, StringBuilder report) =>
            EnsureIdentity(settlement, report) | EnsurePerformer(settlement, report);

        /// <summary>Its culture has an expedition profile, so it sends bands out.</summary>
        private static bool RunsBands(Settlement settlement) => settlement.Culture != null && settlement.Culture.expeditions != null;

        /// <summary>
        /// A settlement that runs bands gets the <see cref="SettlementExpeditions"/> that performs them at home (on a prefab
        /// instance, as an added component, never on the prefab). True when it was added.
        /// </summary>
        private static bool EnsurePerformer(Settlement settlement, StringBuilder report)
        {
            GameObject go = settlement.gameObject;
            if (!RunsBands(settlement) || go.TryGetComponent(out SettlementExpeditions _)) return false;

            Undo.AddComponent<SettlementExpeditions>(go);
            report.Append($"  + {go.scene.name}/{go.name}: SettlementExpeditions added\n");
            return true;
        }

        /// <summary>
        /// An authored <see cref="SaveableEntity"/> with a baked id on the settlement's own GameObject, scoped
        /// <see cref="SaveScope.External"/>: the settlement needs an identity, not a world record. A World record would
        /// restore its pose over an editor move and capture any saver under it that has no entity of its own. Also true
        /// for an entity whose id was stamped in memory when the scene was opened but never saved: the file lacks it.
        /// </summary>
        private static bool EnsureIdentity(Settlement settlement, StringBuilder report)
        {
            GameObject go = settlement.gameObject;
            bool added = !go.TryGetComponent(out SaveableEntity entity);
            if (added) entity = go.AddComponent<SaveableEntity>();

            bool stamped = entity.StampSceneIdentity();
            bool rescoped = entity.Scope != SaveScope.External && ScopeExternal(entity);
            if (!added && !stamped && !rescoped && !EditorUtility.IsDirty(entity)) return false;

            report.Append($"  + {go.scene.name}/{go.name}: id {entity.InstanceId}")
                  .Append(added ? " (SaveableEntity added)" : stamped ? " (identity stamped)" : "")
                  .Append(rescoped ? " (scope set to External)\n" : "\n");
            return true;
        }

        /// <summary>Writes the private scope field (on a prefab instance, as an override); true when it changed.</summary>
        private static bool ScopeExternal(SaveableEntity entity)
        {
            var so = new SerializedObject(entity);
            SerializedFields.SetEnumByName(so, "scope", nameof(SaveScope.External));
            return so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── residents ────────────────────────────────────────────────────────────────────────────

        private static int BackfillSourcePrefabs(Scene scene, StringBuilder report, out Func<string, string> problemInFile)
        {
            int residents = 0, changed = 0, notPlaced = 0;

            foreach (Settlement settlement in SettlementsIn(scene))
            {
                foreach (Resident resident in settlement.GetComponentsInChildren<Resident>(true))
                {
                    GameObject placedFrom = PlacedFrom(resident);
                    if (placedFrom == null)
                    {
                        notPlaced++;
                        continue;
                    }

                    residents++;
                    if (resident.sourcePrefab == placedFrom) continue;

                    Undo.RecordObject(resident, "Back-fill Resident Source Prefabs");
                    resident.sourcePrefab = placedFrom;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(resident);
                    changed++;
                }
            }

            if (residents + notPlaced > 0)
                report.Append($"  {scene.name}: {residents} resident(s) placed from a prefab, {changed} back-filled")
                      .Append(notPlaced > 0 ? $", {notPlaced} left empty (body is not its own prefab instance)\n" : "\n");

            problemInFile = text =>
            {
                int written = Occurrences(text, SourcePrefabOverride);
                return written >= residents ? null : $"{written} sourcePrefab override(s) for {residents} resident(s)";
            };
            return changed;
        }

        /// <summary>
        /// The prefab asset the resident's body was placed from: the source of its own outermost instance root. Null for
        /// a body that is not a prefab instance, or that is part of some larger prefab, where "placed from" means nothing.
        /// </summary>
        private static GameObject PlacedFrom(Resident resident)
        {
            GameObject body = resident.gameObject;
            return PrefabUtility.IsOutermostPrefabInstanceRoot(body) ? PrefabUtility.GetCorrespondingObjectFromSource(body) : null;
        }

        // ── role quotas ──────────────────────────────────────────────────────────────────────────

        /// <summary>A resident a quota recasts, the archetype it is given, and the role that archetype brings.</summary>
        private readonly struct Recruit
        {
            public readonly Resident resident;
            public readonly ResidentArchetype becomes;
            public readonly ExpeditionRole role;

            public Recruit(Resident resident, ResidentArchetype becomes, ExpeditionRole role) =>
                (this.resident, this.becomes, this.role) = (resident, becomes, role);
        }

        /// <summary>One quota the planner fills: a role, how many residents with it the settlement keeps, and the rule behind the count.</summary>
        private readonly struct Quota
        {
            public readonly ExpeditionRole role;
            public readonly int count;
            public readonly string rule;

            public Quota(ExpeditionRole role, int count, string rule) => (this.role, this.count, this.rule) = (role, count, rule);
        }

        private static void RoleQuotas(bool apply)
        {
            if (apply && RefusedInPlayMode()) return;

            Settlement settlement = SelectedSettlement();
            if (settlement == null)
            {
                Debug.LogError($"{Tag} Select a Settlement (or anything under one) in an open scene first.");
                return;
            }

            Scene scene = settlement.gameObject.scene;
            var report = new StringBuilder($"{Tag} {(apply ? "Apply" : "Preview")} Role Quotas: {scene.name}/{settlement.name}\n");
            List<Recruit> recruits = PlanRoleQuotas(settlement, report);
            if (recruits == null)
            {
                Debug.LogError(report.ToString());
                return;
            }

            if (apply && recruits.Count > 0) Apply(scene, Recast(recruits), openedHere: false, report);
            Debug.Log(report.ToString());
        }

        /// <summary>Every quota of <paramref name="profile"/> for a settlement of <paramref name="beds"/>: the warriors first, then its role quotas in order.</summary>
        private static List<Quota> QuotasOf(ExpeditionProfile profile, int beds)
        {
            // In ExpeditionRules.Quotas' order: the warriors, then each role quota.
            var rules = new List<string> { $"{profile.warriorQuotaSmall.x}-{profile.warriorQuotaSmall.y} small, {profile.warriorQuotaLarge} from {profile.largeFromBeds} beds" };
            rules.AddRange(profile.roleQuotas.Select(q => $"{q.small.x}-{q.small.y} small, {q.large} from {profile.largeFromBeds} beds"));
            return ExpeditionRules.Quotas(profile, beds).Select((q, i) => new Quota(q.role, q.count, rules[i])).ToList();
        }

        /// <summary>
        /// The residents to recast and what each becomes, quota by quota in <see cref="QuotasOf"/> order, each quota
        /// counted over the archetypes the earlier ones left, with the counts behind the picks written to
        /// <paramref name="report"/>. Null, with the reason, when the settlement keeps no quota, a quota's role has no
        /// archetype, or the picks cannot be seeded.
        /// </summary>
        private static List<Recruit> PlanRoleQuotas(Settlement settlement, StringBuilder report)
        {
            SettlementCulture culture = settlement.Culture;
            ExpeditionProfile profile = culture != null ? culture.expeditions : null;
            if (profile == null)
            {
                report.Append("  ! its culture names no expedition profile, so it keeps no quota.");
                return null;
            }
            string settlementId = settlement.SettlementId;
            if (string.IsNullOrEmpty(settlementId))
            {
                report.Append($"  ! it has no settlement id to seed the picks from. Run {MenuRoot}Stamp Settlement Identity first.");
                return null;
            }

            Resident[] residents = settlement.GetComponentsInChildren<Resident>(true);
            int beds = settlement.Beds;
            List<Quota> quotas = QuotasOf(profile, beds);
            foreach (Quota quota in quotas)
            {
                if (culture.archetypes.Any(a => Has(a, quota.role))) continue;
                report.Append($"  ! no archetype of {culture.name} has the {quota.role} role. Run {MenuRoot}Author Expedition Content first.");
                return null;
            }

            List<ResidentArchetype> usable = ResidentAssignment.UsableArchetypes(settlement, culture, settlement.Seed);
            int seed = unchecked((int)LineTable.IdOf(settlementId));
            // What each resident is once the picks so far are made, so a later quota counts the earlier recruits.
            Dictionary<Resident, ResidentArchetype> now = residents.ToDictionary(r => r, r => r.archetype);
            var recruits = new List<Recruit>();
            report.Append($"  {beds} bed(s), {residents.Length} resident(s); seed {seed} from settlement id {settlementId}\n");

            foreach (Quota quota in quotas)
            {
                List<Resident> holders = residents.Where(r => Has(now[r], quota.role)).ToList();
                int needed = Mathf.Max(0, quota.count - holders.Count);

                var candidates = new List<RecruitCandidate>(residents.Length);
                var options = new Dictionary<int, Recruit>(residents.Length);
                foreach (Resident resident in residents)
                {
                    ResidentArchetype becomes = ArchetypeFor(quota.role, culture, BodyOf(resident), usable);
                    bool holds = Has(now[resident], quota.role) || Has(now[resident], ExpeditionRole.Warrior) ||
                                 recruits.Any(r => r.resident == resident);
                    candidates.Add(new RecruitCandidate(resident.index, resident.Nerve, isAdult: true,
                                                        suitsRole: becomes != null, holdsRole: holds));
                    options[resident.index] = new Recruit(resident, becomes, quota.role);
                }

                List<Recruit> picks = ExpeditionRules.PickRecruits(candidates, needed, seed).Select(i => options[i]).ToList();
                report.Append($"  {quota.role}: quota {quota.count} ({profile.name}: {quota.rule}); now {holders.Count}: {Tally(holders.Select(r => now[r]))}; ")
                      .Append($"needed {needed}; {candidates.Count(c => c.Eligible)} eligible\n");
                foreach (Recruit pick in picks)
                {
                    Resident resident = pick.resident;
                    report.Append($"    {recruits.Count + 1}. {resident.DisplayName} [{resident.name}] #{resident.index}, nerve {resident.Nerve:0.##}, ")
                          .Append($"body {NameOf(BodyOf(resident))}: {NameOf(now[resident])} -> {pick.becomes.name}\n");
                    now[resident] = pick.becomes;
                    recruits.Add(pick);
                }
                if (picks.Count < needed)
                    report.Append($"    ! only {picks.Count} of {needed} can be given the role: the quota stays {needed - picks.Count} short.\n");
            }

            int patrolNow = residents.Count(r => Patrols(r.archetype));
            int patrolAfter = residents.Count(r => Patrols(now[r]));
            int patrolWanted = ResidentAssignment.PatrolWanted(culture, residents.Length);
            report.Append($"  recast {recruits.Count}: ")
                  .Append(string.Join(", ", quotas.Select(q => $"{q.role} {recruits.Count(r => r.role == q.role)}")))
                  .Append($"\n  patrol {patrolNow} -> {patrolAfter} guard(s); PatrolWanted for {residents.Length} residents is {patrolWanted}: ")
                  .Append(patrolAfter >= patrolWanted ? "holds" : "SHORT").Append(" (an odd one out patrols alone)\n");
            return recruits;
        }

        /// <summary>Gives each recruit its new archetype as an instance override; the saved file must name each one.</summary>
        private static SceneEdit Recast(List<Recruit> recruits) =>
            (Scene scene, StringBuilder report, out Func<string, string> problemInFile) =>
            {
                // Each archetype given must be referenced in the saved file once more per recruit than before.
                string before = File.ReadAllText(scene.path);
                var wanted = recruits.GroupBy(r => r.becomes)
                    .ToDictionary(g => g.Key, g => Occurrences(before, GuidOf(g.Key)) + g.Count());

                Undo.SetCurrentGroupName("Apply Role Quotas");
                foreach (Recruit recruit in recruits)
                {
                    Undo.RecordObject(recruit.resident, "Apply Role Quotas");
                    recruit.resident.archetype = recruit.becomes;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(recruit.resident);
                }

                problemInFile = text =>
                {
                    List<string> missing = wanted.Where(w => Occurrences(text, GuidOf(w.Key)) < w.Value).Select(w => w.Key.name).ToList();
                    return missing.Count == 0 ? null : "too few references to " + string.Join(", ", missing);
                };
                return recruits.Count;
            };

        /// <summary>
        /// What a recruit with this body becomes to hold <paramref name="role"/>; null unless the body's profile suits an
        /// archetype with the role, and null for a body made for one role (<see cref="ResidentAssignment.RoleOf"/>): that
        /// role is never recast. The body decides WHETHER it can hold the role; the settlement decides which archetype.
        /// Of the archetypes with the role this settlement can hand out (<see cref="ResidentAssignment.UsableArchetypes"/>):
        /// one without a post first, since a usable post only means one spot of it exists (a watchtower holds one guard),
        /// not that the spot is free; then a standing duty (Guard-like: a duty needs no spot); then one the body suits; then
        /// the culture's order.
        /// </summary>
        private static ResidentArchetype ArchetypeFor(ExpeditionRole role, SettlementCulture culture, GameObject body,
                                                      List<ResidentArchetype> usable)
        {
            if (ResidentAssignment.RoleOf(body) != null) return null;
            IReadOnlyCollection<ResidentArchetype> suits = culture.SuitsOf(body);
            if (!suits.Any(a => Has(a, role))) return null;

            return suits.Concat(culture.archetypes)
                .Where(a => Has(a, role) && usable.Contains(a))
                .Distinct()
                .OrderBy(a => a.post != null)
                .ThenBy(a => a.duty == ResidentDuty.None)
                .ThenBy(a => !suits.Contains(a))
                .FirstOrDefault();
        }

        private static bool Has(ResidentArchetype archetype, ExpeditionRole role) =>
            archetype != null && (archetype.expeditionRoles & role) != 0;

        private static bool Patrols(ResidentArchetype archetype) => archetype != null && archetype.duty == ResidentDuty.Patrol;

        /// <summary>The prefab the resident's body is: its stamped source, else the prefab it is an instance of.</summary>
        private static GameObject BodyOf(Resident resident) => resident.sourcePrefab != null ? resident.sourcePrefab : PlacedFrom(resident);

        private static string Tally(IEnumerable<ResidentArchetype> archetypes) =>
            string.Join(", ", archetypes.GroupBy(NameOf).Select(g => $"{g.Key} {g.Count()}"));

        private static string GuidOf(UnityEngine.Object asset) => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));

        private static string NameOf(UnityEngine.Object o) => o != null ? o.name : "none";

        /// <summary>The Settlement the selection is part of, in an open scene (not a prefab asset or Prefab Mode); else null.</summary>
        private static Settlement SelectedSettlement()
        {
            GameObject selected = Selection.activeGameObject;
            Settlement settlement = selected != null ? selected.GetComponentInParent<Settlement>(true) : null;
            if (settlement == null) return null;

            Scene scene = settlement.gameObject.scene;
            return scene.IsValid() && scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene) ? settlement : null;
        }

        // ── scenes ───────────────────────────────────────────────────────────────────────────────

        private static IEnumerable<Settlement> SettlementsIn(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Settlement settlement in root.GetComponentsInChildren<Settlement>(true))
                    yield return settlement;
        }

        /// <summary>Every chunk scene of every world, by its path on disk, each once.</summary>
        private static List<string> ChunkScenePaths(StringBuilder report)
        {
            var paths = new List<string>();
            foreach (WorldStreamingConfig config in WorldChunkScenes.AllConfigs())
                foreach (string path in WorldChunkScenes.ScenePaths(config, problem => report.Append($"  ! {problem}, skipped\n")))
                    if (!paths.Contains(path)) paths.Add(path);
            return paths;
        }

        /// <summary>Edits one chunk scene: in place when it is already open, else opened additively and closed again.</summary>
        private static int EditChunkScene(string path, SceneEdit edit, StringBuilder report)
        {
            Scene open = SceneManager.GetSceneByPath(path);
            if (open.IsValid() && open.isLoaded) return Apply(open, edit, openedHere: false, report);

            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                return Apply(scene, edit, openedHere: true, report);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>
        /// Runs <paramref name="edit"/> on <paramref name="scene"/> and saves it when it changed, unless the scene was
        /// already open with unsaved edits of its own. A saved scene is read back from disk.
        /// </summary>
        private static int Apply(Scene scene, SceneEdit edit, bool openedHere, StringBuilder report)
        {
            bool hadUnsavedEdits = !openedHere && scene.isDirty;
            int changed = edit(scene, report, out Func<string, string> problemInFile);
            if (changed == 0) return 0;

            if (hadUnsavedEdits)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                report.Append($"  ! {scene.name}: NOT saved, it was open with unsaved edits of its own. Review and save it.\n");
                return changed;
            }

            if (!EditorSceneManager.SaveScene(scene))
            {
                Debug.LogError($"{Tag} Saving {scene.path} failed; its {changed} change(s) are not on disk.");
                return changed;
            }

            string problem = problemInFile(File.ReadAllText(scene.path));
            if (problem != null) Debug.LogError($"{Tag} {scene.path} was saved without the change: {problem}.");
            else report.Append($"  {scene.name}: saved\n");
            return changed;
        }

        private static int Occurrences(string text, string token)
        {
            int count = 0;
            int at = text.IndexOf(token, StringComparison.Ordinal);
            while (at >= 0)
            {
                count++;
                at = text.IndexOf(token, at + token.Length, StringComparison.Ordinal);
            }
            return count;
        }

        private static bool RefusedInPlayMode()
        {
            if (!EditorApplication.isPlaying) return false;
            Debug.LogError($"{Tag} Exit Play mode first: scene edits made in Play mode are discarded.");
            return true;
        }
    }
}
