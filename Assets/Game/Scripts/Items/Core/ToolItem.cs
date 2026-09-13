using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// ToolItem is for one-time use effects that don't need duration management.
    /// Examples: Heal potion, speed boost, teleport, damage, etc.
    /// These items execute their effect once and don't need cleanup.
    /// </summary>
    public abstract class ToolItem : UsableItem
    {
        // Tool items just override Use() (authority) and/or Present() (every machine).
        protected override void Use() { }
    }
}
