using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using MyFinder.Models;
using NAudio.Wave;
using NWaves.FeatureExtractors;
using NWaves.FeatureExtractors.Options;

namespace MyFinder.Services;

public class VoiceFingerprintService
{
    private readonly string _modelPath;
    private InferenceSession? _session;

    public VoiceFingerprintService(string modelPath)
    {
        _modelPath = modelPath;
    }

    public async Task InitializeAsync()
    {
        await Task.Run(() => 
        {
            if (File.Exists(_modelPath) && _session == null)
            {
                _session = new InferenceSession(_modelPath);
            }
        });
    }

    public async Task<float[]> GetVoiceEmbeddingAsync(string audioPath)
    {
        if (_session == null || !File.Exists(audioPath)) return new float[0];

        return await Task.Run(() =>
        {
            // 1. Read Audio (Force 16kHz Mono)
            // Use MediaFoundationReader directly to stream, avoiding full file scan
            float[] samples;
            try 
            {
                using var reader = new MediaFoundationReader(audioPath);
                var outFormat = new WaveFormat(16000, 1);
                using var resampler = new MediaFoundationResampler(reader, outFormat);
                
                // Read 3 seconds max (16000 * 3 * 2 bytes = 96000 bytes)
                int bytesToRead = outFormat.AverageBytesPerSecond * 3;
                var buffer = new byte[bytesToRead];
                int read = resampler.Read(buffer, 0, buffer.Length);
                
                // Convert byte to float
                int sampleCount = read / 2; // 16bit = 2 bytes
                samples = new float[sampleCount];
                for (int i = 0; i < sampleCount; i++)
                {
                     short val = BitConverter.ToInt16(buffer, i * 2);
                     samples[i] = val / 32768f;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Audio read failed: {ex.Message}");
                return new float[0];
            }

            return GetEmbeddingFromSamples(samples);
        });
    }

    public float[] GetEmbeddingFromSamples(float[] samples)
    {
        if (_session == null) return new float[0];
        if (samples.Length < 1600) return new float[0]; // Too short (< 0.1s)

        try 
        {
            // 2. Extract MFCC (Mel-Frequency Cepstral Coefficients)
            // VoxCeleb models usually take Spectrograms or MFCCs.
            // Let's assume a simplified ResNet that takes raw MFCC blocks (80 dim).
            
            var options = new MfccOptions
            {
                SamplingRate = 16000,
                FeatureCount = 80, 
                FrameDuration = 0.025, // 25ms
                HopDuration = 0.010,   // 10ms
            };
            
            var extractor = new MfccExtractor(options);
            var mfccs = extractor.ComputeFrom(samples);

            // 3. Create Tensor [1, 1, Time, 80] or [1, 80, Time]
            
            int timeSteps = 200; // Fixed size input often required
            if (mfccs.Count < timeSteps) 
            {
                 // Pad or resize? For now, return empty if too short for model
                 // return new float[0]; 
                 // Actually, let's just take whatever we have or 200 min
                 timeSteps = Math.Min(mfccs.Count, 200); 
            }
            // For robustness, stick to 200. If less, padding is better.
            if (mfccs.Count < 200) return new float[0]; // Strict 2s min
            timeSteps = 200;

            // Build Tensor
            var tensor = new DenseTensor<float>(new[] { 1, 1, 80, timeSteps }); 
            
            for (int t = 0; t < timeSteps; t++)
            {
                for (int f = 0; f < 80; f++)
                {
                     tensor[0, 0, f, t] = (float)mfccs[t][f];
                }
            }

            // 4. Run Inference
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input", tensor) 
            };

            lock (_session) // OnnxRuntime is thread-safe but check session lock policy
            {
                using var results = _session.Run(inputs);
                return results.First().AsEnumerable<float>().ToArray();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Inference Failed: {ex.Message}");
            return new float[0];
        }
    }
}
