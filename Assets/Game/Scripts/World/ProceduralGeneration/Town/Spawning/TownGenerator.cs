// The thing you drag into a scene. Drop it on an empty GameObject, fill in the settings, press
// Generate, and the prefabs you listed are placed as children of that GameObject.
//
// It makes no prefab and no asset of its own. What you get is ordinary scene objects that you can
// select, nudge and inspect like anything else you placed by hand — the generator only decides
// where they go.
//
// Press Generate again and it does NOT start from nothing: TownReuse pairs each new slot with the
// object already made from that prefab and moves it. Widen the spacing, press Generate, and the
// same buildings stand further apart. Only prefabs the recipe no longer asks for are destroyed, and
// only that case needs confirmRegenerate — because destroying an object destroys its save identity
// and orphans every record about it, permanently (Towns.md, Gotchas).
//
// Edit time only, and that is forced rather than chosen. The world NavMesh is a single author-time
// bake over all 48 chunks and nothing bakes at runtime (NavMeshSystem.md); a runtime-spawned prefab
// needs a registered network id or it exists on the host alone; and a save id for a scene object is
// baked by SaveableEntity.OnValidate, which does not run in a player. So Generate writes real
// GameObjects into the open scene and you commit the scene — the same path ClankerSettlementBuilder
// takes, for the same three reasons.
//
// Everything spawned parents under one "Generated" child so Clear wipes it without touching
// anything you placed by hand alongside it.
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay.Quests;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SpaceGame.World.Towns
{
    [DisallowMultipleComponent]
    public class TownGenerator : MonoBehaviour
    {
        public const string GeneratedRootName = "Generated";

        [Header("Settings")]
        [Tooltip("What to build. Fill this in here — no asset needed.\n\n" +
                 "Ignored while a Shared Recipe is assigned below.")]
        public TownRecipe settings = new();

        [Header("Shared recipe (optional)")]
        [Tooltip("Leave this EMPTY for the ordinary case. Assign a Town Recipe asset only when " +
                 "several towns must stay identical; while one is assigned the settings above are " +
                 "ignored and this asset is used instead.")]
        public TownRecipeAsset sharedRecipe;

        [Header("Determinism")]
        [Tooltip("Same settings + same seed = same town. Always.")]
        public int seed = 1701;

        [Header("Terrain")]
        [Tooltip("Layers to raycast against when looking for the ground.\n\n" +
                 "Leaving this as Everything means a later placement can land on an earlier one — " +
                 "terrain and buildings share the Default layer, so the mask cannot tell them " +
                 "apart. Narrow it to your terrain layer if you can.")]
        public LayerMask terrainMask = ~0;

        [Header("Safety")]
        [Tooltip("Only needed when Generate would have to DESTROY something — a prefab the recipe " +
                 "no longer asks for, or fewer of it than before. Moving what is already there " +
                 "never needs this.\n\n" +
                 "A destroyed object takes its save identity with it, so every saved record about " +
                 "it — quest progress, health, position, anything dropped there — is orphaned, " +
                 "permanently, in every existing save file.")]
        public bool confirmRegenerate = false;

        /// <summary>The last <see cref="Verify"/> report, for the inspector to show.</summary>
        [HideInInspector] public string lastReport = string.Empty;

        /// <summary>Whether the last <see cref="Verify"/> passed. Drives the inspector's colour.</summary>
        [HideInInspector] public bool lastReportOk = true;

        private TownPlacement placement;

        /// <summary>
        /// The settings actually in force. A shared asset wins when one is assigned, so that "which
        /// of the two am I editing" has one answer rather than a merge.
        /// </summary>
        public TownRecipe Recipe => sharedRecipe != null ? sharedRecipe.recipe : settings;

        /// <summary>True while a shared asset is overriding the inline settings.</summary>
        public bool UsingSharedRecipe => sharedRecipe != null;

        public Transform GeneratedRoot => transform.Find(GeneratedRootName);

        // ── Commands ─────────────────────────────────────────────────────────────

        [ContextMenu("Generate")]
        public void Generate()
        {
            TownRecipe recipe = Recipe;

            if (!Precheck(out string why))
            {
                Report(false, why);
                Debug.LogError("[TownGenerator] " + why, this);
                return;
            }

            placement = new TownPlacement(terrainMask, recipe.defaultFootprint,
                                          recipe.minStructureSpacing, recipe.buildingPadding,
                                          recipe.foundationPadThreshold, recipe.foundationPadOverhang,
                                          recipe.foundationPadMaterial);

            List<TownSlot> slots = TownLayout.Build(recipe, seed, placement.ClearanceRadius);

            // Decide the whole reshuffle BEFORE touching anything, so a refusal leaves the town
            // exactly as it was rather than half taken apart.
            TownReusePlan plan = TownReuse.Match(slots, PrefabFor, GeneratedRoot);

            int orphans = CountSaveables(plan.Surplus);
            if (orphans > 0 && !confirmRegenerate)
            {
                string refusal =
                    $"this would destroy {plan.Surplus.Count} object(s) that the settings no longer " +
                    $"ask for, {orphans} of which carry save records. Destroying them orphans those " +
                    "records permanently, in every existing save file. Tick 'Confirm Regenerate' if " +
                    "you accept that.\n\nMoving what is already placed — spacing, radii, seed — " +
                    "never needs this.";

                Report(false, refusal);
                Debug.LogError("[TownGenerator] " + refusal, this);
                return;
            }

            Transform root = EnsureGeneratedRoot();

            foreach (GameObject leftover in plan.Surplus) Discard(leftover);

            var people = new List<GameObject>();
            int placed = 0;
            int groundMisses = 0;

            for (int i = 0; i < slots.Count; i++)
            {
                GameObject spawned = Place(slots[i], root, plan.Reused[i], out bool missedGround);
                if (missedGround) groundMisses++;
                if (spawned == null) continue;

                spawned.transform.SetSiblingIndex(placed++);
                if (slots[i].Section == TownSection.Person) people.Add(spawned);
            }

            WireTerritory(people);
            AssignQuestlines(people);

            bool ok = Verify(slots, placed, groundMisses, people, plan, out string report);
            Report(ok, report);

            if (ok) Debug.Log($"[TownGenerator] {report}", this);
            else Debug.LogError("[TownGenerator] " + report, this);

            if (ok)
                Debug.LogWarning("[TownGenerator] The world NavMesh is now stale: run " +
                                 "World > Streaming > Bake World NavMesh before playing, or nothing " +
                                 "here can walk and WorldNavMeshBuildCheck fails the next build.", this);
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name != GeneratedRootName) continue;

                Discard(child.gameObject);
            }
        }

        [ContextMenu("Reroll (new seed + generate)")]
        public void Reroll()
        {
            // Unlike RobotSettlementGenerator.Reroll, the new seed is KEPT. That one restored the
            // previous value afterwards, so a town you liked could never be generated a second time.
            seed = new System.Random().Next(int.MinValue, int.MaxValue);
            Generate();
        }

        private Transform EnsureGeneratedRoot()
        {
            Transform existing = GeneratedRoot;
            if (existing != null) return existing;

            Transform root = new GameObject(GeneratedRootName).transform;
            root.SetParent(transform, worldPositionStays: false);
            root.localPosition = Vector3.zero;
            return root;
        }

        private static void Discard(GameObject go)
        {
            if (go == null) return;

            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        // ── Placing ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Put the slot's prefab where the slot says. <paramref name="existing"/> is the object
        /// already made from that prefab, if there is one: it is moved rather than replaced, which
        /// is what keeps its save identity.
        /// </summary>
        private GameObject Place(TownSlot slot, Transform root, GameObject existing, out bool missedGround)
        {
            missedGround = false;

            GameObject prefab = PrefabFor(slot);
            if (prefab == null) return null;

            TownGroup group = GroupFor(slot);

            Vector3 worldXZ = transform.TransformPoint(new Vector3(slot.LocalXZ.x, 0f, slot.LocalXZ.y));
            if (!placement.SampleGround(worldXZ, out float groundY))
            {
                // Deliberately not placing it at a guessed height: a building dropped at y=0 in a
                // world whose terrain sits at 100 m is buried and looks like it was never spawned.
                //
                // An object that is already there is left exactly where it stands rather than
                // destroyed. A terrain collider that has not loaded is a transient editor condition,
                // and throwing the building away over it would orphan its save records for good.
                missedGround = true;
                return existing;
            }

            Quaternion rot = slot.Yaw == 0f && group != null && group.yaw == TownYaw.Keep
                ? prefab.transform.rotation
                : Quaternion.Euler(0f, slot.Yaw, 0f);

            var pos = new Vector3(worldXZ.x, groundY, worldXZ.z);
            GameObject go = existing != null ? existing : Create(prefab, root);
            if (go == null) return null;

            go.transform.SetPositionAndRotation(pos, rot);

            // Assigned from the prefab's own scale, never multiplied into whatever is already there:
            // on a reused object `*=` would compound the group's scale again on every Generate.
            go.transform.localScale = prefab.transform.localScale * slot.Scale;

            Stamp(go, slot, prefab);

            if (group != null && group.foundationPad)
                placement.AddFoundationPad(go.transform, worldXZ, groundY, root,
                                           placement.Footprint(prefab), rot);

            return go;
        }

        /// <summary>
        /// A new instance, kept as a PREFAB instance at edit time rather than a plain copy — that is
        /// what makes later prefab edits reach the town, and what lets the scene store overrides
        /// instead of the whole object.
        /// </summary>
        private static GameObject Create(GameObject prefab, Transform root)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
#endif
            return Instantiate(prefab, root);
        }

        /// <summary>Record which slot placed this, so the next Generate can find it again.</summary>
        private static void Stamp(GameObject go, TownSlot slot, GameObject prefab)
        {
            TownInstance stamp = go.GetComponent<TownInstance>();
            if (stamp == null) stamp = go.AddComponent<TownInstance>();

            stamp.section = slot.Section;
            stamp.groupIndex = slot.GroupIndex;
            stamp.source = prefab;
        }

        private GameObject PrefabFor(TownSlot slot)
        {
            TownRecipe recipe = Recipe;
            if (recipe == null) return null;
            if (slot.Section == TownSection.Centrepiece) return recipe.centrepiecePrefab;

            TownGroup group = GroupFor(slot);
            if (group?.prefabs == null) return null;
            if (slot.PrefabIndex < 0 || slot.PrefabIndex >= group.prefabs.Length) return null;

            return group.prefabs[slot.PrefabIndex];
        }

        private TownGroup GroupFor(TownSlot slot)
        {
            List<TownGroup> groups = Recipe?.GroupsFor(slot.Section);
            if (groups == null) return null;
            if (slot.GroupIndex < 0 || slot.GroupIndex >= groups.Count) return null;

            return groups[slot.GroupIndex];
        }

        // ── Territory ────────────────────────────────────────────────────────────

        /// <summary>
        /// Put the alarm and the population topper-upper on this object — the town ROOT, not the
        /// Generated child, so that Clear does not take them with it and so their radii are measured
        /// from where you dropped the component.
        /// </summary>
        private void WireTerritory(List<GameObject> people)
        {
            TownRecipe recipe = Recipe;

            if (recipe.wireAlarm)
            {
                SettlementAlarm alarm = GetComponent<SettlementAlarm>();
                if (alarm == null) alarm = gameObject.AddComponent<SettlementAlarm>();
                alarm.Configure(recipe.ownerFaction, recipe.relationshipTable,
                                recipe.outerRadius + recipe.alarmMargin);
            }

            if (!recipe.wirePopulation) return;

            SettlementPopulation population = GetComponent<SettlementPopulation>();
            if (population == null) population = gameObject.AddComponent<SettlementPopulation>();

            // The inhabitant table is DERIVED from the People groups rather than authored twice.
            // Weight is the group's midpoint count, so a town of six labourers and one smith
            // replaces labourers six times as often.
            var inhabitants = new List<SettlementPopulation.Inhabitant>();
            foreach (TownGroup group in recipe.people)
            {
                if (group == null || !group.HasPrefabs) continue;

                int weight = Mathf.Max(1, (group.MinCount + group.MaxCount) / 2);
                foreach (GameObject prefab in group.prefabs)
                {
                    if (prefab == null) continue;
                    inhabitants.Add(new SettlementPopulation.Inhabitant { prefab = prefab, weight = weight });
                }
            }

            // Reinforcements land between the inner and outer rings — inside the town rather than
            // walking in from the horizon, but not in the middle of the square. The count radius
            // matches the alarm's, so "who is in this town" is one answer, not two.
            population.Configure(recipe.ownerFaction, recipe.relationshipTable,
                                 inhabitants.ToArray(),
                                 recipe.populationCap, recipe.populationInterval,
                                 recipe.innerRadius, recipe.outerRadius,
                                 recipe.outerRadius + recipe.alarmMargin);
        }

        // ── Quests ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Hand each questline to one placed NPC, chosen with the town's own seed so the same town
        /// always puts the same quest on the same character.
        ///
        /// Only an NPC that can hold a conversation is eligible: a QuestGiver is consulted by
        /// DialogInteraction, so one on a character without it would never be asked anything and the
        /// quest would simply not exist. <see cref="Verify"/> refuses that rather than shipping it.
        ///
        /// Because people are reused between runs, an NPC can arrive here still carrying the
        /// questline it was dealt last time. Every eligible NPC is therefore dealt with explicitly —
        /// given a questline, or stripped of the one it has — so that removing a questline from the
        /// settings actually removes it from the town.
        /// </summary>
        private void AssignQuestlines(List<GameObject> people)
        {
            List<GameObject> eligible = EligibleGivers(people);
            List<Questline> questlines = LiveQuestlines();

            // A separate stream, salted off the seed: drawing quest assignments from the layout's
            // rng would mean adding a questline moved every building in the town.
            var rng = new System.Random(seed ^ 0x51ED21);

            // Shuffle a copy and deal off the top, so two questlines never land on one NPC.
            var pool = new List<GameObject>(eligible);
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = rng.Next(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            int dealt = 0;
            foreach (Questline questline in questlines)
            {
                if (dealt >= pool.Count) break;

                GameObject giver = pool[dealt++];
                QuestGiver component = giver.GetComponent<QuestGiver>();
                if (component == null) component = giver.AddComponent<QuestGiver>();

                component.Assign(questline);
            }

            for (int i = dealt; i < pool.Count; i++) StripQuestGiver(pool[i]);
        }

        private List<Questline> LiveQuestlines()
        {
            var live = new List<Questline>();
            if (Recipe?.questlines == null) return live;

            foreach (Questline questline in Recipe.questlines)
                if (questline != null) live.Add(questline);

            return live;
        }

        /// <summary>
        /// Take the errand off an NPC that is no longer meant to have one — but only an errand this
        /// generator gave it.
        ///
        /// A `QuestGiver` authored on the PREFAB is somebody's decision and stays: quests on an NPC
        /// prefab are the prefab's business, the same rule the rest of this generator follows about
        /// NPC behaviour. Trying to remove one would also make Unity log an error per NPC, since a
        /// component that came from the prefab cannot simply be destroyed off the instance.
        ///
        /// The saver goes first: `QuestGiverSaveable` requires the giver, and Unity refuses to
        /// remove a component something else depends on.
        /// </summary>
        private static void StripQuestGiver(GameObject person)
        {
            var giver = person.GetComponent<QuestGiver>();
            if (giver == null || !AddedByGenerator(giver)) return;

            var saver = person.GetComponent<SpaceGame.Core.Persistence.QuestGiverSaveable>();
            if (saver != null && AddedByGenerator(saver)) RemoveComponent(saver);

            RemoveComponent(giver);
        }

        /// <summary>
        /// Whether this component was added to the instance rather than inherited from its prefab.
        /// Outside the editor there is no prefab connection to consult and no authored town to
        /// protect, so everything counts as ours.
        /// </summary>
        private static bool AddedByGenerator(Component component)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabInstance(component))
                return PrefabUtility.IsAddedComponentOverride(component);
