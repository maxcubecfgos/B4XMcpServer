using System.Collections.Concurrent;
using System.Text;
using B4XMcpServer.Services;
using B4XMcpServer.Utils;

namespace B4XMcpServer.Repositories
{
    /// <summary>
    /// Default file-system repository. Provides centralized, thread-safe file
    /// access with automatic backups and cache invalidation.
    /// </summary>
    public sealed class FileRepository : IFileRepository
    {
        // One lock per file path to avoid corrupting the same file with concurrent writes.
        private readonly ConcurrentDictionary<string, object> _locks = new(StringComparer.OrdinalIgnoreCase);

        public bool Exists(string path)
        {
            return File.Exists(path);
        }

        public string ReadText(string path)
        {
            return CodeUtils.ReadTextSafely(path);
        }

        public string ReadTextWithHeader(string path)
        {
            return CodeUtils.DecodeFileWithFallback(path);
        }

        public string ReadText(string path, Encoding encoding)
        {
            return File.ReadAllText(path, encoding);
        }

        public byte[] ReadBytes(string path)
        {
            return File.ReadAllBytes(path);
        }

        public void WriteText(string path, string content)
        {
            var lockObj = _locks.GetOrAdd(path, _ => new object());
            lock (lockObj)
            {
                // Preserve the original file encoding: B4X files are commonly Windows-1252,
                // and writing them as UTF-8 breaks the B4X IDE's ability to parse them.
                System.Text.Encoding encoding;
                if (File.Exists(path))
                {
                    CodeUtils.DecodeFileWithFallback(path, out encoding);
                }
                else
                {
                    encoding = System.Text.Encoding.UTF8;
                }
                File.WriteAllText(path, content, encoding);
                CacheManager.Invalidate(path);
            }
        }

        public void WriteBytes(string path, byte[] content)
        {
            var lockObj = _locks.GetOrAdd(path, _ => new object());
            lock (lockObj)
            {
                File.WriteAllBytes(path, content);
                CacheManager.Invalidate(path);
            }
        }

        public void Copy(string sourcePath, string destinationPath, bool overwrite = true)
        {
            File.Copy(sourcePath, destinationPath, overwrite);
        }

        public void Delete(string path)
        {
            File.Delete(path);
        }

        public string? BackupPath(string path)
        {
            if (!File.Exists(path))
                return null;

            var lockObj = _locks.GetOrAdd(path, _ => new object());
            lock (lockObj)
            {
                var backup = path + ".bak";
                File.Copy(path, backup, overwrite: true);
                return backup;
            }
        }

        public DateTime GetLastWriteTimeUtc(string path)
        {
            return File.GetLastWriteTimeUtc(path);
        }
    }
}
