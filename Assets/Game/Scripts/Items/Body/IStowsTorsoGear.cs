namespace SpaceGame.Items
{
    /// <summary>
    /// A carrier whose rider's worn torso gear is put away while they ride it — the ornithopter an NPC's
    /// wing pack deploys IS the pack, so the folded one on the back must not show at the same time.
    /// Asked of the hierarchy (the rider is netcode-parented under it), so every machine, late joiners
    /// included, gets the same answer with nothing sent.
    /// </summary>
    public interface IStowsTorsoGear { }
}
