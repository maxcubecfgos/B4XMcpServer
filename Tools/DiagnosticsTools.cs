using B4XMcpServer.Repositories;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace B4XMcpServer.Tools
{
    [McpServerToolType]
    public sealed class DiagnosticsTools
    {
        public DiagnosticsTools(IFileRepository fileRepository, IProjectRepository projectRepository)
        {
        }

        [McpServerTool, Description("Sanity-check tool. Confirms the B4X MCP server is running and can be called by the AI client.")]
        public string Ping()
        {
            return "B4X MCP Server is alive and responding.";
        }
    }
}