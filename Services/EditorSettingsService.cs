using System.Text.Json;

namespace TMapEditor.Services;

public sealed class EditorSettings
{
    public string? LastProjectPath { get; set; }
    public string? LastExportDirectory { get; set; }
    public double ResourcePreviewScale { get; set; } = 100;
}

public static class EditorSettingsService
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TMapEditor");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    public static EditorSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new EditorSettings();
            return JsonSerializer.Deserialize(File.ReadAllText(SettingsPath), TMapJsonContext.Default.EditorSettings)
                   ?? new EditorSettings();
        }
        catch
        {
            return new EditorSettings();
        }
    }

    public static void Save(EditorSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(settings, TMapJsonContext.Default.EditorSettings) + Environment.NewLine);
        }
        catch
        {
            // 编辑器设置保存失败不应影响地图编辑和退出流程。
        }
    }
}
