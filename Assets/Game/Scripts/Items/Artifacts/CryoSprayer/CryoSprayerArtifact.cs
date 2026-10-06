// The cryo sprayer.
//
// Hold Use and a plume of vapour comes out. Every body it washes over wears a film of frost at
// once, and holds the pose it was caught in once it has had enough cold to freeze solid.
//
// Neither of those is this item's work. StatusKind.Frozen owns the helplessness and the ten seconds
// it lasts; StatusKind.Slick owns the film on a body; SupplyReservoir owns the tank. What the
// sprayer owns is WHERE the cold lands, how long it has to stay there, and what the gun looks like
// putting it there — and nothing else. Every number the freeze and the film are made of is authored
// on FrozenStatus and SlickStatus, and this deliberately passes no duration and no radius so that
// it cannot disagree with them.
//
// THE GROUND TAKES NOTHING. The plume used to leave a disc of frost, or a sheet of ice over water,
// wherever its centre line landed. Those patches were a second weapon nobody had asked for: the
// player spraying a creature carpeted the ground they were both standing on, and the film is
// symmetric, so the discs took the holder's own footing away as readily as the target's
// (GDC-L1-BAL-0004). The cold is now a thing that happens to BODIES, which is the one effect the
// gun was reached for, and the ground reads as ground.
//
// WHAT DOES NOT GO ON THE WIRE. Nothing of the sprayer's own. arg.A is the hotbar slot on the press
// and on every hold tick and belongs to the server's stale-slot guard; arg.B's low bit belongs to
// EquipmentController's active flag. The sprayer needs neither: it writes only the aim ray (origin
// in P, rotation in R), and the status receiver sends its own facts.
using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay.Status;

namespace SpaceGame.Items
{
    /// <summary>
    /// A one-handed sprayer that freezes what it is pointed at.
    ///
    /// <para>
    /// <b>Authority is Server</b>, because a freeze is a condition on somebody else's body —
    /// contested, and not the holder's own (GDC-L1-MP-0004). Only the server applies the status.
    /// </para>
    /// <para>
    /// <b>The build-up is derived rather than replicated.</b> Freezing a body takes three quarters
    /// of a second of spray, and every machine has to see it coming or the moment a creature
    /// stops being a creature is one frame of substitution with no warning (GDC-L1-FEEL-0004). It
    /// needs no message: the aim ray already reaches the owner, the server and every peer on the
    /// ordinary hold stream, so each of them traces the same ray and reaches the same fraction
    /// within a frame of the others. That is the same argument <see cref="SupplyReservoir"/> makes
    /// for its tank and <c>LaserStaffArtifact</c> makes for its recharge. <see cref="FrozenBody"/>
    /// holds the fraction and draws it.
    /// </para>
    /// <para>
    /// <b>A telegraph the victim can act on is the anti-lock design</b> (GDC-L1-MP-0002). The rime
    /// creeps over them on their own machine for three quarters of a second before anything
    /// happens to them, breaking line of sight or leaving the plume's reach sheds it, and a body that
    /// is already frozen does not push its expiry out. The freeze itself deals no damage and cannot
    /// kill — see <see cref="StatusKind.Frozen"/> — so the whole of what it costs the victim is ten
    /// seconds they can see coming. The consequence of a hold is legible from the moment it starts,
    /// which is what makes it a fight rather than a click (GDC-L1-DESIGN-0006).
    /// </para>
    /// <para>
    /// <b>Anything the plume washes over freezes, at the same rate wherever it stands in it.</b>
    /// What the player sees leaving the barrel is the whole of what the gun does, so vapour
    /// visibly covering a creature and doing nothing — or doing a third of something — reads as a
    /// broken gun however precisely the crosshair rule is documented (GDC-L1-FEEL-0003: answer
    /// what the player meant, and the cone they can see is what they meant). Every body inside the
    /// cone chills at the authored rate, and it is a cone rather than a fan of sampled dabs so
    /// that the build-up cannot stall and restart for reasons nobody can see.
    /// </para>
    /// <para>
    /// <b>What aiming buys is the cone, not a bonus inside it.</b> The spread is wide enough that
    /// a target near the crosshair is covered and narrow enough that the plume is a spray rather
    /// than an area — the cost of that is deliberate and known: a body at the rim now freezes as
    /// fast as one dead ahead, which makes a held plume swept across a group a crowd freeze at a
    /// range the flamethrower cannot reach (GDC-L1-BAL-0004). The freeze deals no damage and
    /// cannot kill, which is what keeps that affordable. The spread itself is authored on
    /// <see cref="CryoSprayerNozzle"/>, which draws it.
    /// </para>
    /// </summary>
    public sealed class CryoSprayerArtifact : SprayerItem
    {
        [Header("Plume")]
        [Tooltip("How far the cold reaches, in metres. Three times the flamethrower's six, " +
                 "because the flame is a cone that catches a crowd at arm's length and this is a " +
                 "single line held on one target — the reach is what the narrowness buys. The " +
                 "visible plume is built to the same number.\n\n" +
                 "This value is SERIALIZED on the prefab, which is what actually ships. Changing " +
                 "the default here alone changes nothing in the game.")]
        [SerializeField] private float range = 18f;

