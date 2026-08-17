using System.Collections.Generic;

namespace FACM.League
{
    internal sealed class LeagueBuildApplyPlan
    {
        public LeagueBuildApplyPlan()
        {
            PrimaryRuneIds = new List<int>();
            SecondaryRuneIds = new List<int>();
            StatModIds = new List<int>();
        }

        public int OptionRank { get; set; }
        public int ChampionId { get; set; }
        public string ChampionName { get; set; }
        public int QueueId { get; set; }
        public string Mode { get; set; }
        public string Position { get; set; }
        public string Version { get; set; }
        public int Spell1Id { get; set; }
        public int Spell2Id { get; set; }
        public string SpellPreview { get; set; }
        public double? SpellPickRate { get; set; }
        public int SpellPlay { get; set; }
        public int PrimaryStyleId { get; set; }
        public int SecondaryStyleId { get; set; }
        public List<int> PrimaryRuneIds { get; private set; }
        public List<int> SecondaryRuneIds { get; private set; }
        public List<int> StatModIds { get; private set; }
        public string RunePreview { get; set; }
        public double? RunePickRate { get; set; }
        public int RunePlay { get; set; }

        public bool HasSpells
        {
            get { return Spell1Id > 0 && Spell2Id > 0; }
        }

        public bool HasRunes
        {
            get
            {
                return PrimaryStyleId > 0 &&
                       SecondaryStyleId > 0 &&
                       PrimaryRuneIds.Count > 0 &&
                       SecondaryRuneIds.Count > 0 &&
                       StatModIds.Count > 0;
            }
        }

        public List<int> GetSelectedPerkIds()
        {
            var output = new List<int>(PrimaryRuneIds.Count + SecondaryRuneIds.Count + StatModIds.Count);
            output.AddRange(PrimaryRuneIds);
            output.AddRange(SecondaryRuneIds);
            output.AddRange(StatModIds);
            return output;
        }
    }

    internal sealed class LeagueBuildApplyResult
    {
        public string Status { get; set; }
        public string BlockReason { get; set; }
        public string RuneStatus { get; set; }
        public string SpellStatus { get; set; }
        public bool RunesApplied { get; set; }
        public bool SpellsApplied { get; set; }
        public bool RuneSkippedNoCapacity { get; set; }
        public int CreatedRunePageId { get; set; }

        public bool AnyApplied
        {
            get { return RunesApplied || SpellsApplied; }
        }
    }
}
