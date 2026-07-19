using System;
using B4XMcpServer.Tools;

namespace B4XMcpServer.Services
{
    /// <summary>
    /// Canonical list of [<see cref="ModelContextProtocol.Server.McpServerToolTypeAttribute"/>]
    /// classes exposed by this server. Used by:
    /// <list type="bullet">
    ///   <item><description>Program.cs — as the registration list passed to the MCP host's
    ///   <c>WithTools&lt;T&gt;()</c> chain (kept in sync by hand today; candidate for
    ///   reflection-based registration in a future change).</description></item>
    ///   <item><description>B4xProjectInstaller — as the type list the AGENTS.md generator
    ///   enumerates via reflection to render the tool inventory.</description></item>
    /// </list>
    /// Keeping a single registry prevents the AGENTS.md from advertising tools the MCP
    /// server does not actually wire up — and prevents the MCP server from silently
    /// exposing tools the AGENTS.md does not document. If you add a new tool class to
    /// <see cref="B4XMcpServer.Tools"/>, add it here as well.
    /// </summary>
    public static class SupportedTools
    {
        public static readonly Type[] AllTypes =
        {
            typeof(LanguageTools),
            typeof(DiagnosticsTools),
            // ProjectTools has been refactored into granular sub-tools under Tools/Project/
            typeof(B4XMcpServer.Tools.Project.ProjectReadTools),
            typeof(B4XMcpServer.Tools.Project.FileWriteTools),
            typeof(B4XMcpServer.Tools.Project.CompileTools),
            typeof(B4XMcpServer.Tools.Project.LayoutViewTools),
            typeof(B4XMcpServer.Tools.Project.ContextTools),
            typeof(B4XMcpServer.Tools.Project.CodeEditTools),
            typeof(B4XMcpServer.Tools.Project.AnalyzeTools),
            typeof(B4XMcpServer.Tools.Project.ManifestTools),
            typeof(B4XMcpServer.Tools.Project.ValidationTools),
            typeof(B4XMcpServer.Tools.Layout.LayoutFileTools),
            typeof(B4XMcpServer.Tools.Layout.LayoutControlTools),
            typeof(B4XMcpServer.Tools.Layout.LayoutCodeTools),
            typeof(B4XMcpServer.Tools.Layout.LayoutProjectTools),
            typeof(DeviceTools),
            typeof(GitTools),
            typeof(LibraryTools),
            typeof(WorkflowTools),
            typeof(RuntimeTools),
            typeof(ConfigTools),
        };
    }
}
