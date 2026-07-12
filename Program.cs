using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using B4XMcpServer.Tools;

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
    .WithTools<DiagnosticsTools>()
    .WithTools<ProjectTools>();

await builder.Build().RunAsync();