using System;
using System.Text;

namespace FACM.League
{
    internal static class LeagueRespawnTimerSmokeTest
    {
        internal static void Validate()
        {
            var settings = new FACM.Services.AppSettings();
            Require(!settings.LeagueRespawnOverlayEnabled, "Respawn overlay must default off.");
            settings.LeagueRespawnOverlayEnabled = true;
            var local = FACM.Services.AppSettings.ParseLines(settings.BuildLinesForSmokeTest());
            Require(local.LeagueRespawnOverlayEnabled, "Respawn overlay INI setting failed to roundtrip.");
            var cloud = FACM.Services.CloudSettingsSnapshot.Capture(settings);
            var restored = new FACM.Services.AppSettings();
            cloud.ApplyTo(restored);
            Require(restored.LeagueRespawnOverlayEnabled, "Respawn overlay cloud setting failed to roundtrip.");
            var inGame = new LeagueDashboardPhaseState { Connected = true, Phase = "InProgress" };
            Require(LeagueRespawnOverlayHost.ShouldRun(true, inGame) &&
                !LeagueRespawnOverlayHost.ShouldRun(false, inGame) &&
                !LeagueRespawnOverlayHost.ShouldRun(true, new LeagueDashboardPhaseState { Connected = true, Phase = "Reconnect" }) &&
                !LeagueRespawnOverlayHost.ShouldRun(true, new LeagueDashboardPhaseState { Connected = false, Phase = "InProgress" }),
                "Respawn overlay Gameflow gate became active outside connected gameplay.");
            Require(LeagueRespawnOverlayVisibility.IsGameProcess("League of Legends") &&
                !LeagueRespawnOverlayVisibility.IsGameProcess("LeagueClientUx") &&
                !LeagueRespawnOverlayVisibility.IsGameProcess("Explorer"),
                "Respawn overlay foreground process allowlist was broadened.");
            Require(LeagueRespawnTimerProjection.ReadCurrentAccount(Encoding.UTF8.GetBytes(
                "{\"gameName\":\"玩家\",\"tagLine\":\"CN1\",\"displayName\":\"旧名称\"}")) == "玩家#CN1",
                "Current summoner Riot ID was not preferred.");
            Require(LeagueRespawnTimerProjection.ReadCurrentAccount(Encoding.UTF8.GetBytes(
                "{\"displayName\":\"旧名称\"}")) == "旧名称",
                "Legacy summoner display name was discarded.");
            var identity = LeagueRespawnTimerProjection.ReadCurrentIdentity(Encoding.UTF8.GetBytes(
                "{\"gameName\":\"玩家\",\"tagLine\":\"CN1\",\"internalName\":\"legacy123\"}"));
            Require(identity != null && identity.AccountName == "玩家#CN1" &&
                    identity.InternalName == "legacy123",
                "Respawn account must preserve Riot ID and legacy internal name together.");
            Require(LeagueRespawnTimerProjection.ReadCurrentAccount(Encoding.UTF8.GetBytes(
                "{\"internalName\":\"legacy123\"}")) == "legacy123",
                "Legacy-only account identity must remain usable.");
            Require(LeagueRespawnTimerProjection.ReadCurrentAccount(Encoding.UTF8.GetBytes("[]")) == null,
                "Malformed summoner account was accepted.");
            var alive = Sample("[{\"riotId\":\"玩家#CN1\",\"summonerName\":\"旧名称\",\"isDead\":false,\"respawnTimer\":0}]", "玩家#CN1", null);
            Require(alive != null && !alive.IsDead, "Living player's respawn state was not cleared.");

            var first = Sample("[{\"riotId\":\"其他人#CN1\",\"isDead\":true,\"respawnTimer\":99},{\"riotId\":\"玩家#CN1\",\"isDead\":true,\"respawnTimer\":24.5}]", "玩家#CN1", alive);
            Require(first != null && first.IsDead && first.TimeLeftSeconds == 24.5 && first.TotalTimeSeconds == 24.5,
                "Respawn sample selected the wrong player or first death duration.");

            var next = Sample("[{\"riotId\":\"玩家#CN1\",\"isDead\":true,\"respawnTimer\":19}]", "玩家#CN1", first);
            Require(next != null && next.TimeLeftSeconds == 19 && next.TotalTimeSeconds == 24.5,
                "Respawn timer lost the first observation's total duration.");

            var longer = Sample("[{\"riotId\":\"玩家#CN1\",\"isDead\":true,\"respawnTimer\":28}]", "玩家#CN1", next);
            Require(longer != null && longer.TotalTimeSeconds == 28,
                "A reconnect into an existing death must retain the largest observed timer.");

            Require(Sample("[{\"riotId\":\"其他人#CN1\",\"isDead\":true,\"respawnTimer\":10}]", "玩家#CN1", first) == null,
                "Respawn projection reused a different summoner's state.");
            Require(Sample("[{\"summonerName\":\"玩家#CN1\",\"isDead\":true,\"respawnTimer\":10}]", "玩家#CN1", null) != null,
                "Legacy player name fallback was lost.");
            Require(Sample("[{\"riotId\":\"其他人#CN1\",\"summonerName\":\"玩家#CN1\",\"isDead\":true,\"respawnTimer\":10}]", "玩家#CN1", null) == null,
                "Riot identity mismatch improperly used the fallback name.");
            Require(LeagueRespawnTimerProjection.Project(Encoding.UTF8.GetBytes(
                    "[{\"summonerName\":\"legacy123\",\"isDead\":true,\"respawnTimer\":17}]"),
                    "玩家#CN1", null, "legacy123")?.TimeLeftSeconds == 17 &&
                    LeagueRespawnTimerProjection.Project(Encoding.UTF8.GetBytes(
                    "[{\"summonerName\":\"legacy123\",\"isDead\":true,\"respawnTimer\":17}]"),
                    "玩家#CN1", null) == null,
                "Live Client legacy internal-name fallback must require the verified LCU identity.");
            Require(LeagueRespawnTimerProjection.Project(Encoding.UTF8.GetBytes(
                    "[{\"riotId\":\"其他人#CN1\",\"summonerName\":\"legacy123\",\"isDead\":true,\"respawnTimer\":17}]"),
                    "玩家#CN1", null, "legacy123") == null,
                "An explicit different Riot ID must never be replaced by a legacy-name match.");
            Require(Sample("[{\"riotId\":\"玩家#CN1\",\"isDead\":true,\"respawnTimer\":-5}]", "玩家#CN1", first) == null,
                "Invalid negative timer was accepted.");
            Require(Sample("[]", "玩家#CN1", first) == null && Sample("not-json", "玩家#CN1", first) == null,
                "Missing/malformed game client state must not reuse a stale death.");
            Require(LeagueRespawnTimerReader.PlayerListUri.IsLoopback && LeagueRespawnTimerReader.PlayerListUri.Port == 2999 &&
                    LeagueRespawnTimerReader.PlayerListUri.AbsolutePath == "/liveclientdata/playerlist",
                "Respawn source is not the pinned Akari loopback endpoint.");
        }

        private static LeagueRespawnTimerSample Sample(string json, string accountName, LeagueRespawnTimerSample previous)
        {
            return LeagueRespawnTimerProjection.Project(Encoding.UTF8.GetBytes(json), accountName, previous);
        }

        private static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException(reason);
        }
    }
}
