// The single answer to "does this object need saving, and with what?".
//
// It used to live in the editor wiring tool, which meant the rule only ran when somebody remembered
// to open a menu. Anything placed in a scene afterwards was silently unsaveable, and nothing in the
// game said so — the failure looks exactly like a save system that works, right up until a player
// reloads and finds a creature back where it started.
//
// So the policy moved into the runtime and the editor tool now calls it. Two consequences worth
// stating, because they are the reason this file exists at all:
//
//   • the editor pass and the runtime pass CANNOT disagree, because there is one rule;
//   • an object that was never wired is still saved, because the runtime pass wires it as its scene
//     is hydrated. Adding a creature to a chunk scene is now the whole job.
//
// The editor pass is still worth running: it bakes a GUID identity into the scene file, which
// survives the object being renamed or moved in the hierarchy. The runtime fallback derives an
// identity from where the object sits instead, which is stable across sessions but not across scene
// edits. Baked is better; derived is what makes "I forgot" cost nothing.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using SpaceGame.Gameplay;

namespace SpaceGame.Core.Persistence
{
    public static class SaveablePolicy
    {
        /// <summary>
        /// Component type names that mean "this object does not outlive the moment".
        ///
        /// Matched by name rather than by type so this file needs no reference to the weapon and
        /// vehicle assemblies. A bullet has a Rigidbody like a vehicle does, but saving one means
        /// reloading into a world with shots frozen in mid-air — and re-spawning them on every load
        /// until the file fills with them.
        /// </summary>
        private static readonly HashSet<string> Transient = new()
        {
            "AgentProjectile",
            "TurretProjectile",
            "Projectile",
            "RocketLauncherTurret",
        };

        /// <summary>
        /// Whether an object has state a player can change and would expect to survive a reload.
        ///
        /// Driven by components rather than by a hand-kept list of prefabs, so anything added later
        /// is covered without editing this file. <paramref name="why"/> is for the wiring report — a
        /// pass that cannot explain itself is one nobody trusts enough to re-run.
        /// </summary>
        public static bool NeedsSaving(GameObject go, out string why)
        {
            why = null;
            if (go == null) return false;

            // The player is owned by PlayerSaveService, keyed by profile. Marking it as a world
            // object would ALSO capture it here and re-instantiate a lifeless copy on load.
            if (go.GetComponent<PlayerSaveBinder>() != null || go.GetComponent<PlayerSaveSync>() != null)
                return false;

            bool pickup = false;

            foreach (Component c in go.GetComponents<Component>())
            {
                if (c == null) continue;

                string type = c.GetType().Name;
                if (Transient.Contains(type)) return false;

                // By name: PickupableItem is internal to SpaceGame.Items, so it cannot be named as
                // a type here.
                if (type == "PickupableItem") pickup = true;
            }

            var reasons = new List<string>();

            if (go.GetComponent<HealthComponent>() != null) reasons.Add("health");

            // A dropped item: the thing a player most expects to find where they left it.
            if (pickup) reasons.Add("pickup");

            // A mover: anything that can end the session somewhere other than where it started.
            // NavMeshAgent implies a wanderer even when the body is kinematic.
            if (go.GetComponent<NavMeshAgent>() != null) reasons.Add("agent");

            var body = go.GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic) reasons.Add("rigidbody");

            if (reasons.Count == 0) return false;

            why = string.Join("+", reasons);
            return true;
        }

        /// <summary>
        /// Gives an object the identity and the savers its components call for.
        ///
        /// Idempotent: re-running adds nothing and reports no change, which is what lets both the
        /// editor pass and the per-hydrate runtime pass call it freely.
        /// </summary>
        public static bool Ensure(GameObject go, out string added)
        {
            added = string.Empty;
            if (go == null) return false;

            var parts = new List<string>();

            if (go.GetComponent<SaveableEntity>() == null)
            {
                go.AddComponent<SaveableEntity>();
                parts.Add(nameof(SaveableEntity));
            }

            // Position matters for everything here: a creature that wandered, a vehicle that was
            // driven, a prop that was pushed. The scene file puts authored objects back at their
            // authored spot on every load, so without this nothing stays where it was left.
            if (go.GetComponent<TransformSaveable>() == null)
            {
                go.AddComponent<TransformSaveable>();
                parts.Add(nameof(TransformSaveable));
            }

            // HealthSaveable covers NetworkedHealthComponent too: that class is [RequireComponent]
            // on HealthComponent and its RestoreHealth path re-publishes to clients, so one saver
            // serves both the offline and the networked entities.
            if (go.GetComponent<HealthComponent>() != null && go.GetComponent<HealthSaveable>() == null)
            {
                go.AddComponent<HealthSaveable>();
                parts.Add(nameof(HealthSaveable));
            }

            // Momentum only where there is a body to carry it, and never on a kinematic one, whose
            // velocity is meaningless.
            var body = go.GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic && go.GetComponent<RigidbodySaveable>() == null)
            {
                go.AddComponent<RigidbodySaveable>();
                parts.Add(nameof(RigidbodySaveable));
            }

            added = string.Join(", ", parts);
            return parts.Count > 0;
        }

        /// <summary>
        /// Wires everything in a scene that qualifies but was never wired at edit time, and returns
        /// how many objects that was.
        ///
        /// Called as a scene is hydrated, so the save system sees a complete scene rather than the
        /// subset somebody remembered to prepare. Objects wired here get a derived identity — see
        /// <see cref="SaveableEntity.DeriveAuthoredId"/> — because a fresh GUID would be a different
        /// object every session and would persist nothing.
        /// </summary>
        public static int EnsureScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return 0;

            int wired = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    GameObject go = t.gameObject;

                    if (go.GetComponent<SaveableEntity>() != null) continue;
                    if (!NeedsSaving(go, out _)) continue;

                    // Identity before savers: SaveableEntity registers itself the moment it is
                    // added, and a derived id assigned afterwards would leave the random one it
                    // gave itself in the live registry.
                    string derived = SaveableEntity.DeriveAuthoredId(go);

                    Ensure(go, out _);
                    go.GetComponent<SaveableEntity>().AdoptAuthoredIdentity(derived);
                    wired++;
                }
            }

            return wired;
        }
    }
}
