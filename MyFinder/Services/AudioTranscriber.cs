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
            try
            {
                if (!File.Exists(_modelPath) || new FileInfo(_modelPath).Length == 0)
                {
                    // Update for Whisper.net 1.5+: Downloader is instance-based
                    // Update for Whisper.net 1.5+: Downloader is static
                    using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(GgmlType.Base);
                    using var fileWriter = File.OpenWrite(_modelPath);
                    await modelStream.CopyToAsync(fileWriter);
                }
                
                _factory = WhisperFactory.FromPath(_modelPath);
                _processor = _factory.CreateBuilder()
                    .WithLanguage("auto")
                    .Build();
            }
            catch (Exception ex)
            {
                 // Log or rethrow to be caught by UI
                 // Clean up partial file
                 if (File.Exists(_modelPath) && new FileInfo(_modelPath).Length == 0) File.Delete(_modelPath);
                 throw new Exception($"Failed to initialize Whisper: {ex.Message}", ex);
            }
        });
    }

    public async Task<List<TranscriptionSegment>> TranscribeAsync(MediaFile file, string wavPath = null)
    {
        var segments = new List<TranscriptionSegment>();
        if (_factory == null) return segments;

        // 1. Extract Audio if not provided
        string tempWav = wavPath ?? Path.ChangeExtension(Path.GetTempFileName(), ".wav");
        bool deleteTemp = wavPath == null;

        try 
        {
            if (wavPath != null || await ExtractAudioAsync(file.FilePath, tempWav))
            {
                using var fileStream = File.OpenRead(tempWav);
                
                await foreach (var segment in _processor.ProcessAsync(fileStream))
                {
                   segments.Add(new TranscriptionSegment 
                   {
                       Start = segment.Start,
                       End = segment.End,
                       Text = segment.Text
                   });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            if (deleteTemp && File.Exists(tempWav)) File.Delete(tempWav);
        }
        
        return segments;
    }

    public class TranscriptionSegment
    {
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
        public string Text { get; set; } = "";
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
