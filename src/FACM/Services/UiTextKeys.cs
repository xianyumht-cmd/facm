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

        // Novice-first Shell information architecture. Root categories stay stable as modules grow.
        public const string ShellLeague = "ShellLeague";
        public const string ShellMore = "ShellMore";
        public const string ShellFeatureCenter = "ShellFeatureCenter";
        public const string ShellRepairTools = "ShellRepairTools";
        public const string ShellPersonalization = "ShellPersonalization";
        public const string ShellMoreSettings = "ShellMoreSettings";
        public const string ShellManageDirectory = "ShellManageDirectory";
        public const string ShellRepairHint = "ShellRepairHint";
        public const string ShellLeagueHint = "ShellLeagueHint";
        public const string ShellPersonalizationHint = "ShellPersonalizationHint";
        public const string ShellDirectoryReady = "ShellDirectoryReady";
        public const string ShellDirectoryMissing = "ShellDirectoryMissing";

        // Theme popup. These keys describe UI roles, not the current Chinese wording.
        public const string ThemePanelAppearance = "ThemePanelAppearance";
        public const string ThemeDesktopMode = "ThemeDesktopMode";
        public const string ThemeFacmShell = "ThemeFacmShell";
        public const string ThemeSelectDesktopPet = "ThemeSelectDesktopPet";
        public const string ThemeResetDesktopPosition = "ThemeResetDesktopPosition";

        // League Dashboard Gate 1.
        public const string LeagueDashboardMenu = "LeagueDashboardMenu";
        public const string LeagueDashboardWindowTitle = "LeagueDashboardWindowTitle";
        public const string LeagueDashboardTitle = "LeagueDashboardTitle";
        public const string LeagueDashboardHint = "LeagueDashboardHint";
        public const string LeagueDashboardConnection = "LeagueDashboardConnection";
        public const string LeagueDashboardConnected = "LeagueDashboardConnected";
        public const string LeagueDashboardDisconnected = "LeagueDashboardDisconnected";
        public const string LeagueDashboardAccount = "LeagueDashboardAccount";
        public const string LeagueDashboardLevel = "LeagueDashboardLevel";
        public const string LeagueDashboardPlatformRegion = "LeagueDashboardPlatformRegion";
        public const string LeagueDashboardGameflow = "LeagueDashboardGameflow";
        public const string LeagueDashboardPerformance = "LeagueDashboardPerformance";
        public const string LeagueDashboardRefresh = "LeagueDashboardRefresh";
        public const string LeagueDashboardWaitingClient = "LeagueDashboardWaitingClient";
        public const string LeagueDashboardUnknown = "LeagueDashboardUnknown";
        public const string LeagueDashboardLastUpdated = "LeagueDashboardLastUpdated";

        // Player Gate 1 / Gate 2.
        public const string LeaguePlayerMenu = "LeaguePlayerMenu";
        public const string LeaguePlayerWindowTitle = "LeaguePlayerWindowTitle";
        public const string LeaguePlayerTitle = "LeaguePlayerTitle";
        public const string LeaguePlayerHint = "LeaguePlayerHint";
        public const string LeaguePlayerLoadingProfile = "LeaguePlayerLoadingProfile";
        public const string LeaguePlayerLoadingMatches = "LeaguePlayerLoadingMatches";
        public const string LeaguePlayerClientRequired = "LeaguePlayerClientRequired";
        public const string LeaguePlayerNoMatches = "LeaguePlayerNoMatches";
        public const string LeaguePlayerRecentMatches = "LeaguePlayerRecentMatches";
        public const string LeaguePlayerRefresh = "LeaguePlayerRefresh";
        public const string LeaguePlayerLoadMore = "LeaguePlayerLoadMore";
        public const string LeaguePlayerTime = "LeaguePlayerTime";
        public const string LeaguePlayerMode = "LeaguePlayerMode";
        public const string LeaguePlayerChampion = "LeaguePlayerChampion";
        public const string LeaguePlayerChampionStatsFormat = "LeaguePlayerChampionStatsFormat";
        public const string LeaguePlayerKda = "LeaguePlayerKda";
        public const string LeaguePlayerCs = "LeaguePlayerCs";
        public const string LeaguePlayerResult = "LeaguePlayerResult";
        public const string LeaguePlayerDuration = "LeaguePlayerDuration";
        public const string LeaguePlayerWin = "LeaguePlayerWin";
        public const string LeaguePlayerLoss = "LeaguePlayerLoss";
        public const string LeaguePlayerUnknown = "LeaguePlayerUnknown";

        // Champ Select / Current Game Gate 1.
        public const string LeagueLiveMenu = "LeagueLiveMenu";
        public const string LeagueLiveWindowTitle = "LeagueLiveWindowTitle";
        public const string LeagueLiveTitle = "LeagueLiveTitle";
        public const string LeagueLiveHint = "LeagueLiveHint";
        public const string LeagueLivePhase = "LeagueLivePhase";
        public const string LeagueLivePerformance = "LeagueLivePerformance";
        public const string LeagueLiveWaiting = "LeagueLiveWaiting";
        public const string LeagueLiveChampSelect = "LeagueLiveChampSelect";
        public const string LeagueLiveCurrentGame = "LeagueLiveCurrentGame";
        public const string LeagueLiveGame = "LeagueLiveGame";
        public const string LeagueLiveMap = "LeagueLiveMap";
        public const string LeagueLiveMode = "LeagueLiveMode";
        public const string LeagueLiveQueue = "LeagueLiveQueue";
        public const string LeagueLiveTimer = "LeagueLiveTimer";
        public const string LeagueLiveLocalAction = "LeagueLiveLocalAction";
        public const string LeagueLiveBans = "LeagueLiveBans";
        public const string LeagueLiveTeam = "LeagueLiveTeam";
        public const string LeagueLivePlayer = "LeagueLivePlayer";
        public const string LeagueLivePosition = "LeagueLivePosition";
        public const string LeagueLiveChampion = "LeagueLiveChampion";
        public const string LeagueLiveIntent = "LeagueLiveIntent";
        public const string LeagueLiveSpells = "LeagueLiveSpells";
        public const string LeagueLiveRefresh = "LeagueLiveRefresh";
        public const string LeagueLiveReadOnly = "LeagueLiveReadOnly";
        public const string LeagueLiveLocalPlayer = "LeagueLiveLocalPlayer";
        public const string LeagueLiveAlly = "LeagueLiveAlly";
        public const string LeagueLiveEnemy = "LeagueLiveEnemy";
        public const string LeagueLiveTeamOne = "LeagueLiveTeamOne";
        public const string LeagueLiveTeamTwo = "LeagueLiveTeamTwo";
        public const string LeagueLiveUnknown = "LeagueLiveUnknown";

        // Tools / Automation Gate 1: read-only OP.GG build advisor.
        public const string LeagueAdvisorMenu = "LeagueAdvisorMenu";
        public const string LeagueAdvisorWindowTitle = "LeagueAdvisorWindowTitle";
        public const string LeagueAdvisorTitle = "LeagueAdvisorTitle";
        public const string LeagueAdvisorHint = "LeagueAdvisorHint";
        public const string LeagueAdvisorContext = "LeagueAdvisorContext";
        public const string LeagueAdvisorStats = "LeagueAdvisorStats";
        public const string LeagueAdvisorSource = "LeagueAdvisorSource";
        public const string LeagueAdvisorVersion = "LeagueAdvisorVersion";
        public const string LeagueAdvisorCategory = "LeagueAdvisorCategory";
        public const string LeagueAdvisorRecommendation = "LeagueAdvisorRecommendation";
        public const string LeagueAdvisorEvidence = "LeagueAdvisorEvidence";
        public const string LeagueAdvisorRunes = "LeagueAdvisorRunes";
        public const string LeagueAdvisorStarterItems = "LeagueAdvisorStarterItems";
        public const string LeagueAdvisorBoots = "LeagueAdvisorBoots";
        public const string LeagueAdvisorCoreItems = "LeagueAdvisorCoreItems";
        public const string LeagueAdvisorSkills = "LeagueAdvisorSkills";
        public const string LeagueAdvisorCounters = "LeagueAdvisorCounters";
        public const string LeagueAdvisorWaitingChampion = "LeagueAdvisorWaitingChampion";
        public const string LeagueAdvisorWaitingChampSelect = "LeagueAdvisorWaitingChampSelect";
        public const string LeagueAdvisorUnsupportedMode = "LeagueAdvisorUnsupportedMode";
        public const string LeagueAdvisorOpggUnavailable = "LeagueAdvisorOpggUnavailable";
        public const string LeagueAdvisorInGameCache = "LeagueAdvisorInGameCache";
        public const string LeagueAdvisorInGameNoCache = "LeagueAdvisorInGameNoCache";
        public const string LeagueAdvisorTimeout = "LeagueAdvisorTimeout";
        public const string LeagueAdvisorReady = "LeagueAdvisorReady";
        public const string LeagueAdvisorReadOnly = "LeagueAdvisorReadOnly";

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
