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
// ONE TRIGGER, TWO BARRELS. This gun is fired entirely from the Use button, and
// which barrel a shot comes out of is not a property of which button was
// pressed: the gun walks the two barrels itself, orange then blue then orange,
// so two clicks always leave two apertures open.
//
// It used to have a second trigger, and that is the whole reason this note
// exists. There is exactly one Use action in this project's input map, so the
// blue barrel was reached through an alternate-fire event the gun subscribed to
// itself — an InputAction built in code, bound from an item instance that is
// destroyed and rebuilt on every hotbar change, behind an ownership check that
// has to be right on the frame a networked player is restored. Every one of
// those can fail silently, and all of them look identical from the player's
// side: the left trigger works, the right one does nothing, and firing again
// simply MOVES the one aperture the gun has ever opened. A portal gun with one
// hole is not a portal gun.
//
// The cursor that decides the barrel lives on PortalPair, with the portals, not
// here — see there for why.
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

        [Tooltip("Seconds an aperture stays open before it irises shut. Both barrels. 0 would mean forever, which this gun does not offer — a portal you cannot get rid of is a hole in the level.")]
        [SerializeField] private float portalLifetime = 20f;

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
        [Tooltip("The two apertures, told apart by hue so a player can identify either end from across a room — including through the other one.")]
        [SerializeField] private Color primaryColour = new Color(1.00f, 0.54f, 0.12f);
        [SerializeField] private Color secondaryColour = new Color(0.18f, 0.72f, 1.00f);

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

        /// <summary>
        /// The trigger. Every shot comes through here, out of whichever barrel is due.
        ///
        /// The barrel is chosen here, on the shooter's machine, and travels in B. Every other
        /// machine reads it back out rather than deciding for itself, exactly as it does with the
        /// placement: two machines each keeping their own idea of "which barrel is next" would
        /// drift apart the first time one of them dropped a message, and then one player's orange
        /// portal would be another player's blue.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            int barrel = ChooseBarrel();

            arg.B = barrel;
            Aim(ref arg, barrel);

            // Only a shot that actually opens something moves the cursor on — see
            // PortalPair.PeekBarrel.
            if (arg.HasOrientation) CommitBarrel(barrel);
        }

        /// <summary>
        /// Which barrel this shot comes out of.
        ///
        /// Asked of the PLAYER's pair rather than answered from a field here, because the gun
        /// object does not live long enough to hold the answer: EquipmentController destroys and
        /// rebuilds the held item on every hotbar change, so a cursor kept on this instance would
        /// be back on the orange barrel every time the player scrolled the wheel — and a gun that
        /// always fires the same barrel can only ever have one aperture open.
        /// </summary>
        private int ChooseBarrel()
        {
            PortalPair pair = PortalPair.Of(owner);
            return pair != null ? pair.PeekBarrel() : PortalPair.Primary;
        }

        /// <summary>Owner-side only: record which barrel just went, so the next shot takes the other.</summary>
        private void CommitBarrel(int barrel)
        {
            PortalPair pair = PortalPair.Of(owner);
            if (pair != null) pair.CommitBarrel(barrel);
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

            // The lifetime is a property of the gun, not of the message: every machine opens its
            // own copy of the aperture from the same placement and starts the same clock, so the
            // two expire together without a second of it going over the wire.
            pair.Open(index, portalPrefab, position, rotation, host, portalSize,
                      index == PortalPair.Primary ? primaryColour : secondaryColour,
                      portalLifetime);
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
