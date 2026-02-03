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

    public ConfigService(string appDataPath)
    {
        _storagePath = Path.Combine(appDataPath, SettingsFileName);
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
