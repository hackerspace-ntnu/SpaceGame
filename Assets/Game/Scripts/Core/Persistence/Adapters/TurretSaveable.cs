using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists what a turret was shooting at, when it may shoot again, and where its gun was
    /// pointing.
    ///
    /// <b>The barrel is the part nothing else covers.</b> A turret's aim lives on <c>rotatingPart</c>,
    /// which is a CHILD transform, and <see cref="TransformSaveable"/> is only ever on the root. So a
    /// turret reloads pointing wherever the prefab pointed and slews round again from there, in full
    /// view, every time. It is captured as a LOCAL rotation, so a turret that was itself placed at an
    /// angle — bolted to a vehicle, dropped on a slope — comes back aimed correctly rather than
    /// aimed correctly for the origin.
    ///
    /// <b>The cooldown is the part that is unfair.</b> Every weapon in this game reloaded ready to
    /// fire, and a mortar is the worst case of it: a player can save under a hostile turret and
    /// reload into a shell that had no business being in the air yet, or reload repeatedly and never
    /// be shot at, depending which side of the barrel they are on.
    ///
    /// <b>Deferred, because a target is a reference.</b> The turret's target is another entity or a
    /// player, and neither reliably exists when the turret's own scene hydrates.
    ///
    /// Covers <see cref="RocketLauncherTurret"/> as well, which has no target and no aim of its own —
    /// its head is rebuilt every frame from two serialized angles — so for that one this is the
    /// firing clock and nothing else.
    /// </summary>
    public class TurretSaveable : MonoBehaviour, ISaveable, IDeferredSaveable
    {
        public const string Key = "turret";

        public string SaveKey => Key;

        public struct State
        {
            public bool hasTurret;
            public float retargetTimer;
            public float cooldownTimer;

            /// <summary>
            /// The barrel's rotation relative to the turret body. Only meaningful when
            /// <see cref="hasBarrel"/> — a turret with no rotatingPart assigned has no aim to keep.
            /// </summary>
            public bool hasBarrel;

            public Quaternion barrelLocalRotation;

            public SaveRef target;

            public bool hasLauncher;
            public float launcherCooldown;
        }

        private TurretModule turret;
        private RocketLauncherTurret launcher;
        private bool looked;

        private void Look()
        {
            if (looked) return;
            looked = true;
            turret = GetComponent<TurretModule>();
            launcher = GetComponent<RocketLauncherTurret>();
        }

        private SaveRef pendingTarget;
        private bool hasPendingTarget;

        public object CaptureState()
        {
            Look();
            if (turret == null && launcher == null) return null;

            var state = new State();

            if (turret != null)
            {
                state.hasTurret = true;
                state.retargetTimer = turret.RetargetTimer;
                state.cooldownTimer = turret.CooldownTimer;
                state.target = SaveRef.From(turret.CurrentTarget);

                Transform barrel = turret.RotatingPart;
                if (barrel != null)
                {
                    state.hasBarrel = true;
                    state.barrelLocalRotation = barrel.localRotation;
                }
            }

            if (launcher != null)
            {
                state.hasLauncher = true;
                state.launcherCooldown = launcher.CooldownTimer;
            }

            return state;
        }

        public void RestoreState(JObject state)
        {
            Look();

            hasPendingTarget = false;
            pendingTarget = SaveRef.None;

            if (state == null)
            {
                // At its defaults: no target, ready to acquire, ready to fire. The barrel is left
                // where it is — the record says nothing about it, and the prefab's own heading is
                // what the scene already put there.
                turret?.RestoreTurretState(0f, 0f);
                turret?.RestoreTarget(null);
                launcher?.RestoreCooldown(0f);
                return;
            }

            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            if (turret != null && restored.hasTurret)
            {
                turret.RestoreTurretState(restored.retargetTimer, restored.cooldownTimer);

                // Applied here rather than deferred: it needs no reference and no world, and waiting
                // would mean a frame of the gun visibly sitting at its authored heading.
                if (restored.hasBarrel) turret.RestoreBarrelRotation(restored.barrelLocalRotation);

                // Cleared, then re-resolved below. A turret that keeps whatever the live scene handed
                // it would be holding a target the record does not mention.
                turret.RestoreTarget(null);

                pendingTarget = restored.target;
                hasPendingTarget = pendingTarget.IsSet;
            }

            if (launcher != null && restored.hasLauncher)
                launcher.RestoreCooldown(restored.launcherCooldown);
        }

        public void OnLoadComplete()
        {
            if (!hasPendingTarget || turret == null) return;

            // Kept on failure: the target may be a player who has not rejoined. The turret is
            // meanwhile perfectly functional with no target — it re-acquires on its own timer — so
            // an unresolvable ref costs nothing but the aim it was already holding.
            if (!pendingTarget.TryResolve(out GameObject target)) return;

            hasPendingTarget = false;
            turret.RestoreTarget(target.transform);
        }
    }
}
