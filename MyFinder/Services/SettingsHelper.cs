using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace MyFinder.Services;

/// <summary>
/// Cross-platform settings abstraction that handles both packaged and unpackaged WPF applications.
/// Falls back to local JSON file when ApplicationData.Current is unavailable.
/// </summary>
public static class SettingsHelper
{
    private static readonly object _lock = new object();
    private static Dictionary<string, object>? _settings;
    private static readonly string _settingsFilePath = Path.Combine(
        AppContext.BaseDirectory, 
        "app_settings.json"
    );
    private static bool _isPackaged;
    private static bool _isInitialized;

    static SettingsHelper()
    {
        Initialize();
    }

    private static void Initialize()
    {
        if (_isInitialized) return;

        lock (_lock)
        {
            if (_isInitialized) return;

            try
            {
                // Try to access ApplicationData.Current (packaged mode)
                var _ = Windows.Storage.ApplicationData.Current.LocalSettings;
                _isPackaged = true;
            }
            catch
            {
                // Unpackaged mode - use JSON file
                _isPackaged = false;
                LoadSettingsFromFile();
            }

            _isInitialized = true;
        }
    }

    private static void LoadSettingsFromFile()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                _settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json) 
                    ?? new Dictionary<string, object>();
            }
            else
            {
                _settings = new Dictionary<string, object>();
            }
        }
        catch
        {
            _settings = new Dictionary<string, object>();
        }
    }

    private static void SaveSettingsToFile()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsHelper] Failed to save settings: {ex.Message}");
        }
    }

    public static T Get<T>(string key, T defaultValue)
    {
        lock (_lock)
        {
            try
            {
                if (_isPackaged)
                {
                    var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                    if (localSettings.Values.ContainsKey(key))
                    {
                        var value = localSettings.Values[key];
                        if (value is T typedValue)
                            return typedValue;
                        
                        // Handle type conversion (especially for numeric types stored as objects)
                        try
                        {
                            return (T)Convert.ChangeType(value, typeof(T));
                        }
                        catch
                        {
                            return defaultValue;
                        }
                    }
                }
                else
                {
                    if (_settings != null && _settings.ContainsKey(key))
                    {
                        var value = _settings[key];
                        
                        // Handle JsonElement deserialization
                        if (value is JsonElement jsonElement)
                        {
                            return JsonSerializer.Deserialize<T>(jsonElement.GetRawText()) ?? defaultValue;
                        }
                        
                        if (value is T typedValue)
                            return typedValue;
                        
                        try
                        {
                            return (T)Convert.ChangeType(value, typeof(T));
                        }
                        catch
                        {
                            return defaultValue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsHelper] Get error for key '{key}': {ex.Message}");
            }

            return defaultValue;
        }
    }

    public static void Set<T>(string key, T value)
    {
        lock (_lock)
        {
            try
            {
                if (_isPackaged)
                {
                    var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                    localSettings.Values[key] = value;
                }
                else
                {
                    if (_settings == null)
                        _settings = new Dictionary<string, object>();
                    
                    _settings[key] = value!;
                    SaveSettingsToFile();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsHelper] Set error for key '{key}': {ex.Message}");
            }
        }
    }

    public static void Remove(string key)
    {
        lock (_lock)
        {
            try
            {
                if (_isPackaged)
                {
                    var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                    localSettings.Values.Remove(key);
                }
                else
                {
                    if (_settings != null && _settings.ContainsKey(key))
                    {
                        _settings.Remove(key);
                        SaveSettingsToFile();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsHelper] Remove error for key '{key}': {ex.Message}");
            }
        }
    }
}
