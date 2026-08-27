using FACM.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FACM.App;

public sealed partial class MainWindow : Window
{
    private readonly ControlCenterViewModel _controlCenter;

    public MainWindow(ControlCenterViewModel controlCenter)
    {
        _controlCenter = controlCenter ?? throw new ArgumentNullException(nameof(controlCenter));
        InitializeComponent();
        RootNavigation.SelectedItem = RootNavigation.MenuItems[0];
        RootNavigation.Loaded += OnRootNavigationLoaded;
    }

    private async void OnRootNavigationLoaded(object sender, RoutedEventArgs e)
    {
        RootNavigation.Loaded -= OnRootNavigationLoaded;
        try
        {
            var currentVersion = typeof(App).Assembly.GetName().Version ?? new Version(4, 0, 0);
            await _controlCenter.RefreshAsync(currentVersion);
            SectionSubtitle.Text = _controlCenter.StatusText;
        }
        catch (Exception)
        {
            // Gate 2 keeps UI ownership narrow: startup state failure is presented as unavailable,
            // while transport/logging policy lands in later gates.
            SectionSubtitle.Text = "状态暂不可用";
        }
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
            _ => ("控制中心", _controlCenter.StatusText)
        };
        SectionTitle.Text = title;
        SectionSubtitle.Text = subtitle;
    }
}
