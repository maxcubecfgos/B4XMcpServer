using ModelContextProtocol.Server;
using System.ComponentModel;

namespace B4XMcpServer.Tools
{
    [McpServerToolType]
    public sealed class DiagnosticsTools
    {
        [McpServerTool, Description("Sanity-check tool. Confirms the B4X MCP server is running and can be called by the AI client.")]
        public static string Ping()
        {
            return "B4X MCP Server is alive and responding.";
        }
    }
}