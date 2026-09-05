using System.Collections.Generic;
using SpaceGame.Agents;
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

        /// <summary>
        /// Everything currently holding this player down with no end time — a net, a tie, both at
        /// once. See <see cref="HoldDown"/>.
        ///
        /// <para>
        /// A set of holders rather than a flag, and it is the same shape <see cref="CarriedBody"/>
        /// uses for the same reason: two systems can want one body down, and the one that lets go
        /// first must not stand it up. A captor hands back the token it claimed with, so forgetting
        /// is a compile error rather than a captive who gets up on their own.
        /// </para>
        /// <para>
        /// Identity only — nothing is ever read off a holder. <see cref="object"/> rather than an
        /// interface so a captor needs to implement nothing at all to take part.
        /// </para>
        /// </summary>
        private readonly HashSet<object> holders = new HashSet<object>();

        /// <summary>Is anything holding this player down right now?</summary>
        private bool Held => holders.Count > 0;

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
        /// Is this player's body already somebody else's to move?
        ///
        /// <para>
        /// Asked of <see cref="CarriedBody"/> rather than of a mount, and that is the whole reason
        /// this answer is trustworthy. There is no single "am I mounted" flag on a player: a rider
        /// is normally parented into the saddle, so <c>GetComponentInParent&lt;MountModule&gt;()</c>
        /// would usually find it — but <c>MountModule.ParentRiderToMount</c> has a documented
        /// fallback that seats a rider WITHOUT parenting when netcode refuses the reparent, and a
        /// hierarchy check misses exactly that case. Both riding systems in this project register
        /// their claim here instead (<c>MountModule</c> for the saddle,
        /// <c>SeatedRider</c> for a ship's chair), on every path, parented or not.
        /// </para>
        /// <para>
        /// <b>Rigidly, not merely held, and the difference is a desync.</b>
        /// <c>UnderTerrainGuard</c> claims bodies through the same record while the ground under
        /// them loads — but the guard runs OWNER-ONLY, so that claim exists on the victim's machine
        /// and nowhere else. Refusing on it would have the server accept a capture and announce it,
        /// every peer put the body limp, and the victim's own machine alone refuse — and a player's
        /// transform is owner-authoritative, so the victim's answer is the one that wins. Limp on
        /// every other screen, walking around on their own, during ordinary chunk streaming. The
        /// seat and the saddle are replicated and produce the same answer everywhere, which is what
        /// makes them safe to refuse on; see <see cref="CarriedBody.IsCarriedRigidly"/>.
        /// </para>
        /// </summary>
        private bool IsCarried => CarriedBody.IsCarriedRigidly(gameObject);

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

            // Death outranks the hold, the way this file's header says it outranks every other
            // control owner. What is dropped is the hold's CLAIM, not the limpness: the corpse
            // stays down, it just stops being a captive the budget may not reclaim. Without this a
            // player netted at the moment they die keeps their place in RagdollBudget for the rest
            // of the session, and enough of them stop the budget bounding anything.
            //
            // Cleared directly rather than through ReleaseHold, and the difference is not cosmetic.
            // ReleaseHold releases ONE claim and would leave every other captor's standing; death
            // ends all of them at once. It is also the only writer that may empty the set without
            // being asked, which is why it does it here rather than by calling a method whose job
            // is to let somebody stand up.
            holders.Clear();
            rig.BudgetExempt = false;

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

            // Belt to OnDeath's braces. Unreachable today — death always clears the claims first,
            // and HealthComponent only raises this on a dead-to-alive transition — but the failure
            // if it ever were reachable is permanent and silent: Restore below calls rig.Recover,
            // which unregisters from the budget while leaving the claim set standing, and HoldDown
            // answers a stale claim rather than taking a fresh one. That body would be limp to
            // everything that asks and never netted again, for the rest of its life.
            holders.Clear();
            rig.BudgetExempt = false;

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

        /// <summary>
        /// Go limp and STAY limp until told otherwise.
        ///
        /// <para>
        /// Distinct from <see cref="OnKnockdown"/>, which recovers on its own timer, because the
        /// end of this one is not known when it starts: a captive is up when they have struggled
        /// out, and how long that takes is decided on the server against a pool being drained by
        /// their own inputs. Nothing about the duration can travel, so nothing tries to.
        /// </para>
        /// <para>
        /// The caller is <c>SnaredBody.Bind</c>, which every machine reaches: the deciding machine
        /// captures directly in <c>SnareReceiver.ResolveLandedNets</c> and the peers on hearing
        /// <c>NetMsg.Snared</c>. So this runs everywhere, and the Drives split then decides which
        /// half of the ragdoll plumbing this machine gets, exactly as for a knockdown.
        /// </para>
        /// <para>
        /// The caller owns the release. Until <see cref="ReleaseHold"/> runs, the rig is exempt
        /// from <c>RagdollBudget</c> and this component ignores every other reason to stand up.
        /// </para>
        /// </summary>
        /// <returns>
        /// True once the player is actually limp and held. FALSE means the hold did not take and
        /// the caller must not treat this body as held — it is dead, something else is already
        /// carrying it (see <see cref="IsCarried"/>), or the rig declined to go limp at all.
        /// <c>RagdollRig.GoLimp</c> returns without a word when the skeleton build kept no bones,
        /// and a caller that assumed otherwise would leave a player suspended with their input
        /// switched off and nothing in the console: the <c>!rig.IsLimp</c> rescue in
        /// <see cref="Update"/> is skipped while <c>held</c> is set, so nothing else would ever
        /// pick them back up.
        /// </returns>
        public bool HoldDown(object holder)
        {
            if (holder == null || dead) return false;

            // Somebody else already has this body down. Take a claim on it and say so: the work
            // below has been done, and doing it twice would record the suspended state as this
            // body's normal one.
            if (Held)
            {
                holders.Add(holder);
                return true;
            }

            // The player counterpart of AgentRagdoll's rider refusal, and it is the same hazard
            // seen from the other end: a rider is PARENTED into the saddle, so one that goes limp
            // in it is dragged wherever the animal walks, through the ground included — and on a
            // client that is a body the server does not own and cannot put back.
            if (IsCarried) return false;

            holders.Add(holder);

            // Read before Suspend, which switches the body kinematic underneath it — the same
            // ordering OnDeath and OnKnockdown spell out. Taken after, CarriedVelocity is a
            // confident zero and a captive netted mid-sprint drops as if switched off.
            Vector3 carried = CarriedVelocity;

            Suspend();
            rig.BudgetExempt = true;
            rig.GoLimp(carried, settled: false, drives: Drives);

            // Asked of the rig afterwards rather than pre-checked against HasSkeleton, so the
            // refusal covers every reason GoLimp can decline rather than only the one we thought
            // of. Everything this method did is undone, suspend included, or the refusal is worse
            // than the failure it is reporting.
            if (rig.IsLimp) return true;

            holders.Remove(holder);
            rig.BudgetExempt = false;
            Restore();
            return false;
        }

        /// <summary>
        /// Give up one claim. The player stands up only once the LAST one is given up — the same
        /// rule <see cref="CarriedBody.Release"/> follows, and for the same reason: a net that rots
        /// off a hogtied captive must not untie them.
        ///
        /// Safe to call with a token that was never claimed, or after death has cleared the set.
        /// </summary>
        public void ReleaseHold(object holder)
        {
            if (holder == null || !holders.Remove(holder)) return;
            if (Held) return;

            rig.BudgetExempt = false;

            // Not Restore() directly: Update owns the recovery, and it has to check IsSettled first
            // or a player released mid-tumble snaps upright out of a roll. Clearing downUntil is
            // what lets that check run on the very next frame.
            downUntil = 0f;
        }

        /// <summary>
        /// Is this player on the ground right now, by any route — a net, a tie, or a blast?
        ///
        /// Deliberately broader than the claim set: a player knocked flat by a repulsor blast is
        /// just as tieable as a netted one, and refusing that would make the two feel like
        /// unrelated systems.
        /// </summary>
        public bool IsHeldOrDown => Held || (rig != null && rig.IsLimp);

        // ── Getting back up ───────────────────────────────────────────────────

        private void Update()
        {
            if (!suspended) return;

            // Death outranks it. PlayerController's isDead is the authority on whether this player
            // has control, and a knockdown that landed on the same frame as the killing blow must
            // not stand the corpse back up.
            if (dead || (controller != null && controller.IsDead)) return;

            // A hold has no timer and no settle condition to wait for, so every reason to stand up
            // below is the wrong one. It has to sit ABOVE the budget rescue too, not just above the
            // timer: that branch restores the moment the rig stops being limp, and standing a
            // captive up because a corpse elsewhere took their place in the budget is the exact
            // defect BudgetExempt exists to close. Belt and braces — an exempt rig should never
            // reach it.
            if (Held) return;

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
