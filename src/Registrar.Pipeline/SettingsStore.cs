using System.Text.Json;

namespace Registrar.Pipeline;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsStore(string path) => _path = path;

    public static string DefaultPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "registrar", "settings.json");

    public MenuSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new MenuSettings();
            return JsonSerializer.Deserialize<MenuSettings>(File.ReadAllText(_path), Options)
                   ?? new MenuSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new MenuSettings();
        }
    }

    public void Save(MenuSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; never fail the operator's run over them.
        }
    }
}
