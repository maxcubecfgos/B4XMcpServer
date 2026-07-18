using System.Collections.Generic;
using System.Linq;

namespace B4XMcpServer.Engine
{
    public static class ManifestLanguageProvider
    {
        private static readonly List<ManifestKeyword> Keywords = new()
        {
            new("AddPermission", "Adds a permission to the manifest.\nSyntax: AddPermission(permissionName As String)"),
            new("RemovePermission", "Removes a previously added permission.\nSyntax: RemovePermission(permissionName As String)"),
            new("SetApplicationAttribute", "Sets an attribute in the <application> tag.\nSyntax: SetApplicationAttribute(attributeName As String, value As String)"),
            new("SetActivityAttribute", "Sets an attribute in the <activity> tag.\nSyntax: SetActivityAttribute(attributeName As String, value As String)"),
            new("SetReceiverAttribute", "Sets an attribute in the <receiver> tag.\nSyntax: SetReceiverAttribute(attributeName As String, value As String)"),
            new("SetServiceAttribute", "Sets an attribute in the <service> tag.\nSyntax: SetServiceAttribute(attributeName As String, value As String)"),
            new("SetForegroundServiceAttribute", "Sets an attribute for a foreground service.\nSyntax: SetForegroundServiceAttribute(attributeName As String, value As String)"),
            new("RemoveAttribute", "Removes an attribute from the manifest.\nSyntax: RemoveAttribute(tag As String, attributeName As String)"),
            new("AddManifestText", "Adds raw XML text to the manifest.\nSyntax: AddManifestText(text As String)"),
            new("AddApplicationText", "Adds raw XML text inside the <application> tag.\nSyntax: AddApplicationText(text As String)"),
            new("CreateResourceFromFile", "Creates a resource entry from a file.\nSyntax: CreateResourceFromFile(Macro, FileName)"),
            new("GetLogger", "Returns the Logger instance.\nSyntax: GetLogger As Logger"),
            new("GetApplication", "Returns the Application instance.\nSyntax: GetApplication As Application"),
            new("GetActivity", "Returns the current Activity instance.\nSyntax: GetActivity As Activity"),
            new("GetReceiver", "Returns the BroadcastReceiver instance.\nSyntax: GetReceiver As BroadcastReceiver"),
            new("GetService", "Returns the Service instance.\nSyntax: GetService As Service"),
            new("Gravity", "Sets gravity for layout/graphical elements.\nSyntax: Gravity(gravityValue As Int)"),
        };

        public static List<CompletionItem> GetKeywordCompletions(string prefix)
        {
            return Keywords
                .Where(k => k.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(k => new CompletionItem(k.Name, CompletionItemKind.Keyword, k.Name, k.Documentation))
                .ToList();
        }

        public static string? GetHover(string word)
        {
            var kw = Keywords.FirstOrDefault(k => k.Name.Equals(word, StringComparison.OrdinalIgnoreCase));
            return kw?.Documentation;
        }

        public static List<string> GetAllKeywordNames() => Keywords.Select(k => k.Name).ToList();

        private record ManifestKeyword(string Name, string Documentation);
    }
}
