using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeagueDeathDamageDialog : Form
    {
        private readonly CancellationTokenSource _lifetime;
        private readonly ImageList _icons = new ImageList { ImageSize = new Size(24, 24), ColorDepth = ColorDepth.Depth32Bit };
        private readonly List<Bitmap> _ownedIcons = new List<Bitmap>();
        private readonly List<ListViewItem> _rows = new List<ListViewItem>();

        internal LeagueDeathDamageDialog(UiTextCatalog ui, LeagueDeathDamage damage,
            List<LeaguePlayerMatchParticipant> players, LeagueBuildAdvisorCatalog catalog,
            Func<string, CancellationToken, Task<Bitmap>> loadAsset, Func<bool> isCurrent, CancellationToken token)
        {
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            Text = ui.Get(UiTextKeys.LeagueDeathDamageTitle);
            Size = new Size(790, 500); MinimumSize = new Size(550, 330);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            BackColor = FacmDesignSystem.Canvas; ForeColor = FacmDesignSystem.Text;
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var victim = PlayerName(players, damage.VictimId);
            AddDirection(tabs, ui, damage.Received, true, victim, damage.VictimId, players);
            AddDirection(tabs, ui, damage.Dealt, false, victim, damage.VictimId, players);
            Controls.Add(tabs);
            Shown += async (sender, args) =>
            {
                if (loadAsset == null || catalog == null) return;
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(8));
                    try
                    {
                        foreach (var id in _rows.Select(row => (int)row.Tag).Where(id => id > 0).Distinct().Take(20))
                        {
                            if (deadline.IsCancellationRequested || (isCurrent != null && !isCurrent())) return;
                            var player = players.FirstOrDefault(p => p.ParticipantId == id);
                            string path;
                            if (player == null || !catalog.ChampionIcons.TryGetValue(player.ChampionId, out path)) continue;
                            using (var bitmap = await loadAsset(path, deadline.Token))
                            {
                                if (IsDisposed || deadline.IsCancellationRequested || (isCurrent != null && !isCurrent())) return;
                                if (bitmap == null) continue;
                                var key = id.ToString();
                                var owned = new Bitmap(bitmap);
                                _ownedIcons.Add(owned);
                                _icons.Images.Add(key, owned);
                                foreach (var row in _rows.Where(row => (int)row.Tag == id)) row.ImageKey = key;
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception) { /* Optional icons do not hide source damage values. */ }
                }
            };
        }

        private void AddDirection(TabControl tabs, UiTextCatalog ui, LeagueDamageDirection direction,
            bool received, string victim, int victimId, List<LeaguePlayerMatchParticipant> players)
        {
            var page = new TabPage(ui.Get(received ? UiTextKeys.LeagueDamageReceived : UiTextKeys.LeagueDamageDealt));
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var total = direction.Total;
            var summary = new Label { Dock = DockStyle.Fill, AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft, Text = victim + " · " + page.Text + "\r\n" +
                    ui.Get(UiTextKeys.LeagueDamageTotals).Replace("{0}", LeagueDamageAmounts.Format(total?.Total))
                        .Replace("{1}", LeagueDamageAmounts.Format(total?.Physical))
                        .Replace("{2}", LeagueDamageAmounts.Format(total?.Magic))
                        .Replace("{3}", LeagueDamageAmounts.Format(total?.True)) };
            var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                HideSelection = false, MultiSelect = false, SmallImageList = _icons,
                BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text };
            foreach (var key in new[] { UiTextKeys.LeagueDamagePeer, UiTextKeys.LeagueDamageAbility,
                UiTextKeys.LeagueDamageTotal, UiTextKeys.LeagueDamagePhysical, UiTextKeys.LeagueDamageMagic,
                UiTextKeys.LeagueDamageTrue }) list.Columns.Add(ui.Get(key), key == UiTextKeys.LeagueDamagePeer ? 255 : 85);
            foreach (var source in direction.Sources)
            {
                var peer = source.ParticipantId > 0 ? PlayerName(players, source.ParticipantId) :
                    ui.Get(source.Type == "tower" ? UiTextKeys.LeagueDamageTower :
                    source.Type == "minion" ? UiTextKeys.LeagueDamageMinion :
                    source.Type == "monster" ? UiTextKeys.LeagueDamageMonster : UiTextKeys.LeagueDamageOther) +
                    (source.Name.Length == 0 ? "" : " · " + source.Name);
                var name = received ? peer + " → " + victim : victim + " → " + peer;
                var iconId = received ? source.ParticipantId : victimId;
                AddRow(list, name, ui.Get(UiTextKeys.LeagueDamageTotal), source.Amounts, iconId);
                foreach (var ability in new[] { "A", "P", "Q", "W", "E", "R", "?" })
                {
                    LeagueDamageAmounts values;
                    if (source.Abilities.TryGetValue(ability, out values)) AddRow(list, peer, ability, values, 0);
                }
            }
            if (direction.Sources.Count == 0)
                list.Items.Add(new ListViewItem(ui.Get(UiTextKeys.LeagueDamageUnavailable)));
            layout.Controls.Add(summary, 0, 0); layout.Controls.Add(list, 0, 1);
            page.Controls.Add(layout); tabs.TabPages.Add(page);
        }

        private void AddRow(ListView list, string name, string ability, LeagueDamageAmounts amounts, int iconId)
        {
            var row = new ListViewItem(name) { Tag = iconId, UseItemStyleForSubItems = false };
            row.SubItems.Add(ability);
            row.SubItems.Add(LeagueDamageAmounts.Format(amounts.Total));
            row.SubItems.Add(LeagueDamageAmounts.Format(amounts.Physical)).ForeColor = Color.Coral;
            row.SubItems.Add(LeagueDamageAmounts.Format(amounts.Magic)).ForeColor = Color.DeepSkyBlue;
            row.SubItems.Add(LeagueDamageAmounts.Format(amounts.True)).ForeColor = FacmDesignSystem.Text;
            _rows.Add(row); list.Items.Add(row);
        }

        private static string PlayerName(List<LeaguePlayerMatchParticipant> players, int id)
        {
            var player = players.FirstOrDefault(p => p.ParticipantId == id);
            return player == null ? id > 0 ? "#" + id : "—" :
                (string.IsNullOrEmpty(player.PlayerName) ? "#" + id : player.PlayerName) + " · " +
                (string.IsNullOrEmpty(player.ChampionName) ? "#" + player.ChampionId : player.ChampionName);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed) { _lifetime.Cancel(); _lifetime.Dispose(); }
            base.Dispose(disposing);
            if (disposing) { _icons.Dispose(); foreach (var icon in _ownedIcons) icon.Dispose(); _ownedIcons.Clear(); }
        }
    }
}
