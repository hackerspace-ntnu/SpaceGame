Prefabs that `SaveablePrefabRegistry` must be able to reconstruct on load.

`SaveablePrefabRegistry.ResourcesFolder` is the literal `"Saveable"` and the registry populates
itself with `Resources.LoadAll<GameObject>(ResourcesFolder)`. This folder did not exist, so that
call returned an empty array: the registry held zero entries and every dynamically-spawned saved
object (dropped items, spawned vehicles) was silently lost on load, with `WorldSaveStore` reporting
that it could not resolve the saved prefab id.

Creating the folder is only half the fix. Every prefab carrying a `Saveable` component still has to
live in (or be variant-linked into) this folder. `Scripts/Core/Persistence/Editor/
SaveWiringValidator.cs` already scans this exact path and will list the prefabs that belong here.
