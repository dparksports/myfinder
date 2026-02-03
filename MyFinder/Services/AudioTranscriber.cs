using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using MyFinder.Models;
using Whisper.net;
using Whisper.net.Ggml;

namespace MyFinder.Services;

public class AudioTranscriber
{
    private readonly string _modelPath;
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;

    public AudioTranscriber(string modelPath)
    {
        _modelPath = modelPath;
    }

    public async Task InitializeAsync()
    {
        if (_factory != null) return;
        
        await Task.Run(async () => 
        {
            if (!File.Exists(_modelPath))
            {
                // Auto-download model if missing (Base model ~140MB)
                // In a real app, show progress. Here we block or skip.
                if (!File.Exists(_modelPath))
                {
                    // Update for Whisper.net 1.5+: Downloader is instance-based
                    using var client = new System.Net.Http.HttpClient();
                    var downloader = new WhisperGgmlDownloader(client);
                    using var modelStream = await downloader.GetGgmlModelAsync(GgmlType.Base);
                    using var fileWriter = File.OpenWrite(_modelPath);
                    await modelStream.CopyToAsync(fileWriter);
                }
            }
            
            _factory = WhisperFactory.FromPath(_modelPath);
            _processor = _factory.CreateBuilder()
                .WithLanguage("auto")
                .Build();
        });
    }

    public async Task<string> TranscribeAsync(MediaFile file)
    {
        if (_factory == null) return "Model not loaded";

        // 1. Extract Audio to 16kHz WAV (Resampled) using FFmpeg
        string tempWav = Path.ChangeExtension(Path.GetTempFileName(), ".wav");
        
        try 
        {
            if (await ExtractAudioAsync(file.FilePath, tempWav))
            {
                using var fileStream = File.OpenRead(tempWav);
                var segments = new List<string>();
                
                await foreach (var segment in _processor.ProcessAsync(fileStream))
                {
                   segments.Add($"[{segment.Start} - {segment.End}] {segment.Text}");
                }
                
                return string.Join("\n", segments);
            }
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
        finally
        {
            if (File.Exists(tempWav)) File.Delete(tempWav);
        }
        
        return "Extraction Failed";
    }

    private async Task<bool> ExtractAudioAsync(string inputPath, string outputPath)
    {
        // Requires FFmpeg on PATH
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-i \"{inputPath}\" -ar 16000 -ac 1 -c:a pcm_s16le \"{outputPath}\" -y",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try 
        {
            using var proc = Process.Start(startInfo);
            if (proc == null) return false;
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch 
        {
            return false; // FFmpeg likely missing
        }
    }
}
