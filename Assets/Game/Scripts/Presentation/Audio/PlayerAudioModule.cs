// Gives the player character its own voice: footsteps paced by actual speed, jumps, landings
// weighted by impact, dashes, and the hurt/death reactions.
//
// Player-only on purpose: the sounds come from concrete events on PlayerMovement and
// HealthComponent, which no NPC has.
using FMODUnity;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation
{
    [RequireComponent(typeof(PlayerMovement))]
    public class PlayerAudioModule : MonoBehaviour
    {
        [Header("Footsteps")]
        [SerializeField] private EventReference footstepSound;
        [Tooltip("Metres of travel between footsteps. Pacing on distance rather than on a timer is " +
                 "what keeps the stride matched to the animation at every speed.")]
        [SerializeField] private float strideLength = 2.2f;
        [Tooltip("Below this speed the player is considered to be standing still.")]
        [SerializeField] private float movementThreshold = 0.6f;

        [Header("Jump and land")]
        [SerializeField] private EventReference jumpSound;
        [SerializeField] private EventReference landSound;
        [Tooltip("Impact speed past which a landing uses the heavy sound. Negative — this is a " +
                 "downward velocity, and it should sit near the fall-damage threshold so a landing " +
                 "that hurts also sounds like it did.")]
        [SerializeField] private float heavyLandSpeed = -8f;

        [Header("Dash")]
        [SerializeField] private EventReference dashSound;

        [Header("Damage")]
        [SerializeField] private EventReference hurtSound;
        [SerializeField] private EventReference deathSound;
        [SerializeField] private EventReference respawnSound;

        private PlayerMovement movement;
        private HealthComponent health;

        private float distanceSinceStep;
        private Vector3 lastPosition;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            health = GetComponent<HealthComponent>();
            lastPosition = transform.position;
        }

        private void OnEnable()
        {
            if (movement != null)
            {
                movement.OnJumped += HandleJump;
                movement.OnLanded += HandleLand;
                movement.OnDashed += HandleDash;
            }

            if (health != null)
            {
                health.OnDamage += HandleDamage;
                health.OnDeath += HandleDeath;
                health.OnRevive += HandleRevive;
            }

            distanceSinceStep = 0f;
            lastPosition = transform.position;
        }

        private void OnDisable()
        {
            if (movement != null)
            {
                movement.OnJumped -= HandleJump;
                movement.OnLanded -= HandleLand;
                movement.OnDashed -= HandleDash;
            }

            if (health != null)
            {
                health.OnDamage -= HandleDamage;
                health.OnDeath -= HandleDeath;
                health.OnRevive -= HandleRevive;
            }
        }

        private void Update()
        {
            HandleFootsteps();
        }

        /// <summary>
        /// Accumulates ground distance and emits a step each stride.
        /// <para>
        /// Measured from the transform rather than from rigidbody velocity so that a player being
        /// carried — riding a mount or standing on a moving vehicle — does not walk on the spot.
        /// </para>
        /// </summary>
        private void HandleFootsteps()
        {
            Vector3 position = transform.position;
            Vector3 delta = position - lastPosition;
            lastPosition = position;

            if (movement == null || !movement.IsOnGround)
            {
                // Part-way through a stride when the player leaves the ground: reset, so the next
                // landing does not immediately spend a stride that was never walked.
                distanceSinceStep = 0f;
                return;
            }

            if (movement.HorizontalSpeed < movementThreshold)
                return;

            delta.y = 0f;
            distanceSinceStep += delta.magnitude;

            if (distanceSinceStep < strideLength)
                return;

            distanceSinceStep = 0f;
            Sfx.Play(footstepSound, position);
        }

        private void HandleJump()
        {
            Sfx.Play(jumpSound, transform.position);
        }

        private void HandleLand(float impactSpeed)
        {
            float weight = impactSpeed <= heavyLandSpeed ? 1.0f : 0f;
            
            Sfx.PlayWithParameter(landSound, transform.position, "Weight", weight);

            // A landing ends whatever stride was in progress; without this the first step after
            // touching down comes early.
            distanceSinceStep = 0f;
        }

        private void HandleDash()
        {
            Sfx.Play(dashSound, transform.position);
        }

        // DamageFeedback also reacts to OnDamage, but only where that component is present — it
        // carries the camera shake and is not on every player rig. Both playing would double the
        // hit sound, so this defers when it sees one.
        private void HandleDamage(int amount)
        {
            if (GetComponent<DamageFeedback>() != null) return;

            Sfx.Play(hurtSound, transform.position);
        }

        private void HandleDeath()
        {
            Sfx.Play(deathSound, transform.position);
        }

        // OnRevive rather than anything on PlayerRespawn: health state replicates, so this fires on
        // every machine that has this player, which is what makes a remote player's respawn audible
        // to the people standing next to them.
        private void HandleRevive()
        {
            Sfx.Play(respawnSound, transform.position);

            distanceSinceStep = 0f;
            lastPosition = transform.position;
        }

        private void OnValidate()
        {
            strideLength = Mathf.Max(0.2f, strideLength);
            movementThreshold = Mathf.Max(0f, movementThreshold);
            heavyLandSpeed = Mathf.Min(0f, heavyLandSpeed);
        }
    }
}
