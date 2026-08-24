using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Items
{
    /// <summary>
    /// The player's half of the backpack: which pack is theirs, where it rides, and the four states
    /// it moves between.
    ///
    /// The pack instance is created once and lives as long as the wearer does. Deploying unparents
    /// it, re-shouldering parents it back. Spawning a fresh pack per deploy would be simpler, but
    /// the strap items would pop out of existence on every toggle and the contents would need
    /// somewhere else to live.
    ///
    /// Which machine decides what, and why the pack is not a spawned network object, is written out
    /// above the wire protocol further down and beside the Instantiate call in Awake.
    /// </summary>
    public class BackpackController : MonoBehaviour
    {
        public enum State { Shouldered, Deploying, Open, Stowing }

        [Header("Pack")]
        [SerializeField] private GameObject backpackPrefab;

        [Header("Back socket")]
        [Tooltip("Which bone the pack rides on when the rig is humanoid.")]
        [SerializeField] private HumanBodyBones backBone = HumanBodyBones.Spine;

        [Tooltip("Substring hints used when auto-resolving the back socket on a non-humanoid rig " +
                 "(case-insensitive). The first child Transform whose name contains any of these wins.")]
        [SerializeField] private string[] backBoneNameHints = { "Spine", "Chest", "Torso" };

        [Tooltip("Manual override, used only when auto-resolve fails.")]
        [SerializeField] private Transform backSocketOverride;

        [SerializeField] private Vector3 wornLocalPosition = new(0f, 0.12f, -0.18f);
        [SerializeField] private Vector3 wornLocalEuler = new(0f, 0f, 0f);

        [Header("Deploy")]
        [SerializeField, Min(0.05f)] private float deploySeconds = 0.9f;

        [Tooltip("Metres in front of the player the pack is set down. Measured to the pack's ORIGIN, " +
                 "which is the bottom centre of its footprint — so half its depth is nearer than " +
                 "this number, and a value near the pack's own depth puts it against your chest.")]
        // Far enough out that the pack focus camera — PackFocusCamera.DistanceOut (1.9 m) back
        // from the rig along the player→pack line — lands IN FRONT of the player's body instead
        // of behind it, with half a metre to spare. Shrinking this below DistanceOut puts the
        // player back between the lens and the pack.
        [SerializeField, Min(0.2f)] private float deployDistance = 2.4f;

        // ── The toss ─────────────────────────────────────────────────────────
        //
        // The deploy flight starts HERE, in front of the player, not at the back socket where the
        // pack is actually worn. The worn pose is behind them, where a first-person camera never
        // looks: a deploy launched from the back spent its first frames out of frame and read as
        // the pack appearing behind the player's back. The animation's one job is to say where the
        // pack went (GDC-L1-ANIM-0003), so the whole flight has to happen on screen — it appears
        // at chest height in front, as if tossed down, and drops a pace ahead.

        [Tooltip("Where the toss appears: metres in front of the player, on the ground plane.")]
        [SerializeField, Min(0f)] private float tossStartForward = 0.45f;

        [Tooltip("Where the toss appears: metres above the player's feet. Chest height keeps the " +
                 "whole flight inside a first-person frame.")]
        [SerializeField, Min(0f)] private float tossStartHeight = 1.25f;

        [Tooltip("Control-point lift for the toss, in metres. Same convention as arcHeight below: " +
                 "the pack rises HALF this above the straight chest-to-ground line, so a small " +
                 "value is a lob and zero is a straight drop.")]
        [SerializeField, Min(0f)] private float tossArcHeight = 0.5f;

        // ── Over the shoulder (stow only) ────────────────────────────────────
        //
        // These two are the whole of the STOW flight's shape — the deploy is the toss above. Both
        // of them are CONTROL POINT offsets, not distances the pack travels. A quadratic Bezier
        // reaches exactly half its control point's offset from the chord, so the numbers here read
        // about twice as large as the arc they describe. That is worth writing down because the
        // pair they replaced looked sensible and did nothing:
        //
        //   arcHeight 0.6, from a back socket at ~1.2 m to the ground 1.6 m ahead. Height along the
        //   curve works out as y(t) = h0(1-t) + 2A t(1-t), whose peak is at t = (2A - h0) / 4A —
        //   NEGATIVE for any A below h0/2. At A = 0.6 the apex was therefore t = 0, i.e. the back
        //   socket itself: the pack never rose at all, it only fell more slowly. It crossed the
        //   player's own plane at 1.19 m, 6 cm to the side, which is straight out through the chest.
        //
        // The apex of that curve is (h0 + A) / 2 + h0² / 8A, so a 1.2 m socket needs A = 2.67 for a
        // 2.0 m apex. 2.6 gives 1.97 m at t = 0.385 — the pack clears a 1.8 m head with a fifth of a
        // metre to spare, peaks a little over a third of the way through, and comes down in front.
        [Tooltip("Control-point lift, in metres. The pack reaches HALF this above the straight " +
                 "line, so the apex is roughly (socketHeight + this) / 2 — 2.6 puts it near 2.0 m, " +
                 "which is over the head of a standing player. Below half the socket height the " +
                 "arc has no apex at all and the pack simply sinks from the back to the ground.")]
        [SerializeField] private float arcHeight = 2.6f;

        [Tooltip("Control-point bow, in metres, square to the run. The pack reaches HALF this, so " +
                 "0.55 swings it 0.275 m out — a shoulder's width off the spine rather than a " +
                 "nudge. NEGATIVE bows it over the other shoulder; the sign is the only thing that " +
                 "chooses a side, and nothing on the pack reads handedness or aim.")]
        [SerializeField] private float arcOutward = 0.55f;

        [Tooltip("Metres the pack is lifted off the ground hit point along the surface normal. The " +
                 "field backpack's origin is already at the bottom centre of its footprint and it " +
                 "stands upright, so this only has to clear z-fighting with the sand. A mesh whose " +
                 "origin sits at its centre would need half its height here.")]
        [SerializeField] private float groundLift = 0.01f;

        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("Log where the pack was actually placed relative to the player's facing, every " +
                 "deploy. On while the drop direction is under suspicion; off once it is proven.")]
        [SerializeField] private bool verboseDeploy;

        public State CurrentState { get; private set; } = State.Shouldered;
        public BackpackObject Pack { get; private set; }

        private PlayerInputManager input;
        private Transform backSocket;
        private Coroutine arcRoutine;

        // Where the deploy currently in flight is headed. Held so an interrupted arc can land the
        // pack at its destination instead of wherever it had reached — see OnDisable.
        private Pose pendingDeployPose;
        private bool hasPendingDeploy;

        // Sized for the clutter a 3 m downward probe plausibly crosses on the way to the sand: the
        // player's own capsule, their pack, a doorway lip, then ground.
        private readonly RaycastHit[] groundHits = new RaycastHit[12];

        private void Awake()
        {
            input = GetComponent<PlayerInputManager>();

            backSocket = ResolveBackSocket();
            if (backSocket == null)
            {
                Debug.LogError("BackpackController: could not resolve a back socket. Assign " +
                               "backSocketOverride or add hints in backBoneNameHints.", this);
                return;
            }

            if (backpackPrefab == null)
            {
                Debug.LogError("BackpackController: no backpack prefab assigned.", this);
                return;
            }

            // A plain Instantiate, on every machine, and NOT GameServices.World.Spawn. That is a
            // decision rather than an oversight, and it rests on three things:
            //
            //   • World.Spawn returns null on a client by design, so a spawned pack could only ever
            //     be held by this field on the server. Every client's controller would have no Pack
            //     at all.
            //   • A shouldered pack rides the wearer's spine BONE. Netcode reparents a spawned
            //     NetworkObject only underneath another NetworkObject, so a networked pack could
            //     not be worn without hand-driving its pose every frame.
            //   • There is nothing to replicate here anyway. Every machine already has this player,
            //     so every machine builds the same pack from the same prefab with the same starting
            //     contents. What differs — where it is, and what is left in it — is replicated as
            //     state (NetMsg.PackState, and BackpackNetwork) rather than as an object.
            //
            // It is the same shape as a door: ArticulatedPart is a plain MonoBehaviour on a hinge
            // nobody spawned, driven by announcements on the vehicle's channel.
            GameObject instance = Instantiate(backpackPrefab, backSocket);
            Pack = instance.GetComponent<BackpackObject>();

            if (Pack == null)
            {
                Debug.LogError("BackpackController: backpack prefab has no BackpackObject.", this);
                Destroy(instance);
                return;
            }

            Pack.Bind(this);
            SnapToWorn();

            // Focus mode is added here rather than wired on the player prefab, the same way
            // UsableItem adds a HoldPose to whatever it is equipping: the two are a pair — a pack
            // you cannot rummage in is half a feature — and a prefab field is one more thing that
            // can be left unset on a body that was built before the feature existed. It costs a
            // component on every replica, which does nothing: the session refuses to open unless
            // this machine owns the pack.
            if (GetComponent<PackFocusSession>() == null) gameObject.AddComponent<PackFocusSession>();
        }

        /// <summary>
        /// Mirrors EquipmentController.ResolveHandSocket: the armature bone is always preferred, and
        /// the serialized Transform is only a manual override for rigs the resolver cannot handle.
        /// </summary>
        private Transform ResolveBackSocket()
        {
            var animator = GetComponentInChildren<Animator>(true);
            if (animator != null && animator.isHuman)
            {
                Transform bone = animator.GetBoneTransform(backBone);
                if (bone != null) return bone;
            }

            if (backBoneNameHints != null)
            {
                var all = GetComponentsInChildren<Transform>(true);
                foreach (Transform candidate in all)
                {
                    foreach (string hint in backBoneNameHints)
                    {
                        if (string.IsNullOrEmpty(hint)) continue;
                        if (candidate.name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                            return candidate;
                    }
                }
            }

            return backSocketOverride;
        }

        private void OnEnable()
        {
            if (input != null) input.OnBackpackPressed += Toggle;

            this.NetOn(NetMsg.PackState, OnPackStateMessage);
            this.NetOn(NetMsg.PackTake, OnTakeRequested);
            this.NetOn(NetMsg.PackMove, OnMoveRequested);
            this.NetOn(NetMsg.PackDrop, OnDropRequested);
            this.NetOn(NetMsg.PackStow, OnStowRequested);

            // Decided here rather than in the coroutine's first line, so an EditMode test and a
            // scene opened straight from the editor never enter the coroutine machinery at all.
            // Dies with the component and is restarted by the next OnEnable; asking twice is
            // harmless, because the answer is the state we already have.
            if (Network.IsNetworked && !Network.Server)
                StartCoroutine(AskForStateWhenConnected());
        }

        private void OnDisable()
        {
            if (input != null) input.OnBackpackPressed -= Toggle;

            this.NetOff(NetMsg.PackState, OnPackStateMessage);
            this.NetOff(NetMsg.PackTake, OnTakeRequested);
            this.NetOff(NetMsg.PackMove, OnMoveRequested);
            this.NetOff(NetMsg.PackDrop, OnDropRequested);
            this.NetOff(NetMsg.PackStow, OnStowRequested);

            // A coroutine dies with the component. Without this the pack is left hanging in mid-air,
            // unparented, halfway through an arc — which survives a scene reload as a floating pack.
            if (arcRoutine != null)
            {
                StopCoroutine(arcRoutine);
                arcRoutine = null;

                // The pack lands where it was GOING, never where it had got to.
                //
                // This was the "it deploys behind me" bug, and it is why fixing the drop direction
                // never made it go away: the arc then started on the player's back, so a deploy
                // interrupted in its first frames left the pack a few centimetres behind them —
                // unparented, IsWorn false, state Open. The toss now starts in front, but an
                // interrupted flight would still strand the pack short of its announced pose, so
                // landing it at the destination stays load-bearing.
                //
                // It fires constantly rather than rarely because this is a streaming world: the
                // player is disabled and re-enabled as scenes load, migrate and respawn around
                // them, and each of those lands mid-flight.
                if (CurrentState == State.Deploying)
                    FinishDeploy(hasPendingDeploy ? pendingDeployPose : CurrentWorldPose(Pack));
                else if (CurrentState == State.Stowing) SnapToWorn();
            }
        }

        /// <summary>
        /// Take the pack with us.
        ///
        /// <para>
        /// A shouldered pack is a child of the wearer and dies with them; a DEPLOYED one has been
        /// unparented, and nothing else in the game would ever destroy it. So a player who logs out
        /// while their pack is on the sand leaves it standing there on every machine — holding a
        /// container whose owner has despawned, which means nothing replicates it any more and the
        /// copies drift apart the first time anybody takes anything.
        /// </para>
        /// <para>
        /// Every machine destroys its own copy, at the moment that player's body despawns on it, so
        /// this needs nothing on the wire to stay consistent. It does mean a disconnect takes the
        /// pack's contents with it. Keeping them would mean the pack becoming a real spawned world
        /// object with a life of its own — a different feature, not a bug fix.
        /// </para>
        /// </summary>
        private void OnDestroy()
        {
            if (Pack != null) Destroy(Pack.gameObject);
        }

        // ── Across the network ───────────────────────────────────────────────────
        //
        // Where the pack is, and what is in it, are two different problems with two different
        // answers, and only the first one lives here.
        //
        // WHERE IT IS rides NetMsg.PackState on this player's own channel. The pack itself stays a
        // plain Instantiate — deliberately, and see the note on that line — so it has no
        // NetworkObject and no transform sync of its own. Instead every machine builds the same
        // pack from the same prefab in Awake and drives it from the same announcements, exactly the
        // way ArticulatedPartInteraction drives a door that nobody has spawned.
        //
        // WHAT IS IN IT is BackpackNetwork's, beside this component, because item ids are strings
        // and NetArg has no string field.
        //
        // The wire, on top of what NetMsg.PackState documents:
        //
        //   A  the state — 0 shouldered, 1 deploying, 2 open, 3 stowing — or -1 for "what state is
        //      this pack in?", which only a request may ask.
        //   B  the verb: 0 asks the server for a transition, 1 is the server announcing one.
        //   P  and R, for a deploy, the pose the pack is being set down at.
        //
        // Two verbs on one id rather than the request/announce PAIR that doors use, because only
        // one id was allocated for this. They cannot be told apart by "am I the server", tempting
        // as that is: on a host NetToServer and NetToAll BOTH dispatch locally, so a handler that
        // read an announcement as another request would answer its own answer, forever.
        //
        // There is no separate "instantly" verb either. It is carried by WHICH state is announced:
        // Deploying and Stowing are the two that animate, Open and Shouldered are the two settled
        // poses, and a late joiner is simply told the settled one.
        private const int AskState = -1;
        private const int RequestVerb = 0;
        private const int AnnounceVerb = 1;

        /// <summary>
        /// The wearer's own key press. The pack's placement is shared state, so this only ASKS —
        /// a pack that deployed on the presser's machine and stayed shouldered everywhere else is
        /// the failure this is shaped to avoid.
        /// </summary>
        public void Toggle()
        {
            // A replica of somebody else's body must never move their pack, whatever its input
            // component thinks it heard. Non-owners have their input disabled by
            // NetworkPlayerController, so this should be unreachable — it is here because the cost
            // of being wrong about that is four players' packs deploying at once.
            if (!Network.Owns(this)) return;

            switch (CurrentState)
            {
                case State.Shouldered: Deploy(); break;
                case State.Open: Reshoulder(); break;
                // Mid-flight. Swallowing the press is what makes the key safe to mash — restarting an
                // arc from a half-travelled pose would fling it somewhere neither end expects.
                default: break;
            }
        }

        /// <summary>Ask for the pack to be set down. Anyone may ask; the server decides.</summary>
        public void Deploy() => Request(State.Open);

        /// <summary>
        /// Ask for the pack to come back. Deliberately not restricted to the wearer: walking up to
        /// somebody's open pack and shutting it is how you hand it back to them.
        /// </summary>
        public void Reshoulder() => Request(State.Shouldered);

        private void Request(State destination) =>
            this.NetToServer(NetMsg.PackState, new NetArg { A = (int)destination, B = RequestVerb });

        /// <summary>
        /// A joining client asks where this pack is, once there is somebody to ask.
        ///
        /// Waits for the body's NetworkObject to actually be spawned rather than sending on the
        /// first frame: before that there is no relay, the send falls through to a local dispatch,
        /// and the client answers its own question with the state it already had — which is the
        /// prefab's, which is the thing being corrected. Same coroutine, same reason, as
        /// ArticulatedPartInteraction's.
        /// </summary>
        private IEnumerator AskForStateWhenConnected()
        {
            if (!Network.IsNetworked || Network.Server) yield break;

            GameObject root = NetChannel.RootOf(this);
            var netObj = root != null ? root.GetComponent<Unity.Netcode.NetworkObject>() : null;
            if (netObj == null) yield break;

            while (!netObj.IsSpawned)
            {
                if (!Network.IsNetworked) yield break;
                yield return null;
            }

            this.NetToServer(NetMsg.PackState, new NetArg { A = AskState, B = RequestVerb });
        }

        private void OnPackStateMessage(in NetArg arg, ulong sender)
        {
            if (arg.B == RequestVerb) HandleStateRequest(arg);
            else ApplyAnnouncedState((State)arg.A, new Pose(arg.P, arg.R));
        }

        /// <summary>Server side: decide whether the pack may move, then tell everyone.</summary>
        private void HandleStateRequest(in NetArg arg)
        {
            if (!Network.Simulates(this)) return;
            if (Pack == null) return;

            if (arg.A == AskState)
            {
                // Broadcast rather than sent back to the asker, because this layer has no unicast.
                // The cost is that a pack mid-flight on everybody else's screen snaps to where it
                // was going the moment someone joins. That is a frame of a 0.9 s arc, and it is the
                // same trade PartState makes when it answers a joiner "instantly".
                AnnounceState(SettledState(), SettledPose());
                return;
            }

            var destination = (State)arg.A;

            if (destination == State.Open)
            {
                // Re-checked here, not trusted from the sender: two players can press at once, and
                // the second one's message arrives after the first has already put the pack down.
                if (CurrentState != State.Shouldered) return;

                // The ground probe is the SERVER's. Every machine has a replica of the wearer to
                // aim from, but only the server is guaranteed to have the chunk under their feet
                // loaded — clients never stream their own — so a peer's own probe could find no
                // ground at all and refuse a deploy everybody else performed. The pose it finds
                // travels in the message so all four packs land on the same grain of sand.
                if (!TryFindGroundPose(out Pose grounded))
                {
                    Debug.Log("Backpack: no ground to set it down on.", this);
                    return;
                }

                LogDeploy(grounded);
                Commit(State.Deploying, grounded);
                return;
            }

            if (destination == State.Shouldered)
            {
                if (CurrentState != State.Open) return;

                Commit(State.Stowing, CurrentWorldPose(Pack));
            }
        }

        /// <summary>
        /// Server side: move our own pack, then tell everyone else.
        ///
        /// Applied here rather than left to arrive with our own broadcast, because a broadcast goes
        /// to <c>ClientsAndHost</c> — a server that is not also a client never hears it, and its
        /// own <see cref="CurrentState"/> would stay behind while it validated every later request
        /// against the stale value. On a host it DOES come back, and is dropped by
        /// <see cref="ApplyAnnouncedState"/>'s idempotence test. Same shape as NetLatch.
        /// </summary>
        private void Commit(State state, Pose drop)
        {
            ApplyAnnouncedState(state, drop);
            AnnounceState(state, drop);
        }

        private void AnnounceState(State state, Pose drop) =>
            this.NetToAll(NetMsg.PackState, new NetArg
            {
                A = (int)state,
                B = AnnounceVerb,
                P = drop.position,
                R = drop.rotation,
            });

        /// <summary>
        /// Every machine: put the pack where the server says it is.
        ///
        /// Idempotent, because it is not only reached by a transition — a joiner's question is
        /// answered to everybody, so every machine is told a state it is usually already in.
        /// </summary>
        private void ApplyAnnouncedState(State state, Pose drop)
        {
            if (Pack == null) return;
            if (state == CurrentState) return;

            switch (state)
            {
                case State.Deploying:  StartDeploy(drop); break;
                case State.Stowing:    StartReshoulder(); break;

                // The two settled poses. Only ever announced in answer to a joiner's question, so
                // they arrive as "it is already like this", never as a transition to watch.
                case State.Open:       StopArc(); FinishDeploy(drop); break;
                case State.Shouldered: StopArc(); SnapToWorn(); break;
            }
        }

        // ── Restore ────────────────────────────────────────────────────────────

        /// <summary>
        /// Where the pack is, as a save file should record it: never mid-flight.
        ///
        /// <para>
        /// The same answer a joiner gets, and for the same reason — an arc is 0.9 s of animation and
        /// storing a frame of one would restore a pack halfway through a throw with nothing left to
        /// finish it.
        /// </para>
        /// </summary>
        public State SavedState => SettledState();

        /// <summary>Where the pack would come to rest, for a save. See <see cref="SavedState"/>.</summary>
        public Pose SavedPose => Pack != null ? SettledPose() : default;

        /// <summary>
        /// Put the pack back where a save says it was.
        ///
        /// <para>
        /// Restore-only. Called by the save system; do not call from gameplay.
        /// </para>
        /// <para>
        /// Straight to the settled state, never through <see cref="StartDeploy"/>. The arc is the
        /// animation of a player throwing the pack down, and a load is not that — and an arc
        /// interrupted by the player being disabled a moment later is exactly the "it deploys behind
        /// me" bug documented in <see cref="OnDisable"/>, which a load is unusually likely to
        /// trigger because scenes are still streaming around the player at that moment.
        /// </para>
        /// <para>
        /// Announced on the server so the other machines put their copy of this player's pack in the
        /// same place. Every machine builds its own pack in Awake and moves it on this message, so
        /// there is nothing to spawn — only a state to agree on.
        /// </para>
        /// </summary>
        public void RestoreDeployState(State state, Pose grounded)
        {
            if (Pack == null) return;

            StopArc();

            if (state == State.Open)
            {
                FinishDeploy(grounded);
            }
            else
            {
                SnapToWorn();
            }

            // Idempotent on every receiver — ApplyAnnouncedState drops a state it is already in —
            // so a client that has not restored anything is simply told where the pack sits.
            if (!Network.IsNetworked || Network.Server)
                AnnounceState(state == State.Open ? State.Open : State.Shouldered,
                              state == State.Open ? grounded : CurrentWorldPose(Pack));
        }

        /// <summary>Where a pack mid-flight is going to end up, for answering a joiner.</summary>
        private State SettledState() => CurrentState switch
        {
            State.Deploying => State.Open,
            State.Stowing => State.Shouldered,
            _ => CurrentState,
        };

        private Pose SettledPose() =>
            CurrentState == State.Deploying && hasPendingDeploy ? pendingDeployPose
                                                                : CurrentWorldPose(Pack);

        private void StartDeploy(Pose grounded)
        {
            StopArc();

            // Not CurrentWorldPose(Pack): the flight starts from the toss point in front of the
            // player, not from the worn pose behind their back — see the toss fields. The start
            // shares the landing's rotation, so the pack falls already turned the way it will
            // stand, the way a pack somebody set down would.
            Pose start = new(transform.position
                             + DeployForward() * tossStartForward
                             + Vector3.up * tossStartHeight,
                             grounded.rotation);

            pendingDeployPose = grounded;
            hasPendingDeploy = true;

            CurrentState = State.Deploying;
            Pack.SetWorn(false);
            Pack.transform.SetParent(null, true);

            // No outward bow: the toss never crosses the player's own body, so there is nothing
            // for it to clear.
            arcRoutine = StartCoroutine(RunArc(start, () => grounded, tossArcHeight, 0f,
                                               () => FinishDeploy(grounded)));
        }

        private void StartReshoulder()
        {
            StopArc();

            CurrentState = State.Stowing;

            arcRoutine = StartCoroutine(RunStow());
        }

        /// <summary>
        /// The stow, as the deploy run backwards: <b>the rig collects itself where the player can
        /// see it, and only then does it fly.</b>
        ///
        /// <para>
        /// The deploy is arc-then-unfold — <see cref="FinishDeploy"/> lands the pack and opens it
        /// where it stands. The stow used to be neither: it started the fold and the flight on the
        /// same frame, "closes over the first third of the flight rather than before it", to avoid
        /// a pause between the keypress and the pack moving. The trouble is that the fold is the
        /// LONGER of the two — the beat sheet is 1.10 s of sheet time against a 0.90 s arc — and
        /// running it backwards puts the panel last, so the biggest member on the rig was still
        /// standing at 65&#176; when the pack reached the player's back and finished folding 0.2 s
        /// later, behind them, out of frame. What a player saw was a pack that flew home splayed
        /// open and never collected at all.
        /// </para>
        /// <para>
        /// So the fold goes first, on the sand, in front of them. That is the deploy's own order
        /// reversed rather than a second timeline, and the pause it was supposed to avoid does not
        /// exist: the pack is not sitting still during it, it is visibly folding — stakes up, the
        /// whole front flap (leaf, wings and rail as one piece) closing against the panel, panel
        /// down.
        /// </para>
        /// </summary>
        private IEnumerator RunStow()
        {
            Pack.SetOpen(false);

            while (Pack.IsSwinging) yield return null;

            // Exact, rather than trusting the last frame of the sheet to have landed on the nose,
            // and it is also what gives up the rack — see BackpackObject.SnapStowed. A pack stowed
            // from the rack and a pack stowed flat are the same pack from here on.
            Pack.SnapStowed();

            // Captured after the fold rather than before it: nothing has moved the pack, but the
            // pose the flight starts from should be the pose the flight actually starts from.
            //
            // The target is recomputed every frame instead of captured: re-shouldering is allowed
            // from across the map, and the player is usually walking while it flies back.
            yield return Fly(CurrentWorldPose(Pack), WornWorldPose, arcHeight, arcOutward);

            arcRoutine = null;
            SnapToWorn();
        }

        /// <summary>Drop whatever flight is in progress, without landing it. Safe to call twice.</summary>
        private void StopArc()
        {
            if (arcRoutine == null) return;

            StopCoroutine(arcRoutine);
            arcRoutine = null;
        }

        private void LogDeploy(Pose grounded)
        {
            if (!verboseDeploy) return;

            // The one number that settles "it deployed behind me": how far along the player's
            // own facing the pack ended up. Positive is in front. Reported as metres rather
            // than a normalised dot so the distance is legible in the same line.
            float ahead = Vector3.Dot(grounded.position - transform.position, transform.forward);
            Debug.Log($"Backpack deploy: {ahead:F2} m along the player's facing, " +
                      $"drop {grounded.position}.", this);
        }

        // ── Reaching into it ─────────────────────────────────────────────────────

        /// <summary>
        /// Somebody — this player or another one — wants what is in one of the pack's slots.
        ///
        /// <para>
        /// The request goes to the server and nothing happens locally, which is the same rule
        /// TraderInteraction follows and for the same reason: a pack is a container two people can
        /// reach into at once, and only one machine can be allowed to decide which of them got the
        /// last water cell. Doing the transfer optimistically here would hand it to both of them
        /// and then take it back from one.
        /// </para>
        /// <para>
        /// It rides THIS player's channel — the pack owner's — rather than the taker's, because the
        /// contested state is the pack's contents, not the taker's hotbar.
        /// </para>
        /// <para>
        /// <paramref name="hotbarSlot"/> names the slot a DRAG was let go over, and -1 — the
        /// default every existing caller gets — means "wherever it fits", which is the right-click
        /// behaviour <see cref="BackpackObject.TryTakeToHotbar"/> has always had. It travels in
        /// <see cref="NetArg.B"/>, which this message did not use, rather than as a fifth pack
        /// message: the transport, the channel, the contest and the idempotence are all identical,
        /// and the only new thing a drag says is <em>which box</em>.
        /// </para>
        /// </summary>
        public void RequestTake(PackSurfaceId surface, Vector2 uv, Interactor interactor,
                                int hotbarSlot = -1)
        {
            if (interactor == null) return;

            // The taker's BODY, not the camera rig their Interactor happens to sit on. Resolving it
            // the same way the messaging layer does means the id we mint and the object the server
            // resolves are the same thing.
            GameObject taker = NetChannel.RootOf(interactor);
            if (taker == null) return;

            // Positional, not an index into the pack's list. Two reasons, and the first is the one
            // that would bite: the list is rebuilt wholesale on every change, so a client's index N
            // and the server's index N are the same item only until somebody else touches the pack
            // — mid-reconcile they are not, and a take by index would hand over the wrong thing.
            // The second is that a point is what the player actually clicked, so the server is
            // answering the question that was asked.
            //
            // The uv rides P's X and Z. Y is the surface normal in every other pack calculation and
            // a uv has no height, so leaving it zero keeps the convention rather than inventing one.
            var arg = new NetArg
            {
                A = (int)surface,
                B = hotbarSlot,
                P = new Vector3(uv.x, 0f, uv.y),
            };

            this.NetToServer(NetMsg.PackTake, arg.With(taker));
        }

        /// <summary>Server side: hand over whatever is under that point, if it is still there.</summary>
        private void OnTakeRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this)) return;
            if (Pack == null || CurrentState != State.Open) return;

            if (!TryDecodeSurface(arg.A, out PackSurfaceId surface)) return;

            GameObject taker = arg.Resolve();
            if (taker == null) return;

            // GetComponentInChildren rather than GetComponent, so a body that keeps its hotbar on a
            // child still answers. The same lookup on the Interactor instead of the body is what
            // made this whole interaction silently do nothing before it was networked: on this
            // project's player the Interactor lives on the camera rig, and a plain GetComponent
            // there finds no inventory at all.
            var hotbar = taker.GetComponentInChildren<IPlayerInventory>(true);
            if (hotbar == null) return;

            var uv = new Vector2(arg.P.x, arg.P.z);

            // Idempotent by construction: the space is empty the second time, so nothing is found
            // under the point and TryTakeToHotbar answers false rather than conjuring a duplicate.
            // That is exactly the race two players grabbing the same item produce, and this is the
            // machine that settles it.
            if (arg.B < 0) Pack.TryTakeToHotbar(surface, uv, hotbar);
            else TakeIntoSlot(surface, uv, hotbar, arg.B);
        }

        /// <summary>
        /// The drag's version of a take: not "put it wherever it fits" but "put it in <em>this
        /// box</em>, and give me back whatever was in it". <b>Server side only.</b>
        ///
        /// <para>
        /// It lives here rather than on <see cref="BackpackObject"/> because everything it needs is
        /// already public on the pack — find, resolve, take out, put down — and because the
        /// question it answers is the wire message's, not the pack's.
        /// </para>
        /// <para>
        /// <b>The item comes off the pack before the displaced one goes down</b>, which is the
        /// opposite of <see cref="BackpackObject.TryStowFromHotbar"/>'s order and deliberate: the
        /// space the two items are contending for is the SAME space, and the displaced one is
        /// being offered exactly the rectangle the dragged one is vacating. Testing it first would
        /// mean re-deriving that "ignore this id" exception a second time; doing it in this order
        /// makes the question ordinary, and the rollback below is what pays for it.
        /// </para>
        /// <para>
        /// Idempotent like its neighbours: a second copy of the request finds nothing under the
        /// point and does nothing at all.
        /// </para>
        /// </summary>
        private bool TakeIntoSlot(PackSurfaceId surface, Vector2 uv, IPlayerInventory hotbar, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= hotbar.GetInventorySize()) return false;

            if (!Pack.TryFindAt(surface, uv, out PackPlacement grabbed)) return false;

            InventoryItem packItem = Pack.ItemFor(grabbed.ItemId);
            if (packItem == null || string.IsNullOrEmpty(packItem.ID)) return false;

            InventorySlot slot = hotbar.GetSlot(slotIndex);
            InventoryItem held = slot != null && !slot.IsEmpty ? slot.Item : null;

            // The same asset in both places. A hotbar holds items by reference and the pack's
            // layout is keyed by id, so the swap would be asked to put an id down that is on its
            // way up — refused by PackLayout.TryPlace, and rolled back below anyway, but refusing
            // here says what happened instead of taking the long way round to the same answer.
            if (held != null && held.ID == packItem.ID) return false;

            if (Pack.TakeOut(grabbed.ItemId) == null) return false;

            // Aimed at the vacated spot first, first-fit after — TryStowAt's own fallback, and the
            // right one: a displaced item that will not fit where the other one was lying should
            // still end up on the pack rather than blocking the drag.
            if (held != null && !Pack.TryStowAt(held, grabbed.Surface, grabbed.Uv, grabbed.Yaw))
            {
                // Nowhere for the displaced item to go. Put the dragged one back exactly where it
                // was and change nothing: every machine's pack, this one included, then agrees
                // with the display that never moved.
                Pack.TryPlace(packItem, grabbed.Surface, grabbed.Uv, grabbed.Yaw);
                return false;
            }

            WriteHotbarSlot(hotbar, slotIndex, packItem);
            return true;
        }

        /// <summary>
        /// Put <paramref name="item"/> in one named hotbar slot, leaving the others alone.
        ///
        /// <para>
        /// <see cref="IPlayerInventory.RestoreSlots"/> is the only positional write on that
        /// interface — <c>TryAddItem</c> fills the first hole, which is precisely what a drag onto
        /// a chosen slot must not do — so the slot is written by reading the hotbar out, changing
        /// one entry and handing the whole thing back. That is a heavier call than the job needs
        /// and it is the honest one: it is server-side, it happens once per gesture, and on
        /// <see cref="PlayerInventoryNetwork"/> it writes through the same NetworkList every other
        /// hotbar change travels on, so the owning client is corrected by the usual path.
        /// </para>
        /// <para>
        /// The selection is passed back through unchanged. RestoreSlots also sets it, and a drag
        /// that silently re-picked which item the player is holding would be a second, unasked-for
        /// consequence of dropping something into slot 3.
        /// </para>
        /// </summary>
        private static void WriteHotbarSlot(IPlayerInventory hotbar, int index, InventoryItem item)
        {
            int size = hotbar.GetInventorySize();
            var slots = new List<InventoryItem>(size);

            for (int i = 0; i < size; i++)
            {
                InventorySlot slot = hotbar.GetSlot(i);
                slots.Add(slot != null && !slot.IsEmpty ? slot.Item : null);
            }

            if (index < 0 || index >= slots.Count) return;

            slots[index] = item;

            hotbar.RestoreSlots(slots, hotbar.SelectedSlotIndex);
        }

        /// <summary>
        /// A surface id off the wire, refused unless the rig could actually have such a face.
        ///
        /// A surface id is a byte in the save and on the wire, so anything outside the enum is a
        /// malformed or stale request rather than a face this rig happens not to have.
        /// </summary>
        private static bool TryDecodeSurface(int raw, out PackSurfaceId surface)
        {
            surface = default;

            if (raw < 0 || raw > byte.MaxValue) return false;
            if (!System.Enum.IsDefined(typeof(PackSurfaceId), (byte)raw)) return false;

            surface = (PackSurfaceId)raw;
            return true;
        }

        /// <summary>
        /// Somebody has dragged an item to a new spot on the pack and let go. Ask the server.
        ///
        /// <para>
        /// Nothing happens locally, for the reason written out over <see cref="RequestTake"/>: a
        /// pack is a container two people can reach into at once. Free placement makes that sharper
        /// rather than softer — the space one player is dropping a canister into is space the other
        /// may have just filled — so the second request to arrive has to find it taken, and only
        /// one machine can be the one that notices.
        /// </para>
        /// <para>
        /// The item is named by where it was GRABBED rather than by its id, because a string will
        /// not fit in a <see cref="NetArg"/> and because the grab point is what the player actually
        /// clicked. See <see cref="NetMsg.PackMove"/> for the field layout.
        /// </para>
        /// </summary>
        public void RequestMove(PackSurfaceId from, Vector2 fromUv,
                                PackSurfaceId to, Vector2 toUv, float yaw)
        {
            var arg = new NetArg
            {
                A = (int)from | ((int)to << 8),
                B = Mathf.RoundToInt(Mathf.Repeat(yaw, 360f)),
                P = new Vector3(fromUv.x, 0f, fromUv.y),
                R = new Quaternion(toUv.x, 0f, toUv.y, 0f),
            };

            this.NetToServer(NetMsg.PackMove, arg);
        }

        /// <summary>Server side: slide it, if it is still there and the space is still free.</summary>
        private void OnMoveRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this)) return;
            if (Pack == null || CurrentState != State.Open) return;

            if (!TryDecodeSurface(arg.A & 0xFF, out PackSurfaceId from)) return;
            if (!TryDecodeSurface((arg.A >> 8) & 0xFF, out PackSurfaceId to)) return;

            // Resolved here rather than trusted from the sender. Between the grab and this message
            // another player may have taken the item, or filled the space it is headed for; both
            // come out as a quiet false and a pack that stays exactly as it was.
            if (!Pack.TryFindAt(from, new Vector2(arg.P.x, arg.P.z), out PackPlacement grabbed)) return;

            Pack.TryMove(grabbed.ItemId, to, new Vector2(arg.R.x, arg.R.z), arg.B);
        }

        /// <summary>
        /// Somebody has dragged an item clean off the mat and let go: it leaves the pack and lands
        /// on the ground (spec 5.1).
        ///
        /// <para>
        /// Same shape as <see cref="RequestTake"/> and <see cref="RequestMove"/>, and for the same
        /// reason. Two players can be in one pack; whether the thing you dragged off the edge was
        /// still there to drop is the server's to decide. This one has the sharper edge of the
        /// three, because the answer creates a world object — only the server may spawn, and a
        /// client doing it optimistically produces a pickup nobody else can see.
        /// </para>
        /// <para>
        /// The item is named by where it was GRABBED, positionally, for the reasons written out
        /// over <see cref="RequestTake"/>. No interactor is needed: unlike a take there is no
        /// second party's inventory involved, so the message carries only the point.
        /// </para>
        /// </summary>
        public void RequestDrop(PackSurfaceId from, Vector2 fromUv)
        {
            var arg = new NetArg { A = (int)from, P = new Vector3(fromUv.x, 0f, fromUv.y) };

            this.NetToServer(NetMsg.PackDrop, arg);
        }

        /// <summary>Server side: put it on the ground, if it is still on the pack.</summary>
        private void OnDropRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this)) return;
            if (Pack == null || CurrentState != State.Open) return;

            if (!TryDecodeSurface(arg.A, out PackSurfaceId surface)) return;

            // Dropped at the PACK, not at the player. The pack is what the player is looking at in
            // focus mode and may be several metres away by the time a remote request lands; an
            // item that left the mat should be beside the mat.
            Pack.TryDropToWorld(surface, new Vector2(arg.P.x, arg.P.z), Pack.transform);
        }

        /// <summary>
        /// The way IN, and the mirror of <see cref="RequestTake"/>: a hotbar key pressed while
        /// focused on this pack puts that slot's item onto it.
        ///
        /// <para>
        /// Same channel, same direction, same rule that nothing happens locally — two people can be
        /// reaching into one pack, so which of them gets the space under the cursor is the server's
        /// to decide, exactly as which of them gets the last water cell is.
        /// </para>
        /// <para>
        /// The hotbar slot travels as an INDEX where the other three messages are positional, and
        /// that difference is deliberate: a hotbar slot is a numbered box the player pressed a
        /// numbered key for, and it is not a thing anybody else is rearranging underneath them.
        /// See <see cref="NetMsg.PackStow"/> for the field layout.
        /// </para>
        /// </summary>
        public void RequestStow(int slotIndex, bool aimed, PackSurfaceId aimedSurface,
                                Vector2 aimedUv, Interactor interactor)
        {
            if (interactor == null) return;

            // The stower's BODY, resolved the way the messaging layer resolves it — see the note
            // in RequestTake, which this is the other half of.
            GameObject stower = NetChannel.RootOf(interactor);
            if (stower == null) return;

            var arg = new NetArg
            {
                A = slotIndex,
                B = aimed ? (int)aimedSurface : -1,
                P = new Vector3(aimedUv.x, 0f, aimedUv.y),
            };

            this.NetToServer(NetMsg.PackStow, arg.With(stower));
        }

        /// <summary>Server side: put it on the pack, if it is still in that slot and it fits.</summary>
        private void OnStowRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this)) return;
            if (Pack == null || CurrentState != State.Open) return;

            GameObject stower = arg.Resolve();
            if (stower == null) return;

            // GetComponentInChildren rather than GetComponent, for the reason written out in
            // OnTakeRequested: a body may keep its hotbar on a child.
            var hotbar = stower.GetComponentInChildren<IPlayerInventory>(true);
            if (hotbar == null) return;

            // B is -1 when the cursor was not over a usable spot, which asks for first-fit. An
            // out-of-range value that is not the sentinel is a malformed request, and degrading it
            // to first-fit would put gear somewhere the sender never asked for — so it is refused.
            bool aimed = arg.B >= 0;
            if (aimed && !TryDecodeSurface(arg.B, out _)) return;

            PackSurfaceId surface = aimed ? (PackSurfaceId)arg.B : default;

            // Idempotent the way the take is: the second request finds the slot already empty and
            // answers false rather than placing a second copy.
            Pack.TryStowFromHotbar(hotbar, arg.A, aimed, surface, new Vector2(arg.P.x, arg.P.z));
        }

        /// <summary>
        /// The flight itself and nothing else, so <see cref="RunStow"/> can put something in front
        /// of it. Split out rather than nested: <see cref="RunArc"/> clears
        /// <see cref="arcRoutine"/> on its way out, and a caller yielding on it would have its own
        /// handle torn up underneath it half way through.
        /// </summary>
        private IEnumerator Fly(Pose start, Func<Pose> end, float height, float outward)
        {
            for (float elapsed = 0f; elapsed < deploySeconds; elapsed += Time.deltaTime)
            {
                float t = Mathf.Clamp01(elapsed / deploySeconds);
                Pose pose = BackpackDeployArc.Evaluate(start, end(), t, height, outward);
                Pack.transform.SetPositionAndRotation(pose.position, pose.rotation);
                yield return null;
            }
        }

        private IEnumerator RunArc(Pose start, Func<Pose> end, float height, float outward,
                                   Action onArrive)
        {
            yield return Fly(start, end, height, outward);

            arcRoutine = null;
            onArrive();
        }

        private void FinishDeploy(Pose grounded)
        {
            hasPendingDeploy = false;

            Pack.transform.SetParent(null, true);
            Pack.transform.SetPositionAndRotation(grounded.position, grounded.rotation);
            Pack.SetWorn(false);
            CurrentState = State.Open;
            Pack.SetOpen(true);
        }

        private void SnapToWorn()
        {
            hasPendingDeploy = false;

            // SnapStowed, not SetOpen(false), and that is the whole of the "it goes back on my
            // back still folded open" bug. SetOpen answers a pack whose IsOpen is already false by
            // returning, so every path that arrives here with a fold already part-run — an
            // interrupted stow, a joiner told "shouldered", a save restore — used to park the rig
            // on somebody's back at whatever angle it had reached. This one is unconditional.
            Pack.SnapStowed();
            Pack.SetWorn(true);
            Pack.transform.SetParent(backSocket, false);
            Pack.transform.SetLocalPositionAndRotation(wornLocalPosition, Quaternion.Euler(wornLocalEuler));
            CurrentState = State.Shouldered;
        }

        private Pose WornWorldPose()
        {
            Quaternion rotation = backSocket.rotation * Quaternion.Euler(wornLocalEuler);
            return new Pose(backSocket.TransformPoint(wornLocalPosition), rotation);
        }

        private static Pose CurrentWorldPose(BackpackObject pack) =>
            new(pack.transform.position, pack.transform.rotation);

        /// <summary>
        /// The ground-plane direction the pack is set down along: the wearer's own facing.
        ///
        /// The BODY, deliberately not a camera. A camera is free to pitch, and on this branch to
        /// sit away from the body entirely, so "where the camera points" and "in front of the
        /// player" are no longer the same direction — and the pack must land in front of the
        /// PLAYER, where their feet point. The body's forward is also the one vector every
        /// machine's replica of this player actually has, which matters because the server is the
        /// one that runs the ground probe.
        /// </summary>
        private Vector3 DeployForward() => Flatten(transform.forward, Vector3.forward);

        /// <summary>Flatten to the ground plane, falling back when the horizontal part vanishes.</summary>
        private static Vector3 Flatten(Vector3 primary, Vector3 fallback)
        {
            primary.y = 0f;
            if (primary.sqrMagnitude > 1e-6f) return primary.normalized;

            fallback.y = 0f;
            return fallback.sqrMagnitude > 1e-6f ? fallback.normalized : Vector3.forward;
        }

        /// <summary>
        /// Where the pack lands. The probe has to ignore the player's own hierarchy: the capsule sits
        /// on the Default layer, so no layer mask excludes it, and a plain Physics.Raycast started
        /// above head height hits the player's own collider and drops the pack at chest level. The
        /// same trap is documented at length in Interactor.DoInteractionTest.
        /// </summary>
        private bool TryFindGroundPose(out Pose pose)
        {
            pose = default;

            Vector3 ahead = transform.position + DeployForward() * deployDistance;

            // Started above the player's eyeline, not 1 m up. On a rise or a step the ground in front
            // can sit higher than the player's own feet, and a short probe silently finds nothing —
            // which reads to the player as "the key does nothing" while the pack stays on their back.
            Vector3 origin = ahead + Vector3.up * 2f;

            int count = Physics.RaycastNonAlloc(new Ray(origin, Vector3.down), groundHits,
                                                6f, groundMask, QueryTriggerInteraction.Ignore);
            if (count == 0) return false;

            Transform root = transform.root;
            var best = new RaycastHit();
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                Transform hit = groundHits[i].transform;
                if (hit == null) continue;
                if (hit.IsChildOf(root)) continue;                                  // the player
                if (Pack != null && hit.IsChildOf(Pack.transform)) continue;        // the pack itself

                if (!found || groundHits[i].distance < best.distance)
                {
                    best = groundHits[i];
                    found = true;
                }
            }

            if (!found) return false;

            // The pack STANDS UP where it lands, its opening toward the player. Local +Y is the
            // height axis; WHICH horizontal side the player should be shown is settled by what the
            // focus camera actually framed in play, not by what the axes are named — local +Z at
            // the player showed them the closed back of a cabinet, so the side the rig presents is
            // on -Z and local +Z points AWAY from the player. If the model is ever re-authored and
            // the pack lands backwards again, this sign is the whole of the fix.
            Vector3 awayFromPlayer = Vector3.ProjectOnPlane(ahead - transform.position, best.normal);
            if (awayFromPlayer.sqrMagnitude < 1e-6f)
                awayFromPlayer = Vector3.ProjectOnPlane(DeployForward(), best.normal);

            pose = new Pose(best.point + best.normal * groundLift,
                            Quaternion.LookRotation(awayFromPlayer.normalized, best.normal));
            return true;
        }

#if UNITY_EDITOR
        /// <summary>
        /// The drop direction, visible without pressing Play: the body's facing, and the sphere
        /// where the pack would be set down.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            Vector3 eye = transform.position + Vector3.up * 1.2f;

            Gizmos.color = Color.green;
            Gizmos.DrawRay(eye, DeployForward() * deployDistance);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position + DeployForward() * deployDistance, 0.25f);
        }
#endif
    }
}
