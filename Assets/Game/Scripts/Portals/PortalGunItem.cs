// The gun.
//
// It is an ordinary artifact and rides the ordinary Use/Present split, so it
// replicates for the same reason every other artifact does and needs no sync
// component of its own:
//
//   • OnRequestUse — owner-side, the one machine holding a camera. It aims,
//     fits the aperture to the wall, and puts the resulting pose in the message.
//     No peer could recompute that: their copy of a remote player has an
//     AimProvider with no live camera behind it.
//   • Present — every machine. Throws the blob, and opens the aperture where the
//     message says. Both apertures therefore exist on every machine, which is
//     what lets a peer see somebody else's portals and walk through them.
//
// Authority is Owner because a portal shot changes nothing the server arbitrates
// — no damage, no spawn, no contested resource — and routing it through the
// server would put a round trip inside the feel of the trigger.
//
// The second barrel is the only unusual part. There is exactly one Use action in
// this project's input map and a portal gun needs two triggers, so the gun
// subscribes to the alternate-fire event itself and posts the SAME NetMsg.UseItem
// that EquipmentController would have, with the other barrel's index in B. The
// server's existing handler relays it onward untouched, so the blue portal takes
// the identical path to the orange one rather than growing a parallel one.
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    using SpaceGame.Portals;

    public sealed class PortalGunItem : ToolItem
    {
        [Header("Firing")]
        [Tooltip("How far the aperture can be placed.")]
        [SerializeField] private float maxRange = 120f;

        [Tooltip("Layers a portal can be cut into. Anything else fizzles.")]
        [SerializeField] private LayerMask surfaceMask = ~0;

        [Tooltip("On, a shot that cannot fit the whole aperture onto the surface fizzles. Off, it is placed where it was aimed anyway — which is what makes the gun work on terrain, rocks, crates and everything else a level is actually made of.")]
        [SerializeField] private bool requireCleanFit;

        [Tooltip("Size of the opening, in metres. Big enough to run through without lining yourself up, and to drive something through.")]
        [SerializeField] private Vector2 portalSize = new Vector2(2.4f, 3.4f);

        [Header("Parts")]
        [Tooltip("Where the blob leaves the horn. Falls back to this transform.")]
        [SerializeField] private Transform muzzle;

        [Tooltip("The aperture prefab, spawned once per barrel and then moved.")]
        [SerializeField] private Portal portalPrefab;

        [Tooltip("The blob thrown at the wall. Purely cosmetic — the pose is already decided.")]
        [SerializeField] private PortalProjectile projectilePrefab;

        [Header("Reservoirs")]
        [Tooltip("The renderer carrying the fluid materials. On the shipped model that is the whole gun body, and the two fluids are submeshes of it.")]
        [SerializeField] private Renderer bodyRenderer;

        [SerializeField] private string primaryMaterialName = "Mat_Emissive_Portal_Orange";
        [SerializeField] private string secondaryMaterialName = "Mat_Emissive_Portal_Blue";

        [Header("Charge")]
        [Tooltip("Off, the reservoirs are decoration and the gun never runs dry. On, a barrel below its shot cost fizzles.")]
        [SerializeField] private bool requiresCharge;

        [SerializeField, Range(0f, 1f)] private float chargePerShot = 0.22f;
        [SerializeField] private float rechargePerSecond = 0.28f;

        [Header("Colour")]
        [Tooltip("Both apertures are yellow, told apart by value rather than hue.")]
        [SerializeField] private Color primaryColour = new Color(1.00f, 0.76f, 0.10f);
        [SerializeField] private Color secondaryColour = new Color(1.00f, 0.91f, 0.54f);

        private static readonly int FillId = Shader.PropertyToID("_Fill");
        private static readonly int AgitationId = Shader.PropertyToID("_Agitation");

        /// <summary>The swing is the player's own; nothing here is the server's to arbitrate.</summary>
        public override UseAuthority Authority => UseAuthority.Owner;

        /// <summary>
        /// The aperture prefab this gun spawns.
        ///
        /// Exposed for the save system, which has to re-open a player's portals on load and has no
        /// gun to ask: the item is not equipped when a world is loaded, and may never be again in
        /// that session. Reading it off the gun's own prefab is what keeps "which aperture prefab is
        /// this game's" in exactly one place — the inspector field below — rather than duplicating
        /// the answer into a saver that would then silently disagree the day somebody re-authors it.
        /// </summary>
        public Portal PortalPrefab => portalPrefab;

        private readonly float[] charge = { 1f, 1f };
        private readonly float[] agitation = { 0f, 0f };
        private readonly int[] fluidSlot = { -1, -1 };

        private MaterialPropertyBlock fluidBlock;
        private PlayerInputManager subscribedInput;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Awake()
        {
            fluidBlock = new MaterialPropertyBlock();
            ResolveFluidSlots();
        }

        /// <summary>
        /// Find which submesh each fluid is, by material NAME.
        ///
        /// Not by index. The shipped FBX carries twelve materials in the order
        /// the Blender script declared them, but Unity's importer is free to
        /// reorder or drop unused slots, and a hard-coded index that silently
        /// becomes "the chrome bottle" would make the whole gun pulse orange
        /// with no error anywhere. Names are what the model actually promises.
        /// </summary>
        private void ResolveFluidSlots()
        {
            fluidSlot[PortalPair.Primary] = -1;
            fluidSlot[PortalPair.Secondary] = -1;

            if (bodyRenderer == null) return;

            Material[] materials = bodyRenderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null) continue;

                string materialName = materials[i].name;
                if (materialName.Contains(primaryMaterialName)) fluidSlot[PortalPair.Primary] = i;
                else if (materialName.Contains(secondaryMaterialName)) fluidSlot[PortalPair.Secondary] = i;
            }
        }

        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);

            // Only the machine that owns this player listens for a trigger. A
            // peer's copy of a remote player equips the same prefab, and a peer
            // subscribing to its own local input would fire that remote player's
            // gun from this machine's mouse.
            if (holder == null || !Network.Owns(holder.transform)) return;

            var input = holder.GetComponentInChildren<PlayerInputManager>(true);
            if (input == null) return;

            subscribedInput = input;
            subscribedInput.OnAltUsePressed += FireAlternate;
        }

        public override void OnUnequipped(GameObject holder)
        {
            base.OnUnequipped(holder);

            if (subscribedInput != null)
            {
                subscribedInput.OnAltUsePressed -= FireAlternate;
                subscribedInput = null;
            }
        }

        private void OnDestroy()
        {
            // OnUnequipped is not guaranteed: the item is destroyed outright when
            // the player dies or the slot empties, and a live subscription on a
            // destroyed MonoBehaviour throws inside the input manager's event.
            if (subscribedInput == null) return;

            subscribedInput.OnAltUsePressed -= FireAlternate;
            subscribedInput = null;
        }

        private void Update()
        {
            for (int i = 0; i < 2; i++)
            {
                if (requiresCharge)
                    charge[i] = Mathf.Min(1f, charge[i] + rechargePerSecond * Time.deltaTime);

                // Fast attack, slow release: the boil should hit on the frame
                // the trigger goes and then subside, which a symmetric lerp
                // turns into a gentle throb.
                agitation[i] = Mathf.Max(0f, agitation[i] - Time.deltaTime * 2.2f);
            }

            PushFluidState();
        }

        // ── Owner side: aim ────────────────────────────────────────────────────

        /// <summary>Primary trigger — the orange aperture.</summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            arg.B = PortalPair.Primary;
            Aim(ref arg, PortalPair.Primary);
        }

        /// <summary>
        /// Secondary trigger — the blue aperture, posted onto the same message
        /// the primary uses. See the class summary for why it is done here and
        /// not in EquipmentController.
        /// </summary>
        private void FireAlternate()
        {
            if (owner == null) return;

            // A = -1 deliberately: EquipmentController's server-side handler
            // only checks the slot when it is non-negative, and this path has no
            // hotbar index to offer. The item identity is already settled by
            // whatever the player is holding when the message lands.
            var arg = new NetArg { A = -1, B = PortalPair.Secondary };
            Aim(ref arg, PortalPair.Secondary);

            // Presented here and now, so the trigger never waits for the wire.
            PlayUse(owner, arg);

            // The server relays it to the peers; its own handler runs TryUse,
            // which this item does not use, and then broadcasts ItemUsed.
            this.NetToServer(NetMsg.UseItem, arg);
        }

        /// <summary>
        /// Work out where this shot lands and put it in the message.
        ///
        /// A fizzle is reported as a hit point with NO rotation. NetArg.R is
        /// all-zero in a default message and <see cref="NetArg.HasOrientation"/>
        /// exists precisely to tell "the sender filled this in" from "nobody
        /// did", so the miss case needs no flag of its own — and every machine
        /// still gets the impact point, and still splashes fluid on the wall.
        /// </summary>
        private void Aim(ref NetArg arg, int index)
        {
            arg.P = MuzzlePosition() + AimDirection() * maxRange;
            arg.R = default;

            if (requiresCharge && charge[index] < chargePerShot) return;

            if (aimProvider == null) return;

            Ray ray = aimProvider.GetAimRay();
            if (!Physics.Raycast(ray, out RaycastHit hit, maxRange, ~0,
                                 QueryTriggerInteraction.Ignore))
                return;

            arg.P = hit.point;

            // Best effort by default: an aperture that will not fit cleanly is
            // placed where it was aimed rather than refused. See
            // PortalPlacement.Fit — a gun that silently does nothing on most of
            // a real level is worse than one that sometimes puts a portal
            // somewhere slightly awkward.
            PortalPlacement.Result result =
                PortalPlacement.Fit(hit, portalSize, ray.direction, surfaceMask,
                                    requireFullSupport: requireCleanFit);

            if (!result.Valid) return;

            arg.P = result.Position;
            arg.R = result.Rotation;
        }

        // ── Every machine: throw the blob, open the aperture ───────────────────

        protected override void Present()
        {
            int index = Mathf.Clamp(UseArg.B, 0, 1);
            bool willOpen = UseArg.HasOrientation;

            agitation[index] = 1.6f;
            if (requiresCharge && willOpen)
                charge[index] = Mathf.Max(0f, charge[index] - chargePerShot);

            Vector3 impact = UseArg.P;
            Quaternion rotation = UseArg.R;
            Color colour = index == PortalPair.Primary ? primaryColour : secondaryColour;

            if (projectilePrefab == null)
            {
                // No blob authored — open immediately rather than doing nothing.
                // A missing cosmetic must never cost the gun its function.
                if (willOpen) OpenPortal(index, impact, rotation);
                return;
            }

            PortalProjectile blob = Instantiate(projectilePrefab, MuzzlePosition(),
                                                Quaternion.LookRotation(AimDirection()));

            // Captured rather than re-read: by the time the blob lands the
            // player may have fired again, and UseArg will describe that shot.
            GameObject shooter = owner;
            blob.Launch(MuzzlePosition(), impact, colour, () =>
            {
                if (willOpen) OpenPortal(index, impact, rotation, shooter);
            });
        }

        private void OpenPortal(int index, Vector3 position, Quaternion rotation,
                                GameObject shooter = null)
        {
            GameObject holder = shooter != null ? shooter : owner;
            if (holder == null || portalPrefab == null) return;

            // The wall is re-resolved here rather than sent, because a Collider
            // cannot travel in a message. Probing behind the aperture on each
            // machine finds the same wall the shooter fitted against, and a null
            // answer only costs traversal its collision pass-through.
            Collider host = null;
            Vector3 normal = rotation * Vector3.forward;
            if (Physics.Raycast(position + normal * 0.2f, -normal, out RaycastHit hit, 0.6f,
                                surfaceMask, QueryTriggerInteraction.Ignore))
                host = hit.collider;

            PortalPair pair = PortalPair.Of(holder);
            if (pair == null) return;

            pair.Open(index, portalPrefab, position, rotation, host, portalSize,
                      index == PortalPair.Primary ? primaryColour : secondaryColour);
        }

        // ── Presentation helpers ───────────────────────────────────────────────

        private void PushFluidState()
        {
            if (bodyRenderer == null) return;

            for (int i = 0; i < 2; i++)
            {
                int slot = fluidSlot[i];
                if (slot < 0) continue;

                bodyRenderer.GetPropertyBlock(fluidBlock, slot);
                fluidBlock.SetFloat(FillId, requiresCharge ? charge[i] : 0.86f - i * 0.12f);
                fluidBlock.SetFloat(AgitationId, agitation[i]);
                bodyRenderer.SetPropertyBlock(fluidBlock, slot);
            }
        }

        private Vector3 MuzzlePosition() =>
            muzzle != null ? muzzle.position : transform.position;

        /// <summary>
        /// Where the blob is thrown.
        ///
        /// The muzzle's own forward, not the aim ray, so the blob leaves the horn
        /// rather than the player's eye — but only when a muzzle is wired. The
        /// impact point is already decided either way, so this only affects the
        /// arc, which is exactly what it should affect.
        /// </summary>
        private Vector3 AimDirection()
        {
            if (muzzle != null) return muzzle.forward;
            if (aimProvider != null) return aimProvider.GetAimRay().direction;
            return transform.forward;
        }
    }
}
