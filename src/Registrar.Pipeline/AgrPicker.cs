namespace Registrar.Pipeline;

public static class AgrPicker
{
    public static IReadOnlyList<string> Candidates(string workingFolder, IReadOnlyList<string> recent, bool excludeOutputs = false)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in recent)
        {
            if (!(excludeOutputs && OutputPackage.IsPackageFileName(path)) && File.Exists(path) && seen.Add(Path.GetFullPath(path)))
                result.Add(path);
        }

        if (Directory.Exists(workingFolder))
        {
            foreach (var path in Directory.EnumerateFiles(workingFolder, "*.json").OrderBy(p => p))
            {
                if (!(excludeOutputs && OutputPackage.IsPackageFileName(path)) && seen.Add(Path.GetFullPath(path)))
                    result.Add(path);
            }
        }
        return result;
    }
}
