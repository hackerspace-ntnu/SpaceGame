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
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.DuneFoil;

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

            // The declared answer, and the one that matters most. Everything below infers "this
            // moves" from a side effect of movement, and every inference missed the machines this
            // game is made of: a legged rig is a KINEMATIC Rigidbody with no NavMeshAgent and often no
            // HealthComponent, and the DuneFoil has no Rigidbody on its root at all. So the mount a
            // player rides and every vehicle in the world failed all four tests below and were never
            // captured — which is the whole reason nothing but the player persisted.
            if (go.GetComponent<IPersistentEntity>() != null) reasons.Add("entity");

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

            // Who was riding this. Chosen by having a MountModule for the same reason health is
            // chosen by having a HealthComponent: the rule is derivable from the object, so a mount
            // added later is covered by re-running rather than by remembering a list.
            if (go.GetComponent<MountModule>() != null && go.GetComponent<MountSaveable>() == null)
            {
                go.AddComponent<MountSaveable>();
                parts.Add(nameof(MountSaveable));
            }

            // Who this was fighting, and what it remembers. AgentTargeting rather than
            // AgentController: an agent with no targeting has no combat state to lose, and the saver
            // would capture an empty bag on every entity in the world.
            if (go.GetComponent<AgentTargeting>() != null && go.GetComponent<AgentStateSaveable>() == null)
            {
                go.AddComponent<AgentStateSaveable>();
                parts.Add(nameof(AgentStateSaveable));
            }

            if (go.GetComponent<EntityInventoryComponent>() != null &&
                go.GetComponent<EntityInventorySaveable>() == null)
            {
                go.AddComponent<EntityInventorySaveable>();
                parts.Add(nameof(EntityInventorySaveable));
            }

            // Hatches, ramps and canopies anywhere below this object. Asked of the whole subtree
            // because the parts are children while the saver belongs on the entity that owns them.
            if (go.GetComponentInChildren<ArticulatedPart>(true) != null &&
                go.GetComponent<ArticulatedPartsSaveable>() == null)
            {
                go.AddComponent<ArticulatedPartsSaveable>();
                parts.Add(nameof(ArticulatedPartsSaveable));
            }

            // Vehicle-specific rigs. Each is keyed off the one component that defines the vehicle, so
            // the rule stays derivable from the object rather than becoming a list of prefab names.
            if (go.GetComponent<SailRig>() != null && go.GetComponent<DuneFoilSaveable>() == null)
            {
                go.AddComponent<DuneFoilSaveable>();
                parts.Add(nameof(DuneFoilSaveable));
            }

            if (go.GetComponent<OrnithopterFlightMotor>() != null &&
                go.GetComponent<OrnithopterSaveable>() == null)
            {
                go.AddComponent<OrnithopterSaveable>();
                parts.Add(nameof(OrnithopterSaveable));
            }

            added = string.Join(", ", parts);
            return parts.Count > 0;
        }

        /// <summary>
        /// Gives an object spawned during play the identity and savers it qualifies for.
        ///
        /// The runtime-spawn counterpart to <see cref="EnsureScene"/>, and it exists because that
        /// method only ever sees objects a scene load brought in. Anything created during play — a
        /// deployed vehicle, a dropped item, a placed structure — went straight into the world with
        /// whatever savers its prefab happened to carry, so a mount spawned at runtime saved its pose
        /// and not its rider.
        ///
        /// No derived identity here, unlike <see cref="EnsureScene"/>: a runtime object has no
        /// authored position in a scene file to derive one from, and the random GUID
        /// <see cref="SaveableEntity"/> assigns itself is the correct answer for something that did
        /// not exist last session.
        ///
        /// Returns false for anything that does not qualify, which is most spawns.
        /// </summary>
        public static bool EnsureSpawned(GameObject go)
        {
            if (go == null || !NeedsSaving(go, out _)) return false;

            Ensure(go, out _);

            // A prefab whose SaveableEntity was never stamped cannot be resolved back to a prefab on
            // load, so its record would be captured faithfully and then dropped with a warning. Say so
            // now, at the spawn, where the prefab responsible is still identifiable.
            SaveableEntity entity = go.GetComponent<SaveableEntity>();
            if (entity != null && string.IsNullOrEmpty(entity.PrefabId))
            {
                Debug.LogWarning($"[Save] '{go.name}' was spawned at runtime and qualifies for saving, " +
                                 "but its prefab has no stamped prefab id — so it can be captured and " +
                                 "never restored. Add a SaveableEntity to the prefab asset (re-import " +
                                 "stamps the id), or spawn it through a path that supplies one.", go);
            }

            return true;
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
