using System.Collections.ObjectModel;
using Conclave.App.Design;

namespace Conclave.App.ViewModels;

public sealed class ProjectVm : Views.Observable
{
    public Tokens Tokens { get; }

    public string Id { get; init; } = "";
    public string Path { get; init; } = "";
    public string DefaultBranch { get; init; } = "main";

    private string _name = "";
    public string Name { get => _name; set => Set(ref _name, value); }

    private bool _isEditing;
    public bool IsEditing
    {
        get => _isEditing;
        set { if (Set(ref _isEditing, value) && value) EditingName = _name; }
    }

    private string _editingName = "";
    public string EditingName { get => _editingName; set => Set(ref _editingName, value); }

    public ObservableCollection<SessionVm> Sessions { get; } = new();

    public int SessionCount => Sessions.Count;

    // Driven by ShellVm.ApplyFilter — hides the whole project group when no child session matches.
    private bool _isVisibleInTree = true;
    public bool IsVisibleInTree { get => _isVisibleInTree; set => Set(ref _isVisibleInTree, value); }

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (Set(ref _isExpanded, value)) Notify(nameof(ChevronGlyph)); }
    }

    public string ChevronGlyph => _isExpanded ? "▾" : "▸";

    public ProjectVm(Tokens tokens)
    {
        Tokens = tokens;
        Sessions.CollectionChanged += (_, _) => Notify(nameof(SessionCount));
    }
}
