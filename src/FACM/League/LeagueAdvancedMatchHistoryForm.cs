using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
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
            LeagueAdvancedMatchKind.HasChampion,
            LeagueAdvancedMatchKind.HasPlayer,
            LeagueAdvancedMatchKind.KillsBetween,
            LeagueAdvancedMatchKind.DeathsBetween,
            LeagueAdvancedMatchKind.AssistsBetween,
            LeagueAdvancedMatchKind.CsBetween
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
            Text = "高级战绩筛选";
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
                Text = "按组组合 AND / OR / NOT，并与基础筛选叠加；仅过滤已加载战绩，缺失数据不会通过 NOT。",
                Bounds = new Rectangle(18, 14, 698, 42),
                ForeColor = FacmDesignSystem.TextMuted,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            _rootMode = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(18, 60, 176, 26),
                BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text
            };
            _rootMode.Items.Add("所有条件（AND）");
            _rootMode.Items.Add("任意条件（OR）");
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

            var addCondition = Action("添加条件", 18, 382, 98, (s, e) => Add(LeagueAdvancedMatchKind.IsQueue));
            var addAnd = Action("添加 AND 组", 122, 382, 110, (s, e) => Add(LeagueAdvancedMatchKind.And));
            var addOr = Action("添加 OR 组", 238, 382, 110, (s, e) => Add(LeagueAdvancedMatchKind.Or));
            var addNot = Action("添加 NOT 组", 354, 382, 110, (s, e) => Add(LeagueAdvancedMatchKind.Not));
            var remove = Action("删除选中", 470, 382, 104, (s, e) => RemoveSelected());
            foreach (var button in new[] { addCondition, addAnd, addOr, addNot, remove })
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

            var clear = Action("清空规则", 18, 516, 104, (s, e) => Clear());
            var cancel = Action("取消", 516, 516, 94, (s, e) => { DialogResult = DialogResult.Cancel; Close(); });
            var apply = Action("应用筛选", 616, 516, 102, (s, e) => Apply());
            clear.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            apply.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            AcceptButton = apply;
            CancelButton = cancel;

            Controls.AddRange(new Control[] { instruction, _rootMode, _tree, addCondition, addAnd, addOr,
                addNot, remove, _conditionType, _conditionValue, _inputHint, clear, cancel, apply });
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
                case LeagueAdvancedMatchKind.And: return "满足全部（AND）";
                case LeagueAdvancedMatchKind.Or: return "满足任意（OR）";
                case LeagueAdvancedMatchKind.Not: return "排除条件（NOT）";
                case LeagueAdvancedMatchKind.IsGameMode: return "游戏模式";
                case LeagueAdvancedMatchKind.IsQueue: return "队列 ID";
                case LeagueAdvancedMatchKind.IsMap: return "地图 ID";
                case LeagueAdvancedMatchKind.DurationBetween: return "时长范围（秒）";
                case LeagueAdvancedMatchKind.IsWin: return "本人胜利";
                case LeagueAdvancedMatchKind.IsLoss: return "本人失败";
                case LeagueAdvancedMatchKind.HasChampion: return "本场有英雄";
                case LeagueAdvancedMatchKind.HasPlayer: return "本场有玩家 PUUID";
                case LeagueAdvancedMatchKind.KillsBetween: return "本人击杀范围";
                case LeagueAdvancedMatchKind.DeathsBetween: return "本人死亡范围";
                case LeagueAdvancedMatchKind.AssistsBetween: return "本人助攻范围";
                case LeagueAdvancedMatchKind.CsBetween: return "本人补刀范围";
                default: return "未知条件";
            }
        }

        private static string Display(LeagueAdvancedMatchNode node)
        {
            var name = Description(node.Kind);
            return LeagueAdvancedMatchNode.IsGroup(node.Kind) ||
                node.Kind == LeagueAdvancedMatchKind.IsWin || node.Kind == LeagueAdvancedMatchKind.IsLoss ?
                name : name + "：" + (string.IsNullOrEmpty(node.Value) ? "（请填写）" : node.Value);
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
            if (node.Kind == LeagueAdvancedMatchKind.Not && node.Children.Count > 0)
            {
                MessageBox.Show(this, "NOT 组只允许一个子条件，请先选择其已有子条件。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (CountNodes(_tree.Nodes[0]) >= 40 || parent.Level >= 7)
            {
                MessageBox.Show(this, "筛选条件数量或嵌套层级已达到上限。", Text,
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
                _conditionType.Enabled = condition;
                _conditionValue.Enabled = condition && node.Kind != LeagueAdvancedMatchKind.IsWin &&
                    node.Kind != LeagueAdvancedMatchKind.IsLoss;
                _conditionType.SelectedIndex = condition ? Array.IndexOf(ConditionKinds, node.Kind) : -1;
                _conditionValue.Text = condition ? node.Value ?? string.Empty : string.Empty;
                _inputHint.Text = !condition ? "选中一个条件后可修改类型及参数。" :
                    node.Kind == LeagueAdvancedMatchKind.IsGameMode ? "填写原始游戏模式，如 ARAM / CLASSIC / KIWI，区分大小写。" :
                    node.Kind == LeagueAdvancedMatchKind.HasPlayer ? "填写准确的玩家 PUUID，区分大小写；不支持玩家昵称猜测。" :
                    node.Kind == LeagueAdvancedMatchKind.IsWin || node.Kind == LeagueAdvancedMatchKind.IsLoss ? "胜负按本人可验证的战绩计算，无需参数。" :
                    node.Kind == LeagueAdvancedMatchKind.IsQueue || node.Kind == LeagueAdvancedMatchKind.IsMap ||
                    node.Kind == LeagueAdvancedMatchKind.HasChampion ? "填写正整数 ID，例如队列 450、地图 12 或英雄 22。" :
                    "填写 最小值,最大值，例如 600,1800；时长用秒，范围包含两端。";
            }
            finally { _changingSelection = false; }
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
            if (LeagueAdvancedMatchNode.IsGroup(node.Kind)) return;
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
                MessageBox.Show(this, error, "筛选条件不完整", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Result = filter;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
