// A rack of rockets you clamp to things and run away from — five of them, spent one clamp at a
// time, and the pack leaves the hotbar when the last one is on something.
//
// Aim at anything with a surface and press Use: the booster leaves your hand, clamps where the ray
// landed and lights. ANY surface takes a clamp — a crate, a hull, a creature, a wall, the sand.
// Thrust runs along the booster's own axis AS CLAMPED, so how you stick it on is the aim — one on
// the side of a crate slides it, one on the underside flies it, one on a cliff face burns and does
// nothing at all. Nothing here chooses a direction, and nothing here refuses a target; the rule is
// authored and the outcomes are the player's (GDC-L1-SYS-0002).
//
// UseAuthority.Server, because the whole effect is a thing appearing in the world and then moving
// somebody ELSE's body. What the server does NOT do is the push — see BoostedBody.
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Presentation;

namespace SpaceGame.Items
{
    /// <summary>
    /// The strap-on booster in the hand. Spawns and clamps the thing that does the work; see
    /// <see cref="BoosterMount"/> for the burn and <see cref="BoostedBody"/> for the push.
    /// </summary>
    public class StrapOnBoosterItem : ToolItem
    {
        /// <summary>
        /// Server. The booster is a real object in the world, stuck to a body the holder does not
        /// own — both halves of that are shared state, and exactly one machine may create it.
        /// </summary>
        public override UseAuthority Authority => UseAuthority.Server;

        [Header("Clamp")]
        [Tooltip("The booster as it exists in the world, clamped and burning. A REGISTERED network " +
                 "prefab: everybody has to see the thing that is about to launch their crate.")]
        [SerializeField] private GameObject clampedBoosterPrefab;

        [Tooltip("How far the clamp reaches, metres. Short: this is stuck on by hand, not thrown.")]
        [SerializeField, Min(0.5f)] private float range = 4f;

        [Tooltip("Slack added to the range when the SERVER re-checks it, metres. The holder measured " +
                 "from their eye and the server measures from their pivot, which sits about a metre " +
                 "lower, and by the time the request lands they have walked a little further. This " +
                 "is the difference between refusing a legitimate clamp on a laggy connection and " +
                 "accepting a clamp from across the map.")]
        [SerializeField, Min(0f)] private float serverRangeTolerance = 2.5f;

        [Header("Aim")]
        [Tooltip("Light the crosshair while the aim is on something a booster would actually MOVE. " +
                 "The clamp itself takes any surface, so this is the difference between a shove and " +
                 "a firework stuck to a cliff — and the player can see which they are about to get " +
                 "before spending the item.")]
        [SerializeField] private bool showAimHint = true;

        private CrosshairUI crosshair;

        /// <summary>
        /// Grab the local HUD once per equip, and only for the player actually holding this.
        ///
        /// A scene lookup, which is why it is not in Update — and gated on owning the item, because
        /// this prefab is instantiated on every machine and without the gate a remote player picking
        /// a booster up would light YOUR crosshair from across the desert.
        /// </summary>
        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);

