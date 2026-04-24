using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Conclave.App.Views.Shell;

public partial class PlanView : UserControl
{
    public PlanView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
