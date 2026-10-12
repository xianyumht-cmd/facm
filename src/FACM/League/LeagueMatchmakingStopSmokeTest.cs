using System;
using System.Text;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueMatchmakingStopSmokeTest
    {
        public static void Validate()
        {
            ValidatePolicyCodec();
            ValidateSearchStateProjection();
            ValidateReadyCheckFence();
            ValidateWriteFence();
            AppSettings.ValidateMatchmakingStopPreferencesForSmokeTest();
        }

        private static void ValidatePolicyCodec()
        {
            Require(LeagueMatchmakingStopPolicyCodec.Parse("never") == LeagueMatchmakingStopPolicy.Never,
                "Matchmaking stop policy lost never mapping.");
            Require(LeagueMatchmakingStopPolicyCodec.Parse("FIXED") == LeagueMatchmakingStopPolicy.FixedTime,
                "Matchmaking stop policy lost fixed mapping.");
            Require(LeagueMatchmakingStopPolicyCodec.Parse("estimated") == LeagueMatchmakingStopPolicy.EstimatedTime,
                "Matchmaking stop policy lost estimated mapping.");
            Require(LeagueMatchmakingStopPolicyCodec.Parse("unexpected") == LeagueMatchmakingStopPolicy.Never,
                "Unknown matchmaking stop policy did not fail closed to never.");
        }

        private static void ValidateSearchStateProjection()
        {
            var below = LeagueMatchmakingStopController.ParseSearchState(Bytes(
                "{\"isCurrentlyInQueue\":true,\"timeInQueue\":41.5,\"estimatedQueueTime\":50}"));
            Require(below != null && below.IsCurrentlyInQueue && below.HasUsableEstimate,
                "Matchmaking stop parser lost queue estimate context.");
            Require(!LeagueMatchmakingStopController.ShouldStopForEstimate(below),
                "Estimated stop fired before the client estimate was reached.");

            var reached = LeagueMatchmakingStopController.ParseSearchState(Bytes(
                "{\"isCurrentlyInQueue\":true,\"timeInQueue\":50,\"searchState\":{\"estimatedQueueTimeInSeconds\":50}}"));
            Require(reached != null && LeagueMatchmakingStopController.ShouldStopForEstimate(reached),
                "Estimated stop did not fire when the client estimate was reached.");

            var missing = LeagueMatchmakingStopController.ParseSearchState(Bytes(
                "{\"isCurrentlyInQueue\":true,\"timeInQueue\":999}"));
            Require(missing != null && !missing.HasUsableEstimate && !LeagueMatchmakingStopController.ShouldStopForEstimate(missing),
                "Missing matchmaking estimate did not fail closed.");

            var stopped = LeagueMatchmakingStopController.ParseSearchState(Bytes(
                "{\"isCurrentlyInQueue\":false,\"timeInQueue\":80,\"estimatedQueueTime\":60}"));
            Require(stopped != null && !LeagueMatchmakingStopController.ShouldStopForEstimate(stopped),
                "Stopped matchmaking state was treated as an active estimated-stop candidate.");

            Require(LeagueMatchmakingStopController.ParseSearchState(Bytes("not-json")) == null,
                "Malformed matchmaking search payload did not fail closed.");
        }

        private static void ValidateReadyCheckFence()
        {
            Require(LeagueMatchmakingStopController.IsMatchmakingForSmokeTest("Matchmaking", LeagueActivityLevel.Queueing),
                "Matchmaking stop controller lost Matchmaking recognition.");
            Require(!LeagueMatchmakingStopController.IsMatchmakingForSmokeTest("ReadyCheck", LeagueActivityLevel.Queueing),
                "Matchmaking stop controller could remain armed in ReadyCheck.");
            Require(!LeagueMatchmakingStopController.IsMatchmakingForSmokeTest("Lobby", LeagueActivityLevel.Client),
                "Matchmaking stop controller remained armed in Lobby.");
        }

        private static void ValidateWriteFence()
        {
            Require(LeagueMatchmakingWriteApiClient.IsAllowedTargetForSmokeTest("POST", LeagueMatchmakingWriteApiClient.SearchPath),
                "Matchmaking start write was removed from the transport allowlist.");
            Require(LeagueMatchmakingWriteApiClient.IsAllowedTargetForSmokeTest("DELETE", LeagueMatchmakingWriteApiClient.SearchPath),
                "Matchmaking stop write is not fenced into the transport allowlist.");
            Require(LeagueMatchmakingWriteApiClient.IsAllowedTargetForSmokeTest("POST", LeagueMatchmakingWriteApiClient.AcceptPath),
                "ReadyCheck accept write was removed from the transport allowlist.");
            Require(!LeagueMatchmakingWriteApiClient.IsAllowedTargetForSmokeTest("DELETE", LeagueMatchmakingWriteApiClient.AcceptPath),
                "ReadyCheck endpoint incorrectly accepts DELETE.");
            Require(!LeagueMatchmakingWriteApiClient.IsAllowedTargetForSmokeTest("DELETE", "/lol-lobby/v2/lobby"),
                "Matchmaking stop transport can delete the lobby.");
            Require(!LeagueMatchmakingWriteApiClient.IsAllowedTargetForSmokeTest("POST", "/lol-chat/v1/me"),
                "Matchmaking transport escaped its narrow endpoint allowlist.");
        }

        private static byte[] Bytes(string value)
        {
            return Encoding.UTF8.GetBytes(value ?? string.Empty);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
