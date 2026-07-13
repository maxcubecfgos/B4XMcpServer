using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace B4XContext.Utils
{
    public static class CacheManager
    {
        private static readonly Dictionary<string, CacheEntry> _cache = new();
        private static readonly object _lock = new();

        private class CacheEntry
        {
            public object? Value { get; set; }
            public DateTime Mtime { get; set; }
            public DateTime Expiry { get; set; }
        }

        /// <summary>
        /// Gets a cached value if the file's last-write-time hasn't changed.
        /// </summary>
        public static bool TryGetByMtime<T>(string path, out T? result)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(path, out var entry))
                {
                    var currentMtime = File.GetLastWriteTimeUtc(path);
                    if (currentMtime == entry.Mtime)
                    {
                        result = (T?)entry.Value;
                        return true;
                    }
                    _cache.Remove(path);
                }
                result = default;
                return false;
            }
        }

        /// <summary>
        /// Stores a value keyed by file path, tied to the file's current mtime.
        /// </summary>
        public static void SetByMtime(string path, object value)
        {
            lock (_lock)
            {
                _cache[path] = new CacheEntry
                {
                    Value = value,
                    Mtime = File.GetLastWriteTimeUtc(path),
                    Expiry = DateTime.MaxValue
                };
            }
        }

        /// <summary>
        /// Gets a cached value by string key (not tied to a file), with TTL in seconds.
        /// </summary>
        public static bool TryGetByTtl<T>(string key, out T? result)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    if (DateTime.UtcNow <= entry.Expiry)
                    {
                        result = (T?)entry.Value;
                        return true;
                    }
                    _cache.Remove(key);
                }
                result = default;
                return false;
            }
        }

        /// <summary>
        /// Stores a value by string key with a TTL in seconds.
        /// </summary>
        public static void SetByTtl(string key, object value, int ttlSeconds)
        {
            lock (_lock)
            {
                _cache[key] = new CacheEntry
                {
                    Value = value,
                    Mtime = DateTime.MinValue,
                    Expiry = DateTime.UtcNow.AddSeconds(ttlSeconds)
                };
            }
        }

        /// <summary>
        /// Stores a value by string key with no expiry.
        /// </summary>
        public static void Store(string key, object value)
        {
            lock (_lock)
            {
                _cache[key] = new CacheEntry
                {
                    Value = value,
                    Mtime = DateTime.MinValue,
                    Expiry = DateTime.MaxValue
                };
            }
        }

        /// <summary>
        /// Gets a value by string key (no TTL, no file tie).
        /// </summary>
        public static bool TryGet<T>(string key, out T? result)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    result = (T?)entry.Value;
                    return true;
                }
                result = default;
                return false;
            }
        }

        /// <summary>
        /// Invalidates all cached entries whose key starts with a prefix.
        /// </summary>
        public static void InvalidateByPrefix(string prefix)
        {
            lock (_lock)
            {
                var keys = _cache.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var k in keys)
                    _cache.Remove(k);
            }
        }

        /// <summary>
        /// Invalidates a single cached entry by key or file path.
        /// </summary>
        public static void Invalidate(string key)
        {
            lock (_lock)
            {
                _cache.Remove(key);
            }
        }
    }
}