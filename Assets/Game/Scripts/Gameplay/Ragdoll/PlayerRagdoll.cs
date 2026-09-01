using SpaceGame.Characters;
using SpaceGame.Core;
using UnityEngine;

namespace SpaceGame.Gameplay.Ragdoll
{
    /// <summary>
    /// Takes a player's body away from them when they die or are caught by a shock wave, and gives
    /// it back.
    ///
    /// <para>
    /// Taking control from a player is the thing this codebase otherwise refuses to do, so it is
    /// worth being explicit about which rules apply. <c>GDC-L1-FEEL-0002</c> draws the line between
    /// latency — the game being slow to HEAR you, always a defect — and commitment, the game taking
    /// time to carry out what it already heard. A knockdown is neither: it is a state the player did
    /// not ask for, and the only honest way to price it is to bound it. Hence
    /// <c>RagdollRig.maxLimpSeconds</c>: a player wedged against a rock never settles, and without a
    /// ceiling would never stand up. <c>GDC-L1-ANIM-0002</c> supplies the other half — control comes
    /// back at the START of the recovery blend, not the end, so the player is already driving while
    /// their body finishes standing up.
    /// </para>
    ///
    /// <para>
    /// The caster of a repulsor blast is never a victim of it: <c>RepulsorGauntletArtifact.FireBlast</c>
    /// excludes its own holder's root, so the repulsor-jump and its recoil are untouched by any of
    /// this.
    /// </para>
    ///
    /// <para>
    /// Death and knockdown share the ragdoll and nothing else. Death is permanent limpness and
    /// <c>PlayerController</c> keeps owning its freeze, its cursor and its death screen — the flag
    /// on <c>PlayerController.isDead</c> says death outranks every other control owner, and this is
    /// one of them. A knockdown recovers on its own.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(RagdollRig))]
    public class PlayerRagdoll : MonoBehaviour
    {
        [Tooltip("Seconds a knocked-down player stays down when the blast that felled them does " +
                 "not say. Deliberately shorter than a creature's: this is time the player spends " +
                 "not playing.")]
        [SerializeField] private float downedSeconds = 0.9f;

        [Tooltip("Upward speed handed to a dying player's body, m/s. Just enough that they fold " +
                 "over their own feet rather than sinking through them.")]
        [SerializeField] private float deathLift = 1.5f;

        [Header("Camera")]
        [Tooltip("Where the camera sits relative to the fallen body while limp — back, up and to " +
                 "the side, in the body's own frame.\n\n" +
                 "The camera has to leave the head. It normally lives inside the helmet, and a " +
                 "first-person view bolted to a tumbling skull is unusable in the literal sense: " +
                 "the player cannot tell what happened to them, which is the one thing a knockdown " +
                 "has to communicate.")]
        [SerializeField] private Vector3 downedCameraOffset = new Vector3(0.6f, 1.8f, -2.4f);

        [Tooltip("How fast the camera chases that spot, per second. Low enough to smooth the " +
                 "tumble out; a camera rigidly attached to a ragdoll is the tumbling skull again " +
                 "at a longer focal length (GDC-L1-FEEL-0006 — dose the camera motion).")]
        [SerializeField] private float downedCameraLerp = 6f;

        private RagdollRig rig;
        private HealthComponent health;
        private PlayerController controller;
        private PlayerMovement movement;
        private PlayerLook look;
        private Rigidbody body;
        private Collider bodyCollider;

        private bool suspended;
        private bool bodyWasKinematic;
        private bool movementWasEnabled;
        private bool lookWasEnabled;
        private bool inputWasEnabled;
        private bool colliderWasEnabled;
        private bool dead;
        private float downUntil;

        private Transform cameraTransform;
        private Transform cameraParent;
        private Vector3 cameraLocalPosition;
        private Quaternion cameraLocalRotation;

        /// <summary>The heading the camera watches from, fixed when the player goes down.</summary>
        private Quaternion downedCameraFrame;

