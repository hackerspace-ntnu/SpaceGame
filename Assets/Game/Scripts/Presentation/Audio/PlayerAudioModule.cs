// Gives the player character its own voice: footsteps paced by actual speed, jumps, landings
// weighted by impact, dashes, and the hurt/death reactions.
//
// The entity equivalent is EntityAudioModule; this is deliberately a separate component rather than
// a shared one, because the player's sounds come from concrete events on PlayerMovement and
// HealthComponent while an entity's are inferred from a motor interface.
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
        [SerializeField] private SfxId footstepId = SfxId.PlayerFootstep;
        [SerializeField] private EventReference footstepSound;
        [Tooltip("Metres of travel between footsteps. Pacing on distance rather than on a timer is " +
                 "what keeps the stride matched to the animation at every speed.")]
        [SerializeField] private float strideLength = 2.2f;
        [Tooltip("Below this speed the player is considered to be standing still.")]
        [SerializeField] private float movementThreshold = 0.6f;

        [Header("Jump and land")]
        [SerializeField] private SfxId jumpId = SfxId.PlayerJump;
        [SerializeField] private EventReference jumpSound;
        [SerializeField] private SfxId landId = SfxId.PlayerLand;
        [SerializeField] private SfxId landHeavyId = SfxId.PlayerLandHeavy;
        [Tooltip("Impact speed past which a landing uses the heavy sound. Negative — this is a " +
                 "downward velocity, and it should sit near the fall-damage threshold so a landing " +
                 "that hurts also sounds like it did.")]
        [SerializeField] private float heavyLandSpeed = -8f;

        [Header("Dash")]
        [SerializeField] private SfxId dashId = SfxId.PlayerDash;
        [SerializeField] private EventReference dashSound;

        [Header("Damage")]
        [SerializeField] private SfxId hurtId = SfxId.PlayerHurt;
        [SerializeField] private SfxId deathId = SfxId.PlayerDeath;
        [SerializeField] private SfxId respawnId = SfxId.PlayerRespawn;

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
            Sfx.Play(footstepId, position, footstepSound, GetInstanceID());
        }

        private void HandleJump()
        {
            Sfx.Play(jumpId, transform.position, jumpSound, GetInstanceID());
        }

        private void HandleLand(float impactSpeed)
        {
            SfxId id = impactSpeed <= heavyLandSpeed ? landHeavyId : landId;
            Sfx.Play(id, transform.position, default, GetInstanceID());

            // A landing ends whatever stride was in progress; without this the first step after
            // touching down comes early.
            distanceSinceStep = 0f;
        }

        private void HandleDash()
        {
            Sfx.Play(dashId, transform.position, dashSound, GetInstanceID());
        }

        // DamageFeedback also reacts to OnDamage, but only where that component is present — it
        // carries the camera shake and is not on every player rig. Both playing would double the
        // hit sound, so this defers when it sees one.
        private void HandleDamage(int amount)
        {
            if (GetComponent<DamageFeedback>() != null) return;

            Sfx.Play(hurtId, transform.position, default, GetInstanceID());
        }

        private void HandleDeath()
        {
            Sfx.Play(deathId, transform.position, default, GetInstanceID());
        }

        // OnRevive rather than anything on PlayerRespawn: health state replicates, so this fires on
        // every machine that has this player, which is what makes a remote player's respawn audible
        // to the people standing next to them.
        private void HandleRevive()
        {
            Sfx.Play(respawnId, transform.position, default, GetInstanceID());

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
