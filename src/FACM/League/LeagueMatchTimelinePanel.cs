using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeagueMatchTimelinePanel : UserControl
    {
        private readonly UiTextCatalog _ui;
        private readonly Func<CancellationToken, Task<LeagueMatchTimeline>> _load;
        private readonly CancellationTokenSource _lifetime;
        private readonly List<LeaguePlayerMatchParticipant> _participants;
        private readonly LeagueBuildAdvisorCatalog _catalog;
        private readonly ComboBox _player, _eventType;
        private readonly Label _status;
        private readonly Button _retry;
        private readonly ListView _events, _frames, _builds;
        private readonly TextBox _details;
        private readonly LeagueMatchTimelineChartPanel _comparison;
        private readonly LeagueTimelineFrameStats _frameStats;
        private readonly int _targetParticipant;
        private LeagueTimelineBuilds _buildProjection = new LeagueTimelineBuilds();
        private readonly Dictionary<LeagueMatchTimelineEvent, LeagueTimelineBuildStep> _buildSteps =
            new Dictionary<LeagueMatchTimelineEvent, LeagueTimelineBuildStep>();
        private List<LeagueMatchTimelineEvent> _shownEvents = new List<LeagueMatchTimelineEvent>();
        private List<LeagueMatchTimelineEvent> _shownBuilds = new List<LeagueMatchTimelineEvent>();
        private readonly List<Tuple<long, int, Dictionary<string, object>>> _shownFrames =
            new List<Tuple<long, int, Dictionary<string, object>>>();
        private LeagueMatchTimeline _timeline;
        private bool _loading;
        private bool _updating;

        internal LeagueMatchTimelinePanel(UiTextCatalog ui, LeagueEncounterMatchDetailResult detail, CancellationToken token)
        {
            _ui = ui;
            _load = detail.LoadTimeline;
            _participants = detail.Participants;
            _catalog = detail.BuildCatalog;
            _targetParticipant = detail.Match.ParticipantId;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            Dock = DockStyle.Fill;
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
            _player = new ComboBox { Width = 210, DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = T(UiTextKeys.LeagueTimelinePlayer), BackColor = FacmDesignSystem.SurfaceRaised,
                ForeColor = FacmDesignSystem.Text };
            _player.Items.Add(T(UiTextKeys.LeagueTimelineAllPlayers));
            foreach (var participant in _participants) _player.Items.Add(PlayerName(participant.ParticipantId));
            _player.SelectedIndex = 0;
            _eventType = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = T(UiTextKeys.LeagueTimelineEventType), BackColor = FacmDesignSystem.SurfaceRaised,
                ForeColor = FacmDesignSystem.Text };
            _eventType.Items.Add(T(UiTextKeys.LeagueTimelineAllEvents));
            _eventType.SelectedIndex = 0;
            _retry = new Button { Text = T(UiTextKeys.LeaguePlayerRemoteRetry), Width = 70, Height = 26 };
            _retry.Click += async (sender, args) => { _timeline = null; await EnsureLoadedAsync(); };
            tools.Controls.Add(_player); tools.Controls.Add(_eventType); tools.Controls.Add(_retry);
            _status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            _events = EventList();
            _builds = EventList();
            _events.RetrieveVirtualItem += (sender, args) => args.Item = EventRow(_shownEvents, args.ItemIndex);
            _builds.RetrieveVirtualItem += (sender, args) => args.Item = EventRow(_shownBuilds, args.ItemIndex);
            _frames = BaseList();
            _frames.VirtualMode = true;
            _frames.RetrieveVirtualItem += (sender, args) => args.Item = FrameRow(args.ItemIndex);
            foreach (var key in new[] { UiTextKeys.LeagueTimelineTime, UiTextKeys.LeagueTimelinePlayer,
                UiTextKeys.LeagueTimelineLevel, UiTextKeys.LeagueTimelineGold, UiTextKeys.LeagueTimelineCurrentGold,
                UiTextKeys.LeagueTimelineXp, UiTextKeys.LeaguePlayerCs }) _frames.Columns.Add(T(key), 100);
            var tabs = new TabControl { Dock = DockStyle.Fill };
            AddTab(tabs, UiTextKeys.LeagueTimelineEvents, _events);
            AddTab(tabs, UiTextKeys.LeagueTimelineProgression, _frames);
            _comparison = new LeagueMatchTimelineChartPanel(ui, detail.Participants);
            AddTab(tabs, UiTextKeys.LeagueTimelineComparison, _comparison);
            AddTab(tabs, UiTextKeys.LeagueTimelineBuilds, _builds);
            _frameStats = new LeagueTimelineFrameStats(ui);
            AddTab(tabs, UiTextKeys.LeagueFrameTitle, _frameStats);
            _details = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Both, WordWrap = false, BackColor = FacmDesignSystem.SurfaceRaised,
                ForeColor = FacmDesignSystem.Text, AccessibleName = T(UiTextKeys.LeagueTimelineDetails) };
            _events.SelectedIndexChanged += (sender, args) => ShowEvent(_events, _shownEvents);
            _builds.SelectedIndexChanged += (sender, args) => ShowEvent(_builds, _shownBuilds);
            _player.SelectedIndexChanged += (sender, args) => RefreshRows();
            _eventType.SelectedIndexChanged += (sender, args) => RefreshRows();
            layout.Controls.Add(tools, 0, 0); layout.Controls.Add(_status, 0, 1);
            layout.Controls.Add(tabs, 0, 2); layout.Controls.Add(_details, 0, 3);
            Controls.Add(layout);
        }

        internal async Task EnsureLoadedAsync()
        {
            if (_loading || _timeline != null || IsDisposed || _lifetime.IsCancellationRequested) return;
            if (_load == null) { _status.Text = T(UiTextKeys.LeagueTimelineUnavailable); return; }
            _loading = true;
            _retry.Enabled = false;
            _status.Text = T(UiTextKeys.LeagueTimelineLoading);
            try
            {
                var result = await _load(_lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                _timeline = result;
                _comparison.SetTimeline(result);
                _buildProjection = LeagueTimelineBuilds.Project(result);
                _buildSteps.Clear();
                foreach (var step in _buildProjection.Steps) _buildSteps[step.Event] = step;
                _updating = true;
                _eventType.Items.Clear();
                _eventType.Items.Add(T(UiTextKeys.LeagueTimelineAllEvents));
                if (result != null)
                    foreach (var type in result.Events.Select(entry => entry.Type).Distinct().OrderBy(value => value))
                        _eventType.Items.Add(new EventTypeChoice { Type = type, Label = EventName(type) });
                _eventType.SelectedIndex = 0;
                _updating = false;
                RefreshRows();
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (!IsDisposed && !_lifetime.IsCancellationRequested) _status.Text = T(UiTextKeys.LeagueTimelineUnavailable);
            }
            finally { _loading = false; if (!IsDisposed) _retry.Enabled = true; }
        }

        private void RefreshRows()
        {
            if (_updating || IsDisposed) return;
            var id = _player.SelectedIndex <= 0 ? 0 : _participants[_player.SelectedIndex - 1].ParticipantId;
            var type = (_eventType.SelectedItem as EventTypeChoice)?.Type;
            _events.SelectedIndices.Clear();
            _builds.SelectedIndices.Clear();
            _events.VirtualListSize = 0;
            _builds.VirtualListSize = 0;
            _shownEvents = _timeline == null ? new List<LeagueMatchTimelineEvent>() : _timeline.Events
                .Where(entry => entry.Involves(id) && (type == null || entry.Type == type)).ToList();
            _shownBuilds = _buildProjection.Steps.Where(step => step.Event.Involves(id))
                .Select(step => step.Event).ToList();
            _events.VirtualListSize = _shownEvents.Count;
            _builds.VirtualListSize = _shownBuilds.Count;
            _events.Invalidate(); _builds.Invalidate();
            _details.Text = string.Empty;
            _frames.VirtualListSize = 0;
            _shownFrames.Clear();
            if (_timeline != null)
                foreach (var frame in _timeline.Frames)
                    foreach (var pair in frame.Participants.Where(pair => id == 0 || pair.Key == id))
                        _shownFrames.Add(Tuple.Create(frame.Timestamp, pair.Key, pair.Value));
            _frames.VirtualListSize = _shownFrames.Count;
            _frames.Invalidate();
            _frameStats.SetData(_timeline, id > 0 ? id : _targetParticipant);
            _status.Text = _timeline == null ? T(UiTextKeys.LeagueTimelineUnavailable) :
                T(UiTextKeys.LeagueTimelineSummary).Replace("{0}", _timeline.Frames.Count.ToString())
                    .Replace("{1}", _shownEvents.Count.ToString());
            int anvils;
            if (id > 0 && _buildProjection.Anvils.TryGetValue(id, out anvils))
                _status.Text += " · " + T(UiTextKeys.LeagueTimelineAnvils).Replace("{0}", anvils.ToString());
        }

        private ListViewItem FrameRow(int index)
        {
            if (index < 0 || index >= _shownFrames.Count) return new ListViewItem(string.Empty);
            var frame = _shownFrames[index];
            var row = new ListViewItem(LeagueMatchTimelineParser.Time(frame.Item1));
            row.SubItems.Add(PlayerName(frame.Item2));
            foreach (var key in new[] { "level", "totalGold", "currentGold", "xp" })
                row.SubItems.Add(Number(frame.Item3, key));
            var lane = LeagueMatchTimelineParser.Number(frame.Item3, "minionsKilled");
            var jungle = LeagueMatchTimelineParser.Number(frame.Item3, "jungleMinionsKilled");
            row.SubItems.Add(lane.HasValue && jungle.HasValue && lane.Value <= long.MaxValue - jungle.Value
                ? (lane.Value + jungle.Value).ToString(CultureInfo.InvariantCulture) : "—");
            return row;
        }

        private ListViewItem EventRow(List<LeagueMatchTimelineEvent> events, int index)
        {
            if (index < 0 || index >= events.Count) return new ListViewItem(string.Empty);
            var entry = events[index];
            var row = new ListViewItem(LeagueMatchTimelineParser.Time(entry.Timestamp));
            row.SubItems.Add(EventName(entry.Type));
            row.SubItems.Add(PlayerName(entry.ParticipantId > 0 ? entry.ParticipantId :
                entry.KillerId > 0 ? entry.KillerId : entry.CreatorId));
            row.SubItems.Add(PlayerName(entry.VictimId));
            var item = LeagueMatchTimelineParser.Number(entry.Data, "itemId");
            string name;
            row.SubItems.Add(item.HasValue ? _catalog != null && item.Value <= int.MaxValue &&
                _catalog.Items.TryGetValue((int)item.Value, out name) ? name + " (#" + item.Value + ")" : "#" + item.Value :
                LeagueMatchTimelineParser.Text(entry.Data, "monsterType") ??
                LeagueMatchTimelineParser.Text(entry.Data, "buildingType") ??
                LeagueMatchTimelineParser.Text(entry.Data, "wardType") ?? string.Empty);
            LeagueTimelineBuildStep step;
            if (ReferenceEquals(events, _shownBuilds) && _buildSteps.TryGetValue(entry, out step))
            {
                if (entry.Type == "ITEM_PURCHASED")
                    row.SubItems[4].Text = T(UiTextKeys.LeagueTimelinePurchaseGroup)
                        .Replace("{0}", step.PurchaseGroup.ToString()).Replace("{1}", row.SubItems[4].Text);
                else
                {
                    var slot = LeagueMatchTimelineParser.Number(entry.Data, "skillSlot");
                    var skill = slot >= 1 && slot <= 4 ? new[] { "Q", "W", "E", "R" }[(int)slot.Value - 1] : "—";
                    row.SubItems[4].Text = step.IsEvolution ? T(UiTextKeys.LeagueTimelineEvolve).Replace("{0}", skill) :
                        T(UiTextKeys.LeagueTimelineSkillLevel).Replace("{0}", step.SkillLevel.ToString()).Replace("{1}", skill);
                }
            }
            return row;
        }

        private void ShowEvent(ListView list, List<LeagueMatchTimelineEvent> events)
        {
            if (list.SelectedIndices.Count != 1 || list.SelectedIndices[0] >= events.Count) return;
            var entry = events[list.SelectedIndices[0]];
            _details.Text = T(UiTextKeys.LeagueTimelineEventDescription)
                .Replace("{0}", LeagueMatchTimelineParser.Time(entry.Timestamp)).Replace("{1}", EventName(entry.Type))
                .Replace("{2}", PlayerName(entry.ParticipantId > 0 ? entry.ParticipantId : entry.KillerId))
                .Replace("{3}", PlayerName(entry.VictimId))
                .Replace("{4}", string.Join(", ", entry.Assists.Select(PlayerName)));
            if (entry.Type == "ITEM_UNDO")
                _details.Text += "\r\n" + T(UiTextKeys.LeagueTimelineUndoDescription)
                    .Replace("{0}", ItemName(LeagueMatchTimelineParser.Number(entry.Data, "beforeId")))
                    .Replace("{1}", ItemName(LeagueMatchTimelineParser.Number(entry.Data, "afterId")));
            else if (entry.Type.StartsWith("ITEM_", StringComparison.Ordinal))
                _details.Text += "\r\n" + ItemName(LeagueMatchTimelineParser.Number(entry.Data, "itemId"));
            if (entry.Type == "SKILL_LEVEL_UP")
                _details.Text += "\r\n" + T(UiTextKeys.LeagueTimelineSkillDescription)
                    .Replace("{0}", Number(entry.Data, "skillSlot"));
        }
        private string ItemName(long? id)
        {
            string name;
            return !id.HasValue ? "—" : _catalog != null && id.Value <= int.MaxValue &&
                _catalog.Items.TryGetValue((int)id.Value, out name) ? name + " (#" + id.Value + ")" : "#" + id.Value;
        }
        private string PlayerName(int id)
        {
            var player = _participants.FirstOrDefault(participant => participant.ParticipantId == id);
            return player == null ? id > 0 ? "#" + id : "—" :
                (string.IsNullOrWhiteSpace(player.PlayerName) ? "#" + id : player.PlayerName) +
                " · " + (string.IsNullOrWhiteSpace(player.ChampionName) ? "#" + player.ChampionId : player.ChampionName);
        }
        private string EventName(string type)
        {
            var key = type == "CHAMPION_KILL" ? UiTextKeys.LeagueTimelineKill :
                type == "ELITE_MONSTER_KILL" ? UiTextKeys.LeagueTimelineMonster :
                type == "BUILDING_KILL" ? UiTextKeys.LeagueTimelineBuilding :
                type == "ITEM_PURCHASED" ? UiTextKeys.LeagueTimelineItemBuy :
                type == "ITEM_SOLD" ? UiTextKeys.LeagueTimelineItemSell :
                type == "ITEM_DESTROYED" ? UiTextKeys.LeagueTimelineItemDestroy :
                type == "ITEM_UNDO" ? UiTextKeys.LeagueTimelineItemUndo :
                type == "SKILL_LEVEL_UP" ? UiTextKeys.LeagueTimelineSkill : null;
            return key == null ? type : T(key);
        }
        private string T(string key) { return _ui.Get(key); }
        private static string Number(Dictionary<string, object> data, string key)
        {
            var value = LeagueMatchTimelineParser.Number(data, key);
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "—";
        }
        private static ListView BaseList()
        {
            return new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                HideSelection = false, MultiSelect = false, BackColor = FacmDesignSystem.SurfaceRaised,
                ForeColor = FacmDesignSystem.Text };
        }
        private ListView EventList()
        {
            var list = BaseList(); list.VirtualMode = true;
            foreach (var key in new[] { UiTextKeys.LeagueTimelineTime, UiTextKeys.LeagueTimelineEventType,
                UiTextKeys.LeagueTimelinePlayer, UiTextKeys.LeagueTimelineTarget, UiTextKeys.LeagueTimelineDetails })
                list.Columns.Add(T(key), key == UiTextKeys.LeagueTimelineTime ? 70 : 155);
            return list;
        }
        private void AddTab(TabControl tabs, string key, Control content)
        {
            var page = new TabPage(T(key)); page.Controls.Add(content); tabs.TabPages.Add(page);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed) { _lifetime.Cancel(); _lifetime.Dispose(); }
            base.Dispose(disposing);
        }
        private sealed class EventTypeChoice
        {
            internal string Type, Label;
            public override string ToString() { return Label; }
        }
    }
}
