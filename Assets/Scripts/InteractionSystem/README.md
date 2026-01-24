# User manual

To see an example, look at this scene: `Assets/Scenes/TestScenes/Marius test scene`

## IInteractable.cs

A simple interface for objects that can be interacted with. Should be implemented by Interaction classes such as `DoorInteraction.cs` which handles the actual logic.

`CanInteract()`
Should return a boolean of whether a gameobject can be interacted with or not

`Interact()`
Should return void and implement the logic of the actual interaction, i.e. rotating a door, spawning a bullet, applying forces (by delegating to correct object), etc.


## Interactor.cs

Class for detecting interactions. Should be added to objects that takes in player inputs, such as the player object itself.

`DoInteractionTest(out IInteractable interactable)`
Sends out a raycast and returns true if raycast detected an object with an Interactable object (object with a script implementing the IInteractable interface) or felse otherwise. If true is returned, it also outputs the Interactable object used in the Update method as described under.

`Update()`
Runs every frame to detect user inputs, for now it checks if a user clicked the button linked to the "Interact" Action. If the player did, it runs `DoInteractionTest(out IInteractable interactable)`. If the test returned true, it also returned an interactable object which the Interactor calls the `Interact()` method on, as implemented through the IInteractable interface.

## DoorInteraction.cs

Implements the `IInteractable.cs` interface and applies logic to the transform of the game object the script is attached to (and that has been interacted with, as detected by an interactor).

# Basic use

The `Interactor.cs` sends out a raycast from the player when they press a key mapped to an action. If the raycast hits an interactable object i.e. an object with a script - such as `DoorInteraction.cs` implementing the `IInteractable.cs` interface, check if it can be interacted with and if so, run the interaction logic of the object.

1) Attach the `Interactor.cs` script to the player prefab in the scene
2) Attach an `...Interaction.cs` script to a game object and implement the `IInteractable.cs` interface and its corresponding logic methods as described above. For instance, attach `DoorInteraction.cs` to the `prefab 99_Environment_Doors` (Assets/Models/Environment) and implement its logic. Finally, add a collider for this object.
