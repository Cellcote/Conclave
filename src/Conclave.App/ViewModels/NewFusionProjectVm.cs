using System.Collections.ObjectModel;
using Conclave.App.Design;

namespace Conclave.App.ViewModels;

// State for the "create fusion project" modal. Composes a fusion from existing repo-kind
// projects: pick one as primary (its worktree is cwd for sessions) and ≥1 as secondaries
// (passed to claude via --add-dir).
public sealed class NewFusionProjectVm : Views.Observable
{
    public Tokens Tokens { get; }

    // All repo-kind projects, eligible to be primary. Filtered upstream so fusion projects
    // can't be nested.
    public ObservableCollection<ProjectVm> RepoProjects { get; }

    // Per-repo selection state, exposed to the modal as a checked list. The Primary radio
    // and Secondary checkboxes both bind here; flipping IsPrimary on one row clears it on
    // the others (the modal calls SetPrimary).
    public ObservableCollection<FusionMemberPickVm> Picks { get; } = new();

    public NewFusionProjectVm(Tokens tokens, ObservableCollection<ProjectVm> projects)
    {
        Tokens = tokens;
        RepoProjects = projects;
        foreach (var p in projects)
        {
            if (p.IsFusion) continue;
            Picks.Add(new FusionMemberPickVm(tokens, p));
        }
        if (Picks.Count > 0) Picks[0].IsPrimary = true;
    }

    private string _name = "";
    public string Name
    {
        get => _name;
        set { if (Set(ref _name, value)) Notify(nameof(CanCreate)); }
    }

    public ProjectVm? Primary => Picks.FirstOrDefault(p => p.IsPrimary)?.Project;

    public IReadOnlyList<ProjectVm> Secondaries =>
        Picks.Where(p => p.IsSecondary && !p.IsPrimary).Select(p => p.Project).ToList();

    public bool CanCreate =>
        !string.IsNullOrWhiteSpace(_name)
        && Primary is not null
        && Secondaries.Count >= 1;

    // Flip primary to a single row. Also auto-deselects the secondary checkbox on the new
    // primary so the two booleans don't visibly overlap on one row.
    public void SetPrimary(FusionMemberPickVm pick)
    {
        foreach (var p in Picks)
        {
            if (ReferenceEquals(p, pick))
            {
                p.IsPrimary = true;
                p.IsSecondary = false;
            }
            else
            {
                p.IsPrimary = false;
            }
        }
        Notify(nameof(CanCreate));
    }

    public void NotifyPickChanged() => Notify(nameof(CanCreate));

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set { if (Set(ref _errorMessage, value)) Notify(nameof(HasError)); }
    }
    public bool HasError => !string.IsNullOrEmpty(_errorMessage);
}

public sealed class FusionMemberPickVm : Views.Observable
{
    public Tokens Tokens { get; }
    public ProjectVm Project { get; }

    public FusionMemberPickVm(Tokens tokens, ProjectVm project)
    {
        Tokens = tokens;
        Project = project;
    }

    public string Name => Project.Name;
    public string Path => Project.Path;

    private bool _isPrimary;
    public bool IsPrimary { get => _isPrimary; set => Set(ref _isPrimary, value); }

    private bool _isSecondary;
    public bool IsSecondary { get => _isSecondary; set => Set(ref _isSecondary, value); }
}
