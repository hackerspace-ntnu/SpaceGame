// Answers "what on this object is NOT saved?", which is the question the save system could never be
// asked before.
//
// Every other tool here checks that the wiring you meant to have is present. None of them can tell
// you about state nobody ever wrote a saver for — and that is the whole failure mode: a component
// holding something a player changed, with no record behind it, producing no error and no warning
// and no failing test. The audit that produced this file found roughly forty of them by hand, in a
// document that will be out of date the first time somebody adds a module.
//
// So the list is derived instead. For every saveable object, this walks its components and reports
// the ones that hold mutable state and are covered by no saver. It is a heuristic and says so: the
// output is a starting point for judgement, not a defect list. Plenty of what it finds is correctly
// transient, and saying "this is transient on purpose" is a decision worth making explicitly.
//
// Run from: Tools ▸ Save System ▸ Report Unsaved State
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence.EditorTools
{
    public static class SaveCoverageReport
    {
        /// <summary>
        /// Components whose mutable state is deliberately not persisted, and why.
        ///
        /// An allowlist rather than a silence: the point of the report is to force the "is this
        /// transient?" question to be answered once, in writing, instead of re-asked every time
        /// somebody reads the output and assumes the previous reader had checked.
        /// </summary>
        private static readonly Dictionary<string, string> DeliberatelyTransient = new()
        {
            ["Rigidbody"] = "engine-owned; RigidbodySaveable owns the part worth keeping",
            ["Transform"] = "TransformSaveable and the entity record own pose",
            ["NavMeshAgent"] = "path is rebuilt from the destination; SaveTeleport warps the agent",
            ["Animator"] = "presentation, re-derived from live state every frame",
            ["AudioSource"] = "presentation",
            ["Collider"] = "authored, not mutated",
            ["Renderer"] = "presentation",
            ["NetworkObject"] = "netcode identity, rebuilt on spawn",
            ["NetworkTransform"] = "netcode replication of a pose the record already owns",
            ["FlockingModule"] = "pure per-frame steering off neighbour buffers",
            ["AlertBroadcaster"] = "sends, never remembers",
        };

        [MenuItem("Tools/Save System/Report Unsaved State")]
        public static void Report()
        {
            // saver -> the component type it is [RequireComponent]'d onto. That attribute is how a
            // saver declares what it speaks for, so it is the honest coverage map: no hand-kept list
            // to fall out of step with the savers themselves.
            HashSet<Type> covered = CoveredComponentTypes();

            var report = new StringBuilder("[Save] Unsaved state report\n");
            int objects = 0;
            int flagged = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                if (!SaveablePolicy.NeedsSaving(prefab, out _)) continue;

                objects++;

                var uncovered = new List<string>();

                foreach (Component component in prefab.GetComponents<Component>())
                {
                    if (component == null) continue;

                    Type type = component.GetType();
                    if (component is ISaveable) continue;
                    if (covered.Contains(type)) continue;
                    if (IsDeliberatelyTransient(type, out _)) continue;

                    int mutable = MutableFieldCount(type);
                    if (mutable == 0) continue;

                    uncovered.Add($"{type.Name} ({mutable} mutable field(s))");
                }

                if (uncovered.Count == 0) continue;

                flagged++;
                report.Append("  ").Append(prefab.name).Append("  — ")
                      .Append(string.Join(", ", uncovered)).Append('\n');
            }

            report.Append($"\n  {objects} saveable prefab(s) examined, {flagged} carry components with ")
                  .Append("mutable state that no saver speaks for.\n")
                  .Append("  This is a heuristic. Much of it is correctly transient — when you decide ")
                  .Append("something is,\n  add it to SaveCoverageReport.DeliberatelyTransient with the ")
                  .Append("reason, so the next reader\n  inherits the decision instead of re-making it.");

            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Every component type some saver declares itself responsible for.
        ///
        /// Read off <c>[RequireComponent]</c> because that is already how a saver says what it owns.
        /// A saver without one covers nothing by this measure, which is the correct answer: it means
        /// nothing can tell what it speaks for.
        /// </summary>
        private static HashSet<Type> CoveredComponentTypes()
        {
            var covered = new HashSet<Type>();

            foreach (Type saver in TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
            {
                if (!typeof(ISaveable).IsAssignableFrom(saver)) continue;

                foreach (RequireComponent require in saver.GetCustomAttributes<RequireComponent>())
                {
                    if (require.m_Type0 != null) covered.Add(require.m_Type0);
                    if (require.m_Type1 != null) covered.Add(require.m_Type1);
                    if (require.m_Type2 != null) covered.Add(require.m_Type2);
                }
            }

            return covered;
        }

        private static bool IsDeliberatelyTransient(Type type, out string reason)
        {
            for (Type t = type; t != null && t != typeof(Component); t = t.BaseType)
            {
                if (DeliberatelyTransient.TryGetValue(t.Name, out reason)) return true;
            }

            reason = null;
            return false;
        }

        /// <summary>
        /// How many fields on this component could hold something a player changed.
        ///
        /// Counts private fields as well as public ones, because in this codebase the state that goes
        /// missing is almost always private — <c>cooldownTimer</c>, <c>spawnAnchor</c>,
        /// <c>isSearching</c>. Read-only, const and static fields are excluded: none of them can carry
        /// per-instance runtime state.
        /// </summary>
        private static int MutableFieldCount(Type type)
        {
            int count = 0;

            for (Type t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(Component); t = t.BaseType)
            {
                foreach (FieldInfo field in t.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsInitOnly || field.IsLiteral) continue;

                    // Serialized references to other objects are wiring, not state.
                    if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)) continue;
                    if (typeof(Delegate).IsAssignableFrom(field.FieldType)) continue;

                    count++;
                }
            }

            return count;
        }
    }
}
