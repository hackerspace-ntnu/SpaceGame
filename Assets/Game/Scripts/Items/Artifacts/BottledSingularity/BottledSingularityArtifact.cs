// The bottled singularity: a squat sealed flask you throw, which opens where it lands, inhales
// everything loose within eight metres for a second and a half and then flings the lot back out.
// It does not know who threw it, and the comedy is entirely in the lack of exemptions — throw it
// short and you are part of the pile.
//
// WHO DOES WHAT.
//
//   • OnRequestUse — the OWNER, the one machine with a live camera. It reads AimProvider for where
//     the throw is going, reads the hand for where it leaves from, and rolls the one seed the
//     release's scatter is derived from. All three travel in the message; no other machine
//     re-decides any of them.
//   • Use — the SERVER, because the thing thrown is a collider in the shared world. That makes this
//     UseAuthority.Server, the default: GameServices.World.Spawn is server-only by contract, and
//     UseAuthority.Owner would run this on a client where the spawn is refused outright.
//   • Present — EVERY machine: the puff at the hand and the sound. Everything the bottle itself
//     does afterwards belongs to SingularityWell, which is a spawned object and replicates on its
//     own.
//
// WHAT MAY NOT BE WRITTEN TO. arg.A is the hotbar slot code, which the server reads back as its
// stale-slot guard — an item that put its own payload there is silently refused for every slot but
// the matching one. The seed goes in B, which is what B is for on a press.
//
// NO HOLD POSE. Every hold style on the Upper Body layer is a firearm aim clip, and this is a
// bottle held in a fist. The same call the Lightning Spell made, for the same reason.
using SpaceGame.Core;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Throws a bottled singularity along a closed-form arc. One charge per bottle.
    /// </summary>
    public sealed class BottledSingularityArtifact : ToolItem
    {
        [Header("Throw")]
        [Tooltip("Speed the bottle leaves the hand at, in metres per second. Read against this " +
                 "project's 18 m/s² gravity rather than 9.81, and against the well's eight-metre " +
                 "radius: from a standing player's hand a LEVEL throw carries about seven metres, " +
                 "so it lands inside its own reach and takes the thrower with it. Clearing the " +
                 "radius means lobbing it — about 12 m at twenty degrees up, 18 m at forty-five. " +
                 "That is the item working as designed and not a mis-tune: the decision the player " +
                 "makes is how far to commit, and there are no exemptions for whoever threw it.")]
        [SerializeField, Min(1f)] private float throwSpeed = 18f;

        [Tooltip("Where the bottle leaves from — the Marker_ThrowPivot empty on the model, which " +
                 "sits at the mouth of the fist. Falls back to the eye the aim came out of when " +
                 "unset, which throws from slightly too high rather than not at all.")]
        [SerializeField] private Transform throwPivot;

        [Header("Parts")]
        [Tooltip("The bottle that is actually thrown. A spawned world object, so it MUST be a " +
                 "registered network prefab: unregistered, it exists for the host and for nobody " +
                 "else, and single-player runs as a host of one so solo testing proves nothing.")]
        [SerializeField] private SingularityWell wellPrefab;

        [Tooltip("Puff at the hand as the bottle leaves it. Cosmetic; every machine plays it.")]
        [SerializeField] private ParticleSystem throwPuff;

        [Header("Budget")]
        [Tooltip("How many of this player's singularities may be open at once. A well is a sphere " +
                 "sweep every physics step, so a player who could keep a dozen running is the " +
                 "first performance question this artifact raises (GDC-L1-PERF-0004). The oldest " +
                 "is retired to make room rather than the throw being refused, so the item never " +
                 "goes quiet on a press the player already watched.")]
        [SerializeField, Min(1)] private int liveWellBudget = 2;

        /// <summary>
        /// A bottle in a fist. Posing the arms around it would put the character in a pistol aim,
        /// which is a lie about the item — see the file header.
        /// </summary>
        protected override bool UsesHoldPose => false;

        /// <summary>
        /// Owner-side, before the request leaves: the throw, described by the only machine that can
        /// honestly describe it. A peer's copy of this player has an AimProvider with no live camera
        /// behind it, and the server's would answer down the host's crosshair.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            // AimProvider, never a hand-rolled raycast off a transform: this ray already leaves the
            // eye of whatever view the holder is actually looking through, so a throw made from a
            // saddle goes where the rider is looking rather than into the animal's neck.
            Ray aim = aimProvider != null
                ? aimProvider.GetAimRay()
                : new Ray(transform.position, transform.forward);

            arg.P = throwPivot != null ? throwPivot.position : aim.origin;
            arg.R = Quaternion.LookRotation(aim.direction);

            // One roll, in B. The release's whole scatter is derived from it by pure static math,
            // so every machine fans the pile out the same way — the pattern GravelBlastMath and
            // DragonRocketFlight already use, and the reason nothing here calls Random again.
            arg.B = Random.Range(int.MinValue, int.MaxValue);
        }

        /// <summary>
        /// Authority side: the bottle exists.
        /// </summary>
        protected override void Use()
        {
            // No orientation means nobody filled the aim in — the default NetArg EquipmentController
            // sends on unequip or death, or a use with no aim provider. Throwing along a zero
            // quaternion would lob the bottle down the world's +Z from wherever the holder stands.
            if (!UseArg.HasOrientation) return;

            if (wellPrefab == null)
            {
                Debug.LogWarning("[BottledSingularity] No well prefab assigned, so the throw " +
                                 "produces nothing. Wire it on the item prefab.", this);
                return;
            }

            Vector3 velocity = UseArg.R * Vector3.forward * throwSpeed;

            GameObject spawned = GameServices.World.Spawn(wellPrefab.gameObject, UseArg.P, UseArg.R);
            if (spawned == null) return;

            if (!spawned.TryGetComponent(out SingularityWell well))
            {
                Debug.LogError($"[BottledSingularity] '{spawned.name}' has no SingularityWell, so " +
                               "it will never open and never expire.", spawned);
                return;
            }

            well.Begin(owner, UseArg.P, velocity, UseArg.B);

            RetireOverBudget();
        }

        /// <summary>
        /// Every machine, and immediately on the thrower's so the item never feels like it is
        /// waiting for a round trip. <c>useSoundId</c> is already played by <c>PlayUse</c>.
        ///
        /// The bottle's own flight, opening and release are NOT here: it is a spawned network
        /// object and draws itself on every machine off the launch parameters the server stamped.
        /// </summary>
        protected override void Present()
        {
            if (throwPuff != null) throwPuff.Play();
        }

        /// <summary>
        /// Keep this player's open wells inside their budget, oldest out first. Server-side, so the
        /// despawn reaches every machine as an ordinary spawn message.
        /// </summary>
        private void RetireOverBudget()
        {
            while (SingularityWell.CountFor(owner) > liveWellBudget)
            {
                SingularityWell oldest = SingularityWell.OldestOf(owner);
                if (oldest == null) return;

                oldest.Retire();
            }
        }
    }
}
