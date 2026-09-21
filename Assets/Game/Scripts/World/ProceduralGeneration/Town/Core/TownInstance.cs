// A note on a placed object saying which slot of which group put it there.
//
// This is what lets Generate be pressed twice. Without it the second press has no way to tell that
// the hut now standing at (12, 40) is the same hut the recipe still asks for, so the only thing it
// could do is destroy everything and start again — and every destroyed object takes its save
// identity with it, permanently orphaning its records in every existing save file
// (Towns.md, Gotchas). With it, a second Generate pairs each new slot with the object already made
// from that prefab and simply moves it, so changing the spacing moves your buildings instead of
// replacing them.
//
// Deliberately not a Saveable of any kind: it holds authoring bookkeeping, not world state. The
// three fields are exactly the reuse key and nothing else, because anything more would be a second
// place for the recipe's truth to live.
using UnityEngine;

namespace SpaceGame.World.Towns
{
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public class TownInstance : MonoBehaviour
    {
        [Tooltip("Which of the recipe's lists placed this.")]
        public TownSection section;

        [Tooltip("Index into that list. -1 for the centrepiece, which has no group.")]
        public int groupIndex = -1;

        [Tooltip("The prefab this was made from. The identity half of the reuse key — an index " +
                 "alone would re-point at a different prefab the moment the array is reordered.")]
        public GameObject source;

        public TownReuseKey Key => new(section, groupIndex, source);
    }
}
