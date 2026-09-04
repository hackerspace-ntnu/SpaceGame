using UnityEngine;
using SpaceGame.Characters;

namespace SpaceGame.Items
{
    /// <summary>
    /// The torch, worn on a forearm. Q or E switches it; taking the gauntlet off puts the world
    /// back in the dark.
    ///
    /// <para>
    /// This artifact replaced the helmet lamp rather than joining it. The
    /// <see cref="Flashlight"/> component itself is unchanged and still does all three of its
    /// layers — the URP spot, the long-throw shader globals and the beam volume — but it now hangs
    /// on this prefab's <c>Emitter</c>, at the mouth of the horn, instead of under the player's
    /// Main Camera. So the beam points where the *arm* points, which is the whole reason to wear a
    /// lamp on your wrist and the cost of it: the light and the crosshair are no longer the same
    /// direction, and the Point Down/Level/Up clips that raise a firing arm move the beam with
    /// them. That was chosen deliberately over a camera-aimed cone (2026-09-03).
    /// </para>
    ///
    /// <para>
    /// <b>Owner authority, and no replication of its own.</b> A torch is a fact about one player's
    /// body that every machine has to see, including machines that connect later — which is
    /// exactly what <see cref="PlayerViewNetwork"/>'s <c>netTorch</c> NetworkVariable already is.
    /// So this class does not send anything: the owner flips its own lamp,
    /// <see cref="PlayerViewNetwork"/> publishes what the lamp says, and every peer's copy of that
    /// player is switched to match. Presenting the toggle here instead would be a second answer to
    /// the same question, and it would leave a late joiner in the dark beside a lit player.
    /// </para>
    ///
    /// <para>
    /// The lamp is handed to <see cref="PlayerViewNetwork"/> in <see cref="OnEquipped"/> rather
    /// than found by it: a worn gauntlet is instantiated and parented in the same breath, and
    /// anything searching the player for a <see cref="Flashlight"/> before that has finished finds
    /// nothing and never looks again.
    /// </para>
    /// </summary>
    public class FlashlightGauntletArtifact : ToolItem
    {
        /// <summary>
        /// Owner, not Server. Nothing here is world state: the lamp this switches is the one on
        /// this player's own arm, and the switch's result reaches everyone else as replicated
        /// state rather than as an effect a server applies.
        /// </summary>
        public override UseAuthority Authority => UseAuthority.Owner;

        [Tooltip("The lamp itself — the Flashlight prefab nested on the Emitter at the horn's mouth. " +
                 "Its own Inspector holds the beam tuning; nothing here duplicates it.")]
        [SerializeField] private Flashlight lamp;

        [Tooltip("Mesh_Flashlight_Bulb. Darkened when the torch is off, so a gauntlet on someone's " +
                 "arm reads as switched off rather than glowing at nothing.")]
        [SerializeField] private Renderer bulb;

        [Tooltip("Emission the bulb shows while the torch is off. Not black: a cold filament still " +
                 "catches light, and a pure black hole in the dish reads as a missing part.")]
        [SerializeField] private Color bulbDark = new(0.08f, 0.07f, 0.055f);

        [Tooltip("Emission the bulb shows while the torch is on. Brighter than 1 on purpose — the " +
                 "dish is meant to bloom once the lamp is lit.")]
        [SerializeField] private Color bulbLit = new(3.2f, 2.85f, 2.2f);

        [Tooltip("The pose the body takes while the torch is LIT. This is the same pose the body " +
                 "uses to hold an item, which is what the arm needs anyway: forearm up and " +
                 "forward, pitching with the look, so the beam goes where you are looking. " +
                 "OneHanded is the ordinary item pose; Relaxed carries it lower.")]
        [SerializeField] private ItemGrip.HoldStyle litPose = ItemGrip.HoldStyle.OneHanded;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private MaterialPropertyBlock bulbPaint;
        private PlayerViewNetwork view;
        private PlayerAimRig aimRig;

        /// <summary>State key for the switch. Written into save files — never rename.</summary>
        private const string LitKey = "on";

        private void Awake()
        {
            // Serialized rather than searched, but a prefab can be mis-wired and a torch that does
            // nothing with a clean console is the exact failure this whole system is prone to.
            if (lamp == null)
                Debug.LogError("[FlashlightGauntlet] No lamp assigned — this gauntlet cannot light anything.", this);
        }

        private void OnEnable()
        {
            if (lamp != null) lamp.Switched += ShowLit;
        }

        private void OnDisable()
        {
            if (lamp != null) lamp.Switched -= ShowLit;
        }

        /// <summary>
        /// Hand the lamp to the thing that replicates it, and start dark.
        ///
        /// <para>
        /// Dark rather than "whatever the prefab said", because <see cref="RestoreItemState"/> runs
        /// straight after this and states the truth. On a peer the truth arrives instead from
        /// <c>netTorch</c>, which <see cref="PlayerViewNetwork.SetTorch"/> applies immediately.
        /// </para>
        /// </summary>
        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);

