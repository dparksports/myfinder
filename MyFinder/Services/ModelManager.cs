using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace MyFinder.Services;

public static class ModelManager
{
    private static readonly Dictionary<string, string> Models = new()
    {
        // YOLOv8 Nano (Video Analysis)
        { "yolov8n.onnx", "https://github.com/ultralytics/assets/releases/download/v8.2.0/yolov8n.onnx" },
        
        // Haar Cascade (Face Detection - Fast)
        { "haarcascade_frontalface_default.xml", "https://raw.githubusercontent.com/opencv/opencv/master/data/haarcascades/haarcascade_frontalface_default.xml" },
        
        // ArcFace (Face Recognition) - Using MobileFaceNet for speed/size balance
        // Source: k2-fsa/sherpa-onnx releases (Reliable ONNX source)
        { "arcface.onnx", "https://github.com/k2-fsa/sherpa-onnx/releases/download/recognition-models/arcface_mobilefacenet.onnx" },
        
        // VoxCeleb (Speaker ID)
        // Source: Wespeaker (HuggingFace)
        { "voxceleb.onnx", "https://huggingface.co/Wespeaker/wespeaker-voxceleb-resnet34/resolve/main/voxceleb_resnet34.onnx" }
    };

    public static string GetCommonDataPath()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MyFinder");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return path;
    }

    public static async Task EnsureAllModelsAsync(IProgress<string> progress)
    {
        string destDir = GetCommonDataPath();
        using var client = new HttpClient();

        foreach (var model in Models)
        {
            string fileName = model.Key;
            string url = model.Value;
            string destPath = Path.Combine(destDir, fileName);

            if (!File.Exists(destPath))
            {
                progress.Report($"Downloading {fileName}...");
                try 
                {
                    // Simple download with buffering
                    // For large files (VoxCeleb ~100MB), streams are better, but this is simple for prototype
                    var data = await client.GetByteArrayAsync(url);
                    await File.WriteAllBytesAsync(destPath, data);
                    progress.Report($"{fileName} installed.");
                }
                catch (Exception ex)
                {
                    progress.Report($"Failed to download {fileName}: {ex.Message}");
                }
            }
            else
            {
                // Verify non-zero size
                if (new FileInfo(destPath).Length == 0)
                {
                     File.Delete(destPath); // Corrupt, retry next time
                }
            }
        }
        
        progress.Report("All AI Models Ready.");
    }
}
