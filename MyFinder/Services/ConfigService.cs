using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyFinder.Services;

public class ConfigService
{
    private const string SettingsFileName = "settings.json";
    private readonly string _storagePath;

    public bool SkipSystemFolders { get; set; } = true;
    public int RecencyDurationHours { get; set; } = 24;
    
    // Object Detection Settings
    public bool DetectPersons { get; set; } = true;
    public bool DetectVehicles { get; set; } = true;
    public bool DetectAnimals { get; set; } = true;
    public string WhisperModel { get; set; } = "Base";
    
    // Window Settings
    public double WindowWidth { get; set; } = 960;
    public double WindowHeight { get; set; } = 540;

    public ConfigService(string appDataPath)
    {
        _storagePath = Path.Combine(appDataPath, SettingsFileName);
    }

    // Required for deserialization
    public ConfigService() 
    {
        _storagePath = string.Empty; // Will be unused on deserialized instance, as we copy properties
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(_storagePath)) return;

        try
        {
            using var stream = File.OpenRead(_storagePath);
            var settings = await JsonSerializer.DeserializeAsync<ConfigService>(stream);
            if (settings != null)
            {
                SkipSystemFolders = settings.SkipSystemFolders;
                RecencyDurationHours = settings.RecencyDurationHours;
                
                DetectPersons = settings.DetectPersons;
                DetectVehicles = settings.DetectVehicles;
                DetectAnimals = settings.DetectAnimals;
                
                if (!string.IsNullOrEmpty(settings.WhisperModel))
                    WhisperModel = settings.WhisperModel;

                if (settings.WindowWidth > 0 && settings.WindowHeight > 0)
                {
                    WindowWidth = settings.WindowWidth;
                    WindowHeight = settings.WindowHeight;
                }
            }
        }
        catch (JsonException)
        {
            // Ignore corrupted settings, use defaults
        }
    }

    public async Task SaveAsync()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        using var stream = File.Create(_storagePath);
        await JsonSerializer.SerializeAsync(stream, this, options);
    }
}
