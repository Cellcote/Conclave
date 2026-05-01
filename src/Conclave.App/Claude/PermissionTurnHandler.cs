using Avalonia.Threading;
using Conclave.App.ViewModels;

namespace Conclave.App.Claude;

public enum PermissionDecision { Allow, Deny, Cancel }

// Per-turn permission router. ClaudeService creates one of these at the start of each
// turn, registers it with PermissionMcpServer to get a bearer token, and threads the
// token into the --mcp-config JSON. When claude calls our permission_prompt tool, the
// server invokes HandleAsync, which surfaces the matching ToolCallVm in PendingApproval
// state and awaits the user's click.
//
// Cleanup: the turn must call CancelAll() in its finally block so any pending TCS gets
// released and claude doesn't sit on a now-orphaned permission request.
public sealed class PermissionTurnHandler
{
    private readonly Dictionary<string, TaskCompletionSource<PermissionDecision>> _pending = new();
    private readonly Dictionary<string, ToolCallVm> _vmByToolUseId = new();
    private readonly Dictionary<string, string> _inputByToolUseId = new();
    private readonly object _lock = new();

    // Called by ClaudeService when it processes a tool_use event so the broker can flip
    // the matching VM to PendingApproval when claude asks. Idempotent.
    public void NoteToolUse(string toolUseId, ToolCallVm vm)
    {
        lock (_lock)
        {
            _vmByToolUseId[toolUseId] = vm;
            // If the MCP request beat the tool_use event into the dictionary, surface now.
            if (_pending.ContainsKey(toolUseId))
                FlipToPendingApproval(vm, toolUseId);
        }
    }

    // Called by the MCP server when claude asks for permission. Awaits the user's
    // decision and translates it into the JSON body PermissionMcpServer expects back.
    public async Task<string> HandleAsync(string toolName, string inputJson, string toolUseId, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<PermissionDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        ToolCallVm? vm;
        lock (_lock)
        {
            _pending[toolUseId] = tcs;
            _inputByToolUseId[toolUseId] = inputJson;
            _vmByToolUseId.TryGetValue(toolUseId, out vm);
        }

        if (vm is not null) FlipToPendingApproval(vm, toolUseId);

        using var reg = ct.Register(() => tcs.TrySetResult(PermissionDecision.Cancel));
        PermissionDecision decision;
        try { decision = await tcs.Task; }
        finally
        {
            lock (_lock)
            {
                _pending.Remove(toolUseId);
                _inputByToolUseId.Remove(toolUseId);
            }
        }

        // Reset VM status so the throbber can re-render while claude actually invokes
        // the tool. tool_result will set Ok/Fail when it comes back.
        if (vm is not null)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (decision == PermissionDecision.Allow) vm.Status = ToolStatus.Pending;
                else vm.Status = ToolStatus.Fail;
            });
        }

        return decision switch
        {
            PermissionDecision.Allow => PermissionMcpServer.BuildAllow(inputJson),
            PermissionDecision.Deny => PermissionMcpServer.BuildDeny("User declined this tool invocation."),
            _ => PermissionMcpServer.BuildDeny("Turn cancelled."),
        };
    }

    // Wired from ToolCallVm.Approve / Deny when the user clicks. Safe to call from any
    // thread.
    public void Resolve(string toolUseId, PermissionDecision d)
    {
        TaskCompletionSource<PermissionDecision>? tcs;
        lock (_lock) _pending.TryGetValue(toolUseId, out tcs);
        tcs?.TrySetResult(d);
    }

    // Called on turn teardown so any in-flight HTTP handler returns rather than waiting
    // forever. Maps to a "deny" reply with a "cancelled" message — claude treats that as
    // the user pulling out of the turn.
    public void CancelAll()
    {
        TaskCompletionSource<PermissionDecision>[] all;
        lock (_lock) all = _pending.Values.ToArray();
        foreach (var t in all) t.TrySetResult(PermissionDecision.Cancel);
    }

    private static void FlipToPendingApproval(ToolCallVm vm, string toolUseId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            vm.ToolUseId = toolUseId;
            vm.Status = ToolStatus.PendingApproval;
        });
    }
}
