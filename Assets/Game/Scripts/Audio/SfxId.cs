namespace SpaceGame.Audio
{
    /// <summary>
    /// Every sound the game can ask for, named by what it means rather than by which asset plays it.
    ///
    /// <para>
    /// Call sites reference these; <see cref="AudioCatalog"/> decides which FMOD event each one
    /// resolves to. That indirection is the whole point — the catalog currently points many of
    /// these at the same handful of shipped events, and swapping in real audio later is an edit to
    /// one asset rather than a hunt through forty scripts.
    /// </para>
    ///
    /// <para>
    /// Values are explicit and grouped in hundreds. They are serialized into the catalog asset and
    /// into component fields, so a value must never be reused for a different meaning — retiring an
    /// entry means leaving its number burnt, not renumbering the ones after it.
    /// </para>
    /// </summary>
    public enum SfxId
    {
        None = 0,

        // ---- Player (100) ----
        PlayerFootstep = 100,
        PlayerJump = 101,
        PlayerLand = 102,
        PlayerLandHeavy = 103,
        PlayerDash = 104,
        PlayerHurt = 105,
        PlayerDeath = 106,
        PlayerRespawn = 107,

        // ---- Weapons (200) ----
        WeaponGunFire = 200,
        WeaponGunReload = 201,
        WeaponGunEmpty = 202,
        WeaponEnergyFire = 203,
        WeaponEnergyChargeLoop = 204,
        WeaponBallLightningChargeLoop = 205,
        WeaponBallLightningFire = 206,
        WeaponBallLightningArc = 207,
        WeaponMeleeSwing = 208,
        WeaponMeleeImpact = 209,
        WeaponEquip = 210,
        WeaponProjectileWhoosh = 211,

        // ---- Impacts and damage (300) ----
        ImpactFlesh = 300,
        ImpactMetal = 301,
        ImpactShield = 302,
        ImpactCritical = 303,
        ImpactExplosion = 304,
        ImpactProjectile = 305,

        // ---- NPCs and entities (400) ----
        NpcMumbleNeutral = 400,
        NpcMumbleFriendly = 401,
        NpcMumbleHostile = 402,
        NpcDialogBlip = 403,
        NpcDialogOpen = 404,
        NpcDialogClose = 405,
        EntityAggro = 406,
        EntityAlert = 407,
        EntitySearch = 408,
        EntityHurt = 409,
        EntityDeath = 410,
        EntityFootstep = 411,
        EntityAttack = 412,

        // ---- Interaction (500) ----
        InteractDoorOpen = 500,
        InteractDoorClose = 501,
        InteractLever = 502,
        InteractPickup = 503,
        InteractPickupMetal = 504,
        InteractDrop = 505,
        InteractWorkstationRepair = 506,
        InteractScannerDiscovery = 507,
        InteractDenied = 508,
        InteractPrompt = 509,
        InteractOxygenFillLoop = 510,
        InteractOxygenFilled = 511,

        // ---- Wings and flight (600) ----
        WingsDeploy = 600,
        WingsFlap = 601,
        WingsWindLoop = 602,
        WingsStall = 603,
        WingsFold = 604,

        // ---- Ship and vehicles (700) ----
        ShipEngineLoop = 700,
        ShipTakeoff = 701,
        ShipLanding = 702,
        ShipRepair = 703,
        ShipAlarm = 704,
        VehicleStep = 705,

        // ---- Ambience (800) ----
        AmbWindLoop = 800,
        AmbInteriorHum = 801,
        AmbThunder = 802,
        AmbAntigravity = 803,

        // ---- UI (900) ----
        UiHover = 900,
        UiPress = 901,
        UiBack = 902,
        UiError = 903,
        UiNotify = 904,

        // ---- Portals (1000) ----
        PortalSprayLoop = 1000,
        PortalPaintSplat = 1001,

        // ---- Ropes (1100) ----
        //
        // The lasso used to have exactly one sound: the item's `useSound`, pinned to event:/SFX/Hit
        // and played by UsableItem.PlayUse — which for an item whose press starts a WIND-UP fired
        // at the moment the button went down, and again on the press that dropped the rope. The
        // throw, the catch, the crack of the rope going taut and the coil-back were all silent.
        // These name the moments instead of the button.
        RopeTwirl = 1100,
        RopeThrow = 1101,
        RopeCatch = 1102,
        RopeSnap = 1103,
        RopeCoil = 1104,
        RopeHitch = 1105,
    }
}
