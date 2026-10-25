using System;
using System.Collections.Generic;
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
    internal sealed class LeagueFriendInvitationForm : Form
    {
        private readonly LeagueEfficiencyModule _module;
        private readonly UiTextCatalog _ui;
        private readonly CancellationTokenSource _request = new CancellationTokenSource();
        private readonly TextBox _search;
        private readonly ListView _friends;
        private readonly Label _status;
        private readonly FacmActionButton _refresh;
        private LeagueFriendInvitationCatalog _catalog;
        private bool _loading;

        internal LeagueFriendInvitationForm(LeagueEfficiencyModule module, UiTextCatalog ui)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            Text = T(LeagueEfficiencyUiTextKeys.FriendInviteTitle);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(630, 520);
            MinimumSize = new Size(540, 390);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18),
                ColumnCount = 1,
                RowCount = 4,
                BackColor = FacmDesignSystem.Canvas
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            layout.Controls.Add(new Label
            {
                Text = T(LeagueEfficiencyUiTextKeys.FriendInviteHint),
                Dock = DockStyle.Fill,
                ForeColor = FacmDesignSystem.TextMuted,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            _search = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 8, 5),
                AccessibleName = T(LeagueEfficiencyUiTextKeys.FriendInviteSearch),
                BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text
            };
            _search.TextChanged += delegate { RenderFriends(); };
            toolbar.Controls.Add(_search, 0, 0);
            _refresh = new FacmActionButton
            {
                Text = T(LeagueEfficiencyUiTextKeys.FriendInviteRefresh),
                Dock = DockStyle.Fill,
                Tone = FacmButtonTone.Secondary
            };
            _refresh.Click += async delegate { await LoadFriendsAsync(); };
            toolbar.Controls.Add(_refresh, 1, 0);
            layout.Controls.Add(toolbar, 0, 1);

            _friends = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                CheckBoxes = true,
                HideSelection = false,
                MultiSelect = false,
                BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text
            };
            _friends.Columns.Add(T(LeagueEfficiencyUiTextKeys.FriendInviteName), 395);
            _friends.Columns.Add(T(LeagueEfficiencyUiTextKeys.FriendInviteAvailability), 140);
            _friends.ItemCheck += HandleItemCheck;
            layout.Controls.Add(_friends, 0, 2);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            _status = new Label
            {
                Text = T(LeagueEfficiencyUiTextKeys.FriendInviteHint),
                Dock = DockStyle.Fill,
                ForeColor = FacmDesignSystem.TextMuted,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            footer.Controls.Add(_status, 0, 0);
            var close = new FacmActionButton
            {
                Text = T(LeagueEfficiencyUiTextKeys.FriendInviteClose),
                Dock = DockStyle.Fill,
                Tone = FacmButtonTone.Secondary
            };
            close.Click += delegate { Close(); };
            footer.Controls.Add(close, 1, 0);
            layout.Controls.Add(footer, 0, 3);
            Controls.Add(layout);

            Shown += async delegate { await LoadFriendsAsync(); };
            FormClosed += delegate { _request.Cancel(); _request.Dispose(); };
        }

        private async Task LoadFriendsAsync()
        {
            if (_loading || IsDisposed) return;
            _loading = true;
            _refresh.Enabled = false;
            _status.Text = T(LeagueEfficiencyUiTextKeys.FriendInviteLoading);
            try
            {
                var loaded = await _module.LoadInvitationFriendsAsync(_request.Token);
                if (_request.IsCancellationRequested || IsDisposed || Disposing) return;
                _catalog = loaded;
                RenderFriends();
                _status.Text = loaded == null ? T(LeagueEfficiencyUiTextKeys.FriendInviteUnavailable) :
                    string.Format(T(LeagueEfficiencyUiTextKeys.FriendInviteCount), _module.ScheduledFriendInvitationCount);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                if (!IsDisposed && !Disposing)
                    _status.Text = string.Format(T(LeagueEfficiencyUiTextKeys.SaveFailed), exception.GetType().Name);
            }
            finally
            {
                _loading = false;
                if (!IsDisposed && !Disposing) _refresh.Enabled = true;
            }
        }

        private void RenderFriends()
        {
            if (_friends == null) return;
            var query = (_search.Text ?? string.Empty).Trim();
            _friends.BeginUpdate();
            var oldLoading = _loading;
            _loading = true;
            try
            {
                _friends.Items.Clear();
                if (_catalog == null) return;
                foreach (var friend in _catalog.Friends.Where(friend => query.Length == 0 ||
                    friend.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    var item = new ListViewItem(friend.Name) { Tag = friend, Checked = friend.Selected };
                    item.SubItems.Add(AvailabilityLabel(friend.Availability));
                    if (friend.Attempted) item.ForeColor = FacmDesignSystem.TextMuted;
                    _friends.Items.Add(item);
                }
            }
            finally
            {
                _loading = oldLoading;
                _friends.EndUpdate();
            }
        }

        private void HandleItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_loading || _catalog == null) return;
            var friend = _friends.Items[e.Index].Tag as LeagueFriendInvitationChoice;
            if (friend == null) return;
            var selected = e.NewValue == CheckState.Checked;
            if (!_module.SetScheduledFriendInvitation(_catalog.LobbyKey, friend.Puuid, selected))
            {
                e.NewValue = e.CurrentValue;
                _status.Text = T(LeagueEfficiencyUiTextKeys.FriendInviteRejected);
                return;
            }
            friend.Selected = selected;
            _status.Text = string.Format(T(LeagueEfficiencyUiTextKeys.FriendInviteCount),
                _module.ScheduledFriendInvitationCount);
        }

        private static string AvailabilityLabel(string value)
        {
            switch (value)
            {
                case "chat": return "在线";
                case "dnd": return "游戏中";
                case "away": return "离开";
                default: return "离线";
            }
        }

        private string T(string key) { return LeagueEfficiencyText.Get(_ui, key); }
    }
}
