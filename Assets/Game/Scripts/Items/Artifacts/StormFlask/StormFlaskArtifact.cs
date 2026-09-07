// The storm flask.
//
// Aim at a patch of ground and use. A cloud gathers fifteen metres over it, rains for half a minute,
// and every two and a half seconds throws a bolt at whatever is tallest underneath — including
// whoever threw the flask. It has no idea who you are, and that is the point: standing next to your
// own weather on high ground is a way to be struck by it, and standing in a ditch while something
// towers over you is a way to make it useful (GDC-L1-BAL-0004 — situational strength, not raw power).
//
// WHO DOES WHAT.
//
//   • OnRequestUse — the OWNER, the one machine with a live camera. It reads AimProvider and puts
//     the aimed point in the message. No other machine re-decides it: Camera.main on the server is
//     the HOST's camera, so a recomputed aim would put every client's storm over the host's
//     crosshair.
//   • Use — the SERVER. A cloud is a spawned object that bills damage, and GameServices.World.Spawn
//     is server-only by contract, so this is UseAuthority.Server.
//   • Present — EVERY machine. Nothing but the sound, which UsableItem plays. The storm draws
//     itself off the replicated cloud, so there is no per-machine effect to start here and no
//     message per raindrop.
//
// WHAT THIS FILE DOES NOT DO. It does not decide how long the ground stays wet, what being on fire
// is, how a bolt is drawn, or how a cloud is shaped. All four belong to systems that already exist —
// the coat field, the status receiver, the Lightning Spell's VFX and StormCloud itself — and the
// flask's whole job is WHERE and WHETHER (GDC-L1-SYS-0005).
//
// NOTHING IS SPENT UNTIL THE WEATHER CHANGES. maxUses is -1 on the prefab and the charge is taken by
// calling Deplete() from the path that actually put a storm in the world, which is the codebase's
// pattern for an item spent by SUCCEEDING (see UsableItem.Deplete and PlaceableItem). With maxUses 1
// the click that caught open sky would eat the flask exactly as readily as the one that worked.
using SpaceGame.Characters;
using SpaceGame.Core;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// A sealed flask of weather. Uncork it and a storm stands over the point you aimed at.
    /// </summary>
    public sealed class StormFlaskArtifact : ToolItem
    {
        [Header("Placement")]
        [Tooltip("How far away a storm can be put, in metres. Short on purpose: a storm you can " +
                 "place across a valley is area denial with nothing at stake, and the design's " +
                 "tension is that you are standing near the thing you uncorked.")]
        [SerializeField, Min(1f)] private float range = 40f;

        [Tooltip("What the flask can put a storm over. Triggers are always ignored.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("The storm")]
        [Tooltip("The cloud this uncorks. A spawned world object that bills damage, so it MUST be " +
                 "a registered network prefab: unregistered, it exists for the host and for nobody " +
                 "else.")]
        [SerializeField] private StormCloud cloudPrefab;

        [Tooltip("How many storms may stand in the world at once, across every player. Over the " +
                 "budget the oldest is ended, so a flask is never refused — but a clearing cannot " +
                 "be filled with overlapping bolt timers either.")]
        [SerializeField, Min(1)] private int maxLiveClouds = 3;

        /// <summary>
        /// Server: a cloud is a spawned object and damage is contested world state, neither of
        /// which is the holder's own body.
        /// </summary>
        public override UseAuthority Authority => UseAuthority.Server;

        /// <summary>
        /// It is a bottle held in a fist, so it takes no upper-body pose.
        ///
        /// <para>
        /// Every hold style on the Upper Body layer is a firearm clip — <c>Relaxed</c>, despite the
        /// name, is <c>HumanM@Gun_Aim02</c>. On a flask that would read as the character taking aim
        /// down a sight with a bottle, which is both odd and a lie about the item: the storm lands
        /// where the crosshair is, from any posture. Dropping it leaves the arms on the Base Layer.
        /// </para>
        /// </summary>
        protected override bool UsesHoldPose => false;

        // ── Owner side: the aim ────────────────────────────────────────────────

        /// <summary>
        /// Where the storm goes, decided by the player who threw it.
        ///
        /// <para>
        /// <c>AimProvider</c>, and its filtered hit rather than a hand-rolled raycast: the ray
        /// already leaves the eye of whatever view the player is actually looking through — a
        /// saddle's, if they are riding — and is already filtered of their own body and of the
        /// machine they are strapped into.
        /// </para>
        /// <para>
        /// Zero means "aimed at open sky", and <see cref="Use"/> tests for it before spending
        /// anything. A miss read as a position is how the Lightning Spell used to strike the world
        /// origin — here it would put a storm over it.
        /// </para>
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            AimProvider aim = aimProvider;

            RaycastHit hit = default;
            bool aimed = aim != null && aim.TryGetAimHit(range, groundMask, out hit);

            arg.P = aimed ? hit.point : Vector3.zero;
        }

        // ── Server side: the storm exists ──────────────────────────────────────

        /// <summary>
        /// Authority-side. Put a storm over the aimed point, and pay for it only if one stood up.
        /// </summary>
        protected override void Use()
        {
            Vector3 ground = UseArg.P;
            if (ground == Vector3.zero) return;

            if (cloudPrefab == null)
            {
                Debug.LogWarning("[StormFlask] No storm cloud prefab assigned, so uncorking the " +
                                 "flask does nothing. Wire it on the item prefab.", this);
                return;
            }

            // The cloud's own height, read off the prefab rather than copied into a field here. The
            // height the cloud is placed at and the height its rain veil is scaled to are the same
            // number, and a local copy would silently disagree the day the storm is retuned.
            Vector3 plane = ground + Vector3.up * cloudPrefab.Height;

            GameObject spawned = GameServices.World.Spawn(cloudPrefab.gameObject, plane,
                                                          Quaternion.identity);
            if (spawned == null) return;

            if (!spawned.TryGetComponent(out StormCloud cloud))
            {
                Debug.LogError($"[StormFlask] '{spawned.name}' has no StormCloud, so it will never " +
                               "rain and never expire.", spawned);
                return;
            }

            cloud.Begin(owner);

            RetireOverBudget();

            // Spent, now that the world has actually changed. See the file header.
            Deplete();
        }

        /// <summary>
        /// Keep the world's live storms inside their budget, oldest out first.
        ///
        /// <para>
        /// Enforced on the server, so the despawn reaches every machine as an ordinary spawn
        /// message — and enforced by ending a storm rather than by refusing a flask, because the
        /// flask is a consumable somebody spent and the oldest storm has already had its run.
        /// </para>
        /// <para>
        /// Despawned, never hidden. A retired storm has to stop deciding as well as stop drawing,
        /// and a switched-off renderer would leave one that still swept the ground under it and
        /// still billed a bolt every two and a half seconds, invisibly.
        /// </para>
        /// </summary>
        private void RetireOverBudget()
        {
            while (StormCloudField.LiveCount > maxLiveClouds)
            {
                StormCloud oldest = StormCloudField.Oldest;
                if (oldest == null) return;

                oldest.Retire();
            }
        }

        // ── Per-instance state ─────────────────────────────────────────────────
        //
        // No CaptureItemState / RestoreItemState override. The flask is a sealed bottle right up
        // until it is uncorked, and an uncorked one is gone from the inventory in the same frame —
        // there is nothing this instance BECOMES for a state bag to hold. The storm it left behind
        // is thirty seconds of weather and is deliberately not saved either; see StormCloud.
    }
}
