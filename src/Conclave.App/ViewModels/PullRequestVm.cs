namespace Conclave.App.ViewModels;

public enum PrState { Draft, Open, Merged, Closed }

public sealed class PullRequestVm
{
    public int Number { get; init; }
    public PrState State { get; init; }
    public string Branch { get; init; } = "";
    public string Base { get; init; } = "";
    public string MetaTail { get; init; } = "";    // "3 commits · ready to push"
}
