using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MyFinder.Models;

namespace MyFinder.Services;

public class FileScanner
{
    private readonly MetadataStore _store;
    private readonly HashSet<string> _allowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".mp3", ".wav", ".flac", ".m4a"
    };

    public event Action<string>? OnFileFound;
    public event Action<int>? OnScanComplete;

    public FileScanner(MetadataStore store)
    {
        _store = store;
    }

    public bool SkipSystemFolders { get; set; } = true;

    public async Task ScanDriveAsync(string rootPath)
    {
        if (!Directory.Exists(rootPath)) return;

        await Task.Run(() =>
        {
            // Custom recursive walk to handle explicit folder skips
            ScanRecursive(rootPath);
            OnScanComplete?.Invoke(_store.Count);
        });
    }

    private void ScanRecursive(string currentDir)
    {
        try 
        {
            var dirName = Path.GetFileName(currentDir);
            if (SkipSystemFolders)
            {
                if (dirName.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("Program Files", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase) ||
                    dirName.StartsWith("$"))
                {
                    return;
                }
            }

            // Process Files
            var files = Directory.GetFiles(currentDir, "*.*", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                 var ext = Path.GetExtension(file);
                 if (_allowedExtensions.Contains(ext))
                 {
                     ProcessFile(file);
                     OnFileFound?.Invoke(file);
                 }
            }
            
            // Recurse
            foreach (var dir in Directory.GetDirectories(currentDir))
            {
                ScanRecursive(dir);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (Exception) { }
    }

    private void ProcessFile(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        var existing = _store.GetByPath(filePath);

        if (existing == null)
        {
            // New file
            var newFile = new MediaFile
            {
                FilePath = filePath,
                FileName = fileInfo.Name,
                LastModified = fileInfo.LastWriteTime,
                FileSizeBytes = fileInfo.Length
            };
            _store.Upsert(newFile);
        }
        else
        {
            // Update existing if changed
            if (existing.LastModified != fileInfo.LastWriteTime || existing.FileSizeBytes != fileInfo.Length)
            {
                existing.LastModified = fileInfo.LastWriteTime;
                existing.FileSizeBytes = fileInfo.Length;
                existing.IsLowContent = false; // Reset analysis if file changed
                _store.Upsert(existing);
            }
        }
    }
}
