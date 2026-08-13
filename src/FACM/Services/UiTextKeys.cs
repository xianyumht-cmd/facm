namespace FACM.Services
{
    /// <summary>
    /// Stable identifiers for user-visible FACM copy.
    ///
    /// Key names are a compatibility contract: default Chinese copy may evolve, but an existing key
    /// must not be renamed just because the visible wording changes. ui-text.ini [Text] overrides use
    /// these identifiers, while [Replace] remains a legacy/global compatibility layer.
    /// </summary>
    internal static class UiTextKeys
    {
        // Existing public configuration contract. Keep these string values stable.
        public const string AppName = "AppName";
        public const string ControlCenter = "ControlCenter";
        public const string Cleanup = "Cleanup";
        public const string ToolGroup = "ToolGroup";
        public const string ToolA = "ToolA";
        public const string Mode1 = "Mode1";
        public const string Mode2 = "Mode2";
        public const string Mode3 = "Mode3";
        public const string Mode4 = "Mode4";
        public const string CheckUpdate = "CheckUpdate";
        public const string OpenLog = "OpenLog";
        public const string About = "About";
        public const string EditText = "EditText";
        public const string Exit = "Exit";
        public const string PanelTheme = "PanelTheme";
        public const string ThemeSettings = "ThemeSettings";
        public const string DesktopPet = "DesktopPet";
        public const string PetReset = "PetReset";
        public const string RestoreFloatingBall = "RestoreFloatingBall";
        public const string MayhemRanking = "MayhemRanking";
        public const string WorkDirectory = "WorkDirectory";
        public const string AutoDetect = "AutoDetect";
        public const string SelectDirectory = "SelectDirectory";
        public const string RulesConfigured = "RulesConfigured";
        public const string WaitingConfiguration = "WaitingConfiguration";
        public const string CleanupHint = "CleanupHint";
        public const string StartCleanup = "StartCleanup";
        public const string UpdateAndAnnouncements = "UpdateAndAnnouncements";
        public const string AutoCheckAtStartup = "AutoCheckAtStartup";
        public const string Ready = "Ready";
        public const string Administrator = "Administrator";
        public const string StandardMode = "StandardMode";
        public const string Close = "Close";
        public const string ApplyPet = "ApplyPet";
        public const string PetSource = "PetSource";
        public const string Open = "Open";

        // Theme popup. These keys describe UI roles, not the current Chinese wording.
        public const string ThemePanelAppearance = "ThemePanelAppearance";
        public const string ThemeDesktopMode = "ThemeDesktopMode";
        public const string ThemeFacmShell = "ThemeFacmShell";
        public const string ThemeSelectDesktopPet = "ThemeSelectDesktopPet";
        public const string ThemeResetDesktopPosition = "ThemeResetDesktopPosition";

        // Desktop-pet picker shell/status copy.
        public const string PetPickerWindowTitle = "PetPickerWindowTitle";
        public const string PetPickerTitle = "PetPickerTitle";
        public const string PetPickerHint = "PetPickerHint";
        public const string PetCurrentPrefix = "PetCurrentPrefix";
        public const string PetCurrentBadge = "PetCurrentBadge";
        public const string PetCurrentUse = "PetCurrentUse";
        public const string PetInteractionVPet = "PetInteractionVPet";
        public const string PetInteractionFlying = "PetInteractionFlying";
        public const string PetRuntimeVPet = "PetRuntimeVPet";
        public const string PetRuntimeFlying = "PetRuntimeFlying";
        public const string VPetPreviewTitle = "VPetPreviewTitle";
        public const string VPetPreviewDescription = "VPetPreviewDescription";

        // Visible pet names.
        public const string PetNameGreenFly = "PetNameGreenFly";
        public const string PetNameBee = "PetNameBee";
        public const string PetNameRealBee = "PetNameRealBee";
        public const string PetNameDragonfly = "PetNameDragonfly";
        public const string PetNameButterfly = "PetNameButterfly";
        public const string PetNameMoth = "PetNameMoth";
        public const string PetNameVPet = "PetNameVPet";

        // Picker summaries.
        public const string PetSummaryGreenFly = "PetSummaryGreenFly";
        public const string PetSummaryBee = "PetSummaryBee";
        public const string PetSummaryRealBee = "PetSummaryRealBee";
        public const string PetSummaryDragonfly = "PetSummaryDragonfly";
        public const string PetSummaryButterfly = "PetSummaryButterfly";
        public const string PetSummaryMoth = "PetSummaryMoth";
        public const string PetSummaryVPet = "PetSummaryVPet";
        public const string PetSummaryDefaultVPet = "PetSummaryDefaultVPet";
        public const string PetSummaryDefaultFlying = "PetSummaryDefaultFlying";

        // Picker behavior lines.
        public const string PetBehaviorGreenFly = "PetBehaviorGreenFly";
        public const string PetBehaviorBee = "PetBehaviorBee";
        public const string PetBehaviorRealBee = "PetBehaviorRealBee";
        public const string PetBehaviorDragonfly = "PetBehaviorDragonfly";
        public const string PetBehaviorButterfly = "PetBehaviorButterfly";
        public const string PetBehaviorMoth = "PetBehaviorMoth";
        public const string PetBehaviorVPet = "PetBehaviorVPet";

        // Picker descriptions.
        public const string PetDescriptionGreenFly = "PetDescriptionGreenFly";
        public const string PetDescriptionBee = "PetDescriptionBee";
        public const string PetDescriptionRealBee = "PetDescriptionRealBee";
        public const string PetDescriptionDragonfly = "PetDescriptionDragonfly";
        public const string PetDescriptionButterfly = "PetDescriptionButterfly";
        public const string PetDescriptionMoth = "PetDescriptionMoth";
        public const string PetDescriptionVPet = "PetDescriptionVPet";
    }
}
