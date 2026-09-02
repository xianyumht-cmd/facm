using Microsoft.UI.Xaml.Controls;

namespace FACM.App;

public sealed partial class FacmInspectorBar : UserControl
{
    public FacmInspectorBar()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => InspectorText.Text;
        set => InspectorText.Text = value ?? string.Empty;
    }
}
