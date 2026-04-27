using Avalonia.Controls;

namespace Conclave.App.Views.Shell;

// Stick-to-bottom auto-scroll for the transcript and logs. Watches ScrollChanged:
//
//   - When the content extent grows (a new message arrived, or the live message's
//     Content got streamed text appended), scroll to the bottom IF the user was
//     already at (or very near) the bottom before the change.
//
//   - When the user scrolls manually (extent unchanged, offset moved), update the
//     stick flag based on whether they're at the bottom now. Scrolling away from
//     the bottom turns sticking off; scrolling back turns it on.
//
// This lets us auto-follow a streaming reply without yanking the user back down
// while they're reading older history.
public sealed class ScrollHelper
{
    private const double BottomTolerance = 50;

    private readonly ScrollViewer _scroller;
    private bool _stick = true;
    private double _prevExtent;

    private ScrollHelper(ScrollViewer scroller)
    {
        _scroller = scroller;
        _prevExtent = scroller.Extent.Height;
        scroller.ScrollChanged += OnScrollChanged;
    }

    public static ScrollHelper? AttachIfReady(ScrollViewer? scroller) =>
        scroller is null ? null : new ScrollHelper(scroller);

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var extent = _scroller.Extent.Height;
        var offset = _scroller.Offset.Y;
        var viewport = _scroller.Viewport.Height;
        var distanceFromBottom = extent - offset - viewport;
        var atBottom = distanceFromBottom <= BottomTolerance;
        var extentGrew = extent > _prevExtent + 0.5;

        if (extentGrew)
        {
            // Content arrived (new message OR live-streamed text deltas). Follow it
            // only if the user was sticking — set in the previous iteration.
            if (_stick) _scroller.ScrollToEnd();
        }
        else
        {
            // Pure user scroll (offset/viewport changed without content growth).
            // Update the stick flag to whatever the user is doing.
            _stick = atBottom;
        }

        _prevExtent = extent;
    }
}
