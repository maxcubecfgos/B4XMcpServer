using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using B4XMcpServer.Repositories;
using B4XMcpServer.Services;
using B4XMcpServer.Tools;

// Force UTF-8 on stdout so JSON output preserves non-ASCII characters on
// Windows hosts that default to a legacy code page (cp1252 / cp850). Cheap
// and harmless on any other platform.
Console.OutputEncoding = Encoding.UTF8;

// Installer first: only when invoked with no args AND stdio not fully piped.
// Manual launch from a terminal or by double-clicking triggers it; MCP-aware
// clients (Claude Desktop, Cursor, ...) pipe both streams and pass no args,
// but the gate discriminates them via the pipe check.
if (args.Length == 0
    && (!Console.IsInputRedirected || !Console.IsOutputRedirected)
    && B4xProjectInstaller.TryRun() == B4xProjectInstaller.Outcome.Installed)
{
    return 0;
}

// CLI dispatcher: when any args are passed, route argv[0] to a built-in
// command (--help, --list-tools, --describe ...) or to a known tool. Returns
// the exit code directly and never falls through to the MCP host.
if (args.Length >= 1)
{
    return await CliDispatcher.TryRun(args);
}

// MCP host: Claude Desktop / Cursor / Cline / etc. pipe stdio and pass no args.
var builder = Host.CreateApplicationBuilder(args);

// El transporte MCP stdio usa stdout exclusivamente para el protocolo JSON-RPC.
// Cualquier log debe ir a stderr, o corrompe la comunicación con el cliente
// (Claude Code, etc.) — por eso el logger se configura explícitamente hacia stderr.
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Register repositories as singletons so tools can request them via DI.
builder.Services.AddSingleton<IFileRepository, FileRepository>();
builder.Services.AddSingleton<IProjectRepository, ProjectRepository>();

var mcpBuilder = builder.Services
    .AddMcpServer()
    .WithStdioServerTransport();

// Auto-register every tool class listed in SupportedTools.AllTypes.
// This keeps Program.cs in sync with the AGENTS.md generator and prevents
// a tool from being advertised but not wired up (or vice versa).
// The MCP SDK exposes multiple WithTools overloads; select the generic
// extension that takes IMcpServerBuilder and JsonSerializerOptions.
var withToolsMethod = typeof(McpServerBuilderExtensions)
    .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
    .Single(m =>
    {
        if (m.Name != "WithTools" || !m.IsGenericMethod || m.GetGenericArguments().Length != 1)
            return false;
        var parameters = m.GetParameters();
        return parameters.Length == 2
            && parameters[0].ParameterType.FullName == "Microsoft.Extensions.DependencyInjection.IMcpServerBuilder"
            && parameters[1].ParameterType.FullName == "System.Text.Json.JsonSerializerOptions";
    });

foreach (var toolType in SupportedTools.AllTypes)
{
    // Register the tool class itself in DI so constructor dependencies are resolved
    // when the MCP SDK instantiates it per-invocation.
    builder.Services.AddTransient(toolType);

    var generic = withToolsMethod.MakeGenericMethod(toolType);
    generic.Invoke(null, new object?[] { mcpBuilder, null });
}

var host = builder.Build();
await host.RunAsync();
return 0;