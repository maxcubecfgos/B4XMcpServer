using ModelContextProtocol.Server;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using B4XContext.Engine;

namespace B4XMcpServer.Tools
{
    [McpServerToolType]
    public sealed class LayoutTools
    {
        [McpServerTool, Description("Writes a B4X layout file (.bal or .bil) from JSON. Accepts the JSON format produced by get_layout_structure (simplified tree). Synthesizes missing header info (ControlsHeaders, DesignerScript, Files) and creates a .bak backup before overwriting.")]
        public static string WriteLayout(
            [Description("Absolute path to the .bal or .bil layout file to write")] string layoutPath,
            [Description("Layout JSON (as returned by get_layout_structure). Must contain balVersion, variants, and layoutTree.")] string jsonContent)
        {
            try
            {
                if (!File.Exists(layoutPath))
                    return JsonSerializer.Serialize(new { success = false, error = $"Layout file not found: {layoutPath}" });

                var ext = Path.GetExtension(layoutPath).ToLowerInvariant();
                if (ext != ".bal" && ext != ".bil")
                    return JsonSerializer.Serialize(new { success = false, error = "File must have .bal or .bil extension" });

                JObject json;
                try
                {
                    json = JObject.Parse(jsonContent);
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { success = false, error = $"Invalid JSON: {ex.Message}" });
                }

                // Validate required fields
                if (json["balVersion"] == null)
                    return JsonSerializer.Serialize(new { success = false, error = "Missing 'balVersion' in JSON" });
                if (json["variants"] == null)
                    return JsonSerializer.Serialize(new { success = false, error = "Missing 'variants' in JSON" });
                if (json["layoutTree"] == null)
                    return JsonSerializer.Serialize(new { success = false, error = "Missing 'layoutTree' in JSON" });

                // Backup
                string backupPath = layoutPath + ".bak";
                File.Copy(layoutPath, backupPath, overwrite: true);

                // Encode
                bool toBil = ext == ".bil";
                byte[] data = BalEncoder.Encode(jsonContent);
                File.WriteAllBytes(layoutPath, data);

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    path = layoutPath,
                    backup = backupPath,
                    bytesWritten = data.Length
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }
    }
}