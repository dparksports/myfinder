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
    private readonly GgmlType _modelType;
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;

    public AudioTranscriber(string modelPath, GgmlType modelType = GgmlType.Base)
    {
        _modelPath = modelPath;
        _modelType = modelType;
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
                    using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(_modelType);
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
                 if (File.Exists(_modelPath) && new FileInfo(_modelPath).Length == 0) File.Delete(_modelPath);
                 throw new Exception($"Failed to initialize Whisper: {ex.Message}", ex);
            }
        });
    }

    public async Task<List<TranscriptionSegment>> TranscribeAsync(MediaFile file, bool force = false, string? wavPath = null)
    {
        // 0. Migration & Persistence Check
        if (!string.IsNullOrEmpty(file.TranscriptText) && file.Transcripts.Count == 0)
        {
             // Migrate legacy
             file.Transcripts.Add(new TranscriptEntry 
             {
                 Model = "Legacy",
                 Created = DateTime.MinValue, // Mark as old
                 JsonContent = file.TranscriptText
             });
        }

        if (!force && file.Transcripts.Count > 0)
        {
            try 
            {
                var latest = file.Transcripts.OrderByDescending(t => t.Created).First();
                var cached = System.Text.Json.JsonSerializer.Deserialize<List<TranscriptionSegment>>(latest.JsonContent);
                if (cached != null) return cached;
            }
            catch { /* Ignore parse error */ }
        }

        var segments = new List<TranscriptionSegment>();
        if (_factory == null || _processor == null) return segments;

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

                // Save to file (Multi-transcript)
                var json = System.Text.Json.JsonSerializer.Serialize(segments);
                
                file.Transcripts.Add(new TranscriptEntry 
                {
                    Model = _modelType.ToString(),
                    Created = DateTime.Now,
                    JsonContent = json
                });
                
                // Sync legacy field
                file.TranscriptText = json;
                
                // Update snippet
                if (segments.Any())
                    file.TranscriptSnippet = segments.First().Text;
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
