using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// One beat of the objective chain: what the visor calls it, what the lander says when the crew
    /// reach it, and the rule that finishes it. Each kind of step is a subclass; each step in the
    /// game is one asset of that kind.
    ///
    /// <para>
    /// <b>Stateless.</b> A step asset is read on every machine at once, so anything it needs to
    /// remember lives in <see cref="ObjectiveWorld"/> or <see cref="ObjectiveProgress"/>, never in a
    /// field here.
    /// </para>
    /// <para>
    /// <b>Every member says which side of the authority split it runs on.</b> The SERVER begins a
    /// step and decides when it is met; EVERY machine asks what to show, because the visor, the
    /// beacon and the briefing are presentation.
    /// </para>
    /// </summary>
    public abstract class ObjectiveStep : ScriptableObject
    {
        [Tooltip("Written into save files as the crew's place in the chain. NEVER rename it once a " +
                 "save exists — a record naming an id the chain no longer has restarts the chain.")]
        [SerializeField] private string id;

        [Tooltip("The objective line on the visor. Short and imperative.")]
        [SerializeField] private string title;

        [Tooltip("What the lander's computer says when the crew reach this step, one popup per line.")]
        [SerializeField, TextArea(2, 4)] private string[] briefing = Array.Empty<string>();

        [Tooltip("Turn each player's camera to the step's focus as the briefing starts — the " +
                 "damaged hull, the wreck on the horizon.")]
        [SerializeField] private bool lookAtFocus;

        public string Id => id;
        public string Title => title;
        public IReadOnlyList<string> Briefing => briefing;
        public bool LookAtFocus => lookAtFocus;

        // ── SERVER ──────────────────────────────────────────────────────────────

        /// <summary>
        /// SERVER, once, when the crew reach this step — never on a restore. The place to spawn
        /// what the step is about. False means "not yet" (the ground there has not streamed in, the
        /// ship does not exist yet) and is asked again next frame; it never means "never".
        /// </summary>
        public virtual bool TryBegin(ObjectiveWorld world) => true;

        /// <summary>SERVER, every frame while this is the current step, after it has begun.</summary>
        public abstract bool IsMet(ObjectiveWorld world);

        // ── EVERY machine ───────────────────────────────────────────────────────

        /// <summary>
        /// EVERY machine, every frame while this step is current and begun: watches THIS machine's
        /// own player and returns true once they have done their personal part. Only for steps each
        /// player must do themselves (see <see cref="ObjectiveWorld.EveryoneFinished"/>); the rest
        /// leave it false.
        /// </summary>
        public virtual bool TrackLocalPlayer(ObjectiveWorld world) => false;

        /// <summary>
        /// EVERY machine: lines shown under the title — a count, a checklist. May use TMP rich text.
        /// Empty for none.
        /// </summary>
        public virtual string Status(ObjectiveWorld world) => string.Empty;

        /// <summary>EVERY machine: where the crew should be heading, if anywhere.</summary>
        public virtual bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position)
        {
            position = default;
            return false;
        }

        /// <summary>
        /// EVERY machine: what the briefing turns the camera to, when <see cref="LookAtFocus"/> is
        /// set. The waypoint unless a step has something better to show.
        /// </summary>
        public virtual bool TryGetFocus(ObjectiveWorld world, out Vector3 position) =>
            TryGetWaypoint(world, out position);

        /// <summary>
        /// EVERY machine: where to stand a beacon that can be seen across the desert. Only for
        /// something lying in the open — never for a console inside the ship.
        /// </summary>
        public virtual bool TryGetBeacon(ObjectiveWorld world, out Vector3 position)
        {
            position = default;
            return false;
        }
    }
}
