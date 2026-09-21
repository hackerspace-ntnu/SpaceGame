// A TownRecipe saved as an asset, so several towns can share one set of settings.
//
// Entirely optional, and deliberately nothing more than a box round a TownRecipe. The ordinary way
// to build a town is to fill the settings in on the TownGenerator component itself; this exists for
// when you have three mining posts that must stay identical and do not want to edit three
// components. TownGenerator's editor moves settings in and out of one of these with a button, so
// starting inline and extracting an asset later costs nothing.
using UnityEngine;

namespace SpaceGame.World.Towns
{
    [CreateAssetMenu(fileName = "TownRecipe", menuName = "SpaceGame/World/Town Recipe")]
    public class TownRecipeAsset : ScriptableObject
    {
        public TownRecipe recipe = new();

        private void OnValidate() => recipe?.Normalize();
    }
}
