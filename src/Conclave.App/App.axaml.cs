using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Conclave.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        StartupLog.Mark("App.OnFrameworkInitializationCompleted: begin");
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }
        StartupLog.Mark("App.OnFrameworkInitializationCompleted: MainWindow assigned");

        base.OnFrameworkInitializationCompleted();
    }
}