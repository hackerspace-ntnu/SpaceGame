// SaveRef is the only way one saved object can name another, so a fault here is invisible: a
// reference that fails to describe or fails to resolve reads as "nobody was riding" and "no target",
// which are both legitimate worlds. These tests pin the difference between "no referent" and
// "referent I could not find".
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;

namespace SpaceGame.Tests
{
    public class SaveRefTests
    {
        /// <summary>
        /// A binder with no scene behind it. The real one needs the live entity registry and the player
        /// bindings; the contract it implements needs neither, which is exactly why
        /// <see cref="ISaveRefBinder"/> is an interface rather than a pair of statics.
        /// </summary>
        private class FakeBinder : ISaveRefBinder
        {
            public GameObject Described;
            public string Kind = SaveRef.EntityKind;
            public string Id = "abc";
            public GameObject Resolved;

            public bool TryDescribe(GameObject target, out string kind, out string id)
            {
                kind = null;
                id = null;
                if (target == null || target != Described) return false;

                kind = Kind;
                id = Id;
                return true;
            }

            public bool TryResolve(string kind, string id, out GameObject target)
            {
                target = null;
                if (kind != Kind || id != Id) return false;

                target = Resolved;
                return target != null;
            }
        }

        private FakeBinder binder;
        private GameObject subject;

        [SetUp]
        public void SetUp()
        {
            subject = new GameObject("Rider");
            binder = new FakeBinder { Described = subject, Resolved = subject };
            SaveRefBinding.Active = binder;
        }

        [TearDown]
        public void TearDown()
        {
            SaveRefBinding.Active = null;
            if (subject != null) Object.DestroyImmediate(subject);
        }

        [Test]
        public void From_DescribesAKnownObject()
        {
            SaveRef reference = SaveRef.From(subject);

            Assert.IsTrue(reference.IsSet);
            Assert.AreEqual(SaveRef.EntityKind, reference.Kind);
            Assert.AreEqual("abc", reference.Id);
        }

        [Test]
        public void From_Null_IsUnset()
        {
            Assert.IsFalse(SaveRef.From((GameObject)null).IsSet);
            Assert.IsFalse(SaveRef.From((Component)null).IsSet);
        }

        [Test]
        public void From_UnknownObject_IsUnset()
        {
            var stranger = new GameObject("Stranger");
            try
            {
                Assert.IsFalse(SaveRef.From(stranger).IsSet);
            }
            finally
            {
                Object.DestroyImmediate(stranger);
            }
        }

        [Test]
        public void TryResolve_FindsTheReferent()
        {
            Assert.IsTrue(SaveRef.From(subject).TryResolve(out GameObject resolved));
            Assert.AreSame(subject, resolved);
        }

        [Test]
        public void TryResolve_Unset_IsFalseAndNotAnError()
        {
            Assert.IsFalse(SaveRef.None.TryResolve(out GameObject resolved));
            Assert.IsNull(resolved);
        }

        [Test]
        public void TryResolve_ReferentGone_IsFalse()
        {
            SaveRef reference = SaveRef.From(subject);
            binder.Resolved = null;   // destroyed since the save was written

            Assert.IsFalse(reference.TryResolve(out _));
        }

        [Test]
        public void TryResolve_WithNoBinder_IsFalseRatherThanThrowing()
        {
            // Outside a session there is nothing to resolve against, and every saver holding a ref is
            // still constructed and still asks. Throwing here would take a whole load down.
            SaveRef reference = SaveRef.From(subject);
            SaveRefBinding.Active = null;

            Assert.IsFalse(reference.TryResolve(out _));
        }

        [Test]
        public void SurvivesTheSaveSerializer()
        {
            // Through the same serializer a StateBag uses. The field names travel into save files, so a
            // change to them orphans every rider and target already stored.
            SaveRef original = SaveRef.From(subject);

            JObject json = JObject.FromObject(original, SaveSerializer.Serializer);

            Assert.AreEqual(SaveRef.EntityKind, json["kind"]?.Value<string>());
            Assert.AreEqual("abc", json["id"]?.Value<string>());

            var restored = json.ToObject<SaveRef>(SaveSerializer.Serializer);

            Assert.AreEqual(original.Kind, restored.Kind);
            Assert.AreEqual(original.Id, restored.Id);
            Assert.IsTrue(restored.TryResolve(out GameObject resolved));
            Assert.AreSame(subject, resolved);
        }

        [Test]
        public void PlayerAndEntityKindsAreDistinct()
        {
            // The two populations are keyed differently — profile vs instance id — so a ref that lost
            // its kind would look up a profile id in the entity registry and silently miss.
            Assert.AreNotEqual(SaveRef.PlayerKind, SaveRef.EntityKind);

            binder.Kind = SaveRef.PlayerKind;
            SaveRef asPlayer = SaveRef.From(subject);

            Assert.IsFalse(new SaveRef { Kind = SaveRef.EntityKind, Id = asPlayer.Id }.TryResolve(out _));
        }
    }
}