        private void Awake()
        {
            rig = GetComponent<RagdollRig>();
            health = GetComponent<HealthComponent>();
            controller = GetComponent<PlayerController>();
            movement = GetComponent<PlayerMovement>();
            look = GetComponent<PlayerLook>();
            body = GetComponent<Rigidbody>();
            bodyCollider = GetComponent<Collider>();

            if (health == null)
                Debug.LogWarning($"{name}: PlayerRagdoll needs a HealthComponent to know when this " +
                                 "player dies.", this);
        }

        private void OnEnable()
        {
            this.NetOn(NetMsg.Knockdown, OnKnockdown);
            if (health != null) health.OnDeath += OnDeath;
            if (health != null) health.OnRevive += OnRevive;
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.Knockdown, OnKnockdown);
            if (health != null) health.OnDeath -= OnDeath;
            if (health != null) health.OnRevive -= OnRevive;
        }

        /// <summary>
        /// A player's body is OWNER-authoritative — the rule FlungBody follows, and the reason a
        /// server-side push on one is overwritten within a tick. So the machine that drives the
        /// ragdoll is the one that owns the player, not the server.
        /// </summary>
        private bool Drives => Network.Owns(this);

        /// <summary>
        /// How fast this player was already moving. Unlike a creature's, this one really is on the
        /// rigidbody — but only until <see cref="Suspend"/> makes it kinematic, so every caller
        /// reads it first.
        /// </summary>
        private Vector3 CarriedVelocity =>
            body != null && !body.isKinematic ? body.linearVelocity : Vector3.zero;

        // ── What starts it ────────────────────────────────────────────────────

        private void OnDeath()
        {
            dead = true;

            // On a peer's machine a networked death arrives through RestoreHealth, which sets
            // IsRestoring — so this is true both for a save being loaded and for a remote player's
            // death reaching this machine. Both want the same thing: the body lies down where it
            // is, without being thrown again.
            bool restoring = health != null && health.IsRestoring;

            // Read before Suspend, which switches the body kinematic underneath it.
            Vector3 carried = restoring ? Vector3.zero : CarriedVelocity;

            Suspend();
            rig.GoLimp(restoring ? Vector3.zero : Vector3.up * deathLift + carried,
                       settled: restoring, drives: Drives);
        }

        private void OnRevive()
        {
            dead = false;
            if (rig.IsLimp) Restore();
        }

        /// <summary>A blast: impulse in <c>P</c>, seconds down in <c>A</c> as milliseconds.</summary>
        private void OnKnockdown(in NetArg arg, ulong sender)
        {
            if (dead) return;

            Vector3 carried = CarriedVelocity;

            Suspend();
            rig.GoLimp(arg.P + carried, settled: false, drives: Drives);

            float down = arg.A > 0 ? arg.A / 1000f : downedSeconds;
            downUntil = Time.time + down;
        }

        // ── Getting back up ───────────────────────────────────────────────────

        private void Update()
        {
            if (!suspended) return;

            // Death outranks it. PlayerController's isDead is the authority on whether this player
            // has control, and a knockdown that landed on the same frame as the killing blow must
            // not stand the corpse back up.
            if (dead || (controller != null && controller.IsDead)) return;

            // Frozen out from under us by RagdollBudget — see AgentRagdoll for the same guard. On a
            // player this one is not cosmetic: leaving it suspended is leaving them unable to move.
            if (!rig.IsLimp)
            {
                Restore();
                return;
            }

            if (Time.time < downUntil || !rig.IsSettled) return;

            Restore();
        }

