using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Gameplay.Ragdoll;

namespace SpaceGame.Gameplay.Arena
{
    /// <summary>
    /// An arena player's body leaves the field when they are eliminated and comes back when the
    /// next round revives them — instead of the ragdoll heap the story world leaves behind.
    ///
    /// <para>
    /// Turning the death ragdoll off alone would leave the body standing frozen where it died,
    /// which reads as a live opponent who stopped moving. So the body is hidden outright: its
    /// renderers, its collider (nothing should be shot at or bumped into that nobody can see), and
    /// its rigidbody held kinematic so a body with no collider does not fall through the ground.
    /// </para>
    ///
    /// <para>
    /// Every machine, off the replicated death and revive <see cref="HealthComponent"/> raises
    /// everywhere, so every screen takes the body away on the same death. Added by
    /// <see cref="ArenaMatch"/> to each player as they appear; nothing to author.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class ArenaEliminatedBody : MonoBehaviour
    {
        private HealthComponent health;
        private Rigidbody body;
        private Collider bodyCollider;

        // What this component switched off, so a revive hands back exactly that and no more — a
        // renderer something else had hidden stays hidden.
        private readonly List<Renderer> hiddenRenderers = new();
        private bool hidden;
        private bool bodyWasKinematic;
        private bool colliderWasEnabled;

        private void Awake()
        {
            health = GetComponent<HealthComponent>();
            body = GetComponent<Rigidbody>();

            // The player's capsule lives on a child, beside the ragdoll's bone capsules, so it is
            // asked of the movement rather than searched for.
            bodyCollider = TryGetComponent(out PlayerMovement movement) ? movement.BodyCapsule : null;

            if (TryGetComponent(out RagdollController ragdoll)) ragdoll.LimpsOnDeath = false;

            if (health == null)
                Debug.LogError($"[Arena] '{name}' has no HealthComponent beside its ArenaEliminatedBody, " +
                               "so its body is never taken off the field.", this);
        }

        private void OnEnable()
        {
            if (health == null) return;

            health.OnDeath += Hide;
            health.OnRevive += Show;

            // Joined late, or added after the death it should have answered.
            if (!health.Alive) Hide();
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.OnDeath -= Hide;
                health.OnRevive -= Show;
            }

            Show();
        }

        private void Hide()
        {
            if (hidden) return;
            hidden = true;

            // Gathered now rather than in Awake: what the player holds is parented under them at
            // runtime and comes and goes with the hotbar.
            hiddenRenderers.Clear();
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                r.enabled = false;
                hiddenRenderers.Add(r);
            }

            if (bodyCollider != null)
            {
                colliderWasEnabled = bodyCollider.enabled;
                bodyCollider.enabled = false;
            }

            if (body != null)
            {
                bodyWasKinematic = body.isKinematic;
                if (!body.isKinematic) body.linearVelocity = Vector3.zero;
                body.isKinematic = true;
            }
        }

        private void Show()
        {
            if (!hidden) return;
            hidden = false;

            foreach (Renderer r in hiddenRenderers)
                if (r != null) r.enabled = true;
            hiddenRenderers.Clear();

            if (bodyCollider != null && colliderWasEnabled) bodyCollider.enabled = true;
            if (body != null) body.isKinematic = bodyWasKinematic;
        }
    }
}
