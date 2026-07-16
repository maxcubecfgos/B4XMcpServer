using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using B4XMcpServer.Services;
using B4XMcpServer.Tools;

// Installer first. When the exe is launched manually (stdio not fully piped —
// e.g. from PowerShell, cmd, or by double-clicking), B4xProjectInstaller may
// write or append AGENTS.md in the project directory and exit. MCP-aware
// clients (Claude Desktop, Cursor, etc.) pipe both stdio streams, so they
// skip this block and fall through to the MCP host unchanged. If the
// installer handled the call, we return early; otherwise we proceed.
if ((!Console.IsInputRedirected || !Console.IsOutputRedirected)
    && B4xProjectInstaller.TryRun() == B4xProjectInstaller.Outcome.Installed)
{
    return;
}

var builder = Host.CreateApplicationBuilder(args);

// El transporte MCP stdio usa stdout exclusivamente para el protocolo JSON-RPC.
// Cualquier log debe ir a stderr, o corrompe la comunicación con el cliente
// (Claude Code, etc.) — por eso el logger se configura explícitamente hacia stderr.
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<LanguageTools>()
    .WithTools<DiagnosticsTools>()
    .WithTools<ProjectTools>()
    .WithTools<LayoutTools>()
    .WithTools<DeviceTools>()
    .WithTools<GitTools>()
    .WithTools<LibraryTools>()
    .WithTools<WorkflowTools>()
    .WithTools<RuntimeTools>();

await builder.Build().RunAsync();