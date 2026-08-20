using UnityEngine;
using SpaceGame.Characters;

namespace SpaceGame.Items
{
    /// <summary>
    /// ToolItem is for one-time use effects that don't need duration management.
    /// Examples: Heal potion, speed boost, teleport, damage, etc.
    /// These items execute their effect once and don't need cleanup.
    /// </summary>
    public abstract class ToolItem : UsableItem
    {
        /// <summary>
        /// Where the holder is pointing.
        ///
        /// Resolved on demand rather than cached in Use(), so it is also available in
        /// <see cref="UsableItem.OnRequestUse"/> — which is the only place an aim can honestly be
        /// read, because it is the only one that runs on the machine holding the camera. A peer's
        /// copy of a remote player has an AimProvider with no live camera behind it, so aimed
        /// items must report their result rather than recompute it.
        /// </summary>
        protected AimProvider aimProvider =>
            owner != null ? owner.GetComponent<AimProvider>() : null;

        // Tool items just override Use() (authority) and/or Present() (every machine).
        protected override void Use() { }
    }
}
