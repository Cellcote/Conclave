using System.Collections.ObjectModel;
using Conclave.App.Commands;
using Conclave.App.Design;

namespace Conclave.App.ViewModels;

public sealed class CommandPaletteVm : Views.Observable
{
    private readonly ShellVm _shell;

    public Tokens Tokens => _shell.Tokens;
    public ObservableCollection<CommandResultVm> Results { get; } = new();

    private string _query = "";
    public string Query
    {
        get => _query;
        set
        {
            if (Set(ref _query, value))
            {
                Recompute();
                Notify(nameof(HasQuery));
            }
        }
    }
    public bool HasQuery => !string.IsNullOrEmpty(_query);

    private int _selectedIndex;
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            // Clamp to current results — arrow keys past the ends shouldn't select something
            // that isn't there. Empty result set leaves the index at 0.
            int clamped = Results.Count == 0 ? 0 : Math.Clamp(value, 0, Results.Count - 1);
            Set(ref _selectedIndex, clamped);
        }
    }

    public CommandPaletteVm(ShellVm shell)
    {
        _shell = shell;
        Recompute();
    }

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0) return;
        int next = SelectedIndex + delta;
        // Wrap so down-at-bottom goes to top — palette UX expects this.
        if (next < 0) next = Results.Count - 1;
        else if (next >= Results.Count) next = 0;
        SelectedIndex = next;
    }

    public void ExecuteSelected()
    {
        if (_selectedIndex < 0 || _selectedIndex >= Results.Count) return;
        var result = Results[_selectedIndex];
        _shell.CloseCommandPalette();
        // Defer execution slightly: the palette is closing, and some actions (e.g.
        // OpenPreferences) immediately open another modal that wants focus. Letting
        // the close finish first avoids focus thrash on the same UI tick.
        Avalonia.Threading.Dispatcher.UIThread.Post(result.Execute);
    }

    private void Recompute()
    {
        Results.Clear();

        var pool = new List<CommandResultVm>();
        foreach (var cmd in _shell.Commands.All)
        {
            if (!cmd.CanExecute()) continue;
            var (score, _) = FuzzyMatch.Score(_query, cmd.Title);
            if (score == 0) continue;
            var shortcut = _shell.KeyMap.FindForCommand(cmd.Id)?.Display;
            pool.Add(new CommandResultVm(cmd.Title, cmd.Group, shortcut, cmd.Execute, score));
        }

        // Synthetic per-session "Switch to" entries — generated on the fly so we don't
        // have to invalidate the registry when sessions come and go.
        foreach (var project in _shell.Projects)
        {
            foreach (var session in project.Sessions)
            {
                if (ReferenceEquals(session, _shell.ActiveSession)) continue;
                var title = $"Switch to: {session.Title}";
                var (score, _) = FuzzyMatch.Score(_query, title);
                if (score == 0) continue;
                var sessionRef = session;
                pool.Add(new CommandResultVm(
                    title,
                    project.Name,
                    null,
                    () => _shell.ActiveSession = sessionRef,
                    score));
            }
        }

        pool.Sort((a, b) => b.Score.CompareTo(a.Score));
        foreach (var result in pool) Results.Add(result);

        SelectedIndex = 0;
    }
}
