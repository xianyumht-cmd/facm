using System;
using System.Linq;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueAutoApplySmokeTest
    {
        public static void Validate()
        {
            ValidateSettingsPersistenceContract();
            ValidateStableExactlyOnceContract();
            ValidateDisableAndPhaseContract();
            ValidateResultTruthfulness();
            ValidateUiTextDefaults();
        }

        private static void ValidateSettingsPersistenceContract()
        {
            var defaults = AppSettings.ParseLines(new string[0]);
            Require(!defaults.LeagueAutoApplyRecommended,
                "Gate 4 auto apply must default OFF for existing users.");

            var enabled = AppSettings.ParseLines(new[]
            {
                "GamePath=C:\\Games\\League",
                "LeagueAutoApplyRecommended=True"
            });
            Require(enabled.LeagueAutoApplyRecommended,
                "Gate 4 did not restore the enabled auto apply setting.");
            Require(enabled.BuildLines().Any(line =>
                    string.Equals(line, "LeagueAutoApplyRecommended=True", StringComparison.OrdinalIgnoreCase)),
                "Gate 4 enabled setting was not serialized back to settings.ini.");

            var disabled = AppSettings.ParseLines(new[] { "LeagueAutoApplyRecommended=False" });
            Require(!disabled.LeagueAutoApplyRecommended,
                "Gate 4 explicit disabled setting was not restored.");
            Require(disabled.BuildLines().Any(line =>
                    string.Equals(line, "LeagueAutoApplyRecommended=False", StringComparison.OrdinalIgnoreCase)),
                "Gate 4 disabled setting was not serialized back to settings.ini.");
        }

        private static void ValidateStableExactlyOnceContract()
        {
            var coordinator = new LeagueAutoApplyCoordinator(TimeSpan.FromMilliseconds(1500));
            var t0 = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
            var yasuo = CreateSnapshot(157, "疾风剑豪");

            Require(!coordinator.Observe(yasuo, true, t0).ShouldExecute,
                "Gate 4 executed immediately before the context was stable.");
            Require(!coordinator.Observe(yasuo, true, t0.AddSeconds(1)).ShouldExecute,
                "Gate 4 ignored its stability window.");
            var first = coordinator.Observe(yasuo, true, t0.AddSeconds(2));
            Require(first.ShouldExecute && !string.IsNullOrWhiteSpace(first.Fingerprint),
                "Gate 4 did not execute once after a stable context.");
            Require(!coordinator.Observe(yasuo, true, t0.AddSeconds(4)).ShouldExecute,
                "Gate 4 repeated the same stable fingerprint.");
            Require(!coordinator.Observe(yasuo, true, t0.AddSeconds(8)).ShouldExecute,
                "Gate 4 entered a retry storm for an unchanged fingerprint.");

            var kaisa = CreateSnapshot(145, "卡莎");
            Require(!coordinator.Observe(kaisa, true, t0.AddSeconds(9)).ShouldExecute,
                "Champion change must restart stability instead of writing immediately.");
            Require(coordinator.Observe(kaisa, true, t0.AddSeconds(11)).ShouldExecute,
                "A new stable champion fingerprint did not receive exactly one apply opportunity.");

            var changedRecommendation = CreateSnapshot(145, "卡莎");
            changedRecommendation.Recommendation.Rows[0].Recommendation = "治疗术 · 闪现";
            Require(!coordinator.Observe(changedRecommendation, true, t0.AddSeconds(12)).ShouldExecute,
                "Recommendation change must establish a new stable fingerprint first.");
            Require(coordinator.Observe(changedRecommendation, true, t0.AddSeconds(14)).ShouldExecute,
                "Changed recommendation content was not recognized as a new fingerprint.");
        }

        private static void ValidateDisableAndPhaseContract()
        {
            var coordinator = new LeagueAutoApplyCoordinator(TimeSpan.FromMilliseconds(1500));
            var t0 = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
            var snapshot = CreateSnapshot(157, "疾风剑豪");

            Require(!coordinator.Observe(snapshot, false, t0).ShouldExecute,
                "Gate 4 OFF state produced an auto apply decision.");
            Require(!coordinator.Observe(snapshot, true, t0.AddSeconds(1)).ShouldExecute,
                "Gate 4 should start a fresh stability window after enabling.");
            Require(!coordinator.Observe(snapshot, false, t0.AddSeconds(2)).ShouldExecute,
                "Disabling Gate 4 produced an auto apply decision.");
            Require(!coordinator.Observe(snapshot, true, t0.AddSeconds(3)).ShouldExecute,
                "Re-enabling after a cancelled pending context must stabilize again.");
            Require(coordinator.Observe(snapshot, true, t0.AddSeconds(5)).ShouldExecute,
                "Gate 4 did not recover after a disable/re-enable cycle.");

            var inGame = CreateSnapshot(157, "疾风剑豪");
            inGame.Activity = LeagueActivityLevel.InGame;
            inGame.Phase = "InProgress";
            Require(!coordinator.Observe(inGame, true, t0.AddSeconds(7)).ShouldExecute,
                "Gate 4 must never auto apply in game.");

            var notReady = CreateSnapshot(157, "疾风剑豪");
            notReady.Status = "opgg-unavailable";
            Require(!coordinator.Observe(notReady, true, t0.AddSeconds(9)).ShouldExecute,
                "Gate 4 must not write from an unavailable OP.GG recommendation.");

            Require(LeagueAutoApplyController.PollInterval >= TimeSpan.FromSeconds(2),
                "Gate 4 observer poll interval became more aggressive than the frozen Champ Select plan.");
            Require(LeagueAutoApplyCoordinator.DefaultStabilityWindow >= TimeSpan.FromSeconds(1),
                "Gate 4 stability window became too short for a novice-facing automatic write.");
        }

        private static void ValidateResultTruthfulness()
        {
            var fullBuild = new LeagueBuildApplyResult
            {
                Status = "success",
                RunesApplied = true,
                SpellsApplied = true
            };
            var fullItems = new LeagueItemSetWriteResult { Status = "success" };
            Require(LeagueAutoApplyAttemptResult.Aggregate(true, fullBuild, true, fullItems).Status == "success",
                "Gate 4 lost full-success aggregation.");

            var partialBuild = new LeagueBuildApplyResult
            {
                Status = "partial",
                RunesApplied = true,
                SpellsApplied = false
            };
            Require(LeagueAutoApplyAttemptResult.Aggregate(true, partialBuild, true, fullItems).Status == "partial",
                "Gate 4 falsely upgraded partial runes/spells to full success.");

            var failedBuild = new LeagueBuildApplyResult { Status = "failed" };
            var failedItems = new LeagueItemSetWriteResult { Status = "failed" };
            Require(LeagueAutoApplyAttemptResult.Aggregate(true, failedBuild, true, failedItems).Status == "failed",
                "Gate 4 failed result was not preserved.");

            Require(LeagueAutoApplyAttemptResult.Aggregate(false, null, true, fullItems).Status == "success",
                "Gate 4 must allow a valid item-set-only recommendation to report success.");
        }

        private static void ValidateUiTextDefaults()
        {
            foreach (var key in LeagueAutoApplyUiTextKeys.AllKeys)
            {
                string value;
                Require(LeagueAutoApplyUiTextKeys.TryGetDefault(key, out value) && !string.IsNullOrWhiteSpace(value),
                    "Gate 4 UI text key has no runtime default: " + key);
            }
        }

        private static LeagueBuildAdvisorSnapshot CreateSnapshot(int championId, string championName)
        {
            var recommendation = new LeagueBuildRecommendation();
            recommendation.Rows.Add(new LeagueBuildAdvisorRow
            {
                Category = "summoner-spells",
                Recommendation = "引燃 · 闪现"
            });
            recommendation.Rows.Add(new LeagueBuildAdvisorRow
            {
                Category = "runes",
                Recommendation = "致命节奏 · 凯旋"
            });
            recommendation.Rows.Add(new LeagueBuildAdvisorRow
            {
                Category = "core-items",
                Recommendation = "破败王者之刃 · 无尽之刃"
            });

            return new LeagueBuildAdvisorSnapshot
            {
                Connected = true,
                Phase = "ChampSelect",
                Activity = LeagueActivityLevel.ChampSelect,
                BudgetName = "champ-select",
                QueueId = 420,
                ChampionId = championId,
                ChampionName = championName,
                Mode = "ranked",
                Position = "mid",
                Source = "OP.GG Global",
                Version = "16.16",
                Status = "ready",
                Recommendation = recommendation
            };
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
