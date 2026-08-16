// Tests for the generic network channel.
//
// The property under test throughout is the degradation contract: with no NetworkManager — which is
// what an EditMode test, a scene opened straight from the editor, and a torn-down session all look
// like — every entry point still runs the handler locally rather than throwing or going silent.
// That is what stops an un-networked system from taking a session down with it.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Core;

namespace SpaceGame.Tests
{
    public class NetMessagingTests
    {
        private readonly List<GameObject> spawned = new();

        private GameObject NewObject(string name = "entity")
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);

            spawned.Clear();
        }

        // ─────────── Message ids ───────────

        [Test]
        public void MessageIdsAreUnique()
        {
            var seen = new Dictionary<ushort, string>();

            foreach (FieldInfo field in typeof(NetMsg).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(ushort)) continue;

                var id = (ushort)field.GetValue(null);
                Assert.IsFalse(seen.ContainsKey(id),
                    $"NetMsg.{field.Name} reuses id {id}, already taken by NetMsg.{seen.GetValueOrDefault(id)}. " +
                    "Ids route messages to handlers and travel between builds — only ever append.");

                seen[id] = field.Name;
            }

            Assert.Greater(seen.Count, 0, "NetMsg has no ids at all — the reflection lookup is wrong.");
        }

        [Test]
        public void ZeroIsNotUsed()
        {
            foreach (FieldInfo field in typeof(NetMsg).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(ushort)) continue;

                Assert.AreNotEqual(0, (ushort)field.GetValue(null),
                    $"NetMsg.{field.Name} is 0, which is also what an uninitialised ushort is.");
            }
        }

        // ─────────── Payload ───────────

        [Test]
        public void NetArgSurvivesSerialization()
        {
            var sent = new NetArg
            {
                Target = 12345,
                A = -7,
                B = 99,
                P = new Vector3(1.5f, -2.25f, 3f),
                R = Quaternion.Euler(10f, 20f, 30f),
            };

            var writer = new FastBufferWriter(256, Allocator.Temp);
            try
            {
                writer.WriteNetworkSerializable(sent);

                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    reader.ReadNetworkSerializable(out NetArg received);

                    Assert.AreEqual(sent.Target, received.Target);
                    Assert.AreEqual(sent.A, received.A);
                    Assert.AreEqual(sent.B, received.B);
                    Assert.AreEqual(sent.P, received.P);
                    Assert.AreEqual(sent.R.x, received.R.x, 1e-5f);
                }
                finally { reader.Dispose(); }
            }
            finally { writer.Dispose(); }
        }

        [Test]
        public void WithCarriesTheSubjectOfflineWhereNoIdExists()
        {
            GameObject subject = NewObject("subject");

            var arg = new NetArg { A = 3 }.With(subject);

            // No NetworkManager, so there is no id to mint — and the message must still know what
            // it is about, or every single-player use of a subject-carrying message breaks.
            Assert.AreEqual(0UL, arg.Target, "An unspawned object cannot have a NetworkObjectId.");
            Assert.AreSame(subject, arg.Resolve());
        }

        [Test]
        public void ResolveIsNullWhenThereIsNoSubject()
        {
            Assert.IsNull(new NetArg { A = 1 }.Resolve());
            Assert.IsNull(new NetArg().With((GameObject)null).Resolve());
        }

        // ─────────── Dispatch ───────────

        [Test]
        public void HandlerReceivesTheMessageOffline()
        {
            GameObject entity = NewObject();
            var probe = entity.AddComponent<Probe>();

            probe.NetOn(NetMsg.UseItem, probe.Record);
            probe.NetToServer(NetMsg.UseItem, new NetArg { A = 42 });

            Assert.AreEqual(1, probe.Calls, "Offline, a request to the server must run here.");
            Assert.AreEqual(42, probe.LastArg.A);
        }

        [Test]
        public void HandlerStopsReceivingAfterNetOff()
        {
            GameObject entity = NewObject();
            var probe = entity.AddComponent<Probe>();

            probe.NetOn(NetMsg.UseItem, probe.Record);
            probe.NetOff(NetMsg.UseItem, probe.Record);
            probe.NetToServer(NetMsg.UseItem);

            Assert.AreEqual(0, probe.Calls);
        }

        [Test]
        public void MessageWithNoHandlerIsIgnored()
        {
            GameObject entity = NewObject();
            var probe = entity.AddComponent<Probe>();

            probe.NetOn(NetMsg.UseItem, probe.Record);
            probe.NetToServer(NetMsg.Damage);   // nobody listening for this one

            Assert.AreEqual(0, probe.Calls);
        }

        [Test]
        public void AThrowingHandlerDoesNotStopTheOthers()
        {
            GameObject entity = NewObject();
            var probe = entity.AddComponent<Probe>();

            probe.NetOn(NetMsg.UseItem, probe.Throw);
            probe.NetOn(NetMsg.UseItem, probe.Record);

            // The failure is reported, loudly — it is just not allowed to swallow the message.
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("threw"));
            probe.NetToServer(NetMsg.UseItem, new NetArg { A = 8 });

            Assert.AreEqual(1, probe.Calls, "A broken handler must not cost the next one its message.");
            Assert.AreEqual(8, probe.LastArg.A);
        }

        [Test]
        public void HandlersOnChildrenShareTheEntitysChannel()
        {
            GameObject entity = NewObject("root");
            var child = new GameObject("held item");
            child.transform.SetParent(entity.transform);

            var listener = child.AddComponent<Probe>();
            var sender = entity.AddComponent<Probe>();

            listener.NetOn(NetMsg.UseItem, listener.Record);
            sender.NetToServer(NetMsg.UseItem, new NetArg { A = 5 });

            Assert.AreEqual(1, listener.Calls,
                "A weapon nested under a player must hear messages addressed to that player.");
        }

        [Test]
        public void ToOthersIsANoOpWithNoWire()
        {
            GameObject entity = NewObject();
            var probe = entity.AddComponent<Probe>();

            probe.NetOn(NetMsg.ItemUsed, probe.Record);
            probe.NetToOthers(NetMsg.ItemUsed);

            Assert.AreEqual(0, probe.Calls,
                "'Everyone but me' has to be nobody when this machine is everyone.");
        }

        [Test]
        public void SendingToAnotherEntityReachesThatEntity()
        {
            GameObject attacker = NewObject("attacker");
            GameObject victim = NewObject("victim");

            var attackerProbe = attacker.AddComponent<Probe>();
            var victimProbe = victim.AddComponent<Probe>();

            attackerProbe.NetOn(NetMsg.Damage, attackerProbe.Record);
            victimProbe.NetOn(NetMsg.Damage, victimProbe.Record);

            NetMessaging.NetSendTo(victim, NetMsg.Damage, new NetArg { A = 25 }.With(attacker));

            Assert.AreEqual(1, victimProbe.Calls, "Damage belongs on the victim's channel.");
            Assert.AreEqual(0, attackerProbe.Calls, "...and nowhere else.");
            Assert.AreEqual(25, victimProbe.LastArg.A);
            Assert.AreSame(attacker, victimProbe.LastArg.Resolve());
        }

        [Test]
        public void SendingToNothingIsSafe()
        {
            Assert.DoesNotThrow(() => NetMessaging.NetSendTo(null, NetMsg.Damage, default));
            Assert.DoesNotThrow(() => ((Component)null).NetToServer(NetMsg.Damage));
            Assert.DoesNotThrow(() => ((Component)null).NetOff(NetMsg.Damage, (in NetArg _, ulong __) => { }));
        }

        /// <summary>A stand-in for any gameplay component that joins the channel.</summary>
        private class Probe : MonoBehaviour
        {
            public int Calls;
            public NetArg LastArg;

            public void Record(in NetArg arg, ulong sender)
            {
                Calls++;
                LastArg = arg;
            }

            public void Throw(in NetArg arg, ulong sender) =>
                throw new System.InvalidOperationException("deliberate");
        }
    }
}
