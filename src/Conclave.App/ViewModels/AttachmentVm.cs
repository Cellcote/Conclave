using Conclave.App.Design;

namespace Conclave.App.ViewModels;

public sealed class AttachmentVm : Views.Observable
{
    public Tokens Tokens { get; }
    public string Path { get; }
    public string FileName { get; }

    public AttachmentVm(Tokens tokens, string path)
    {
        Tokens = tokens;
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
    }
}
