using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MyFinder.Models;

namespace MyFinder.Services;

public class MetadataStore
{
    private const string IndexFileName = "myfinder_index.json";
    private readonly string _storagePath;
    private Dictionary<string, MediaFile> _cache = new();

    public MetadataStore(string appDataPath, string uniqueId = "default")
    {
        // e.g. "myfinder_index_883999226.json"
        string fileName = $"myfinder_index_{uniqueId}.json";
        _storagePath = Path.Combine(appDataPath, fileName);
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(_storagePath)) return;

        try 
        {
            using var stream = File.OpenRead(_storagePath);
            var list = await JsonSerializer.DeserializeAsync<List<MediaFile>>(stream);
            if (list != null)
            {
                _cache = list.ToDictionary(k => k.FilePath, v => v);
            }
        }
        catch (JsonException)
        {
            // Handle corrupted DB or empty file
            _cache = new Dictionary<string, MediaFile>();
        }
    }

    public async Task SaveAsync()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        using var stream = File.Create(_storagePath);
        await JsonSerializer.SerializeAsync(stream, _cache.Values, options);
    }
    
    public void Upsert(MediaFile file)
    {
        _cache[file.FilePath] = file;
    }
    
    public void Clear()
    {
        _cache.Clear();
        // Optionally delete the file too if "Hard Reset" is desired, 
        // but clearing cache + SaveAsync is safer.
    }

    public int Count => _cache.Count;
    public IEnumerable<MediaFile> GetAll() => _cache.Values;
    public MediaFile? GetByPath(string path) => _cache.TryGetValue(path, out var file) ? file : null;

    public IEnumerable<MediaFile> GetRecentFiles(TimeSpan window)
    {
        var cutoff = DateTime.Now - window;
        return _cache.Values
            .Where(f => f.LastOpened >= cutoff)
            .OrderByDescending(f => f.LastOpened);
    }

    public void AddTag(string filePath, string tag)
    {
        if (_cache.TryGetValue(filePath, out var file))
        {
            if (!file.Tags.Contains(tag))
            {
                file.Tags.Add(tag);
            }
        }
    }

    public void RemoveTag(string filePath, string tag)
    {
        if (_cache.TryGetValue(filePath, out var file))
        {
            file.Tags.Remove(tag);
        }
    }
}
