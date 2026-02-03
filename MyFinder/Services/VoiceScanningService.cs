using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MyFinder.Models;
using NAudio.Wave;

namespace MyFinder.Services;

public class VoiceScanningService
{
    private readonly AudioTranscriber _transcriber;
    private readonly VoiceFingerprintService _fingerprintService;

    public VoiceScanningService(AudioTranscriber transcriber, VoiceFingerprintService fingerprintService)
    {
        _transcriber = transcriber;
        _fingerprintService = fingerprintService;
    }

    public async Task<List<SpeakerCluster>> ScanVideoForVoicesAsync(MediaFile file)
    {
        // 1. Extract Full Audio to Temp WAV (16kHz Mono)
        string tempWav = Path.ChangeExtension(Path.GetTempFileName(), ".wav");
        var clusters = new List<SpeakerCluster>();

        try
        {
            // Transcribe and get segments (Transcriber handles extraction if needed, but we need the file for later too)
            // We'll let Transcriber extract it to tempWav for us by passing it?
            // AudioTranscriber implementation: if wavPath provided, it uses it or creates it?
            // Let's modify AudioTranscriber usage:
            // We need the WAV file to persist for embedding extraction.
            // Transcriber.TranscribeAsync(file, tempWav) would be ideal.
            
            // Re-reading AudioTranscriber code:
            // It accepts (MediaFile file, string wavPath = null).
            // If wavPath is passed, it uses it if exists, or extracts to it.
            // Perfect.
            
            var segments = await _transcriber.TranscribeAsync(file, tempWav);
            
            if (segments.Count == 0) return clusters;

            // 2. Process Segments for Embeddings
            var embeddings = new List<(AudioTranscriber.TranscriptionSegment Segment, float[] Vector)>();
            
            using (var reader = new AudioFileReader(tempWav)) // 16kHz WAV
            {
                // Ensure format is correct
                if (reader.WaveFormat.SampleRate != 16000)
                {
                    // Should rely on Transcriber's FFmpeg conversion forcing 16kHz
                    System.Diagnostics.Debug.WriteLine($"Warning: Wav is {reader.WaveFormat.SampleRate}, expected 16000");
                }

                foreach (var seg in segments)
                {
                    // Skip very short segments (< 0.5s)
                    if ((seg.End - seg.Start).TotalSeconds < 0.5) continue;

                    // Read Audio Chunk
                    reader.CurrentTime = seg.Start;
                    int bytesToRead = (int)((seg.End - seg.Start).TotalSeconds * 16000 * 2) * 2; // *2 for short, *? (AudioFileReader is IEEE float? No, it's 16bit PCM usually if ffmpeg did pcm_s16le, but AudioFileReader converts to float output)
                    // Wait, AudioFileReader emits float samples (IEEE).
                    // So bytes per second = 16000 * 4 (float) * 1 (mono).
                    
                    int samplesToRead = (int)((seg.End - seg.Start).TotalSeconds * 16000);
                    float[] buffer = new float[samplesToRead];
                    int read = reader.Read(buffer, 0, samplesToRead); // Returns count of samples read
                    
                    if (read < 1600) continue; // Skip if read failed or too short

                    var vector = _fingerprintService.GetEmbeddingFromSamples(buffer.Take(read).ToArray());
                    
                    if (vector.Length > 0)
                    {
                        embeddings.Add((seg, vector));
                    }
                }
            }

            // 3. Cluster Embeddings
            clusters = ClusterEmbeddings(embeddings);
        }
        catch (Exception ex) 
        {
            System.Diagnostics.Debug.WriteLine($"Voice Scan Error: {ex.Message}");
        }
        finally
        {
            if (File.Exists(tempWav)) File.Delete(tempWav);
        }

        return clusters;
    }

    private List<SpeakerCluster> ClusterEmbeddings(List<(AudioTranscriber.TranscriptionSegment Segment, float[] Vector)> items)
    {
        var clusters = new List<SpeakerCluster>();
        float threshold = 0.80f; // Similarity threshold (tuning required)

        foreach (var item in items)
        {
            SpeakerCluster? bestMatch = null;
            float bestScore = -1;

            foreach (var cluster in clusters)
            {
                float score = CosineSimilarity(item.Vector, cluster.Centroid);
                if (score > threshold && score > bestScore)
                {
                    bestScore = score;
                    bestMatch = cluster;
                }
            }

            if (bestMatch != null)
            {
                bestMatch.Segments.Add(item.Segment);
                // Update Centroid (Moving Average for simplicity)
                // Or keep list of vectors and re-average.
                // Simple: Average
                for(int i=0; i<item.Vector.Length; i++) 
                    bestMatch.Centroid[i] = (bestMatch.Centroid[i] * (bestMatch.Segments.Count - 1) + item.Vector[i]) / bestMatch.Segments.Count;
            }
            else
            {
                clusters.Add(new SpeakerCluster 
                { 
                    Id = $"Speaker {clusters.Count + 1}",
                    Centroid = item.Vector, // Clone?
                    Segments = new List<AudioTranscriber.TranscriptionSegment> { item.Segment }
                });
            }
        }

        return clusters;
    }

    private float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        
        float dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        
        if (magA == 0 || magB == 0) return 0;
        
        return dot / (float)(Math.Sqrt(magA) * Math.Sqrt(magB));
    }
}

public class SpeakerCluster
{
    public string Id { get; set; } = "Unknown";
    public float[] Centroid { get; set; } = Array.Empty<float>();
    public List<AudioTranscriber.TranscriptionSegment> Segments { get; set; } = new();
    
    public TimeSpan TotalDuration => TimeSpan.FromSeconds(Segments.Sum(s => (s.End - s.Start).TotalSeconds));
}
