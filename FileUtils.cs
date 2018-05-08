using System.IO;

public static class FileUtils
{
    public static string CreateFileToRoot(string dir, string name)
    {
        var currentDir = Directory.GetCurrentDirectory();
        var outputDir = Path.Combine(currentDir, dir);
        Directory.CreateDirectory(outputDir);
        var targetFile = Path.Combine(outputDir, $"{name}");
        return targetFile;
    }
}