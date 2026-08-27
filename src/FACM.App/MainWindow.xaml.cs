using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FACM.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        RootNavigation.SelectedItem = RootNavigation.MenuItems[0];
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is not NavigationViewItem item) return;
        var tag = item.Tag?.ToString() ?? "home";
        var (title, subtitle) = tag switch
        {
            "repair" => ("清理与修复", "环境级恢复入口；正式能力将在后续 Gate 从 legacy orchestration 抽离。"),
            "league" => ("LOL 工作台", "比赛 / 攻略 / 自动化；唯一 League runtime 契约保持不变。"),
            "personalization" => ("个性化", "FACM 全局 Theme Resources 的统一入口。"),
            "settings" => ("更多设置", "typed settings、诊断与更新能力将在对应 Gate 接入。"),
            _ => ("控制中心", "我要打开什么：四个产品入口，不把内部模块边界暴露给用户。")
        };
        SectionTitle.Text = title;
        SectionSubtitle.Text = subtitle;
    }
}
