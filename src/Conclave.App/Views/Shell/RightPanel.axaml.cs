using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Conclave.App.Views.Shell;

public partial class RightPanel : UserControl
{
    public RightPanel() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
