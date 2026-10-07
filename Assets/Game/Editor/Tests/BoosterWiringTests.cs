using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The clamped booster prefab, checked on disk.
    ///
    /// <para>
    /// The plume is a MESH and not a billboard, because <c>SpaceGame/Effects/JetFlame</c> reads its
    /// own object space as the flame's frame: base at y = 0 radius 1, tip at y = 1 radius 0. A cone
    /// hung at the wrong angle, at the wrong end, or on a particle renderer draws a flame pointing
    /// somewhere the booster is not pushing, and nothing in the console says so.
    /// </para>
    /// <para>
    /// <see cref="NetworkPrefabRegistrationTests"/> already sweeps every item prefab in the
    /// project, so registration is not re-checked here.
    /// </para>
    /// </summary>
    public class BoosterWiringTests
    {
        private const string ClampedPath =
            "Assets/Game/Prefabs/Items/Artifacts/Gadgets/ClampedStrapOnBooster.prefab";

        private static GameObject Clamped()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ClampedPath);
            Assert.IsNotNull(prefab, $"No prefab at {ClampedPath}.");
            return prefab;
        }

        private const string ItemPath =
            "Assets/Game/Prefabs/Items/Artifacts/Gadgets/StrapOnBooster.prefab";

        /// <summary>
        /// The shipped tuning, read off the asset rather than off the class.
        ///
        /// A `[SerializeField]` keeps whatever the prefab was saved with, so a default changed in
        /// code and never applied to the asset is a number that is true in the editor and false in
        /// the game.
        /// </summary>
        [Test]
        public void ThePackHoldsFiveAndPushesAtTheShippedThrust()
        {
            var item = AssetDatabase.LoadAssetAtPath<GameObject>(ItemPath);
            Assert.IsNotNull(item, $"No prefab at {ItemPath}.");

            var carried = new SerializedObject(item.GetComponent<StrapOnBoosterItem>());
            Assert.AreEqual(5, carried.FindProperty("maxUses").intValue,
                            "Five boosters to a pack. A charge is spent only by a clamp that " +
                            "landed — see StrapOnBoosterItem.Use.");

            var mount = new SerializedObject(Clamped().GetComponent<BoosterMount>());
            Assert.AreEqual(40f, mount.FindProperty("thrustAcceleration").floatValue, 1e-3f,
                            "40 m/s² against 18 of gravity is 22 of climb — about 95 m for a " +
                            "player, and a landing they have to arrange.");
        }

        [Test]
        public void Shell_KnowsWhichWayItPoints()
        {
            BoosterShell shell = Clamped().GetComponent<BoosterShell>();

            Assert.IsNotNull(shell, "The clamped booster carries the shell that seats it.");
            Assert.IsTrue(shell.IsWired,
                          "Marker_Mount and Marker_Muzzle are what every seating measurement is " +
                          "taken between. Without them the booster clamps at an arbitrary angle.");
            Assert.AreNotEqual(Vector3.zero, shell.LocalExhaustAxis);
        }

        [Test]
        public void Plume_LeavesTheMuzzleAlongTheBore()
        {
            GameObject booster = Clamped();
            Transform plume = booster.transform.Find("Exhaust/Plume");
            Transform muzzle = booster.transform.Find("Armed/Marker_Muzzle");

            Assert.IsNotNull(plume, "The flame is a cone under Exhaust, not a particle system.");
            Assert.IsNotNull(muzzle);

            Assert.AreEqual(0f, Vector3.Distance(plume.position, muzzle.position), 1e-3f,
                            "The cone's BASE is the nozzle mouth; the shader grows it from y = 0.");

            BoosterShell shell = booster.GetComponent<BoosterShell>();
            Vector3 axis = booster.transform.InverseTransformDirection(plume.up).normalized;

            Assert.AreEqual(1f, Vector3.Dot(shell.LocalExhaustAxis, axis), 1e-3f,
                            "The cone's +Y is the direction the exhaust leaves. Any other angle " +
                            "draws a flame that disagrees with the thrust.");
        }

        [Test]
        public void Plume_BurnsTheJetpacksFlame()
        {
            Transform plume = Clamped().transform.Find("Exhaust/Plume");
            var renderer = plume.GetComponent<MeshRenderer>();

            Assert.IsNotNull(plume.GetComponent<MeshFilter>().sharedMesh,
                             "The unit cone IS the JetFlame contract — the shader holds no " +
                             "measurements of its own.");
            Assert.AreEqual("SpaceGame/Effects/JetFlame", renderer.sharedMaterial.shader.name);
            Assert.IsFalse(renderer.enabled,
                           "Authored dark. The plume is switched on for the burn and off at " +
                           "burnout — a booster lying in the sand is not on fire.");
        }

        [Test]
        public void Smoke_IsTheJetpacksSmoke()
        {
            var trail = Clamped().transform.Find("Exhaust/Trail");

            Assert.IsNotNull(trail, "The booster smokes from one system, the way the pack does.");

            var renderer = trail.GetComponent<ParticleSystemRenderer>();
            Assert.AreEqual("SpaceGame/Effects/JetSmoke", renderer.sharedMaterial.shader.name);

            ParticleSystem.MainModule main = trail.GetComponent<ParticleSystem>().main;
            Assert.AreEqual(ParticleSystemSimulationSpace.World, main.simulationSpace,
                            "A local-space trail follows the booster and reads as a stuck decal.");
            Assert.IsFalse(main.playOnAwake,
                           "It is lit by the clamp landing, not by the prefab being instantiated.");
        }
    }
}
