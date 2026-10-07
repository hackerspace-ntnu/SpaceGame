// Whether a menu-scope owner has gone without giving the scope back.
//
// The judgement is deliberately narrow. Only two states are read as abandoned — destroyed, and
// disabled — because those are the two a screen cannot be in while it is on screen. Anything else
// gets the benefit of the doubt: a guard that guessed would close the pause menu under somebody
// reading it, which is a worse outcome than the freeze it prevents.
//
// A non-Unity owner is never judged at all. Nothing can be read off a plain object, and every owner
// in the project today is a MonoBehaviour — but that must not become an assumption this file makes.
using UnityEngine;

namespace SpaceGame.Core.Safety
{
    public static class StuckScopeRule
    {
        public static bool IsAbandoned(object owner)
        {
            if (owner == null) return true;

            // Unity's null: a destroyed object compares equal to null while the C# reference is
            // still alive, which is exactly the state a leaked scope owner is in.
            if (owner is Object unityObject && unityObject == null) return true;

            if (owner is Behaviour behaviour) return !behaviour.isActiveAndEnabled;

            return false;
        }
    }
}
