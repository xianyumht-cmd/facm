using System;
using System.Drawing;
using System.Linq;
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
        private readonly ComboBox _queues, _members, _firstRole, _secondRole;
        private readonly Button _positions, _spectator, _grant, _revoke;
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
            _firstRole = MakeCombo();
            _secondRole = MakeCombo();
            _firstRole.Width = _secondRole.Width = 88;
            _firstRole.AccessibleName = "首选位置";
            _secondRole.AccessibleName = "次选位置";
            for (var role = 0; role <= 6; role++)
            {
                _firstRole.Items.Add(new Option { Id = role, Label = LeagueLobbyRoomText.RoleLabel(role) });
                _secondRole.Items.Add(new Option { Id = role, Label = LeagueLobbyRoomText.RoleLabel(role) });
            }
            _positions = MakeButton("设置位置");
            _spectator = MakeButton("进入观战位");
            _grant = MakeButton("授予邀请权限");
            _revoke = MakeButton("撤销邀请权限");
            _positions.Click += async delegate { await ExecuteAsync(LeagueLobbyRoomAction.Positions, Selected(_firstRole) * 10 + Selected(_secondRole)); };
            _spectator.Click += async delegate { await ExecuteAsync(LeagueLobbyRoomAction.Spectator, 0); };
            _grant.Click += async delegate { await ExecuteAsync(LeagueLobbyRoomAction.GrantInvite, Selected(_members)); };
            _revoke.Click += async delegate { await ExecuteAsync(LeagueLobbyRoomAction.RevokeInvite, Selected(_members)); };
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
            _controls.Controls.Add(MakeRow(_grant, _revoke));
            _controls.Controls.Add(MakeRow(_firstRole, _secondRole, _positions));
            _controls.Controls.Add(MakeRow(_spectator));
            _members.SelectedIndexChanged += delegate { UpdateEnabled(); };
            _firstRole.SelectedIndexChanged += delegate { UpdateEnabled(); };
            _secondRole.SelectedIndexChanged += delegate { UpdateEnabled(); };
            _controls.Controls.Add(_status);
            Controls.Add(_controls);
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
            _firstRole.Enabled = _secondRole.Enabled = idle && _snapshot != null && _snapshot.CanSetPositions;
            _positions.Enabled = idle && _snapshot != null && _snapshot.Allows(LeagueLobbyRoomAction.Positions,
                Selected(_firstRole) * 10 + Selected(_secondRole));
            _spectator.Enabled = idle && _snapshot != null && _snapshot.CanSpectate;
            _grant.Enabled = idle && _snapshot != null && _snapshot.Allows(LeagueLobbyRoomAction.GrantInvite, Selected(_members));
            _revoke.Enabled = idle && _snapshot != null && _snapshot.Allows(LeagueLobbyRoomAction.RevokeInvite, Selected(_members));
        }

        private void Render(LeagueLobbyRoomSnapshot snapshot, string status)
        {
            _snapshot = snapshot;
            _controls.Visible = true;
            _description.Text = snapshot == null ? T(LeagueEfficiencyUiTextKeys.RoomUnavailable) :
                snapshot.Phase == "None" ? T(LeagueEfficiencyUiTextKeys.RoomNone) :
                snapshot.QueueName + " · " + snapshot.Members.Count + " 人" +
                (snapshot.IsLeader ? " · 队长" : "") + (snapshot.IsSpectator == true ? " · 观战" : "");
            _status.Text = string.IsNullOrEmpty(status) ? LeagueLobbyRoomText.CatalogStatus(snapshot) : status;
            _members.Items.Clear();
            _queues.Items.Clear();
            _firstRole.SelectedIndex = snapshot == null ? 0 : snapshot.FirstRole;
            _secondRole.SelectedIndex = snapshot == null ? 0 : snapshot.SecondRole;
            if (snapshot != null)
            {
                if (snapshot.QueueChoices != null)
                    foreach (var row in snapshot.QueueChoices.OrderBy(x => x.Key))
                        _queues.Items.Add(new Option { Id = row.Key, Label = row.Value });
                if (_queues.Items.Count > 0) _queues.SelectedIndex = 0;
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
