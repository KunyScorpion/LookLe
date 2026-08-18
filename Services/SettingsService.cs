using System.IO;
using System.Text.Json;
using LeeyesViewer.Models;

namespace LeeyesViewer.Services;

public class SettingsService
{
    private readonly string _settingsPath;

    public SettingsService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDir = Path.Combine(localAppData, "LeeyesViewer");
        if (!Directory.Exists(appDir))
        {
            Directory.CreateDirectory(appDir);
        }
        _settingsPath = Path.Combine(appDir, "settings.json");
    }

    public AppSettings LoadSettings()
    {
        if (File.Exists(_settingsPath))
        {
            try
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                // Fallback to default
            }
        }
        return new AppSettings();
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // Ignore write errors
        }
    }
}