        [Tooltip("What the plume can land on. Triggers are always ignored.")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Tooltip("Half the freezing cone's opening angle, in degrees. Must match the spread the " +
                 "plume is DRAWN at — CryoSprayerNozzle.openConeDegrees — or vapour washes over a " +
                 "creature and does nothing to it; OnValidate warns when the two drift apart.\n\n" +
                 "Everything inside it freezes at the same rate, so this angle IS the gun's " +
                 "accuracy: widening it is the difference between a spray and an area weapon.")]
        [SerializeField, Range(0f, 45f)] private float coneHalfAngle = 22f;

        [Tooltip("What stops the cold reaching a body. Set to nothing to let the plume freeze " +
                 "through walls, which is almost never what is wanted at eighteen metres.")]
        [SerializeField] private LayerMask sightBlockers = ~0;

        [Header("Freeze")]
        [Tooltip("Seconds of continuous spray on one target before it freezes solid. Short " +
                 "enough that landing a freeze is a shot rather than a siege, long enough that " +
                 "the victim's rime is a warning they can still break line of sight against " +
                 "(GDC-L1-MP-0002); how long the freeze then LASTS is FrozenStatus's own number, " +
                 "not this one. Serialized on the prefab — see the note on range.")]
        [SerializeField, Min(0.05f)] private float freezeSeconds = 0.75f;

        [Tooltip("What the ice on a frozen body is made of. Handed to every body this gun chills " +
                 "— see FrostLook.")]
        [SerializeField] private FrostLook frost = new FrostLook();

        /// <summary>
        /// The trace buffer. An instance field rather than a static one: two sprayers are one
        /// hotbar scroll apart, and a buffer shared between them would be a surprise waiting for
        /// the day somebody sprays from a vehicle with a second one in a turret.
        /// </summary>
        private readonly RaycastHit[] hits = new RaycastHit[16];

        /// <summary>
        /// The bodies the cone is on this sweep, one entry each. Reused between sweeps: this runs
        /// fifteen times a second for as long as the trigger is down.
        /// </summary>
        private readonly List<ConeBody> chilled = new List<ConeBody>();

        /// <summary>
        /// What the plume counts as a body, handed to <see cref="ConeSweep"/>. Cached because a
        /// method group becomes a fresh delegate at every call site it is written at, and this one
        /// is written at fifteen a second.
        /// </summary>
        private static readonly Func<GameObject, StatusReceiver> ResolveBody =
            StatusReceiver.EnsureOnBody;

        /// <summary>
        /// Sweep the plume and leave every body in the cone colder.
        ///
        /// <para>
        /// The cone and the centre line answer two different questions and are not alternatives.
        /// The cold comes from the cone, because the vapour the player watched wash over a creature
        /// is what they aimed (GDC-L1-FEEL-0003); the centre-line trace decides only where the
        /// landing burst is drawn, because a burst is one point and a cone has no one point.
        /// </para>
        /// <para>
        /// What counts as a body is <see cref="StatusReceiver.EnsureOnBody"/> — anything alive,
        /// anything loose, and anything somebody authored a receiver onto — which is the same rule
        /// the flame uses, and the reason a creature nobody remembered to tick a box on still
        /// freezes. Asked on EVERY machine and not only the authority: a receiver the server
        /// invented alone is a body that freezes for the server and nobody else, because the status
        /// arrives on that body's own relay and a relay with nothing subscribed drops it without a
        /// word.
        /// </para>
        /// </summary>
        protected override void Land(float elapsed)
        {
            if (owner == null) return;

            bool authority = Decides;

            ConeSweep.Bodies(RayOrigin, RayDirection, range, coneHalfAngle, hitMask, sightBlockers,
                             owner.transform.root, StatusReceiver.Of(owner), ResolveBody, chilled);

            foreach (ConeBody caught in chilled)
                Chill(caught.Body, elapsed, authority);

            if (!Trace(out RaycastHit hit))
            {
                if (Nozzle != null) Nozzle.SetLanding(false, Vector3.zero, false);
                return;
            }

            // The landing burst follows the centre line, which is where the player is looking. It
            // bites on a body — which is always freezing, being the least off-axis thing in the
            // cone — and blows off anything else, because ground is the one thing the plume now
            // leaves exactly as it found it (GDC-L1-SYS-0006: the refusal is shown, not explained).
            bool onBody = StatusReceiver.EnsureOnBody(hit.collider.gameObject) != null;

            if (Nozzle != null) Nozzle.SetLanding(true, hit.point, onBody);
        }

