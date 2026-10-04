// A seat has one occupant, lets go of a sitter it is carried away from, is found by an id every machine derives alike,
// serves one sit spot each, and every sit spot a prefab brings has a seat to sit on.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SpaceGame.Agents.Residents;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class SeatTests
    {
        private const string PrefabsDir = "Assets/Game/Prefabs/Environment";
        private const float Reach = 1.5f;

        private readonly List<GameObject> made = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in made)
            {
                if (go == null) continue;
                go.GetComponent<Seat>()?.Unregister();
                Object.DestroyImmediate(go);
            }
            made.Clear();
        }

        private Seat MakeSeat(string name, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            made.Add(go);
            Seat seat = go.AddComponent<Seat>();
            seat.Register();
            return seat;
        }

        private Transform MakeBody(string name)
        {
            var go = new GameObject(name);
            made.Add(go);
            return go.transform;
        }

        [Test]
        public void ASeatHasOneOccupant()
        {
            Seat seat = MakeSeat("OneOccupant", Vector3.zero);
            Transform first = MakeBody("first"), second = MakeBody("second");

            Assert.IsTrue(seat.TryClaim(first));
            Assert.IsFalse(seat.TryClaim(second), "taken");
            Assert.IsTrue(seat.TryClaim(first), "the sitter's own claim is idempotent");
            Assert.AreSame(first, seat.Occupant);

            Assert.IsFalse(seat.Release(second), "a stranger cannot evict the sitter");
            Assert.AreSame(first, seat.Occupant);
        }

        [Test]
        public void ReleasingFreesTheSeatForTheNextSitter_AndSaysSo()
        {
            Seat seat = MakeSeat("Release", Vector3.zero);
            Transform first = MakeBody("first"), second = MakeBody("second");
            int vacated = 0;
            Transform left = null;
            seat.Vacated += (_, who) => { vacated++; left = who; };

            seat.TryClaim(first);
            Assert.IsTrue(seat.Release(first));
            Assert.AreEqual(1, vacated);
            Assert.AreSame(first, left);
            Assert.IsTrue(seat.IsFree);
            Assert.IsFalse(seat.Release(first), "releasing twice does nothing");
            Assert.AreEqual(1, vacated);
            Assert.IsTrue(seat.TryClaim(second));
        }

        [Test]
        public void ADestroyedSitterFreesItsSeat()
        {
            Seat seat = MakeSeat("Destroyed", Vector3.zero);
            Transform sitter = MakeBody("sitter");
            seat.TryClaim(sitter);

            Object.DestroyImmediate(sitter.gameObject);

            Assert.IsTrue(seat.IsFree);
            Assert.IsNull(seat.Occupant);
            Assert.IsTrue(seat.TryClaim(MakeBody("next")));
        }

        [Test]
        public void ASeatThatMovesThrowsItsSitterOff()
        {
            Seat seat = MakeSeat("Moved", Vector3.zero);
            Transform sitter = MakeBody("sitter");
            Transform thrown = null;
            seat.Vacated += (_, who) => thrown = who;
            seat.TryClaim(sitter);

            seat.transform.position += new Vector3(0.01f, 0f, 0f);
            seat.ReleaseIfMoved();
            Assert.AreSame(sitter, seat.Occupant, "a nudge inside the tolerance keeps the sitter");

            seat.transform.position += new Vector3(1f, 0f, 0f);
            seat.ReleaseIfMoved();
            Assert.IsTrue(seat.IsFree);
            Assert.AreSame(sitter, thrown);
        }

        [Test]
        public void ASeatThatTurnsThrowsItsSitterOff()
        {
            Seat seat = MakeSeat("Turned", Vector3.zero);
            seat.TryClaim(MakeBody("sitter"));

            seat.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            seat.ReleaseIfMoved();

            Assert.IsTrue(seat.IsFree);
        }

        [Test]
        public void HasMoved_ComparesPositionAndYaw()
        {
            Assert.IsFalse(Seat.HasMoved(Vector3.zero, 0f, new Vector3(0.04f, 0f, 0f), 5f, 0.05f, 10f));
            Assert.IsTrue(Seat.HasMoved(Vector3.zero, 0f, new Vector3(0.06f, 0f, 0f), 0f, 0.05f, 10f));
            Assert.IsTrue(Seat.HasMoved(Vector3.zero, 350f, Vector3.zero, 20f, 0.05f, 10f), "30 degrees across the wrap");
            Assert.IsFalse(Seat.HasMoved(Vector3.zero, 358f, Vector3.zero, 5f, 0.05f, 10f), "7 degrees across the wrap is within tolerance");
        }

        [Test]
        public void ANearestFreeSeatIsFoundAcrossTheGround_NeverATakenOne()
        {
            Seat near = MakeSeat("Near", new Vector3(0.5f, 0f, 0f));
            Seat far = MakeSeat("Far", new Vector3(1.4f, 0f, 0f));
            MakeSeat("TooFar", new Vector3(3f, 0f, 0f));
            near.transform.position += Vector3.up * 5f;   // height is not distance: a sit point is above the floor a spot stands on

            Assert.AreSame(near, Seat.NearestFree(Vector3.zero, Reach));
            near.TryClaim(MakeBody("sitter"));
            Assert.AreSame(far, Seat.NearestFree(Vector3.zero, Reach), "the taken one is skipped");
            far.TryClaim(MakeBody("other"));
            Assert.IsNull(Seat.NearestFree(Vector3.zero, Reach), "the third is out of reach");
        }

        [Test]
        public void AnIdIsFoundAgain_NeverZero_AndNamesTheSeat()
        {
            Seat a = MakeSeat("IdA", Vector3.zero), b = MakeSeat("IdB", Vector3.zero);

            Assert.AreNotEqual(0, a.Id);
            Assert.AreEqual(Seat.IdOf(a.gameObject), a.Id);
            Assert.AreNotEqual(a.Id, b.Id);
            Assert.AreSame(a, Seat.Find(a.Id));
            Assert.AreSame(b, Seat.Find(b.Id));
            Assert.IsNull(Seat.Find(0), "0 is no seat on the wire");

            a.Unregister();
            Assert.IsNull(Seat.Find(a.Id), "a seat switched off cannot be found");
        }

        [Test]
        public void EachSeatServesOneSitSpot()
        {
            Seat one = MakeSeat("One", Vector3.zero);
            Seat two = MakeSeat("Two", new Vector3(10f, 0f, 0f));
            var spots = new List<Vector3> { Vector3.zero, new Vector3(0.2f, 0f, 0f), new Vector3(10f, 0f, 0.3f), new Vector3(30f, 0f, 0f) };

            List<int> seatless = SettlementPlaces.SeatlessSpots(spots, new[] { one, two }, Reach);

            CollectionAssert.AreEqual(new[] { 1, 3 }, seatless, "the second spot over the first seat and the spot far from any seat have none");
        }

        [Test]
        public void EverySitSpotOfEveryPrefabHasASeat()
        {
            string[] seatedUses = AssetDatabase.FindAssets("t:SpotUse")
                .Where(guid => AssetDatabase.LoadAssetAtPath<SpotUse>(AssetDatabase.GUIDToAssetPath(guid)).seated)
                .ToArray();

            var offenders = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabsDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string yaml = File.ReadAllText(path);
                if (!seatedUses.Any(yaml.Contains)) continue;

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    List<SettlementSpot> sits = root.GetComponentsInChildren<SettlementSpot>(true).Where(s => s.Use != null && s.Use.seated).ToList();

                    List<int> seatless = SettlementPlaces.SeatlessSpots(sits.ConvertAll(s => s.Position), root.GetComponentsInChildren<Seat>(true), Reach);
                    offenders.AddRange(seatless.Select(i => $"{path}: '{sits[i].name}' ({sits[i].Use.name})"));
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            Assert.IsEmpty(offenders, "sit spots with no Seat of their own (run Tools > SpaceGame > Residents > Place Seats At Sit Spots):\n" + string.Join("\n", offenders));
        }

        [TestCase("Clay")]
        [TestCase("Pillow")]
        [TestCase("Wood")]
        public void ASeatPrefabSitsAboveItsFloor_AndCanBePosed(string kind)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsDir}/Decorations/Furniture/Deco_Seat_{kind}.prefab");
            Assert.IsNotNull(prefab, kind);
            Seat seat = prefab.GetComponent<Seat>();
            Assert.IsNotNull(seat, "a Seat on the root");
            Assert.IsNotNull(prefab.GetComponent<SpaceGame.Agents.ChairPose>(), "a ChairPose to sit a player down");

            Assert.Greater(seat.SitPoint.localPosition.y, 0.3f, "the sit point is above the floor");
            Assert.AreEqual(prefab.transform.position.y, seat.FeetPosition.y, 1e-4f, "the feet hang to the floor the root stands on");
            Assert.IsTrue(prefab.GetComponents<Collider>().Any(c => c.isTrigger), "a trigger on the root the crosshair can hit");
        }

        // Read off the prefab assets, not the class: the field's default is Stool and a prefab keeps whatever it stored.
        [TestCase("Clay", SeatPose.Stool)]
        [TestCase("Wood", SeatPose.Stool)]
        [TestCase("Pillow", SeatPose.Stool)]
        public void ASeatPrefabSaysHowItsSitterSits(string kind, SeatPose expected)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsDir}/Decorations/Furniture/Deco_Seat_{kind}.prefab");

            Assert.AreEqual(expected, prefab.GetComponent<Seat>().Pose,
                            "all three sit 0.56 m up with a body under the sitter, so its legs hang down the front: the cross-legged loops " +
                            "(Floor) put the lower legs inside the pillow and fit only a cushion on the ground");
        }
    }
}
