using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Conclave.App.Views.Shell;

public partial class LogsView : UserControl
{
    public LogsView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
