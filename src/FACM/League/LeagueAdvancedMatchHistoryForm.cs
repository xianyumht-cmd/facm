using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    // A transient native expression editor. It never reads the League Client and never modifies
    // an active filter until the user applies a completely validated copy of the tree.
    internal sealed class LeagueAdvancedMatchHistoryForm : Form
    {
        private static readonly LeagueAdvancedMatchKind[] ConditionKinds =
        {
            LeagueAdvancedMatchKind.IsGameMode,
            LeagueAdvancedMatchKind.IsQueue,
            LeagueAdvancedMatchKind.IsMap,
            LeagueAdvancedMatchKind.DurationBetween,
            LeagueAdvancedMatchKind.IsWin,
            LeagueAdvancedMatchKind.IsLoss,
            LeagueAdvancedMatchKind.IsNormalLoss,
            LeagueAdvancedMatchKind.IsSurrenderLoss,
            LeagueAdvancedMatchKind.IsRemake,
            LeagueAdvancedMatchKind.IsAbort,
            LeagueAdvancedMatchKind.IsMatchedGame,
            LeagueAdvancedMatchKind.IsPveGame,
            LeagueAdvancedMatchKind.HasChampion,
            LeagueAdvancedMatchKind.HasPlayer,
            LeagueAdvancedMatchKind.KillsBetween,
            LeagueAdvancedMatchKind.DeathsBetween,
            LeagueAdvancedMatchKind.AssistsBetween,
            LeagueAdvancedMatchKind.CsBetween,
            LeagueAdvancedMatchKind.HasItem,
            LeagueAdvancedMatchKind.HasSpell,
            LeagueAdvancedMatchKind.HasPerk,
            LeagueAdvancedMatchKind.HasPerkStyle,
            LeagueAdvancedMatchKind.HasAugment,
            LeagueAdvancedMatchKind.IsPosition,
            LeagueAdvancedMatchKind.KdaBetween,
            LeagueAdvancedMatchKind.LevelBetween,
            LeagueAdvancedMatchKind.GoldBetween,
            LeagueAdvancedMatchKind.GoldSpentBetween,
            LeagueAdvancedMatchKind.DamageToChampionsBetween,
            LeagueAdvancedMatchKind.PhysicalDamageToChampionsBetween,
            LeagueAdvancedMatchKind.MagicDamageToChampionsBetween,
            LeagueAdvancedMatchKind.TrueDamageToChampionsBetween,
            LeagueAdvancedMatchKind.DamageTakenBetween,
            LeagueAdvancedMatchKind.PhysicalDamageTakenBetween,
            LeagueAdvancedMatchKind.MagicDamageTakenBetween,
            LeagueAdvancedMatchKind.TrueDamageTakenBetween,
            LeagueAdvancedMatchKind.DamageToTowersBetween,
            LeagueAdvancedMatchKind.HealBetween,
            LeagueAdvancedMatchKind.VisionScoreBetween,
            LeagueAdvancedMatchKind.TimeCCingOthersBetween,
            LeagueAdvancedMatchKind.SoloKillsBetween,
            LeagueAdvancedMatchKind.KillParticipationBetween,
            LeagueAdvancedMatchKind.DamageGoldEfficiencyBetween,
            LeagueAdvancedMatchKind.ChampionDamageShareBetween,
            LeagueAdvancedMatchKind.DamageTakenShareBetween,
            LeagueAdvancedMatchKind.GoldShareBetween,
            LeagueAdvancedMatchKind.DoubleKillsBetween,
            LeagueAdvancedMatchKind.TripleKillsBetween,
            LeagueAdvancedMatchKind.QuadraKillsBetween,
            LeagueAdvancedMatchKind.PentaKillsBetween
        };

        private readonly LeagueAdvancedMatchNode _root;
        private readonly TreeView _tree;
        private readonly ComboBox _rootMode;
        private readonly ComboBox _conditionType;
        private readonly TextBox _conditionValue;
        private readonly Label _inputHint;
        private bool _changingSelection;

        internal LeagueAdvancedMatchHistoryFilter Result { get; private set; }

        internal LeagueAdvancedMatchHistoryForm(LeagueAdvancedMatchHistoryFilter existing)
        {
            Text = UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedTitle);
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(736, 576);
            MinimumSize = new Size(700, 530);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);

            _root = existing == null ? new LeagueAdvancedMatchNode { Kind = LeagueAdvancedMatchKind.And } :
                Copy(existing.Root);
            var instruction = new Label
            {
                Text = UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedHint),
                Bounds = new Rectangle(18, 14, 698, 42),
                ForeColor = FacmDesignSystem.TextMuted,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            _rootMode = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(18, 60, 176, 26),
                BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text
            };
            _rootMode.Items.Add(UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedAnd));
            _rootMode.Items.Add(UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedOr));
            _rootMode.SelectedIndex = _root.Kind == LeagueAdvancedMatchKind.Or ? 1 : 0;

            _tree = new TreeView
            {
                Bounds = new Rectangle(18, 94, 700, 278),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                BackColor = FacmDesignSystem.Surface, ForeColor = FacmDesignSystem.Text,
                BorderStyle = BorderStyle.FixedSingle, HideSelection = false
            };
            var rootView = BuildTreeNode(_root);
            _tree.Nodes.Add(rootView);
            rootView.ExpandAll();
            _tree.SelectedNode = rootView;

            var addCondition = Action(UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedAddCondition), 18, 382, 98,
                (s, e) => Add(LeagueAdvancedMatchKind.IsQueue));
            var addAnd = Action(UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedAddAnd), 122, 382, 110,
                (s, e) => Add(LeagueAdvancedMatchKind.And));
            var addOr = Action(UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedAddOr), 238, 382, 110,
                (s, e) => Add(LeagueAdvancedMatchKind.Or));
            var addNot = Action(UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedAddNot), 354, 382, 110,
                (s, e) => Add(LeagueAdvancedMatchKind.Not));
            var remove = Action(UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedRemove), 470, 382, 104,
                (s, e) => RemoveSelected());
            var scopeMenu = new ContextMenuStrip();
            foreach (var kind in new[] { LeagueAdvancedMatchKind.Player, LeagueAdvancedMatchKind.Anyone,
                LeagueAdvancedMatchKind.Everyone, LeagueAdvancedMatchKind.All,
                LeagueAdvancedMatchKind.Allies, LeagueAdvancedMatchKind.Enemies })
            {
                var choice = kind;
                scopeMenu.Items.Add(Description(choice), null, (s, e) => Add(choice));
            }
            Button addScope = null;
            addScope = Action(UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedScope), 580, 382, 138,
                (s, e) => scopeMenu.Show(addScope, new Point(0, addScope.Height)));
            Disposed += (s, e) => scopeMenu.Dispose();
            foreach (var button in new[] { addCondition, addAnd, addOr, addNot, remove, addScope })
                button.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;

            _conditionType = new ComboBox
            {
                Bounds = new Rectangle(18, 429, 180, 27),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text
            };
            foreach (var kind in ConditionKinds) _conditionType.Items.Add(Description(kind));
            _conditionValue = new TextBox
            {
                Bounds = new Rectangle(208, 429, 340, 27),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text
            };
            _inputHint = new Label
            {
                Bounds = new Rectangle(18, 463, 700, 38),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = FacmDesignSystem.TextMuted, AutoEllipsis = true
            };

            var clear = Action(UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedClear), 18, 516, 104, (s, e) => Clear());
            var cancel = Action(UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedCancel), 516, 516, 94,
                (s, e) => { DialogResult = DialogResult.Cancel; Close(); });
            var apply = Action(UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedApply), 616, 516, 102,
                (s, e) => Apply());
            clear.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            apply.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            AcceptButton = apply;
            CancelButton = cancel;

            Controls.AddRange(new Control[] { instruction, _rootMode, _tree, addCondition, addAnd, addOr,
                addNot, remove, addScope, _conditionType, _conditionValue, _inputHint, clear, cancel, apply });
            _tree.AfterSelect += (s, e) => ShowSelectedCondition();
            _rootMode.SelectedIndexChanged += (s, e) =>
            {
                _root.Kind = _rootMode.SelectedIndex == 1 ? LeagueAdvancedMatchKind.Or : LeagueAdvancedMatchKind.And;
                _tree.Nodes[0].Text = Description(_root.Kind);
            };
            _conditionType.SelectedIndexChanged += (s, e) => ChangeConditionType();
            _conditionValue.TextChanged += (s, e) => ChangeConditionValue();
            ShowSelectedCondition();
        }

        private Button Action(string title, int x, int y, int width, EventHandler click)
        {
            var button = new Button
            {
                Text = title, Bounds = new Rectangle(x, y, width, 30), FlatStyle = FlatStyle.Flat,
                BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += click;
            return button;
        }

        private static LeagueAdvancedMatchNode Copy(LeagueAdvancedMatchNode node)
        {
            var result = new LeagueAdvancedMatchNode { Kind = node.Kind, Value = node.Value };
            foreach (var child in node.Children) result.Children.Add(Copy(child));
            return result;
        }

        private static string Description(LeagueAdvancedMatchKind kind)
        {
            switch (kind)
            {
                case LeagueAdvancedMatchKind.And: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedAnd);
                case LeagueAdvancedMatchKind.Or: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedOr);
                case LeagueAdvancedMatchKind.Not: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedNot);
                case LeagueAdvancedMatchKind.Player: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedPlayerScope);
                case LeagueAdvancedMatchKind.Anyone: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedAnyone);
                case LeagueAdvancedMatchKind.Everyone: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedEveryone);
                case LeagueAdvancedMatchKind.All: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedAll);
                case LeagueAdvancedMatchKind.Allies: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedAllies);
                case LeagueAdvancedMatchKind.Enemies: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedEnemies);
                case LeagueAdvancedMatchKind.IsGameMode: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedMode);
                case LeagueAdvancedMatchKind.IsQueue: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedQueue);
                case LeagueAdvancedMatchKind.IsMap: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedMap);
                case LeagueAdvancedMatchKind.DurationBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedDuration);
                case LeagueAdvancedMatchKind.IsWin: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedWin);
                case LeagueAdvancedMatchKind.IsLoss: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedLoss);
                case LeagueAdvancedMatchKind.IsNormalLoss: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedNormalLoss);
                case LeagueAdvancedMatchKind.IsSurrenderLoss: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedSurrenderLoss);
                case LeagueAdvancedMatchKind.IsRemake: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedRemake);
                case LeagueAdvancedMatchKind.IsAbort: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedAbort);
                case LeagueAdvancedMatchKind.IsMatchedGame: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedMatched);
                case LeagueAdvancedMatchKind.IsPveGame: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedPve);
                case LeagueAdvancedMatchKind.HasChampion: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedChampion);
                case LeagueAdvancedMatchKind.HasPlayer: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedPlayer);
                case LeagueAdvancedMatchKind.KillsBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedKills);
                case LeagueAdvancedMatchKind.DeathsBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedDeaths);
                case LeagueAdvancedMatchKind.AssistsBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedAssists);
                case LeagueAdvancedMatchKind.CsBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedCs);
                case LeagueAdvancedMatchKind.HasItem: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedItem);
                case LeagueAdvancedMatchKind.HasSpell: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedSpell);
                case LeagueAdvancedMatchKind.HasPerk: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedPerk);
                case LeagueAdvancedMatchKind.HasPerkStyle: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedPerkStyle);
                case LeagueAdvancedMatchKind.HasAugment: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedAugment);
                case LeagueAdvancedMatchKind.IsPosition: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedPosition);
                case LeagueAdvancedMatchKind.KdaBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedKda);
                case LeagueAdvancedMatchKind.LevelBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedLevel);
                case LeagueAdvancedMatchKind.GoldBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedGold);
                case LeagueAdvancedMatchKind.GoldSpentBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedGoldSpent);
                case LeagueAdvancedMatchKind.DamageToChampionsBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedDamage);
                case LeagueAdvancedMatchKind.PhysicalDamageToChampionsBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedPhysicalDamage);
                case LeagueAdvancedMatchKind.MagicDamageToChampionsBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedMagicDamage);
                case LeagueAdvancedMatchKind.TrueDamageToChampionsBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedTrueDamage);
                case LeagueAdvancedMatchKind.DamageTakenBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedTakenDamage);
                case LeagueAdvancedMatchKind.PhysicalDamageTakenBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedTakenPhysical);
                case LeagueAdvancedMatchKind.MagicDamageTakenBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedTakenMagic);
                case LeagueAdvancedMatchKind.TrueDamageTakenBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedTakenTrue);
                case LeagueAdvancedMatchKind.DamageToTowersBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedTowerDamage);
                case LeagueAdvancedMatchKind.HealBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedHeal);
                case LeagueAdvancedMatchKind.VisionScoreBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedVision);
                case LeagueAdvancedMatchKind.TimeCCingOthersBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedCc);
                case LeagueAdvancedMatchKind.SoloKillsBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedSoloKills);
                case LeagueAdvancedMatchKind.KillParticipationBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedParticipation);
                case LeagueAdvancedMatchKind.DamageGoldEfficiencyBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedEfficiency);
                case LeagueAdvancedMatchKind.ChampionDamageShareBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedDamageShare);
                case LeagueAdvancedMatchKind.DamageTakenShareBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedTakenShare);
                case LeagueAdvancedMatchKind.GoldShareBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedGoldShare);
                case LeagueAdvancedMatchKind.DoubleKillsBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedDoubleKills);
                case LeagueAdvancedMatchKind.TripleKillsBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedTripleKills);
                case LeagueAdvancedMatchKind.QuadraKillsBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedQuadraKills);
                case LeagueAdvancedMatchKind.PentaKillsBetween: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedPentaKills);
                default: return UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedUnknown);
            }
        }

        private static string Display(LeagueAdvancedMatchNode node)
        {
            var name = Description(node.Kind);
            if (IsScopedIdentityGroup(node.Kind))
                return name + ": " + (string.IsNullOrEmpty(node.Value) ?
                    UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedEnterValue) : node.Value);
            return LeagueAdvancedMatchNode.IsGroup(node.Kind) ||
                IsResultCondition(node.Kind) ?
                name : name + ": " + (string.IsNullOrEmpty(node.Value) ?
                    UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedEnterValue) : node.Value);
        }

        private static bool IsScopedIdentityGroup(LeagueAdvancedMatchKind kind)
        {
            return kind == LeagueAdvancedMatchKind.Player ||
                kind == LeagueAdvancedMatchKind.Allies || kind == LeagueAdvancedMatchKind.Enemies;
        }

        private static bool IsResultCondition(LeagueAdvancedMatchKind kind)
        {
            return kind == LeagueAdvancedMatchKind.IsWin || kind == LeagueAdvancedMatchKind.IsLoss ||
                kind == LeagueAdvancedMatchKind.IsNormalLoss ||
                kind == LeagueAdvancedMatchKind.IsSurrenderLoss ||
                kind == LeagueAdvancedMatchKind.IsRemake || kind == LeagueAdvancedMatchKind.IsAbort ||
                kind == LeagueAdvancedMatchKind.IsMatchedGame || kind == LeagueAdvancedMatchKind.IsPveGame;
        }

        private static TreeNode BuildTreeNode(LeagueAdvancedMatchNode node)
        {
            var treeNode = new TreeNode(Display(node)) { Tag = node };
            foreach (var child in node.Children) treeNode.Nodes.Add(BuildTreeNode(child));
            return treeNode;
        }

        private void Add(LeagueAdvancedMatchKind kind)
        {
            var parent = _tree.SelectedNode ?? _tree.Nodes[0];
            if (!LeagueAdvancedMatchNode.IsGroup(((LeagueAdvancedMatchNode)parent.Tag).Kind)) parent = parent.Parent;
            if (parent == null) return;
            var node = (LeagueAdvancedMatchNode)parent.Tag;
            if (node.Kind != LeagueAdvancedMatchKind.And && node.Kind != LeagueAdvancedMatchKind.Or &&
                node.Children.Count > 0)
            {
                MessageBox.Show(this, UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedScopeLimit), Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (CountNodes(_tree.Nodes[0]) >= 40 || parent.Level >= 7)
            {
                MessageBox.Show(this, UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedSizeLimit), Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var added = new LeagueAdvancedMatchNode { Kind = kind };
            node.Children.Add(added);
            var view = BuildTreeNode(added);
            parent.Nodes.Add(view);
            parent.Expand();
            _tree.SelectedNode = view;
        }

        private static int CountNodes(TreeNode node)
        {
            return 1 + node.Nodes.Cast<TreeNode>().Sum(CountNodes);
        }

        private void RemoveSelected()
        {
            var selected = _tree.SelectedNode;
            if (selected == null || selected.Parent == null) return;
            var parent = selected.Parent;
            ((LeagueAdvancedMatchNode)parent.Tag).Children.Remove((LeagueAdvancedMatchNode)selected.Tag);
            selected.Remove();
            _tree.SelectedNode = parent;
        }

        private void Clear()
        {
            _root.Children.Clear();
            _tree.Nodes[0].Nodes.Clear();
            _tree.SelectedNode = _tree.Nodes[0];
        }

        private void ShowSelectedCondition()
        {
            _changingSelection = true;
            try
            {
                var node = _tree.SelectedNode == null ? null : (LeagueAdvancedMatchNode)_tree.SelectedNode.Tag;
                var condition = node != null && !LeagueAdvancedMatchNode.IsGroup(node.Kind);
                var scopedIdentity = node != null && IsScopedIdentityGroup(node.Kind);
                _conditionType.Enabled = condition;
                _conditionValue.Enabled = scopedIdentity || condition && !IsResultCondition(node.Kind);
                _conditionType.SelectedIndex = condition ? Array.IndexOf(ConditionKinds, node.Kind) : -1;
                _conditionValue.Text = condition || scopedIdentity ? node.Value ?? string.Empty : string.Empty;
                _inputHint.Text = scopedIdentity ? UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedScopeHint) :
                    !condition ? UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedSelectHint) :
                    node.Kind == LeagueAdvancedMatchKind.IsGameMode ? UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedModeHint) :
                    node.Kind == LeagueAdvancedMatchKind.HasPlayer ? UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedPlayerHint) :
                    IsResultCondition(node.Kind) ?
                        UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedResultHint) :
                    node.Kind == LeagueAdvancedMatchKind.IsQueue || node.Kind == LeagueAdvancedMatchKind.IsMap ||
                    node.Kind == LeagueAdvancedMatchKind.HasChampion ? UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedIdHint) :
                    node.Kind == LeagueAdvancedMatchKind.HasItem || node.Kind == LeagueAdvancedMatchKind.HasSpell ||
                    node.Kind == LeagueAdvancedMatchKind.HasPerk || node.Kind == LeagueAdvancedMatchKind.HasPerkStyle ||
                    node.Kind == LeagueAdvancedMatchKind.HasAugment ? UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedSlotHint) :
                    node.Kind == LeagueAdvancedMatchKind.IsPosition ? UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedPositionHint) :
                    IsPercentCondition(node.Kind) ? UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedPercentHint) :
                    IsMeasuredCondition(node.Kind) ? UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedMeasureHint) :
                    (int)node.Kind >= (int)LeagueAdvancedMatchKind.KdaBetween ? UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedDecimalHint) :
                    UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedRangeHint);
            }
            finally { _changingSelection = false; }
        }

        private static bool IsPercentCondition(LeagueAdvancedMatchKind kind)
        {
            return kind == LeagueAdvancedMatchKind.KillParticipationBetween ||
                kind == LeagueAdvancedMatchKind.DamageGoldEfficiencyBetween ||
                kind == LeagueAdvancedMatchKind.ChampionDamageShareBetween ||
                kind == LeagueAdvancedMatchKind.DamageTakenShareBetween ||
                kind == LeagueAdvancedMatchKind.GoldShareBetween;
        }

        private static bool IsMeasuredCondition(LeagueAdvancedMatchKind kind)
        {
            return kind == LeagueAdvancedMatchKind.KillsBetween ||
                kind == LeagueAdvancedMatchKind.DeathsBetween ||
                kind == LeagueAdvancedMatchKind.AssistsBetween ||
                kind == LeagueAdvancedMatchKind.CsBetween ||
                ((int)kind >= (int)LeagueAdvancedMatchKind.LevelBetween &&
                 (int)kind <= (int)LeagueAdvancedMatchKind.SoloKillsBetween) ||
                ((int)kind >= (int)LeagueAdvancedMatchKind.DoubleKillsBetween &&
                 (int)kind <= (int)LeagueAdvancedMatchKind.PentaKillsBetween);
        }

        private void ChangeConditionType()
        {
            if (_changingSelection || _tree.SelectedNode == null || _conditionType.SelectedIndex < 0) return;
            var node = (LeagueAdvancedMatchNode)_tree.SelectedNode.Tag;
            if (LeagueAdvancedMatchNode.IsGroup(node.Kind)) return;
            var kind = ConditionKinds[_conditionType.SelectedIndex];
            if (kind == node.Kind) return;
            node.Kind = kind;
            node.Value = string.Empty;
            _tree.SelectedNode.Text = Display(node);
            ShowSelectedCondition();
        }

        private void ChangeConditionValue()
        {
            if (_changingSelection || _tree.SelectedNode == null) return;
            var node = (LeagueAdvancedMatchNode)_tree.SelectedNode.Tag;
            if (LeagueAdvancedMatchNode.IsGroup(node.Kind) && !IsScopedIdentityGroup(node.Kind)) return;
            node.Value = _conditionValue.Text;
            _tree.SelectedNode.Text = Display(node);
        }

        private void Apply()
        {
            if (_root.Children.Count == 0)
            {
                Result = null;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            LeagueAdvancedMatchHistoryFilter filter;
            string error;
            if (!LeagueAdvancedMatchHistoryFilter.TryCreate(_root, out filter, out error))
            {
                MessageBox.Show(this, UiTextRuntime.Text(error), UiTextRuntime.Text(UiTextKeys.LeaguePlayerAdvancedInvalid),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Result = filter;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
