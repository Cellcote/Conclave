using Conclave.App.Design;

namespace Conclave.App.ViewModels;

public enum PlanItemStatus { Pending, InProgress, Completed }

// One row on the Plan tab. Sourced from claude's TodoWrite tool input:
//   { content, status: "pending"|"in_progress"|"completed", activeForm }
public sealed class PlanItemVm : Views.Observable
{
    public Tokens Tokens { get; init; } = null!;
    public string Content { get; init; } = "";
    public string ActiveForm { get; init; } = "";
    public PlanItemStatus Status { get; init; }

    public bool IsPending => Status == PlanItemStatus.Pending;
    public bool IsInProgress => Status == PlanItemStatus.InProgress;
    public bool IsCompleted => Status == PlanItemStatus.Completed;
}
