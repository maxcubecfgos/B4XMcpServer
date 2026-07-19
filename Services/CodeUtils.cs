using System;
using System.IO;
using System.Linq;
using System.Text;

namespace B4XMcpServer.Services
{
    public static class CodeUtils
    {
        // .NET Core/5+ only ships Unicode + ASCII + Latin1 encodings out of the box.
        // Legacy code pages like windows-1252 throw NotSupportedException unless this
        // provider is registered — without it, ReadTextSafely/DecodeFileWithFallback
        // would crash instead of falling back, for exactly the files this cascade exists
        // to handle. Static constructor guarantees this runs once before first use,
        // regardless of call order.
        static CodeUtils()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        // Note: "utf-8-sig" is not a valid .NET encoding name (that's Python's codec naming);
        // we handle the BOM manually above, so a plain "utf-8" attempt is enough.
        // Both remaining fallbacks (windows-1252, iso-8859-1) are single-byte codepages that
        // can represent any byte value, so in practice this resolves to: strict UTF-8 if valid,
        // otherwise windows-1252 (the common legacy encoding for B4X files saved on Spanish/
        // Windows locales) — iso-8859-1 is kept as a final belt-and-suspenders catch-all.
        private static readonly string[] EncodingsToTry = new[] { "utf-8", "windows-1252", "iso-8859-1" };
        private const string DESIGN_TEXT_MARKER = "@EndOfDesignText@";

        public static string ReadTextSafely(string path)
        {
            var text = DecodeFileWithFallback(path);

            int idx = text.IndexOf(DESIGN_TEXT_MARKER, StringComparison.Ordinal);
            if (idx >= 0)
            {
                text = text.Substring(idx + DESIGN_TEXT_MARKER.Length);
                // strip leading newlines
                text = text.TrimStart('\r', '\n');
            }

            return text;
        }

        /// <summary>
        /// Reads a file and decodes it with the same encoding-detection cascade as
        /// ReadTextSafely, but WITHOUT stripping the IDE metadata header. Use this when
        /// the header needs to be preserved and reassembled later (e.g. EditSub) — calling
        /// ReadTextSafely there would silently discard the header from the final output.
        /// </summary>
        public static string DecodeFileWithFallback(string path)
        {
            return DecodeFileWithFallback(path, out _);
        }

        /// <summary>
        /// Reads a file and decodes it with encoding detection, also returning the
        /// encoding that was used. This lets callers preserve the original encoding
        /// when writing back (critical for B4X files that use Windows-1252).
        /// </summary>
        public static string DecodeFileWithFallback(string path, out Encoding usedEncoding)
        {
            var raw = File.ReadAllBytes(path);
            // Strip BOM if present
            if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
            {
                raw = raw.Skip(3).ToArray();
            }

            string? text = null;
            Encoding? detected = null;
            foreach (var enc in EncodingsToTry)
            {
                try
                {
                    var encoding = Encoding.GetEncoding(enc, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                    text = encoding.GetString(raw);
                    detected = encoding;
                    break;
                }
                catch (Exception ex) when (ex is DecoderFallbackException or NotSupportedException or ArgumentException)
                {
                }
            }

            if (text == null)
            {
                detected = Encoding.Latin1;
                text = detected.GetString(raw);
            }

            usedEncoding = detected!;
            return text;
        }

        public static string ExtractSub(string source, int startLine, int endLine)
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;
            var lines = source.Split(new[] { '\n' });
            int n = lines.Length;
            int s = Math.Max(1, Math.Min(startLine, n)) - 1;
            int e = Math.Max(s, Math.Min(endLine, n)) - 1;
            return string.Join("\n", lines.Skip(s).Take(e - s + 1));
        }

        // Extract sub given that source may be only the selection that contains the sub.
        // This attempts to extract the lines relative to the provided source text.
        public static string ExtractSubBySourceText(string sourceText, int startLine, int endLine)
        {
            return ExtractSub(sourceText, startLine, endLine);
        }
    }
}