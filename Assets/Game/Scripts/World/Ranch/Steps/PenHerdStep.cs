using System.Text;
using UnityEngine;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Presentation;

namespace SpaceGame.World
{
    /// <summary>
    /// Round the ranch's wild herd up into its corral and shut the gate. Pair it with <see cref="RanchCue.Lassos"/> so
    /// the wrangler hands out the ropes as it begins (the herd itself is turned loose by an earlier step's
    /// <see cref="RanchCue.Herd"/>, so it is grazing when the crew ride up).
    ///
    /// <para>
    /// Met when every living wild one of the ranch stands inside the corral and the gate is shut. One that dies no
    /// longer counts, so a hunt gone wrong never leaves the quest waiting on an animal that cannot come; at least one must
    /// be penned. Count, gate and waypoint are all read from replicated positions and the gate's latch, on every machine.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Ranch/Pen Herd Step")]
    public class PenHerdStep : RanchStep
    {
        [Tooltip("Under the title: {0} penned, {1} in the herd.")]
        [SerializeField] private string count = "In the corral: <b>{0}/{1}</b>";

        [Tooltip("Under the count while the gate stands open.")]
        [SerializeField] private string gateOpen = "The gate is open";

        [Tooltip("Under the count once every one is in but the gate still stands open.")]
        [SerializeField] private string shutIt = "All in · <b>shut the gate</b>";

        public override bool IsMet(ObjectiveWorld world)
        {
            Ranch ranch = LoadedRanch;
            if (ranch == null || !ranch.GateShut) return false;

            ranch.CountHerd(ranch.GatePosition, out int penned, out int total, out _);
            return penned > 0 && penned == total;
        }

        public override string Status(ObjectiveWorld world)
        {
            Ranch ranch = LoadedRanch;
            if (ranch == null) return string.Empty;

            ranch.CountHerd(ranch.GatePosition, out int penned, out int total, out _);
            var text = new StringBuilder(string.Format(count, penned, total));
            if (!ranch.GateShut) text.Append('\n').Append(penned == total && total > 0 ? shutIt : gateOpen);
            return text.ToString();
        }

        /// <summary>The stray nearest this machine's player; once all are in, the gate.</summary>
        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position)
        {
            Ranch ranch = LoadedRanch;
            if (ranch == null)
            {
                position = site != null ? site.Range : default;
                return site != null;
            }

            Transform local = GameplayMenuScope.LocalPlayerTransform;
            ranch.CountHerd(local != null ? local.position : ranch.GatePosition, out _, out _, out RanchStock stray);
            position = stray != null ? stray.transform.position : ranch.GatePosition;
            return true;
        }
    }
}
