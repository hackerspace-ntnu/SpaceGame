# Inventory System


## Core systems
### `Inventory.cs`
An array of inventory slots. Contains pure logic for adding and removing items to the slots of this array.

### `InventoryComponent.cs`
A component that can be attached to a gameObject to give it an inventory. Connects the inventory data class to the game environment. Meant to be inherited to allow more specialized inventories.

### `InventorySlot.cs`
Contains the item. Can be empty or occupied.

### `InventoryItem.cs`
A scriptableObject representing the item stored in inventory. Contains item data such as name, icon and gameObject prefab reference.
Instances can be created in the editor and placed in the `ScriptableObjects/Items` folder.
___
## PlayerInventory
### `PlayerInventory.cs`
A subclass pf `InventoryComponent` meant to be attached to the player. Connects the hotbar, InventoryUI and equipmentcontroller to the player inventory.
### `HotbarController.cs`
Input handling for the hotbar. (Numbers 1-9 to select and G to drop the currently selected item)
### `EquipmentController.cs`
Handles spawning and despawning the currently equipped item in the player's hand.
### `DropItemPhysics.cs`
Handles the physics of thrown items when dropped from the inventory.


## Usage Guide
___
### Creating Inventory Items
1. Use the base class 'InventoryItem.cs' or create a new class of `ScriptableObject` that inherits from `InventoryItem` (e.g., `WeaponItem`, `ConsumableItem`, etc.).
Define item properties (e.g., name, icon, stats) in the new class.
2. Create instances of the new item type in the Unity Editor. Go to 'Create->Items->Item'
3. Create a new gameObject in the scene and attach the 'PickupableItem.cs' script. Assign the ScriptableObject to the 'item' field in the inspector.
4. Make the gameObject into a prefab, and assign the prefab in the ScriptableObject.
5. Also add a Rigidbody with `IsKinematic = true` (makes the object not react to physics while on the ground) and the `DropItemPhysics' script to the prefab so that you are able to throw the item.
