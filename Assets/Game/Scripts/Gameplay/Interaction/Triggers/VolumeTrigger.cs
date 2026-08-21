using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Persistence;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Drop on a GameObject with a trigger Collider that also has any <see cref="ITriggerable"/>.
    /// Fires when a player or AI agent enters the volume. Identifies initiators by:
    ///   • Player — GameObject tagged "Player".
    ///   • AI agent — has an AgentController in self or parents.
    /// Both checks are togglable. After firing, a cooldown re-arms so the same agent stepping
    /// back through the volume doesn't immediately re-trigger.
    ///
    /// Eligibility does NOT require the initiator to share the trigger's scene — world streaming
    /// migrates players/agents between exterior chunk sub-scenes constantly. The one exclusion is
    /// a player who is currently inside an interior (asked via <see cref="InteriorManager"/>),
    /// because interiors load additively and overlap the exterior in world space.
    ///
    /// Re-entry cooldown: after a player triggers this volume and goes through an interior, they
    /// cannot re-trigger THIS SAME volume for <see cref="reentryCooldown"/> seconds after they
    /// return to the exterior. The window is measured from the exit (not the original fire),
    /// because real time keeps passing while the player is inside the interior — a cooldown
    /// started at entry would have expired by the time they came back out.
    ///
    /// ONLY THE SERVER FIRES. See <see cref="ThisMachineDecides"/> — a volume is a local
    /// observation of a shared event, and only the authority's observation is allowed to count.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [AddComponentMenu("Triggers/Volume Trigger")]
    public class VolumeTrigger : MonoBehaviour, IPersistentEntity
    {
        [Tooltip("Optional. If unset, the first ITriggerable on this GameObject is used.")]
        [SerializeField] private MonoBehaviour triggerableOverride;

        [SerializeField] private bool triggerForPlayers = true;
        [SerializeField] private bool triggerForAgents = true;
        [Tooltip("Seconds before this volume can fire again after a successful trigger.")]
        [SerializeField] private float rearmCooldown = 1f;
        [Tooltip("After a player returns from an interior, how long (seconds) THIS volume refuses to " +
                 "re-fire for them. Measured from the moment they step back into the exterior, so it " +
                 "is a real post-exit cooldown regardless of how long they spent inside.")]
        [SerializeField] private float reentryCooldown = 4f;

        private ITriggerable cached;
        private float armedAt;
        private float lastStayLog;

        /// <summary>
        /// Whether this machine's observation of the volume is the one that counts.
        ///
        /// Every player's body exists on every machine, so a volume in the world overlaps a collider
        /// locally whenever ANYONE walks into it — including a remote player's body, on your screen,
        /// firing YOUR ITriggerable. That is one player walking into a cave marking everybody's
        /// SceneTransition busy, and every client deciding for itself that some other player is now
        /// inside an interior it has not loaded and holds no return position for.
        ///
        /// Deliberately "server or offline" and NOT <c>Network.Simulates(this)</c>, which is the gate
        /// the interactables use. Simulates answers about the object it is handed, and a trigger
        /// volume is scenery with no NetworkObject of its own — it would answer "yes, you simulate
        /// this" on every client and change nothing at all. The thing that HAS an authority here is
        /// the player being moved, and the server is the machine that owns that decision.
        ///
        /// Offline is still this machine, so single-player is untouched.
        /// </summary>
        private static bool ThisMachineDecides => !Network.IsNetworked || Network.Server;

        // Per-(player, this-volume) re-entry state. Static so it survives the volume being
        // unloaded/reloaded by world streaming while the player is off in an interior.
        //
        // Only ever populated on the machine that fires — see ThisMachineDecides — which is why the
        // gate sits at the very top of TryFire rather than beside the Trigger call. A client that got
        // as far as GetReentryState would mint an entry for a player it will never fire for.
        //
        // KEYED BY STABLE IDENTITY, not by GetInstanceID. Instance ids are per-object-per-session,
        // which broke this map twice over: a volume that streamed out and back in was a different
        // key on its way back, so the cooldown it was keeping never applied to the volume it was
        // kept for, and nothing about the state could be written to a save at all. Identity keys are
        // the same strings a save record uses, so the map both survives streaming and can be stored.
        private class ReentryState
        {
            // True once this player triggers this volume, until they are next confirmed back
            // in the exterior. Names a "transition is in flight through this volume" — NOT a
            // literal inside-interior reading (the interior load is async; the player is not
            // inside synchronously on the frame Trigger() is called).
            public bool TransitionPending;
            public float CooldownUntil;      // unscaled-time before which this volume must not re-fire
        }
        private static readonly Dictionary<(string volumeKey, string playerKey), ReentryState> s_reentry = new();

        /// <summary>
        /// Empty the map when play starts.
        ///
        /// Static state survives leaving play mode when domain reload is off, so without this the
        /// cooldowns from the previous session are still in force in the next one — a transition
        /// volume silently refusing to fire for a player who has never used it, which is
        /// indistinguishable from the volume being broken. Same reasoning, and the same attribute,
        /// as <c>WorldSiteRegistry.Clear</c>.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetReentry() => s_reentry.Clear();

        /// <summary>
        /// This volume's identity, as something two sessions can agree on.
        ///
        /// The save record's own id where the object has been wired for saving, and the same derived
        /// scene-plus-hierarchy id the save system would give it otherwise — so the key is stable
        /// whether or not anybody has run the wiring tool.
        /// </summary>
        private string VolumeKey
        {
            get
            {
                if (!string.IsNullOrEmpty(volumeKey)) return volumeKey;

                var entity = GetComponent<SaveableEntity>();
                volumeKey = entity != null && !string.IsNullOrEmpty(entity.InstanceId)
                    ? entity.InstanceId
                    : SaveableEntity.DeriveAuthoredId(gameObject);

                return volumeKey;
            }
        }

        private string volumeKey;

        /// <summary>
        /// Who this is, as something two sessions can agree on.
        ///
        /// <see cref="SaveRef"/> answers "profile abc" for a player and "entity def" for an agent,
        /// which is exactly the distinction this map needs and exactly what a save file can hold.
        /// The fallback is only reached outside a bound session, where nothing is being saved
        /// anyway and a per-session key is all that is wanted.
        /// </summary>
        private static string PlayerKeyOf(GameObject initiator)
        {
            SaveRef reference = SaveRef.From(initiator);
            return reference.IsSet ? reference.ToString() : "local:" + initiator.GetInstanceID();
        }

        private void Awake()
        {
            cached = ResolveTriggerable();
            var col = GetComponent<Collider>();
            if (!col.isTrigger)
                Debug.LogWarning($"[VolumeTrigger] Collider on '{name}' should be set to isTrigger.", this);
        }

        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        // Deliberately NOT dropping this volume's entries when it is destroyed any more.
        //
        // The old sweep existed because the key was an instance id, which a destroyed volume could
        // never present again — so the entries really were dead weight. It also defeated the reason
        // the map is static: world streaming unloads the cave entrance the moment the player is deep
        // enough inside the cave, and sweeping on destroy threw away the very cooldown that stops
        // the entrance re-firing the instant they step back out of it.
        //
        // With identity keys the entries are worth keeping, and there are only ever as many as
        // (players × volumes they have actually used).

        private void OnTriggerEnter(Collider other)
        {
            Debug.Log($"[VolumeTrigger] '{name}' OnTriggerEnter from '{other?.name}'", this);
            TryFire(other, "Enter");
        }

        private void OnTriggerExit(Collider other)
        {
            Debug.Log($"[VolumeTrigger] '{name}' OnTriggerExit from '{other?.name}'", this);
        }

        // Also poll while overlapping — a destination that teleports the player back to the
        // exterior often lands them *inside* this volume (e.g. cave exit teleports to the
        // saved entry position). Unity does not fire OnTriggerEnter for instantaneous
        // teleports, so without this the player can never re-enter. CanTrigger + the cross-
        // transition lockout in SceneTransition still prevent immediate re-fire.
        private void OnTriggerStay(Collider other)
        {
            if (Time.time - lastStayLog > 1f)
            {
                Debug.Log($"[VolumeTrigger] '{name}' OnTriggerStay from '{other?.name}' (armedAt-now={armedAt - Time.time:0.00})", this);
                lastStayLog = Time.time;
            }
            TryFire(other, "Stay");
        }

        private void TryFire(Collider other, string source)
        {
            // Before anything else, so a client neither logs once a frame out of OnTriggerStay nor
            // mints a per-(player, volume) re-entry entry it will never consult. That map is static
            // and is only swept when the volume is destroyed, so entries a client can never use
            // would sit in it for the length of the session.
            if (!ThisMachineDecides)
            {
                if (source == "Enter")
                    Debug.Log($"[VolumeTrigger] '{name}' {source} ignored: only the server decides " +
                              "that a volume fired. The server sees this body too.", this);
                return;
            }

            if (Time.time < armedAt)
            {
                if (source == "Enter")
                    Debug.Log($"[VolumeTrigger] '{name}' {source} rejected: armed-cooldown ({armedAt - Time.time:0.00}s remaining)", this);
                return;
            }
            var t = cached ?? ResolveTriggerable();
            if (t == null)
            {
                if (source == "Enter")
                    Debug.LogWarning($"[VolumeTrigger] '{name}' has no ITriggerable", this);
                return;
            }

            GameObject candidate = ResolveInitiatorRoot(other);
            if (candidate == null) return;
            if (source == "Enter")
                Debug.Log($"[VolumeTrigger] '{name}' {source}: resolved candidate='{candidate.name}' tag='{candidate.tag}' (other='{other.name}', otherTag='{other.tag}', attachedRb={(other.attachedRigidbody != null ? other.attachedRigidbody.name : "<none>")})", this);

            // Advance the inside→outside tracking every time we see the candidate, so the
            // post-exit cooldown can start the instant they return even if we never get a
            // clean OnTriggerEnter (teleport-back lands them mid-volume → only Stay fires).
            UpdateReentryTracking(candidate);

            if (!IsEligible(candidate))
            {
                if (source == "Enter")
                {
                    bool insideInterior = InteriorManager.Instance != null && InteriorManager.Instance.IsInsideInterior(candidate);
                    Debug.Log($"[VolumeTrigger] '{name}' {source} rejected: '{candidate.name}' not eligible " +
                              $"(tag={candidate.tag}, hasAgent={candidate.GetComponentInParent<AgentController>() != null}, " +
                              $"insideInterior={insideInterior}, triggerForPlayers={triggerForPlayers}, triggerForAgents={triggerForAgents})", this);
                }
                return;
            }

            // Per-(player, this-volume) re-entry cooldown — refuse to re-fire the same
            // entrance for a while after they came back out through it.
            var state = GetReentryState(candidate);
            if (Time.unscaledTime < state.CooldownUntil)
            {
                if (source == "Enter")
                    Debug.Log($"[VolumeTrigger] '{name}' {source} rejected: re-entry cooldown " +
                              $"({state.CooldownUntil - Time.unscaledTime:0.00}s remaining for '{candidate.name}')", this);
                return;
            }

            if (!t.CanTrigger(candidate))
            {
                // SceneTransition prints its own diagnostic when it denies — no log here.
                return;
            }

            if (t.Trigger(candidate) != null)
            {
                armedAt = Time.time + rearmCooldown;
                // A transition is now in flight through this volume. When the player is next
                // confirmed back outside (not inside an interior), the post-exit cooldown arms.
                state.TransitionPending = true;
            }
        }

        private ReentryState GetReentryState(GameObject player)
        {
            var key = (VolumeKey, PlayerKeyOf(player));
            if (!s_reentry.TryGetValue(key, out var state))
            {
                state = new ReentryState();
                s_reentry[key] = state;
            }
            return state;
        }

        // ── Persistence ──────────────────────────────────────────────────────────

        /// <summary>One player's standing with this volume, in a form a save record can hold.</summary>
        public struct ReentryRecord
        {
            /// <summary>Who — a <see cref="SaveRef"/> rendered as a string. See PlayerKeyOf.</summary>
            public string player;

            /// <summary>A transition through this volume that has not yet resolved.</summary>
            public bool transitionPending;

            /// <summary>
            /// Seconds of refusal still owed. A remaining duration and not the deadline, because
            /// <c>Time.unscaledTime</c> restarts with the session.
            /// </summary>
            public float cooldownRemaining;
        }

        /// <summary>
        /// What this volume currently refuses, and to whom.
        ///
        /// Worth saving because a one-per-trip transition volume that forgets is a volume that can
        /// fire again the moment the world finishes loading — and a load very often puts the player
        /// standing exactly where they were, which for a cave mouth is inside the trigger.
        /// </summary>
        public List<ReentryRecord> CaptureReentry()
        {
            List<ReentryRecord> records = null;
            string volume = VolumeKey;

            foreach (KeyValuePair<(string volumeKey, string playerKey), ReentryState> entry in s_reentry)
            {
                if (entry.Key.volumeKey != volume || entry.Value == null) continue;

                float remaining = Mathf.Max(0f, entry.Value.CooldownUntil - Time.unscaledTime);
                if (!entry.Value.TransitionPending && remaining <= 0f) continue;

                records ??= new List<ReentryRecord>();
                records.Add(new ReentryRecord
                {
                    player = entry.Key.playerKey,
                    transitionPending = entry.Value.TransitionPending,
                    cooldownRemaining = remaining,
                });
            }

            return records;
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// Replaces this volume's entries and leaves every other volume's alone, so restoring one
        /// chunk cannot re-arm a transition somewhere else in the world.
        /// </summary>
        public void RestoreReentry(List<ReentryRecord> records)
        {
            string volume = VolumeKey;

            var stale = new List<(string, string)>();
            foreach (var key in s_reentry.Keys)
                if (key.volumeKey == volume) stale.Add(key);
            foreach (var key in stale) s_reentry.Remove(key);

            if (records == null) return;

            foreach (ReentryRecord record in records)
            {
                if (string.IsNullOrEmpty(record.player)) continue;

                s_reentry[(volume, record.player)] = new ReentryState
                {
                    TransitionPending = record.transitionPending,
                    CooldownUntil = Time.unscaledTime + Mathf.Max(0f, record.cooldownRemaining),
                };
            }
        }

        // Arm the post-exit re-entry cooldown the first time we see this player back in the
        // exterior after they triggered a transition through this volume.
        //
        // Why "back outside" and not "exited interior": some destinations are same-scene
        // teleports with no interior at all. The condition we want is simply "the transition
        // this volume kicked off has resolved and the player is here again, eligible". Using
        // IsInsideInterior == false covers both: interior round-trips (false once they return)
        // and non-interior transitions (false the whole time, so the cooldown arms on the
        // next frame the player overlaps this volume — still a real post-fire guard).
        private void UpdateReentryTracking(GameObject player)
        {
            var state = GetReentryState(player);
            if (!state.TransitionPending) return;

            bool insideInterior = InteriorManager.Instance != null && InteriorManager.Instance.IsInsideInterior(player);
            if (insideInterior) return;   // still away — wait for the round-trip to finish

            // Player is back (or never left, for same-scene destinations). Arm the cooldown,
            // measured from now, and clear the pending flag so it only arms once per trip.
            state.CooldownUntil = Time.unscaledTime + Mathf.Max(0f, reentryCooldown);
            state.TransitionPending = false;
        }

        private ITriggerable ResolveTriggerable()
        {
            if (triggerableOverride is ITriggerable explicitT) return explicitT;
            return GetComponent<ITriggerable>();
        }

        private static GameObject ResolveInitiatorRoot(Collider other)
        {
            if (other.attachedRigidbody != null) return other.attachedRigidbody.gameObject;
            return other.gameObject;
        }

        private bool IsEligible(GameObject go)
        {
            // We must reject a player who has walked off into an interior: interior scenes load
            // additively beside the exterior, so an entrance volume in the persistent scene
            // physically overlaps the world position of a player who is no longer "here", and
            // OnTriggerStay would keep re-firing the entrance against them.
            //
            // The check used to be `go.scene != gameObject.scene`. That is WRONG: world streaming
            // migrates the player between exterior chunk sub-scenes, so the player's scene almost
            // never equals the trigger's persistent scene during normal play — the raw scene check
            // silently rejected every legitimate trigger. Ask InteriorManager directly instead.
            if (InteriorManager.Instance != null && InteriorManager.Instance.IsInsideInterior(go))
                return false;

            if (triggerForPlayers && go.CompareTag("Player")) return true;
            if (triggerForAgents && go.GetComponentInParent<AgentController>() != null) return true;
            return false;
        }
    }
}
