using System;
using System.Text;

namespace FACM.League
{
    internal static class LeagueRespawnTimerSmokeTest
    {
        internal static void Validate()
        {
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
