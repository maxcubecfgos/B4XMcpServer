using System.IO;

namespace B4XEngineCore
{
    public static class PlatformDetector
    {
        public static Platform DetectPlatform(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".bjl" => Platform.B4J,
                _ => Platform.B4A,
            };
        }
    }
}