            if (showAimHint && OwnerIsLocal()) crosshair = FindFirstObjectByType<CrosshairUI>();
        }

        public override void OnUnequipped(GameObject holder)
        {
            base.OnUnequipped(holder);
            crosshair = null;
        }

        private void Update()
        {
            if (crosshair == null || !OwnerIsLocal()) return;

            crosshair.SetAimHint(AimWouldLaunch());
        }

        /// <summary>
        /// Owner-side, before the request leaves. The only machine whose aim is honest: the
        /// server's camera is the HOST's, so a ray recomputed there would clamp every client's
        /// booster to whatever the host was looking at.
        ///
        /// <para>
        /// A refusal here costs no round trip and, more importantly, no booster — the item is spent
        /// only where the world actually changed, which is the rule <c>PlaceableItem</c> established
        /// for the same shape of problem.
        /// </para>
        /// <para>
        /// The SURFACE NORMAL travels in <c>R</c>, because it is the one fact the server cannot
        /// recover and the whole seating depends on it: the bell has to point out of the surface for
        /// the thrust to go into it. <c>P</c> carries the hit point and <c>Target</c> the body.
        /// <c>A</c> is untouched — it is the slot code, and the server reads it as its stale-slot
        /// guard.
        /// </para>
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            if (!AimWouldClamp(out RaycastHit hit, out Transform body)) return;

            arg.P = hit.point;


            // FromToRotation and not LookRotation: the commonest clamp in the game is onto the top
            // of something, where the normal is straight up — and LookRotation's default up vector
            // is straight up too, which is degenerate and answers with an error and an identity
            // rotation. Decoded on the far side as `R * Vector3.forward`.
            arg.R = Quaternion.FromToRotation(Vector3.forward, hit.normal);

            // No body is a clamp on the world, and the request carries no subject at all. Naming
            // one would mean naming the collider, which for terrain and chunk scenery is a
            // scene object no other machine can resolve.
            if (body != null) arg = arg.With(body.gameObject);
        }

        /// <summary>
        /// Authority only. Spawn the booster, clamp it, and let the charge be spent — in that
        /// order, so a clamp that could not be made costs the player nothing.
        ///
        /// <para>
        /// Every refusal below calls <c>CancelUse</c>. The charge is spent by SUCCEEDING: the pack
        /// holds a fixed number of boosters and a press that clamped nothing has not used one, so
        /// the count must not move on the click that missed as readily as on the one that stuck.
        /// </para>
        /// </summary>
        protected override void Use()
        {
            if (clampedBoosterPrefab == null)
            {
                Debug.LogWarning("[StrapOnBooster] clampedBoosterPrefab is not assigned, so there is " +
                                 "nothing to clamp.", this);
                CancelUse();
                return;
            }

            // Default R is all-zero, which is not a rotation. That is what the owner leaves behind
            // when their own aim found nothing worth clamping to.
            if (!UseArg.HasOrientation) { CancelUse(); return; }

            // Null is a clamp on the world — a wall, a rock, the sand. Only the RANGE is re-asked
            // here, because it is the only claim in the request a client could gain anything by
            // lying about.
            Transform body = BoosterClamp.BodyFor(UseArg.Resolve());

            if (!WithinReach(UseArg.P)) { CancelUse(); return; }

            BoosterShell prefabShell = clampedBoosterPrefab.GetComponent<BoosterShell>();
            if (prefabShell == null || !prefabShell.IsWired)
            {
                Debug.LogError("[StrapOnBooster] The clamped booster prefab has no BoosterShell with " +
                               "both markers assigned, so there is no way to tell which way it " +
                               "points. Wire Marker_Mount and Marker_Muzzle.", clampedBoosterPrefab);
                CancelUse();
                return;
            }

            Vector3 normal = UseArg.R * Vector3.forward;
            Quaternion seat = BoosterClamp.Seat(prefabShell.LocalExhaustAxis, normal);

            // The model's mounting face is laid ON the surface, so the booster's own origin sits
            // back from the hit by however far the model puts its face from its origin.
            Vector3 position = UseArg.P - seat * prefabShell.LocalMountPoint;

            GameObject booster = GameServices.World.Spawn(clampedBoosterPrefab, position, seat);
            if (booster == null) { CancelUse(); return; }

            if (!booster.TryGetComponent(out BoosterMount mount))
            {
                Debug.LogError("[StrapOnBooster] The clamped booster prefab has no BoosterMount, so " +
                               "it would sit in the world doing nothing.", clampedBoosterPrefab);
                GameServices.World.Despawn(booster);
                CancelUse();
                return;
            }

            // Clamped AFTER the spawn, never before: the clamp is published through the booster's
            // own NetworkVariables, and an object that is not spawned yet has nothing to publish
            // through.
            mount.Clamp(body, position, seat, owner);

            // Nothing else to do: reaching here without cancelling is what spends one charge, and
            // the last one takes the pack out of the hotbar through the default OnMaxUsesReached.
            // The count lives on `maxUses` rather than in a field of this class, so it is the count
            // ItemState already persists and the one the rest of the game can read.
        }

        /// <summary>
        /// Would a press right now put a booster on a surface? Any surface — the clamp refuses
        /// nothing, so this is the aim finding geometry and nothing more.
        ///
        /// <para>
        /// <paramref name="body"/> comes back null for the world itself: terrain, a settlement
        /// wall, a chunk rock. That is a clamp, not a miss.
        /// </para>
        /// <para>
        /// Through <c>AimProvider</c> and its RAY, never a hand-rolled raycast off the camera
        /// transform: it is already filtered of the holder's own body and of whatever they are
        /// riding, and it follows the view they are actually looking through rather than the one
        /// bolted to their head.
        /// </para>
        /// </summary>
        private bool AimWouldClamp(out RaycastHit hit, out Transform body)
        {
            hit = default;
            body = null;

            if (aimProvider == null || !aimProvider.TryGetAimHit(range, out hit)) return false;

            body = BoosterClamp.BodyFor(hit.collider != null ? hit.collider.gameObject : null);
            return true;
        }

        /// <summary>
        /// Would a press right now actually launch something?
        ///
        /// <para>
        /// What the crosshair hint answers, and deliberately a narrower question than the clamp:
        /// a booster sticks to a cliff face perfectly well and moves it not at all, and a hint
        /// that lit on every surface in the desert would tell the player nothing. Lit means "this
        /// one goes somewhere" (GDC-L1-UX-0004).
        /// </para>
        /// </summary>
        private bool AimWouldLaunch() =>
            AimWouldClamp(out _, out Transform body) && BoosterClamp.CanPush(body);

        private bool WithinReach(Vector3 point)
        {
            if (owner == null) return true;

            return Vector3.Distance(owner.transform.position, point) <= range + serverRangeTolerance;
        }
    }
}
