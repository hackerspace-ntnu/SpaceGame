// Persistence coverage for the world's prefabs.
//
// ── If you are adding a prefab, read this ─────────────────────────────────────────────────────
//
// You probably do not need to write anything. The two sweeps at the top of this file already cover
// every prefab in the project, including yours, the moment it exists — that is deliberate, because
// coverage that depends on somebody remembering is coverage this project has already lost once.
//
// Write a per-prefab test only when the prefab has state a sweep cannot know about: a rig to trim, a
// hatch to open, a fuel level. The template is three lines:
//
//     [Test]
//     public void Thing_KeepsItsWhatever() =>
//         PersistenceProbe.For("Assets/Game/Prefabs/.../Thing.prefab")
//             .Mutate(go => go.GetComponent<Whatever>().SetSomething(0.7f))
//             .AssertSurvivesRoundTrip();
//
// Mutate() into a state a PLAYER could put it in, then let the probe prove that state comes back.
// The probe handles capture, real JSON text, restoring onto a fresh instance and comparing — you
// only supply the change. See PersistenceProbe.cs for what each assertion actually proves.
//
// ── What these tests can and cannot see ───────────────────────────────────────────────────────
//
// EditMode does not run MonoBehaviour Awake. Savers here are written to lazy-resolve their component
// precisely so they work anyway, but a saver that depends on state BUILT in Awake — or on a runtime
// registry — genuinely cannot round-trip here. That is what Excluding<T>() is for, and it is the only
// legitimate use of it: excluding a saver because its round trip fails is hiding the bug.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.DuneFoil;

namespace SpaceGame.EditorTools
{
    public class PrefabPersistenceTests
    {
        // ─────────────────────────────────────────────
        //  Zero-maintenance coverage
        // ─────────────────────────────────────────────

        /// <summary>
        /// The regression test for the bug this whole system was rebuilt around: mounts and vehicles
        /// that no save file ever contained, with nothing anywhere reporting it.
        /// </summary>
        [Test]
        public void EveryWorldEntityPrefabIsWiredForSaving() =>
            PersistenceProbe.AssertEveryWorldEntityPrefabIsWired();

        /// <summary>
        /// Catches the slower version of the same failure: a prefab wired months ago that has since
        /// gained a MountModule or an AgentTargeting, and quietly stopped saving what they own.
        /// </summary>
        [Test]
        public void EveryWiredPrefabHasTheSaversItsComponentsImply() =>
            PersistenceProbe.AssertEveryWiredPrefabHasItsSavers();

        /// <summary>
        /// The half neither sweep above can see, because both of them ask Unity rather than the file.
        /// A prefab whose <c>prefabId</c> is only ever filled in by <c>OnValidate</c> looks correct in
        /// the editor and ships blank — and anything spawned from it is captured into the save and
        /// then dropped, so it is missing from the world with nothing said. The PlayerShip reached
        /// exactly that state when its builder was run in Play mode, where the wiring pass refuses.
        /// </summary>
        [Test]
        public void EveryWorldEntityPrefabCarriesItsPrefabIdOnDisk() =>
            PersistenceProbe.AssertEveryWorldEntityPrefabIsStampedOnDisk();

        /// <summary>
        /// A saveable prefab nested inside another keeps its own entity, and OnValidate stamps that
        /// entity with the OUTER prefab's id. The map projector inside the PlayerShip did exactly
        /// this, and every load of a world put a second hull on top of the first.
        /// </summary>
        [Test]
        public void NoWorldEntityPrefabNestsASecondSaveableEntity() =>
            PersistenceProbe.AssertNoWorldEntityPrefabNestsASecondSaveableEntity();

        /// <summary>
        /// The second half of the same nesting bug: with the nested entity gone, the nested
        /// object's own TransformSaveable is collected after the root's under the same key, and a
        /// capture keeps the child's pose as the whole object's.
        /// </summary>
        [Test]
        public void NoWorldEntityPrefabHasTwoSaversOnOneKey() =>
            PersistenceProbe.AssertOneSaverPerKeyOnEveryWorldEntityPrefab();

