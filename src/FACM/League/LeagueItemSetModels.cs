using System.Collections.Generic;
using System.Linq;

namespace FACM.League
{
    internal sealed class LeagueItemSetPlan
    {
        public LeagueItemSetPlan()
        {
            Blocks = new List<LeagueItemSetBlock>();
        }

        public int ChampionId { get; set; }
        public string ChampionName { get; set; }
        public int QueueId { get; set; }
        public string Mode { get; set; }
        public string Position { get; set; }
        public string Version { get; set; }
        public string Uid { get; set; }
        public string Title { get; set; }
        public List<LeagueItemSetBlock> Blocks { get; private set; }

        public int ItemCount
        {
            get { return Blocks.Sum(block => block == null ? 0 : block.Items.Count); }
        }

        public bool HasItems
        {
            get { return Blocks.Any(block => block != null && block.Items.Count > 0); }
        }
    }

    internal sealed class LeagueItemSetBlock
    {
        public LeagueItemSetBlock()
        {
            Items = new List<int>();
        }

        public string Title { get; set; }
        public List<int> Items { get; private set; }
    }

    internal sealed class LeagueItemSetWriteResult
    {
        public string Status { get; set; }
        public string BlockReason { get; set; }
        public string TargetDirectory { get; set; }
        public string FileName { get; set; }
        public int RemovedOldFiles { get; set; }
        public bool CleanupWarning { get; set; }
        public string Error { get; set; }

        public bool Succeeded
        {
            get { return string.Equals(Status, "success", System.StringComparison.OrdinalIgnoreCase); }
        }
    }
}
