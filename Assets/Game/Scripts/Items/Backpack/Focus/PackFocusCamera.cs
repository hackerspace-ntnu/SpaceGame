using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.Items
{
    /// <summary>
    /// The view used while rummaging in a deployed pack.
    ///
    /// <para>
    /// The pose is authored, not orbited: on the rig's centre axis, 2.46 m out on the side of it
    /// AWAY from the player, 1.5 m up — both in the ORIGINAL frame, so 2.58 and 1.575 m at the
    /// current <see cref="PackScale.Factor"/> — pitched 38&#176; down, at FOV 40, looking back down the
    /// player&#8594;pack line. The distance was 1.9 m until the 2026-08-25 board deepening pushed
    /// the leading edge from 0.94 m to 1.50 m out from the rig root and left the near cells
    /// 0.40 m from the lens, cropped out of the bottom of the frame; the lens moved back by
    /// exactly the growth — 0.56 m — so the near edge sits the 0.96 m from the lens it always
    /// did. The 38&#176; pitch aimed the camera at the rig's base by construction while
    /// atan(1.5 / 1.9) was 38.3&#176;; from 2.46 m it centred a half-step short of the base, on
    /// the mat itself, which is where the items are. FOV 40 is narrower than
    /// gameplay FOV on purpose: a narrow lens flattens perspective, which is what makes comparing
    /// two items' true sizes honest and makes a placement easy to judge.
    /// </para>
    /// <para>
    /// <b>Both distances ride <see cref="PackScale.Factor"/>, and the framing never moves with
    /// it.</b> They are offsets from the rig's own origin, so multiplying them by exactly what the
    /// rig, the faces and the gear are multiplied by makes every shot a similarity transform of
    /// the one before it: the same solid angle, the same pitch, the same field of view, every item
    /// filling the same fraction of the frame. That held when the factor went 1 -> 1.5 on
    /// 2026-09-01 (leaving the lens put would have had it a third of the way inside a 3.12 m mat)
    /// and it holds the same way at 1.5 -> 1.05. <b>So shrinking the rig does not make the pack
    /// smaller on screen in focus mode</b> — the camera comes in with it and the mat fills the
    /// frame exactly as it did. What changes is the rig in the WORLD: how big it is on the
    /// player's back and standing on the sand. Wanting the mat to read smaller in focus mode is a
    /// change to <see cref="PackFocusCamera"/>'s own numbers, not to the factor.
    /// </para>
    /// </summary>
    public sealed class PackFocusCamera : FocusCamera
    {
        // ── The authored pose ────────────────────────────────────────────────
        //
        // The shot is ON the player→pack axis, facing the pack square — from the FAR side of the
        // rig, looking back down that axis. The mat unfolds away from the player's feet, so a lens
        // on the player's own side of the pack frames the closed harness back and none of the
        // items; crossing over is what puts the mat, and everything laid out on it, toward the
        // camera.
        //
        // The player's body is consequently IN the shot, standing beyond the rig at
        // BackpackController.deployDistance + DistanceOut (6.6 m) from the lens, small in a 40°
        // lens and mostly behind the raised board. It reads as the owner kneeling over their pack
        // rather than as an obstruction, and nothing about the framing depends on the two
        // distances any more — the lens can no longer end up between the player and the pack.
        //
        // Both distances are offsets from the RIG's origin, so they take PackScale.Factor with the
        // rig. The angles do not, and must not: a similarity transform leaves every angle alone,
        // which is exactly why the enlarged pack frames identically from here.
        private static readonly float DistanceOut = PackScale.Apply(2.46f);
        private static readonly float HeightUp = PackScale.Apply(1.5f);
        private const float Pitch = 38f;
        private const float FieldOfView = 40f;

        // ── The arrival ──────────────────────────────────────────────────────
        //
        // The pack's own arc is 0.9 s (BackpackController.deploySeconds). Starting 0.15 s in and
        // taking 0.9 s means the camera settles a breath after the rig does. It deliberately does
        // NOT wait for the unfold to finish: this is an interaction performed hundreds of times a
        // session, and 1.4 s of nothing at the front of it is the difference between a pocket and
        // a cutscene.
        private const float Delay = 0.15f;
        private const float Seconds = 0.9f;

        private Transform rig;

        // Where the LENS looks: down the player→pack line, from beyond the pack back toward the
        // player. The reverse of what the caller hands over, reversed once on the way in.
        private Vector3 lensForward = Vector3.forward;

        /// <summary>
        /// Puts a focus camera on <paramref name="rig"/>.
        /// </summary>
        /// <param name="rig">The deployed pack. Tracked live rather than sampled once, because it
        /// is still mid-arc when this is called and lands under the camera as it arrives.</param>
        /// <param name="viewDirection">The player→rig line, flattened. The camera sits DistanceOut
        /// PAST the rig along it and looks back down it, so the mat faces the lens square-on and
        /// the player stands beyond the pack. Frozen at spawn so the shot cannot swing while the
        /// player's body drifts.</param>
        /// <param name="playerCamera">Switched off, with its AudioListener, for the duration.</param>
        public static PackFocusCamera Spawn(Transform rig, Vector3 viewDirection, Camera playerCamera)
        {
            if (rig == null) return null;

            var go = new GameObject("PackFocusCamera");
            var focus = go.AddComponent<PackFocusCamera>();

            focus.rig = rig;

            // Reversed here, once: the caller measures player→pack, the lens looks pack→player.
            var flat = new Vector3(viewDirection.x, 0f, viewDirection.z);
            focus.lensForward = flat.sqrMagnitude > 1e-6f ? -flat.normalized : Vector3.forward;

            focus.Begin(playerCamera);
            return focus;
        }

        protected override bool HasTarget => rig != null;

        /// <summary>
        /// Live rather than sampled once, because the rig is still travelling along its deploy arc
        /// when the camera spawns. Tracking it means the shot converges on the landing pose instead
        /// of framing the patch of sand the pack was over when the key was pressed.
        /// </summary>
        protected override Vector3 LensPosition() =>
            rig.position - lensForward * DistanceOut + Vector3.up * HeightUp;

        /// <summary>Frozen with <see cref="lensForward"/> at spawn, so unlike the position this does not move.</summary>
        protected override float LensYaw() => Quaternion.LookRotation(lensForward, Vector3.up).eulerAngles.y;

        protected override float PitchDown => Pitch;
        protected override float Fov => FieldOfView;
        protected override float FlyInDelay => Delay;
        protected override float FlyInSeconds => Seconds;
        protected override float FocusDistance() => Vector3.Distance(transform.position, rig.position);
    }
}
