// The sun shrine's geometry, read off the shipped prefab and the shipped Sun. Nothing here is a
// copy of a number: move a mirror, retune a step or change the length of the day and these re-ask
// the two questions the puzzle rests on — can it be solved at every hour it is awake, and does the
// moving sun actually force the players to keep up.
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Gameplay.Puzzles;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class SunShrineTests
    {
        private const string ShrinePath = "Assets/Game/Prefabs/Environment/SunShrine/SunShrine.prefab";
        private const string SunPath = "Assets/Game/Prefabs/Environment/Sky/Sun.prefab";

        private GameObject shrineObject;
        private SunShrine shrine;
        private DayNightCycle sun;

        [OneTimeSetUp]
        public void Load()
        {
            shrineObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ShrinePath));
            shrine = shrineObject.GetComponent<SunShrine>();
            sun = AssetDatabase.LoadAssetAtPath<GameObject>(SunPath).GetComponent<DayNightCycle>();
            Assert.IsNotNull(shrine, "SunShrine prefab has no SunShrine");
            Assert.IsNotNull(sun, "Sun prefab has no DayNightCycle");
        }

        [OneTimeTearDown]
        public void Unload() => Object.DestroyImmediate(shrineObject);

        /// <summary>Where the sun lies at <paramref name="degrees"/> through the day, as DayNightCycle poses it.</summary>
        private Vector3 ToSun(float degrees) =>
            -(Quaternion.AngleAxis(degrees, sun.rotationAxis) * Vector3.forward);

        private List<float> WakingDegrees()
        {
            var awake = new List<float>();
            for (float d = 0f; d < 360f; d += 1f)
                if (shrine.InWakingWindow(ToSun(d))) awake.Add(d);
            return awake;
        }

        [Test]
        public void TheShrineIsAwakeForAboutAThirdOfTheDay()
        {
            float fraction = WakingDegrees().Count / 360f;
            Assert.That(fraction, Is.InRange(0.25f, 0.4f),
                "Waiting for the shrine to wake should be rare and short.");
        }

        [Test]
        public void EveryHourItIsAwakeHasASolution()
        {
            foreach (float degrees in WakingDegrees())
            {
                Vector3 toSun = ToSun(degrees);
                bool solved = false;
                for (int a = 0; a < shrine.ArrangementCount && !solved; a++)
                    solved = shrine.LightsSeal(a, toSun);

                Assert.IsTrue(solved, $"No arrangement lights the seal with the sun {degrees}° round the day.");
            }
        }

        [Test]
        public void TheSunDriftsEverySolutionOffWithinAboutAnHour()
        {
            // How many consecutive degrees of sun travel each arrangement keeps the seal lit for.
            var run = new Dictionary<int, float>();
            var longest = new Dictionary<int, float>();
            foreach (float degrees in WakingDegrees())
            {
                Vector3 toSun = ToSun(degrees);
                for (int a = 0; a < shrine.ArrangementCount; a++)
                {
                    if (!shrine.LightsSeal(a, toSun)) { run.Remove(a); continue; }
                    run[a] = run.TryGetValue(a, out float r) ? r + 1f : 1f;
                    longest[a] = Mathf.Max(longest.TryGetValue(a, out float l) ? l : 0f, run[a]);
                }
            }

            float secondsPerDegree = sun.cycleDuration / 360f;
            float inGameHour = sun.cycleDuration / 24f;
            foreach (KeyValuePair<int, float> entry in longest)
            {
                float seconds = entry.Value * secondsPerDegree;
                Assert.That(seconds, Is.LessThanOrEqualTo(2f * inGameHour),
                    $"Arrangement {entry.Key} holds the seal for {seconds:F0} s: set it once and walk away.");
            }

            float best = 0f;
            foreach (float degrees in longest.Values) best = Mathf.Max(best, degrees * secondsPerDegree);
            Assert.That(best, Is.GreaterThanOrEqualTo(20f),
                "No arrangement stays right long enough to hold the seal and adjust.");
        }
    }
}
