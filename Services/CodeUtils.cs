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
            var raw = File.ReadAllBytes(path);
            // Strip BOM if present
            if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
            {
                raw = raw.Skip(3).ToArray();
            }

            string? text = null;
            foreach (var enc in EncodingsToTry)
            {
                try
                {
                    // See ReadTextSafely's historical note: ExceptionFallback is required or
                    // this cascade never actually falls through past UTF-8.
                    var encoding = Encoding.GetEncoding(enc, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                    text = encoding.GetString(raw);
                    break;
                }
                catch (Exception ex) when (ex is DecoderFallbackException or NotSupportedException or ArgumentException)
                {
                    // DecoderFallbackException: bytes aren't valid in this encoding — try the next one.
                    // NotSupportedException: encoding unavailable (shouldn't happen now that the
                    //   CodePagesEncodingProvider is registered, but fail safe rather than crash
                    //   if it's ever missing on some runtime).
                    // ArgumentException: malformed/unknown encoding name — same fail-safe reasoning.
                }
            }

            return text ?? Encoding.Latin1.GetString(raw);
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