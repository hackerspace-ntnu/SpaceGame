// The half of a scene transition that belongs to one pair of eyes.
//
// A transition is two things at once. Loading a scene and moving a body is shared state and the
// server's to decide. Fading the screen, muffling the audio and walking the player through the door
// is something that happens to ONE person — and every version of this code so far has got that
// second half onto the wrong machine. First every peer ran it for every initiator (the host's
// screen going black because somebody else entered a cave), then, once VolumeTrigger was
// server-gated, the server ran it for everybody (a client walking into a volume seeing nothing at
// all while the host got the fade).
//
// So the server sends NetMsg.SceneEffects on the INITIATOR's channel and this component, sitting on
// the player, decides whether it is the one being talked to. Broadcast-and-filter rather than
// unicast because the messaging layer has no unicast; ownership is the filter, exactly as
// RopeTugReceiver does for a rope's pull.
//
// The ack going the other way (NetMsg.SceneEffectsDone) is not a nicety: EffectHandle.AwaitOutPhase
// is allowed to block the load so a walk-through cutscene finishes before the teleport, and the
// server has no way to watch a client's cutscene end.
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Core
{
    /// <summary>
    /// Plays scene-transition effects for the player this sits on, when the server asks.
    ///
    /// Added on demand rather than authored on the player prefab — same reason and same shape as
    /// <c>RopeTugReceiver.Ensure</c> and NetChannel's own: it needs to exist on every machine, and
    /// a prefab edit is a thing that can be forgotten. It installs itself onto every player as they
    /// spawn by listening to <see cref="PlayerIdentity.RosterChanged"/>, so it is present before
    /// the first message can arrive. Authoring one on the prefab as well is harmless.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // added in code, never by hand
    public sealed class SceneTransitionViewer : MonoBehaviour
    {
        /// <summary>Matches <see cref="NetArg.B"/> when no transition is named. See SceneTransition.TransitionId.</summary>
        private const int NoTransition = 0;

        /// <summary>
        /// How long this machine will hold a transition's effects up before finishing them itself.
        ///
        /// The mirror of the server's own timeout, and it exists for the mirror-image failure: if
        /// the server drops (or its destination throws) after the out phase, the in phase never
        /// arrives and this player sits on a black screen for the rest of the session. Comfortably
        /// longer than a destination can legitimately take — InteriorSceneDestination alone allows
        /// itself 15 s to wait for a scene — so it never cuts a slow but working load short.
        /// </summary>
        private const float PhaseMaxSeconds = 30f;

        private List<EffectHandle> handles = new();
        private SceneTransition running;
        private int runningId = NoTransition;
        private bool outPhaseReported;
        private float startedAt;

        // The two fields are on two different machines despite sitting in one class: this component
        // exists on every peer's copy of the player, and only one of them is the owner.
        //
        // outPhaseReported above is the OWNER's "I have sent my ack". ackedId below is the SERVER's
        // "the owner's ack arrived", read by SceneTransition.AwaitRemoteOutPhase off the server's
        // own copy of that player.
        private int ackedId = NoTransition;

        // ─────────── Installation ───────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install()
        {
            // Unsubscribe first: with domain reload off the static event survives leaving play
            // mode, and a second subscription would install twice per roster change.
            PlayerIdentity.RosterChanged -= EnsureOnEveryPlayer;
            PlayerIdentity.RosterChanged += EnsureOnEveryPlayer;
        }

        /// <summary>
        /// Every player, not just the local one. The owner needs it to receive its own effects; the
        /// SERVER needs one on its copy of a remote player for that player's ack to land on. Both
        /// are covered by simply putting it on all of them, and on a machine that is neither it
        /// costs one component that never does anything.
        /// </summary>
        private static void EnsureOnEveryPlayer()
        {
            IReadOnlyList<PlayerIdentity> all = PlayerIdentity.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null) Ensure(all[i].gameObject);
        }

        /// <summary>The viewer for <paramref name="player"/>'s entity, adding it if needed.</summary>
        public static SceneTransitionViewer Ensure(GameObject player)
        {
            if (player == null) return null;

            // Onto the NetworkObject root, so the handlers register on the same NetChannel the
            // message is addressed to. NetChannel resolves an entity the same way.
            NetworkObject netObj = player.GetComponentInParent<NetworkObject>();
            GameObject root = netObj != null ? netObj.gameObject : player.transform.root.gameObject;

            return root.TryGetComponent(out SceneTransitionViewer existing)
                ? existing
                : root.AddComponent<SceneTransitionViewer>();
        }

        private void OnEnable()
        {
            this.NetOn(NetMsg.SceneEffects, OnEffects);
            this.NetOn(NetMsg.SceneEffectsDone, OnEffectsDone);
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.SceneEffects, OnEffects);
            this.NetOff(NetMsg.SceneEffectsDone, OnEffectsDone);
        }

        private void Update()
        {
            if (runningId == NoTransition) return;
            if (Time.unscaledTime - startedAt <= PhaseMaxSeconds) return;

            Debug.LogWarning(
                $"[SceneTransition] '{name}' has been mid-transition for {PhaseMaxSeconds:0}s with no " +
                "in-phase from the server — finishing the effects locally so the screen comes back.",
                this);
            FinishIn();
        }

        // ─────────── Server → the one machine that has to watch ───────────

        private void OnEffects(in NetArg arg, ulong sender)
        {
            // Everyone on the session receives this; exactly one machine is looking through this
            // player's eyes. Same filter, same reason, as RopeTugReceiver: on any other machine
            // this body is a replica, and a replica has no screen of its own.
            if (!Network.Owns(this)) return;

            if (arg.A == SceneEffectPhase.Out) BeginOut(arg.B);
            else if (arg.A == SceneEffectPhase.In) FinishIn();
        }

        private void BeginOut(int transitionId)
        {
            // Idempotent, because the same phase can arrive twice — a resend, or the host handing
            // its own NetTo.All broadcast straight back to itself — and a second fade started over
            // the first would never be ended by the single in phase that follows.
            //
            // "Still un-acked" rather than just "same id": walking back through the SAME door is
            // the normal way to use one, and that second trip has to start its own effects rather
            // than be mistaken for a duplicate of the first.
            if (runningId == transitionId && !outPhaseReported) return;

            // A previous transition that never got its in phase. Finish it rather than abandoning
            // its handles, or the screen it faded down stays down forever.
            if (runningId != NoTransition) FinishIn();

            SceneTransition transition = SceneTransition.FindById(transitionId);
            if (transition == null)
            {
                // The door is not loaded on this machine, or the two builds disagree about its id.
                // The transition still has to complete — the ack below is sent either way — but the
                // player gets no fade, which is the very bug this file exists to fix, so say so.
                Debug.LogWarning(
                    $"[SceneTransition] No transition with id {transitionId} on this machine, so its " +
                    "effects cannot be played here. The scene holding it is probably not loaded.", this);
            }

            running = transition;
            runningId = transitionId;
            outPhaseReported = false;
            startedAt = Time.unscaledTime;
            handles = transition != null ? transition.BeginEffects(gameObject) : new List<EffectHandle>();

            // On the DontDestroyOnLoad runner, not on this component: the player is about to be
            // moved between scenes, and the ack has to survive that. Same reason SceneTransition
            // runs its own orchestrator there.
            TransitionRunner.Instance.Run(AckWhenOutPhaseDone(transitionId, handles));
        }

        /// <summary>
        /// Tell the server the out phase is over — the answer it is blocking on.
        ///
        /// Sent even when there were no effects at all: the server is waiting either way, and an
        /// immediate ack is what keeps a door with no fade from costing every client the full
        /// timeout.
        /// </summary>
        private IEnumerator AckWhenOutPhaseDone(int transitionId, List<EffectHandle> phaseHandles)
        {
            // The list is captured rather than read off the field: a second transition arriving
            // mid-wait replaces the field, and iterating it here would walk somebody else's handles.
            for (int i = 0; i < phaseHandles.Count; i++)
            {
                if (phaseHandles[i] != null) yield return phaseHandles[i].AwaitOutPhase();
            }

            // Abandoned while we waited (the player despawned, or a second transition took over).
            // Acking now would release a wait that belongs to a different transition.
            if (this == null || runningId != transitionId) yield break;

            outPhaseReported = true;
            this.NetToServer(NetMsg.SceneEffectsDone, new NetArg { B = transitionId });
        }

        private void FinishIn()
        {
            if (runningId == NoTransition) return;

            List<EffectHandle> finishing = handles;
            SceneTransition transition = running;

            handles = new List<EffectHandle>();
            running = null;
            runningId = NoTransition;
            outPhaseReported = false;

            if (transition != null)
            {
                TransitionRunner.Instance.Run(transition.EndEffects(finishing));
                return;
            }

            // No transition to hand them back to (it was never found). End them directly so a fade
            // that somehow started still comes back up.
            foreach (var h in finishing) h?.End();
        }

        // ─────────── Owner → server ───────────

        private void OnEffectsDone(in NetArg arg, ulong sender)
        {
            // Only the machine that is waiting cares. Written as "not a client" rather than
            // Network.Server so the offline path — where the send falls through to a local
            // dispatch and this machine IS the server by definition — still lands.
            if (Network.IsNetworked && !Network.Server) return;

            ackedId = arg.B;
        }

        /// <summary>
        /// Server side: has this player's owner reported that <paramref name="transitionId"/>'s out
        /// phase finished? Consumes the ack, so a stale one cannot release a later transition.
        /// </summary>
        public bool TakeOutPhaseAck(int transitionId)
        {
            // Never answer for the sentinel, or a caller with nothing to wait on would be told the
            // ack it never asked for had arrived. SceneTransition.TransitionId never mints a 0.
            if (transitionId == NoTransition) return false;
            if (ackedId != transitionId) return false;

            ackedId = NoTransition;
            return true;
        }
    }
}
