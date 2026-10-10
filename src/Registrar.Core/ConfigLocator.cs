namespace Registrar.Core;

public static class ConfigLocator
{
    // Walk up from baseDir until a directory holds both schemas/ and config/,
    // then return the absolute path to <subDir>/<fileName>.
    private static string Find(string baseDir, string subDir, string fileName)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(baseDir));
        while (dir is not null)
        {
            var schemas = Path.Combine(dir.FullName, "schemas");
            var config = Path.Combine(dir.FullName, "config");
            if (Directory.Exists(schemas) && Directory.Exists(config))
            {
                var candidate = Path.Combine(dir.FullName, subDir, fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"Could not locate {subDir}/{fileName} by walking up from {baseDir}.");
    }

    public static string FindSchema(string baseDir, string fileName)
        => Find(baseDir, "schemas", fileName);

    public static string FindConfig(string baseDir, string fileName)
        => Find(baseDir, "config", fileName);

    public static string FindAsset(string baseDir, string fileName)
        => Find(baseDir, "assets", fileName);
}