        /// <summary>
        /// A floor under the sweeps. If the discovery query breaks — a moved folder, a renamed root —
        /// both sweeps above start passing while checking nothing at all, which is the one way a
        /// project-wide test can fail silently.
        /// </summary>
        [Test]
        public void TheSweepActuallyFindsPrefabs()
        {
            int found = 0;
            foreach (var _ in PersistenceProbe.WorldEntityPrefabs()) found++;

            Assert.Greater(found, 5,
                $"Only {found} world-entity prefab(s) found under {PersistenceProbe.PrefabRoot}. The " +
                "sweeps are passing because they are looking at nothing.");
        }

        // ─────────────────────────────────────────────
        //  Per-prefab: the templates to copy
        // ─────────────────────────────────────────────

        private const string Ostrich = "Assets/Game/Prefabs/agents/creatures/Ostrich.prefab";
        private const string Golem = "Assets/Game/Prefabs/agents/creatures/Golem.prefab";
        private const string DuneFoil = "Assets/Game/Prefabs/agents/Vehicles/Ground/DuneFoil.prefab";
        private const string PatrolRobot = "Assets/Game/Prefabs/agents/Robots/PatrolRobot.prefab";
        private const string PlayerShip = "Assets/Game/Prefabs/agents/Vehicles/Spacecraft/PlayerShip.prefab";

        [Test]
        public void Ostrich_IsWiredForSaving() =>
            PersistenceProbe.For(Ostrich).AssertWiredCorrectly();

        /// <summary>
        /// The Ostrich is the prefab that proved the old policy wrong: a kinematic Rigidbody with no
        /// NavMeshAgent and no HealthComponent, which every clause of the old opt-in test missed. So
        /// "it moved and stayed moved" is the assertion worth making about it.
        /// </summary>
        [Test]
        public void Ostrich_StaysWhereItWalkedTo() =>
            PersistenceProbe.For(Ostrich)
                .Mutate(go => go.transform.SetPositionAndRotation(
                    new Vector3(120f, 8f, -45f), Quaternion.Euler(0f, 137f, 0f)))
                .AssertSurvivesRoundTrip();

        [Test]
        public void Golem_StaysWounded() =>
            PersistenceProbe.For(Golem)
                .Mutate(go => go.GetComponent<HealthComponent>().Damage(7))
                .AssertSurvivesRoundTrip();

        [Test]
        public void Golem_RemembersWhatItWasFighting() =>
            PersistenceProbe.For(Golem)
                .Mutate(go => go.GetComponent<AgentTargeting>()
                    .RestoreMemory(null, new Vector3(30f, 2f, 12f), true, 1.5f, null))
                .AssertSurvivesRoundTrip();

        /// <summary>
        /// The rig is the only part of sailing that costs a player effort, so it is the part a reload
        /// must not throw away. Also the prefab with no Rigidbody on its root at all — the one case no
        /// amount of component sniffing could ever have found.
        /// </summary>
        [Test]
        public void DuneFoil_KeepsItsRigTrimmed() =>
            PersistenceProbe.For(DuneFoil)
                .Mutate(go =>
                {
                    SailRig rig = go.GetComponent<SailRig>();

                    foreach (SailSurface sail in rig.Sails)
                    {
                        if (sail == null) continue;
                        sail.SetSheet(0.35f);
                        sail.SetCant(-0.4f);
                        sail.SetHoist(1f);
                    }
                })
                .AssertSurvivesRoundTrip();

        [Test]
        public void DuneFoil_StaysMoored() =>
            PersistenceProbe.For(DuneFoil)
                .Mutate(go => go.GetComponent<DuneFoilLocomotion>().HoldStation = true)
                .AssertSurvivesRoundTrip();

        /// <summary>
        /// The hull modules a player found, hauled home and fitted. This is the entire reward of the
        /// salvage loop, and it is the one thing on a wrecked ship that a reload must not undo —
        /// coming back to the same hole in the roof with the motor gone from the pack too is worse
        /// than never having found it.
        /// </summary>
        [Test]
        public void PlayerShip_KeepsTheModulesFittedToIt() =>
            PersistenceProbe.For(PlayerShip)
                .Mutate(go =>
                {
                    ShipPartRack rack = go.GetComponent<ShipPartRack>();

                    // Two, not all: a mask that happens to equal "everything" would pass even if the
                    // saver were writing a constant.
                    rack.RestoreMask(0b101);
                })
                .AssertSurvivesRoundTrip();

