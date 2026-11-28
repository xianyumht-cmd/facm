using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeagueLobbyRoomPanel : FlowLayoutPanel
    {
        private sealed class Option
        {
            internal long Id;
            public string Label { get; set; }
        }

        private readonly LeagueEfficiencyModule _module;
        private readonly UiTextCatalog _ui;
        private readonly Button _refresh, _queue, _leave, _kick, _promote;
        private readonly ComboBox _queues, _members;
        private readonly Label _status, _description;
        private readonly FlowLayoutPanel _controls;
        private CancellationTokenSource _request;
        private LeagueLobbyRoomSnapshot _snapshot;

        internal LeagueLobbyRoomPanel(LeagueEfficiencyModule module, UiTextCatalog ui)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Dock = DockStyle.Top;
            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            Padding = new Padding(4, 8, 4, 8);
            BackColor = FacmDesignSystem.Canvas;
            _refresh = MakeButton(T(LeagueEfficiencyUiTextKeys.RoomManage));
            _refresh.Click += async delegate { await ExecuteAsync(null, 0); };
            Controls.Add(_refresh);
            _controls = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false, Margin = Padding.Empty };
            _description = new Label { AutoSize = true, ForeColor = FacmDesignSystem.Text, Margin = new Padding(4, 4, 4, 4) };
            _status = new Label { AutoSize = true, ForeColor = FacmDesignSystem.TextMuted, Margin = new Padding(4, 4, 4, 4) };
            _queues = MakeCombo();
            _members = MakeCombo();
            _queue = MakeButton(T(LeagueEfficiencyUiTextKeys.RoomQueue));
            _leave = MakeButton(T(LeagueEfficiencyUiTextKeys.RoomLeave));
            _kick = MakeButton(T(LeagueEfficiencyUiTextKeys.RoomKick));
            _promote = MakeButton(T(LeagueEfficiencyUiTextKeys.RoomPromote));
            _queue.Click += async delegate { await ExecuteAsync(LeagueLobbyRoomAction.Queue, Selected(_queues)); };
            _leave.Click += async delegate { await ExecuteAsync(LeagueLobbyRoomAction.Leave, 0); };
            _kick.Click += async delegate { await ExecuteAsync(LeagueLobbyRoomAction.Kick, Selected(_members)); };
            _promote.Click += async delegate { await ExecuteAsync(LeagueLobbyRoomAction.Promote, Selected(_members)); };
            _controls.Controls.Add(_description);
            _controls.Controls.Add(MakeRow(_queues, _queue, _leave));
            _controls.Controls.Add(MakeRow(_members, _kick, _promote));
            _controls.Controls.Add(_status);
            Controls.Add(_controls);
            foreach (var id in LeagueLobbyRoomService.Queues)
                _queues.Items.Add(new Option { Id = id, Label = LeagueLobbyRoomText.QueueLabel(id) });
            if (_queues.Items.Count > 0) _queues.SelectedIndex = 0;
            UpdateEnabled();
        }

        private static Button MakeButton(string text)
        {
            return new Button { Text = text, AutoSize = true, MinimumSize = new Size(76, 30),
                FlatStyle = FlatStyle.Flat, BackColor = FacmDesignSystem.CanvasRaised, ForeColor = FacmDesignSystem.Text };
        }

        private static ComboBox MakeCombo()
        {
            return new ComboBox { Width = 145, DisplayMember = "Label", DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = FacmDesignSystem.CanvasRaised, ForeColor = FacmDesignSystem.Text };
        }

        private static FlowLayoutPanel MakeRow(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false, Margin = Padding.Empty };
            row.Controls.AddRange(controls);
            return row;
        }

        private static long Selected(ComboBox combo)
        {
            var option = combo.SelectedItem as Option;
            return option == null ? 0 : option.Id;
        }

        private void UpdateEnabled()
        {
            var idle = _request == null;
            _refresh.Enabled = idle;
            _queues.Enabled = _queue.Enabled = idle && _snapshot != null && _snapshot.CanQueue;
            _leave.Enabled = idle && _snapshot != null && _snapshot.CanLeave;
            _members.Enabled = _kick.Enabled = _promote.Enabled = idle && _snapshot != null &&
                _snapshot.CanManageMembers && _members.Items.Count > 0;
        }

        private void Render(LeagueLobbyRoomSnapshot snapshot, string status)
        {
            _snapshot = snapshot;
            _controls.Visible = true;
            _description.Text = snapshot == null ? T(LeagueEfficiencyUiTextKeys.RoomUnavailable) :
                snapshot.Phase == "None" ? T(LeagueEfficiencyUiTextKeys.RoomNone) :
                LeagueLobbyRoomText.QueueLabel(snapshot.QueueId) + " · " + snapshot.Members.Count + " 人" +
                (snapshot.IsLeader ? " · 队长" : "");
            _status.Text = status ?? string.Empty;
            _members.Items.Clear();
            if (snapshot != null)
            {
                for (var index = 0; index < _queues.Items.Count; index++)
                    if (((Option)_queues.Items[index]).Id == snapshot.QueueId) _queues.SelectedIndex = index;
                foreach (var member in snapshot.Members)
                    if (member.Id != snapshot.SelfId)
                        _members.Items.Add(new Option { Id = member.Id, Label = member.Name });
            }
            if (_members.Items.Count > 0) _members.SelectedIndex = 0;
            UpdateEnabled();
        }

        private async Task ExecuteAsync(LeagueLobbyRoomAction? action, long value)
        {
            if (IsDisposed || !Visible || _request != null || (action.HasValue &&
                (_snapshot == null || !_snapshot.Allows(action.Value, value)))) return;
            var source = new CancellationTokenSource();
            _request = source;
            _controls.Visible = true;
            _status.Text = T(LeagueEfficiencyUiTextKeys.InsightLoading);
            UpdateEnabled();
            try
            {
                if (!action.HasValue)
                {
                    var snapshot = await _module.ReadLobbyRoomAsync(source.Token);
                    if (Current(source)) Render(snapshot, null);
                }
                else
                {
                    var result = await _module.ApplyLobbyRoomAsync(_snapshot, action.Value, value, source.Token);
                    if (Current(source)) Render(result.Snapshot, LeagueLobbyRoomText.Status(result.State));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                AppLog.Info("Native lobby room unavailable: " + e.GetType().Name);
                if (Current(source)) Render(null, null);
            }
            finally
            {
                if (ReferenceEquals(_request, source))
                {
                    _request = null;
                    if (!IsDisposed) UpdateEnabled();
                }
                source.Dispose();
            }
        }

        private bool Current(CancellationTokenSource source)
        {
            return !IsDisposed && Visible && ReferenceEquals(_request, source) && !source.IsCancellationRequested;
        }

        internal void ObservePhase(string phase)
        {
            if (_request == null && _snapshot != null && _snapshot.Phase != phase)
                Render(null, LeagueLobbyRoomText.Status("stale"));
        }

        internal void CancelRoom()
        {
            var previous = _request;
            _request = null;
            if (previous != null) previous.Cancel();
            _snapshot = null;
            if (!IsDisposed) { _controls.Visible = false; UpdateEnabled(); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) CancelRoom();
            base.Dispose(disposing);
        }

        private string T(string key) { return LeagueEfficiencyText.Get(_ui, key); }
    }
}
