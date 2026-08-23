using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Keeps the ropes tied.
    ///
    /// <para>
    /// A <see cref="Leash"/> is the one thing in this game that no other saver could have covered.
    /// It is not a prefab and it is not spawned: <c>LeashArtifact</c> builds it with a bare
    /// <c>new GameObject</c>, adds the components by hand and calls <c>DontDestroyOnLoad</c> — so it
    /// has no <c>prefabId</c>, <c>SaveablePrefabRegistry</c> can never resolve one, and putting a
    /// <c>SaveableEntity</c> on it would produce a record nothing could rebuild. Its two endpoints
    /// were raw <c>Transform</c> and <c>Rigidbody</c> references, which a file cannot hold either.
    /// So two tethered objects came back untethered, every time.
    /// </para>
    /// <para>
    /// <b>Global rather than per-object, because a rope belongs to neither end.</b> One end may be a
    /// crate the server simulates and the other a player who simulates themselves; filing the record
    /// under either would lose the rope whenever that one was unloaded, and filing it under both
    /// would restore two ropes.
    /// </para>
    /// <para>
    /// <b>Its own retry loop.</b> Global savers get no <see cref="IDeferredSaveable"/> pass —
    /// <c>SaveManager</c> runs that only for world entities and bound players — and both endpoints
    /// are references that may still be streaming in. So this one keeps its unresolved entries and
    /// tries again each frame, for a bounded window, tying each rope the moment both of its ends
    /// exist.
    /// </para>
    /// </summary>
    public class LeashSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "leashes";

        public string SaveKey => Key;

        /// <summary>
        /// How long to keep waiting for an endpoint that has not appeared.
        ///
        /// Long enough for chunks to stream in around a player who loaded on the far side of the
        /// world, short enough that a rope to something genuinely deleted stops being retried. An
        /// entry that times out is dropped with a warning rather than silently.
        /// </summary>
        private const float ResolveWindowSeconds = 60f;

        // ── Format ─────────────────────────────────────────────────────────────

        /// <summary>One end of a rope. Exactly one of <see cref="anchor"/> and <see cref="point"/> matters.</summary>
        public struct Endpoint
        {
            /// <summary>What it is tied to, when that is something the save system can name.</summary>
            public SaveRef anchor;

            /// <summary>Where on that thing, in its own local space — so it survives the thing moving.</summary>
            public Vector3 offset;

            /// <summary>Where in the world, for an end pinned to bare geometry that has no identity.</summary>
            public Vector3 point;

            /// <summary>True for the end a player is holding.</summary>
            public bool held;
        }

        public struct Rope
        {
            public Endpoint a;
            public Endpoint b;

            /// <summary>
            /// Carried because <c>TerminateHandEndOnto</c> stretches it to fit the geometry it lands
            /// on. Rebuilding a rope at the artifact's authored length would snap a long one the
            /// instant physics resumed.
            /// </summary>
            public float maxLength;
        }

        public struct State
        {
            public List<Rope> ropes;
        }

        // ── Capture ────────────────────────────────────────────────────────────

        public object CaptureState()
        {
            IReadOnlyList<Leash> live = Leash.All;
            if (live == null || live.Count == 0) return null;

            var ropes = new List<Rope>(live.Count);

            for (int i = 0; i < live.Count; i++)
            {
                Leash leash = live[i];
                if (leash == null) continue;
                if (leash.EndATransform == null || leash.EndBTransform == null) continue;

                ropes.Add(new Rope
                {
                    a = Describe(leash.aKind, leash.EndATransform, leash.aLocalOffset, leash.EndAPos),
                    b = Describe(leash.bKind, leash.EndBTransform, leash.bLocalOffset, leash.EndBPos),
                    maxLength = leash.maxLength,
                });
            }

            return ropes.Count == 0 ? null : new State { ropes = ropes };
        }

        /// <summary>
        /// Turn one live endpoint into something a file can hold.
        ///
        /// <para>
        /// An object with no <see cref="SaveRef"/> — a prop nobody opted into saving — degrades to
        /// its world point rather than losing the whole rope. The rope then comes back tied to that
        /// place instead of to that thing, which is wrong in exactly the way a prop that is not
        /// saved is wrong: it was not going to be there either.
        /// </para>
        /// </summary>
        private static Endpoint Describe(Leash.EndpointKind kind, Transform xform,
                                         Vector3 localOffset, Vector3 worldPoint)
        {
            if (kind == Leash.EndpointKind.Static)
                return new Endpoint { point = worldPoint };

            SaveRef anchor = SaveRef.From(xform.gameObject);

            if (!anchor.IsSet)
                return new Endpoint { point = worldPoint };

            return new Endpoint
            {
                anchor = anchor,
                offset = localOffset,
                point = worldPoint,
                held = kind == Leash.EndpointKind.PlayerHand,
            };
        }

        // ── Restore ────────────────────────────────────────────────────────────

        private readonly List<Rope> pending = new();
        private float deadline;

        public void RestoreState(JObject state)
        {
            pending.Clear();
            deadline = 0f;

            // Every rope from before this load is retired first. Leashes call DontDestroyOnLoad on
            // themselves, so returning to the menu and opening another world leaves the previous
            // world's ropes hanging in the new one — and restoring on top of them would tie a second
            // copy of each.
            DisposeLive();

            if (state == null) return;

            List<Rope> ropes = state.ToObject<State>(SaveSerializer.Serializer).ropes;
            if (ropes == null || ropes.Count == 0) return;

            pending.AddRange(ropes);
            deadline = Time.time + ResolveWindowSeconds;
        }

        private static void DisposeLive()
        {
            IReadOnlyList<Leash> live = Leash.All;
            if (live == null) return;

            for (int i = live.Count - 1; i >= 0; i--)
                live[i]?.Dispose();
        }

        private void Update()
        {
            if (pending.Count == 0) return;

            // Walked backwards so a rope tied this frame can be removed without disturbing the ones
            // still waiting.
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (TryTie(pending[i])) pending.RemoveAt(i);
            }

            if (pending.Count == 0 || Time.time < deadline) return;

            Debug.LogWarning($"[Save] {pending.Count} leash(es) could not be tied: an endpoint never " +
                             "appeared. The objects they were tied to are gone from this world.", this);
            pending.Clear();
        }

        /// <summary>
        /// Tie one saved rope, if both of its ends are here. False means "not yet", never "never".
        /// </summary>
        private static bool TryTie(in Rope rope)
        {
            if (!TryResolve(rope.a, out GameObject rootA)) return false;
            if (!TryResolve(rope.b, out GameObject rootB)) return false;

            LeashArtifact.TryResolveSettings(out Leash.Settings settings);

            // The stored length wins over the artifact's authored one — see Rope.maxLength.
            if (rope.maxLength > 0.01f) settings.maxLength = rope.maxLength;

            Leash leash = Leash.Create(settings);

            leash.RestoreEndpointA(new Leash.EndpointRestore
            {
                Root = rootA, LocalOffset = rope.a.offset, WorldPoint = rope.a.point, Held = rope.a.held,
            });

            leash.RestoreEndpointB(new Leash.EndpointRestore
            {
                Root = rootB, LocalOffset = rope.b.offset, WorldPoint = rope.b.point, Held = rope.b.held,
            });

            return true;
        }

        /// <summary>
        /// The live object for an endpoint, or false while it is still on its way.
        ///
        /// <para>
        /// A null object with <c>true</c> is a legitimate answer and means "this end is a place, not
        /// a thing" — <see cref="Leash.RestoreEndpointA"/> makes an anchor for it.
        /// </para>
        /// </summary>
        private static bool TryResolve(in Endpoint endpoint, out GameObject root)
        {
            root = null;
            if (!endpoint.anchor.IsSet) return true;

            return endpoint.anchor.TryResolve(out root);
        }

        private void OnEnable() => SaveManager.RegisterGlobalSaver(this);

        private void OnDisable() => SaveManager.UnregisterGlobalSaver(this);
    }
}