        private void LateUpdate()
        {
            if (!rig.IsLimp || cameraTransform == null || rig.Hips == null) return;

            // The offset is applied in a FIXED frame, not the hips'. Hanging it off the pelvis
            // means it inherits the tumble, and a camera that rolls with the body is the tumbling
            // first-person view this exists to escape — just further away.
            Vector3 target = rig.Hips.position + downedCameraFrame * downedCameraOffset;
            Vector3 lookAt = rig.Hips.position;

            float t = 1f - Mathf.Exp(-downedCameraLerp * Time.deltaTime);
            cameraTransform.position = Vector3.Lerp(cameraTransform.position, target, t);

            Vector3 toBody = lookAt - cameraTransform.position;
            if (toBody.sqrMagnitude > 1e-4f)
                cameraTransform.rotation = Quaternion.Slerp(cameraTransform.rotation,
                                                            Quaternion.LookRotation(toBody), t);
        }

        // ── Handing the body over and back ────────────────────────────────────

        private void Suspend()
        {
            if (suspended) return;
            suspended = true;

            // Recorded rather than assumed. A player can go limp while already frozen by something
            // else — mounted, mid-cutscene, or dead — and restoring a blanket "enabled" would hand
            // control back to a body that was never supposed to have it.
            colliderWasEnabled = bodyCollider != null && bodyCollider.enabled;
            if (bodyCollider != null) bodyCollider.enabled = false;

            if (body != null)
            {
                bodyWasKinematic = body.isKinematic;
                body.isKinematic = true;
            }

            if (!Drives) return;

            movementWasEnabled = movement != null && movement.enabled;
            lookWasEnabled = look != null && look.enabled;
            inputWasEnabled = controller != null && controller.Input != null
                              && controller.Input.enabled;

            if (movement != null) movement.enabled = false;
            if (look != null) look.enabled = false;

            // Killed at the source, not merely by disabling PlayerMovement: jump and dash arrive as
            // input EVENTS that PlayerMovement subscribes to in Start and never unsubscribes, so a
            // disabled component still leaves a limp player able to jump. PlayerController's own
            // death freeze documents the same trap.
            if (controller != null && controller.Input != null) controller.Input.enabled = false;

            DetachCamera();
        }

        private void Restore()
        {
            if (!suspended) return;
            suspended = false;

            rig.Recover();

            // Only ever switched back ON. PlayerController owns the death freeze and re-asserts it
            // from several places, so writing a recorded "false" back over one of those would be
            // this component quietly taking control away on a frame it was not asked to.
            if (bodyCollider != null && colliderWasEnabled) bodyCollider.enabled = true;
            if (body != null) body.isKinematic = bodyWasKinematic;

            if (!Drives) return;

            AttachCamera();

            if (movement != null && movementWasEnabled) movement.enabled = true;
            if (look != null && lookWasEnabled) look.enabled = true;
            if (controller != null && controller.Input != null && inputWasEnabled)
                controller.Input.enabled = true;
        }

        /// <summary>
        /// Move the camera out of the helmet and show the player their own body.
        ///
        /// <para>
        /// The head has to be un-hidden along with it. PlayerLook permanently draws this player's
        /// helmet and scarf as shadows-only for their OWN camera, because in first person those
        /// sit between the eye and the world — and it does so from a render callback subscribed in
        /// Start, which keeps running while the component is disabled. Left alone, a player looking
        /// at their own knocked-down body would find it headless.
        /// </para>
        /// </summary>
        private void DetachCamera()
        {
            if (look == null || look.playerCamera == null) return;

            cameraTransform = look.playerCamera.transform;
            cameraParent = cameraTransform.parent;
            downedCameraFrame = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            cameraLocalPosition = cameraTransform.localPosition;
            cameraLocalRotation = cameraTransform.localRotation;

            cameraTransform.SetParent(null, true);
            look.SetFirstPersonHidden(false);
        }

        private void AttachCamera()
        {
            if (cameraTransform == null) return;

            cameraTransform.SetParent(cameraParent, false);
            cameraTransform.localPosition = cameraLocalPosition;
            cameraTransform.localRotation = cameraLocalRotation;
            cameraTransform = null;

            if (look != null) look.SetFirstPersonHidden(true);
        }
    }
}
