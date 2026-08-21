using System;
using System.Collections;
using UnityEngine;
using SpaceGame.Characters;
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
        [Tooltip("What the drop point is measured from. Left empty it resolves through the project's " +
                 "own aim source — PlayerController's camera, then AimProvider's — because the pack " +
                 "must land where the player is LOOKING, and it must agree with what every other " +
                 "system in the game calls 'forward'.")]
        [SerializeField] private Transform aimTransform;

        [SerializeField, Min(0.05f)] private float deploySeconds = 0.9f;

        [Tooltip("Metres in front of the player the pack is set down. Measured to the pack's ORIGIN, " +
                 "which is the bottom centre of its footprint — so half its depth is nearer than " +
                 "this number, and a value near the pack's own depth puts it against your chest.")]
        [SerializeField, Min(0.2f)] private float deployDistance = 1.6f;
        [SerializeField] private float arcHeight = 0.6f;
        [Tooltip("Metres the pack bows sideways mid-flight so it clears the player's own body.")]
        [SerializeField] private float arcOutward = 0.35f;

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

            if (aimTransform == null) aimTransform = ResolveAimTransform();

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
        }

        /// <summary>
        /// The transform the drop direction is measured from, resolved through the project's OWN
        /// aim source rather than by hunting for a camera.
        ///
        /// This is the fix for a bug that kept coming back: the pack landed behind the player.
        /// The old resolve was `GetComponentInChildren&lt;Camera&gt;(true)`, which takes the first
        /// camera in hierarchy order INCLUDING INACTIVE ONES and never checks that it is the camera
        /// the player is looking through. PlayerCharacter.prefab carries its Main Camera as a
        /// deactivated nested prefab that PlayerController switches on at spawn, so what that
        /// search returned had no relationship to where anyone was aiming.
        ///
        /// Order: PlayerController's camera (what the player sees through) → AimProvider's (what
        /// every weapon aims along) → the body. The body is last and can never be null, so this
        /// always returns something usable.
        /// </summary>
        private Transform ResolveAimTransform()
        {
            var player = GetComponent<PlayerController>();
            if (player != null && player.PlayerCameraTransform != null)
                return player.PlayerCameraTransform;

            var aim = GetComponent<AimProvider>();
            if (aim != null && aim.AimTransform != null)
                return aim.AimTransform;

            return transform;
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

            // A coroutine dies with the component. Without this the pack is left hanging in mid-air,
            // unparented, halfway through an arc — which survives a scene reload as a floating pack.
            if (arcRoutine != null)
            {
                StopCoroutine(arcRoutine);
                arcRoutine = null;

                // The pack lands where it was GOING, never where it had got to.
                //
                // This is the "it deploys behind me" bug, and it is why fixing the drop direction
                // never made it go away. The arc starts on the player's back, so a deploy
                // interrupted in its first frames leaves the pack a few centimetres behind them —
                // unparented, IsWorn false, state Open. Everything reports a completed deploy and
                // the pack is behind the player, every time.
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

            Pose start = CurrentWorldPose(Pack);

            pendingDeployPose = grounded;
            hasPendingDeploy = true;

            CurrentState = State.Deploying;
            Pack.SetWorn(false);
            Pack.transform.SetParent(null, true);

            arcRoutine = StartCoroutine(RunArc(start, () => grounded, () => FinishDeploy(grounded)));
        }

        private void StartReshoulder()
        {
            StopArc();

            Pose start = CurrentWorldPose(Pack);

            CurrentState = State.Stowing;

            // Closes over the first third of the flight rather than before it. Waiting for the lid
            // would put a visible pause between the interaction and the pack moving.
            Pack.SetOpen(false);

            // The target is recomputed every frame instead of captured: re-shouldering is allowed
            // from across the map, and the player is usually walking while it flies back.
            arcRoutine = StartCoroutine(RunArc(start, WornWorldPose, SnapToWorn));
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
            Debug.Log($"Backpack deploy: {ahead:F2} m along the player's facing " +
                      $"(aim '{(aimTransform != null ? aimTransform.name : "none")}'), " +
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
        /// </summary>
        public void RequestTake(BackpackCompartment compartment, int index, Interactor interactor)
        {
            if (interactor == null) return;

            // The taker's BODY, not the camera rig their Interactor happens to sit on. Resolving it
            // the same way the messaging layer does means the id we mint and the object the server
            // resolves are the same thing.
            GameObject taker = NetChannel.RootOf(interactor);
            if (taker == null) return;

            var arg = new NetArg { A = (int)compartment, B = index };
            this.NetToServer(NetMsg.PackTake, arg.With(taker));
        }

        /// <summary>Server side: hand over what is in the slot, if it is still there.</summary>
        private void OnTakeRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this)) return;
            if (Pack == null || CurrentState != State.Open) return;

            if (arg.A != (int)BackpackCompartment.Strap && arg.A != (int)BackpackCompartment.Main) return;

            GameObject taker = arg.Resolve();
            if (taker == null) return;

            // GetComponentInChildren rather than GetComponent, so a body that keeps its hotbar on a
            // child still answers. The same lookup on the Interactor instead of the body is what
            // made this whole interaction silently do nothing before it was networked: on this
            // project's player the Interactor lives on the camera rig, and a plain GetComponent
            // there finds no inventory at all.
            var hotbar = taker.GetComponentInChildren<IPlayerInventory>(true);
            if (hotbar == null) return;

            // Idempotent by construction: the slot is empty the second time, and TryTakeToHotbar
            // answers false rather than conjuring a duplicate. That is exactly the race two players
            // grabbing the same item produce, and this is the machine that settles it.
            Pack.TryTakeToHotbar((BackpackCompartment)arg.A, arg.B, hotbar);
        }

        private IEnumerator RunArc(Pose start, Func<Pose> end, Action onArrive)
        {
            for (float elapsed = 0f; elapsed < deploySeconds; elapsed += Time.deltaTime)
            {
                float t = Mathf.Clamp01(elapsed / deploySeconds);
                Pose pose = BackpackDeployArc.Evaluate(start, end(), t, arcHeight, arcOutward);
                Pack.transform.SetPositionAndRotation(pose.position, pose.rotation);
                yield return null;
            }

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

            Pack.SetOpen(false);
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
        /// Where the player is looking, flattened to the ground plane. Falls back to the body's
        /// facing when the view is straight up or down, where the horizontal component vanishes.
        /// </summary>
        private Vector3 AimForward()
        {
            Vector3 aim = aimTransform != null ? aimTransform.forward : transform.forward;
            Vector3 forward = DeployDirection(aim, transform.forward, out bool inverted);

            if (inverted)
                Debug.LogError(
                    $"BackpackController: aim source '{(aimTransform != null ? aimTransform.name : "none")}' " +
                    "points opposite the body, so the pack would have been set down BEHIND the " +
                    "player. Deploying along the body instead — assign aimTransform to the camera " +
                    "the player actually looks through.", this);

            return forward;
        }

        /// <summary>
        /// The ground-plane direction the pack is set down along, given where the player is looking
        /// and which way their body faces. Pure, so the guard below can be tested without a scene.
        ///
        /// `inverted` reports that the aim disagreed with the body and was overridden. In a
        /// first-person rig those two cannot legitimately disagree — PlayerLook yaws the body's
        /// Rigidbody and writes Euler(pitch, 0, 0) into the camera's LOCAL rotation, so the camera
        /// contributes pitch and nothing else. A negative dot therefore never means "the player is
        /// looking backwards"; it means the aim source is not the camera they are looking through,
        /// which is exactly how the pack kept ending up behind them.
        ///
        /// The test is `&lt; 0` rather than a tight tolerance on purpose: any disagreement short of
        /// an actual inversion still puts the pack in front, and a future third-person camera that
        /// legitimately trails the body should not trip this.
        /// </summary>
        public static Vector3 DeployDirection(Vector3 aimForward, Vector3 bodyForward, out bool inverted)
        {
            Vector3 body = Flatten(bodyForward, Vector3.forward);
            Vector3 forward = Flatten(aimForward, body);

            inverted = Vector3.Dot(forward, body) < 0f;
            return inverted ? body : forward;
        }

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

            Vector3 ahead = transform.position + AimForward() * deployDistance;

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

            // The pack STANDS UP where it lands, doors toward the player. Its local +Y is the height
            // axis and its local +Z is the door side — the frame that rides against the wearer's
            // back is on -Z — so pointing local +Z at the player is what puts the opening interior
            // in front of them rather than showing them the back of a cabinet.
            Vector3 toPlayer = Vector3.ProjectOnPlane(transform.position - ahead, best.normal);
            if (toPlayer.sqrMagnitude < 1e-6f)
                toPlayer = Vector3.ProjectOnPlane(-AimForward(), best.normal);

            pose = new Pose(best.point + best.normal * groundLift,
                            Quaternion.LookRotation(toPlayer.normalized, best.normal));
            return true;
        }

#if UNITY_EDITOR
        /// <summary>
        /// The drop direction, visible without pressing Play. Green is the body's facing, cyan the
        /// resolved aim, and the sphere is where the pack would be set down — so an aim source that
        /// disagrees with the body shows up as two lines pointing opposite ways in the Scene view
        /// rather than as a pack found behind you ten minutes into a playtest.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            Transform aim = aimTransform != null ? aimTransform : ResolveAimTransform();
            Vector3 eye = transform.position + Vector3.up * 1.2f;

            Gizmos.color = Color.green;
            Gizmos.DrawRay(eye, transform.forward * deployDistance);

            if (aim != null && aim != transform)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(eye, DeployDirection(aim.forward, transform.forward, out _) * deployDistance);
            }

            Vector3 drop = transform.position +
                           DeployDirection(aim != null ? aim.forward : transform.forward,
                                           transform.forward, out _) * deployDistance;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(drop, 0.25f);
        }
#endif
    }
}
