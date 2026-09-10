// The one thing the rest of the game talks to about coats.
//
// The Cryo Sprayer and the Storm Flask are consumers of this facade and know nothing
// about the field, the patches, the messages or the save record. Laying a new kind of coat, or a new
// way to lay one, is a call to one function here — that is the whole reason it exists.
//
// Every query answers sensibly with no field, no session and no coats: full grip, bare ground, and a
// spray that quietly does nothing. A scene with no surface coats in it is not a broken scene.
using UnityEngine;

namespace SpaceGame.Gameplay.Surface
{
    public static class SurfaceCoats
    {
        private static bool warnedNoField;

        // Statics outlive the world, the session and play mode. A warning flag left set would
        // silence the one line that says why nothing is being sprayed in the NEXT session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession() => warnedNoField = false;

        /// <summary>The session's field, or null. Prefer the calls below to reaching for it.</summary>
        public static SurfaceCoatField Field => SurfaceCoatField.Instance;

        /// <summary>True when there is a field to talk to at all.</summary>
        public static bool Exists => SurfaceCoatField.Instance != null;

        /// <summary>
        /// Lay a coat of <paramref name="kind"/> at <paramref name="point"/>.
        ///
        /// <para>
        /// <b>Call it on the deciding machine.</b> It is false — and does nothing — on a machine
        /// that does not decide, exactly like <c>StatusReceiver.Apply</c>: the server sprays and
        /// every machine draws. An artifact with <c>UseAuthority.Server</c> calls this from
        /// <c>Use()</c> and nothing from <c>Present()</c>.
        /// </para>
        /// <para>
        /// It is also false when the kind refuses the surface — ice sprayed on dry sand — so a
        /// caller that wants to know before it fires asks <see cref="CanCoat"/>.
        /// </para>
        /// <para>
        /// Spraying the same kind onto ground it is already on GROWS and REFRESHES that patch
        /// rather than laying a second one, which is what lets a held spray run at fifteen ticks a
        /// second without carpeting a chunk.
        /// </para>
        /// </summary>
        /// <param name="radius">Footprint in metres, or 0 for the kind's own dab size.</param>
        /// <param name="seconds">Lifetime, or 0 for the kind's own — which for ice is forever.</param>
        /// <returns>True when a coat was laid or refreshed.</returns>
        public static bool Spray(SurfaceCoatKind kind, Vector3 point, float radius = 0f,
                                 float seconds = 0f)
        {
            SurfaceCoatField field = SurfaceCoatField.Instance;
            if (field == null)
            {
                WarnNoField();
                return false;
            }

            return field.Spray(kind, point, radius, seconds);
        }

        /// <summary>
        /// Would a coat of <paramref name="kind"/> stick at <paramref name="point"/>? Ice is the
        /// only kind that ever says no, and it says no to anything that is not liquid or wet.
        ///
        /// For an item that wants to show the player what its spray is about to do before they
        /// commit to it (GDC-L1-SYS-0006) — a rule with no visible consequence is superstition.
        /// </summary>
        public static bool CanCoat(SurfaceCoatKind kind, Vector3 point)
        {
            SurfaceCoatField field = SurfaceCoatField.Instance;
            return field != null && field.CanCoat(kind, point);
        }

        /// <summary>
        /// End every coat covering <paramref name="point"/> within <paramref name="radius"/> — ice
        /// smashed, ground dried out. The deciding machine's call; returns how many it ended.
        ///
        /// <paramref name="kind"/> narrows it to one coat, which is usually what a caller means: a
        /// hammer breaks the ice it was swung at and has nothing to say about the puddle under it.
        /// </summary>
        public static int Break(Vector3 point, float radius, SurfaceCoatKind? kind = null)
        {
            SurfaceCoatField field = SurfaceCoatField.Instance;
            return field != null ? field.Break(point, radius, kind) : 0;
        }

        /// <summary>
        /// How much grip the coats leave a body standing at <paramref name="groundPoint"/>.
        ///
        /// <para>
        /// <b>Movement does not call this.</b> A mover asks <c>GroundGrip.For</c>, which asks every
        /// source — this field and the body's own Slick status alike — and takes the smallest,
        /// so a mover never learns which of them is the reason (GDC-L1-SYS-0005). This is here for
        /// things that want to know about the SURFACE specifically: a UI, a test, a creature
        /// deciding whether to walk round the puddle.
        /// </para>
        /// </summary>
        public static float GripAt(Vector3 groundPoint)
        {
            SurfaceCoatField field = SurfaceCoatField.Instance;
            return field != null ? field.GripAt(groundPoint) : GroundGrip.Full;
        }

        /// <summary>Is there a coat of <paramref name="kind"/> under <paramref name="point"/>?</summary>
        public static bool Has(SurfaceCoatKind kind, Vector3 point)
        {
            SurfaceCoatField field = SurfaceCoatField.Instance;
            return field != null && field.HasCoat(kind, point);
        }

        /// <summary>
        /// The coat on the surface at <paramref name="point"/> — the newest one where several
        /// cross — or null for bare ground.
        /// </summary>
        public static SurfaceCoatKind? KindAt(Vector3 point)
        {
            SurfaceCoatField field = SurfaceCoatField.Instance;
            return field != null ? field.KindAt(point) : null;
        }

        /// <summary>
        /// Said once, and worth saying: a spray that reaches no field is not an error anywhere in
        /// the code and looks exactly like a spray that worked.
        /// </summary>
        private static void WarnNoField()
        {
            if (warnedNoField) return;
            warnedNoField = true;

            Debug.LogWarning("[Coats] Nothing was coated: this session has no SurfaceCoatField. " +
                             "It belongs on the NetworkGameManager prefab, beside ChatNetwork — " +
                             "see SurfaceCoatField's own summary for why it has to be that object.");
        }
    }
}
