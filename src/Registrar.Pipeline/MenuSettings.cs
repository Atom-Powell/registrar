using System.Text.Json.Serialization;

namespace Registrar.Pipeline;

public sealed record MenuSettings
{
    public const int MaxRecent = 8;

    [JsonPropertyName("lastFolder")] public string? LastFolder { get; init; }
    [JsonPropertyName("recentAgrs")] public IReadOnlyList<string> RecentAgrs { get; init; } = Array.Empty<string>();

    [JsonPropertyName("registryPath")] public string? RegistryPath { get; init; }

    public MenuSettings WithRegistry(string? path) => this with { RegistryPath = path };

    public MenuSettings WithFolder(string folder) => this with { LastFolder = folder };

    public MenuSettings WithRecent(string agrPath)
    {
        var deduped = new List<string> { agrPath };
        deduped.AddRange(RecentAgrs.Where(p => !string.Equals(p, agrPath, StringComparison.OrdinalIgnoreCase)));
        return this with { RecentAgrs = deduped.Take(MaxRecent).ToList() };
    }
}