#endif
            return true;
        }

        private static void RemoveComponent(Component component)
        {
            if (Application.isPlaying) Destroy(component);
            else DestroyImmediate(component);
        }

        private static List<GameObject> EligibleGivers(List<GameObject> people)
        {
            var eligible = new List<GameObject>();
            foreach (GameObject person in people)
                if (person != null && person.GetComponentInChildren<SpaceGame.Gameplay.DialogInteraction>(true) != null)
                    eligible.Add(person);

            return eligible;
        }

        // ── Verification ─────────────────────────────────────────────────────────

        /// <summary>The sections that hold groups, in emit order. The centrepiece has no list.</summary>
        private static readonly TownSection[] GroupSections =
        {
            TownSection.Building, TownSection.Prop, TownSection.Scatter, TownSection.Person,
        };

        /// <summary>How a group is named in a report: where it lives, plus its label if it has one.</summary>
        private static string Describe(TownSection section, int index, TownGroup group) =>
            $"{section}[{index}]" +
            (string.IsNullOrWhiteSpace(group?.label) ? "" : $" '{group.label}'");

        /// <summary>
        /// Everything that must be true BEFORE anything is written. These abort rather than
        /// degrade: a half-generated town saved into a chunk scene is worse than no town.
        ///
        /// The regeneration check is NOT here, because whether anything has to be destroyed cannot
        /// be known until the new layout has been matched against the old one. <see cref="Generate"/>
        /// makes that call, still before it writes anything.
        /// </summary>
        public bool Precheck(out string why)
        {
            var problems = new List<string>();
            TownRecipe recipe = Recipe;

            if (recipe == null)
            {
                why = "No settings to build from.";
                return false;
            }

            if (recipe.innerRadius <= 0f || recipe.midRadius <= recipe.innerRadius ||
                recipe.outerRadius <= recipe.midRadius)
                problems.Add($"radii must increase and be positive, but they are " +
                             $"inner={recipe.innerRadius} mid={recipe.midRadius} outer={recipe.outerRadius}");

            foreach (TownSection section in GroupSections)
            {
                List<TownGroup> groups = recipe.GroupsFor(section);
                if (groups == null) continue;

                for (int i = 0; i < groups.Count; i++)
                {
                    TownGroup group = groups[i];
                    string where = Describe(section, i, group);

                    if (group == null) { problems.Add($"{where} is null"); continue; }
                    if (group.MaxCount <= 0) continue;

                    if (!group.HasPrefabs)
                        problems.Add($"{where} places up to {group.MaxCount} but has no prefabs");
                    else if (group.prefabs != null)
                        for (int p = 0; p < group.prefabs.Length; p++)
                            if (group.prefabs[p] == null)
                                problems.Add($"{where} prefab slot {p} is empty — " +
                                             "fill it or shorten the array; an empty slot silently " +
                                             "places nothing when the dice land on it");
                }
            }

            if (recipe.questlines != null)
                foreach (Questline questline in recipe.questlines)
                {
                    if (questline == null) { problems.Add("a questline entry is null"); continue; }
                    problems.AddRange(questline.Problems());
                }

            if ((recipe.wireAlarm || recipe.wirePopulation) &&
                (recipe.ownerFaction == null || recipe.relationshipTable == null))
                problems.Add("wireAlarm/wirePopulation need both an ownerFaction and a relationshipTable");

            why = problems.Count == 0 ? string.Empty : Join(problems);
            return problems.Count == 0;
        }

        /// <summary>Everything that can only be checked after the town exists.</summary>
        private bool Verify(List<TownSlot> slots, int placed, int groundMisses,
                            List<GameObject> people, TownReusePlan plan, out string report)
        {
            var problems = new List<string>();
            TownRecipe recipe = Recipe;

            if (groundMisses > 0)
                problems.Add($"{groundMisses} placement(s) found no ground and were skipped. Did the " +
                             "chunk's terrain collider load? Open the terrain's scene before generating.");

            problems.AddRange(GroupsThatPlacedNothing(slots));

            int wanted = MinimumExpected();
            if (placed < wanted)
                problems.Add($"placed {placed} of at least {wanted} expected — the rest could not " +
                             "find room. Widen the radii, or lower the counts.");

            if (recipe.questlines != null)
            {
                int questlines = LiveQuestlines().Count;
                int eligible = EligibleGivers(people).Count;
                if (questlines > eligible)
                    problems.Add($"{questlines} questline(s) but only {eligible} placed NPC(s) carry a " +
                                 "DialogInteraction to hand one to. " + DescribePeopleWithoutDialog());
            }

            int reused = plan.ReuseCount;
            string movement = reused > 0
                ? $"moved {reused} already there, added {placed - reused}, removed {plan.Surplus.Count}"
                : $"placed {placed}";

            report = problems.Count > 0
                ? Join(problems)
                : $"{movement} — {slots.Count} slot(s), seed {seed}.";

            return problems.Count == 0;
        }

        /// <summary>
        /// Every group that has prefabs and asks for something, but ended up with no slots at all.
        ///
        /// "I pressed Generate and nothing appeared" is the commonest way a generator fails and the
        /// hardest to diagnose from outside, so each one is named and told WHY. The count is a
        /// range, and a range whose low end is zero is allowed to roll zero — which looks exactly
        /// like a broken generator to somebody who typed 1 in the first box and left the second at
        /// its default.
        /// </summary>
        private IEnumerable<string> GroupsThatPlacedNothing(List<TownSlot> slots)
        {
            var counts = new Dictionary<(TownSection, int), int>();
            foreach (TownSlot slot in slots)
            {
                var key = (slot.Section, slot.GroupIndex);
                counts.TryGetValue(key, out int n);
                counts[key] = n + 1;
            }

            foreach (TownSection section in GroupSections)
            {
                List<TownGroup> groups = Recipe.GroupsFor(section);
                if (groups == null) continue;

                for (int i = 0; i < groups.Count; i++)
                {
                    TownGroup group = groups[i];
                    if (group == null || !group.HasPrefabs || group.MaxCount <= 0) continue;
                    if (counts.ContainsKey((section, i))) continue;

                    string where = Describe(section, i, group);

                    yield return group.MinCount <= 0
                        ? $"{where} placed nothing. Its count is a RANGE — currently " +
                          $"{group.MinCount} to {group.MaxCount} — so rolling none of them is a legal " +
                          "outcome. Set both ends of the count to the number you actually want."
                        : $"{where} placed nothing, though its count starts at {group.MinCount}: " +
                          "every attempt overlapped something already claimed. Widen the radii, " +
                          "lower Min Structure Spacing, or turn off Reserves Clearance for it.";
                }
            }
        }

        private string DescribePeopleWithoutDialog()
        {
            var names = new List<string>();
            foreach (TownGroup group in Recipe.people)
            {
                if (group?.prefabs == null) continue;
                foreach (GameObject prefab in group.prefabs)
                    if (prefab != null &&
                        prefab.GetComponentInChildren<SpaceGame.Gameplay.DialogInteraction>(true) == null &&
                        !names.Contains(prefab.name))
                        names.Add(prefab.name);
            }

            return names.Count == 0
                ? string.Empty
                : "These people prefabs have no DialogInteraction: " + string.Join(", ", names) + ".";
        }

        private int MinimumExpected()
        {
            TownRecipe recipe = Recipe;
            int total = recipe.centrepiecePrefab != null ? 1 : 0;

            foreach (TownSection section in GroupSections)
            {
                List<TownGroup> groups = recipe.GroupsFor(section);
                if (groups == null) continue;

                foreach (TownGroup group in groups)
                    if (group != null && group.HasPrefabs)
                        total += group.MinCount * Mathf.Max(1, Mathf.Min(group.clusterSize.x, group.clusterSize.y));
            }

            return total;
        }

        /// <summary>How many of these objects carry a save record that destroying them would orphan.</summary>
        private static int CountSaveables(List<GameObject> objects)
        {
            int total = 0;
            foreach (GameObject go in objects)
                if (go != null)
                    total += go.GetComponentsInChildren<SpaceGame.Core.Persistence.SaveableEntity>(true).Length;

            return total;
        }

        private void Report(bool ok, string text)
        {
            lastReportOk = ok;
            lastReport = text;
        }

        private static string Join(List<string> problems)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < problems.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append("• ").Append(problems[i]);
            }

            return sb.ToString();
        }

        private void OnValidate() => settings?.Normalize();

        // ── Gizmos ───────────────────────────────────────────────────────────────

        private void OnDrawGizmosSelected()
        {
            TownRecipe recipe = Recipe;
            if (recipe == null) return;

            Vector3 c = transform.position;
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.4f);
            DrawCircle(c, recipe.innerRadius);
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.4f);
            DrawCircle(c, recipe.midRadius);
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.4f);
            DrawCircle(c, recipe.outerRadius);

            if (recipe.wireAlarm || recipe.wirePopulation)
            {
                Gizmos.color = new Color(1f, 0.4f, 0.8f, 0.35f);
                DrawCircle(c, recipe.outerRadius + recipe.alarmMargin);
            }
        }

        private static void DrawCircle(Vector3 centre, float radius, int segments = 48)
        {
            Vector3 prev = centre + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments * Mathf.PI * 2f;
                Vector3 next = centre + new Vector3(Mathf.Cos(t) * radius, 0f, Mathf.Sin(t) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
