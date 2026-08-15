using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace FACM.League
{
    internal sealed class LeagueProcessSnapshot
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    internal interface ILeagueDesktopPlatform
    {
        IReadOnlyList<LeagueProcessSnapshot> GetProcesses();
        bool IsProcessAlive(int processId);
        bool Kill(int processId);
    }

    internal sealed class WindowsLeagueDesktopPlatform : ILeagueDesktopPlatform
    {
        public IReadOnlyList<LeagueProcessSnapshot> GetProcesses()
        {
            var result = new List<LeagueProcessSnapshot>();
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    result.Add(new LeagueProcessSnapshot { Id = process.Id, Name = process.ProcessName ?? string.Empty });
                }
                catch { }
                finally { process.Dispose(); }
            }
            return result;
        }

        public bool IsProcessAlive(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId)) return !process.HasExited;
            }
            catch { return false; }
        }

        public bool Kill(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    if (process.HasExited) return true;
                    process.Kill();
                    return true;
                }
            }
            catch { return false; }
        }
    }

    internal sealed class LeagueEfficiencyActionResult
    {
        public string Status { get; set; }
        public string Detail { get; set; }
        public int AffectedProcesses { get; set; }
    }

    internal sealed class LeagueEfficiencyActionService
    {
        private static readonly HashSet<string> GameProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "League of Legends(TM)",
            "League of Legends"
        };

        private static readonly HashSet<string> LobbyProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LeagueClient",
            "LeagueClientUx",
            "LeagueClientUxRender"
        };

        private readonly ILeagueDesktopPlatform _platform;

        public LeagueEfficiencyActionService()
            : this(new WindowsLeagueDesktopPlatform())
        {
        }

        internal LeagueEfficiencyActionService(ILeagueDesktopPlatform platform)
        {
            _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        }

        public Task<LeagueEfficiencyActionResult> ExitGameAsync()
        {
            return Task.FromResult(KillTargets(GameProcessNames, "game-not-running", "game-exit"));
        }

        public Task<LeagueEfficiencyActionResult> CloseLobbyAsync()
        {
            return Task.FromResult(KillTargets(LobbyProcessNames, "lobby-not-running", "lobby-exit"));
        }

        private LeagueEfficiencyActionResult KillTargets(HashSet<string> names, string noTargetDetail, string successDetail)
        {
            var targets = (_platform.GetProcesses() ?? new LeagueProcessSnapshot[0])
                .Where(process => process != null && process.Id > 0 && names.Contains(process.Name ?? string.Empty))
                .GroupBy(process => process.Id)
                .Select(group => group.First())
                .ToList();

            if (targets.Count == 0) return Result("no-target", noTargetDetail, 0);

            var affected = 0;
            foreach (var target in targets)
            {
                if (!_platform.IsProcessAlive(target.Id)) continue;
                if (_platform.Kill(target.Id)) affected++;
            }
            return Result(affected > 0 ? "success" : "failed", successDetail, affected);
        }

        private static LeagueEfficiencyActionResult Result(string status, string detail, int affected)
        {
            return new LeagueEfficiencyActionResult { Status = status, Detail = detail, AffectedProcesses = affected };
        }
    }
}
