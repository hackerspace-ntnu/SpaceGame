// Wanted posters: the raster pieces the baker paints with, the type it sets, the bake key, and the baked prefabs.
// The last test reads the ASSETS, so a poster prefab whose picture is stale or missing fails here.
using System.Linq;
using NUnit.Framework;
using SpaceGame.EditorTools.Posters;
using SpaceGame.World.Posters;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class WantedPosterTests
    {
        private const string LabelFontPath = "Assets/Game/Art/Fonts/Posters/BlackOpsOne-Regular.ttf";

        [Test]
        public void HalftonePrintsTheCoverageItIsAskedFor()
        {
            var coverage = new PosterMask(240, 240);
            for (int i = 0; i < coverage.Values.Length; i++) coverage.Values[i] = 0.3f;

            PosterMask dots = PosterShapes.Halftone(coverage, 12f, 45f);

            // Away from the edges, where the screen is complete.
            float sum = 0f;
            int n = 0;
            for (int y = 40; y < 200; y++)
            for (int x = 40; x < 200; x++, n++)
                sum += dots.Values[y * dots.Width + x];
            Assert.That(sum / n, Is.EqualTo(0.3f).Within(0.06f));
        }

        [Test]
        public void CircleCoversItsArea()
        {
            PosterMask disc = PosterShapes.Circle(200, 200, new Vector2(100f, 100f), 40f);
            Assert.That(disc.Values.Sum(), Is.EqualTo(Mathf.PI * 40f * 40f).Within(60f));
        }

        [Test]
        public void ShiftMovesCoverageAndLeavesTheGapEmpty()
        {
            var mask = new PosterMask(10, 10);
            mask.Values[0] = 1f;
            PosterMask moved = mask.Shift(3, 2);
            Assert.AreEqual(1f, moved.Values[2 * 10 + 3]);
            Assert.AreEqual(0f, moved.Values[0]);
        }

        [Test]
        public void RotatingAnticlockwiseLiftsTheRightHandEnd()
        {
            // A wide bar turned 30 degrees anticlockwise: its right end rises up the sheet.
            var bar = new PosterImage(100, 10, Color.white);
            var sheet = new PosterImage(200, 200, Color.clear);
            sheet.DrawRotated(bar, new Vector2(100f, 100f), 30f);
            Assert.Greater(sheet.Pixels[(100 - 25) * 200 + 143].a, 0.5f, "right end should be above the centre line");
            Assert.AreEqual(0f, sheet.Pixels[(100 + 25) * 200 + 143].a, 0.01f, "right end should not drop below it");
        }

        [Test]
        public void TypeIsSetToTheRequestedCapHeightOnItsBaseline()
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(LabelFontPath);
            Assert.IsNotNull(font, $"missing {LabelFontPath}");

            PosterMask h = PosterText.Render(font, "H", 48f, 0f, out float baseline);

            int top = -1, bottom = -1;
            for (int y = 0; y < h.Height; y++)
            for (int x = 0; x < h.Width; x++)
            {
                if (h.Values[y * h.Width + x] < 0.5f) continue;
                if (top < 0) top = y;
                bottom = y;
            }

            Assert.That(bottom - top + 1, Is.EqualTo(48).Within(3), "cap height");
            Assert.That(bottom + 1, Is.EqualTo(baseline).Within(2f), "letters stand on the baseline");
        }

        [Test]
        public void APosterWithNothingBakedCannotBeRead()
        {
            var go = new GameObject("PosterTest", typeof(BoxCollider));
            try
            {
                var poster = go.AddComponent<WantedPoster>();
                Assert.IsFalse(poster.CanInteract(), "nothing baked yet");
                Set(poster, "displayName", "Kessek Vorr");
                StringAssert.Contains("Kessek Vorr", poster.Label);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheBakeKeyFollowsEveryPrintedField()
        {
            var go = new GameObject("PosterTest");
            try
            {
                var poster = go.AddComponent<WantedPoster>();
                string first = WantedPosterBaker.KeyOf(poster);
                Assert.AreEqual(first, WantedPosterBaker.KeyOf(poster), "the same fields give the same key");

                Set(poster, "displayName", "Sabel Nook");
                string named = WantedPosterBaker.KeyOf(poster);
                Assert.AreNotEqual(first, named, "a new name is a new poster");

                var so = new SerializedObject(poster);
                so.FindProperty("status").enumValueIndex = (int)WantedStatus.Dead;
                so.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreNotEqual(named, WantedPosterBaker.KeyOf(poster), "DEAD and ALIVE are different posters");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void EveryPosterOnAPrefabIsBakedAndReadable()
        {
            var style = AssetDatabase.LoadAssetAtPath<WantedPosterStyle>(WantedPosterBaker.StylePath);
            Assert.IsNotNull(style, $"missing {WantedPosterBaker.StylePath}");

            const string bake = "select it and press Bake again, or run Tools > SpaceGame > Posters > Bake All Wanted Posters";
            int checkedPosters = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (WantedPoster poster in AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<WantedPoster>(true))
                {
                    if (poster.Agent == null) continue;
                    checkedPosters++;
                    string who = $"{path}: {poster.name}";
                    Assert.IsTrue(WantedPosterBaker.IsCurrent(poster), $"{who} shows a stale or missing picture: {bake}");
                    Assert.AreEqual(style.sheetPixels, new Vector2Int(poster.Poster.width, poster.Poster.height), $"{who}'s picture is the wrong size");
                    Assert.IsNotNull(poster.Sheet, $"{who} has no sheet to show its picture on");
                    Assert.IsNotNull(poster.GetComponentInChildren<Collider>(true), $"{who} has no collider to be read by");
                }
            }

            Assert.Greater(checkedPosters, 0, "no poster prefab has an agent");
        }

        private static void Set(WantedPoster poster, string field, string value)
        {
            var so = new SerializedObject(poster);
            so.FindProperty(field).stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
