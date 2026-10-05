// A herder: rides beside or behind the herd its formation drives, and goes after a stray.
//
// A drive has three kinds of member, and they already share one FormationModule id — NpcWorldSim
// keys every member of a group into the group's formation. The POINT RIDER leads that formation: its
// task list and GoalTravelModule choose the route, and this module passes on it. The LIVESTOCK are
// the members without this module: they follow the point rider as an ordinary formation, a loose
// mob. Every other member with this module is a HERDER, and this is what it does instead of walking
// a column slot:
//
//   STATION — the drag and flank positions of a cattle drive: an arc behind the herd (opposite the
//   point rider's heading), past the herd's edge by standOff. At its station it stops and watches
//   the herd, which claims the frame on purpose — standing guard IS the behaviour, and passing
//   would hand the body to FormationModule, which would walk it into the column.
//
//   FETCH — a head beyond strayRadius from the herd's centre is ridden round by the herder nearest
//   it, from the far side. The formation's own regroup brings the animal back; the herder is what
//   makes that read as herding.
//
// Social + 1: above FormationModule (15), below everything reactive, so a herder whose mount is
// shot at still flees or fights, and its rider still shoots from the saddle either way. With no
// livestock left it passes, and the herder rides on in the column behind the point rider.
//
// Reads FormationModule's public API only, decides on the authority alone (AgentController gates
// the tick) and holds no state worth saving: stations are recomputed from where the herd is.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.Agents
{
    [RequireComponent(typeof(FormationModule))]
    public class HerdingModule : BehaviourModuleBase
    {
        [Header("Stations")]
        [Tooltip("Width of the arc behind the herd the herders spread across, in degrees. 180 puts two " +
                 "herders level with the herd's middle; smaller tucks them in behind.")]
        [SerializeField] private float flankArc = 150f;

        [Tooltip("How far beyond the herd's edge a herder rides.")]
        [SerializeField] private float standOff = 9f;

        [Tooltip("Within this distance of its station a herder stops and watches the herd.")]
        [SerializeField] private float stationTolerance = 4f;

        [Tooltip("The herd is never treated as smaller than this, so a herd bunched on one spot still " +
                 "leaves its herders room.")]
        [SerializeField] private float minHerdRadius = 8f;

        [Header("Strays")]
        [Tooltip("A head this far from the herd's centre is a stray, and the nearest herder rides for it.")]
        [SerializeField] private float strayRadius = 26f;

        [Tooltip("How far beyond a stray, on the side away from the herd, its herder rides.")]
        [SerializeField] private float fetchStandOff = 7f;

        [Header("Pace")]
        [Tooltip("Walking pace while closing on a station, as a multiple of the walk. Above the herd's " +
                 "own catch-up so a herder is never left behind a marching herd.")]
        [SerializeField] private float closingSpeedMultiplier = 1.6f;

        [Tooltip("Farther than this from where it should be, the herder runs.")]
        [SerializeField] private float runDistance = 30f;

        [Header("Queries")]
        [Tooltip("Seconds between re-reading who is livestock and who is a herder. Membership changes " +
                 "only when somebody dies or is taken, so this need not be per frame.")]
        [SerializeField] private float rescanInterval = 1f;

        [Tooltip("How far off the NavMesh a station may fall and still be ridden to.")]
        [SerializeField] private float navSampleDistance = 8f;

        private FormationModule formation;

        // Instance-level: two herders ticking in one frame must not read each other's lists.
        private readonly List<FormationModule> members = new();
        private readonly List<Transform> livestock = new();
        private readonly List<HerdingModule> herders = new();
        private readonly List<Vector3> headPositions = new();
        private readonly List<Vector3> herderPositions = new();
        private float rescanTimer;

        private void Reset() => SetPriorityDefault(ModulePriority.Social + 1);

        private void Awake() => formation = GetComponent<FormationModule>();

        private void OnEnable()
        {
            rescanTimer = 0f;
            livestock.Clear();
            herders.Clear();
        }

        public override string ModuleDescription =>
            "Herds the livestock of this member's formation: holds a drag or flank station behind the " +
            "herd and rides after a stray. Passes on the member that leads the formation (the point " +
            "rider, whose task list routes the drive).\n\n" +
            "• Livestock — formation members without a HerdingModule\n" +
            "• flankArc / standOff — where the herders ride\n" +
            "• strayRadius — how far a head may wander before the nearest herder fetches it\n" +
            "• Priority Social + 1: above FormationModule, below anything reactive";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (formation == null || string.IsNullOrWhiteSpace(formation.FormationId) || formation.LeadsFormation)
                return null;

            rescanTimer -= deltaTime;
            if (rescanTimer <= 0f)
            {
                rescanTimer = rescanInterval;
                Rescan();
            }

            ReadPositions();
            if (headPositions.Count == 0) return null;

            int me = herders.IndexOf(this);
            if (me < 0) return null;

            Vector3 centre = HerdingMath.Centroid(headPositions);
            Vector3 target = ChooseTarget(centre, me);

            float distance = Flat(target - context.Position).magnitude;
            if (distance <= stationTolerance)
                return MoveIntent.StopAndFace(centre);

            if (NavMesh.SamplePosition(target, out NavMeshHit hit, navSampleDistance, NavMesh.AllAreas))
                target = hit.position;

            bool run = distance > runDistance;
            return MoveIntent.MoveTo(target, stationTolerance * 0.75f, run ? 1f : closingSpeedMultiplier, isRunning: run);
        }

        /// <summary>The stray's far side if this herder is the one nearest a stray, else its station.</summary>
        private Vector3 ChooseTarget(Vector3 centre, int me)
        {
            int stray = HerdingMath.StrayIndex(headPositions, centre, strayRadius);
            if (stray >= 0 && HerdingMath.NearestIndex(herderPositions, headPositions[stray]) == me)
                return HerdingMath.FetchPoint(headPositions[stray], centre, fetchStandOff);

            FormationModule leader = FormationModule.LeaderOf(formation.FormationId);
            Vector3 heading = leader != null ? leader.SmoothedHeading : transform.forward;
            float radius = HerdingMath.HerdRadius(headPositions, centre, minHerdRadius);
            float angle = HerdingMath.StationAngle(me, herders.Count, flankArc);

            return HerdingMath.StationPoint(centre, heading, radius, standOff, angle);
        }

        /// <summary>Split the formation into livestock and herders, in column order, the point rider in neither.</summary>
        private void Rescan()
        {
            livestock.Clear();
            herders.Clear();

            FormationModule.CollectMembers(formation.FormationId, members);
            FormationModule leader = FormationModule.LeaderOf(formation.FormationId);

            foreach (FormationModule member in members)
            {
                if (member == leader) continue;

                if (member.TryGetComponent(out HerdingModule herder)) herders.Add(herder);
                else livestock.Add(member.transform);
            }
        }

        private void ReadPositions()
        {
            headPositions.Clear();
            foreach (Transform head in livestock)
                if (head != null && head.gameObject.activeInHierarchy) headPositions.Add(head.position);

            herderPositions.Clear();
            for (int i = herders.Count - 1; i >= 0; i--)
                if (herders[i] == null || !herders[i].isActiveAndEnabled) herders.RemoveAt(i);
            foreach (HerdingModule herder in herders) herderPositions.Add(herder.transform.position);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        protected override void OnValidate()
        {
            SetMinPriority(ModulePriority.Social + 1);
            flankArc = Mathf.Clamp(flankArc, 0f, 300f);
            standOff = Mathf.Max(0f, standOff);
            stationTolerance = Mathf.Max(0.5f, stationTolerance);
            minHerdRadius = Mathf.Max(1f, minHerdRadius);
            strayRadius = Mathf.Max(minHerdRadius, strayRadius);
            fetchStandOff = Mathf.Max(0f, fetchStandOff);
            closingSpeedMultiplier = Mathf.Max(0.1f, closingSpeedMultiplier);
            runDistance = Mathf.Max(stationTolerance, runDistance);
            rescanInterval = Mathf.Max(0.1f, rescanInterval);
            navSampleDistance = Mathf.Max(0.5f, navSampleDistance);
        }
    }
}
