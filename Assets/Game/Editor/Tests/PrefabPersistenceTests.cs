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

        private const string Ostrich = "Assets/Game/Prefabs/Agents/Creatures/Ostrich.prefab";
        private const string Golem = "Assets/Game/Prefabs/Agents/Creatures/Golem.prefab";
        private const string DuneFoil = "Assets/Game/Prefabs/Agents/Vehicles/Ground/DuneFoil.prefab";

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
    }
}
