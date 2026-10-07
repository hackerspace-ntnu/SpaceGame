// Gives the game back when a menu that took it has gone.
//
// Seven screens claim GameplayMenuScope, all by passing `this`. Every one of them gives it back in a
// paired call at the end of a method — and every one of those pairs is broken by a throw in between,
// by a destroy, or by a scene load that takes the screen with it. What the player sees is the game
// frozen behind a free cursor with nothing on screen to close.
//
// It measures the outcome, the same bargain UnderTerrainGuard makes: it does not care which of those
// happened, only that somebody is holding the scope who cannot possibly still be using it.
//
// The grace period is not politeness, it is correctness. A screen legitimately spends a frame or two
// disabled during its own open and close animations, and releasing on the first sighting would close
// menus out from under people.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.Core.Safety
{
    public sealed class StuckScopeGuard : ISessionGuard
    {
        /// <summary>How long an owner must look abandoned before its claim is dropped.</summary>
        public const float GraceSeconds = 2f;

        private readonly Dictionary<object, float> abandonedFor = new();
        private readonly List<object> release = new();

        public string Name => "StuckScope";

        public void Check(float interval)
        {
            if (!GameplayMenuScope.IsActive)
            {
                abandonedFor.Clear();
                return;
            }

            release.Clear();

            foreach (object owner in GameplayMenuScope.Owners)
            {
                if (!StuckScopeRule.IsAbandoned(owner))
                {
                    abandonedFor.Remove(owner);
                    continue;
                }

                abandonedFor.TryGetValue(owner, out float elapsed);
                elapsed += interval;
                abandonedFor[owner] = elapsed;

                if (elapsed >= GraceSeconds) release.Add(owner);
            }

            // Collected first, released after: Exit mutates the set being walked.
            foreach (object owner in release)
            {
                // An error, not a warning. This guard repairing a session means a real bug happened
                // somewhere else, and the guard must never be the thing that hides it.
                Debug.LogError($"[StuckScopeGuard] '{Describe(owner)}' held the gameplay menu scope " +
                               $"for {GraceSeconds:F0}s after being destroyed or disabled. Releasing it " +
                               "so the player can move again — something failed to call " +
                               "GameplayMenuScope.Exit and that is the real bug.");

                GameplayMenuScope.Exit(owner);
                abandonedFor.Remove(owner);
            }

            release.Clear();
        }

        // The type name, because the instance's own name needs a live object to read it from and by
        // definition there may not be one.
        private static string Describe(object owner) =>
            owner == null ? "null" : owner.GetType().Name;
    }
}
