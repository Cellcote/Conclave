using System.Collections.ObjectModel;

namespace Conclave.App.ViewModels;

public enum FileChangeKind { Added, Modified, Deleted }

public sealed class FileChangeVm
{
    public FileChangeKind Kind { get; init; }
    public string Path { get; init; } = "";
    public int Add { get; init; }
    public int Del { get; init; }

    public string KindLetter => Kind switch
    {
        FileChangeKind.Added => "A",
        FileChangeKind.Modified => "M",
        FileChangeKind.Deleted => "D",
        _ => "?",
    };
    public string AddDel => Add > 0 && Del > 0 ? $"+{Add} −{Del}"
                          : Add > 0 ? $"+{Add}"
                          : Del > 0 ? $"−{Del}"
                          : "";
}

public sealed class DiffStatVm : Views.Observable
{
    private int _files, _add, _del;
    public int Files { get => _files; set { if (Set(ref _files, value)) { Notify(nameof(HasChanges)); Notify(nameof(Summary)); } } }
    public int Add { get => _add; set { if (Set(ref _add, value)) Notify(nameof(Summary)); } }
    public int Del { get => _del; set { if (Set(ref _del, value)) Notify(nameof(Summary)); } }

    public ObservableCollection<FileChangeVm> Changes { get; } = new();

    public bool HasChanges => _files > 0;
    public string Summary => $"+{_add} / −{_del}";
}
