using B4XMcpServer.Models;

namespace B4XMcpServer.Repositories
{
    /// <summary>
    /// Abstraction over B4X project structure discovery and metadata access.
    /// </summary>
    public interface IProjectRepository
    {
        string? FindProjectRoot(string? startPath);
        string? FindProjectFile(string? projectRoot);
        List<ProjectFile> ScanProject(string? projectRoot);
        ProjectConfig GetConfig(string projectFile);
    }
}
