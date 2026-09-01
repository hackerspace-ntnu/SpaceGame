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
using SpaceGame.Items;
using SpaceGame.Locomotion;
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

            // RocketLauncherTurret is NOT here any more, and never should have been. It is the
            // launcher, not the rocket — the rocket is TurretProjectile, blacklisted on the line
            // above — and the entry reads like it was added by name-association with the three real
            // projectile types around it. The contradiction it produced is visible in the assets:
            // Assets/Game/Resources/Saveable/RocketSpawn.prefab is the only thing carrying the
            // component, it already ships SaveableEntity + TransformSaveable + RigidbodySaveable,
            // and it lives in the folder whose entire purpose is "the save system must be able to
            // rebuild this". Blacklisting it made EnsureSpawned return false, so a turret a player
            // deployed got no savers at all and came back re-armed, if it came back.
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

            // Which hull modules a ship has been repaired with. On the root rather than the
            // subtree, unlike ArticulatedPart above: the rack IS the entity's own component, and
            // a hull towing another hull must not adopt its parts.
            if (go.GetComponent<ShipPartRack>() != null && go.GetComponent<ShipPartsSaveable>() == null)
            {
                go.AddComponent<ShipPartsSaveable>();
                parts.Add(nameof(ShipPartsSaveable));
            }

            // Which team a hull is painted for. Runtime-spawned rather than authored — every versus
            // ship is made mid-match — so the runtime pass is the one that matters here, and it is
            // the reason this clause exists rather than the colour being wired onto a prefab.
            if (go.GetComponent<ShipTeamAccent>() != null && go.GetComponent<ShipAccentSaveable>() == null)
            {
                go.AddComponent<ShipAccentSaveable>();
                parts.Add(nameof(ShipAccentSaveable));
            }

            EnsureAgentMind(go, parts);
            EnsureAgentRoutine(go, parts);
            EnsureAgentCombat(go, parts);
            EnsureWorldInteractables(go, parts);

            added = string.Join(", ", parts);
            return parts.Count > 0;
        }

        /// <summary>
        /// What an agent knows and feels: grudges, searches, alerts, fear, cover.
        ///
        /// Split out of <see cref="Ensure"/> only because the list got long enough that one method
        /// stopped being readable. The rule is unchanged — every clause is derivable from a
        /// component, so a prefab that gains the component gains the saver by re-running.
        /// </summary>
        private static void EnsureAgentMind(GameObject go, List<string> parts)
        {
            // The grudge. ProvocationModule is the ONLY thing that can make a Fauna creature
            // hostile — AgentTargeting.Reevaluate structurally cannot, because Fauna is Neutral to
            // everything — so without this a creature you provoked is peaceful after one reload and
            // can never re-acquire you on its own.
            if (go.GetComponent<ProvocationModule>() != null && go.GetComponent<ProvocationSaveable>() == null)
            {
                go.AddComponent<ProvocationSaveable>();
                parts.Add(nameof(ProvocationSaveable));
            }

            // What makes AgentStateSaveable's last-known position mean anything. SearchModule starts
            // on a falling edge (had a target, lost it) and a restored agent's `hadTarget` is always
            // false — so the position the save went out of its way to keep was never walked to.
            if (go.GetComponent<SearchModule>() != null && go.GetComponent<SearchSaveable>() == null)
            {
                go.AddComponent<SearchSaveable>();
                parts.Add(nameof(SearchSaveable));
            }

            if (go.GetComponent<AlertReceiverModule>() != null && go.GetComponent<AlertResponseSaveable>() == null)
            {
                go.AddComponent<AlertResponseSaveable>();
                parts.Add(nameof(AlertResponseSaveable));
            }

            if (go.GetComponent<NoiseReceiverModule>() != null &&
                go.GetComponent<NoiseInvestigationSaveable>() == null)
            {
                go.AddComponent<NoiseInvestigationSaveable>();
                parts.Add(nameof(NoiseInvestigationSaveable));
            }

            // Fleeing is hysteresis — trigger radius in, safe radius out — so it cannot be recomputed
            // from where things are standing. A creature restored calm inside the gap between the two
            // never resumes running.
            if (go.GetComponent<FleeModule>() != null && go.GetComponent<FleeSaveable>() == null)
            {
                go.AddComponent<FleeSaveable>();
                parts.Add(nameof(FleeSaveable));
            }

            if (go.GetComponent<CoverModule>() != null && go.GetComponent<CoverSaveable>() == null)
            {
                go.AddComponent<CoverSaveable>();
                parts.Add(nameof(CoverSaveable));
            }
        }

        /// <summary>
        /// Where an agent was going and where it belongs: routes, territory, errands, formation.
        /// </summary>
        private static void EnsureAgentRoutine(GameObject go, List<string> parts)
        {
            // Keyed off PatrolModule rather than AgentTargeting, which is where patrol progress used
            // to ride. PatrolRobot and DeathmatchBot have the first and not the second, so the one
            // population whose whole identity IS a route was the population saving nothing about it.
            if (go.GetComponent<PatrolModule>() != null && go.GetComponent<PatrolSaveable>() == null)
            {
                go.AddComponent<PatrolSaveable>();
                parts.Add(nameof(PatrolSaveable));
            }

            if (go.GetComponent<BasePatrolModule>() != null && go.GetComponent<BasePatrolSaveable>() == null)
            {
                go.AddComponent<BasePatrolSaveable>();
                parts.Add(nameof(BasePatrolSaveable));
            }

            // Anchors, not just destinations. These modules re-latch their home from
            // transform.position after a load, so a guard's patrol circle and a flying creature's
            // roost silently re-centre wherever the thing was standing when you saved — and drift
            // a little further on every single save/load cycle.
            if (go.GetComponent<WanderModule>() != null && go.GetComponent<WanderSaveable>() == null)
            {
                go.AddComponent<WanderSaveable>();
                parts.Add(nameof(WanderSaveable));
            }

            if (go.GetComponent<AirWanderModule>() != null && go.GetComponent<AirWanderSaveable>() == null)
            {
                go.AddComponent<AirWanderSaveable>();
                parts.Add(nameof(AirWanderSaveable));
            }

            if (go.GetComponent<WanderBehaviour>() != null &&
                go.GetComponent<WanderBehaviourSaveable>() == null)
            {
                go.AddComponent<WanderBehaviourSaveable>();
                parts.Add(nameof(WanderBehaviourSaveable));
            }

            // One saver for the three modules that resolve their own target: an agent almost never
            // has more than one of them, and they hold the same two fields for the same reason.
            if ((go.GetComponent<HuntModule>() != null ||
                 go.GetComponent<KeepDistanceModule>() != null ||
                 go.GetComponent<ApproachModule>() != null) &&
                go.GetComponent<PursuitSaveable>() == null)
            {
                go.AddComponent<PursuitSaveable>();
                parts.Add(nameof(PursuitSaveable));
            }

            // An NPC's errand. The virtual group's task already survived through NpcWorldSaveable;
            // a live NPC's did not, and EnsureHome would re-resolve their home to whichever site was
            // nearest the save position — so an NPC could permanently adopt a new home by being saved
            // somewhere else.
            if (go.GetComponent<NpcTaskModule>() != null && go.GetComponent<NpcTaskSaveable>() == null)
            {
                go.AddComponent<NpcTaskSaveable>();
                parts.Add(nameof(NpcTaskSaveable));
            }

            // AgentController attaches an AgentGoal at runtime, so a prefab may not carry one at edit
            // time. AgentGoalSaveable requires it, so adding the saver adds the goal — which is what
            // AgentController would have done anyway.
            if ((go.GetComponent<AgentGoal>() != null || go.GetComponent<AgentController>() != null) &&
                go.GetComponent<AgentGoalSaveable>() == null)
            {
                go.AddComponent<AgentGoalSaveable>();
                parts.Add(nameof(AgentGoalSaveable));
            }

            if (go.GetComponent<HerdModule>() != null && go.GetComponent<HerdMemberSaveable>() == null)
            {
                go.AddComponent<HerdMemberSaveable>();
                parts.Add(nameof(HerdMemberSaveable));
            }

            if (go.GetComponent<FormationModule>() != null && go.GetComponent<FormationSaveable>() == null)
            {
                go.AddComponent<FormationSaveable>();
                parts.Add(nameof(FormationSaveable));
            }

            // The phase offset that stops a crowd marching in step for a moment after every load.
            if (go.GetComponent<AgentController>() != null && go.GetComponent<AgentPacingSaveable>() == null)
            {
                go.AddComponent<AgentPacingSaveable>();
                parts.Add(nameof(AgentPacingSaveable));
            }
        }

        /// <summary>
        /// What an agent was in the middle of doing: cooldowns, weapons, allegiance, motion.
        /// </summary>
        private static void EnsureAgentCombat(GameObject go, List<string> parts)
        {
            // Every cooldown in the game reloaded at zero, which is a free hit for whoever reloads:
            // a melee creature saved mid-swing struck immediately, a turret two seconds into its
            // reload was ready. One saver for all three modules because an agent composes them and
            // they hold the same shape of state.
            if (go.GetComponent<CombatCadenceSaveable>() == null &&
                (go.GetComponent<AgentRangedCombatModule>() != null ||
                 go.GetComponent<CloseCombatModule>() != null ||
                 go.GetComponent<NpcItemUseModule>() != null))
            {
                go.AddComponent<CombatCadenceSaveable>();
                parts.Add(nameof(CombatCadenceSaveable));
            }

            // Includes where the barrel pointed, which lives on a CHILD transform and so is invisible
            // to TransformSaveable.
            if (go.GetComponent<TurretSaveable>() == null &&
                (go.GetComponent<TurretModule>() != null || go.GetComponent<RocketLauncherTurret>() != null))
            {
                go.AddComponent<TurretSaveable>();
                parts.Add(nameof(TurretSaveable));
            }

            // Asked of the subtree: a WeaponMount lives on a hand bone while the saver belongs on the
            // entity — the same split ArticulatedPartsSaveable makes.
            if (go.GetComponentInChildren<WeaponMount>(true) != null &&
                go.GetComponent<WeaponMountSaveable>() == null)
            {
                go.AddComponent<WeaponMountSaveable>();
                parts.Add(nameof(WeaponMountSaveable));
            }

            // EntityInventorySaveable keeps what is in the bag; this keeps what is in the hand, and
            // stops Start re-equipping the authored starting slot over a restore.
            if (go.GetComponent<EntityEquipmentController>() != null &&
                go.GetComponent<EntityEquipmentSaveable>() == null)
            {
                go.AddComponent<EntityEquipmentSaveable>();
                parts.Add(nameof(EntityEquipmentSaveable));
            }

            // Which side this entity is on. SetFaction is a runtime reassignment — MatchManager
            // re-teams every arena spawn — and nothing captured it, so a re-teamed entity reloaded on
            // its prefab's faction and either turned on its own side or became untargetable.
            if (go.GetComponent<EntityFaction>() != null && go.GetComponent<EntityFactionSaveable>() == null)
            {
                go.AddComponent<EntityFactionSaveable>();
                parts.Add(nameof(EntityFactionSaveable));
            }

            // Which health thresholds have already fired. Without it, onThresholdReached re-fires on
            // the first hit after every load — a badly hurt creature replays its enrage and its
            // scream — and any module a threshold switched off comes back on.
            if (go.GetComponent<HealthReactionModule>() != null &&
                go.GetComponent<HealthReactionSaveable>() == null)
            {
                go.AddComponent<HealthReactionSaveable>();
                parts.Add(nameof(HealthReactionSaveable));
            }

            // What the motor was in the middle of. Any of the five, because an entity carries exactly
            // one and the saver writes only the block for the motor it finds. Includes the flag that
            // says what a mid-arc body's isKinematic should go back to — lose that and an agent saved
            // mid-leap is permanently kinematic and unpushable.
            if (go.GetComponent<MotorStateSaveable>() == null &&
                (go.GetComponent<NavMeshAgentMotor>() != null ||
                 go.GetComponent<RigidbodyMotor>() != null ||
                 go.GetComponent<HoverRigidbodyMotor>() != null ||
                 go.GetComponent<FlyingRigidbodyMotor>() != null ||
                 go.GetComponent<LeggedDriver>() != null))
            {
                go.AddComponent<MotorStateSaveable>();
                parts.Add(nameof(MotorStateSaveable));
            }

            // Cosmetic and lowest priority of anything here: it removes the one visible stumble a
            // legged machine makes on load, as every foot snaps to a default stance and the body
            // settles from an unprimed ride height.
            if (go.GetComponent<LeggedLocomotion>() != null && go.GetComponent<LeggedGaitSaveable>() == null)
            {
                go.AddComponent<LeggedGaitSaveable>();
                parts.Add(nameof(LeggedGaitSaveable));
            }
        }

        /// <summary>
        /// Things in the world a player changes and expects to stay changed.
        ///
        /// These all reach <see cref="NeedsSaving"/> through <c>IPersistentEntity</c>, which they
        /// implement for exactly this reason: a door has no health, no NavMeshAgent and no
        /// non-kinematic Rigidbody, so every inference the policy makes about "this can move" said no
        /// and none of them were saved at all.
        /// </summary>
        private static void EnsureWorldInteractables(GameObject go, List<string> parts)
        {
            if (go.GetComponent<DoorInteraction>() != null && go.GetComponent<DoorSaveable>() == null)
            {
                go.AddComponent<DoorSaveable>();
                parts.Add(nameof(DoorSaveable));
            }

            if (go.GetComponent<LeverInteraction>() != null && go.GetComponent<LeverSaveable>() == null)
            {
                go.AddComponent<LeverSaveable>();
                parts.Add(nameof(LeverSaveable));
            }

            if (go.GetComponent<RepairWorkstation>() != null &&
                go.GetComponent<RepairWorkstationSaveable>() == null)
            {
                go.AddComponent<RepairWorkstationSaveable>();
                parts.Add(nameof(RepairWorkstationSaveable));
            }

            // The game's win condition. Deposit two of three scrap, reload, and it was back at zero.
            if (go.GetComponent<SpaceGame.World.Ship>() != null && go.GetComponent<ShipSaveable>() == null)
            {
                go.AddComponent<ShipSaveable>();
                parts.Add(nameof(ShipSaveable));
            }

            if (go.GetComponent<SpaceGame.Gameplay.Trading.TraderInteraction>() != null &&
                go.GetComponent<TraderSaveable>() == null)
            {
                go.AddComponent<TraderSaveable>();
                parts.Add(nameof(TraderSaveable));
            }

            if (go.GetComponent<VolumeTrigger>() != null && go.GetComponent<VolumeTriggerSaveable>() == null)
            {
                go.AddComponent<VolumeTriggerSaveable>();
                parts.Add(nameof(VolumeTriggerSaveable));
            }

            if (go.GetComponent<SpaceGame.Items.RuinSecret>() != null &&
                go.GetComponent<RuinSecretSaveable>() == null)
            {
                go.AddComponent<RuinSecretSaveable>();
                parts.Add(nameof(RuinSecretSaveable));
            }

            if (go.GetComponent<SpaceshipManager>() != null && go.GetComponent<SpaceshipSaveable>() == null)
            {
                go.AddComponent<SpaceshipSaveable>();
                parts.Add(nameof(SpaceshipSaveable));
            }

            // "Once per scene load" was literally what playOnce meant, so a one-time cutscene played
            // again on every load — and so did whatever its onCutsceneEnded event was wired to.
            if (go.GetComponent<SpaceGame.Presentation.CutsceneAction>() != null &&
                go.GetComponent<CutsceneActionSaveable>() == null)
            {
                go.AddComponent<CutsceneActionSaveable>();
                parts.Add(nameof(CutsceneActionSaveable));
            }

            // A scanner beacon marks a cache the player has already been shown. It has no health, no
            // pickup, no agent and no loose body, so it qualified for nothing until it declared
            // itself an entity — which is why a spent beacon lit up again on every load.
            if (go.GetComponent<ScanBeacon>() != null && go.GetComponent<ScanBeaconSaveable>() == null)
            {
                go.AddComponent<ScanBeaconSaveable>();
                parts.Add(nameof(ScanBeaconSaveable));
            }
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