        /// <summary>
        /// Another <paramref name="elapsed"/> seconds of cold on one body, on every machine, and
        /// the film and the freeze on the one that decides.
        ///
        /// <para>
        /// Where in the plume the body stands buys it nothing: a body the vapour reaches at all
        /// freezes at the authored rate, and it wears the film from the first touch — which is
        /// what makes the graze a warning rather than a surprise.
        /// </para>
        ///
        /// <para>
        /// Neither status carries a duration or a magnitude: twenty seconds of film and ten of
        /// freeze are what those conditions are worth, and a sprayer does not get to decide them.
        /// The source is the HOLDER, which is what <c>ProvocationModule</c> reads to work out who a
        /// creature is now afraid of.
        /// </para>
        /// <para>
        /// <b>The film lands on the first touch and the freeze only after the build-up.</b> That
        /// ordering is the telegraph: the moment a body is grazed it is visibly slithering, which
        /// is the warning it has three quarters of a second to act on before the pose locks
        /// (GDC-L1-MP-0002). It also outlives the freeze by ten seconds, so a thaw hands the victim
        /// their body back on ground they still cannot brake on rather than at a standstill.
        /// </para>
        /// </summary>
        private void Chill(StatusReceiver body, float elapsed, bool authority)
        {
            FrozenBody frozen = FrozenBody.Ensure(body, frost);
            if (frozen == null) return;

            frozen.Chill(elapsed, freezeSeconds);

            if (!authority) return;

            // Reapplying refreshes the expiry rather than stacking, so a held plume simply keeps
            // the film topped up for as long as it is on the target. See StatusReceiver.
            body.Apply(StatusKind.Slick, source: owner.transform);

            if (frozen.Progress >= 1f)
                body.Apply(StatusKind.Frozen, source: owner.transform);
        }

        /// <summary>
        /// The nearest thing the plume can land on, skipping the holder's own body and the machine
        /// they are strapped into.
        ///
        /// <para>
        /// The ray starts at the holder's eye, inside the holder, so an unfiltered trace freezes
        /// the person spraying. <c>AimProvider.NearestOutside</c> is the shared rule for that and is
        /// static precisely so a trace like this one can borrow it; it also picks the nearest hit by
        /// hand, because <c>RaycastNonAlloc</c> neither sorts its buffer nor tells you when it
        /// truncated one. Occlusion needs no second test: a ray stops at the first thing in the way,
        /// so nothing behind a rock is ever reached.
        /// </para>
        /// <para>
        /// The carrier is the owner's transform ROOT. Mounting parents the rider under the mount,
        /// so the root IS the machine they are riding — and on their own feet the root is the owner
        /// themselves, which makes the second filter a repeat of the first rather than a case that
        /// has to be branched on.
        /// </para>
        /// </summary>
        private bool Trace(out RaycastHit hit)
        {
            int count = Physics.RaycastNonAlloc(new Ray(RayOrigin, RayDirection), hits, range,
                                                hitMask, QueryTriggerInteraction.Ignore);

            return AimProvider.NearestOutside(hits, count, owner.transform, owner.transform.root,
                                              out hit);
        }

        /// <summary>
        /// Keep the cone that freezes and the cone that is drawn in step.
        ///
        /// A warning rather than an assignment: the plume is authored on the prefab
        /// and the nozzle may be missing altogether on a stripped display copy, so silently taking
        /// the visual's angle would make what freezes depend on what happens to be on the prefab.
        /// Same rule, and the same reason, as the builder's reach check.
        /// </summary>
        protected override void OnValidate()
        {
            base.OnValidate();

            range = Mathf.Max(0.5f, range);

            CryoSprayerNozzle drawn = Nozzle != null
                ? Nozzle
                : GetComponentInChildren<CryoSprayerNozzle>(true);

            if (drawn != null && Mathf.Abs(drawn.OpenConeDegrees - coneHalfAngle) > 0.5f)
                Debug.LogWarning($"[CryoSprayer] The plume is drawn at {drawn.OpenConeDegrees}° and " +
                                 $"freezes at {coneHalfAngle}°. Vapour a player can see washing " +
                                 "over a creature has to freeze it.", this);
        }

        // No CaptureItemState / RestoreItemState override. The only thing this instance BECOMES is
        // its tank level, and UsableItem already writes the sibling reservoir's fill into the slot's
        // bag under SupplyCharge's own key — which is what makes it survive a save, a hotbar scroll,
        // a stow on the pack and a drop. Whether the trigger is down is not state: it is a button,
        // and a quicksave that loaded a sprayer already spraying would hand the player a press they
        // did not make. Frozen is not saved either, by StatusEffects' own decision, and the gun
        // leaves nothing in the world behind it that a save would have to remember.
    }
}
