/// <summary>
/// ItemID is an enum that represents the different types of items that can be in the player's inventory.
/// Useful if you want to reference a specific item without having to reference the entire InventoryItem scriptable object,
/// such as in the case of the ship accepting scrap.
/// </summary>
public enum ItemId
{
    Undefined,
    Scrap,
    Cube,
    Sphere,
    AntiGravityPotion,
}