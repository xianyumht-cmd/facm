using System;
using System.Collections.Generic;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueEfficiencyUiTextKeys
    {
        public const string Menu = "LeagueEfficiencyMenu";
        public const string WindowTitle = "LeagueEfficiencyWindowTitle";
        public const string Title = "LeagueEfficiencyTitle";
        public const string Hint = "LeagueEfficiencyHint";
        public const string HotkeySection = "LeagueEfficiencyHotkeySection";
        public const string ExitGame = "LeagueEfficiencyExitGame";
        public const string ExitGameHint = "LeagueEfficiencyExitGameHint";
        public const string CloseLobby = "LeagueEfficiencyCloseLobby";
        public const string CloseLobbyHint = "LeagueEfficiencyCloseLobbyHint";
        public const string Capture = "LeagueEfficiencyCapture";
        public const string Clear = "LeagueEfficiencyClear";
        public const string Save = "LeagueEfficiencySave";
        public const string Disabled = "LeagueEfficiencyDisabled";
        public const string CaptureTitle = "LeagueEfficiencyCaptureTitle";
        public const string CapturePrompt = "LeagueEfficiencyCapturePrompt";
        public const string CaptureUnsafe = "LeagueEfficiencyCaptureUnsafe";
        public const string Saved = "LeagueEfficiencySaved";
        public const string SaveFailed = "LeagueEfficiencySaveFailed";
        public const string PostGameSection = "LeagueEfficiencyPostGameSection";
        public const string AutoHonor = "LeagueEfficiencyAutoHonor";
        public const string AutoHonorHint = "LeagueEfficiencyAutoHonorHint";
        public const string HonorStrategyTitle = "LeagueEfficiencyHonorStrategyTitle";
        public const string HonorPreferParty = "LeagueEfficiencyHonorPreferParty";
        public const string HonorOnlyParty = "LeagueEfficiencyHonorOnlyParty";
        public const string HonorAnyAlly = "LeagueEfficiencyHonorAnyAlly";
        public const string HonorOptOut = "LeagueEfficiencyHonorOptOut";
        public const string HonorIncludingOpponent = "LeagueEfficiencyHonorIncludingOpponent";
        public const string HonorLastNone = "LeagueEfficiencyHonorLastNone";
        public const string HonorLastSuccess = "LeagueEfficiencyHonorLastSuccess";
        public const string HonorLastSkipped = "LeagueEfficiencyHonorLastSkipped";
        public const string HonorLastUnknown = "LeagueEfficiencyHonorLastUnknown";
        public const string HonorLastFailed = "LeagueEfficiencyHonorLastFailed";
        public const string AutoReturn = "LeagueEfficiencyAutoReturn";
        public const string AutoReturnHint = "LeagueEfficiencyAutoReturnHint";
        public const string PostGameSaved = "LeagueEfficiencyPostGameSaved";
        public const string NextGameSection = "LeagueEfficiencyNextGameSection";
        public const string AutoMatchmaking = "LeagueEfficiencyAutoMatchmaking";
        public const string AutoMatchmakingHint = "LeagueEfficiencyAutoMatchmakingHint";
        public const string AutoAccept = "LeagueEfficiencyAutoAccept";
        public const string AutoAcceptHint = "LeagueEfficiencyAutoAcceptHint";
        public const string AutoReconnect = "LeagueEfficiencyAutoReconnect";
        public const string AutoReconnectHint = "LeagueEfficiencyAutoReconnectHint";
        public const string RespawnOverlay = "LeagueEfficiencyRespawnOverlay";
        public const string RespawnOverlayHint = "LeagueEfficiencyRespawnOverlayHint";
        public const string RespawnOverlaySaved = "LeagueEfficiencyRespawnOverlaySaved";
        public const string QuickMessageOpen = "LeagueQuickMessageOpen";
        public const string QuickMessageTitle = "LeagueQuickMessageTitle";
        public const string QuickMessageHint = "LeagueQuickMessageHint";
        public const string QuickMessageSave = "LeagueQuickMessageSave";
        public const string QuickMessageSend = "LeagueQuickMessageSend";
        public const string QuickMessageSaved = "LeagueQuickMessageSaved";
        public const string QuickMessageSent = "LeagueQuickMessageSent";
        public const string QuickMessageUnavailable = "LeagueQuickMessageUnavailable";
        public const string QuickMessageInvalid = "LeagueQuickMessageInvalid";
        public const string QuickMessageShortcut = "LeagueQuickMessageShortcut";
        public const string QuickMessageCancel = "LeagueQuickMessageCancel";
        public const string QuickMessageInterval = "LeagueQuickMessageInterval";
        public const string LobbyInvitationSection = "LeagueEfficiencyLobbyInvitationSection";
        public const string HandleInvitations = "LeagueEfficiencyHandleInvitations";
        public const string HandleInvitationsHint = "LeagueEfficiencyHandleInvitationsHint";
        public const string InvitationGameType = "LeagueEfficiencyInvitationGameType";
        public const string InvitationGameTypeHint = "LeagueEfficiencyInvitationGameTypeHint";
        public const string InvitationAction = "LeagueEfficiencyInvitationAction";
        public const string InvitationActionHint = "LeagueEfficiencyInvitationActionHint";
        public const string InvitationIgnore = "LeagueEfficiencyInvitationIgnore";
        public const string InvitationAccept = "LeagueEfficiencyInvitationAccept";
        public const string InvitationDecline = "LeagueEfficiencyInvitationDecline";
        public const string SkipInvitationsWhenAway = "LeagueEfficiencySkipInvitationsWhenAway";
        public const string SkipInvitationsWhenAwayHint = "LeagueEfficiencySkipInvitationsWhenAwayHint";
        public const string AutoSkipLeader = "LeagueEfficiencyAutoSkipLeader";
        public const string AutoSkipLeaderHint = "LeagueEfficiencyAutoSkipLeaderHint";
        public const string AramTeamSideSection = "LeagueEfficiencyAramSideSection";
        public const string AramTeamSide = "LeagueEfficiencyAramSide";
        public const string AramTeamSideHint = "LeagueEfficiencyAramSideHint";
        public const string AramTeamSideVisible = "LeagueEfficiencyAramSideVisible";
        public const string AramTeamSideVisibleHint = "LeagueEfficiencyAramSideVisibleHint";
        public const string LoginSignatureSection = "LeagueEfficiencyLoginSignatureSection";
        public const string LoginSignatureText = "LeagueEfficiencyLoginSignatureText";
        public const string LoginSignatureTextHint = "LeagueEfficiencyLoginSignatureTextHint";
        public const string LoginSignatureSave = "LeagueEfficiencyLoginSignatureSave";
        public const string LoginSignatureAuto = "LeagueEfficiencyLoginSignatureAuto";
        public const string LoginSignatureAutoHint = "LeagueEfficiencyLoginSignatureAutoHint";
        public const string LoginSignatureSaved = "LeagueEfficiencyLoginSignatureSaved";
        public const string LoginRankAuto = "LeagueEfficiencyLoginRankAuto";
        public const string LoginRankAutoHint = "LeagueEfficiencyLoginRankAutoHint";
        public const string LoginRankQueue = "LeagueEfficiencyLoginRankQueue";
        public const string LoginRankQueueHint = "LeagueEfficiencyLoginRankQueueHint";
        public const string LoginRankTier = "LeagueEfficiencyLoginRankTier";
        public const string LoginRankTierHint = "LeagueEfficiencyLoginRankTierHint";
        public const string LoginRankDivision = "LeagueEfficiencyLoginRankDivision";
        public const string LoginRankDivisionHint = "LeagueEfficiencyLoginRankDivisionHint";
        public const string LoginRankSaved = "LeagueEfficiencyLoginRankSaved";
        public const string AutoReply = "LeagueEfficiencyAutoReply";
        public const string AutoReplyHint = "LeagueEfficiencyAutoReplyHint";
        public const string ReplyAway = "LeagueEfficiencyReplyAway";
        public const string ReplyAwayHint = "LeagueEfficiencyReplyAwayHint";
        public const string ReplyText = "LeagueEfficiencyReplyText";
        public const string OfflineLock = "LeagueEfficiencyOfflineLock";
        public const string OfflineLockHint = "LeagueEfficiencyOfflineLockHint";
        public const string OfflineLockSaved = "LeagueEfficiencyOfflineLockSaved";
        public const string LobbyInvitationSaved = "LeagueEfficiencyLobbyInvitationSaved";
        public const string FriendInviteOpen = "LeagueEfficiencyFriendInviteOpen";
        public const string FriendInviteTitle = "LeagueEfficiencyFriendInviteTitle";
        public const string FriendInviteHint = "LeagueEfficiencyFriendInviteHint";
        public const string FriendInviteAnnounce = "LeagueEfficiencyFriendInviteAnnounce";
        public const string FriendInviteSearch = "LeagueEfficiencyFriendInviteSearch";
        public const string FriendInviteRefresh = "LeagueEfficiencyFriendInviteRefresh";
        public const string FriendInviteName = "LeagueEfficiencyFriendInviteName";
        public const string FriendInviteAvailability = "LeagueEfficiencyFriendInviteAvailability";
        public const string FriendInviteLoading = "LeagueEfficiencyFriendInviteLoading";
        public const string FriendInviteUnavailable = "LeagueEfficiencyFriendInviteUnavailable";
        public const string FriendInviteCount = "LeagueEfficiencyFriendInviteCount";
        public const string FriendInviteRejected = "LeagueEfficiencyFriendInviteRejected";
        public const string FriendInviteClose = "LeagueEfficiencyFriendInviteClose";
        public const string FriendInviteOnline = "LeagueEfficiencyFriendInviteOnline";
        public const string FriendInviteInGame = "LeagueEfficiencyFriendInviteInGame";
        public const string FriendInviteAway = "LeagueEfficiencyFriendInviteAway";
        public const string FriendInviteOffline = "LeagueEfficiencyFriendInviteOffline";
        public const string AutoSelectSection = "LeagueEfficiencyAutoSelectSection";
        public const string AutoPick = "LeagueEfficiencyAutoPick";
        public const string AutoPickHint = "LeagueEfficiencyAutoPickHint";
        public const string AutoPickChampion = "LeagueEfficiencyAutoPickChampion";
        public const string AutoPickChampionHint = "LeagueEfficiencyAutoPickChampionHint";
        public const string AutoBan = "LeagueEfficiencyAutoBan";
        public const string AutoBanHint = "LeagueEfficiencyAutoBanHint";
        public const string AutoBanChampion = "LeagueEfficiencyAutoBanChampion";
        public const string AutoBanChampionHint = "LeagueEfficiencyAutoBanChampionHint";
        public const string ChampionNotSet = "LeagueEfficiencyChampionNotSet";
        public const string ChampionCatalogUnavailable = "LeagueEfficiencyChampionCatalogUnavailable";
        public const string AutoSelectSaved = "LeagueEfficiencyAutoSelectSaved";
        public const string AutoSelectPause = "LeagueEfficiencyAutoSelectPause";
        public const string AutoSelectPauseHint = "LeagueEfficiencyAutoSelectPauseHint";
        public const string AdvancedOpen = "LeagueEfficiencyAdvancedOpen";
        public const string AdvancedTitle = "LeagueEfficiencyAdvancedTitle";
        public const string AdvancedHint = "LeagueEfficiencyAdvancedHint";
        public const string AdvancedMode = "LeagueEfficiencyAdvancedMode";
        public const string AdvancedRole = "LeagueEfficiencyAdvancedRole";
        public const string AdvancedAction = "LeagueEfficiencyAdvancedAction";
        public const string AdvancedPriority = "LeagueEfficiencyAdvancedPriority";
        public const string AdvancedAdd = "LeagueEfficiencyAdvancedAdd";
        public const string AdvancedRemove = "LeagueEfficiencyAdvancedRemove";
        public const string AdvancedUp = "LeagueEfficiencyAdvancedUp";
        public const string AdvancedDown = "LeagueEfficiencyAdvancedDown";
        public const string AdvancedStrategy = "LeagueEfficiencyAdvancedStrategy";
        public const string AdvancedDelay = "LeagueEfficiencyAdvancedDelay";
        public const string AdvancedBenchControls = "LeagueEfficiencyAdvancedBenchControls";
        public const string AdvancedIntentControls = "LeagueEfficiencyAdvancedIntentControls";
        public const string AdvancedShowIntent = "LeagueEfficiencyAdvancedShowIntent";
        public const string AdvancedIgnoreIntent = "LeagueEfficiencyAdvancedIgnoreIntent";
        public const string AdvancedBenchSwap = "LeagueEfficiencyAdvancedBenchSwap";
        public const string AdvancedBenchUpgrade = "LeagueEfficiencyAdvancedBenchUpgrade";
        public const string AdvancedTrade = "LeagueEfficiencyAdvancedTrade";
        public const string AdvancedBenchDelay = "LeagueEfficiencyAdvancedBenchDelay";
        public const string AdvancedSave = "LeagueEfficiencyAdvancedSave";
        public const string AdvancedCancel = "LeagueEfficiencyAdvancedCancel";
        public const string ProfileModeRanked = "LeagueEfficiencyProfileModeRanked";
        public const string ProfileModeNormal = "LeagueEfficiencyProfileModeNormal";
        public const string ProfileModeCherry = "LeagueEfficiencyProfileModeCherry";
        public const string ProfileModeUrf = "LeagueEfficiencyProfileModeUrf";
        public const string ProfileModeOneForAll = "LeagueEfficiencyProfileModeOneForAll";
        public const string ProfileModeUltbook = "LeagueEfficiencyProfileModeUltbook";
        public const string ProfileModeBot = "LeagueEfficiencyProfileModeBot";
        public const string ProfileModeCustom = "LeagueEfficiencyProfileModeCustom";
        public const string AdvancedGroupEnabled = "LeagueEfficiencyAdvancedGroupEnabled";
        public const string QuickMessageFixedTab = "LeagueEfficiencyQuickMessageFixedTab";
        public const string DynamicKda = "LeagueEfficiencyDynamicKda";
        public const string DynamicPartial = "LeagueEfficiencyDynamicPartial";
        public const string DynamicGameHint = "LeagueEfficiencyDynamicGameHint";
        public const string DynamicShortcutHint = "LeagueEfficiencyDynamicShortcutHint";
        public const string DynamicOpen = "LeagueEfficiencyDynamicOpen";
        public const string DynamicLoad = "LeagueEfficiencyDynamicLoad";
        public const string DynamicCompleteMissing = "LeagueEfficiencyDynamicCompleteMissing";
        public const string DynamicSend = "LeagueEfficiencyDynamicSend";
        public const string DynamicLoading = "LeagueEfficiencyDynamicLoading";
        public const string DynamicReady = "LeagueEfficiencyDynamicReady";
        public const string DynamicUnavailable = "LeagueEfficiencyDynamicUnavailable";
        public const string DynamicNoChampion = "LeagueEfficiencyDynamicNoChampion";
        public const string DynamicNoMatches = "LeagueEfficiencyDynamicNoMatches";
        public const string DynamicNoChampionMatches = "LeagueEfficiencyDynamicNoChampionMatches";
        public const string DynamicPlayer = "LeagueEfficiencyDynamicPlayer";
        public const string DynamicMatchCount = "LeagueEfficiencyDynamicMatchCount";
        public const string DynamicWinRate = "LeagueEfficiencyDynamicWinRate";
        public const string DynamicSoloKills = "LeagueEfficiencyDynamicSoloKills";
        public const string DynamicVision = "LeagueEfficiencyDynamicVision";
        public const string DynamicDamage = "LeagueEfficiencyDynamicDamage";
        public const string DynamicTaken = "LeagueEfficiencyDynamicTaken";
        public const string DynamicGold = "LeagueEfficiencyDynamicGold";
        public const string DynamicCs = "LeagueEfficiencyDynamicCs";
        public const string DynamicParticipation = "LeagueEfficiencyDynamicParticipation";
        public const string DynamicEfficiency = "LeagueEfficiencyDynamicEfficiency";
        public const string DynamicChampions = "LeagueEfficiencyDynamicChampions";
        public const string DynamicPositions = "LeagueEfficiencyDynamicPositions";
        public const string DynamicNoMain = "LeagueEfficiencyDynamicNoMain";
        public const string DynamicFriendly = "LeagueEfficiencyDynamicFriendly";
        public const string DynamicEnemy = "LeagueEfficiencyDynamicEnemy";
        public const string DynamicAll = "LeagueEfficiencyDynamicAll";
        public const string DynamicNames = "LeagueEfficiencyDynamicNames";
        public const string DynamicChampionNames = "LeagueEfficiencyDynamicChampionNames";
        public const string DynamicCombinedNames = "LeagueEfficiencyDynamicCombinedNames";
        public const string DynamicCurrentChampion = "LeagueEfficiencyDynamicCurrentChampion";
        public const string DynamicSaved = "LeagueEfficiencyDynamicSaved";
        public const string DynamicTagEdit = "LeagueEfficiencyDynamicTagEdit";
        public const string DynamicTagRemove = "LeagueEfficiencyDynamicTagRemove";
        public const string DynamicTagTitle = "LeagueEfficiencyDynamicTagTitle";
        public const string DynamicTagCancel = "LeagueEfficiencyDynamicTagCancel";
        public const string DynamicTagSaved = "LeagueEfficiencyDynamicTagSaved";
        public const string DynamicTagUnavailable = "LeagueEfficiencyDynamicTagUnavailable";
        public const string DynamicTagTools = "LeagueEfficiencyDynamicTagTools";
        public const string DynamicTagBrowse = "LeagueEfficiencyDynamicTagBrowse";
        public const string DynamicSavedPlayerAll = "LeagueEfficiencyDynamicSavedPlayerAll";
        public const string DynamicSavedPlayerMetCount = "LeagueEfficiencyDynamicSavedPlayerMetCount";
        public const string DynamicSavedPlayerLastMet = "LeagueEfficiencyDynamicSavedPlayerLastMet";
        public const string DynamicTagSearch = "LeagueEfficiencyDynamicTagSearch";
        public const string DynamicTagTarget = "LeagueEfficiencyDynamicTagTarget";
        public const string DynamicTagSelf = "LeagueEfficiencyDynamicTagSelf";
        public const string DynamicTagRegion = "LeagueEfficiencyDynamicTagRegion";
        public const string DynamicTagUpdated = "LeagueEfficiencyDynamicTagUpdated";
        public const string DynamicTagPrevious = "LeagueEfficiencyDynamicTagPrevious";
        public const string DynamicTagNext = "LeagueEfficiencyDynamicTagNext";
        public const string DynamicTagPage = "LeagueEfficiencyDynamicTagPage";
        public const string DynamicTagDeleteConfirm = "LeagueEfficiencyDynamicTagDeleteConfirm";
        public const string DynamicTagChanged = "LeagueEfficiencyDynamicTagChanged";
        public const string DynamicTagImport = "LeagueEfficiencyDynamicTagImport";
        public const string DynamicTagExport = "LeagueEfficiencyDynamicTagExport";
        public const string DynamicTagImported = "LeagueEfficiencyDynamicTagImported";
        public const string DynamicTagExported = "LeagueEfficiencyDynamicTagExported";
        public const string DynamicTagExchangeFailed = "LeagueEfficiencyDynamicTagExchangeFailed";
        public const string DynamicEncounterHistory = "LeagueEfficiencyDynamicEncounterHistory";
        public const string DynamicEncounterTitle = "LeagueEfficiencyDynamicEncounterTitle";
        public const string DynamicEncounterEmpty = "LeagueEfficiencyDynamicEncounterEmpty";
        public const string DynamicEncounterDelete = "LeagueEfficiencyDynamicEncounterDelete";
        public const string DynamicEncounterConfirm = "LeagueEfficiencyDynamicEncounterConfirm";
        public const string DynamicEncounterGame = "LeagueEfficiencyDynamicEncounterGame";
        public const string DynamicEncounterQueue = "LeagueEfficiencyDynamicEncounterQueue";
        public const string DynamicEncounterDate = "LeagueEfficiencyDynamicEncounterDate";
        public const string DynamicEncounterUnavailable = "LeagueEfficiencyDynamicEncounterUnavailable";
        public const string DynamicEncounterFilter = "LeagueEfficiencyDynamicEncounterFilter";
        public const string DynamicEncounterOrderNewest = "LeagueEfficiencyDynamicEncounterOrderNewest";
        public const string DynamicEncounterOrderOldest = "LeagueEfficiencyDynamicEncounterOrderOldest";
        public const string DynamicEncounterApply = "LeagueEfficiencyDynamicEncounterApply";
        public const string DynamicEncounterPage = "LeagueEfficiencyDynamicEncounterPage";
        public const string DynamicEncounterMatchDetail = "LeagueEfficiencyDynamicEncounterMatchDetail";
        public const string DynamicEncounterMatchLoading = "LeagueEfficiencyDynamicEncounterMatchLoading";
        public const string DynamicEncounterMatchUnavailable = "LeagueEfficiencyDynamicEncounterMatchUnavailable";
        public const string DynamicEncounterMatchNoClient = "LeagueEfficiencyDynamicEncounterMatchNoClient";
        public const string DynamicEncounterMatchWrongAccount = "LeagueEfficiencyDynamicEncounterMatchWrongAccount";
        public const string DynamicEncounterMatchSummary = "LeagueEfficiencyDynamicEncounterMatchSummary";
        public const string DynamicEncounterMatchWon = "LeagueEfficiencyDynamicEncounterMatchWon";
        public const string DynamicEncounterMatchLost = "LeagueEfficiencyDynamicEncounterMatchLost";
        public const string DynamicEncounterScoreboardTitle = "LeagueEfficiencyDynamicEncounterScoreboardTitle";
        public const string DynamicEncounterReplay = "LeagueEfficiencyDynamicEncounterReplay";
        public const string DynamicEncounterReplayChecking = "LeagueEfficiencyDynamicEncounterReplayChecking";
        public const string DynamicEncounterReplayDownloadRequested = "LeagueEfficiencyDynamicEncounterReplayDownloadRequested";
        public const string DynamicEncounterReplayWatchRequested = "LeagueEfficiencyDynamicEncounterReplayWatchRequested";
        public const string DynamicEncounterReplayDownloading = "LeagueEfficiencyDynamicEncounterReplayDownloading";
        public const string DynamicEncounterReplayIncompatible = "LeagueEfficiencyDynamicEncounterReplayIncompatible";
        public const string DynamicEncounterReplayUnavailable = "LeagueEfficiencyDynamicEncounterReplayUnavailable";
        public const string DynamicEncounterScoreboardSummary = "LeagueEfficiencyDynamicEncounterScoreboardSummary";
        public const string DynamicEncounterScoreboardTeam = "LeagueEfficiencyDynamicEncounterScoreboardTeam";
        public const string DynamicEncounterScoreboardPlayer = "LeagueEfficiencyDynamicEncounterScoreboardPlayer";
        public const string DynamicEncounterScoreboardChampion = "LeagueEfficiencyDynamicEncounterScoreboardChampion";
        public const string DynamicEncounterScoreboardCs = "LeagueEfficiencyDynamicEncounterScoreboardCs";
        public const string DynamicEncounterScoreboardDamage = "LeagueEfficiencyDynamicEncounterScoreboardDamage";
        public const string DynamicEncounterScoreboardGold = "LeagueEfficiencyDynamicEncounterScoreboardGold";
        public const string DynamicEncounterScoreboardVision = "LeagueEfficiencyDynamicEncounterScoreboardVision";
        public const string DynamicEncounterScoreboardResult = "LeagueEfficiencyDynamicEncounterScoreboardResult";
        public const string DynamicEncounterScoreboardUnknown = "LeagueEfficiencyDynamicEncounterScoreboardUnknown";
        public const string DynamicEncounterScoreboardSelectionHint = "LeagueEfficiencyDynamicEncounterScoreboardSelectionHint";
        public const string DynamicEncounterScoreboardSelection = "LeagueEfficiencyDynamicEncounterScoreboardSelection";
        public const string DynamicEncounterScoreboardRates = "LeagueEfficiencyDynamicEncounterScoreboardRates";
        public const string DynamicEncounterScoreboardDamageSplit = "LeagueEfficiencyDynamicEncounterScoreboardDamageSplit";
        public const string DynamicEncounterScoreboardTakenSplit = "LeagueEfficiencyDynamicEncounterScoreboardTakenSplit";
        public const string DynamicEncounterScoreboardDealtBar = "LeagueEfficiencyDynamicEncounterScoreboardDealtBar";
        public const string DynamicEncounterScoreboardTakenBar = "LeagueEfficiencyDynamicEncounterScoreboardTakenBar";
        public const string DynamicEncounterScoreboardDamageTooltip = "LeagueEfficiencyDynamicEncounterScoreboardDamageTooltip";
        public const string DynamicEncounterRadarTitle = "LeagueEfficiencyDynamicEncounterRadarTitle";
        public const string DynamicEncounterRadarHint = "LeagueEfficiencyDynamicEncounterRadarHint";
        public const string DynamicEncounterRadarLegend = "LeagueEfficiencyDynamicEncounterRadarLegend";
        public const string DynamicEncounterRadarPlayerTip = "LeagueEfficiencyDynamicEncounterRadarPlayerTip";
        public const string DynamicEncounterRadarTeamTip = "LeagueEfficiencyDynamicEncounterRadarTeamTip";
        public const string DynamicEncounterRadarDamage = "LeagueEfficiencyDynamicEncounterRadarDamage";
        public const string DynamicEncounterRadarTaken = "LeagueEfficiencyDynamicEncounterRadarTaken";
        public const string DynamicEncounterRadarGold = "LeagueEfficiencyDynamicEncounterRadarGold";
        public const string DynamicEncounterRadarCs = "LeagueEfficiencyDynamicEncounterRadarCs";
        public const string DynamicEncounterRadarKda = "LeagueEfficiencyDynamicEncounterRadarKda";
        public const string DynamicEncounterRadarParticipation = "LeagueEfficiencyDynamicEncounterRadarParticipation";
        public const string DynamicEncounterRadarHeal = "LeagueEfficiencyDynamicEncounterRadarHeal";
        public const string DynamicEncounterMultikillTitle = "LeagueEfficiencyDynamicEncounterMultikillTitle";
        public const string DynamicEncounterMultikillTimes = "LeagueEfficiencyDynamicEncounterMultikillTimes";
        public const string DynamicEncounterMultikillDouble = "LeagueEfficiencyDynamicEncounterMultikillDouble";
        public const string DynamicEncounterMultikillTriple = "LeagueEfficiencyDynamicEncounterMultikillTriple";
        public const string DynamicEncounterMultikillQuadra = "LeagueEfficiencyDynamicEncounterMultikillQuadra";
        public const string DynamicEncounterMultikillPenta = "LeagueEfficiencyDynamicEncounterMultikillPenta";
        public const string DynamicEncounterTagStrip = "LeagueEfficiencyDynamicEncounterTagStrip";
        public const string DynamicEncounterTagOverflow = "LeagueEfficiencyDynamicEncounterTagOverflow";
        public const string DynamicEncounterTagMore = "LeagueEfficiencyDynamicEncounterTagMore";
        public const string DynamicEncounterLeaderTitle = "LeagueEfficiencyDynamicEncounterLeaderTitle";
        public const string DynamicEncounterLeaderDamageMatch = "LeagueEfficiencyDynamicEncounterLeaderDamageMatch";
        public const string DynamicEncounterLeaderDamageTeam = "LeagueEfficiencyDynamicEncounterLeaderDamageTeam";
        public const string DynamicEncounterLeaderHealMatch = "LeagueEfficiencyDynamicEncounterLeaderHealMatch";
        public const string DynamicEncounterLeaderHealTeam = "LeagueEfficiencyDynamicEncounterLeaderHealTeam";
        public const string DynamicEncounterLeaderTakenMatch = "LeagueEfficiencyDynamicEncounterLeaderTakenMatch";
        public const string DynamicEncounterLeaderTakenTeam = "LeagueEfficiencyDynamicEncounterLeaderTakenTeam";
        public const string DynamicEncounterLeaderGoldMatch = "LeagueEfficiencyDynamicEncounterLeaderGoldMatch";
        public const string DynamicEncounterLeaderGoldTeam = "LeagueEfficiencyDynamicEncounterLeaderGoldTeam";
        public const string DynamicEncounterLeaderEfficiencyMatch = "LeagueEfficiencyDynamicEncounterLeaderEfficiencyMatch";
        public const string DynamicEncounterLeaderEfficiencyTeam = "LeagueEfficiencyDynamicEncounterLeaderEfficiencyTeam";
        public const string DynamicEncounterLeaderTowerMatch = "LeagueEfficiencyDynamicEncounterLeaderTowerMatch";
        public const string DynamicEncounterLeaderTowerTeam = "LeagueEfficiencyDynamicEncounterLeaderTowerTeam";
        public const string DynamicEncounterLeaderShieldMatch = "LeagueEfficiencyDynamicEncounterLeaderShieldMatch";
        public const string DynamicEncounterLeaderShieldTeam = "LeagueEfficiencyDynamicEncounterLeaderShieldTeam";
        public const string DynamicEncounterLeaderSolo = "LeagueEfficiencyDynamicEncounterLeaderSolo";
        public const string DynamicEncounterLeaderCs = "LeagueEfficiencyDynamicEncounterLeaderCs";
        public const string DynamicEncounterLeaderCsAdvantage = "LeagueEfficiencyDynamicEncounterLeaderCsAdvantage";
        public const string DynamicEncounterLeaderKnockUp = "LeagueEfficiencyDynamicEncounterLeaderKnockUp";
        public const string DynamicEncounterLeaderTowerDive = "LeagueEfficiencyDynamicEncounterLeaderTowerDive";
        public const string DynamicEncounterLeaderTowerUnder = "LeagueEfficiencyDynamicEncounterLeaderTowerUnder";
        public const string DynamicEncounterLeaderKillsMatch = "LeagueEfficiencyDynamicEncounterLeaderKillsMatch";
        public const string DynamicEncounterLeaderKillsTeam = "LeagueEfficiencyDynamicEncounterLeaderKillsTeam";
        public const string DynamicEncounterLeaderKpMatch = "LeagueEfficiencyDynamicEncounterLeaderKpMatch";
        public const string DynamicEncounterLeaderKpTeam = "LeagueEfficiencyDynamicEncounterLeaderKpTeam";
        public const string DynamicEncounterLeaderCcMatch = "LeagueEfficiencyDynamicEncounterLeaderCcMatch";
        public const string DynamicEncounterLeaderCcTeam = "LeagueEfficiencyDynamicEncounterLeaderCcTeam";
        public const string DynamicEncounterScoreboardIconsItems = "LeagueEfficiencyDynamicEncounterScoreboardIconsItems";
        public const string DynamicEncounterScoreboardIconsBoundItem = "LeagueEfficiencyDynamicEncounterScoreboardIconsBoundItem";
        public const string DynamicEncounterScoreboardIconsSpells = "LeagueEfficiencyDynamicEncounterScoreboardIconsSpells";
        public const string DynamicEncounterScoreboardIconsPerks = "LeagueEfficiencyDynamicEncounterScoreboardIconsPerks";
        public const string DynamicEncounterScoreboardIconsPrimary = "LeagueEfficiencyDynamicEncounterScoreboardIconsPrimary";
        public const string DynamicEncounterScoreboardIconsSecondary = "LeagueEfficiencyDynamicEncounterScoreboardIconsSecondary";
        public const string DynamicEncounterScoreboardIconsAugments = "LeagueEfficiencyDynamicEncounterScoreboardIconsAugments";
        public const string DynamicEncounterAugmentPrismatic = "LeagueEfficiencyDynamicEncounterAugmentPrismatic";
        public const string DynamicEncounterAugmentGold = "LeagueEfficiencyDynamicEncounterAugmentGold";
        public const string DynamicEncounterAugmentSilver = "LeagueEfficiencyDynamicEncounterAugmentSilver";
        public const string DynamicEncounterAugmentBronze = "LeagueEfficiencyDynamicEncounterAugmentBronze";
        public const string DynamicEncounterAugmentEvent = "LeagueEfficiencyDynamicEncounterAugmentEvent";
        public const string DynamicEncounterRuneDetailsTab = "LeagueEfficiencyDynamicEncounterRuneDetailsTab";
        public const string DynamicEncounterRuneStatShards = "LeagueEfficiencyDynamicEncounterRuneStatShards";
        public const string DynamicEncounterRuneSelectionHint = "LeagueEfficiencyDynamicEncounterRuneSelectionHint";
        public const string DynamicEncounterTeamBlue = "LeagueEfficiencyDynamicEncounterTeamBlue";
        public const string DynamicEncounterTeamRed = "LeagueEfficiencyDynamicEncounterTeamRed";
        public const string DynamicEncounterTeamOther = "LeagueEfficiencyDynamicEncounterTeamOther";
        public const string DynamicEncounterCherryTeam = "LeagueEfficiencyDynamicEncounterCherryTeam";
        public const string DynamicEncounterCherryPlacement = "LeagueEfficiencyDynamicEncounterCherryPlacement";
        public const string DynamicEncounterCherrySummary = "LeagueEfficiencyDynamicEncounterCherrySummary";
        public const string DynamicEncounterCherryInspect = "LeagueEfficiencyDynamicEncounterCherryInspect";
        public const string DynamicEncounterTeamUnknownResult = "LeagueEfficiencyDynamicEncounterTeamUnknownResult";
        public const string DynamicEncounterTeamSummary = "LeagueEfficiencyDynamicEncounterTeamSummary";
        public const string DynamicEncounterTeamInspect = "LeagueEfficiencyDynamicEncounterTeamInspect";
        public const string DynamicEncounterTeamUnknown = "LeagueEfficiencyDynamicEncounterTeamUnknown";
        public const string DynamicEncounterTeamNone = "LeagueEfficiencyDynamicEncounterTeamNone";
        public const string DynamicEncounterTeamYes = "LeagueEfficiencyDynamicEncounterTeamYes";
        public const string DynamicEncounterTeamNo = "LeagueEfficiencyDynamicEncounterTeamNo";
        public const string DynamicEncounterTeamBanTurn = "LeagueEfficiencyDynamicEncounterTeamBanTurn";
        public const string DynamicEncounterTeamBans = "LeagueEfficiencyDynamicEncounterTeamBans";
        public const string DynamicEncounterTeamFirsts = "LeagueEfficiencyDynamicEncounterTeamFirsts";
        public const string DynamicRefreshNeeded = "LeagueEfficiencyDynamicRefreshNeeded";
        public const string DynamicSelectPlayers = "LeagueEfficiencyDynamicSelectPlayers";
        public const string DynamicSending = "LeagueEfficiencyDynamicSending";
        public const string PremadeSelectGroups = "LeagueEfficiencyPremadeSelectGroups";
        public const string PremadeGroupCount = "LeagueEfficiencyPremadeGroupCount";
        public const string PremadeNoGroups = "LeagueEfficiencyPremadeNoGroups";
        public const string PremadeMessageTitle = "LeagueEfficiencyPremadeMessageTitle";
        public const string JungleTab = "LeagueEfficiencyJungleTab";
        public const string JungleLoad = "LeagueEfficiencyJungleLoad";
        public const string JungleLoading = "LeagueEfficiencyJungleLoading";
        public const string JungleSelectHint = "LeagueEfficiencyJungleSelectHint";
        public const string JungleActivity = "LeagueEfficiencyJungleActivity";
        public const string JungleFirstClear = "LeagueEfficiencyJungleFirstClear";
        public const string JungleEarlyGank = "LeagueEfficiencyJungleEarlyGank";
        public const string JungleDragon = "LeagueEfficiencyJungleDragon";
        public const string JungleMonsters = "LeagueEfficiencyJungleMonsters";
        public const string JungleChampions = "LeagueEfficiencyJungleChampions";
        public const string ProfileModeDefault = "LeagueEfficiencyProfileModeDefault";
        public const string ProfileModeRift = "LeagueEfficiencyProfileModeRift";
        public const string ProfileModeAram = "LeagueEfficiencyProfileModeAram";
        public const string ProfileModeMayhem = "LeagueEfficiencyProfileModeMayhem";
        public const string ProfileRoleDefault = "LeagueEfficiencyProfileRoleDefault";
        public const string ProfileRoleTop = "LeagueEfficiencyProfileRoleTop";
        public const string ProfileRoleJungle = "LeagueEfficiencyProfileRoleJungle";
        public const string ProfileRoleMiddle = "LeagueEfficiencyProfileRoleMiddle";
        public const string ProfileRoleBottom = "LeagueEfficiencyProfileRoleBottom";
        public const string ProfileRoleSupport = "LeagueEfficiencyProfileRoleSupport";
        public const string StrategyImmediate = "LeagueEfficiencyStrategyImmediate";
        public const string StrategyShowLock = "LeagueEfficiencyStrategyShowLock";
        public const string StrategyShowOnly = "LeagueEfficiencyStrategyShowOnly";
        public const string MinPartySize = "LeagueEfficiencyMinPartySize";
        public const string WaitForInvitees = "LeagueEfficiencyWaitForInvitees";
        public const string WaitForInviteesHint = "LeagueEfficiencyWaitForInviteesHint";
        public const string MatchmakingRoomNotices = "LeagueEfficiencyMatchmakingRoomNotices";
        public const string MatchmakingRoomNoticesHint = "LeagueEfficiencyMatchmakingRoomNoticesHint";
        public const string MinPartySizeHint = "LeagueEfficiencyMinPartySizeHint";
        public const string MatchmakingDelay = "LeagueEfficiencyMatchmakingDelay";
        public const string MatchmakingDelayHint = "LeagueEfficiencyMatchmakingDelayHint";
        public const string AcceptDelay = "LeagueEfficiencyAcceptDelay";
        public const string AcceptDelayHint = "LeagueEfficiencyAcceptDelayHint";
        public const string MatchmakingStopPolicy = "LeagueEfficiencyMatchmakingStopPolicy";
        public const string MatchmakingStopPolicyHint = "LeagueEfficiencyMatchmakingStopPolicyHint";
        public const string MatchmakingStopNever = "LeagueEfficiencyMatchmakingStopNever";
        public const string MatchmakingStopFixed = "LeagueEfficiencyMatchmakingStopFixed";
        public const string MatchmakingStopEstimated = "LeagueEfficiencyMatchmakingStopEstimated";
        public const string MatchmakingStopAfter = "LeagueEfficiencyMatchmakingStopAfter";
        public const string MatchmakingStopAfterHint = "LeagueEfficiencyMatchmakingStopAfterHint";
        public const string NextGameSaved = "LeagueEfficiencyNextGameSaved";
    }

    internal static class LeagueEfficiencyText
    {
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { LeagueEfficiencyUiTextKeys.Menu, "快捷工具" },
            { LeagueEfficiencyUiTextKeys.WindowTitle, "GGman · 快捷工具" },
            { LeagueEfficiencyUiTextKeys.Title, "快捷工具" },
            { LeagueEfficiencyUiTextKeys.Hint, "常用操作都放这里；把鼠标移到选项上方可查看说明。" },
            { LeagueEfficiencyUiTextKeys.HotkeySection, "快捷操作" },
            { LeagueEfficiencyUiTextKeys.ExitGame, "一键结束游戏" },
            { LeagueEfficiencyUiTextKeys.ExitGameHint, "立即结束当前英雄联盟游戏进程；与“跳过卡结算”是两个不同功能。" },
            { LeagueEfficiencyUiTextKeys.CloseLobby, "快速关闭大厅" },
            { LeagueEfficiencyUiTextKeys.CloseLobbyHint, "直接关闭英雄联盟大厅进程；游戏正在运行时也可以执行。" },
            { LeagueEfficiencyUiTextKeys.Capture, "设置" },
            { LeagueEfficiencyUiTextKeys.Clear, "清除" },
            { LeagueEfficiencyUiTextKeys.Save, "保存快捷键" },
            { LeagueEfficiencyUiTextKeys.Disabled, "未设置" },
            { LeagueEfficiencyUiTextKeys.CaptureTitle, "设置快捷键" },
            { LeagueEfficiencyUiTextKeys.CapturePrompt, "按下要使用的快捷键。Esc 清除；单独字母或数字不会保存。" },
            { LeagueEfficiencyUiTextKeys.CaptureUnsafe, "这个按键容易误触，请加 Ctrl / Alt / Shift / Win，或改用 F1-F12。" },
            { LeagueEfficiencyUiTextKeys.Saved, "快捷键已保存并立即生效。" },
            { LeagueEfficiencyUiTextKeys.SaveFailed, "快捷键没有修改：{0}" },
            { LeagueEfficiencyUiTextKeys.PostGameSection, "赛后处理" },
            { LeagueEfficiencyUiTextKeys.AutoHonor, "赛后自动点赞" },
            { LeagueEfficiencyUiTextKeys.AutoHonorHint, "按右侧策略选择点赞对象，读取新版结算名单并验证写入；无法确认时不会盲目重复点赞。" },
            { LeagueEfficiencyUiTextKeys.HonorStrategyTitle, "点赞对象策略" },
            { LeagueEfficiencyUiTextKeys.HonorPreferParty, "优先组队成员" },
            { LeagueEfficiencyUiTextKeys.HonorOnlyParty, "仅组队成员" },
            { LeagueEfficiencyUiTextKeys.HonorAnyAlly, "随机队友" },
            { LeagueEfficiencyUiTextKeys.HonorOptOut, "跳过点赞" },
            { LeagueEfficiencyUiTextKeys.HonorIncludingOpponent, "队友与对手" },
            { LeagueEfficiencyUiTextKeys.HonorLastNone, "上局点赞：暂无记录" },
            { LeagueEfficiencyUiTextKeys.HonorLastSuccess, "上局点赞：成功 · {0}" },
            { LeagueEfficiencyUiTextKeys.HonorLastSkipped, "上局点赞：未执行 · {0}" },
            { LeagueEfficiencyUiTextKeys.HonorLastUnknown, "上局点赞：已提交，未确认 · {0}" },
            { LeagueEfficiencyUiTextKeys.HonorLastFailed, "上局点赞：失败 · {0}" },
            { LeagueEfficiencyUiTextKeys.AutoReturn, "自动回大厅" },
            { LeagueEfficiencyUiTextKeys.AutoReturnHint, "结算时先给点赞流程留出机会，再自动返回大厅；点赞失败也不会一直卡住。" },
            { LeagueEfficiencyUiTextKeys.PostGameSaved, "赛后处理设置已保存。" },
            { LeagueEfficiencyUiTextKeys.NextGameSection, "下一局" },
            { LeagueEfficiencyUiTextKeys.AutoMatchmaking, "自动开始排队" },
            { LeagueEfficiencyUiTextKeys.AutoMatchmakingHint, "你是房主且队伍满足开局条件时自动开始排队；条件不满足就不操作。" },
            { LeagueEfficiencyUiTextKeys.AutoAccept, "自动接受对局" },
            { LeagueEfficiencyUiTextKeys.AutoAcceptHint, "匹配成功后自动接受一次；已经接受或你主动拒绝时不会反复操作。" },
            { LeagueEfficiencyUiTextKeys.AutoReconnect, "断线后自动重连" },
            { LeagueEfficiencyUiTextKeys.AutoReconnectHint, "游戏客户端进入重连状态后等待 10 秒，再确认状态并尝试重连一次；离开该状态或关闭开关就取消。默认关闭。" },
            { LeagueEfficiencyUiTextKeys.RespawnOverlay, "游戏内复活倒计时" },
            { LeagueEfficiencyUiTextKeys.RespawnOverlayHint, "对局中阵亡时显示轻量悬浮倒计时；只读取本机游戏接口，不抢游戏焦点。默认关闭。" },
            { LeagueEfficiencyUiTextKeys.RespawnOverlaySaved, "复活倒计时设置已保存。" },
            { LeagueEfficiencyUiTextKeys.QuickMessageOpen, "快捷消息预设…" },
            { LeagueEfficiencyUiTextKeys.QuickMessageTitle, "GGman · 快捷消息" },
            { LeagueEfficiencyUiTextKeys.QuickMessageHint, "常用消息与动态战绩摘要；房间/选人可点发送，游戏内使用快捷键。固定消息每条最多四行、300 字。" },
            { LeagueEfficiencyUiTextKeys.QuickMessageSave, "保存四条预设" },
            { LeagueEfficiencyUiTextKeys.QuickMessageSend, "发送" },
            { LeagueEfficiencyUiTextKeys.QuickMessageSaved, "四条预设已保存。" },
            { LeagueEfficiencyUiTextKeys.QuickMessageSent, "消息已提交到当前聊天室。" },
            { LeagueEfficiencyUiTextKeys.QuickMessageUnavailable, "当前无法确认可发送的聊天室，未发送消息。" },
            { LeagueEfficiencyUiTextKeys.QuickMessageInvalid, "消息为空或超出四行、300 字的限制。" },
            { LeagueEfficiencyUiTextKeys.QuickMessageShortcut, "发送快捷键" },
            { LeagueEfficiencyUiTextKeys.QuickMessageCancel, "取消发送" },
            { LeagueEfficiencyUiTextKeys.QuickMessageInterval, "逐行间隔（毫秒）" },
            { LeagueEfficiencyUiTextKeys.LobbyInvitationSection, "组队与邀请" },
            { LeagueEfficiencyUiTextKeys.HandleInvitations, "自动处理收到的组队邀请" },
            { LeagueEfficiencyUiTextKeys.HandleInvitationsHint, "按下方模式策略接受或拒绝邀请。所有模式默认忽略；关闭时不处理邀请。" },
            { LeagueEfficiencyUiTextKeys.InvitationGameType, "组队邀请模式" },
            { LeagueEfficiencyUiTextKeys.InvitationGameTypeHint, "选择一种邀请模式后，设置右侧的动作。<DEFAULT> 是未单独设置模式时的默认策略。" },
            { LeagueEfficiencyUiTextKeys.InvitationAction, "当前模式邀请策略" },
            { LeagueEfficiencyUiTextKeys.InvitationActionHint, "忽略：不操作；接受：自动入队；拒绝：自动拒绝。保存后立即对新邀请生效。" },
            { LeagueEfficiencyUiTextKeys.InvitationIgnore, "忽略" },
            { LeagueEfficiencyUiTextKeys.InvitationAccept, "接受" },
            { LeagueEfficiencyUiTextKeys.InvitationDecline, "拒绝" },
            { LeagueEfficiencyUiTextKeys.SkipInvitationsWhenAway, "离开状态不处理邀请" },
            { LeagueEfficiencyUiTextKeys.SkipInvitationsWhenAwayHint, "聊天状态为离开时不自动接受或拒绝。状态无法核实时，也不会擅自处理。" },
            { LeagueEfficiencyUiTextKeys.AutoSkipLeader, "自动让出房主" },
            { LeagueEfficiencyUiTextKeys.AutoSkipLeaderHint, "组队大厅中自己为房主且有其他真人成员时，优先把房主交给已准备成员；默认关闭。" },
            { LeagueEfficiencyUiTextKeys.AramTeamSideSection, "大乱斗选人提醒" },
            { LeagueEfficiencyUiTextKeys.AramTeamSide, "自动提示蓝方 / 红方" },
            { LeagueEfficiencyUiTextKeys.AramTeamSideHint, "仅限大乱斗、海克斯大乱斗选人阶段，在聊天窗口提示自己的阵营；默认关闭。" },
            { LeagueEfficiencyUiTextKeys.AramTeamSideVisible, "让队友也看见阵营提示" },
            { LeagueEfficiencyUiTextKeys.AramTeamSideVisibleHint, "开启后会向选人聊天室发普通消息，队友可见；关闭时使用仅本地提示。" },
            { LeagueEfficiencyUiTextKeys.LoginSignatureSection, "登录显示设置" },
            { LeagueEfficiencyUiTextKeys.LoginSignatureText, "保存登录签名" },
            { LeagueEfficiencyUiTextKeys.LoginSignatureTextHint, "填入希望登录后显示的游戏签名并保存；这里的文字仅用于您主动开启的自动恢复。" },
            { LeagueEfficiencyUiTextKeys.LoginSignatureSave, "保存" },
            { LeagueEfficiencyUiTextKeys.LoginSignatureAuto, "登录后自动恢复签名" },
            { LeagueEfficiencyUiTextKeys.LoginSignatureAutoHint, "聊天服务就绪后延迟约 2 秒，仅尝试一次；若期间签名已被改动，则不覆盖。默认关闭。" },
            { LeagueEfficiencyUiTextKeys.LoginSignatureSaved, "登录签名设置已保存。" },
            { LeagueEfficiencyUiTextKeys.LoginRankAuto, "登录后自动恢复展示段位" },
            { LeagueEfficiencyUiTextKeys.LoginRankAutoHint, "聊天就绪后恢复你保存的展示段位，至多写一次；检测到期间手动更改就放弃。与签名共用登录检测，默认关闭。" },
            { LeagueEfficiencyUiTextKeys.LoginRankQueue, "展示段位队列" },
            { LeagueEfficiencyUiTextKeys.LoginRankQueueHint, "选择要展示段位的游戏队列，支持单排、灵活组排及其他客户端队列。" },
            { LeagueEfficiencyUiTextKeys.LoginRankTier, "展示段位等级" },
            { LeagueEfficiencyUiTextKeys.LoginRankTierHint, "选择要恢复的段位；大师及以上不需要填写小段。" },
            { LeagueEfficiencyUiTextKeys.LoginRankDivision, "展示段位小段" },
            { LeagueEfficiencyUiTextKeys.LoginRankDivisionHint, "选择 I / II / III / IV；大师、宗师与王者不会发送小段。" },
            { LeagueEfficiencyUiTextKeys.LoginRankSaved, "登录展示段位设置已保存。" },
            { LeagueEfficiencyUiTextKeys.AutoReply, "私聊自动回复" },
            { LeagueEfficiencyUiTextKeys.AutoReplyHint, "收到新的私聊时发送已保存的回复，默认关闭；离开输入框后保存，最多500字。" },
            { LeagueEfficiencyUiTextKeys.ReplyAway, "仅离开时回复" },
            { LeagueEfficiencyUiTextKeys.ReplyAwayHint, "开启后，只有大厅当前状态为离开时才自动回复。" },
            { LeagueEfficiencyUiTextKeys.ReplyText, "自动回复内容" },
            { LeagueEfficiencyUiTextKeys.OfflineLock, "维持离线显示" },
            { LeagueEfficiencyUiTextKeys.OfflineLockHint, "已观察到离线后，若大厅将状态改回在线或离开，会通过状态事件再次核实并恢复离线；默认关闭。" },
            { LeagueEfficiencyUiTextKeys.OfflineLockSaved, "离线显示设置已保存。" },
            { LeagueEfficiencyUiTextKeys.LobbyInvitationSaved, "组队和邀请设置已保存。" },
            { LeagueEfficiencyUiTextKeys.FriendInviteOpen, "好友上线后自动邀请…" },
            { LeagueEfficiencyUiTextKeys.FriendInviteTitle, "GGman · 好友邀请排队" },
            { LeagueEfficiencyUiTextKeys.FriendInviteHint, "在组队大厅勾选好友，等其状态变为在线时尝试邀请一次；离开大厅即清空。" },
            { LeagueEfficiencyUiTextKeys.FriendInviteAnnounce, "邀请成功后在组队聊天中提示（仅当前房间）" },
            { LeagueEfficiencyUiTextKeys.FriendInviteSearch, "搜索游戏昵称" },
            { LeagueEfficiencyUiTextKeys.FriendInviteRefresh, "刷新好友" },
            { LeagueEfficiencyUiTextKeys.FriendInviteName, "游戏好友" },
            { LeagueEfficiencyUiTextKeys.FriendInviteAvailability, "当前状态" },
            { LeagueEfficiencyUiTextKeys.FriendInviteLoading, "正在读取当前大厅与好友…" },
            { LeagueEfficiencyUiTextKeys.FriendInviteUnavailable, "无法读取好友或当前不在组队大厅。" },
            { LeagueEfficiencyUiTextKeys.FriendInviteCount, "等待邀请：{0} 人（最多 5 人）" },
            { LeagueEfficiencyUiTextKeys.FriendInviteRejected, "操作未生效：大厅已变更、好友已尝试邀请或排队已满。请刷新。" },
            { LeagueEfficiencyUiTextKeys.FriendInviteClose, "关闭" },
            { LeagueEfficiencyUiTextKeys.FriendInviteOnline, "在线" },
            { LeagueEfficiencyUiTextKeys.FriendInviteInGame, "游戏中" },
            { LeagueEfficiencyUiTextKeys.FriendInviteAway, "离开" },
            { LeagueEfficiencyUiTextKeys.FriendInviteOffline, "离线" },
            { LeagueEfficiencyUiTextKeys.AutoSelectSection, "自动选禁英雄" },
            { LeagueEfficiencyUiTextKeys.AutoPick, "自动锁定英雄" },
            { LeagueEfficiencyUiTextKeys.AutoPickHint, "轮到自己选人时，先确认目标英雄可选，且你没有手动选其他英雄，再尝试锁定一次。默认关闭。" },
            { LeagueEfficiencyUiTextKeys.AutoPickChampion, "优先选择" },
            { LeagueEfficiencyUiTextKeys.AutoPickChampionHint, "从当前客户端英雄列表选一个默认目标。后续版本继续补齐分路、多英雄优先级和亮出后锁定策略。" },
            { LeagueEfficiencyUiTextKeys.AutoBan, "自动禁用英雄" },
            { LeagueEfficiencyUiTextKeys.AutoBanHint, "轮到自己禁用时，确认英雄在可禁列表中，才提交一次禁用。默认关闭。" },
            { LeagueEfficiencyUiTextKeys.AutoBanChampion, "优先禁用" },
            { LeagueEfficiencyUiTextKeys.AutoBanChampionHint, "从当前客户端英雄列表选一个默认禁用目标；已禁、不可禁时不执行。" },
            { LeagueEfficiencyUiTextKeys.ChampionNotSet, "未设置英雄" },
            { LeagueEfficiencyUiTextKeys.ChampionCatalogUnavailable, "无法读取英雄列表；请先进入 LOL 大厅，再重新打开此页面。" },
            { LeagueEfficiencyUiTextKeys.AutoSelectSaved, "自动选禁设置已保存。" },
            { LeagueEfficiencyUiTextKeys.AutoSelectPause, "临时暂停自动选禁" },
            { LeagueEfficiencyUiTextKeys.AutoSelectPauseHint, "立即取消本次运行的自动选、禁、候选席与英雄交换任务；取消勾选后恢复。不会改动已保存的设置。" },
            { LeagueEfficiencyUiTextKeys.AdvancedOpen, "模式、分路与候选优先级设置…" },
            { LeagueEfficiencyUiTextKeys.AdvancedTitle, "GGman · 自动选禁详细设置" },
            { LeagueEfficiencyUiTextKeys.AdvancedHint, "按模式和分路保存候选顺序；轮到自己时，从前往后选择第一个可用英雄。更改在保存后生效。" },
            { LeagueEfficiencyUiTextKeys.AdvancedMode, "游戏模式" },
            { LeagueEfficiencyUiTextKeys.AdvancedRole, "分路" },
            { LeagueEfficiencyUiTextKeys.AdvancedAction, "操作类型" },
            { LeagueEfficiencyUiTextKeys.AdvancedPriority, "英雄优先级" },
            { LeagueEfficiencyUiTextKeys.AdvancedAdd, "添加英雄" },
            { LeagueEfficiencyUiTextKeys.AdvancedRemove, "移除" },
            { LeagueEfficiencyUiTextKeys.AdvancedUp, "上移" },
            { LeagueEfficiencyUiTextKeys.AdvancedDown, "下移" },
            { LeagueEfficiencyUiTextKeys.AdvancedStrategy, "执行策略" },
            { LeagueEfficiencyUiTextKeys.AdvancedDelay, "延迟（秒）" },
            { LeagueEfficiencyUiTextKeys.AdvancedBenchControls, "候选席规则" },
            { LeagueEfficiencyUiTextKeys.AdvancedIntentControls, "预选规则" },
            { LeagueEfficiencyUiTextKeys.AdvancedShowIntent, "轮到选人前自动预选" },
            { LeagueEfficiencyUiTextKeys.AdvancedIgnoreIntent, "忽略队友的英雄预选" },
            { LeagueEfficiencyUiTextKeys.AdvancedBenchSwap, "自动换候选席英雄（需开启自动选人）" },
            { LeagueEfficiencyUiTextKeys.AdvancedBenchUpgrade, "发现更高优先级英雄时继续换" },
            { LeagueEfficiencyUiTextKeys.AdvancedTrade, "按优先级自动接受或拒绝交换" },
            { LeagueEfficiencyUiTextKeys.AdvancedBenchDelay, "候选席等待（秒）" },
            { LeagueEfficiencyUiTextKeys.AdvancedSave, "保存" },
            { LeagueEfficiencyUiTextKeys.AdvancedCancel, "取消" },
            { LeagueEfficiencyUiTextKeys.ProfileModeRanked, "排位模式" },
            { LeagueEfficiencyUiTextKeys.ProfileModeNormal, "普通模式" },
            { LeagueEfficiencyUiTextKeys.ProfileModeCherry, "斗魂竞技场" },
            { LeagueEfficiencyUiTextKeys.ProfileModeUrf, "无限火力" },
            { LeagueEfficiencyUiTextKeys.ProfileModeOneForAll, "克隆模式" },
            { LeagueEfficiencyUiTextKeys.ProfileModeUltbook, "终极魔典" },
            { LeagueEfficiencyUiTextKeys.ProfileModeBot, "人机模式" },
            { LeagueEfficiencyUiTextKeys.ProfileModeCustom, "自定义对局" },
            { LeagueEfficiencyUiTextKeys.AdvancedGroupEnabled, "启用本组当前操作" },
            { LeagueEfficiencyUiTextKeys.QuickMessageFixedTab, "固定消息" },
            { LeagueEfficiencyUiTextKeys.DynamicKda, "KDA" },
            { LeagueEfficiencyUiTextKeys.DynamicPartial, "部分玩家身份或战绩暂不可用，请查看预览中的提示。" },
            { LeagueEfficiencyUiTextKeys.DynamicGameHint, "游戏内请保存选项并使用己方、敌方或双方快捷键发送，确保游戏处于前台。" },
            { LeagueEfficiencyUiTextKeys.DynamicShortcutHint, "快捷键：点击设置、右键清除，保存后生效。游戏内发送前请切回游戏窗口。" },
            { LeagueEfficiencyUiTextKeys.DynamicOpen, "动态对局消息" },
            { LeagueEfficiencyUiTextKeys.DynamicLoad, "读取当前对局" },
            { LeagueEfficiencyUiTextKeys.DynamicCompleteMissing, "补齐缺失战绩" },
            { LeagueEfficiencyUiTextKeys.DynamicSend, "发送已选摘要" },
            { LeagueEfficiencyUiTextKeys.DynamicLoading, "正在读取当前对局与近期战绩…" },
            { LeagueEfficiencyUiTextKeys.DynamicReady, "已生成预览。请选择玩家及显示项后发送；游戏内请先切回游戏窗口。" },
            { LeagueEfficiencyUiTextKeys.DynamicUnavailable, "数据暂不可用" },
            { LeagueEfficiencyUiTextKeys.DynamicNoChampion, "尚未选择英雄" },
            { LeagueEfficiencyUiTextKeys.DynamicNoMatches, "暂无近期记录" },
            { LeagueEfficiencyUiTextKeys.DynamicNoChampionMatches, "此英雄暂无近期记录" },
            { LeagueEfficiencyUiTextKeys.DynamicPlayer, "玩家" },
            { LeagueEfficiencyUiTextKeys.DynamicMatchCount, "近{0}场" },
            { LeagueEfficiencyUiTextKeys.DynamicWinRate, "胜率" },
            { LeagueEfficiencyUiTextKeys.DynamicSoloKills, "场均单杀" },
            { LeagueEfficiencyUiTextKeys.DynamicVision, "场均视野" },
            { LeagueEfficiencyUiTextKeys.DynamicDamage, "伤害占比" },
            { LeagueEfficiencyUiTextKeys.DynamicTaken, "承伤占比" },
            { LeagueEfficiencyUiTextKeys.DynamicGold, "经济占比" },
            { LeagueEfficiencyUiTextKeys.DynamicCs, "场均补刀/分" },
            { LeagueEfficiencyUiTextKeys.DynamicParticipation, "参团率" },
            { LeagueEfficiencyUiTextKeys.DynamicEfficiency, "伤害经济比" },
            { LeagueEfficiencyUiTextKeys.DynamicChampions, "主玩英雄" },
            { LeagueEfficiencyUiTextKeys.DynamicPositions, "主玩位置" },
            { LeagueEfficiencyUiTextKeys.DynamicNoMain, "暂无稳定主玩英雄" },
            { LeagueEfficiencyUiTextKeys.DynamicFriendly, "己方" },
            { LeagueEfficiencyUiTextKeys.DynamicEnemy, "敌方" },
            { LeagueEfficiencyUiTextKeys.DynamicAll, "双方" },
            { LeagueEfficiencyUiTextKeys.DynamicNames, "玩家名称" },
            { LeagueEfficiencyUiTextKeys.DynamicChampionNames, "优先英雄名" },
            { LeagueEfficiencyUiTextKeys.DynamicCombinedNames, "英雄名与玩家名" },
            { LeagueEfficiencyUiTextKeys.DynamicCurrentChampion, "只统计当前英雄" },
            { LeagueEfficiencyUiTextKeys.DynamicSaved, "动态消息选项已保存。" },
            { LeagueEfficiencyUiTextKeys.DynamicTagEdit, "标记玩家…" },
            { LeagueEfficiencyUiTextKeys.DynamicTagRemove, "清除标记" },
            { LeagueEfficiencyUiTextKeys.DynamicTagTitle, "玩家标记（仅保存在本机）" },
            { LeagueEfficiencyUiTextKeys.DynamicTagCancel, "取消" },
            { LeagueEfficiencyUiTextKeys.DynamicTagSaved, "玩家标记已保存到本机。" },
            { LeagueEfficiencyUiTextKeys.DynamicTagUnavailable, "玩家标记保存失败，请检查本地数据文件。" },
            { LeagueEfficiencyUiTextKeys.DynamicTagTools, "标记管理" },
            { LeagueEfficiencyUiTextKeys.DynamicTagBrowse, "查看所有标记…" },
            { LeagueEfficiencyUiTextKeys.DynamicSavedPlayerAll, "全部历史玩家（含未标记）" },
            { LeagueEfficiencyUiTextKeys.DynamicSavedPlayerMetCount, "相遇次数" },
            { LeagueEfficiencyUiTextKeys.DynamicSavedPlayerLastMet, "最近相遇" },
            { LeagueEfficiencyUiTextKeys.DynamicTagSearch, "搜索标记或玩家编号：" },
            { LeagueEfficiencyUiTextKeys.DynamicTagTarget, "玩家编号" },
            { LeagueEfficiencyUiTextKeys.DynamicTagSelf, "标记者编号" },
            { LeagueEfficiencyUiTextKeys.DynamicTagRegion, "区服 / 平台" },
            { LeagueEfficiencyUiTextKeys.DynamicTagUpdated, "修改时间" },
            { LeagueEfficiencyUiTextKeys.DynamicTagPrevious, "上一页" },
            { LeagueEfficiencyUiTextKeys.DynamicTagNext, "下一页" },
            { LeagueEfficiencyUiTextKeys.DynamicTagPage, "第 {0}/{1} 页 · 共 {2} 条" },
            { LeagueEfficiencyUiTextKeys.DynamicTagDeleteConfirm, "确定删除选中玩家的本地标记？" },
            { LeagueEfficiencyUiTextKeys.DynamicTagChanged, "记录已变化，请重新选择。" },
            { LeagueEfficiencyUiTextKeys.DynamicTagImport, "导入 Akari 标记 JSON…" },
            { LeagueEfficiencyUiTextKeys.DynamicTagExport, "导出 Akari 标记 JSON…" },
            { LeagueEfficiencyUiTextKeys.DynamicTagImported, "已导入 {0} 条玩家标记。" },
            { LeagueEfficiencyUiTextKeys.DynamicTagExported, "已导出 {0} 条玩家标记。" },
            { LeagueEfficiencyUiTextKeys.DynamicTagExchangeFailed, "标记文件导入或导出失败，请检查 JSON 格式和文件权限。" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterHistory, "历史相遇记录…" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterTitle, "与此玩家相遇的历史对局（仅本机）" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterEmpty, "尚无本机历史相遇记录" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterDelete, "删除选中记录" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterConfirm, "确定删除此条本机相遇记录？" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterGame, "对局编号" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterQueue, "队列" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterDate, "记录时间" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterUnavailable, "相遇记录读取或删除失败，请检查本地记录文件。" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterFilter, "队列类型：" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterOrderNewest, "最新在前" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterOrderOldest, "最早在前" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterApply, "筛选" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterPage, "第 {0}/{1} 页 · 共 {2} 条" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterMatchDetail, "查看本场战绩" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterMatchLoading, "正在读取这场对局的详细战绩…" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterMatchUnavailable, "客户端未返回这场对局的完整战绩。" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterMatchNoClient, "LOL 客户端未连接或连接已更换。" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterMatchWrongAccount, "请登录记录所属的 LOL 账号及区服后重试。" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterMatchSummary, "{0} · 英雄 {1} · K/D/A {2}/{3}/{4} · 补刀 {5} · 时长 {6}" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterMatchWon, "胜利" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterMatchLost, "失败" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardTitle, "本场对局 · 玩家记分板" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterReplay, "下载 / 观看回放" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterReplayChecking, "正在检查回放状态…" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterReplayDownloadRequested, "已向客户端提交下载；稍后可再次点击观看。" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterReplayWatchRequested, "已向客户端提交播放请求。" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterReplayDownloading, "回放下载中（{0}%），稍后再试。" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterReplayIncompatible, "当前客户端版本无法播放这场回放。" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterReplayUnavailable, "这场回放暂时不可用，请检查客户端或稍后重试。" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardSummary, "对局 {0} · 模式 {1} · 时长 {2} · ★ 为所选玩家" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardTeam, "队伍" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardPlayer, "玩家" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardChampion, "英雄" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardCs, "补刀" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardDamage, "英雄伤害" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardGold, "金币" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardVision, "视野" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardResult, "结果" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardUnknown, "未提供名称" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardSelectionHint, "选择一名玩家查看装备、召唤师技能和符文。" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardSelection, "{0} · 英雄：{1} · 召唤师技能：{2}\r\n装备：{3} · 绑定装备：{7}\r\n符文：{4} · 流派：{5} · 强化：{6}" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardRates, "KDA {0} · 参团率 {1} · 伤害占比 {2}\r\n补刀/分 {3} · 金币/分 {4}" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardDamageSplit, "英雄输出 {0}（物理 {1} / 魔法 {2} / 真实 {3}）" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardTakenSplit, "承受伤害 {0}（物理 {1} / 魔法 {2} / 真实 {3}）" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardDealtBar, "英雄输出" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardTakenBar, "承受伤害" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardDamageTooltip, "{0} {1} · 全场峰值占比 {2}\r\n物理 {3}（{4}） · 魔法 {5}（{6}） · 真实 {7}（{8}）" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterRadarTitle, "表现雷达" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterRadarHint, "选择一名玩家，查看七项表现及同队平均。" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterRadarLegend, "玩家（红） / 同队平均（灰）\r\n括号内为相对本场最高值的比例；— 表示缺少证据。" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterRadarPlayerTip, "{0} · 玩家：{1}" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterRadarTeamTip, "{0} · 同队平均：{1}" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterRadarDamage, "输出" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterRadarTaken, "承伤" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterRadarGold, "金币" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterRadarCs, "补刀" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterRadarKda, "KDA" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterRadarParticipation, "参团" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterRadarHeal, "治疗" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterMultikillTitle, "连杀表现：{0}" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterMultikillTimes, "{0} ×{1}" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterMultikillDouble, "双杀" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterMultikillTriple, "三杀" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterMultikillQuadra, "四杀" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterMultikillPenta, "五杀" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterTagStrip, "对局表现标签" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterTagOverflow, "+{0}" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterTagMore, "更多对局表现标签 +{0}" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTitle, "对局表现：{0}" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderDamageMatch, "全场输出第一" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderDamageTeam, "队内输出第一" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderHealMatch, "全场治疗第一" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderHealTeam, "队内治疗第一" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTakenMatch, "全场承伤第一" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTakenTeam, "队内承伤第一" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderGoldMatch, "全场经济第一" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderGoldTeam, "队内经济第一" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderEfficiencyMatch, "全场伤害经济效率第一" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderEfficiencyTeam, "队内伤害经济效率第一" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTowerMatch, "全场推塔伤害第一" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTowerTeam, "队内推塔伤害第一" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderShieldMatch, "全场护盾第一" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderShieldTeam, "队内护盾第一" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderSolo, "单杀 ×{0}" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderCs, "全场补刀第一（{0}）" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderCsAdvantage, "补刀压制 ×{0}" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderKnockUp, "击飞配合击杀 ×{0}" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTowerDive, "越塔" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTowerUnder, "塔之子" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderKillsMatch, "全场击杀第一" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderKillsTeam, "队内击杀第一" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderKpMatch, "全场参团率第一" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderKpTeam, "队内参团率第一" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderCcMatch, "全场控制第一" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderCcTeam, "队内控制第一" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardIconsItems, "装备" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardIconsBoundItem, "绑定装备" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardIconsSpells, "技能" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardIconsPerks, "符文" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardIconsPrimary, "主系符文" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardIconsSecondary, "副系符文" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardIconsAugments, "强化" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterAugmentPrismatic, "棱彩" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterAugmentGold, "黄金" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterAugmentSilver, "白银" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterAugmentBronze, "青铜" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterAugmentEvent, "事件" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterRuneDetailsTab, "符文详情" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterRuneStatShards, "属性碎片" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterRuneSelectionHint, "选择一名玩家查看符文的局后效果及属性碎片。" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterTeamBlue, "蓝方" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterTeamRed, "红方" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterTeamOther, "队伍 {0}" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterCherryTeam, "斗魂队伍 {0}" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterCherryPlacement, "第{0}名" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterCherrySummary, "{0} · {1} · {2}人 · K/D/A {3}/{4}/{5}\r\n伤害 {6} · 金币 {7}" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterCherryInspect, "点击查看小队战绩" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterTeamUnknownResult, "结果未确认" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterTeamSummary, "{0} · {1} · {2}人 · K/D/A {3}/{4}/{5}\r\n伤害 {6} · 金币 {7}\r\n大龙 {8} · 小龙 {9} · 塔 {10} · 先锋 {11} · 水晶 {12} · 巢虫 {13}" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterTeamInspect, "点击查看禁用与首目标" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterTeamUnknown, "未知" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterTeamNone, "无" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterTeamYes, "是" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterTeamNo, "否" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterTeamBanTurn, "（顺位 {0}）" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterTeamBans, "禁用英雄：{0}" },
            { LeagueEfficiencyUiTextKeys.DynamicEncounterTeamFirsts, "首大龙 {0} · 首小龙 {1} · 首塔 {2} · 首水晶 {3}" },
            { LeagueEfficiencyUiTextKeys.DynamicRefreshNeeded, "对局、账号或阵容已变化，请重新读取。" },
            { LeagueEfficiencyUiTextKeys.DynamicSelectPlayers, "选择参与摘要的玩家" },
            { LeagueEfficiencyUiTextKeys.DynamicSending, "正在发送；可用已设置的取消快捷键停止。" },
            { LeagueEfficiencyUiTextKeys.PremadeSelectGroups, "疑似组队" },
            { LeagueEfficiencyUiTextKeys.PremadeGroupCount, "{0} 人同队记录" },
            { LeagueEfficiencyUiTextKeys.PremadeNoGroups, "当前历史未发现足够的重复同队证据（至少五局），不能判定无人组队。" },
            { LeagueEfficiencyUiTextKeys.PremadeMessageTitle, "疑似组队" },
            { LeagueEfficiencyUiTextKeys.JungleTab, "打野分析" },
            { LeagueEfficiencyUiTextKeys.JungleLoad, "读取所选打野" },
            { LeagueEfficiencyUiTextKeys.JungleLoading, "正在读取所选玩家的打野时间线…" },
            { LeagueEfficiencyUiTextKeys.JungleSelectHint, "勾选玩家后点击读取所选打野，可逐个读取；数据加载完毕后生成预览。" },
            { LeagueEfficiencyUiTextKeys.JungleActivity, "抓线偏好" },
            { LeagueEfficiencyUiTextKeys.JungleFirstClear, "开野分布" },
            { LeagueEfficiencyUiTextKeys.JungleEarlyGank, "三级/四级抓人" },
            { LeagueEfficiencyUiTextKeys.JungleDragon, "控龙" },
            { LeagueEfficiencyUiTextKeys.JungleMonsters, "史诗野怪" },
            { LeagueEfficiencyUiTextKeys.JungleChampions, "常用打野英雄" },
            { LeagueEfficiencyUiTextKeys.ProfileModeDefault, "默认" },
            { LeagueEfficiencyUiTextKeys.ProfileModeRift, "召唤师峡谷" },
            { LeagueEfficiencyUiTextKeys.ProfileModeAram, "普通大乱斗" },
            { LeagueEfficiencyUiTextKeys.ProfileModeMayhem, "海克斯大乱斗" },
            { LeagueEfficiencyUiTextKeys.ProfileRoleDefault, "通用" },
            { LeagueEfficiencyUiTextKeys.ProfileRoleTop, "上路" },
            { LeagueEfficiencyUiTextKeys.ProfileRoleJungle, "打野" },
            { LeagueEfficiencyUiTextKeys.ProfileRoleMiddle, "中路" },
            { LeagueEfficiencyUiTextKeys.ProfileRoleBottom, "下路" },
            { LeagueEfficiencyUiTextKeys.ProfileRoleSupport, "辅助" },
            { LeagueEfficiencyUiTextKeys.StrategyImmediate, "立即锁定" },
            { LeagueEfficiencyUiTextKeys.StrategyShowLock, "亮出后锁定" },
            { LeagueEfficiencyUiTextKeys.StrategyShowOnly, "只亮出" },
            { LeagueEfficiencyUiTextKeys.MinPartySize, "自动排队最低人数（人）" },
            { LeagueEfficiencyUiTextKeys.WaitForInvitees, "等待受邀玩家" },
            { LeagueEfficiencyUiTextKeys.WaitForInviteesHint, "开启：还有待回应的大厅邀请时暂不开始排队。关闭：仅按队长资格、人数等条件决定；默认开启。" },
            { LeagueEfficiencyUiTextKeys.MatchmakingRoomNotices, "房间倒计时播报" },
            { LeagueEfficiencyUiTextKeys.MatchmakingRoomNoticesHint, "开启后，排队前等待期间每秒向当前自定义房间发送倒计时；取消排队时发一次提示。仅在可确认自己是队长的房间内发送，默认关闭。" },
            { LeagueEfficiencyUiTextKeys.MinPartySizeHint, "只有当前大厅真人数量达到这个值时才自动开始排队；1-5 人。" },
            { LeagueEfficiencyUiTextKeys.MatchmakingDelay, "排队前等待（秒）" },
            { LeagueEfficiencyUiTextKeys.MatchmakingDelayHint, "进入大厅并满足条件后等待一段时间再开始排队；期间离开大厅会立即取消。" },
            { LeagueEfficiencyUiTextKeys.AcceptDelay, "接受对局延迟（秒）" },
            { LeagueEfficiencyUiTextKeys.AcceptDelayHint, "ReadyCheck 出现后等待指定时间再接受；离开 ReadyCheck 或关闭自动接受会立即取消。" },
            { LeagueEfficiencyUiTextKeys.MatchmakingStopPolicy, "停止匹配策略" },
            { LeagueEfficiencyUiTextKeys.MatchmakingStopPolicyHint, "自动匹配开启时，可选择永不停止、到固定时间停止，或超过客户端给出的预计排队时间后停止。只使用当前匹配状态，不新建 Gameflow 监听。" },
            { LeagueEfficiencyUiTextKeys.MatchmakingStopNever, "永不" },
            { LeagueEfficiencyUiTextKeys.MatchmakingStopFixed, "固定时间" },
            { LeagueEfficiencyUiTextKeys.MatchmakingStopEstimated, "超过队列预估时间" },
            { LeagueEfficiencyUiTextKeys.MatchmakingStopAfter, "退出匹配时间（秒）" },
            { LeagueEfficiencyUiTextKeys.MatchmakingStopAfterHint, "仅“固定时间”策略生效；排队达到该时长后停止当前匹配。离开匹配或进入 ReadyCheck 会取消待执行操作。" },
            { LeagueEfficiencyUiTextKeys.NextGameSaved, "下一局设置已保存。" }
        };

        public static string Get(UiTextCatalog ui, string key)
        {
            string fallback;
            if (!Defaults.TryGetValue(key ?? string.Empty, out fallback)) fallback = string.Empty;
            var resolved = ui == null ? fallback : ui.Get(key, fallback);

            // Earlier builds accidentally shipped two unrelated labels for the exit-game action.
            // Migrate only those known defaults so user-created custom wording remains untouched.
            if (string.Equals(key, LeagueEfficiencyUiTextKeys.ExitGame, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(resolved, "跳过卡结算", StringComparison.Ordinal) ||
                 string.Equals(resolved, "自动返回大厅", StringComparison.Ordinal)))
                return fallback;

            if (string.Equals(key, LeagueEfficiencyUiTextKeys.ExitGameHint, StringComparison.OrdinalIgnoreCase) &&
                (resolved.IndexOf("结算一直转圈", StringComparison.Ordinal) >= 0 ||
                 resolved.IndexOf("快速回到大厅", StringComparison.Ordinal) >= 0))
                return fallback;

            return resolved;
        }

        internal static IReadOnlyDictionary<string, string> DefaultsForSmokeTest()
        {
            return Defaults;
        }
    }
}
