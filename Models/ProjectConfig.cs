namespace B4XMcpServer.Models
{
    /// <summary>
    /// Parsed B4X project file metadata.
    /// </summary>
    public sealed class ProjectConfig
    {
        public string ProjectFile { get; set; } = string.Empty;
        public string? AppType { get; set; }
        public string? Version { get; set; }
        public string? NumberOfModules { get; set; }
        public List<string> Libraries { get; set; } = new();
        public List<string> Modules { get; set; } = new();
        public List<string> IncludedFiles { get; set; } = new();
        public Dictionary<string, string> RawSettings { get; set; } = new();
    }
}
