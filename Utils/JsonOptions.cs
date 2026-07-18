using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace B4XMcpServer.Utils
{
    /// <summary>
    /// Shared, cached <see cref="JsonSerializerOptions"/> instance.
    ///
    /// Constructing <c>JsonSerializerOptions</c> on every tool call is wasteful:
    /// each instance allocates a new reflection cache, a new type-resolver pipeline,
    /// and forces <c>System.Text.Json</c> to redo contract metadata for every
    /// anonymous / dynamic shape we serialize. Reusing a single instance avoids
    /// the allocation churn on the hot path of every MCP tool response.
    ///
    /// The <see cref="DefaultJsonTypeInfoResolver"/> is required when serializing
    /// a <c>JsonNode</c> tree (e.g. the output of <c>LayoutParser</c>) via
    /// <c>JsonNode.ToJsonString(options)</c> — without it, .NET 8 throws
    /// <c>InvalidOperationException: JsonSerializerOptions instance must specify
    /// a TypeInfoResolver</c>. Anonymous-object serialization (which is what
    /// every tool in this project uses for response shaping) does not strictly
    /// need it, but adding it here costs nothing and keeps one config for both
    /// paths so callers don't have to think about it.
    /// </summary>
    public static class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };

        /// <summary>
        /// Compact variant for very large responses (e.g. <c>get_full_context</c>)
        /// where indentation would balloon token counts without adding signal.
        /// </summary>
        public static readonly JsonSerializerOptions Compact = new()
        {
            WriteIndented = false,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
    }
}