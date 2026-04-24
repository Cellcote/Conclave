namespace Conclave.App.Claude;

// Headless end-to-end check: spawn claude with a trivial prompt and verify we see
// a system/init event, an assistant event with text, and a final result event.
// Invoked via `dotnet run -- --smoke-claude`.
internal static class SmokeClaude
{
    public static async Task<int> RunAsync()
    {
        var client = new ClaudeClient();
        var cwd = Environment.CurrentDirectory;

        bool sawInit = false, sawAssistantText = false, sawResult = false;
        bool resultErrored = false;
        string? claudeSid = null;

        try
        {
            await foreach (var ev in client.StreamAsync(
                cwd,
                "respond with just the word pineapple and nothing else",
                claudeSessionId: null,
                modelAlias: null))
            {
                Console.WriteLine($"  event: {ev.Type,-10}" +
                                  (ev is ResultEvent r ? $"  error={r.IsError}  result={Truncate(r.Result)}" : ""));
                switch (ev)
                {
                    case SystemInitEvent init:
                        sawInit = true;
                        claudeSid = init.SessionId;
                        break;
                    case AssistantEvent asst:
                        if (asst.Content.OfType<TextContent>().Any(t => !string.IsNullOrWhiteSpace(t.Text)))
                            sawAssistantText = true;
                        break;
                    case ResultEvent res:
                        sawResult = true;
                        resultErrored = res.IsError;
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"smoke-claude: FAIL — {ex.Message}");
            return 1;
        }

        Console.WriteLine($"  sawInit={sawInit} sawAssistantText={sawAssistantText} sawResult={sawResult} errored={resultErrored} sid={claudeSid}");
        if (!sawInit || !sawAssistantText || !sawResult || resultErrored)
        {
            Console.Error.WriteLine("smoke-claude: FAIL — missing expected events or result errored");
            return 1;
        }
        Console.WriteLine("smoke-claude: OK");
        return 0;
    }

    private static string Truncate(string? s, int n = 60)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace('\n', ' ');
        return s.Length <= n ? s : s[..n] + "…";
    }
}