        // ─────────────────────────────────────────────
        //  The gaps the 2026-08 audit found
        // ─────────────────────────────────────────────

        /// <summary>
        /// The single largest behavioural gap the audit turned up.
        ///
        /// <c>ProvocationModule</c> is the ONLY thing that can make a Fauna creature hostile —
        /// <c>AgentTargeting.Reevaluate</c> structurally cannot, because Fauna is Neutral to
        /// everything. It had no saver at all and <c>OnEnable</c> called <c>Forget()</c>
        /// unconditionally, so you could shoot a Golem, watch it charge, reload, and find it
        /// peacefully wandering — permanently unable to re-acquire you on its own.
        /// </summary>
        [Test]
        public void Golem_KeepsItsGrudge() =>
            PersistenceProbe.For(Golem)
                .Mutate(go => go.GetComponent<ProvocationModule>().RestoreGrudge(go.transform, 4.5f))
                .AssertSurvivesRoundTrip();

        /// <summary>
        /// Threshold latches, which did not merely go missing — they misfired.
        ///
        /// <c>HealthThresholdReaction.triggered</c> was reset in <c>OnEnable</c> and never recorded,
        /// so <c>onThresholdReached</c> fired again on the first hit after every single load. A badly
        /// hurt creature replayed its enrage event and its scream every time the world was opened.
        /// </summary>
        [Test]
        public void Golem_RemembersWhichThresholdsHaveFired()
        {
            PersistenceProbe.For(Golem)
                .Mutate(go =>
                {
                    var reactions = go.GetComponent<HealthReactionModule>();
                    if (reactions == null) return;

                    bool[] fired = reactions.TriggeredThresholds();
                    if (fired.Length == 0) return;

                    fired[0] = true;
                    reactions.RestoreThresholds(fired);
                })
                .AssertSurvivesRoundTrip();
        }

        /// <summary>
        /// A guard's territory, which drifted a little further on every save/load cycle.
        ///
        /// In <c>PatrolMode.RadiusBased</c> the anchor was re-latched from <c>transform.position</c>
        /// after a load, so the patrol circle silently re-centred on wherever the guard happened to
        /// be standing when the game was saved.
        /// </summary>
        [Test]
        public void PatrolRobot_KeepsItsPostAndItsPlaceOnTheRoute() =>
            PersistenceProbe.For(PatrolRobot)
                .Mutate(go =>
                {
                    var patrol = go.GetComponent<PatrolModule>();
                    patrol.RestoreSpawnAnchor(true, new Vector3(84f, 3f, -19f));
                    patrol.RestorePatrolLeg(true, new Vector3(90f, 3f, -12f), 1.25f);
                })
                .AssertSurvivesRoundTrip();

        /// <summary>
        /// The patrol robots are the reason patrol progress had to leave <c>AgentStateSaveable</c>:
        /// they have a <c>PatrolModule</c> and no <c>AgentTargeting</c>, so the saver keyed off the
        /// latter never reached the one population whose entire identity is a route.
        /// </summary>
        [Test]
        public void PatrolRobot_IsWiredForSaving() =>
            PersistenceProbe.For(PatrolRobot).AssertWiredCorrectly();

        /// <summary>
        /// What makes the memory <c>AgentStateSaveable</c> already kept actually do something.
        ///
        /// <c>SearchModule</c> starts only on a falling edge — had a target, lost it — and after a
        /// load <c>hadTarget</c> was always false, so the edge could never fire. The last-known
        /// position the save went out of its way to preserve was never walked to.
        /// </summary>
        [Test]
        public void Golem_ResumesTheSearchItWasOn()
        {
            PersistenceProbe.For(Golem)
                .Mutate(go =>
                {
                    var search = go.GetComponent<SearchModule>();
                    if (search == null) return;

                    search.RestoreSearch(true, 3f, new Vector3(12f, 1f, 40f), true);
                })
                .AssertSurvivesRoundTrip();
        }
    }
}
