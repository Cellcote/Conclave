using System.Collections.ObjectModel;
using Conclave.App.Design;

namespace Conclave.App.ViewModels;

public sealed class NewSessionVm : Views.Observable
{
    private static readonly string[] ModelOptions = { "Haiku 4.5", "Sonnet 4.5", "Opus 4" };

    public Tokens Tokens { get; }
    public ObservableCollection<ProjectVm> Projects { get; }
    public IReadOnlyList<string> Models => ModelOptions;

    public NewSessionVm(Tokens tokens, ObservableCollection<ProjectVm> projects)
    {
        Tokens = tokens;
        Projects = projects;
        _project = projects.FirstOrDefault();
    }

    private ProjectVm? _project;
    public ProjectVm? Project
    {
        get => _project;
        set
        {
            if (Set(ref _project, value))
            {
                Notify(nameof(WorktreePath));
                Notify(nameof(CanCreate));
                Notify(nameof(BaseBranch));
            }
        }
    }

    public string BaseBranch => _project?.DefaultBranch ?? "main";

    private string _branch = "";
    public string Branch
    {
        get => _branch;
        set
        {
            if (Set(ref _branch, value))
            {
                Notify(nameof(WorktreePath));
                Notify(nameof(CanCreate));
            }
        }
    }

    private string _model = "Sonnet 4.5";
    public string Model
    {
        get => _model;
        set
        {
            if (Set(ref _model, value))
            {
                Notify(nameof(IsHaiku));
                Notify(nameof(IsSonnet));
                Notify(nameof(IsOpus));
            }
        }
    }

    public bool IsHaiku => _model == "Haiku 4.5";
    public bool IsSonnet => _model == "Sonnet 4.5";
    public bool IsOpus => _model == "Opus 4";

    private string _permissionMode = PermissionModes.Default;
    public string PermissionMode
    {
        get => _permissionMode;
        set
        {
            if (Set(ref _permissionMode, value))
            {
                Notify(nameof(IsPermDefault));
                Notify(nameof(IsPermAcceptEdits));
                Notify(nameof(IsPermBypass));
            }
        }
    }
    public bool IsPermDefault => _permissionMode == PermissionModes.Default;
    public bool IsPermAcceptEdits => _permissionMode == PermissionModes.AcceptEdits;
    public bool IsPermBypass => _permissionMode == PermissionModes.BypassPermissions;

    public string WorktreePath
    {
        get
        {
            if (_project is null || string.IsNullOrWhiteSpace(_branch)) return "—";
            var slug = DeriveSlug(_branch);
            if (_project.IsFusion)
                return $"{_project.MemberIds.Count} worktrees · {slug}";
            return $"worktrees/{_project.Id[..8]}/{slug}";
        }
    }

    public bool CanCreate => _project is not null && !string.IsNullOrWhiteSpace(_branch);

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set { if (Set(ref _errorMessage, value)) Notify(nameof(HasError)); }
    }
    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    private static string DeriveSlug(string branch)
    {
        var last = branch.LastIndexOf('/');
        var raw = last >= 0 ? branch[(last + 1)..] : branch;
        return new string(raw.Select(c => char.IsLetterOrDigit(c) || c == '-' ? char.ToLowerInvariant(c) : '-').ToArray());
    }
}
