using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Conclave.App.Views.Shell;

public partial class LogsView : UserControl
{
    private ScrollHelper? _scroll;

    public LogsView()
    {
        InitializeComponent();
        _scroll = ScrollHelper.AttachIfReady(this.FindControl<ScrollViewer>("LogsScroller"));
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
