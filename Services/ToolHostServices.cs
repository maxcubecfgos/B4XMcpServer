using B4XMcpServer.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace B4XMcpServer.Services
{
    /// <summary>
    /// Shared DI configuration for B4XMcpServer tooling.
    /// Both the MCP host (<c>Program.cs</c>) and the argv-driven CLI dispatcher
    /// (<c>CliDispatcher.cs</c>) call into this helper so that tool instances
    /// resolve identical repositories. This keeps behavior consistent between
    /// the two entry points and prevents the CLI path from silently diverging
    /// from the MCP path (which was the cause of the
    /// "TargetException: Non-static method requires a target" crash — the
    /// <c>CliDispatcher</c> was invoking instance methods with a <c>null</c>
    /// target because it skipped the DI graph the host uses).
    /// </summary>
    internal static class ToolHostServices
    {
        /// <summary>
        /// Registers the two core repositories every tool class depends on
        /// (<see cref="IFileRepository"/> and <see cref="IProjectRepository"/>).
        /// Tools themselves are registered by the caller — as Transients in the
        /// MCP host, and resolved on demand via <see cref="ActivatorUtilities"/>
        /// in the CLI dispatcher.
        /// </summary>
        /// <returns>The same <paramref name="services"/> collection, for chaining.</returns>
        public static IServiceCollection RegisterCoreRepositories(IServiceCollection services)
        {
            services.AddSingleton<IFileRepository, FileRepository>();
            services.AddSingleton<IProjectRepository, ProjectRepository>();
            // Knowledge base that lazy-loads the bundled B4X reference on
            // first query and exposes it to MCP tools (see B4xKnowledgeBase).
            services.AddSingleton<B4xKnowledgeBase>();
            return services;
        }
    }
}
