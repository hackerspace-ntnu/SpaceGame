// Red "-25" floating up from whatever the local player just hit.
//
// Only the local player's own hits are drawn. The signal for that cannot come from the shooter's
// machine: Weapon.Use() runs on the authority alone, so a client pulling the trigger runs only the
// cosmetic Present() and never learns what its shot did. It arrives instead as NetMsg.Damaged,
// which the server broadcasts and every peer filters — see NetworkedHealthComponent.
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Unity.Netcode;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation
{
    [DisallowMultipleComponent]
    public class DamageNumbers : MonoBehaviour
    {
        [Header("Look")]
        [SerializeField] private Color damageColor = new(1f, 0.27f, 0.22f);
        [SerializeField] private float fontSize = 34f;

        [Tooltip("Canvas units left of, and above, the victim's head. Negative x is left.")]
        [SerializeField] private Vector2 headOffset = new(-52f, 20f);

        [Header("Motion")]
        [Tooltip("Seconds a number stays on screen.")]
        [SerializeField] private float lifetime = 0.9f;

        [Tooltip("Metres the number drifts upward through the world over its life.")]
        [SerializeField] private float riseMetres = 0.85f;

        [Tooltip("Fraction of the life spent fully opaque before the fade starts.")]
        [SerializeField, Range(0f, 1f)] private float holdFraction = 0.5f;

        [Tooltip("Scale the number punches out to on appearing, easing back to 1.")]
        [SerializeField] private float popScale = 1.3f;

        [Tooltip("Seconds the appearing punch takes to settle.")]
        [SerializeField] private float popDuration = 0.12f;

        [Header("Limits")]
        [Tooltip("Hide numbers farther away than this (m). 0 = no limit.")]
        [SerializeField] private float maxDistance = 90f;

        [Tooltip("Most numbers on screen at once. Beyond this the oldest is recycled.")]
        [SerializeField] private int maxConcurrent = 24;

        /// <summary>
        /// Vertical world-space nudge applied per number already in flight near the same point, so
        /// a burst weapon stacks its numbers into a readable column instead of printing them all on
        /// top of each other.
        /// </summary>
        private const float StackStep = 0.28f;

        private const float StackRadius = 1.5f;

        private sealed class Popup
        {
            public TextMeshProUGUI Text;
            public Vector3 Origin;      // world point the number rises from
            public float Age;
            public bool Active;
        }

        private readonly List<Popup> popups = new();

        /// <summary>
        /// Two signals, because one alone cannot cover both cases.
        ///
        /// <see cref="HealthComponent.AnyDamaged"/> fires on the machine that decided the hit, and
        /// needs nothing replicated — it is what makes numbers appear over a plain crate, a test
        /// cube, or any creature nobody has networked. <see cref="NetworkedHealthComponent"/>'s
        /// announcement is the other half: a client's own shot is resolved on the server, so
        /// without it a client would never see a single number.
        ///
        /// They cannot both fire for the same hit — the broadcast is sent with NetToOthers, which
        /// excludes the machine that applied the damage.
        /// </summary>
        /// <summary>
        /// The one instance attached to the damage signals. There is only ever one overlay, so a
        /// second subscriber can only be a leftover — and a leftover draws a duplicate number for
        /// every hit while holding labels that were destroyed with its own overlay.
        /// <para>
        /// Compared with <see cref="ReferenceEquals"/> throughout, never <c>==</c>: a destroyed
        /// MonoBehaviour compares equal to null through Unity's operator, which would skip the
        /// hand-over below and leak exactly the listener it is here to remove.
        /// </para>
        /// </summary>
        private static DamageNumbers subscribed;

        private void OnEnable() => Bind();

        private void OnDisable() => Unbind();

        // OnDisable does not run outside play mode, and these are STATIC events — an unbound
        // listener on a destroyed component would outlive its overlay and keep answering.
        private void OnDestroy() => Unbind();

        /// <summary>
        /// Attaches to both damage signals. Idempotent, and called explicitly by
        /// <see cref="WorldOverlay"/> as well as from OnEnable, because Unity raises OnEnable on
        /// AddComponent in play mode and not outside it — the same reason the overlay builds itself
        /// through an explicit method rather than trusting Awake.
        /// </summary>
        public void Bind()
        {
            if (ReferenceEquals(subscribed, this)) return;

            // Explicit hand-over. Outside play mode Unity raises neither OnDisable nor OnDestroy,
            // so the previous overlay's listener is still attached however carefully it was torn
            // down; making the newcomer evict it means there is never a second one either way.
            if (!ReferenceEquals(subscribed, null)) subscribed.Unbind();

            HealthComponent.AnyDamaged += OnDamagedHere;
            NetworkedHealthComponent.DamageAnnounced += OnDamageAnnounced;
            subscribed = this;
        }

        /// <summary>
        /// Detaches from both signals. Public because the lifecycle callbacks cannot be trusted to
        /// do it everywhere: outside play mode Unity raises neither OnDisable nor OnDestroy, so a
        /// caller that builds an overlay itself has to release it itself.
        /// </summary>
        public void Unbind()
        {
            if (!ReferenceEquals(subscribed, this)) return;

            HealthComponent.AnyDamaged -= OnDamagedHere;
            NetworkedHealthComponent.DamageAnnounced -= OnDamageAnnounced;
            subscribed = null;
        }

        /// <summary>
        /// Damage applied on this machine. The attacker is whatever the caller passed to
        /// <c>NetDamage.Apply</c>, which the victim recorded.
        /// </summary>
        private void OnDamagedHere(HealthComponent victim, int amount)
        {
            if (victim == null || amount <= 0) return;

            Transform source = victim.LastDamageSource;
            if (source == null) return;   // a fall, a cactus — nobody to credit

            Show(victim, amount, source.gameObject);
        }

        /// <summary>A player-dealt hit resolved on another machine — see <see cref="NetMsg.Damaged"/>.</summary>
        private void OnDamageAnnounced(HealthComponent victim, int amount, GameObject attacker)
            => Show(victim, amount, attacker);

        /// <summary>Draw it only if this machine is the one that fired.</summary>
        private void Show(HealthComponent victim, int amount, GameObject attacker)
        {
            if (victim == null || amount <= 0 || attacker == null) return;
            if (!IsLocalPlayer(attacker)) return;

            Spawn(amount, WorldOverlay.HeadOffset(victim.gameObject) + victim.transform.position.y,
                  victim.transform.position);
        }

        /// <summary>
        /// Whether <paramref name="attacker"/> is the player at this keyboard.
        ///
        /// Ownership is the test, not the client id: a NetworkObject's IsOwner is true on exactly
        /// one machine, which is the definition of "mine" that survives being the host, being a
        /// client, and playing alone — singleplayer runs as a host, so there is no separate offline
        /// path to get wrong.
        /// </summary>
        private static bool IsLocalPlayer(GameObject attacker)
        {
            var netObj = attacker.GetComponentInParent<NetworkObject>();
            if (netObj != null) return netObj.IsOwner;

            // Nothing networked above it — an unnetworked test scene, or a session that never
            // started. Then there is only one player it could be, so the tag is answer enough.
            //
            // Walked rather than tested directly, because the attacker handed to us is usually not
            // the player: a hitscan weapon reports its own transform, and that weapon is a child of
            // a socket on the player's rig.
            for (Transform t = attacker.transform; t != null; t = t.parent)
                if (t.CompareTag("Player")) return true;

            return false;
        }

        private void Spawn(int amount, float headWorldY, Vector3 footPosition)
        {
            WorldOverlay overlay = WorldOverlay.Instance;
            if (overlay == null) return;

            // Drop pooled entries whose label is gone. Nothing in the game destroys them — the
            // overlay outlives every scene load — but a pool that trusts its own contents throws a
            // MissingReferenceException instead of drawing a number if anything ever does.
            for (int i = popups.Count - 1; i >= 0; i--)
                if (popups[i].Text == null) popups.RemoveAt(i);

            var origin = new Vector3(footPosition.x, headWorldY, footPosition.z);

            // Lift clear of anything already rising from about the same spot.
            for (int i = 0; i < popups.Count; i++)
            {
                Popup other = popups[i];
                if (!other.Active) continue;
                if ((other.Origin - origin).sqrMagnitude < StackRadius * StackRadius)
                    origin.y += StackStep;
            }

            Popup popup = Take(overlay);
            popup.Origin = origin;
            popup.Age = 0f;
            popup.Active = true;

            popup.Text.text = $"-{amount}";
            popup.Text.color = damageColor;
            // Reset explicitly: a recycled popup may have been culled mid-flight for being
            // off-screen, and would otherwise start its new life invisible.
            popup.Text.enabled = true;
            popup.Text.gameObject.SetActive(true);
        }

        private Popup Take(WorldOverlay overlay)
        {
            for (int i = 0; i < popups.Count; i++)
                if (!popups[i].Active) return popups[i];

            if (popups.Count >= maxConcurrent)
            {
                // All busy and at the ceiling: steal the oldest, which is the one closest to fading
                // out anyway. Losing the tail of an old number beats dropping a new hit.
                Popup oldest = popups[0];
                for (int i = 1; i < popups.Count; i++)
                    if (popups[i].Age > oldest.Age) oldest = popups[i];

                return oldest;
            }

            var created = new Popup
            {
                Text = WorldOverlay.CreateLabel(overlay.Layer, "DamageNumber", fontSize, 260f),
            };
            created.Text.fontStyle = FontStyles.Bold;
            popups.Add(created);

            return created;
        }

        private void Update()
        {
            WorldOverlay overlay = WorldOverlay.Instance;
            if (overlay == null) return;

            Camera eye = overlay.Eye;
            float life = Mathf.Max(0.05f, lifetime);

            for (int i = 0; i < popups.Count; i++)
            {
                Popup popup = popups[i];
                if (!popup.Active) continue;

                popup.Age += Time.deltaTime;
                if (popup.Age >= life)
                {
                    Retire(popup);
                    continue;
                }

                float t = popup.Age / life;

                // The number rises through the WORLD, not the screen, so it keeps its relation to
                // the thing that was hit while the player turns and strafes. It stays put at the
                // point of impact rather than following the victim: victims despawn on death, and a
                // number chasing a fleeing animal is unreadable anyway.
                Vector3 world = popup.Origin + Vector3.up * (riseMetres * t);

                if (!overlay.Project(world, out Vector2 point))
                {
                    popup.Text.enabled = false;
                    continue;
                }

                if (maxDistance > 0f && eye != null
                    && (world - eye.transform.position).sqrMagnitude > maxDistance * maxDistance)
                {
                    popup.Text.enabled = false;
                    continue;
                }

                popup.Text.enabled = true;
                popup.Text.rectTransform.anchoredPosition = point + headOffset;

                float fade = holdFraction >= 1f
                    ? 1f
                    : 1f - Mathf.Clamp01((t - holdFraction) / (1f - holdFraction));
                popup.Text.alpha = fade;

                float pop = popDuration <= 0f
                    ? 1f
                    : Mathf.Lerp(popScale, 1f, Mathf.Clamp01(popup.Age / popDuration));
                popup.Text.rectTransform.localScale = Vector3.one * pop;
            }
        }

        private static void Retire(Popup popup)
        {
            popup.Active = false;
            popup.Text.enabled = true;
            popup.Text.gameObject.SetActive(false);
        }
    }
}
