// Assets/Game/Scripts/Core/Persistence/Runtime/SaveScopeHold.cs
using System;
using UnityEngine;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Keep a world-saved object out of the save for a while, and give it back exactly as it was. For a
    /// Sky nomad's flight (D4: a flier is not restored). The world store withholds it
    /// (<see cref="WorldSaveStore.Withhold"/>): the record an earlier capture wrote is dropped too, so a
    /// save taken mid-flight does not reload it where it took off. Only an object that IS world-scope is
    /// held: a group member is External already — its group record owns it — and handing it to the world
    /// store would save it twice and load it twice.
    /// </summary>
    public sealed class SaveScopeHold
    {
        private readonly Func<WorldSaveStore> storeSource;
        private SaveableEntity held;
        private WorldSaveStore heldIn;

        /// <summary>Held against the running SaveManager's world store.</summary>
        public SaveScopeHold() : this(() => SaveManager.Instance != null ? SaveManager.Instance.World : null) { }

        public SaveScopeHold(Func<WorldSaveStore> storeSource) => this.storeSource = storeSource;

        public bool Held => held != null;

        public void Hold(GameObject target)
        {
            if (held != null || target == null) return;
            if (!target.TryGetComponent(out SaveableEntity entity) || !entity.BelongsToWorld) return;

            // No store (no SaveManager: a disposable session) saves nothing, so there is nothing to withhold.
            heldIn = storeSource();
            heldIn?.Withhold(entity);
            held = entity;
        }

        public void Release()
        {
            if (held != null) heldIn?.Return(held);
            held = null;
            heldIn = null;
        }
    }
}
