using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Core
{
    /// <summary>
    /// Drop-in scene transition orchestrator.
    ///
    /// Place this on any GameObject that should send an initiator (player or AI agent) to
    /// another scene. It does no triggering of its own — pair it with a trigger component:
    ///
    ///   • InteractableTrigger — fires when the player interacts with this object.
    ///   • VolumeTrigger       — fires when a player or agent enters a trigger volume.
    ///   • From script         — call <see cref="Trigger"/> directly.
    ///
    /// What it does, in order:
    ///   1. Plays all assigned effects in parallel (fade, audio muffle, camera shake, ...).
    ///   2. Asks the destination to apply itself (additive load + place initiator at anchor).
    ///   3. Tells the effects the load is done; waits for their "in" phase to finish.
    ///
    /// Effects must use different <see cref="TransitionChannel"/>s — two effects on the same
    /// channel will fight each other. The inspector warns at edit-time if this happens.
    ///
    /// The transition is reentry-guarded by an internal busy flag, so multiple triggers
    /// firing on the same frame all call back into the same single transition safely.
    ///
    /// NETCODE — the transition is split down one line, and the line is "whose eyes".
    ///
    /// The authoritative half (deciding that it fires, and <c>destination.Apply</c>, which loads a
    /// scene and moves a body) is the server's, because it is shared state. The effects half — the
    /// fade, the audio muffle, the walk-through cutscene — belongs to exactly ONE pair of eyes: the
    /// machine that owns the initiator. Both halves have been on the wrong machine at some point:
    ///
    ///   • Before <see cref="VolumeTrigger"/> was server-gated, every peer ran the whole transition
    ///     for every initiator, because every player's body exists on every machine. The host's
    ///     screen faded to black because somebody ELSE walked through a door.
    ///   • With the gate and nothing else, the effects ran wherever the decision was made — so a
    ///     client walking into a volume got no fade, no muffle and no cutscene, while the host got
    ///     all three for a transition that was not happening to them.
    ///
    /// So when the deciding machine is not the one watching, the effects are asked for over the
    /// wire (<see cref="NetMsg.SceneEffects"/>) and the owner reports its out phase finished
    /// (<see cref="NetMsg.SceneEffectsDone"/>) — the ack exists because a walk-through cutscene is
    /// allowed to hold up the teleport, and the server cannot see a client's cutscene end. The
    /// receiving half lives in <see cref="SceneTransitionViewer"/>.
    ///
    /// Offline, and for the host's own player, none of that runs: the audience is "this machine"
    /// and the code path is the pre-netcode one, verbatim.
    /// </summary>
    [AddComponentMenu("Scene Management/Scene Transition")]
    public class SceneTransition : MonoBehaviour, ITriggerable
    {
        [TextArea(6, 12)]
        [SerializeField] private string description =
            "Drop-in scene transition.\n" +
            "• Destination: which scene + spawn anchor (ScriptableObject).\n" +
            "• Effects: visual/audio effects that play during the load. Multiple allowed,\n" +
            "  but each must use a different TransitionChannel (Screen/Audio/Camera/Time).\n" +
            "• Pair with an InteractableTrigger or VolumeTrigger on the same GameObject,\n" +
            "  or call Trigger(initiator) from script, to fire the transition.\n" +
            "• Effects play during load. When the load finishes, the 'in' phase of each\n" +
            "  effect runs and the transition completes.\n" +
            "• Effects play on the INITIATOR's machine only — see the netcode note on the class.\n" +
            "• Spacebar skips effects (skip is ignored until the load completes).";

        [Header("Configuration")]
        [SerializeField] private SceneDestination destination;
        [SerializeField] private SceneTransitionEffect[] effects;

        [Tooltip("After this transition fires for an initiator, that initiator cannot be moved by ANY " +
                 "SceneTransition for this many seconds. Prevents an exit volume from re-firing on the " +
                 "spawn frame inside the destination, or an entrance from re-firing as the player walks back out.")]
        [SerializeField] private float postTransitionLockoutSeconds = 1f;

        // Initiator → unscaled-time when the post-transition lockout ends.
        // Static so the gate is shared across every SceneTransition in every scene
        // (entrance and exit are different components on different GameObjects).
        //
        // Keyed by GetInstanceID(), which is a handle into THIS process — and that is still exactly
        // right, because the map is now only ever written and read on one machine: the one that
        // decided the transition. VolumeTrigger only asks on the server, and InteractableTrigger
        // only ever asks for a body its own machine owns, so a lockout armed here is always
        // consulted by the same machine that armed it. What changed is that clients no longer arm
        // entries for players they will never fire for — they never reach CanTrigger at all.
        private static readonly Dictionary<int, float> s_lockoutUntil = new();

        /// <summary>
        /// Every enabled transition on this machine, by <see cref="TransitionId"/>, so that a
        /// machine handed only an id in a message can find its own copy of the door.
        /// </summary>
        private static readonly Dictionary<int, SceneTransition> s_byId = new();

        private bool busy;
        private GameObject lastInitiator;
        private float busySetAt;
        private int transitionId;
        private bool warnedInvalidDestination;

        private const float BusyMaxSeconds = 20f;

        /// <summary>
        /// How long the server will wait for a remote owner to say its out phase finished.
        ///
        /// Deliberately well under <see cref="BusyMaxSeconds"/>. A client that drops mid-fade never
        /// sends the ack, and if this wait outlived the busy flag's self-heal the volume would
        /// re-arm while destination.Apply was still running — the self-heal is the last line of
        /// defence, not the mechanism. Generous enough for a walk-through cutscene, which is the
        /// only effect that legitimately holds up the load.
        /// </summary>
        private const float RemoteOutPhaseMaxSeconds = 8f;

        /// <summary>
        /// Statics survive leaving play mode when domain reload is off, and both maps above would
        /// then start the next session holding destroyed components and stale lockouts.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_lockoutUntil.Clear();
            s_byId.Clear();
        }

        private void OnEnable()
        {
            int id = TransitionId;
            if (s_byId.TryGetValue(id, out SceneTransition existing) && existing != null && existing != this)
            {
                Debug.LogError(
                    $"[SceneTransition] '{name}' and '{existing.name}' hash to the same transition id " +
                    $"({id}). One of them will get the other's effects on a remote client. Rename one " +
                    "of the GameObjects.", this);
                return;
            }
            s_byId[id] = this;
        }

        private void OnDisable()
        {
            int id = TransitionId;
            if (s_byId.TryGetValue(id, out SceneTransition registered) && registered == this)
                s_byId.Remove(id);
        }

        private void Update()
        {
            // Self-heal: if busy has been stuck longer than any plausible transition,
            // something didn't clean up (host destroyed, coroutine cancelled, scene
            // unload mid-flight). Force-clear so the volume can fire again next time.
            if (busy && Time.unscaledTime - busySetAt > BusyMaxSeconds)
            {
                Debug.LogWarning(
                    $"[SceneTransition] '{name}' busy stuck for {Time.unscaledTime - busySetAt:0.0}s — " +
                    $"force-clearing. Initiator was {(lastInitiator != null ? lastInitiator.name : "<null>")}.",
                    this);
                busy = false;
                lastInitiator = null;
            }
        }

        public bool IsBusy => busy;
        public SceneDestination Destination => destination;

        /// <summary>
        /// The GameObject that fired the currently-running transition (the player or AI agent).
        /// Effects that need to know who's being transported read this on Begin(). Null between
        /// transitions.
        /// </summary>
        public GameObject LastInitiator => lastInitiator;

        /// <summary>
        /// A machine-independent name for this transition.
        ///
        /// <see cref="Object.GetInstanceID"/> cannot serve: it is a handle into one process, and
        /// the whole point of this id is to cross processes — the server names a transition in
        /// <see cref="NetMsg.SceneEffects"/> and the initiator's own machine has to find ITS copy
        /// of the same door, so it can play that door's authored effects (including the
        /// per-door <c>Cutscene</c> component a WalkThroughCutsceneEffect reads off this
        /// GameObject). The scene name plus the hierarchy path is stable because both machines
        /// deserialize the same scene file. It is the same trick Netcode uses to identify a scene,
        /// and it is why chunk scenes must keep unique names.
        /// </summary>
        public int TransitionId
        {
            get
            {
                if (transitionId == 0) transitionId = ComputeTransitionId();
                return transitionId;
            }
        }

        /// <summary>This machine's copy of the transition with that id, or null if it has none.</summary>
        public static SceneTransition FindById(int id) =>
            s_byId.TryGetValue(id, out SceneTransition found) && found != null ? found : null;

        public bool CanTrigger(GameObject initiator)
        {
            if (busy) return false;
            if (initiator == null) return false;
            if (destination == null || !destination.IsValid())
            {
                WarnInvalidDestinationOnce();
                return false;
            }
            if (IsLockedOut(initiator)) return false;
            return true;
        }

        /// <summary>
        /// Once per component, not once per frame: a misconfigured door sitting inside a volume is
        /// asked this from OnTriggerStay, so an unthrottled log buries the console. Same shape and
        /// reason as NetChannel.WarnUnrelayed.
        /// </summary>
        private void WarnInvalidDestinationOnce()
        {
            if (warnedInvalidDestination) return;
            warnedInvalidDestination = true;

            Debug.LogWarning(
                $"[SceneTransition] '{name}' has no usable destination " +
                $"({(destination == null ? "none assigned" : $"'{destination.name}'.IsValid() is false")}), " +
                "so it will never fire. Assign one, or check the scene/anchor it points at exists.", this);
        }

        private static bool IsLockedOut(GameObject initiator)
        {
            int key = initiator.GetInstanceID();
            if (!s_lockoutUntil.TryGetValue(key, out var until)) return false;
            if (Time.unscaledTime >= until)
            {
                s_lockoutUntil.Remove(key);
                return false;
            }
            return true;
        }

        /// <summary>Fire the transition for the given initiator. Returns null if not eligible.</summary>
        public Coroutine Trigger(GameObject initiator)
        {
            if (!CanTrigger(initiator)) return null;

            busy = true;
            busySetAt = Time.unscaledTime;
            lastInitiator = initiator;

            // Run on TransitionRunner (DontDestroyOnLoad). The host GameObject may be
            // inside a scene that the destination unloads — if the coroutine ran on us,
            // it would die mid-transition and effects would never receive End().
            return TransitionRunner.Instance.Run(Run(initiator));
        }

        // ─────────── Who the effects are for ───────────

        /// <summary>Whose screen this transition's effects belong on.</summary>
        private enum EffectAudience
        {
            /// <summary>This machine is the one looking through the initiator's eyes.</summary>
            ThisMachine,

            /// <summary>Another machine owns the initiator; ask it over the wire and wait for its ack.</summary>
            RemoteOwner,

            /// <summary>Nobody is looking through these eyes. No effects, and nothing to wait for.</summary>
            Nobody,
        }

        private static EffectAudience AudienceFor(GameObject initiator)
        {
            if (initiator == null) return EffectAudience.Nobody;

            // Offline there is one machine, one screen and one set of eyes, so this is the
            // pre-netcode behaviour verbatim — including for an AI initiator, which is what
            // single-player has always done. Nothing below this line may change solo play.
            if (!Network.IsNetworked) return EffectAudience.ThisMachine;

            NetworkObject netObj = initiator.GetComponentInParent<NetworkObject>();

            // IsPlayerObject rather than the "Player" tag: it is Netcode's own record of "a human
            // client is attached to this object", which is precisely the question being asked, and
            // it is false for every AI agent however that agent is tagged or spawned. An agent has
            // no eyes — fading a screen for one is the original bug wearing a different hat, and
            // it is worse than it sounds because CutsceneDirector falls back to the LOCAL player
            // when its subject has no PlayerController, so an NPC's walk-through cutscene would
            // seize control of whoever happened to be hosting.
            if (netObj == null || !netObj.IsSpawned || !netObj.IsPlayerObject)
                return EffectAudience.Nobody;

            // The host's own player, and a client that pressed E on a door with an
            // InteractableTrigger: the machine deciding IS the machine watching. Same first branch
            // as PlayerInteriorTransit.NotifyViewer, and for the same reason — the commonest case
            // in the game must not take a different code path from single-player.
            if (netObj.IsOwner) return EffectAudience.ThisMachine;

            // Reaching somebody else's machine means a broadcast, and broadcasts are the server's
            // alone (NetRelay refuses one from a client). A client firing a transition for a body
            // it does not own has no way to tell that body's owner, so it runs the authoritative
            // half and shows nobody anything rather than logging a refused send every time.
            if (!Network.Server)
            {
                Debug.LogWarning(
                    $"[SceneTransition] A client fired a transition for '{initiator.name}', which it " +
                    "does not own. Only the server can reach that player's machine, so the effects " +
                    "are skipped. Fire transitions for other players from the server.");
                return EffectAudience.Nobody;
            }

            // No relay means the send would fall through to a local dispatch on the server, which
            // no owner would ever answer — so the wait below would burn its full timeout for
            // nothing. Skipping is the honest answer, and the warning names the missing piece.
            NetRelay relay = NetRelay.Find(netObj.transform);
            if (relay == null || !relay.CanSend)
            {
                Debug.LogWarning(
                    $"[SceneTransition] '{initiator.name}' has no usable NetRelay, so its owner " +
                    "cannot be told to play the transition's effects. Add a NetRelay beside the " +
                    "player prefab's NetworkObject.", initiator);
                return EffectAudience.Nobody;
            }

            return EffectAudience.RemoteOwner;
        }

        // ─────────── The run ───────────

        private IEnumerator Run(GameObject initiator)
        {
            // Cached because `this` is routinely destroyed partway through: a transition inside an
            // interior is unloaded by its own destination, and reading TransitionId (or anything
            // else on the component) after that throws.
            int id = TransitionId;

            EffectAudience audience = AudienceFor(initiator);
            SceneTransitionViewer viewer = null;
            List<EffectHandle> handles = null;

            switch (audience)
            {
                case EffectAudience.ThisMachine:
                    handles = BeginEffects(initiator);
                    break;

                case EffectAudience.RemoteOwner:
                    // Ensured before the send, so the server has somewhere for the owner's ack to
                    // land even if nothing else has touched this player's channel yet.
                    viewer = SceneTransitionViewer.Ensure(initiator);
                    SendPhase(initiator, id, SceneEffectPhase.Out);
                    break;
            }

            // Arm the cross-transition lockout BEFORE the destination runs. The destination
            // can teleport the initiator into another SceneTransition's volume (e.g. an exit
            // teleporting the player back into the entrance volume); without an early lockout,
            // that volume's OnTriggerEnter fires on the same frame and re-fires the entrance
            // before we've had a chance to clear our own busy. Refresh the lockout again
            // after Apply so the full duration always extends past landing.
            ArmLockout(initiator);

            // Wait for anything that wants to block the load (a walk-through cutscene that must
            // play before the teleport). Out phases run in parallel — each is yielded in turn, so
            // the total wait is the slowest.
            switch (audience)
            {
                case EffectAudience.ThisMachine:
                    foreach (var h in handles) yield return h.AwaitOutPhase();
                    break;

                case EffectAudience.RemoteOwner:
                    yield return AwaitRemoteOutPhase(viewer, id, name);
                    break;

                // Nobody: an AI initiator has no effects to run and no owner to hear from. Falling
                // straight through is what keeps it from wedging on an ack that can never arrive.
            }

            yield return destination.Apply(initiator);

            ArmLockout(initiator);

            // Clear busy as soon as the destination has landed — the remaining work
            // (in-phase fades) is cosmetic and must not gate the next trigger. If we
            // wait until AwaitCompletion and that hangs or throws, busy never clears
            // and the volume becomes permanently dead (the original bug).
            if (this != null)
            {
                busy = false;
                lastInitiator = null;
            }

            if (audience == EffectAudience.RemoteOwner)
            {
                // Fire-and-forget: the in phase is cosmetic, it happens on somebody else's screen,
                // and there is no second ack to wait for. A client that has dropped simply never
                // hears it, which costs this session nothing.
                SendPhase(initiator, id, SceneEffectPhase.In);
            }
            else if (handles != null)
            {
                yield return EndEffects(handles);
            }
        }

        private void ArmLockout(GameObject initiator)
        {
            if (initiator == null || postTransitionLockoutSeconds <= 0f) return;
            s_lockoutUntil[initiator.GetInstanceID()] = Time.unscaledTime + postTransitionLockoutSeconds;
        }

        /// <summary>
        /// Ask the initiator's owner to run one phase of this transition's effects.
        ///
        /// Broadcast on the INITIATOR's channel and filtered by ownership on arrival, because this
        /// layer has no unicast — the same shape NetMsg.RopeTug and NetMsg.Damaged use. Static
        /// because the in phase is sent after destination.Apply, by which point this component may
        /// already have been destroyed along with the scene it was standing in.
        /// </summary>
        private static void SendPhase(GameObject initiator, int transitionId, int phase)
        {
            if (initiator == null) return;

            NetMessaging.NetSendTo(initiator, NetMsg.SceneEffects,
                                   new NetArg { A = phase, B = transitionId }, NetTo.All);
        }

        /// <summary>
        /// Wait for the owner to report its out phase finished — but only for so long.
        ///
        /// The wait is real: a walk-through cutscene is supposed to finish walking the player
        /// through the door before the teleport, and only their machine knows when it has. The cap
        /// is equally real: a client that drops mid-fade sends nothing, and without a deadline this
        /// transition would sit here forever holding its busy flag.
        /// </summary>
        private static IEnumerator AwaitRemoteOutPhase(SceneTransitionViewer viewer, int transitionId,
                                                       string label)
        {
            float deadline = Time.unscaledTime + RemoteOutPhaseMaxSeconds;

            while (viewer != null && !viewer.TakeOutPhaseAck(transitionId))
            {
                if (Time.unscaledTime >= deadline)
                {
                    Debug.LogWarning(
                        $"[SceneTransition] '{label}' waited {RemoteOutPhaseMaxSeconds:0.0}s for the " +
                        "initiator's machine to finish its effects and heard nothing — continuing " +
                        "without it. The player most likely dropped mid-transition.");
                    yield break;
                }
                yield return null;
            }

            // viewer == null means the player despawned while we waited. Falling out is right:
            // there is nobody left to run an in phase for, and the destination still has to run so
            // the session's own record of where that body lives stays consistent.
        }

        // ─────────── Effects, played by whichever machine is watching ───────────

        /// <summary>
        /// Start every effect's out phase ON THIS MACHINE and hand back their handles.
        ///
        /// Public because the machine that plays a transition's effects is not always the machine
        /// that decided it: <see cref="SceneTransitionViewer"/> calls this on the initiator's owner
        /// when the two differ. One implementation for both, so a fade cannot drift into behaving
        /// differently for the host than for everybody else.
        /// </summary>
        public List<EffectHandle> BeginEffects(GameObject initiator)
        {
            // Set before the first Begin: effects read the subject off the host —
            // WalkThroughCutsceneEffect resolves its cutscene subject from LastInitiator — and on
            // a remote owner's machine nothing else has filled this in.
            lastInitiator = initiator;

            var handles = new List<EffectHandle>();
            if (effects == null) return handles;

            foreach (var e in effects)
            {
                if (e == null) continue;
                EffectHandle handle = e.Begin(this);
                if (handle != null) handles.Add(handle);
            }
            return handles;
        }

        /// <summary>
        /// Run the in phase for handles produced by <see cref="BeginEffects"/> and wait it out.
        /// </summary>
        public IEnumerator EndEffects(List<EffectHandle> handles)
        {
            // `this` may already be destroyed — the destination routinely unloads the scene holding
            // the door — so the only member touched here is guarded by Unity's own lifetime check.
            // The handles themselves survive because effects run on DontDestroyOnLoad hosts.
            if (this != null) lastInitiator = null;

            if (handles == null) yield break;

            foreach (var h in handles) h?.End();
            foreach (var h in handles)
            {
                if (h != null) yield return h.AwaitCompletion();
            }
        }

        // ─────────── Identity ───────────

        private int ComputeTransitionId()
        {
            var path = new StringBuilder(gameObject.scene.name);
            AppendPath(transform, path);
            return StableHash(path.ToString());
        }

        private static void AppendPath(Transform t, StringBuilder sb)
        {
            if (t.parent != null) AppendPath(t.parent, sb);

            // The sibling index is in there so two identically-named doors under one parent — the
            // normal result of duplicating a prefab instance in a scene — still get different ids.
            sb.Append('/').Append(t.name).Append('[').Append(t.GetSiblingIndex()).Append(']');
        }

        /// <summary>
        /// FNV-1a, written out rather than calling <c>string.GetHashCode</c>.
        ///
        /// .NET randomises string hash seeds per process, so the built-in hash is a different
        /// number on every machine — which is exactly the property this id must not have.
        /// </summary>
        private static int StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char c in value)
                {
                    hash ^= (uint)c;
                    hash *= 16777619u;
                }

                // 0 is the "no transition" sentinel in the message and in the lazy cache above.
                int id = (int)hash;
                return id == 0 ? 1 : id;
            }
        }

    #if UNITY_EDITOR
        private void OnValidate()
        {
            if (effects == null) return;
            var seen = new HashSet<TransitionChannel>();
            foreach (var e in effects)
            {
                if (e == null) continue;
                if (e.Channel == TransitionChannel.Custom) continue;
                if (!seen.Add(e.Channel))
                {
                    Debug.LogWarning(
                        $"[SceneTransition] Two effects share channel '{e.Channel}' on '{name}'. " +
                        "They will collide — give one a different channel or remove the duplicate.",
                        this);
                }
            }
        }
    #endif
    }
}
