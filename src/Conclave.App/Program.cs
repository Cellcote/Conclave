using Avalonia;
using Conclave.App.Claude;
using Conclave.App.Sessions;
using System;

namespace Conclave.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--smoke-db")
            return SmokeDb.Run();
        if (args.Length > 0 && args[0] == "--smoke-worktree")
            return SmokeWorktree.Run();
        if (args.Length > 0 && args[0] == "--smoke-claude")
            return SmokeClaude.RunAsync().GetAwaiter().GetResult();
        if (args.Length > 0 && args[0] == "--smoke-permission")
            return SmokePermission.RunAsync().GetAwaiter().GetResult();
        if (args.Length > 0 && args[0] == "--smoke-fusion")
            return SmokeFusion.Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
