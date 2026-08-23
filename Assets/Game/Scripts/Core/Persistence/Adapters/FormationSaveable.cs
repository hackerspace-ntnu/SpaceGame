using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists a caravan member's place in the column.
    ///
    /// <b>The slot is not stored anywhere — it is derived.</b>
    /// <c>FormationModule.FollowerIndexOf</c> answers "which slot am I?" with "where am I in the
    /// membership list?", and that list is built in <c>OnEnable</c> order. Nothing reproduces that
    /// order across a load, so a caravan reloads with its animals shuffled: the same column, in the
    /// same place, made of different beasts. The fix is a sort key rather than a fixed assignment,
    /// because membership genuinely changes — a member lost to a fight must let the column close up,
    /// not leave a permanent hole in it.
    ///
    /// <b>The heading and the seed go with it.</b> <c>smoothedHeading</c> is which way the group
    /// thinks it is facing, and restoring it stops the whole column swinging round on the first frame
    /// after a load. <c>memberSeed</c> is the member's fixed personal offset — it is
    /// <c>GetInstanceID()</c>, which is different every session, so without saving it every member's
    /// jitter and rest position changes on every reload even when the order is right.
    /// </summary>
    [RequireComponent(typeof(FormationModule))]
    public class FormationSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "formation";     // written into save files — NEVER rename

        private FormationModule formation;

        private FormationModule Formation =>
            formation != null ? formation : formation = GetComponent<FormationModule>();

        public string SaveKey => Key;

        public struct State
        {
            /// <summary>Place in the column, leader excluded. -1 for the leader or a stray.</summary>
            public int followerIndex;

            /// <summary>The group's travel direction as this member last measured or read it.</summary>
            public Vector3 smoothedHeading;

            /// <summary>
            /// Horizontal speed, which is how followers decide whether the group is marching or
            /// halted. Restoring it keeps a stopped caravan clustered instead of snapping into a
            /// travelling column for the frame before the leader's velocity is measured again.
            /// </summary>
            public float leaderSpeed;

            /// <summary>The member's fixed personal offset seed. Zero means "none recorded".</summary>
            public int memberSeed;
        }

        public object CaptureState()
        {
            if (Formation == null) return null;

            return new State
            {
                followerIndex = Formation.FollowerIndex,
                smoothedHeading = Formation.SmoothedHeading,
                leaderSpeed = Formation.LeaderSpeed,
                memberSeed = Formation.MemberSeed,
            };
        }

        public void RestoreState(JObject state)
        {
            if (Formation == null) return;

            if (state == null)
            {
                // -1 means "take whatever place registration gives you", which is the pre-save
                // behaviour and the right default. The seed is left alone: Awake already gave this
                // member one, and zeroing it would put every member on the same offset.
                Formation.RestoreFormationState(-1, Vector3.zero, 0f, false, 0);
                return;
            }

            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            Formation.RestoreFormationState(restored.followerIndex, restored.smoothedHeading,
                                            restored.leaderSpeed,
                                            restored.memberSeed != 0, restored.memberSeed);
        }
    }
}
