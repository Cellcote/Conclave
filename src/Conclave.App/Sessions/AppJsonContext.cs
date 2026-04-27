using System.Text.Json.Serialization;

namespace Conclave.App.Sessions;

// Persisted tool-call row format (one entry per pill in the messages.tools_json blob).
// Top-level record so System.Text.Json's source generator can reach it.
internal sealed record ToolCallRow(string Kind, string Target, string Meta, string Status);

// Source-generated System.Text.Json context. The CLI generates a `ToolCallRowArray`
// property we use to (de)serialize without runtime reflection — required for NativeAOT.
[JsonSerializable(typeof(ToolCallRow[]))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