            view = holder != null ? holder.GetComponent<PlayerViewNetwork>() : null;
            if (view != null) view.SetTorch(lamp);

            aimRig = holder != null ? holder.GetComponent<PlayerAimRig>() : null;

            ShowLit(lamp != null && lamp.IsOn);
        }

        public override void OnUnequipped(GameObject holder)
        {
            base.OnUnequipped(holder);

            // Before the instance goes: the player's view still holds a reference to a lamp that is
            // about to be destroyed, and an owner who stops publishing rather than publishing FALSE
            // leaves every peer looking at a light on an arm with no gauntlet on it.
            if (view != null) view.ClearTorch(lamp);
            view = null;

            // Put the arm down on the way out, or a gauntlet taken off while lit leaves the body
            // holding a pose for a lamp that is no longer there.
            ShowLit(false);
            aimRig = null;
        }

        /// <summary>Owner-side. The lamp is this player's own; nobody else decides its state.</summary>
        protected override void Use()
        {
            if (lamp == null) return;

            lamp.Switch(!lamp.IsOn);
        }

        // ── Per-instance state ─────────────────────────────────────────────────
        //
        // One bit, and the one a player is most likely to notice missing: this is a game with a
        // night cycle, and a reload after dark used to put everyone back in the dark with a torch
        // they had already switched on. It lived in FlashlightSaveable on the player root while the
        // lamp was part of the player; now that the lamp is part of an ITEM, the item's own bag is
        // where it belongs — it travels with the gauntlet, including into a chest.

        public override void CaptureItemState(ItemState state)
        {
            base.CaptureItemState(state);
            if (state == null || lamp == null) return;

            // Off is the default and the common case; storing it would put a key in the record of
            // every gauntlet anyone has ever owned for the state a fresh one is already in.
            if (lamp.IsOn) state.Set(LitKey, true);
        }

        public override void RestoreItemState(ItemState state)
        {
            base.RestoreItemState(state);
            if (lamp == null) return;

            // OWNER ONLY, and this guard is load-bearing. A peer's copy of a worn slot arrives with
            // an empty bag — BodyEquipmentNetwork replicates the item id and clears the state on
            // every machine — so an ungated restore runs one frame after SetTorch has already
            // applied netTorch and switches a lit torch back off. LateUpdate would put it right
            // again, which is exactly the kind of one-frame flicker nobody can reproduce on
            // purpose. On a peer this bit is netTorch's to state, not the bag's.
            if (!OwnerIsLocal()) return;

            lamp.Switch(state != null && state.GetBool(LitKey));
        }

        /// <summary>
        /// Make the bulb agree with the lamp, on whatever machine this is.
        ///
        /// <para>
        /// Driven off <see cref="Flashlight.Switched"/> rather than off <see cref="Use"/>, so it is
        /// right no matter who did the switching: the owner pressing Q, a peer being told by
        /// <c>netTorch</c>, or a save restore. Use only ever happens on one machine of the several
        /// that can see this arm.
        /// </para>
        /// <para>
        /// A property block, not a material: the model's materials come out of the shared palette
        /// and are the same asset on every gauntlet in the world, so writing one would light every
        /// other player's bulb too.
        /// </para>
        /// </summary>
        private void ShowLit(bool lit)
        {
            PoseArm(lit);
            PaintBulb(lit);
        }

        /// <summary>
        /// Bring the arm up while the torch is lit, and drop it when it goes out.
        ///
        /// <para>
        /// The beam leaves along the forearm, so an arm hanging at the player's side lights their
        /// boots — the pose is what makes the lamp usable. It is the body's ordinary held-item
        /// pose rather than one of its own: the forearm ends up where a held item's would, which
        /// is exactly where the beam wants it.
        /// </para>
        /// <para>
        /// Only while LIT, which is the user's call and a good one — switching off puts the arm
        /// down, so the pose reads the lamp's state from across a cave. Anything actually in the
        /// hands still outranks it; <see cref="PlayerAimRig"/> resolves that.
        /// </para>
        /// </summary>
        private void PoseArm(bool lit)
        {
            if (aimRig == null) return;

            aimRig.SetTorchStyle(lit ? litPose : ItemGrip.HoldStyle.None);
        }

        private void PaintBulb(bool lit)
        {
            if (bulb == null) return;

            bulbPaint ??= new MaterialPropertyBlock();
            bulb.GetPropertyBlock(bulbPaint);
            bulbPaint.SetColor(EmissionColor, lit ? bulbLit : bulbDark);
            bulb.SetPropertyBlock(bulbPaint);
        }
    }
}
