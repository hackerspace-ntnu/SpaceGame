# Artifacts
The Artifacts architecture makes it simple to add custom logic to a custom model to make a usable item by equipping and intearcting with "mouse1" after having picked it up and added it to your hotbar/inventory.

# How to make an artifact

1) Make a new gameobject by adding a new shape, the shape you can change to whatever model you want. Make it into a prefab, then go into the prefab and continue the steps under.
2) Add a `RigidBody` component. Make sure `useGravity` is checked and `isKinematic` is unchecked. This way, the item falls to the ground instead of floating, and is interactable, as non-kinematic bodies can be seen by raycasts.
3) Add these scripts:
   - `PickupableItem.cs`
   - `DropItemPhysics.cs`
4) For the `DropItemPhysics.cs` script, add the Rigidbody from step 2 as the `Rb` and the collider from the model in step 1 as the `Trigger Collider`
5) Make a new scriptable object by going into "Assets -> ScriptableObject -> Items -> Artifacts", then right click and go to "Create -> Items -> Item". 
6) Remember to name it correctly as you wish, then add the gameObject prefab as the `Item Prefab`. 
7) The `Icon` is the icon it displays in the inventory, to make the icon go to "Tools -> Inventory -> Icon Generator" at the top of the screen.
   1) Put a new camera into the scene and face it at the gameObject/artifact. When you are happy, put this camera into `Render camera`, this will be the actual image.
   2) Add the artifact prefab into `Prefab`
   3) (Optional) Add the scriptableObject from step 5 into `Inventory Item`
8) The scriptableObject made in step 5 should now be complete with a prefab, icon and name. Add this to the `PickupableItem.cs` script
9) Now you can pick up the item and add it to your inventory. To make it functional, make a new script that inherits either `ToolItem.cs` or `EffectItem.cs` based on your wished behaviour. These again implement the `UsableItem.cs` which applies your implemented logic correctly and lets you add a `UseCount` and `UseSound` which is how many times you can use the artifact before it is destroyed, and what SFX are played when used. Implement the inherited methods as you wish. Look at `AntiGravityPotion.cs` for inspiration.