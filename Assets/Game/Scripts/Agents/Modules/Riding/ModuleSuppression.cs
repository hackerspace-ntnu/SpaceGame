// Switching an agent's modules off for a while, and giving back exactly what was taken.
//
// Two things park a vehicle's brain modules and neither owns them: MountModule while a player
// drives, MonowheelDriverGate while nobody is at a Strider wheel's tiller. Both follow one rule —
// take only what is on, give back only what you took — because a module is allowed to switch
// ITSELF off (DormantModule does, for good, the moment its wake finishes) and a restore that
// switched everything on undid that. The rule lives here so the two cannot drift apart.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents
{
    /// <summary>
    /// One owner's worth of switched-off modules. Suppress may be called repeatedly (it only adds
    /// what is still on); Restore hands back exactly those and forgets them.
    /// </summary>
    public sealed class ModuleSuppression
    {
        private readonly List<MonoBehaviour> taken = new();

        /// <summary>Whether this owner currently holds anything off.</summary>
        public bool IsApplied => taken.Count > 0;

        /// <summary>Switch off every module in <paramref name="modules"/> that is on, and remember it.</summary>
        public void Suppress(IEnumerable<MonoBehaviour> modules)
        {
            if (modules == null) return;

            foreach (MonoBehaviour module in modules)
            {
                // Already off, for reasons of its own (or already ours). Not ours to take, and so
                // not ours to give back — not recording it is what stops Restore switching it on.
                if (!module || !module.enabled) continue;

                module.enabled = false;
                taken.Add(module);
            }
        }

        /// <summary>Switch back on exactly the modules this owner switched off. A destroyed one is dropped.</summary>
        public void Restore()
        {
            foreach (MonoBehaviour module in taken)
                if (module) module.enabled = true;

            taken.Clear();
        }

        /// <summary>Drop the record without touching the modules, for a teardown where they are going away.</summary>
        public void Forget() => taken.Clear();
    }
}
