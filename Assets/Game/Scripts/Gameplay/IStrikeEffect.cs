using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// What a strike prefab draws once it is told where the strike landed — a bolt falling out of
    /// the sky (<see cref="LightningBoltEffect"/>), thorns bursting out of the ground
    /// (<see cref="ThornEruption"/>).
    ///
    /// <para>
    /// The seam that lets one caster deliver different strikes: <see cref="LightningStrike.Present"/>
    /// spawns whatever prefab it is handed and tells the first one of these it finds both ends and
    /// the blast radius. A strike that only has a ground point ignores <paramref name="from"/>.
    /// </para>
    /// <para>
    /// Pure presentation, on every machine. The damage was decided elsewhere, on one.
    /// </para>
    /// </summary>
    public interface IStrikeEffect
    {
        /// <param name="from">Where the strike is drawn from — the sky above, or the caster's muzzle.</param>
        /// <param name="to">Where it lands, which is where the damage was billed.</param>
        /// <param name="radius">The blast radius the damage used, so the picture covers exactly what was hit.</param>
        void Strike(Vector3 from, Vector3 to, float radius);
    }
}
