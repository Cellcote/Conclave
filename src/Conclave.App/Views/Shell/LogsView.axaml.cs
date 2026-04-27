using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Conclave.App.Views.Shell;

public partial class LogsView : UserControl
{
    private ScrollViewer? _scroller;

    public LogsView()
    {
        InitializeComponent();
        _scroller = this.FindControl<ScrollViewer>("LogsScroller");
        var list = this.FindControl<ItemsControl>("LogsList");
        if (list?.ItemsView is { } view) view.CollectionChanged += OnLogsChanged;
    }

    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add
            && e.Action != NotifyCollectionChangedAction.Reset) return;
        Dispatcher.UIThread.Post(() => _scroller?.ScrollToEnd(), DispatcherPriority.Background);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
