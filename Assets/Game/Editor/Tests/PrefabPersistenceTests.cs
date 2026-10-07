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
        private const string ClankerOutrider = "Assets/Game/Prefabs/agents/Robots/ClankerOutrider.prefab";
        private const string DuneFoil = "Assets/Game/Prefabs/agents/Vehicles/Ground/DuneFoil.prefab";
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
                    .RestoreMemory(null, false, new Vector3(30f, 2f, 12f), true, 1.5f, null))
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
        /// A search in progress reloads as a search in progress, walking to the same place with the
        /// same time left. The ClankerOutrider, because it carries a <c>SearchModule</c>: this test
        /// used to probe the Golem, which has none, and returned early without asserting anything.
        /// Not the plain Clanker: its inventory and equipment savers need state built in
        /// <c>Awake</c>, which edit mode never runs, so its capture logs errors before any search
        /// is compared.
        /// </summary>
        [Test]
        public void ClankerOutrider_ResumesTheSearchItWasOn()
        {
            PersistenceProbe.For(ClankerOutrider)
                .Mutate(go =>
                {
                    var search = go.GetComponent<SearchModule>();
                    Assert.IsNotNull(search, "the ClankerOutrider must carry a SearchModule for this test to mean anything");

                    search.RestoreSearch(true, 3f, new Vector3(12f, 1f, 40f));
                })
                .AssertSurvivesRoundTrip();
        }

        /// <summary>The satellite dish comes back pointing where a player slewed it, not at its authored rest.</summary>
        [Test]
        public void SatelliteTower_KeepsWhereTheDishWasPointed() =>
            PersistenceProbe.For("Assets/Game/Prefabs/Environment/Structures/SatelliteTower/SatelliteTower.prefab")
                .Mutate(go => go.GetComponent<DishRig>().RestoreAngles(212f, 71f))
                .AssertSurvivesRoundTrip();
    }
}
