using System.Text.Json;
using System.Text.Json.Serialization;

namespace B4XMcpServer.Utils
{
    /// <summary>
    /// Standard tool-response envelope for every MCP tool in this server.
    ///
    /// Every tool returns a JSON object with this exact shape so the AI client
    /// (Claude Code, etc.) can pattern-match on `success` without having to
    /// string-sniff for emojis or exception text:
    ///
    ///   Success:
    ///     { "success": true, "data": { ...tool-specific payload... },
    ///       "error": null, "hints": [], "nextSteps": [] }
    ///
    ///   Soft (tool-domain) error:
    ///     { "success": false, "data": null,
    ///       "error": "Library 'jrandom' not found in any configured directory.",
    ///       "hints": [ "Run list_available_libraries to see exact names." ],
    ///       "nextSteps": [] }
    ///
    /// Hard system errors (FileNotFoundException, UnauthorizedAccessException,
    /// etc.) are NOT wrapped — they're allowed to bubble up so the MCP transport
    /// can surface them as JSON-RPC errors with stack traces. Only "soft" errors
    /// that are part of the tool's normal contract (validation, not-found in
    /// expected contexts) use the envelope.
    ///
    /// `hints` and `nextSteps` always serialize as arrays (never null) so the
    /// AI can iterate them without null checks.
    /// </summary>
    public static class ToolResponse
    {
        // Always emit `hints` and `nextSteps` as arrays even when empty so the
        // JSON shape is stable. DefaultIgnoreCondition.WhenWritingNull would
        // also work but it would also drop `error: null` on success, which makes
        // the success/failure asymmetry less obvious.
        private static readonly JsonSerializerOptions EnvelopeJson = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            TypeInfoResolver = JsonOptions.Default.TypeInfoResolver
        };

        /// <summary>
        /// Builds a success envelope. `data` is the tool-specific payload and
        /// may be null (e.g. for "no payload, just confirm it worked" replies).
        /// </summary>
        public static string Success(object? data = null, string[]? hints = null, string[]? nextSteps = null)
        {
            return JsonSerializer.Serialize(new Envelope
            {
                Success = true,
                Data = data,
                Error = null,
                Hints = hints ?? Array.Empty<string>(),
                NextSteps = nextSteps ?? Array.Empty<string>()
            }, EnvelopeJson);
        }

        /// <summary>
        /// Builds a soft-error envelope (success=false). `error` is mandatory;
        /// `hints`/`nextSteps` are optional but always serialize as arrays.
        /// </summary>
        public static string Error(string error, string[]? hints = null, string[]? nextSteps = null)
        {
            return JsonSerializer.Serialize(new Envelope
            {
                Success = false,
                Data = null,
                Error = error,
                Hints = hints ?? Array.Empty<string>(),
                NextSteps = nextSteps ?? Array.Empty<string>()
            }, EnvelopeJson);
        }

        /// <summary>
        /// Soft-error overload that ALSO attaches a payload (e.g. structured
        /// build errors that the AI should read and act on). Lets error paths
        /// pass context-rich data without resorting to string surgery on the
        /// already-serialized JSON envelope.
        /// </summary>
        public static string Error(string error, object? data, string[]? hints = null, string[]? nextSteps = null)
        {
            return JsonSerializer.Serialize(new Envelope
            {
                Success = false,
                Data = data,
                Error = error,
                Hints = hints ?? Array.Empty<string>(),
                NextSteps = nextSteps ?? Array.Empty<string>()
            }, EnvelopeJson);
        }

        private sealed class Envelope
        {
            public bool Success { get; set; }
            public object? Data { get; set; }
            public string? Error { get; set; }
            public string[] Hints { get; set; } = Array.Empty<string>();
            public string[] NextSteps { get; set; } = Array.Empty<string>();
        }
    }
}