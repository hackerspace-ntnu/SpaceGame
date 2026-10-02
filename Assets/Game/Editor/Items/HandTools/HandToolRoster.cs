using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>The carried tools, one row each. <see cref="HandToolBuilder"/> builds them.</summary>
    public static class HandToolRoster
    {
        public static readonly IReadOnlyList<HandToolSpec> All = new HandToolSpec[]
        {
            new HandToolSpec
            {
                Id = "Carry_Basket_Open", Title = "Open Basket", Category = "Carriers",
                Stance = CarryStance.Hang, HoldSize = 0.75f,
            },
            new HandToolSpec
            {
                Id = "Carry_Basket_Wicker", Title = "Wicker Basket", Category = "Carriers",
                Stance = CarryStance.Hang, HoldSize = 0.76f,
            },
            new HandToolSpec
            {
                Id = "Carry_Bucket_Metal", Title = "Metal Bucket", Category = "Carriers",
                Stance = CarryStance.Hang, HoldSize = 0.61f,
            },
            new HandToolSpec
            {
                Id = "Carry_Bucket_Wood", Title = "Wood Bucket", Category = "Carriers",
                Stance = CarryStance.Hang, HoldSize = 0.64f,
            },
            new HandToolSpec
            {
                Id = "Carry_Cart_Hand", Title = "Hand Cart", Category = "Carriers",
                Stance = CarryStance.Push, HoldSize = 3.5f, PackSize = 0.9f, Nudge = new Vector3(0f, 0.18f, 0f),
            },
            new HandToolSpec
            {
                Id = "Carry_Cart_Hover", Title = "Hover Cart", Category = "Carriers",
                Stance = CarryStance.Push, HoldSize = 3f, PackSize = 0.9f, Nudge = new Vector3(0f, 0.18f, 0f),
            },
            new HandToolSpec
            {
                Id = "Carry_Tank_Oil", Title = "Oil Tank", Category = "Carriers",
                Stance = CarryStance.Hang, HoldSize = 0.77f,
            },
            new HandToolSpec
            {
                Id = "Carry_Tank_Water", Title = "Water Tank", Category = "Carriers",
                Stance = CarryStance.Hang, HoldSize = 0.77f,
            },
            new HandToolSpec
            {
                Id = "Carry_Yoke", Title = "Yoke", Category = "Carriers",
                Stance = CarryStance.Hang, HoldSize = 1.58f, PackSize = 0.9f,
            },
            new HandToolSpec
            {
                Id = "Tool_Adze", Title = "Adze", Category = "Building",
                Stance = CarryStance.Wield, HoldSize = 0.85f,
                OnBelt = true, HangPoint = new Vector3(0f, -0.402f, 0.046f), HangDown = new Vector3(0f, 1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_Awl", Title = "Awl", Category = "Craft",
                Stance = CarryStance.Wield, HoldSize = 0.4f,
                OnBelt = true, HangPoint = new Vector3(0f, -0.152f, 0f), HangDown = new Vector3(0f, 1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_BaitPouch", Title = "Bait Pouch", Category = "Guard",
                Stance = CarryStance.Hang, HoldSize = 0.45f,
                OnBelt = true, HangPoint = new Vector3(0f, 0.13f, 0f), HangDown = new Vector3(0f, -1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_BandageRoll", Title = "Bandage Roll", Category = "Care",
                Stance = CarryStance.Hang, HoldSize = 0.16f,
                OnBelt = true, HangPoint = new Vector3(-0.048f, 0.108f, 0f), HangDown = new Vector3(0f, -1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_BoneSaw", Title = "Bone Saw", Category = "Craft",
                Stance = CarryStance.Wield, HoldSize = 0.8f,
            },
            new HandToolSpec
            {
                Id = "Tool_BowDrill", Title = "Bow Drill", Category = "Building",
                Stance = CarryStance.Wield, HoldSize = 0.69f,
                OnBelt = true, HangPoint = new Vector3(-0.075f, 0.368f, 0f), HangDown = new Vector3(0f, -1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_BrickMould", Title = "Brick Mould", Category = "Building",
                Stance = CarryStance.Hang, HoldSize = 0.45f,
            },
            new HandToolSpec
            {
                Id = "Tool_Broom", Title = "Broom", Category = "Maintenance",
                Stance = CarryStance.Staff, HoldSize = 1.81f, PackSize = 0.9f,
            },
            new HandToolSpec
            {
                Id = "Tool_Chisel", Title = "Chisel", Category = "Building",
                Stance = CarryStance.Wield, HoldSize = 0.59f,
                OnBelt = true, HangPoint = new Vector3(0f, -0.218f, 0.046f), HangDown = new Vector3(0f, 1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_Cleaver", Title = "Cleaver", Category = "Craft",
                Stance = CarryStance.Wield, HoldSize = 0.59f,
                OnBelt = true, HangPoint = new Vector3(0f, -0.205f, 0f), HangDown = new Vector3(0f, 1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_Club_Knotted", Title = "Knotted Club", Category = "Guard",
                Stance = CarryStance.Wield, HoldSize = 1.19f,
                UseAction = "Sword Strike",
            },
            new HandToolSpec
            {
                Id = "Tool_Club_Studded", Title = "Studded Club", Category = "Guard",
                Stance = CarryStance.Wield, HoldSize = 1.23f,
                UseAction = "Sword Strike",
            },
            new HandToolSpec
            {
                Id = "Tool_CookingPot", Title = "Cooking Pot", Category = "Craft",
                Stance = CarryStance.Hang, HoldSize = 0.41f,
            },
            new HandToolSpec
            {
                Id = "Tool_Crowbar", Title = "Crowbar", Category = "Fieldwork",
                Stance = CarryStance.Wield, HoldSize = 1.03f,
            },
            new HandToolSpec
            {
                Id = "Tool_Dibber", Title = "Dibber", Category = "Fieldwork",
                Stance = CarryStance.Wield, HoldSize = 0.6f,
                UseAction = "Plant Seedling",
                OnBelt = true, HangPoint = new Vector3(0f, -0.33f, 0f), HangDown = new Vector3(0f, 1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_ElectricHarpoon", Title = "Electric Harpoon", Category = "Hunting",
                Stance = CarryStance.Staff, HoldSize = 1.6f, PackSize = 0.9f,
            },
            new HandToolSpec
            {
                Id = "Tool_ElectricHarpoonGun", Title = "Electric Harpoon Gun", Category = "Hunting",
                Stance = CarryStance.Aim, PoseOverride = ItemGrip.HoldStyle.TwoHanded, HoldSize = 1.21f,
            },
            new HandToolSpec
            {
                Id = "Tool_FilterCloth", Title = "Filter Cloth", Category = "Care",
                Stance = CarryStance.Wield, HoldSize = 0.68f,
            },
            new HandToolSpec
            {
                Id = "Tool_FishingNet", Title = "Fishing Net", Category = "Guard",
                Stance = CarryStance.Staff, HoldSize = 2.48f, PackSize = 0.9f,
            },
            new HandToolSpec
            {
                Id = "Tool_FishingRod", Title = "Fishing Rod", Category = "Hunting",
                Stance = CarryStance.Staff, HoldSize = 2.61f, PackSize = 0.9f,
            },
            new HandToolSpec
            {
                Id = "Tool_Flask", Title = "Flask", Category = "Care",
                Stance = CarryStance.Hang, HoldSize = 0.32f,
                OnBelt = true, HangPoint = new Vector3(0f, -0.022f, 0f), HangDown = new Vector3(0f, -1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_FleshingScraper", Title = "Fleshing Scraper", Category = "Craft",
                Stance = CarryStance.Wield, ItemAlong = Vector3.right, HoldSize = 0.59f,
            },
            new HandToolSpec
            {
                Id = "Tool_Hammer", Title = "Hammer", Category = "Building",
                Stance = CarryStance.Wield, HoldSize = 0.74f,
                OnBelt = true, HangPoint = new Vector3(0f, -0.298f, 0.05f), HangDown = new Vector3(0f, 1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_HandAxe", Title = "Hand Axe", Category = "Building",
                Stance = CarryStance.Wield, HoldSize = 0.83f,
                UseAction = "Sword Strike",
                OnBelt = true, HangPoint = new Vector3(0f, -0.382f, 0.046f), HangDown = new Vector3(0f, 1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_HandDrill", Title = "Hand Drill", Category = "Fieldwork",
                Stance = CarryStance.Wield, HoldSize = 0.88f,
            },
            new HandToolSpec
            {
                Id = "Tool_HandPump", Title = "Hand Pump", Category = "Care",
                Stance = CarryStance.Aim, PoseOverride = ItemGrip.HoldStyle.Carry, HoldSize = 0.57f,
            },
            new HandToolSpec
            {
                Id = "Tool_HandSaw", Title = "Hand Saw", Category = "Building",
                Stance = CarryStance.Wield, HoldSize = 0.79f,
            },
            new HandToolSpec
            {
                Id = "Tool_HandScales", Title = "Hand Scales", Category = "Craft",
                Stance = CarryStance.Hang, HoldSize = 0.63f,
            },
            new HandToolSpec
            {
                Id = "Tool_Harpoon_Bone", Title = "Bone Harpoon", Category = "Hunting",
                Stance = CarryStance.Staff, HoldSize = 2.52f, PackSize = 0.9f,
                UseAction = "Spear Throw",
            },
            new HandToolSpec
            {
                Id = "Tool_Harpoon_Float", Title = "Float Harpoon", Category = "Hunting",
                Stance = CarryStance.Staff, HoldSize = 2.49f, PackSize = 0.9f,
                UseAction = "Spear Throw",
            },
            new HandToolSpec
            {
                Id = "Tool_Harpoon_Iron", Title = "Iron Harpoon", Category = "Hunting",
                Stance = CarryStance.Staff, HoldSize = 2.58f, PackSize = 0.9f,
                UseAction = "Spear Throw",
            },
            new HandToolSpec
            {
                Id = "Tool_HerbKnife", Title = "Herb Knife", Category = "Care",
                Stance = CarryStance.Wield, HoldSize = 0.46f,
                OnBelt = true, HangPoint = new Vector3(0f, -0.225f, 0f), HangDown = new Vector3(0f, 1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_HerdingCrook", Title = "Herding Crook", Category = "Fieldwork",
                Stance = CarryStance.Staff, HoldSize = 1.78f, PackSize = 0.9f,
            },
            new HandToolSpec
            {
                Id = "Tool_Hoe", Title = "Hoe", Category = "Fieldwork",
                Stance = CarryStance.Staff, HoldSize = 1.67f, PackSize = 0.9f,
            },
            new HandToolSpec
            {
                Id = "Tool_HookPole", Title = "Hook Pole", Category = "Guard",
                Stance = CarryStance.Staff, HoldSize = 2.4f, PackSize = 0.9f,
            },
            new HandToolSpec
            {
                Id = "Tool_HuntingKnife", Title = "Hunting Knife", Category = "Hunting",
                Stance = CarryStance.Wield, HoldSize = 0.56f,
                UseAction = "Stab",
                OnBelt = true, HangPoint = new Vector3(0f, 0.16f, -0.052f), HangDown = new Vector3(0f, 1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_Ladle", Title = "Ladle", Category = "Craft",
                Stance = CarryStance.Wield, HoldSize = 1.03f,
            },
            new HandToolSpec
            {
                Id = "Tool_Lantern", Title = "Lantern", Category = "Maintenance",
                Stance = CarryStance.Hang, PoseOverride = ItemGrip.HoldStyle.Torch, HoldSize = 0.35f,
            },
            new HandToolSpec
            {
                Id = "Tool_Lasso", Title = "Lasso", Category = "Hunting",
                Stance = CarryStance.Hang, HoldSize = 0.7f,
                UseAction = "Lasso Throw",
                OnBelt = true, HangPoint = new Vector3(0f, 0.05f, 0f), HangDown = new Vector3(0f, -1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_MagnetPole", Title = "Magnet Pole", Category = "Fieldwork",
                Stance = CarryStance.Staff, HoldSize = 1.9f, PackSize = 0.9f,
            },
            new HandToolSpec
            {
                Id = "Tool_Mallet", Title = "Mallet", Category = "Building",
                Stance = CarryStance.Wield, HoldSize = 1.05f,
            },
            new HandToolSpec
            {
                Id = "Tool_MeasuringStaff", Title = "Measuring Staff", Category = "Building",
                Stance = CarryStance.Staff, HoldSize = 1.97f, PackSize = 0.9f,
            },
            new HandToolSpec
            {
                Id = "Tool_MortarPestle", Title = "Mortar Pestle", Category = "Craft",
                Stance = CarryStance.Hang, HoldSize = 0.33f,
            },
            new HandToolSpec
            {
                Id = "Tool_OilCan", Title = "Oil Can", Category = "Maintenance",
                Stance = CarryStance.Aim, PoseOverride = ItemGrip.HoldStyle.Carry, HoldSize = 0.42f,
            },
            new HandToolSpec
            {
                Id = "Tool_OilRag", Title = "Oil Rag", Category = "Maintenance",
                Stance = CarryStance.Hang, HoldSize = 0.3f,
                OnBelt = true, HangPoint = new Vector3(0f, 0.078f, 0f), HangDown = new Vector3(0f, -1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_PatchKit", Title = "Patch Kit", Category = "Maintenance",
                Stance = CarryStance.Wield, HoldSize = 0.32f,
                OnBelt = true, HangPoint = new Vector3(0f, 0.01f, -0.046f), HangDown = new Vector3(0f, -1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_Pickaxe", Title = "Pickaxe", Category = "Fieldwork",
                Stance = CarryStance.Staff, HoldSize = 1.47f, PackSize = 0.9f,
            },
            new HandToolSpec
            {
                Id = "Tool_Pickaxe_Rust", Title = "Rust Pickaxe", Category = "Fieldwork",
                Stance = CarryStance.Staff, HoldSize = 1.47f, PackSize = 0.9f,
            },
            new HandToolSpec
            {
                Id = "Tool_PipeWrench", Title = "Pipe Wrench", Category = "Maintenance",
                Stance = CarryStance.Wield, HoldSize = 0.59f,
            },
            new HandToolSpec
            {
                Id = "Tool_Pliers", Title = "Pliers", Category = "Maintenance",
                Stance = CarryStance.Wield, HoldSize = 0.39f,
                OnBelt = true, HangPoint = new Vector3(0.032f, -0.122f, 0.005f), HangDown = new Vector3(0f, 1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_PlumbLine", Title = "Plumb Line", Category = "Building",
                Stance = CarryStance.Wield, HoldSize = 0.69f,
                OnBelt = true, HangPoint = new Vector3(0f, 0.427f, 0f), HangDown = new Vector3(0f, -1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_PoulticeBowl", Title = "Poultice Bowl", Category = "Care",
                Stance = CarryStance.Hang, HoldSize = 0.25f,
            },
            new HandToolSpec
            {
                Id = "Tool_Rake", Title = "Rake", Category = "Fieldwork",
                Stance = CarryStance.Staff, HoldSize = 1.67f, PackSize = 0.9f,
            },
            new HandToolSpec
            {
                Id = "Tool_RockHammer", Title = "Rock Hammer", Category = "Fieldwork",
                Stance = CarryStance.Wield, HoldSize = 0.73f,
                OnBelt = true, HangPoint = new Vector3(0f, -0.5f, 0f), HangDown = new Vector3(0f, 1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_SandBrush", Title = "Sand Brush", Category = "Maintenance",
                Stance = CarryStance.Wield, HoldSize = 0.95f,
            },
            new HandToolSpec
            {
                Id = "Tool_ScrapCutter", Title = "Scrap Cutter", Category = "Fieldwork",
                Stance = CarryStance.Staff, HoldSize = 1.47f, PackSize = 0.9f,
            },
            new HandToolSpec
            {
                Id = "Tool_Shield", Title = "Shield", Category = "Guard",
                Stance = CarryStance.Aim, HoldSize = 0.95f,
            },
            new HandToolSpec
            {
                Id = "Tool_ShortBow", Title = "Short Bow", Category = "Hunting",
                Stance = CarryStance.Staff, HoldSize = 1.45f, PackSize = 0.9f,
            },
            new HandToolSpec
            {
                Id = "Tool_Shovel", Title = "Shovel", Category = "Fieldwork",
                Stance = CarryStance.Staff, HoldSize = 1.79f, PackSize = 0.9f,
            },
            new HandToolSpec
            {
                Id = "Tool_Sickle", Title = "Sickle", Category = "Fieldwork",
                Stance = CarryStance.Wield, HoldSize = 0.77f,
                OnBelt = true, HangPoint = new Vector3(0f, -0.33f, 0f), HangDown = new Vector3(0f, 1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_SiftingPan", Title = "Sifting Pan", Category = "Fieldwork",
                Stance = CarryStance.Aim, PoseOverride = ItemGrip.HoldStyle.Carry, HoldSize = 0.73f,
            },
            new HandToolSpec
            {
                Id = "Tool_SignalFlag", Title = "Signal Flag", Category = "Guard",
                Stance = CarryStance.Staff, PoseOverride = ItemGrip.HoldStyle.Torch, HoldSize = 1.79f, PackSize = 0.9f,
            },
            new HandToolSpec
            {
                Id = "Tool_SignalHorn", Title = "Signal Horn", Category = "Guard",
                Stance = CarryStance.Wield, HoldSize = 0.65f,
                OnBelt = true, HangPoint = new Vector3(0f, 0f, -0.175f), HangDown = new Vector3(0f, -1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_SkinningKnife", Title = "Skinning Knife", Category = "Hunting",
                Stance = CarryStance.Wield, HoldSize = 0.35f,
                UseAction = "Stab",
                OnBelt = true, HangPoint = new Vector3(0f, 0.1f, -0.04f), HangDown = new Vector3(0f, 1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_Sling", Title = "Sling", Category = "Hunting",
                Stance = CarryStance.Hang, HoldSize = 0.67f,
                OnBelt = true, HangPoint = new Vector3(0f, 0.04f, 0f), HangDown = new Vector3(0f, -1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_Snare", Title = "Snare", Category = "Guard",
                Stance = CarryStance.Wield, HoldSize = 0.84f,
            },
            new HandToolSpec
            {
                Id = "Tool_Spear_Bone", Title = "Bone Spear", Category = "Hunting",
                Stance = CarryStance.Staff, HoldSize = 2.58f, PackSize = 0.9f,
                UseAction = "Stab",
            },
            new HandToolSpec
            {
                Id = "Tool_Spear_Stone", Title = "Stone Spear", Category = "Hunting",
                Stance = CarryStance.Staff, HoldSize = 2.46f, PackSize = 0.9f,
                UseAction = "Stab",
            },
            new HandToolSpec
            {
                Id = "Tool_Spindle", Title = "Spindle", Category = "Craft",
                Stance = CarryStance.Wield, HoldSize = 0.48f,
                OnBelt = true, HangPoint = new Vector3(0f, 0.273f, -0.019f), HangDown = new Vector3(0f, -1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_Splint", Title = "Splint", Category = "Care",
                Stance = CarryStance.Wield, HoldSize = 0.62f,
            },
            new HandToolSpec
            {
                Id = "Tool_Spyglass", Title = "Spyglass", Category = "Guard",
                Stance = CarryStance.Aim, HoldSize = 0.82f,
                OnBelt = true, HangPoint = new Vector3(0f, 0f, -0.47f), HangDown = new Vector3(0f, 0f, 1f),
            },
            new HandToolSpec
            {
                Id = "Tool_TanningPaddle", Title = "Tanning Paddle", Category = "Craft",
                Stance = CarryStance.Staff, HoldSize = 1.77f, PackSize = 0.9f,
            },
            new HandToolSpec
            {
                Id = "Tool_ThrowingNet", Title = "Throwing Net", Category = "Hunting",
                Stance = CarryStance.Hang, HoldSize = 1f,
                UseAction = "Lasso Throw",
            },
            new HandToolSpec
            {
                Id = "Tool_Trowel", Title = "Trowel", Category = "Building",
                Stance = CarryStance.Wield, HoldSize = 0.72f,
                UseAction = "Poke Ground",
                OnBelt = true, HangPoint = new Vector3(0f, -0.213f, 0.052f), HangDown = new Vector3(0f, 1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_WaterSkin", Title = "Water Skin", Category = "Care",
                Stance = CarryStance.Hang, HoldSize = 0.43f,
                OnBelt = true, HangPoint = new Vector3(0f, 0f, 0f), HangDown = new Vector3(0f, -1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_WateringCan", Title = "Watering Can", Category = "Fieldwork",
                Stance = CarryStance.Hang, HoldSize = 0.91f,
            },
            new HandToolSpec
            {
                Id = "Tool_Whetstone", Title = "Whetstone", Category = "Maintenance",
                Stance = CarryStance.Wield, HoldSize = 0.29f,
                OnBelt = true, HangPoint = new Vector3(0f, 0.16f, 0f), HangDown = new Vector3(0f, -1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_WireCutters", Title = "Wire Cutters", Category = "Maintenance",
                Stance = CarryStance.Wield, HoldSize = 0.38f,
                OnBelt = true, HangPoint = new Vector3(0.035f, -0.132f, 0.005f), HangDown = new Vector3(0f, 1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_Wrench_Open", Title = "Open Wrench", Category = "Maintenance",
                Stance = CarryStance.Wield, HoldSize = 0.56f,
                OnBelt = true, HangPoint = new Vector3(0f, -0.278f, 0f), HangDown = new Vector3(0f, 1f, 0f),
            },
            new HandToolSpec
            {
                Id = "Tool_Wrench_Ring", Title = "Ring Wrench", Category = "Maintenance",
                Stance = CarryStance.Wield, HoldSize = 0.56f,
                OnBelt = true, HangPoint = new Vector3(0f, -0.278f, 0f), HangDown = new Vector3(0f, 1f, 0f),
            },
        };
    }
}
