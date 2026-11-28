using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueRewardsOverview
    {
        public static readonly LeagueRewardsOverview Unavailable = new LeagueRewardsOverview { Available = false };

        public bool Available { get; set; }
        public bool LootAvailable { get; set; }
        public bool MissionsAvailable { get; set; }
        public int LootItemCount { get; set; }
        public int SkinsOwned { get; set; }
        public int MissionsCompleted { get; set; }
        public int MissionsInProgress { get; set; }

        public string Summary
        {
            get
            {
                if (!Available) return "奖励数据不可用";
                return (LootAvailable ? "战利品 " + LootItemCount + " 项" : "战利品未知") + " · " +
                       (MissionsAvailable ? "已完成任务 " + MissionsCompleted + " · 进行中 " + MissionsInProgress : "任务未知");
            }
        }
    }

    /// <summary>
    /// Read-only reward overview built from player-loot and missions. Crafting and claiming are
    /// intentionally not attempted here: those routes depend on per-recipe payloads that must be
    /// verified against a live inventory before anything is written.
    /// </summary>
    internal sealed class LeagueRewardsOverviewService
    {
        internal const string PlayerLootPath = "/lol-loot/v1/player-loot";
        internal const string MissionsPath = "/lol-missions/v1/missions";

        private readonly ILeagueClientApi _reader;

        public LeagueRewardsOverviewService(ILeagueClientApi reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        public async Task<LeagueRewardsOverview> ReadAsync(CancellationToken cancellationToken)
        {
            var loot = await _reader.TryGetBytesAsync(PlayerLootPath, cancellationToken).ConfigureAwait(false);
            var missions = await _reader.TryGetBytesAsync(MissionsPath, cancellationToken).ConfigureAwait(false);
            if (loot == null && missions == null) return LeagueRewardsOverview.Unavailable;

            var overview = new LeagueRewardsOverview();
            var lootItems = ParseArray(loot);
            if (lootItems != null)
            {
                overview.LootAvailable = true;
                overview.LootItemCount = lootItems.Count;
                foreach (var item in lootItems)
                {
                    var entry = item as Dictionary<string, object>;
                    if (entry == null) continue;
                    if (string.Equals(ReadString(entry, "type"), "CHAMPION_SKIN", StringComparison.OrdinalIgnoreCase) &&
                        ReadDouble(entry, "count") > 0) overview.SkinsOwned++;
                }
            }

            var missionItems = ParseArray(missions);
            if (missionItems != null)
            {
                overview.MissionsAvailable = true;
                foreach (var item in missionItems)
                {
                    var entry = item as Dictionary<string, object>;
                    if (entry == null) continue;
                    var status = ReadString(entry, "status");
                    if (string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase)) overview.MissionsCompleted++;
                    else if (string.Equals(status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase)) overview.MissionsInProgress++;
                }
            }

            overview.Available = overview.LootAvailable || overview.MissionsAvailable;
            AppLog.Info("Rewards overview: loot=" + overview.LootItemCount +
                        "; missionsDone=" + overview.MissionsCompleted +
                        "; missionsActive=" + overview.MissionsInProgress);
            return overview;
        }

        private static IList<object> ParseArray(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                var items = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(bytes)) as object[];
                if (items == null) return null;
                foreach (var item in items) if (!(item is Dictionary<string, object>)) return null;
                return items;
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private static string ReadString(Dictionary<string, object> value, string key)
        {
            object entry;
            return value != null && value.TryGetValue(key, out entry) && entry != null
                ? Convert.ToString(entry, CultureInfo.InvariantCulture)
                : null;
        }

        private static double ReadDouble(Dictionary<string, object> value, string key)
        {
            object entry;
            if (value == null || !value.TryGetValue(key, out entry) || entry == null) return 0;
            double parsed;
            return double.TryParse(Convert.ToString(entry, CultureInfo.InvariantCulture),
                NumberStyles.Any, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }
    }
}
