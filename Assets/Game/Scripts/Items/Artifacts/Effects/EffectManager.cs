using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// Runs the timed effects on one body.
    ///
    /// <para>
    /// <b>Exactly one machine ever runs this loop for a given body: the one that owns it.</b> That
    /// is enforced at the door, in <see cref="AddEffect"/>, rather than by hoping every caller
    /// remembered — because the failure of two machines both running it is not a crash, it is a
    /// slow divergence. Both would tick the same duration against their own frame rate, both would
    /// write <c>useGravity</c>, and the copy that is not the owner's is kinematic
    /// (<c>NetworkRigidbody</c> with AutoUpdateKinematicState), so its forces vanish while its
    /// flag writes do not. The player would land whenever the unluckier of the two timers ran out.
    /// </para>
    /// <para>
    /// With the guard, the list is only ever non-empty on the owner's machine and the loop below
    /// costs the other three nothing.
    /// </para>
    /// </summary>
    public class EffectManager : MonoBehaviour
    {
        private readonly List<Effect> activeEffects = new();
        private Rigidbody playerRigidbody;

        private void Awake()
        {
            playerRigidbody = GetComponent<Rigidbody>();
        }

        /// <summary>
        /// Start an effect on this body, replacing any earlier effect with the same
        /// <see cref="Effect.Key"/>.
        ///
        /// <para>
        /// Replacing rather than stacking is what makes an effect that toggles a flag safe to use
        /// twice. Stopping the old one first also means its <c>stopEffect</c> runs BEFORE the new
        /// one's <c>applyEffect</c>, so the new effect captures the body's real resting state
        /// rather than the state the old effect had left it in.
        /// </para>
        /// </summary>
        public void AddEffect(Effect effect)
        {
            if (effect == null) return;

            // The whole point of this class living on the player. See the class summary: an effect
            // registered anywhere but the owner's machine is at best inert and at worst a flag
            // written onto a body that somebody else is publishing the truth for.
            //
            // Loud rather than silent because nothing legitimate reaches here off the owner —
            // EffectItem.Present already filters on the same question, so this only fires for a
            // caller that has not been taught the rule yet.
            if (!Network.Owns(this))
            {
                Debug.LogWarning($"[Effect] Refused an effect on '{name}': this machine does not own " +
                                 "that body, so the effect would fight the owner's transform sync. " +
                                 "Apply it from the owner — see EffectItem.Present.", this);
                return;
            }

            if (playerRigidbody == null)
            {
                Debug.LogWarning($"[Effect] '{name}' has an EffectManager but no Rigidbody, so there " +
                                 "is nothing for an effect to act on.", this);
                return;
            }

            RemoveByKey(effect.Key);

            activeEffects.Add(effect);
            effect.applyEffect?.Invoke(playerRigidbody);
        }

        /// <summary>
        /// What is running right now, newest last. Live list — read it, do not keep it.
        ///
        /// <para>
        /// For the save system, which has to write down which effects were mid-flight and how much
        /// of each was left. Exposed as read-only because every way of STARTING or STOPPING one has
        /// to go through the two methods either side of this, which own the apply/stop pairing.
        /// </para>
        /// </summary>
        public IReadOnlyList<Effect> ActiveEffects => activeEffects;

        /// <summary>
        /// Replace everything running with a restored set.
        ///
        /// <para>
        /// Restore-only. Called by the save system; do not call from gameplay.
        /// </para>
        /// <para>
        /// Routed through <see cref="RemoveEffect"/> and <see cref="AddEffect"/> rather than
        /// assigning the list, so that every <c>stopEffect</c> that was pending still runs and every
        /// restored <c>applyEffect</c> still fires. Skipping either would leave a body carrying half
        /// an effect — the exact shape of the <c>useGravity</c>/<c>isKinematic</c> bugs that have
        /// already made loaded worlds unplayable in this project.
        /// </para>
        /// <para>
        /// The ownership guard inside <see cref="AddEffect"/> still applies, deliberately: an effect
        /// belongs on the machine that simulates the body, and a restore is not a reason to break
        /// that.
        /// </para>
        /// </summary>
        public void RestoreEffects(IReadOnlyList<Effect> restored)
        {
            for (int i = activeEffects.Count - 1; i >= 0; i--)
                RemoveEffect(activeEffects[i]);

            if (restored == null) return;

            for (int i = 0; i < restored.Count; i++)
                AddEffect(restored[i]);
        }

        public void RemoveEffect(Effect effect)
        {
            if (effect == null) return;

            if (activeEffects.Remove(effect))
            {
                effect.stopEffect?.Invoke(playerRigidbody);
            }
        }

        /// <summary>Stop whatever is currently running under <paramref name="key"/>, if anything.</summary>
        private void RemoveByKey(object key)
        {
            if (key == null) return;

            for (int i = activeEffects.Count - 1; i >= 0; i--)
            {
                if (Equals(activeEffects[i].Key, key)) RemoveEffect(activeEffects[i]);
            }
        }

        /// <summary>
        /// The tick, in FIXED time.
        ///
        /// <para>
        /// Not Update, which is where this used to be. <c>onTick</c> is where an effect pushes the
        /// body — <c>AddForce</c>, and forces are integrated once per physics step, not once per
        /// frame. Called from Update a floating player rose at a rate set by their frame rate: a
        /// 120 fps machine applied the anti-gravity lift four times as often as a 30 fps one, over
        /// a duration that also ran on a different clock. In a session those two players are
        /// looking at each other.
        /// </para>
        /// </summary>
        private void FixedUpdate()
        {
            for (int i = activeEffects.Count - 1; i >= 0; i--)
            {
                // Re-read each iteration: an effect's own tick may remove another one, and the
                // index we are walking toward can move under us.
                if (i >= activeEffects.Count) continue;

                Effect effect = activeEffects[i];
                effect.timer -= Time.fixedDeltaTime;

                effect.onTick?.Invoke(playerRigidbody);

                if (effect.timer <= 0) RemoveEffect(effect);
            }
        }

        /// <summary>
        /// Undo everything on the way out.
        ///
        /// <para>
        /// An effect's <c>stopEffect</c> is the half that puts the body back — gravity on, speed
        /// back to normal. A body destroyed mid-effect never reaches its expiry, and while that is
        /// harmless for a body that is going away, it is not harmless for the Rigidbody itself:
        /// this is the component that gets torn down when a player despawns, and leaving the last
        /// write of <c>useGravity = false</c> standing on a body something else might still be
        /// holding is exactly the shape of the kinematic-flag bug that made loaded worlds
        /// unplayable.
        /// </para>
        /// </summary>
        private void OnDestroy() => StopAll();

        /// <summary>
        /// Stop everything running and put the body back.
        ///
        /// <para>
        /// Public because destruction is not the only moment that wants it — a respawn and an
        /// ownership change both need the body handed back in one piece — and because a guarantee
        /// that can only be triggered by Unity's teardown callback cannot be tested. Edit mode does
        /// not raise <c>OnDestroy</c> for a component it never awakened, so the callback alone left
        /// the rule unverifiable.
        /// </para>
        /// <para>
        /// Idempotent: an effect is removed before its <c>stopEffect</c> runs, so a second call has
        /// nothing left to undo.
        /// </para>
        /// </summary>
        public void StopAll()
        {
            for (int i = activeEffects.Count - 1; i >= 0; i--)
                RemoveEffect(activeEffects[i]);
        }
    }
}
