using System.Text;

namespace B4XMcpServer.Repositories
{
    /// <summary>
    /// Abstraction over the file system for the B4X MCP server.
    /// All file reads/writes should go through this repository so that
    /// caching, locking, backup and encoding concerns live in one place.
    /// </summary>
    public interface IFileRepository
    {
        bool Exists(string path);

        /// <summary>
        /// Reads the editable text of a file. For B4X project/module files this
        /// strips the IDE metadata header so callers work on the source code only.
        /// </summary>
        string ReadText(string path);

        /// <summary>
        /// Reads the full raw text of a file, including any IDE metadata header,
        /// preserving the original encoding. Use this when the header must be kept
        /// intact (e.g. before reassembling a .b4a/.b4j/.b4i file after an edit).
        /// </summary>
        string ReadTextWithHeader(string path);

        string ReadText(string path, Encoding encoding);
        byte[] ReadBytes(string path);
        void WriteText(string path, string content);
        void WriteBytes(string path, byte[] content);
        void Copy(string sourcePath, string destinationPath, bool overwrite = true);
        void Delete(string path);
        string? BackupPath(string path);

        /// <summary>
        /// Returns the last write time of the file in UTC.
        /// </summary>
        DateTime GetLastWriteTimeUtc(string path);
    }
}
