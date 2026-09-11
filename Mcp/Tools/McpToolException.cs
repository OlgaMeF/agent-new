namespace MB.ComTools.Apps.Setup.Mcp.Tools;

/// <summary>Machine-readable failure raised by an MCP tool.</summary>
public sealed class McpToolException : Exception
{
    public McpToolException(string code, string message)
        : base($"[{code}] {message}")
    {
        Code = code;
    }

    public string Code { get; }
}
